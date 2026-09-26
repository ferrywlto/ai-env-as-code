namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class CopilotBackupTests
{
    [Fact]
    public void CapturesRuntimeInstructionsInOneCommitWithoutChangingRuntime()
    {
        using var layout = new CopilotBackupLayout("Canonical personal.\n", "Runtime personal.\n");
        var runtimeBefore = File.ReadAllBytes(layout.RuntimeInstructions);
        var runtimeTimestamp = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(layout.RuntimeInstructions, runtimeTimestamp);

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.StartsWith("committed ", result.Output, StringComparison.Ordinal);
        Assert.Empty(result.Error);
        Assert.Equal(runtimeBefore, File.ReadAllBytes(layout.CanonicalInstructions));
        Assert.Equal(runtimeBefore, File.ReadAllBytes(layout.RuntimeInstructions));
        Assert.Equal(runtimeTimestamp, File.GetLastWriteTimeUtc(layout.RuntimeInstructions));
        Assert.Equal(
            CopilotBackupCommand.CommitMessage,
            Git(layout, "log", "-1", "--format=%s").Output.Trim());
        Assert.Equal(
            AecApplication.CopilotSourceRelativePath,
            Git(
                layout,
                "diff-tree",
                "--no-commit-id",
                "--name-only",
                "-r",
                "HEAD").Output.Trim());
        Assert.Empty(Git(layout, "status", "--porcelain").Output);

        var status = TestApplication.Run(
            "status",
            "--repo",
            layout.Repository,
            "--provider=copilot",
            "--copilot-home",
            layout.CopilotHome);
        Assert.Equal(0, status.ExitCode);
        Assert.Equal(
            $"copilot/copilot-instructions.md in_sync{Environment.NewLine}",
            status.Output);
    }

    [Fact]
    public void EqualCommittedInstructionsAreUnchanged()
    {
        using var layout = new CopilotBackupLayout("Same.\n", "Same.\n");
        var headBefore = Git(layout, "rev-parse", "HEAD").Output.Trim();

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"unchanged{Environment.NewLine}", result.Output);
        Assert.Empty(result.Error);
        Assert.Equal(headBefore, Git(layout, "rev-parse", "HEAD").Output.Trim());
        Assert.Empty(Git(layout, "status", "--porcelain").Output);
    }

    [Fact]
    public void EqualUntrackedCanonicalInstructionsAreCommitted()
    {
        using var layout = new CopilotBackupLayout(
            "Same.\n",
            "Same.\n",
            commitCanonical: false);

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.StartsWith("committed ", result.Output, StringComparison.Ordinal);
        Assert.Empty(result.Error);
        Assert.Equal(
            File.ReadAllBytes(layout.RuntimeInstructions),
            File.ReadAllBytes(layout.CanonicalInstructions));
        Assert.Equal(
            AecApplication.CopilotSourceRelativePath,
            Git(
                layout,
                "diff-tree",
                "--no-commit-id",
                "--name-only",
                "-r",
                "HEAD").Output.Trim());
        Assert.Empty(Git(layout, "status", "--porcelain").Output);
    }

    [Fact]
    public void UnrelatedRepositoryChangeStopsBeforeCanonicalMutation()
    {
        using var layout = new CopilotBackupLayout("Canonical.\n", "Runtime.\n");
        var canonicalBefore = File.ReadAllBytes(layout.CanonicalInstructions);
        var headBefore = Git(layout, "rev-parse", "HEAD").Output.Trim();
        File.WriteAllText(Path.Combine(layout.Repository, "unrelated.txt"), "preserve\n");

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("changes outside", result.Error, StringComparison.Ordinal);
        Assert.Equal(canonicalBefore, File.ReadAllBytes(layout.CanonicalInstructions));
        Assert.Equal(headBefore, Git(layout, "rev-parse", "HEAD").Output.Trim());
        Assert.Empty(Git(layout, "diff", "--cached", "--name-only").Output);
    }

    [Fact]
    public void MissingRuntimeInstructionsStopWithoutRepositoryMutation()
    {
        using var layout = new CopilotBackupLayout("Canonical.\n", null);
        var canonicalBefore = File.ReadAllBytes(layout.CanonicalInstructions);
        var headBefore = Git(layout, "rev-parse", "HEAD").Output.Trim();

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "Copilot runtime instructions does not exist",
            result.Error,
            StringComparison.Ordinal);
        Assert.Equal(canonicalBefore, File.ReadAllBytes(layout.CanonicalInstructions));
        Assert.Equal(headBefore, Git(layout, "rev-parse", "HEAD").Output.Trim());
        Assert.Empty(Git(layout, "diff", "--cached", "--name-only").Output);
    }

    [Fact]
    public void RuntimeWithoutManagedBlockStopsWithoutRepositoryMutation()
    {
        using var layout = new CopilotBackupLayout("Canonical.\n", "Runtime.\n");
        var canonicalBefore = File.ReadAllBytes(layout.CanonicalInstructions);
        var headBefore = Git(layout, "rev-parse", "HEAD").Output.Trim();
        File.WriteAllText(layout.RuntimeInstructions, "Unmanaged runtime.\n");

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("supported managed AEC block", result.Error, StringComparison.Ordinal);
        Assert.Equal(canonicalBefore, File.ReadAllBytes(layout.CanonicalInstructions));
        Assert.Equal(headBefore, Git(layout, "rev-parse", "HEAD").Output.Trim());
        Assert.Empty(Git(layout, "diff", "--cached", "--name-only").Output);
    }

    [Fact]
    public void RuntimeBoundToAnotherRepositoryStopsWithoutMutation()
    {
        using var layout = new CopilotBackupLayout("Canonical.\n", "Runtime.\n");
        var canonicalBefore = File.ReadAllBytes(layout.CanonicalInstructions);
        var anotherRepository = Path.Combine(layout.Root, "another-repository");
        File.WriteAllBytes(
            layout.RuntimeInstructions,
            CopilotInstructionBlock.Merge("Runtime.\n"u8.ToArray(), anotherRepository));

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("different data repository", result.Error, StringComparison.Ordinal);
        Assert.Equal(canonicalBefore, File.ReadAllBytes(layout.CanonicalInstructions));
        Assert.Empty(Git(layout, "diff", "--cached", "--name-only").Output);
    }

    [Fact]
    public void RejectsCodexHomeForCopilotBackup()
    {
        using var layout = new CopilotBackupLayout("Same.\n", "Same.\n");

        var result = TestApplication.Run(
            "backup",
            "--repo",
            layout.Repository,
            "--provider=copilot",
            "--codex-home",
            layout.CopilotHome);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "--codex-home is not valid with --provider=copilot",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsCopilotHomeWithoutTheProvider()
    {
        using var layout = new CopilotBackupLayout("Same.\n", "Same.\n");

        var result = TestApplication.Run(
            "backup",
            "--repo",
            layout.Repository,
            "--copilot-home",
            layout.CopilotHome);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "--copilot-home requires --provider=copilot",
            result.Error,
            StringComparison.Ordinal);
    }

    private static CommandResult Run(CopilotBackupLayout layout) =>
        TestApplication.Run(
            "backup",
            "--repo",
            layout.Repository,
            "--provider=copilot",
            "--copilot-home",
            layout.CopilotHome);

    private static GitResult Git(CopilotBackupLayout layout, params string[] arguments) =>
        TestGit.Run(layout.Repository, arguments);

    private sealed class CopilotBackupLayout : IDisposable
    {
        public CopilotBackupLayout(
            string canonicalPersonal,
            string? runtimePersonal,
            bool commitCanonical = true)
        {
            Root = Path.Combine(
                OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(),
                "aec-copilot-backup-tests",
                Guid.NewGuid().ToString("N"));
            Repository = Path.Combine(Root, "repository");
            CopilotHome = Path.Combine(Root, "copilot-home");
            CanonicalInstructions = Path.Combine(
                Repository,
                AecApplication.CopilotSourceRelativePath);
            RuntimeInstructions = Path.Combine(CopilotHome, "copilot-instructions.md");

            Directory.CreateDirectory(Path.Combine(Repository, "environment", "providers", "codex"));
            Directory.CreateDirectory(Path.GetDirectoryName(CanonicalInstructions)!);
            Directory.CreateDirectory(CopilotHome);
            File.WriteAllBytes(
                CanonicalInstructions,
                CopilotInstructionBlock.Merge(
                    System.Text.Encoding.UTF8.GetBytes(canonicalPersonal),
                    Repository));
            if (runtimePersonal is not null)
            {
                File.WriteAllBytes(
                    RuntimeInstructions,
                    CopilotInstructionBlock.Merge(
                        System.Text.Encoding.UTF8.GetBytes(runtimePersonal),
                        Repository));
            }

            File.WriteAllText(Path.Combine(Repository, "README.md"), "Fixture repository.\n");
            Assert.Equal(0, Git(this, "init", "--quiet", "--initial-branch=main").ExitCode);
            Assert.Equal(0, Git(this, "add", "--", "README.md").ExitCode);
            if (commitCanonical)
            {
                Assert.Equal(
                    0,
                    Git(this, "add", "--", AecApplication.CopilotSourceRelativePath).ExitCode);
            }

            Assert.Equal(
                0,
                Git(this, "commit", "--quiet", "--message", "Initialize fixture").ExitCode);
        }

        public string Root { get; }

        public string Repository { get; }

        public string CopilotHome { get; }

        public string CanonicalInstructions { get; }

        public string RuntimeInstructions { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
