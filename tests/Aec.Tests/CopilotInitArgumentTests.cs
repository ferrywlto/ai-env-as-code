namespace Aec.Tests;

[Trait("Category", "Unit")]
public sealed class CopilotInitArgumentTests
{
    [Fact]
    public void RequiresAnExplicitRepository()
    {
        var result = TestApplication.Run(
            "copilot", "init", "--home", Path.GetFullPath("copilot-home"));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("init requires --repo", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsLegacyCodexHomeFlag()
    {
        var result = TestApplication.Run(
            "copilot", "init", "--repo", Path.GetFullPath("repository"),
            "--home", Path.GetFullPath("copilot-home"),
            "--codex-home", Path.GetFullPath("codex-home"));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Unknown argument: --codex-home", result.Error, StringComparison.Ordinal);
    }
}
