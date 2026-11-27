namespace Aec.Tests;

[Trait("Category", "Unit")]
public sealed class ProviderFirstCommandTests
{
    [Theory]
    [InlineData("help")]
    [InlineData("--help")]
    [InlineData("-h")]
    public void HelpListsProviderCommandsAndExplainsUpgrade(string argument)
    {
        var result = TestApplication.Run(argument);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains("aec codex init --repo", result.Output, StringComparison.Ordinal);
        Assert.Contains("aec copilot init --repo", result.Output, StringComparison.Ordinal);
        Assert.Contains("aec chatgpt init --repo", result.Output, StringComparison.Ordinal);
        Assert.Contains("aec codex upgrade [--home", result.Output, StringComparison.Ordinal);
        Assert.Contains("only the bundled AEC skill guidance", result.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("--provider=", result.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("codex status --repo PATH", "status", "--repo", "relative")]
    [InlineData("copilot apply --repo PATH", "apply", "--provider=copilot", "--repo", "relative")]
    [InlineData("copilot init --repo PATH --home PATH --enroll-shared", "init", "--provider=copilot",
        "--copilot-home", "relative", "--enroll-shared", "--repo", "relative")]
    [InlineData("chatgpt init --repo PATH", "init", "--repo", "relative", "--provider=chatgpt")]
    [InlineData("codex upgrade [--home PATH]", "skill", "upgrade", "--codex-home", "relative")]
    [InlineData("codex uninstall [--home PATH]", "uninstall", "--codex-home", "relative")]
    public void LegacyCommandsFailWithTheReplacementBeforeResolvingPaths(
        string replacement, params string[] arguments)
    {
        var result = TestApplication.Run(arguments);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains($"Legacy command syntax. Use `aec {replacement}`.", result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ConflictingLegacyProvidersDoNotSuggestAReplacement()
    {
        var result = TestApplication.Run("init", "--provider=copilot", "--provider=chatgpt", "--repo", "relative");

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("Legacy command syntax. Use `aec help`", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("chatgpt", "status")]
    [InlineData("copilot", "upgrade")]
    [InlineData("copilot", "uninstall")]
    public void UnsupportedProviderCommandsFail(string provider, string command)
    {
        var result = TestApplication.Run(provider, command);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains($"Unknown {provider} command: {command}", result.Error, StringComparison.Ordinal);
    }
}
