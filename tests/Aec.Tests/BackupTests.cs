namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
// The 1.x capture engine remains part of provider initialization. Its direct
// tests stay here; public ordinary backup has separate portable-command tests.
public sealed class LegacyBackupCommandTests
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

        var result = Run(layout, CodexPersonality.Friendly);

        Assert.Equal(0, result.ExitCode);
        var head = Git(layout, "rev-parse", "--verify", "HEAD").Output.Trim();
        Assert.Equal($"committed {head}{Environment.NewLine}", result.Output);
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
    public void ConfigUpdatePreservesCanonicalFormattingAndRuntimeBytes()
    {
        using var layout = new BackupLayout("same\n");
        layout.SeedBaseline("same\n");
        var baseline = Git(layout, "rev-parse", "HEAD").Output.Trim();
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

        var result = Run(layout, CodexPersonality.Pragmatic);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"committed {Git(layout, "rev-parse", "HEAD").Output.Trim()}{Environment.NewLine}", result.Output);
        Assert.Empty(result.Error);
        Assert.Equal("same\n", File.ReadAllText(layout.Source));
        Assert.Equal(expectedCanonical, File.ReadAllBytes(layout.ConfigSource));
        Assert.Equal(runtimeBefore, File.ReadAllBytes(layout.RuntimeConfig));
        Assert.Equal(
            ConfigSourceRelativePath,
            Git(layout, "diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD").Output.Trim());
        Assert.Equal(baseline, Git(layout, "rev-parse", "HEAD^").Output.Trim());
        Assert.Empty(Git(layout, "status", "--porcelain").Output);
    }

    [Fact]
    public void InvalidRuntimeConfigStopsBeforeCapturingAgents()
    {
        using var layout = new BackupLayout("first\n");
        layout.SeedBaseline("first\n");
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
        layout.SeedBaseline("first\n");
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
        File.WriteAllText(
            Path.Combine(layout.Repository, ".gitattributes"),
            $"{ConfigSourceRelativePath} text eol=lf\n");
        layout.SeedBaseline("same\n", ".gitattributes");
        var headBefore = Git(layout, "rev-parse", "HEAD").Output.Trim();
        File.WriteAllBytes(layout.ConfigSource, "personality = \"friendly\"\r\n"u8.ToArray());
        File.WriteAllText(layout.RuntimeConfig, "personality = \"friendly\"\n");

        var result = Run(layout, CodexPersonality.Friendly);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("filters changed", result.Error, StringComparison.Ordinal);
        Assert.Contains("canonical config", result.Error, StringComparison.Ordinal);
        Assert.Equal(headBefore, Git(layout, "rev-parse", "HEAD").Output.Trim());
        Assert.Equal(
            ConfigSourceRelativePath,
            Git(layout, "diff", "--cached", "--name-only").Output.Trim());
    }

    [Fact]
    public void ChangedRuntimeCreatesASecondCommit()
    {
        using var layout = new BackupLayout("first\n");
        layout.SeedBaseline("first\n");
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
        layout.SeedBaseline("same\n");
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
        using var layout = new BackupLayout(runtimeContent: null, runtimeConfig: null);
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

        var stagedFiles = ReviewStagedManagedFiles(layout);
        BackupCommand.CommitStagedIndex(
            layout.Repository,
            "Pinned test commit",
            stagedFiles);

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
        using var layout = new BackupLayout(runtimeContent: null, runtimeConfig: null);
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
        var stagedFiles = ReviewStagedManagedFiles(layout);
        File.WriteAllText(Path.Combine(layout.Repository, "unrelated.txt"), "unexpected\n");
        Assert.Equal(0, Git(layout, "add", "--", "unrelated.txt").ExitCode);

        var exception = Assert.Throws<InvalidOperationException>(
            () => BackupCommand.CommitStagedIndex(
                layout.Repository,
                "Must not commit",
                stagedFiles));

        Assert.Contains("outside the approved", exception.Message, StringComparison.Ordinal);
        Assert.NotEqual(0, Git(layout, "rev-parse", "--verify", "HEAD").ExitCode);
    }

    [Fact]
    public void CommitRejectsAnUnrelatedIndexChangeHiddenByAReplacementTree()
    {
        using var layout = new BackupLayout(runtimeContent: null, runtimeConfig: null);
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
        var stagedFiles = ReviewStagedManagedFiles(layout);
        var approvedTree = Git(layout, "write-tree").Output.Trim();

        File.WriteAllText(Path.Combine(layout.Repository, "unrelated.txt"), "unexpected\n");
        Assert.Equal(0, Git(layout, "add", "--", "unrelated.txt").ExitCode);
        var poisonedTree = Git(layout, "write-tree").Output.Trim();
        Assert.NotEqual(approvedTree, poisonedTree);
        Assert.Equal(
            0,
            Git(
                layout,
                "update-ref",
                $"refs/replace/{poisonedTree}",
                approvedTree).ExitCode);

        // Prove the fixture hides the unrelated path unless replacement objects are disabled.
        Assert.DoesNotContain(
            "unrelated.txt",
            Git(layout, "ls-tree", "-r", "--name-only", poisonedTree).Output,
            StringComparison.Ordinal);
        Assert.Contains(
            "unrelated.txt",
            Git(
                layout,
                "--no-replace-objects",
                "ls-tree",
                "-r",
                "--name-only",
                poisonedTree).Output,
            StringComparison.Ordinal);

        var exception = Assert.Throws<InvalidOperationException>(
            () => BackupCommand.CommitStagedIndex(
                layout.Repository,
                "Must not commit",
                stagedFiles));

        Assert.Contains("outside the approved", exception.Message, StringComparison.Ordinal);
        Assert.NotEqual(
            0,
            Git(layout, "--no-replace-objects", "rev-parse", "--verify", "HEAD").ExitCode);
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
        var unrelated = Path.Combine(layout.Repository, "unrelated.txt");
        File.WriteAllText(unrelated, "committed\n");
        layout.SeedBaseline("first\n", "unrelated.txt");
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
        layout.SeedBaseline("first\n");
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
        using var layout = new BackupLayout(runtimeContent: null);

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
        using var layout = new BackupLayout(runtimeContent: null);
        Directory.CreateDirectory(layout.Runtime);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Runtime target must be a regular file", result.Error, StringComparison.Ordinal);
        Assert.Empty(File.ReadAllBytes(layout.Source));
    }

    [Fact]
    public void RuntimeFilesOverOneMiBAreRejectedWithoutRepositoryChanges()
    {
        using var layout = new BackupLayout(runtimeContent: null);
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

        using var layout = new BackupLayout(runtimeContent: null);
        var referent = Path.Combine(layout.Root, "runtime referent.md");
        File.WriteAllText(referent, "external\n");
        File.CreateSymbolicLink(layout.Runtime, referent);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Runtime target must not be a symbolic link", result.Error, StringComparison.Ordinal);
        Assert.Empty(File.ReadAllBytes(layout.Source));
        Assert.Equal("external\n", File.ReadAllText(referent));
    }

    [Fact]
    public void DetachedHeadIsRejectedBeforeWriting()
    {
        using var layout = new BackupLayout("first\n");
        layout.SeedBaseline("first\n");
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
    public void ActiveSequencerStopsBeforeWriting()
    {
        using var layout = new BackupLayout("runtime\n");
        Directory.CreateDirectory(Path.Combine(layout.Repository, ".git", "sequencer"));

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Git operation sequencer is active", result.Error, StringComparison.Ordinal);
        Assert.Empty(File.ReadAllBytes(layout.Source));
        Assert.NotEqual(0, Git(layout, "rev-parse", "--verify", "HEAD").ExitCode);
    }

    [Fact]
    public void OrdinaryRefNamedLikeAnOperationDoesNotBlockBackup()
    {
        using var layout = new BackupLayout("first\n");
        layout.SeedBaseline("first\n");
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
        File.WriteAllText(
            Path.Combine(layout.Repository, ".gitattributes"),
            $"{SourceRelativePath} text eol=lf\n");
        layout.SeedBaseline("first\n", ".gitattributes");
        var headBefore = Git(layout, "rev-parse", "HEAD").Output.Trim();
        File.WriteAllBytes(layout.Runtime, "second\r\n"u8.ToArray());

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("filters changed", result.Error, StringComparison.Ordinal);
        Assert.Equal(headBefore, Git(layout, "rev-parse", "HEAD").Output.Trim());
        Assert.Equal("second\r\n"u8.ToArray(), File.ReadAllBytes(layout.Source));
        Assert.Equal(SourceRelativePath, Git(layout, "diff", "--cached", "--name-only").Output.Trim());
    }

    private static CommandResult Run(
        BackupLayout layout, CodexPersonality expectedPersonality = CodexPersonality.None)
    {
        var output = new StringWriter();
        var warning = new StringWriter();
        try
        {
            var exitCode = BackupCommand.RunForInitialization(
                layout.Repository, layout.CodexHome, expectedPersonality, output);
            return new CommandResult(exitCode, output.ToString(), warning.ToString());
        }
        catch (Exception error)
        {
            warning.WriteLine($"error: {error.Message}");
            return new CommandResult(1, output.ToString(), warning.ToString());
        }
    }

    private static (string RelativePath, string Blob)[] ReviewStagedManagedFiles(BackupLayout layout)
    {
        var blobs = GitProcess.RunRequired(layout.Repository, "Could not read staged fixture blobs",
                "rev-parse", $":{SourceRelativePath}", $":{ConfigSourceRelativePath}")
            .Output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        Assert.Equal(2, blobs.Length);
        return [(SourceRelativePath, blobs[0]), (ConfigSourceRelativePath, blobs[1])];
    }

    private static GitResult Git(BackupLayout layout, params string[] arguments)
    {
        return TestGit.Run(layout.Repository, arguments);
    }

    private sealed class BackupLayout : IDisposable
    {
        public BackupLayout(
            string? runtimeContent,
            string? runtimeConfig = "personality = \"none\"\n")
        {
            Root = Path.Combine(TestTemporaryDirectory.Root, "aec-backup-tests", Guid.NewGuid().ToString("N"));
            Repository = Path.Combine(Root, "data repository");
            CodexHome = Path.Combine(Root, "codex home");
            Source = Path.Combine(Repository, SourceRelativePath);
            ConfigSource = Path.Combine(Repository, ConfigSourceRelativePath);
            Runtime = Path.Combine(CodexHome, "AGENTS.md");
            RuntimeConfig = Path.Combine(CodexHome, "config.toml");

            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(CodexHome);
            if (runtimeContent is not null)
            {
                File.WriteAllText(Runtime, runtimeContent);
            }
            if (runtimeConfig is not null)
            {
                File.WriteAllText(RuntimeConfig, runtimeConfig);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(Source)!);
            var init = TestGit.Run(
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

            // Identity and signing come from the collection. The empty Git template supplies no hooks.
            File.AppendAllText(Path.Combine(Repository, ".git", "config"),
                "\n[core]\n\thooksPath = .git/hooks\n\tautocrlf = false\n");
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

        public void SeedBaseline(string instructions, params string[] extraPaths)
        {
            File.WriteAllText(Source, instructions);
            File.WriteAllText(ConfigSource, "personality = \"none\"\n");
            _ = GitProcess.RunRequired(Repository, "Could not stage backup baseline",
                ["add", "--", SourceRelativePath, ConfigSourceRelativePath, .. extraPaths]);
            _ = GitProcess.RunRequired(Repository, "Could not commit backup baseline",
                "commit", "--quiet", "-m", BackupCommand.OrdinaryCommitMessage);
        }
    }
}
