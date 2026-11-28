namespace Aec.Tests;

[Trait("Category", "Unit")]
public sealed class VersionCommandTests
{
    [Fact]
    public void VersionReportsAssemblyMetadata()
    {
        var result = TestApplication.Run("version");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"3.0.0-alpha.1{Environment.NewLine}", result.Output);
        Assert.Empty(result.Error);
    }

    [Theory]
    [InlineData("--version", null, "Unknown command: --version")]
    [InlineData("version", "unexpected", "Unknown argument: unexpected")]
    public void RejectsUnsupportedVersionArguments(string command, string? argument, string expectedError)
    {
        var result = TestApplication.Run(argument is null ? [command] : [command, argument]);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Equal($"error: {expectedError}{Environment.NewLine}", result.Error);
    }
}
