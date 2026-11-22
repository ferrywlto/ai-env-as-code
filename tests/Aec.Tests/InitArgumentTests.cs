namespace Aec.Tests;

[Trait("Category", "Unit")]
public sealed class InitArgumentTests
{
    public static TheoryData<string[], string> InvalidArguments => new()
    {
        { ["init"], "init requires --repo" },
        { ["init", "--repo", "relative"], "--repo must be an absolute path" },
        { ["init", "--repo"], "--repo requires a value" },
        { ["init", "--repo", ""], "--repo requires a non-empty path" },
        { ["init", "--repo", "first", "--repo", "second"], "--repo may be specified only once" },
        { ["apply", "--repo", "repository", "--force-path-change"], "Unknown argument: --force-path-change" },
        { ["init", "--repo", "repository", "--provider=chatgpt", "--force-path-change"],
            "--force-path-change is not valid with --provider=chatgpt" },
        { ["init", "--repo", "repository", "--force-path-change", "--force-path-change"],
            "--force-path-change may be specified only once" },
        { ["init", "--repo", "repository", "--codex-home", "first", "--codex-home", "second"],
            "--codex-home may be specified only once" },
        { ["init", "--unknown"], "Unknown argument: --unknown" },
        { ["init", "positional"], "Unknown argument: positional" }
    };

    [Theory]
    [MemberData(nameof(InvalidArguments))]
    public void RejectsInvalidArguments(string[] arguments, string reason)
    {
        var result = TestApplication.Run(arguments);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(reason, result.Error, StringComparison.Ordinal);
    }
}

[Collection(ProcessStateTestGroup.Name)]
[Trait("Category", "Unit")]
public sealed class ProviderHomeResolutionTests
{
    [Theory]
    [InlineData("CODEX_HOME", false)]
    [InlineData("COPILOT_HOME", false)]
    [InlineData("COPILOT_HOME", true)]
    public void ResolvesProviderHome(string environmentVariable, bool passExplicitHome)
    {
        var home = Path.GetFullPath("unused-provider-home");
        var previous = Environment.GetEnvironmentVariable(environmentVariable);
        var explicitHome = passExplicitHome ? home : null;

        try
        {
            Environment.SetEnvironmentVariable(
                environmentVariable, passExplicitHome ? "relative-home" : home);

            var resolved = environmentVariable == "CODEX_HOME"
                ? AecApplication.ResolveCodexHome(explicitHome)
                : AecApplication.ResolveCopilotHome(explicitHome);

            Assert.Equal(home, resolved);
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentVariable, previous);
        }
    }
}
