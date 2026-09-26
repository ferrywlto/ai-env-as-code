namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class CopilotApplyTests
{
    [Fact]
    public void AppliesExactCommittedBytesWithoutChangingGitOrCanonicalSource()
    {
        using var layout = new CopilotApplyLayout("Canonical.\n", "Runtime.\n");
        var canonicalBefore = File.ReadAllBytes(layout.CanonicalInstructions);
        var headBefore = Git(layout, "rev-parse", "HEAD").Output.Trim();

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"applied{Environment.NewLine}", result.Output);
        Assert.Empty(result.Error);
        Assert.Equal(canonicalBefore, File.ReadAllBytes(layout.RuntimeInstructions));
        Assert.Equal(canonicalBefore, File.ReadAllBytes(layout.CanonicalInstructions));
        Assert.Equal(headBefore, Git(layout, "rev-parse", "HEAD").Output.Trim());
        Assert.Empty(Git(layout, "status", "--porcelain").Output);
    }

    [Fact]
    public void MissingRuntimeInstructionsAreCreatedWithUserOnlyPermissions()
    {
        using var layout = new CopilotApplyLayout("Canonical.\n", runtimePersonal: null);

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"applied{Environment.NewLine}", result.Output);
        Assert.Equal(
            File.ReadAllBytes(layout.CanonicalInstructions),
            File.ReadAllBytes(layout.RuntimeInstructions));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(layout.RuntimeInstructions));
        }
    }

    [Fact]
    public void EqualRuntimeInstructionsAreUnchangedWithoutRewrite()
    {
        using var layout = new CopilotApplyLayout("Same.\n", "Same.\n");
        var timestamp = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(layout.RuntimeInstructions, timestamp);

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"unchanged{Environment.NewLine}", result.Output);
        Assert.Empty(result.Error);
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(layout.RuntimeInstructions));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirtyCanonicalInstructionsAreRejectedBeforeRuntimeWrite(bool stageChange)
    {
        using var layout = new CopilotApplyLayout("Canonical.\n", "Runtime.\n");
        var runtimeBefore = File.ReadAllBytes(layout.RuntimeInstructions);
        File.AppendAllText(layout.CanonicalInstructions, "Dirty.\n");
        if (stageChange)
        {
            Assert.Equal(
                0,
                Git(layout, "add", "--", AecApplication.CopilotSourceRelativePath).ExitCode);
        }

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            stageChange ? "staged changes" : "unstaged changes",
            result.Error,
            StringComparison.Ordinal);
        Assert.Equal(runtimeBefore, File.ReadAllBytes(layout.RuntimeInstructions));
    }

    [Fact]
    public void UncommittedCanonicalInstructionsAreRejectedBeforeRuntimeWrite()
    {
        using var layout = new CopilotApplyLayout(
            "Canonical.\n",
            "Runtime.\n",
            commitCanonical: false);
        var runtimeBefore = File.ReadAllBytes(layout.RuntimeInstructions);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("not a single committed Git file", result.Error, StringComparison.Ordinal);
        Assert.Equal(runtimeBefore, File.ReadAllBytes(layout.RuntimeInstructions));
    }

    [Fact]
    public void CanonicalInstructionsBoundToAnotherRepositoryDirectToInit()
    {
        using var layout = new CopilotApplyLayout(
            "Canonical.\n",
            "Runtime.\n",
            bindingRepository: Path.Combine(
                OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(),
                "different-aec-repository"));
        var runtimeBefore = File.ReadAllBytes(layout.RuntimeInstructions);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("different data repository", result.Error, StringComparison.Ordinal);
        Assert.Contains("aec init --provider=copilot", result.Error, StringComparison.Ordinal);
        Assert.Equal(runtimeBefore, File.ReadAllBytes(layout.RuntimeInstructions));
    }

    [Fact]
    public void CanonicalInstructionsWithoutManagedBlockAreRejected()
    {
        using var layout = new CopilotApplyLayout("Canonical.\n", "Runtime.\n");
        File.WriteAllText(layout.CanonicalInstructions, "Unmanaged canonical.\n");
        Assert.Equal(
            0,
            Git(layout, "add", "--", AecApplication.CopilotSourceRelativePath).ExitCode);
        Assert.Equal(
            0,
            Git(layout, "commit", "--quiet", "--message", "Replace canonical fixture").ExitCode);
        var runtimeBefore = File.ReadAllBytes(layout.RuntimeInstructions);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("supported managed AEC block", result.Error, StringComparison.Ordinal);
        Assert.Equal(runtimeBefore, File.ReadAllBytes(layout.RuntimeInstructions));
    }

    [Fact]
    public void UnrelatedRepositoryChangesArePreserved()
    {
        using var layout = new CopilotApplyLayout("Canonical.\n", "Runtime.\n");
        File.WriteAllText(Path.Combine(layout.Repository, "unrelated.txt"), "preserve\n");
        var statusBefore = Git(layout, "status", "--porcelain=v1").Output;
        var headBefore = Git(layout, "rev-parse", "HEAD").Output.Trim();

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            File.ReadAllBytes(layout.CanonicalInstructions),
            File.ReadAllBytes(layout.RuntimeInstructions));
        Assert.Equal(statusBefore, Git(layout, "status", "--porcelain=v1").Output);
        Assert.Equal(headBefore, Git(layout, "rev-parse", "HEAD").Output.Trim());
    }

    [Fact]
    public void RejectsCodexHomeForCopilotApply()
    {
        using var layout = new CopilotApplyLayout("Same.\n", "Same.\n");

        var result = TestApplication.Run(
            "apply",
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
        using var layout = new CopilotApplyLayout("Same.\n", "Same.\n");

        var result = TestApplication.Run(
            "apply",
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

    [Fact]
    public void CopilotRuntimeInsideRepositoryIsRejected()
    {
        using var layout = new CopilotApplyLayout("Canonical.\n", "Runtime.\n");
        var runtimeBefore = File.ReadAllBytes(layout.RuntimeInstructions);

        var result = TestApplication.Run(
            "apply",
            "--repo",
            layout.Repository,
            "--provider=copilot",
            "--copilot-home",
            layout.Repository);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "Copilot runtime target must be outside the data repository",
            result.Error,
            StringComparison.Ordinal);
        Assert.Equal(runtimeBefore, File.ReadAllBytes(layout.RuntimeInstructions));
    }

    private static CommandResult Run(CopilotApplyLayout layout) =>
        TestApplication.Run(
            "apply",
            "--repo",
            layout.Repository,
            "--provider=copilot",
            "--copilot-home",
            layout.CopilotHome);

    private static GitResult Git(CopilotApplyLayout layout, params string[] arguments) =>
        TestGit.Run(layout.Repository, arguments);

    private sealed class CopilotApplyLayout : IDisposable
    {
        public CopilotApplyLayout(
            string canonicalPersonal,
            string? runtimePersonal,
            bool commitCanonical = true,
            string? bindingRepository = null)
        {
            Root = Path.Combine(
                OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(),
                "aec-copilot-apply-tests",
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
                    bindingRepository ?? Repository));
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
