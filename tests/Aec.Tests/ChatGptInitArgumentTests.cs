namespace Aec.Tests;

[Trait("Category", "Unit")]
public sealed class ChatGptInitArgumentTests
{
    // A relative repository makes this fixture-free even if parsing accidentally
    // dispatches the command instead of reporting the expected diagnostic.
    private const string Repository = "unused-relative-repository";

    [Theory]
    [InlineData("--provider=other", "Unsupported provider")]
    [InlineData("--provider", "Unknown argument")]
    public void RejectsUnsupportedProviderSyntax(string providerArgument, string expectedError)
    {
        AssertRejected(expectedError, "init", "--repo", Repository, providerArgument);
    }

    [Fact]
    public void RejectsDuplicateProvider()
    {
        AssertRejected("--provider may be specified only once",
            "init", "--repo", Repository, "--provider=chatgpt", "--provider=chatgpt");
    }

    [Fact]
    public void ProviderModeRejectsCodexHome()
    {
        AssertRejected("--codex-home is not valid with --provider=chatgpt",
            "init", "--repo", Repository, "--provider=chatgpt", "--codex-home", "unused-relative-home");
    }

    private static void AssertRejected(string expectedError, params string[] args)
    {
        var result = TestApplication.Run(args);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(expectedError, result.Error, StringComparison.Ordinal);
    }
}
