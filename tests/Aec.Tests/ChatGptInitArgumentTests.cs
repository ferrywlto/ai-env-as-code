namespace Aec.Tests;

[Trait("Category", "Unit")]
public sealed class ChatGptInitArgumentTests
{
    // A relative repository makes this fixture-free even if parsing accidentally
    // dispatches the command instead of reporting the expected diagnostic.
    private const string Repository = "unused-relative-repository";

    [Theory]
    [InlineData("--provider=other", "Unknown argument: --provider=other")]
    [InlineData("--provider", "Unknown argument")]
    public void RejectsUnsupportedProviderSyntax(string providerArgument, string expectedError)
    {
        AssertRejected(expectedError, "chatgpt", "init", "--repo", Repository, providerArgument);
    }

    [Fact]
    public void RejectsDuplicateRepository()
    {
        AssertRejected("--repo may be specified only once",
            "chatgpt", "init", "--repo", Repository, "--repo", Repository);
    }

    [Fact]
    public void ScaffoldRejectsHome()
    {
        AssertRejected("Unknown argument: --home",
            "chatgpt", "init", "--repo", Repository, "--home", "unused-relative-home");
    }

    private static void AssertRejected(string expectedError, params string[] args)
    {
        var result = TestApplication.Run(args);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(expectedError, result.Error, StringComparison.Ordinal);
    }
}
