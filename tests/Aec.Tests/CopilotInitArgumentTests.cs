namespace Aec.Tests;

[Trait("Category", "Unit")]
public sealed class CopilotInitArgumentTests
{
    [Fact]
    public void RequiresAnExplicitRepository()
    {
        var result = TestApplication.Run(
            "init", "--provider=copilot", "--copilot-home", Path.GetFullPath("copilot-home"));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("init requires --repo", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsCodexHomeAndCopilotHomeTogether()
    {
        var result = TestApplication.Run(
            "init", "--repo", Path.GetFullPath("repository"), "--provider=copilot",
            "--copilot-home", Path.GetFullPath("copilot-home"),
            "--codex-home", Path.GetFullPath("codex-home"));

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("--codex-home is not valid", result.Error, StringComparison.Ordinal);
    }
}
