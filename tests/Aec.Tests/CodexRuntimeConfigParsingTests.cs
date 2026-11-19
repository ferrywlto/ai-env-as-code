using System.Text;

namespace Aec.Tests;

[Trait("Category", "Unit")]
public sealed class CodexRuntimeConfigParsingTests
{
    [Theory]
    [InlineData("\"personality\" = \"none\"\n", false)]
    [InlineData("instructions = \"\"\"\npersonality = \"friendly\"\n\"\"\"\npersonality = \"none\"\n", false)]
    [InlineData("matrix = [\n  [1, 2],\n  [3, 4],\n]\npersonality = \"none\"\n", false)]
    [InlineData("\"personal\\u0069ty\" = \"none\"\n", false)]
    [InlineData("personality = \"frien\\u0064ly\"\n", true)]
    [InlineData("'machine\\path' = \"local\"\npersonality = \"none\"\n", false)]
    public void MatchingRootPersonalityLeavesRuntimeBytesUnchanged(string config, bool expectsFriendly)
    {
        var runtime = Encoding.UTF8.GetBytes(config);
        var desired = expectsFriendly ? CodexPersonality.Friendly : CodexPersonality.None;

        var update = CodexPersonalityConfig.PlanRuntimeUpdate(runtime, "config.toml", desired);

        Assert.False(update.Changed);
        Assert.False(update.Inserted);
        Assert.Equal(Encoding.UTF8.GetBytes(config), update.Content);
    }

    [Fact]
    public void MultilineManagedValueIsRejectedAsAmbiguous()
    {
        var runtime = "personality = \"\"\"none\"\"\"\n"u8.ToArray();

        var error = Assert.Throws<InvalidDataException>(() =>
            CodexPersonalityConfig.PlanRuntimeUpdate(runtime, "config.toml", CodexPersonality.None));

        Assert.Contains("ambiguous personality declaration", error.Message, StringComparison.Ordinal);
    }
}
