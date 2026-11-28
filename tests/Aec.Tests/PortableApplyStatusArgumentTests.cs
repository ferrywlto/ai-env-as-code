namespace Aec.Tests;

[Trait("Category", "Unit")]
public sealed class PortableApplyStatusArgumentTests
{
    [Fact]
    public void StatusRejectsRelativeRepositoryPaths()
    {
        var result = TestApplication.Run("codex", "status", "--repo", "relative/path");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("--repo must be an absolute path", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void StatusRejectsDuplicateRepositoryArguments()
    {
        var result = TestApplication.Run("codex", "status", "--repo", "first", "--repo", "second");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("--repo may be specified only once", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void StatusRejectsAnEmptyExplicitCodexHome()
    {
        var repository = Path.GetFullPath("unused-status-repository");
        var result = TestApplication.Run("codex", "status", "--repo", repository, "--home", "");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("--home requires a non-empty path", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("apply")]
    [InlineData("status")]
    public void CopilotRejectsLegacyCodexHomeFlag(string command)
    {
        var result = TestApplication.Run("copilot", command, "--repo", "unused-relative-repository", "--codex-home", "unused-relative-home");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("Unknown argument: --codex-home", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("apply")]
    [InlineData("status")]
    public void CodexRejectsLegacyCopilotHomeFlag(string command)
    {
        var result = TestApplication.Run("codex", command, "--repo", "unused-relative-repository",
            "--copilot-home", "unused-relative-home");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("Unknown argument: --copilot-home", result.Error, StringComparison.Ordinal);
    }
}
