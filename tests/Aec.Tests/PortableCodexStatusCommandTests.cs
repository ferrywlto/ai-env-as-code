namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class PortableCodexStatusCommandTests
{
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
    public void LegacyRuntimeBlockReportsDriftWithoutReplacingIt()
    {
        using var layout = new SharedTargetStatusTests.StatusLayout("codex");
        layout.WriteRuntime();
        File.WriteAllText(Path.Combine(layout.ProviderHome, "config.toml"),
            "personality = \"none\"\n");
        var runtime = File.ReadAllBytes(layout.RuntimePath);

        var result = Run(layout);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal($"codex/AGENTS.md   different{Environment.NewLine}" +
            $"codex/config.toml in_sync{Environment.NewLine}", result.Output);
        Assert.Empty(result.Error);
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    private static CommandResult Run(SharedTargetStatusTests.StatusLayout layout) =>
        TestApplication.Run("status", "--repo", layout.Repository,
            "--codex-home", layout.ProviderHome);
}
