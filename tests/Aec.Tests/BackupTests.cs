using System.Diagnostics;

namespace Aec.Tests;

[Collection(ProcessStateCollection.Name)]
public sealed class BackupTests
{
    private const string SourceRelativePath = "environment/providers/codex/AGENTS.md";
    private const string ConfigSourceRelativePath = "environment/providers/codex/config.toml";

    [Fact]
    public void CapturesManagedEnvironmentInOneCommit()
    {
        const string runtimeConfig =
            "model = \"gpt-test\"\n" +
            "personality = 'friendly'\n" +
            "[projects.\"/tmp/example\"]\ntrust_level = \"trusted\"\n";
        using var layout = new BackupLayout("runtime\n", runtimeConfig);
        var runtimeConfigBefore = File.ReadAllBytes(layout.RuntimeConfig);

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.StartsWith("committed ", result.Output, StringComparison.Ordinal);
        Assert.Empty(result.Error);
        Assert.Equal("runtime\n", File.ReadAllText(layout.Source));
        Assert.Equal("personality = \"friendly\"\n", File.ReadAllText(layout.ConfigSource));
        Assert.Equal(runtimeConfigBefore, File.ReadAllBytes(layout.RuntimeConfig));
        Assert.Equal(
            "Backup Codex environment",
            Git(layout, "log", "-1", "--format=%s").Output.Trim());
        Assert.Equal(
            $"{SourceRelativePath}{Environment.NewLine}{ConfigSourceRelativePath}",
            Git(
                layout,
                "ls-tree",
                "--name-only",
                "-r",
                "HEAD").Output.Trim());
        Assert.Equal(string.Empty, Git(layout, "status", "--porcelain").Output);
    }

    [Fact]
    public void MissingRuntimePersonalityWarnsAndStopsWithoutRepositoryMutation()
    {
        using var layout = new BackupLayout("runtime\n", "model = \"gpt-test\"\n");
        var sourceBefore = File.ReadAllBytes(layout.Source);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("warning:", result.Error, StringComparison.Ordinal);
        Assert.Contains("personality", result.Error, StringComparison.Ordinal);
        Assert.Contains("stopped", result.Error, StringComparison.Ordinal);
        Assert.Equal(sourceBefore, File.ReadAllBytes(layout.Source));
        Assert.False(File.Exists(layout.ConfigSource));
        Assert.NotEqual(0, Git(layout, "rev-parse", "--verify", "HEAD").ExitCode);
        Assert.Equal(string.Empty, Git(layout, "diff", "--cached", "--name-only").Output);
    }

    [Fact]
    public void MissingRuntimeConfigWarnsAndStopsWithoutRepositoryMutation()
    {
        using var layout = new BackupLayout("runtime\n", runtimeConfig: null);
        var sourceBefore = File.ReadAllBytes(layout.Source);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("warning:", result.Error, StringComparison.Ordinal);
        Assert.Contains("personality", result.Error, StringComparison.Ordinal);
        Assert.Contains("stopped", result.Error, StringComparison.Ordinal);
        Assert.Equal(sourceBefore, File.ReadAllBytes(layout.Source));
        Assert.False(File.Exists(layout.ConfigSource));
        Assert.NotEqual(0, Git(layout, "rev-parse", "--verify", "HEAD").ExitCode);
        Assert.Equal(string.Empty, Git(layout, "diff", "--cached", "--name-only").Output);
    }

    [Fact]
    public void ConfigOnlyChangeCreatesACommitForOnlyCanonicalConfig()
    {
        using var layout = new BackupLayout("same\n");
        Assert.Equal(0, Run(layout).ExitCode);
        File.WriteAllText(
            layout.RuntimeConfig,
            "model = \"gpt-test\"\npersonality = \"friendly\"\n");

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.StartsWith("committed ", result.Output, StringComparison.Ordinal);
        Assert.Empty(result.Error);
        Assert.Equal("same\n", File.ReadAllText(layout.Source));
        Assert.Equal("personality = \"friendly\"\n", File.ReadAllText(layout.ConfigSource));
        Assert.Equal(
            ConfigSourceRelativePath,
            Git(layout, "diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD").Output.Trim());
        Assert.Equal(string.Empty, Git(layout, "status", "--porcelain").Output);
    }

    [Fact]
    public void ConfigUpdatePreservesCanonicalFormattingAndRuntimeBytes()
    {
        using var layout = new BackupLayout("same\n");
        Assert.Equal(0, Run(layout).ExitCode);
        byte[] canonicalBefore =
        [
            0xEF, 0xBB, 0xBF,
            .. "# keep this comment\r\npersonality\t = 'none' # keep this too\r\n"u8.ToArray()
        ];
        var runtimeBefore =
            "model = \"gpt-test\"\r\npersonality = 'pragmatic'\r\n"u8.ToArray();
        File.WriteAllBytes(layout.ConfigSource, canonicalBefore);
        File.WriteAllBytes(layout.RuntimeConfig, runtimeBefore);
        byte[] expectedCanonical =
        [
            0xEF, 0xBB, 0xBF,
            .. "# keep this comment\r\npersonality\t = \"pragmatic\" # keep this too\r\n"u8.ToArray()
        ];

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Equal(expectedCanonical, File.ReadAllBytes(layout.ConfigSource));
        Assert.Equal(runtimeBefore, File.ReadAllBytes(layout.RuntimeConfig));
        Assert.Equal(
            ConfigSourceRelativePath,
            Git(layout, "diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD").Output.Trim());
    }

    [Fact]
    public void InvalidRuntimeConfigStopsBeforeCapturingAgents()
    {
        using var layout = new BackupLayout("first\n");
        Assert.Equal(0, Run(layout).ExitCode);
        var headBefore = Git(layout, "rev-parse", "HEAD").Output.Trim();
        File.WriteAllText(layout.Runtime, "second\n");
        File.WriteAllText(
            layout.RuntimeConfig,
            "personality = \"friendly\"\npersonality = \"pragmatic\"\n");

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("more than once", result.Error, StringComparison.Ordinal);
        Assert.Equal("first\n", File.ReadAllText(layout.Source));
        Assert.Equal(headBefore, Git(layout, "rev-parse", "HEAD").Output.Trim());
        Assert.Equal(string.Empty, Git(layout, "diff", "--cached", "--name-only").Output);
    }

    [Fact]
    public void InvalidCanonicalConfigStopsBeforeCapturingAgents()
    {
        using var layout = new BackupLayout("first\n");
        Assert.Equal(0, Run(layout).ExitCode);
        var headBefore = Git(layout, "rev-parse", "HEAD").Output.Trim();
        File.WriteAllText(layout.Runtime, "second\n");
        File.WriteAllText(
            layout.ConfigSource,
            "personality = \"none\"\nmodel = \"must-not-be-managed\"\n");

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("unmanaged setting", result.Error, StringComparison.Ordinal);
        Assert.Equal("first\n", File.ReadAllText(layout.Source));
        Assert.Equal(headBefore, Git(layout, "rev-parse", "HEAD").Output.Trim());
        Assert.Equal(string.Empty, Git(layout, "diff", "--cached", "--name-only").Output);
    }

    [Fact]
    public void GitFiltersThatChangeCanonicalConfigBytesStopBeforeCommit()
    {
        using var layout = new BackupLayout("same\n");
        Assert.Equal(0, Run(layout).ExitCode);
        File.WriteAllText(
            Path.Combine(layout.Repository, ".gitattributes"),
            $"{ConfigSourceRelativePath} text eol=lf\n");
        Assert.Equal(0, Git(layout, "add", "--", ".gitattributes").ExitCode);
        Assert.Equal(0, Git(layout, "commit", "--quiet", "--message", "Test attributes").ExitCode);
        var headBefore = Git(layout, "rev-parse", "HEAD").Output.Trim();
        File.WriteAllBytes(layout.ConfigSource, "personality = \"friendly\"\r\n"u8.ToArray());
        File.WriteAllText(layout.RuntimeConfig, "personality = \"friendly\"\n");

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("filters changed", result.Error, StringComparison.Ordinal);
        Assert.Contains("canonical config", result.Error, StringComparison.Ordinal);
        Assert.Equal(headBefore, Git(layout, "rev-parse", "HEAD").Output.Trim());
        Assert.Equal(
            ConfigSourceRelativePath,
            Git(layout, "diff", "--cached", "--name-only").Output.Trim());
    }

    [Fact]
    public void FirstBackupCommitsTheRuntimeManagedFiles()
    {
        using var layout = new BackupLayout("runtime\n");

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        var head = Git(layout, "rev-parse", "--verify", "HEAD").Output.Trim();
        Assert.Equal($"committed {head}{Environment.NewLine}", result.Output);
        Assert.Equal("runtime\n", File.ReadAllText(layout.Source));
        Assert.Equal(
            $"{SourceRelativePath}{Environment.NewLine}{ConfigSourceRelativePath}",
            Git(layout, "ls-tree", "-r", "--name-only", "HEAD").Output.Trim());
        Assert.Equal("Backup Codex environment", Git(layout, "log", "-1", "--format=%s").Output.Trim());
        Assert.Equal(string.Empty, Git(layout, "status", "--porcelain").Output);

        var statusOutput = new StringWriter();
        var statusError = new StringWriter();
        var statusExitCode = AecApplication.Run(
            ["status", "--repo", layout.Repository, "--codex-home", layout.CodexHome],
            statusOutput,
            statusError);
        Assert.Equal(0, statusExitCode);
        Assert.Equal(
            $"codex/AGENTS.md   in_sync{Environment.NewLine}" +
            $"codex/config.toml in_sync{Environment.NewLine}",
            statusOutput.ToString());
        Assert.Empty(statusError.ToString());
    }

    [Fact]
    public void ChangedRuntimeCreatesASecondCommit()
    {
        using var layout = new BackupLayout("first\n");
        Assert.Equal(0, Run(layout).ExitCode);
        File.WriteAllText(layout.Runtime, "second\n");

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.StartsWith("committed ", result.Output, StringComparison.Ordinal);
        Assert.Equal("second\n", File.ReadAllText(layout.Source));
        Assert.Equal("2", Git(layout, "rev-list", "--count", "HEAD").Output.Trim());
        Assert.Equal(
            SourceRelativePath,
            Git(layout, "diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD").Output.Trim());
        Assert.Equal(string.Empty, Git(layout, "status", "--porcelain").Output);
    }

    [Fact]
    public void EqualCommittedFilesAreUnchanged()
    {
        using var layout = new BackupLayout("same\n");
        Assert.Equal(0, Run(layout).ExitCode);
        var headBefore = Git(layout, "rev-parse", "HEAD").Output.Trim();

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"unchanged{Environment.NewLine}", result.Output);
        Assert.Empty(result.Error);
        Assert.Equal(headBefore, Git(layout, "rev-parse", "HEAD").Output.Trim());
        Assert.Equal(string.Empty, Git(layout, "status", "--porcelain").Output);
    }

    [Fact]
    public void EqualStagedSourceResumesAnInterruptedBackup()
    {
        using var layout = new BackupLayout("pending\n");
        File.WriteAllText(layout.Source, "pending\n");
        Assert.Equal(0, Git(layout, "add", "--", SourceRelativePath).ExitCode);

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.StartsWith("committed ", result.Output, StringComparison.Ordinal);
        Assert.Empty(result.Error);
        Assert.Equal("pending\n", Git(layout, "show", $"HEAD:{SourceRelativePath}").Output);
        Assert.Equal(string.Empty, Git(layout, "status", "--porcelain").Output);
    }

    [Fact]
    public void FailedCommitLeavesTheCanonicalPathReadyForARetry()
    {
        using var layout = new BackupLayout("pending\n");
        Assert.Equal(0, Git(layout, "config", "--local", "user.name", "").ExitCode);

        var failed = Run(layout);

        Assert.Equal(1, failed.ExitCode);
        Assert.Contains("could not commit", failed.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("pending\n", File.ReadAllText(layout.Source));
        Assert.Equal(
            $"{SourceRelativePath}{Environment.NewLine}{ConfigSourceRelativePath}",
            Git(layout, "diff", "--cached", "--name-only").Output.Trim());
        Assert.NotEqual(0, Git(layout, "rev-parse", "--verify", "HEAD").ExitCode);

        Assert.Equal(0, Git(layout, "config", "--local", "user.name", "AEC Tests").ExitCode);
        var resumed = Run(layout);

        Assert.Equal(0, resumed.ExitCode);
        Assert.StartsWith("committed ", resumed.Output, StringComparison.Ordinal);
        Assert.Equal(string.Empty, Git(layout, "status", "--porcelain").Output);
    }

    [Fact]
    public void ConfiguredHooksCannotChangeTheBackupCommit()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var layout = new BackupLayout("runtime\n");
        var hooks = Path.Combine(layout.Root, "mutating hooks");
        Directory.CreateDirectory(hooks);
        var hook = Path.Combine(hooks, "pre-commit");
        File.WriteAllText(
            hook,
            $"#!/bin/sh\nprintf 'hook\\n' > {SourceRelativePath}\ngit add -- {SourceRelativePath}\n");
        File.SetUnixFileMode(
            hook,
            UnixFileMode.UserRead |
            UnixFileMode.UserWrite |
            UnixFileMode.UserExecute |
            UnixFileMode.GroupRead |
            UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead |
            UnixFileMode.OtherExecute);
        Assert.Equal(0, Git(layout, "config", "--local", "core.hooksPath", hooks).ExitCode);

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.StartsWith("committed ", result.Output, StringComparison.Ordinal);
        Assert.Equal("runtime\n", File.ReadAllText(layout.Source));
        Assert.Equal("runtime\n", Git(layout, "show", $"HEAD:{SourceRelativePath}").Output);
        Assert.Equal("Backup Codex environment", Git(layout, "log", "-1", "--format=%s").Output.Trim());
        Assert.Equal(string.Empty, Git(layout, "status", "--porcelain").Output);
    }

    [Fact]
    public void CommitUsesStagedSnapshotInsteadOfRereadingWorkingFiles()
    {
        using var layout = new BackupLayout("unused\n");
        File.WriteAllText(layout.Source, "staged\n");
        File.WriteAllText(layout.ConfigSource, "personality = \"none\"\n");
        Assert.Equal(
            0,
            Git(
                layout,
                "add",
                "--",
                SourceRelativePath,
                ConfigSourceRelativePath).ExitCode);
        File.WriteAllText(layout.Source, "changed after staging\n");

        var stagedSource = Git(
            layout,
            "rev-parse",
            $":{SourceRelativePath}").Output.Trim();
        var stagedConfig = Git(
            layout,
            "rev-parse",
            $":{ConfigSourceRelativePath}").Output.Trim();
        BackupCommand.CommitStagedIndex(
            layout.Repository,
            "Pinned test commit",
            [
                (SourceRelativePath, stagedSource),
                (ConfigSourceRelativePath, stagedConfig)
            ]);

        Assert.Equal("staged\n", Git(layout, "show", $"HEAD:{SourceRelativePath}").Output);
        Assert.Equal("changed after staging\n", File.ReadAllText(layout.Source));
        Assert.Equal(
            "Pinned test commit",
            Git(layout, "log", "-1", "--format=%s").Output.Trim());
        Assert.Equal(
            $" M {SourceRelativePath}",
            Git(layout, "status", "--porcelain").Output.TrimEnd());
    }

    [Fact]
    public void CommitRejectsAnUnrelatedIndexChangeAfterManagedVerification()
    {
        using var layout = new BackupLayout("unused\n");
        File.WriteAllText(layout.Source, "staged\n");
        File.WriteAllText(layout.ConfigSource, "personality = \"none\"\n");
        Assert.Equal(
            0,
            Git(
                layout,
                "add",
                "--",
                SourceRelativePath,
                ConfigSourceRelativePath).ExitCode);
        var stagedSource = Git(
            layout,
            "rev-parse",
            $":{SourceRelativePath}").Output.Trim();
        var stagedConfig = Git(
            layout,
            "rev-parse",
            $":{ConfigSourceRelativePath}").Output.Trim();
        File.WriteAllText(Path.Combine(layout.Repository, "unrelated.txt"), "unexpected\n");
        Assert.Equal(0, Git(layout, "add", "--", "unrelated.txt").ExitCode);

        var exception = Assert.Throws<InvalidOperationException>(
            () => BackupCommand.CommitStagedIndex(
                layout.Repository,
                "Must not commit",
                [
                    (SourceRelativePath, stagedSource),
                    (ConfigSourceRelativePath, stagedConfig)
                ]));

        Assert.Contains("outside the approved", exception.Message, StringComparison.Ordinal);
        Assert.NotEqual(0, Git(layout, "rev-parse", "--verify", "HEAD").ExitCode);
    }

    [Fact]
    public void RejectsAnUntrackedFileOutsideTheCanonicalSourceBeforeWriting()
    {
        using var layout = new BackupLayout("runtime\n");
        var unrelated = Path.Combine(layout.Repository, "unrelated.txt");
        File.WriteAllText(unrelated, "preserve me");
        var sourceBefore = File.ReadAllBytes(layout.Source);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("changes outside", result.Error, StringComparison.Ordinal);
        Assert.Equal(sourceBefore, File.ReadAllBytes(layout.Source));
        Assert.Equal("preserve me", File.ReadAllText(unrelated));
        Assert.NotEqual(0, Git(layout, "rev-parse", "--verify", "HEAD").ExitCode);
        Assert.Equal(string.Empty, Git(layout, "diff", "--cached", "--name-only").Output);
    }

    [Fact]
    public void RejectsATrackedChangeOutsideTheCanonicalSourceBeforeWriting()
    {
        using var layout = new BackupLayout("first\n");
        Assert.Equal(0, Run(layout).ExitCode);
        var unrelated = Path.Combine(layout.Repository, "unrelated.txt");
        File.WriteAllText(unrelated, "committed\n");
        Assert.Equal(0, Git(layout, "add", "--", "unrelated.txt").ExitCode);
        Assert.Equal(0, Git(layout, "commit", "--quiet", "--message", "Test setup").ExitCode);
        File.WriteAllText(unrelated, "dirty\n");
        File.WriteAllText(layout.Runtime, "second\n");
        var sourceBefore = File.ReadAllBytes(layout.Source);
        var headBefore = Git(layout, "rev-parse", "HEAD").Output.Trim();

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("changes outside", result.Error, StringComparison.Ordinal);
        Assert.Equal(sourceBefore, File.ReadAllBytes(layout.Source));
        Assert.Equal(headBefore, Git(layout, "rev-parse", "HEAD").Output.Trim());
        Assert.Equal(string.Empty, Git(layout, "diff", "--cached", "--name-only").Output);
    }

    [Fact]
    public void RejectsAStagedChangeOutsideTheCanonicalSourceBeforeWriting()
    {
        using var layout = new BackupLayout("first\n");
        Assert.Equal(0, Run(layout).ExitCode);
        File.WriteAllText(Path.Combine(layout.Repository, "staged.txt"), "pending\n");
        Assert.Equal(0, Git(layout, "add", "--", "staged.txt").ExitCode);
        File.WriteAllText(layout.Runtime, "second\n");
        var sourceBefore = File.ReadAllBytes(layout.Source);
        var headBefore = Git(layout, "rev-parse", "HEAD").Output.Trim();

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("changes outside", result.Error, StringComparison.Ordinal);
        Assert.Equal(sourceBefore, File.ReadAllBytes(layout.Source));
        Assert.Equal(headBefore, Git(layout, "rev-parse", "HEAD").Output.Trim());
        Assert.Equal("staged.txt", Git(layout, "diff", "--cached", "--name-only").Output.Trim());
    }

    [Fact]
    public void MissingRuntimeFileIsAnErrorWithoutRepositoryChanges()
    {
        using var layout = new BackupLayout("unused\n");
        File.Delete(layout.Runtime);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("Runtime target does not exist", result.Error, StringComparison.Ordinal);
        Assert.Empty(File.ReadAllBytes(layout.Source));
        Assert.Equal("?? environment/", Git(layout, "status", "--porcelain").Output.Trim());
    }

    [Fact]
    public void RuntimeDirectoryIsRejectedWithoutRepositoryChanges()
    {
        using var layout = new BackupLayout("unused\n");
        File.Delete(layout.Runtime);
        Directory.CreateDirectory(layout.Runtime);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Runtime target must be a regular file", result.Error, StringComparison.Ordinal);
        Assert.Empty(File.ReadAllBytes(layout.Source));
    }

    [Fact]
    public void RuntimeFilesOverOneMiBAreRejectedWithoutRepositoryChanges()
    {
        using var layout = new BackupLayout("unused\n");
        File.WriteAllBytes(layout.Runtime, new byte[(1024 * 1024) + 1]);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("exceeds 1 MiB", result.Error, StringComparison.Ordinal);
        Assert.Empty(File.ReadAllBytes(layout.Source));
    }

    [Fact]
    public void SymbolicLinkRuntimeIsRejectedWithoutRepositoryChanges()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var layout = new BackupLayout("unused\n");
        var referent = Path.Combine(layout.Root, "runtime referent.md");
        File.WriteAllText(referent, "external\n");
        File.Delete(layout.Runtime);
        File.CreateSymbolicLink(layout.Runtime, referent);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Runtime target must not be a symbolic link", result.Error, StringComparison.Ordinal);
        Assert.Empty(File.ReadAllBytes(layout.Source));
        Assert.Equal("external\n", File.ReadAllText(referent));
    }

    [Fact]
    public void NestedDirectoryIsRejectedAsTheRepositoryArgument()
    {
        using var layout = new BackupLayout("runtime\n");
        var nested = Path.Combine(layout.Repository, "nested");
        var nestedSource = Path.Combine(nested, SourceRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(nestedSource)!);
        File.WriteAllText(nestedSource, "nested\n");
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = AecApplication.Run(
            ["backup", "--repo", nested, "--codex-home", layout.CodexHome],
            output,
            error);

        Assert.Equal(1, exitCode);
        Assert.Empty(output.ToString());
        Assert.Contains("repository root", error.ToString(), StringComparison.Ordinal);
        Assert.Equal("nested\n", File.ReadAllText(nestedSource));
    }

    [Fact]
    public void DetachedHeadIsRejectedBeforeWriting()
    {
        using var layout = new BackupLayout("first\n");
        Assert.Equal(0, Run(layout).ExitCode);
        Assert.Equal(0, Git(layout, "checkout", "--detach", "--quiet").ExitCode);
        File.WriteAllText(layout.Runtime, "second\n");
        var sourceBefore = File.ReadAllBytes(layout.Source);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("detached HEAD", result.Error, StringComparison.Ordinal);
        Assert.Equal(sourceBefore, File.ReadAllBytes(layout.Source));
    }

    [Fact]
    public void SymbolicHeadOutsideLocalBranchesIsRejectedBeforeWriting()
    {
        using var layout = new BackupLayout("runtime\n");
        Assert.Equal(0, Git(layout, "symbolic-ref", "HEAD", "refs/remotes/origin/odd").ExitCode);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("local Git branch", result.Error, StringComparison.Ordinal);
        Assert.Empty(File.ReadAllBytes(layout.Source));
        Assert.NotEqual(0, Git(layout, "rev-parse", "--verify", "HEAD").ExitCode);
    }

    [Fact]
    public void OrdinaryRefNamedLikeAnOperationDoesNotBlockBackup()
    {
        using var layout = new BackupLayout("first\n");
        Assert.Equal(0, Run(layout).ExitCode);
        Assert.Equal(0, Git(layout, "branch", "MERGE_HEAD").ExitCode);
        File.WriteAllText(layout.Runtime, "second\n");

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.StartsWith("committed ", result.Output, StringComparison.Ordinal);
        Assert.Equal("second\n", Git(layout, "show", $"HEAD:{SourceRelativePath}").Output);
    }

    [Fact]
    public void GitFiltersThatChangeBytesStopBeforeCommit()
    {
        using var layout = new BackupLayout("first\n");
        Assert.Equal(0, Run(layout).ExitCode);
        File.WriteAllText(
            Path.Combine(layout.Repository, ".gitattributes"),
            $"{SourceRelativePath} text eol=lf\n");
        Assert.Equal(0, Git(layout, "add", "--", ".gitattributes").ExitCode);
        Assert.Equal(0, Git(layout, "commit", "--quiet", "--message", "Test attributes").ExitCode);
        var headBefore = Git(layout, "rev-parse", "HEAD").Output.Trim();
        File.WriteAllBytes(layout.Runtime, "second\r\n"u8.ToArray());

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("filters changed", result.Error, StringComparison.Ordinal);
        Assert.Equal(headBefore, Git(layout, "rev-parse", "HEAD").Output.Trim());
        Assert.Equal("second\r\n"u8.ToArray(), File.ReadAllBytes(layout.Source));
        Assert.Equal(SourceRelativePath, Git(layout, "diff", "--cached", "--name-only").Output.Trim());
    }

    [Fact]
    public void ExplicitCodexHomeOverridesTheEnvironment()
    {
        using var layout = new BackupLayout("explicit\n");
        var previous = Environment.GetEnvironmentVariable("CODEX_HOME");

        try
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", Path.Combine(layout.Root, "missing home"));

            var result = Run(layout);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal("explicit\n", File.ReadAllText(layout.Source));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", previous);
        }
    }

    [Fact]
    public void UsesCodexHomeFromTheEnvironmentWhenTheFlagIsAbsent()
    {
        using var layout = new BackupLayout("environment\n");
        var previous = Environment.GetEnvironmentVariable("CODEX_HOME");

        try
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", layout.CodexHome);
            var output = new StringWriter();
            var error = new StringWriter();

            var exitCode = AecApplication.Run(
                ["backup", "--repo", layout.Repository],
                output,
                error);

            Assert.Equal(0, exitCode);
            Assert.StartsWith("committed ", output.ToString(), StringComparison.Ordinal);
            Assert.Empty(error.ToString());
            Assert.Equal("environment\n", File.ReadAllText(layout.Source));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", previous);
        }
    }

    private static CommandResult Run(BackupLayout layout)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = AecApplication.Run(
            ["backup", "--repo", layout.Repository, "--codex-home", layout.CodexHome],
            output,
            error);
        return new CommandResult(exitCode, output.ToString(), error.ToString());
    }

    private static GitResult Git(BackupLayout layout, params string[] arguments)
    {
        return RunGit(layout.Repository, arguments);
    }

    private static GitResult RunGit(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var variable in new[]
                 {
                     "GIT_DIR",
                     "GIT_WORK_TREE",
                     "GIT_COMMON_DIR",
                     "GIT_OBJECT_DIRECTORY",
                     "GIT_INDEX_FILE",
                     "GIT_ALTERNATE_OBJECT_DIRECTORIES",
                     "GIT_TEMPLATE_DIR",
                     "GIT_CONFIG_PARAMETERS",
                     "GIT_CONFIG_COUNT"
                 })
        {
            startInfo.Environment.Remove(variable);
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return new GitResult(
            process.ExitCode,
            output.GetAwaiter().GetResult(),
            error.GetAwaiter().GetResult());
    }

    private sealed record CommandResult(int ExitCode, string Output, string Error);

    private sealed record GitResult(int ExitCode, string Output, string Error);

    private sealed class BackupLayout : IDisposable
    {
        public BackupLayout(
            string runtimeContent,
            string? runtimeConfig = "personality = \"none\"\n")
        {
            Root = Path.Combine(RealTemporaryDirectory(), "aec-backup-tests", Guid.NewGuid().ToString("N"));
            Repository = Path.Combine(Root, "data repository");
            CodexHome = Path.Combine(Root, "codex home");
            Source = Path.Combine(Repository, SourceRelativePath);
            ConfigSource = Path.Combine(Repository, ConfigSourceRelativePath);
            Runtime = Path.Combine(CodexHome, "AGENTS.md");
            RuntimeConfig = Path.Combine(CodexHome, "config.toml");

            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(CodexHome);
            File.WriteAllText(Runtime, runtimeContent);
            if (runtimeConfig is not null)
            {
                File.WriteAllText(RuntimeConfig, runtimeConfig);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Source)!);
            var init = RunGit(
                Repository,
                "init",
                "--quiet",
                "--template=",
                "--initial-branch=main");
            if (init.ExitCode != 0)
            {
                throw new InvalidOperationException(init.Error);
            }
            File.WriteAllBytes(Source, []);

            ConfigureGit("user.name", "AEC Tests");
            ConfigureGit("user.email", "aec-tests@example.invalid");
            ConfigureGit("commit.gpgSign", "false");
            var hooks = Path.Combine(Root, "empty hooks");
            Directory.CreateDirectory(hooks);
            ConfigureGit("core.hooksPath", hooks);
        }

        public string Root { get; }

        public string Repository { get; }

        public string CodexHome { get; }

        public string Source { get; }

        public string ConfigSource { get; }

        public string Runtime { get; }

        public string RuntimeConfig { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }

        private void ConfigureGit(string key, string value)
        {
            var result = RunGit(Repository, "config", "--local", key, value);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(result.Error);
            }
        }

        private static string RealTemporaryDirectory()
        {
            return OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath();
        }
    }
}
