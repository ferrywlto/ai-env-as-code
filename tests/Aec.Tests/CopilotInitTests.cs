namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class CopilotInitTests
{
    [Fact]
    public void CapturesRuntimeInstructionsAndInstallsTheCopilotSkill()
    {
        using var layout = new CopilotLayout();
        var expected = CopilotInstructionBlock.Merge(
            File.ReadAllBytes(layout.RuntimeInstructions),
            layout.Repository);

        var result = Run(layout.Repository, layout.CopilotHome);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"initialized{Environment.NewLine}", result.Output);
        Assert.Empty(result.Error);
        Assert.Equal(expected, File.ReadAllBytes(layout.CanonicalInstructions));
        Assert.Equal(expected, File.ReadAllBytes(layout.RuntimeInstructions));
        Assert.Contains(
            "Initialize the local GitHub Copilot CLI integration",
            File.ReadAllText(layout.Skill),
            StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(layout.CopilotHome, "config.json")));
    }

    [Fact]
    public void AcceptsProviderBeforeRepository()
    {
        using var layout = new CopilotLayout();

        var result = TestApplication.Run(
            "init",
            "--provider=copilot",
            "--copilot-home",
            layout.CopilotHome,
            "--repo",
            layout.Repository);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(layout.CanonicalInstructions));
    }

    [Fact]
    public void RequiresAnExplicitRepository()
    {
        using var layout = new CopilotLayout();
        var previousDirectory = Environment.CurrentDirectory;

        try
        {
            Environment.CurrentDirectory = layout.Repository;

            var result = TestApplication.Run(
                "init",
                "--provider=copilot",
                "--copilot-home",
                layout.CopilotHome);

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("init requires --repo", result.Error, StringComparison.Ordinal);
            Assert.False(File.Exists(layout.CanonicalInstructions));
        }
        finally
        {
            Environment.CurrentDirectory = previousDirectory;
        }
    }

    [Fact]
    public void ExplicitCopilotHomeTakesPrecedenceOverEnvironment()
    {
        using var layout = new CopilotLayout();
        var previous = Environment.GetEnvironmentVariable("COPILOT_HOME");

        try
        {
            Environment.SetEnvironmentVariable("COPILOT_HOME", "not-an-absolute-path");

            var result = Run(layout.Repository, layout.CopilotHome);

            Assert.Equal(0, result.ExitCode);
            Assert.True(File.Exists(layout.RuntimeInstructions));
        }
        finally
        {
            Environment.SetEnvironmentVariable("COPILOT_HOME", previous);
        }
    }

    [Fact]
    public void UsesCopilotHomeEnvironmentWhenNoOptionWasSupplied()
    {
        using var layout = new CopilotLayout();
        var previous = Environment.GetEnvironmentVariable("COPILOT_HOME");

        try
        {
            Environment.SetEnvironmentVariable("COPILOT_HOME", layout.CopilotHome);

            var result = TestApplication.Run(
                "init",
                "--repo",
                layout.Repository,
                "--provider=copilot");

            Assert.Equal(0, result.ExitCode);
            Assert.True(File.Exists(layout.CanonicalInstructions));
        }
        finally
        {
            Environment.SetEnvironmentVariable("COPILOT_HOME", previous);
        }
    }

    [Fact]
    public void RepeatInitializationIsByteAndTimestampStable()
    {
        using var layout = new CopilotLayout();
        Assert.Equal(0, Run(layout.Repository, layout.CopilotHome).ExitCode);
        var timestamp = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(layout.CanonicalInstructions, timestamp);
        File.SetLastWriteTimeUtc(layout.RuntimeInstructions, timestamp);
        File.SetLastWriteTimeUtc(layout.Skill, timestamp);

        var result = Run(layout.Repository, layout.CopilotHome);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"unchanged{Environment.NewLine}", result.Output);
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(layout.CanonicalInstructions));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(layout.RuntimeInstructions));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(layout.Skill));
    }

    [Fact]
    public void CanonicalInstructionsApplyOnARepeatInitialization()
    {
        using var layout = new CopilotLayout();
        Assert.Equal(0, Run(layout.Repository, layout.CopilotHome).ExitCode);
        var expected = File.ReadAllBytes(layout.CanonicalInstructions);
        File.WriteAllText(layout.RuntimeInstructions, "Local drift.\n");

        var result = Run(layout.Repository, layout.CopilotHome);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"initialized{Environment.NewLine}", result.Output);
        Assert.Equal(expected, File.ReadAllBytes(layout.RuntimeInstructions));
    }

    [Fact]
    public void RejectsACustomCopilotSkillBeforeWritingInstructions()
    {
        using var layout = new CopilotLayout();
        Directory.CreateDirectory(Path.GetDirectoryName(layout.Skill)!);
        File.WriteAllText(layout.Skill, "# Personal skill\n");
        var runtimeBefore = File.ReadAllBytes(layout.RuntimeInstructions);

        var result = Run(layout.Repository, layout.CopilotHome);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("conflicts with the bundled version", result.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(layout.CanonicalInstructions));
        Assert.Equal(runtimeBefore, File.ReadAllBytes(layout.RuntimeInstructions));
    }

    [Fact]
    public void RejectsAProviderPathBoundToAnotherRepository()
    {
        using var layout = new CopilotLayout();
        Assert.Equal(0, Run(layout.Repository, layout.CopilotHome).ExitCode);
        var movedRepository = Path.Combine(layout.Root, "moved-repository");
        Directory.Move(layout.Repository, movedRepository);
        var movedCanonical = Path.Combine(movedRepository, AecApplication.CopilotSourceRelativePath);
        var runtimeBefore = File.ReadAllBytes(layout.RuntimeInstructions);

        var result = Run(movedRepository, layout.CopilotHome);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("different data repository", result.Error, StringComparison.Ordinal);
        Assert.Equal(runtimeBefore, File.ReadAllBytes(layout.RuntimeInstructions));
        Assert.True(File.Exists(movedCanonical));
    }

    [Fact]
    public void RejectsCodexHomeAndCopilotHomeTogether()
    {
        using var layout = new CopilotLayout();

        var result = TestApplication.Run(
            "init",
            "--repo",
            layout.Repository,
            "--provider=copilot",
            "--copilot-home",
            layout.CopilotHome,
            "--codex-home",
            layout.CodexHome);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("--codex-home is not valid", result.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(layout.CanonicalInstructions));
    }

    private static CommandResult Run(string repository, string copilotHome) =>
        TestApplication.Run(
            "init",
            "--repo",
            repository,
            "--provider=copilot",
            "--copilot-home",
            copilotHome);

    private sealed class CopilotLayout : IDisposable
    {
        public CopilotLayout()
        {
            Root = Path.Combine(
                OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(),
                "aec-copilot-init-tests",
                Guid.NewGuid().ToString("N"));
            Repository = Path.Combine(Root, "repository");
            CodexHome = Path.Combine(Root, "codex-home");
            CopilotHome = Path.Combine(Root, "copilot-home");
            RuntimeInstructions = Path.Combine(CopilotHome, "copilot-instructions.md");
            CanonicalInstructions = Path.Combine(
                Repository,
                AecApplication.CopilotSourceRelativePath);
            Skill = Path.Combine(CopilotHome, "skills", "aec", "SKILL.md");

            Directory.CreateDirectory(CodexHome);
            File.WriteAllText(Path.Combine(CodexHome, "AGENTS.md"), "Codex runtime.\n");
            var initialization = TestApplication.Run(
                "init",
                "--repo",
                Repository,
                "--codex-home",
                CodexHome);
            if (initialization.ExitCode != 0)
            {
                throw new InvalidOperationException(initialization.Error);
            }

            Directory.CreateDirectory(CopilotHome);
            File.WriteAllText(RuntimeInstructions, "Existing Copilot instruction.\n");
        }

        public string Root { get; }

        public string Repository { get; }

        public string CodexHome { get; }

        public string CopilotHome { get; }

        public string RuntimeInstructions { get; }

        public string CanonicalInstructions { get; }

        public string Skill { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
