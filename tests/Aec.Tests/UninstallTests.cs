namespace Aec.Tests;

[Collection(ProcessStateCollection.Name)]
public sealed class UninstallTests
{
    [Fact]
    public void HelpListsTheExactUninstallForm()
    {
        var result = Run("help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(
            "aec uninstall [--codex-home ABSOLUTE_PATH]",
            result.Output,
            StringComparison.Ordinal);
        Assert.Empty(result.Error);
    }

    [Fact]
    public void RemovesManagedInstructionsAndSkillWhilePreservingEverythingElse()
    {
        using var layout = new UninstallLayout();
        var originalInstructions = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat("# Personal instructions\r\nKeep this byte-for-byte.\r\n"u8.ToArray())
            .ToArray();
        var managedInstructions = AecInstructionBlock.MergeForChatGptProvider(
            originalInstructions,
            layout.Repository);
        File.WriteAllBytes(layout.Agents, managedInstructions);
        var config = new byte[] { 0, 1, 2, 3, 255 };
        File.WriteAllBytes(layout.Config, config);
        var notes = Path.Combine(layout.SkillRoot, "notes.md");
        var otherSkill = Path.Combine(layout.CodexHome, "skills", "other", "SKILL.md");
        Directory.CreateDirectory(Path.GetDirectoryName(otherSkill)!);
        File.WriteAllText(notes, "personal skill notes\n");
        File.WriteAllText(otherSkill, "another skill\n");

        var result = Run("uninstall", "--codex-home", layout.CodexHome);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"uninstalled{Environment.NewLine}", result.Output);
        Assert.Empty(result.Error);
        Assert.Equal(originalInstructions, File.ReadAllBytes(layout.Agents));
        Assert.Equal(config, File.ReadAllBytes(layout.Config));
        Assert.False(File.Exists(layout.Skill));
        Assert.False(File.Exists(layout.Metadata));
        Assert.Equal("personal skill notes\n", File.ReadAllText(notes));
        Assert.Equal("another skill\n", File.ReadAllText(otherSkill));
        Assert.True(Directory.Exists(layout.SkillRoot));
    }

    [Fact]
    public void RepeatedUninstallIsIdempotent()
    {
        using var layout = new UninstallLayout();
        File.WriteAllBytes(
            layout.Agents,
            AecInstructionBlock.Merge("outside\n"u8.ToArray(), layout.Repository));

        var first = Run("uninstall", "--codex-home", layout.CodexHome);
        var agentsAfterFirst = File.ReadAllBytes(layout.Agents);
        var second = Run("uninstall", "--codex-home", layout.CodexHome);

        Assert.Equal(0, first.ExitCode);
        Assert.Equal($"uninstalled{Environment.NewLine}", first.Output);
        Assert.Equal(0, second.ExitCode);
        Assert.Equal($"unchanged{Environment.NewLine}", second.Output);
        Assert.Empty(second.Error);
        Assert.Equal(agentsAfterFirst, File.ReadAllBytes(layout.Agents));
    }

    [Fact]
    public void UnmanagedAecReferenceStopsBeforeAnyMutation()
    {
        using var layout = new UninstallLayout();
        var instructions = AecInstructionBlock.Merge(
            "Use $aec for my separate workflow.\n"u8.ToArray(),
            layout.Repository);
        File.WriteAllBytes(layout.Agents, instructions);
        var skill = File.ReadAllBytes(layout.Skill);
        var metadata = File.ReadAllBytes(layout.Metadata);

        var result = Run("uninstall", "--codex-home", layout.CodexHome);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("$aec", result.Error, StringComparison.Ordinal);
        Assert.Equal(instructions, File.ReadAllBytes(layout.Agents));
        Assert.Equal(skill, File.ReadAllBytes(layout.Skill));
        Assert.Equal(metadata, File.ReadAllBytes(layout.Metadata));
    }

    [Fact]
    public void ModifiedSkillStopsBeforeRemovingManagedInstructions()
    {
        using var layout = new UninstallLayout();
        var instructions = AecInstructionBlock.Merge(
            "outside\n"u8.ToArray(),
            layout.Repository);
        File.WriteAllBytes(layout.Agents, instructions);
        File.AppendAllText(layout.Skill, "# local customization\n");
        var modifiedSkill = File.ReadAllBytes(layout.Skill);

        var result = Run("uninstall", "--codex-home", layout.CodexHome);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("exact supported official bundle", result.Error, StringComparison.Ordinal);
        Assert.Equal(instructions, File.ReadAllBytes(layout.Agents));
        Assert.Equal(modifiedSkill, File.ReadAllBytes(layout.Skill));
        Assert.True(File.Exists(layout.Metadata));
    }

    [Fact]
    public void LinkedMetadataStopsBeforeRemovingManagedInstructions()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var layout = new UninstallLayout();
        var instructions = AecInstructionBlock.Merge(
            "outside\n"u8.ToArray(),
            layout.Repository);
        File.WriteAllBytes(layout.Agents, instructions);
        var referent = Path.Combine(layout.Root, "external metadata.yaml");
        File.WriteAllBytes(referent, File.ReadAllBytes(layout.Metadata));
        File.Delete(layout.Metadata);
        File.CreateSymbolicLink(layout.Metadata, referent);

        var result = Run("uninstall", "--codex-home", layout.CodexHome);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("must not be a symbolic link", result.Error, StringComparison.Ordinal);
        Assert.Equal(instructions, File.ReadAllBytes(layout.Agents));
        Assert.True(File.Exists(layout.Skill));
        Assert.True(File.Exists(referent));
    }

    [Fact]
    public void MalformedManagedBlockStopsBeforeRemovingTheSkill()
    {
        using var layout = new UninstallLayout();
        var instructions = "<!-- AEC:BEGIN version=4 -->\ncustom\n<!-- AEC:END -->\n"u8.ToArray();
        File.WriteAllBytes(layout.Agents, instructions);
        var skill = File.ReadAllBytes(layout.Skill);

        var result = Run("uninstall", "--codex-home", layout.CodexHome);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("supported initialized AEC block", result.Error, StringComparison.Ordinal);
        Assert.Equal(instructions, File.ReadAllBytes(layout.Agents));
        Assert.Equal(skill, File.ReadAllBytes(layout.Skill));
    }

    [Fact]
    public void ResumesWhenManagedInstructionsWereAlreadyRemoved()
    {
        using var layout = new UninstallLayout();
        File.WriteAllText(layout.Agents, "outside\n");
        File.Delete(layout.Skill);

        var result = Run("uninstall", "--codex-home", layout.CodexHome);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"uninstalled{Environment.NewLine}", result.Output);
        Assert.Empty(result.Error);
        Assert.Equal("outside\n", File.ReadAllText(layout.Agents));
        Assert.False(File.Exists(layout.Metadata));
    }

    [Fact]
    public void RemovesTheExactVersion100SkillPredecessor()
    {
        using var layout = new UninstallLayout();
        File.WriteAllBytes(
            layout.Skill,
            File.ReadAllBytes(Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "Skills",
                "1.0.0.md")));

        var result = Run("uninstall", "--codex-home", layout.CodexHome);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"uninstalled{Environment.NewLine}", result.Output);
        Assert.Empty(result.Error);
        Assert.False(File.Exists(layout.Skill));
        Assert.False(File.Exists(layout.Metadata));
    }

    [Fact]
    public void RemovesTheExactVersion110SkillPredecessor()
    {
        using var layout = new UninstallLayout();
        File.WriteAllBytes(
            layout.Skill,
            File.ReadAllBytes(Path.Combine(
                AppContext.BaseDirectory,
                "Fixtures",
                "Skills",
                "1.1.0.md")));

        var result = Run("uninstall", "--codex-home", layout.CodexHome);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"uninstalled{Environment.NewLine}", result.Output);
        Assert.Empty(result.Error);
        Assert.False(File.Exists(layout.Skill));
        Assert.False(File.Exists(layout.Metadata));
    }

    [Fact]
    public void ExplicitCodexHomeOverridesEnvironmentSelection()
    {
        using var selected = new UninstallLayout();
        using var poison = new UninstallLayout();
        File.WriteAllBytes(
            selected.Agents,
            AecInstructionBlock.Merge("selected\n"u8.ToArray(), selected.Repository));
        File.WriteAllBytes(
            poison.Agents,
            AecInstructionBlock.Merge("poison\n"u8.ToArray(), poison.Repository));
        var poisonSkill = File.ReadAllBytes(poison.Skill);
        var previous = Environment.GetEnvironmentVariable("CODEX_HOME");

        try
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", poison.CodexHome);
            var result = Run("uninstall", "--codex-home", selected.CodexHome);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal("selected\n", File.ReadAllText(selected.Agents));
            Assert.False(File.Exists(selected.Skill));
            Assert.Contains("<!-- AEC:BEGIN", File.ReadAllText(poison.Agents), StringComparison.Ordinal);
            Assert.Equal(poisonSkill, File.ReadAllBytes(poison.Skill));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", previous);
        }
    }

    [Theory]
    [MemberData(nameof(InvalidArguments))]
    public void RejectsInvalidArguments(string[] arguments, string message)
    {
        var result = Run(arguments);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Equal($"error: {message}{Environment.NewLine}", result.Error);
    }

    public static TheoryData<string[], string> InvalidArguments => new()
    {
        { ["uninstall", "--codex-home"], "--codex-home requires a value." },
        { ["uninstall", "--codex-home", ""], "--codex-home requires a non-empty path." },
        { ["uninstall", "--codex-home", "relative"], "--codex-home must be an absolute path." },
        {
            ["uninstall", "--codex-home", Path.GetTempPath(), "--codex-home", Path.GetTempPath()],
            "--codex-home may be specified only once."
        },
        { ["uninstall", "--repo", Path.GetTempPath()], "Unknown argument: --repo" },
        { ["uninstall", "unexpected"], "Unknown argument: unexpected" },
        { ["uninstall", "--codex-home=/tmp/codex"], "Unknown argument: --codex-home=/tmp/codex" }
    };

    private static CommandResult Run(params string[] arguments)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = AecApplication.Run(arguments, output, error);
        return new CommandResult(exitCode, output.ToString(), error.ToString());
    }

    private sealed record CommandResult(int ExitCode, string Output, string Error);

    private sealed class UninstallLayout : IDisposable
    {
        public UninstallLayout()
        {
            Root = Path.Combine(
                OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(),
                "aec-uninstall-tests",
                Guid.NewGuid().ToString("N"));
            CodexHome = Path.Combine(Root, "codex home");
            Repository = Path.Combine(Root, "data repo");
            Agents = Path.Combine(CodexHome, "AGENTS.md");
            Config = Path.Combine(CodexHome, "config.toml");
            Skill = Path.Combine(CodexHome, "skills", "aec", "SKILL.md");
            Metadata = Path.Combine(CodexHome, "skills", "aec", "agents", "openai.yaml");

            Directory.CreateDirectory(CodexHome);
            Directory.CreateDirectory(Repository);
            File.WriteAllText(Agents, "outside\n");
            File.WriteAllText(Config, "personality = \"pragmatic\"\n");
            AecSkillInstaller.Install(CodexHome);
        }

        public string Root { get; }

        public string CodexHome { get; }

        public string Repository { get; }

        public string Agents { get; }

        public string Config { get; }

        public string Skill { get; }

        public string Metadata { get; }

        public string SkillRoot => Path.GetDirectoryName(Skill)!;

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
