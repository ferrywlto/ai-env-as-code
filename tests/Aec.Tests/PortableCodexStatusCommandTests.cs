namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class PortableCodexStatusCommandTests
{
    [Theory]
    [InlineData("none", "in_sync", 0)]
    [InlineData("friendly", "different", 2)]
    public void ReportsConfigStatusWithoutWriting(
        string runtimePersonality, string expectedStatus, int expectedExit)
    {
        using var layout = new SharedTargetStatusTests.StatusLayout("codex");
        var runtime = layout.WriteRuntime(portableBlock: true);
        var configPath = Path.Combine(layout.ProviderHome, "config.toml");
        var config = $"personality = \"{runtimePersonality}\"\nmodel = \"keep-me\"\n";
        File.WriteAllText(configPath, config);
        var head = layout.Head();

        var result = Run(layout);

        Assert.Equal(expectedExit, result.ExitCode);
        Assert.Equal(StatusOutput("in_sync", expectedStatus), result.Output);
        Assert.Empty(result.Error);
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(config, File.ReadAllText(configPath));
        Assert.Equal(head, layout.Head());
        Assert.Empty(layout.GitStatus());
    }

    [Fact]
    public void ReportsMissingRuntimeInstructionsAndConfig()
    {
        using var layout = new SharedTargetStatusTests.StatusLayout("codex");

        var result = Run(layout);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(StatusOutput("missing", "missing"), result.Output);
        Assert.Empty(result.Error);
        Assert.False(File.Exists(layout.RuntimePath));
        Assert.False(File.Exists(Path.Combine(layout.ProviderHome, "config.toml")));
        Assert.Empty(layout.GitStatus());
    }

    [Fact]
    public void DirtyCanonicalCodexConfigStopsWithoutWrites()
    {
        using var layout = new SharedTargetStatusTests.StatusLayout("codex");
        var runtime = layout.WriteRuntime(portableBlock: true);
        var configPath = Path.Combine(layout.ProviderHome, "config.toml");
        const string runtimeConfig = "personality = \"none\"\n";
        const string dirtyConfig = "personality = \"friendly\"\n";
        File.WriteAllText(configPath, runtimeConfig);
        File.WriteAllText(layout.CanonicalConfigPath, dirtyConfig);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("unstaged changes", result.Error, StringComparison.Ordinal);
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(runtimeConfig, File.ReadAllText(configPath));
        Assert.Equal(dirtyConfig, File.ReadAllText(layout.CanonicalConfigPath));
    }

    [Fact]
    public void MissingEnrollmentDoesNotFallBackToLegacySource()
    {
        using var layout = new SharedTargetStatusTests.StatusLayout("codex");
        layout.WriteRuntime(portableBlock: true);
        var runtime = File.ReadAllBytes(layout.RuntimePath);
        var legacyPath = Path.Combine(layout.Repository, AecApplication.SourceRelativePath);
        File.WriteAllText(legacyPath, "Legacy source must not be inspected.\n");
        GitProcess.RunRequired(layout.Repository, "Could not stage legacy source fixture",
            "add", "--", AecApplication.SourceRelativePath);
        GitProcess.RunRequired(layout.Repository, "Could not remove target fixture",
            "rm", "--", Path.GetRelativePath(layout.Repository, layout.TargetPath));
        GitProcess.RunRequired(layout.Repository, "Could not commit missing target fixture",
            "-c", "user.name=AEC Test", "-c", "user.email=aec-test@example.invalid",
            "-c", "commit.gpgsign=false", "commit", "-m", "Remove target fixture");

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("Committed target", result.Error, StringComparison.Ordinal);
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void WrongRepositoryBindingStopsWithoutPartialOutput()
    {
        using var layout = new SharedTargetStatusTests.StatusLayout("codex");
        layout.WriteRuntime(boundRepository: Path.Combine(layout.Root, "other-data"),
            portableBlock: true);
        var runtime = File.ReadAllBytes(layout.RuntimePath);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("not bound to the selected AEC repository", result.Error,
            StringComparison.Ordinal);
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void DirtyRenderedTargetStopsWithoutPartialOutput()
    {
        using var layout = new SharedTargetStatusTests.StatusLayout("codex");
        layout.WriteRuntime(portableBlock: true);
        var runtime = File.ReadAllBytes(layout.RuntimePath);
        File.AppendAllText(layout.TargetPath, "Uncommitted target edit.\n");

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("unstaged changes", result.Error, StringComparison.Ordinal);
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
    }

    [Fact]
    [Trait("Category", "PortableReview")]
    public void InvalidConfigStopsEvenWhenRuntimeInstructionsAreMissing()
    {
        using var layout = new SharedTargetStatusTests.StatusLayout("codex");
        var configPath = Path.Combine(layout.ProviderHome, "config.toml");
        var invalid = "personality = \"none\"\npersonality = \"friendly\"\n";
        File.WriteAllText(configPath, invalid);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("more than once", result.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(layout.RuntimePath));
        Assert.Equal(invalid, File.ReadAllText(configPath));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    [Trait("Category", "PortableReview")]
    public void LegacyRuntimeBlockReportsDriftWithoutReplacingIt()
    {
        using var layout = new SharedTargetStatusTests.StatusLayout("codex");
        layout.WriteRuntime();
        File.WriteAllText(Path.Combine(layout.ProviderHome, "config.toml"),
            "personality = \"none\"\n");
        var runtime = File.ReadAllBytes(layout.RuntimePath);

        var result = Run(layout);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(StatusOutput("different", "in_sync"), result.Output);
        Assert.Empty(result.Error);
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    private static string StatusOutput(string instructions, string config) =>
        $"codex/AGENTS.md   {instructions}{Environment.NewLine}codex/config.toml {config}{Environment.NewLine}";

    private static CommandResult Run(SharedTargetStatusTests.StatusLayout layout) =>
        TestApplication.Run("codex", "status", "--repo", layout.Repository,
            "--home", layout.ProviderHome);
}
