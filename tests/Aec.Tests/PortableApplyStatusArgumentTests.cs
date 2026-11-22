namespace Aec.Tests;

[Trait("Category", "Unit")]
public sealed class PortableApplyStatusArgumentTests
{
    [Fact]
    public void StatusRejectsRelativeRepositoryPaths()
    {
        var result = TestApplication.Run("status", "--repo", "relative/path");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("--repo must be an absolute path", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void StatusRejectsDuplicateRepositoryArguments()
    {
        var result = TestApplication.Run("status", "--repo", "first", "--repo", "second");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("--repo may be specified only once", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void StatusRejectsAnEmptyExplicitCodexHome()
    {
        var repository = Path.GetFullPath("unused-status-repository");
        var result = TestApplication.Run("status", "--repo", repository, "--codex-home", "");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("--codex-home requires a non-empty path", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("apply")]
    [InlineData("status")]
    public void CopilotRejectsCodexHome(string command)
    {
        var result = TestApplication.Run(command, "--repo", "unused-relative-repository",
            "--provider=copilot", "--codex-home", "unused-relative-home");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("--codex-home is not valid with --provider=copilot", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("apply")]
    [InlineData("status")]
    public void CopilotHomeRequiresCopilotProvider(string command)
    {
        var result = TestApplication.Run(command, "--repo", "unused-relative-repository",
            "--copilot-home", "unused-relative-home");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("--copilot-home requires --provider=copilot", result.Error, StringComparison.Ordinal);
    }
}
