namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class PortableCodexApplyCommandTests
{
    [Fact]
    public void ExactPortableRuntimeAndPersonalityAreUnchanged()
    {
        using var layout = new SharedTargetStatusTests.StatusLayout("codex");
        layout.WriteRuntime(portableBlock: true);
        var configPath = Path.Combine(layout.ProviderHome, "config.toml");
        File.WriteAllText(configPath, "personality = \"none\"\nmodel = \"keep-me\"\n");
        var runtime = File.ReadAllBytes(layout.RuntimePath);
        var config = File.ReadAllBytes(configPath);
        var head = layout.Head();

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"unchanged{Environment.NewLine}", result.Output);
        Assert.Empty(result.Error);
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(config, File.ReadAllBytes(configPath));
        Assert.Equal(head, layout.Head());
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AppliesOnlyTheDriftingManagedFiles(
        bool instructionDrift, bool personalityDrift)
    {
        using var layout = new SharedTargetStatusTests.StatusLayout("codex");
        layout.WriteRuntime(portableBlock: true);
        var configPath = Path.Combine(layout.ProviderHome, "config.toml");
        File.WriteAllText(configPath,
            $"personality = \"{(personalityDrift ? "friendly" : "none")}\"\n" +
            "model = \"keep-me\"\n");
        if (instructionDrift)
        {
            File.AppendAllText(layout.RuntimePath, "Unreviewed instruction edit.\n");
        }

        var desired = PortableApplyPreview.Create(
            layout.Repository, "codex", layout.ProviderHome).DesiredRuntime;
        var head = layout.Head();

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"applied{Environment.NewLine}", result.Output);
        Assert.Empty(result.Error);
        Assert.Equal(desired, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal("personality = \"none\"\nmodel = \"keep-me\"\n",
            File.ReadAllText(configPath));
        Assert.Equal(head, layout.Head());
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void MissingRuntimePersonalityIsInsertedWithWarning()
    {
        using var layout = new SharedTargetStatusTests.StatusLayout("codex");
        layout.WriteRuntime(portableBlock: true);
        var configPath = Path.Combine(layout.ProviderHome, "config.toml");
        File.WriteAllText(configPath, "model = \"keep-me\"\n");

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"applied{Environment.NewLine}", result.Output);
        Assert.Contains("warning: runtime config does not declare root `personality`",
            result.Error, StringComparison.Ordinal);
        Assert.Equal("personality = \"none\"\nmodel = \"keep-me\"\n",
            File.ReadAllText(configPath));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void MissingRuntimeConfigIsCreatedWithWarning()
    {
        using var layout = new SharedTargetStatusTests.StatusLayout("codex");
        layout.WriteRuntime(portableBlock: true);
        var configPath = Path.Combine(layout.ProviderHome, "config.toml");
        Assert.False(File.Exists(configPath));

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"applied{Environment.NewLine}", result.Output);
        Assert.Contains("warning: runtime config does not declare root `personality`",
            result.Error, StringComparison.Ordinal);
        Assert.Equal("personality = \"none\"\n", File.ReadAllText(configPath));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void MissingRuntimeInstructionsStopBeforeConfigWrite()
    {
        using var layout = new SharedTargetStatusTests.StatusLayout("codex");
        var configPath = Path.Combine(layout.ProviderHome, "config.toml");
        File.WriteAllText(configPath, "personality = \"friendly\"\n");

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("Run ordinary provider init first", result.Error,
            StringComparison.Ordinal);
        Assert.False(File.Exists(layout.RuntimePath));
        Assert.Equal("personality = \"friendly\"\n", File.ReadAllText(configPath));
    }

    [Fact]
    public void MissingCommittedEnrollmentDoesNotFallBackToLegacySource()
    {
        using var layout = new SharedTargetStatusTests.StatusLayout("codex");
        layout.WriteRuntime(portableBlock: true);
        var legacyPath = Path.Combine(layout.Repository,
            AecApplication.SourceRelativePath);
        File.WriteAllText(legacyPath, "Legacy source must not be applied.\n");
        GitProcess.RunRequired(layout.Repository, "Could not stage legacy fixture source",
            "add", "--", AecApplication.SourceRelativePath);
        GitProcess.RunRequired(layout.Repository, "Could not remove enrolled fixture target",
            "rm", "--", Path.GetRelativePath(layout.Repository, layout.TargetPath));
        GitProcess.RunRequired(layout.Repository, "Could not commit fixture change",
            "-c", "user.name=AEC Test", "-c", "user.email=aec-test@example.invalid",
            "-c", "commit.gpgsign=false", "commit", "-m", "Remove portable enrollment");
        var runtime = File.ReadAllBytes(layout.RuntimePath);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("Committed target", result.Error, StringComparison.Ordinal);
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void WrongRepositoryBindingStopsBeforeConfigWrite()
    {
        using var layout = new SharedTargetStatusTests.StatusLayout("codex");
        layout.WriteRuntime(boundRepository: Path.Combine(layout.Root, "other-data"),
            portableBlock: true);
        var configPath = Path.Combine(layout.ProviderHome, "config.toml");
        File.WriteAllText(configPath, "personality = \"friendly\"\n");
        var runtime = File.ReadAllBytes(layout.RuntimePath);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("not bound to the selected AEC repository", result.Error,
            StringComparison.Ordinal);
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal("personality = \"friendly\"\n", File.ReadAllText(configPath));
    }

    [Fact]
    public void InvalidRuntimeConfigStopsBeforeEitherWrite()
    {
        using var layout = new SharedTargetStatusTests.StatusLayout("codex");
        layout.WriteRuntime(portableBlock: true);
        File.AppendAllText(layout.RuntimePath, "Unreviewed instruction edit.\n");
        var runtime = File.ReadAllBytes(layout.RuntimePath);
        var configPath = Path.Combine(layout.ProviderHome, "config.toml");
        var invalid = "personality = \"none\"\npersonality = \"friendly\"\n";
        File.WriteAllText(configPath, invalid);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("more than once", result.Error, StringComparison.Ordinal);
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(invalid, File.ReadAllText(configPath));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void DirtyRenderedTargetStopsBeforeConfigWrite()
    {
        using var layout = new SharedTargetStatusTests.StatusLayout("codex");
        layout.WriteRuntime(portableBlock: true);
        var runtime = File.ReadAllBytes(layout.RuntimePath);
        var configPath = Path.Combine(layout.ProviderHome, "config.toml");
        File.WriteAllText(configPath, "personality = \"friendly\"\n");
        File.AppendAllText(layout.TargetPath, "Uncommitted target edit.\n");

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("unstaged changes", result.Error, StringComparison.Ordinal);
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal("personality = \"friendly\"\n", File.ReadAllText(configPath));
    }

    [Fact]
    public void LegacyChatGptBlockUpgradesWhileRetainingGuidance()
    {
        using var layout = new SharedTargetStatusTests.StatusLayout("codex");
        layout.WriteRuntime(codexChatGptBlock: true);
        File.WriteAllText(Path.Combine(layout.ProviderHome, "config.toml"),
            "personality = \"none\"\n");

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"applied{Environment.NewLine}", result.Output);
        var runtime = File.ReadAllText(layout.RuntimePath);
        Assert.Contains("<!-- AEC:BEGIN version=10 -->", runtime,
            StringComparison.Ordinal);
        Assert.Contains("Manual ChatGPT instruction backups live under", runtime,
            StringComparison.Ordinal);
        Assert.Contains("<!-- AEC:SOURCE:SHARED:BEGIN -->", runtime,
            StringComparison.Ordinal);
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    private static CommandResult Run(SharedTargetStatusTests.StatusLayout layout) =>
        TestApplication.Run("apply", "--repo", layout.Repository,
            "--codex-home", layout.ProviderHome);
}
