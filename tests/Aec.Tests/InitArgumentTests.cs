namespace Aec.Tests;

[Trait("Category", "Unit")]
public sealed class InitArgumentTests
{
    public static TheoryData<string[], string> InvalidArguments => new()
    {
        { ["codex", "init"], "init requires --repo" },
        { ["codex", "init", "--repo", "relative"], "--repo must be an absolute path" },
        { ["codex", "init", "--repo"], "--repo requires a value" },
        { ["codex", "init", "--repo", ""], "--repo requires a non-empty path" },
        { ["codex", "init", "--repo", "first", "--repo", "second"], "--repo may be specified only once" },
        { ["codex", "apply", "--repo", "repository", "--force-path-change"], "Unknown argument: --force-path-change" },
        { ["chatgpt", "init", "--repo", "repository", "--force-path-change"],
            "Unknown argument: --force-path-change" },
        { ["codex", "init", "--repo", "repository", "--force-path-change", "--force-path-change"],
            "--force-path-change may be specified only once" },
        { ["codex", "init", "--repo", "repository", "--home", "first", "--home", "second"],
            "--home may be specified only once" },
        { ["codex", "init", "--unknown"], "Unknown argument: --unknown" },
        { ["codex", "init", "positional"], "Unknown argument: positional" }
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
