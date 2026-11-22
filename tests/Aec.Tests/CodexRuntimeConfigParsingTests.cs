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
    [InlineData("# Machine-owned settings\r\nmodel = \"another-model\"\r\n" +
        "personality    =    \"none\" # comments may contain \"\"\"\r\n[features]\r\nexample = false\r\n", false)]
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

    [Theory]
    [InlineData("[personality]\nvalue = \"nested\"\n")]
    [InlineData("[[personality]]\nvalue = \"nested\"\n")]
    [InlineData("[\"personality\".options]\nvalue = \"nested\"\n")]
    [InlineData("[features]\nenabled = true\n[personality]\nvalue = \"nested\"\n")]
    public void PersonalityTableConflictsWithRootInsertion(string config)
    {
        var error = Assert.Throws<InvalidDataException>(() =>
            CodexPersonalityConfig.PlanRuntimeUpdate(Encoding.UTF8.GetBytes(config), "config.toml", CodexPersonality.Friendly));

        Assert.Contains("conflicts with personality", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeConfigRejectsLoneCarriageReturns()
    {
        var config = "model = \"gpt-test\"\r[projects.\"/tmp/example\"]\r"u8.ToArray();

        var error = Assert.Throws<InvalidDataException>(() =>
            CodexPersonalityConfig.PlanRuntimeUpdate(config, "config.toml", CodexPersonality.Friendly));

        Assert.Contains("lone carriage return", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("friendly")]
    [InlineData("pragmatic")]
    public void SupportedPersonalityIsReadFromBothConfigs(string value)
    {
        var config = Encoding.UTF8.GetBytes($"personality = \"{value}\"\n");
        var personality = Enum.Parse<CodexPersonality>(value, ignoreCase: true);

        Assert.Equal(personality, CodexPersonalityConfig.ReadCanonical(config, "canonical.toml"));
        Assert.Equal(personality, CodexPersonalityConfig.ReadRuntime(config, "config.toml"));
    }

    [Theory]
    [InlineData("model = \"local-model\"\n[features]\nexample = true\n")]
    [InlineData("[features]\npersonality = \"none\"\n")]
    public void OnlyARootPersonalityCountsAsManaged(string config)
    {
        Assert.Null(CodexPersonalityConfig.ReadRuntime(Encoding.UTF8.GetBytes(config), "config.toml"));
    }

    [Theory]
    [InlineData("model = \"local-model\"\npersonality = \"none\"\n", "contains an unmanaged setting")]
    [InlineData("# no managed value\n", "does not declare personality")]
    [InlineData("personality = \"none\"\npersonality = \"friendly\"\n", "declares personality more than once")]
    [InlineData("personality = \"playful\"\n", "has unsupported personality")]
    [InlineData("personality\u00A0=\u00A0\"none\"\n", "contains an ambiguous root key")]
    [InlineData("# invalid\0comment\npersonality = \"none\"\n", "contains a forbidden control character")]
    public void InvalidCanonicalConfigIsRejected(string config, string expectedError)
    {
        var error = Assert.Throws<InvalidDataException>(() =>
            CodexPersonalityConfig.ReadCanonical(Encoding.UTF8.GetBytes(config), "canonical.toml"));

        Assert.Contains($"Canonical config {expectedError}", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CanonicalConfigRejectsInvalidUtf8()
    {
        var error = Assert.Throws<InvalidDataException>(() =>
            CodexPersonalityConfig.ReadCanonical([0xFF], "canonical.toml"));

        Assert.Contains("Canonical config is not valid UTF-8", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RuntimeConfigRejectsUnsupportedPersonality()
    {
        var error = Assert.Throws<InvalidDataException>(() =>
            CodexPersonalityConfig.ReadRuntime("personality = \"playful\"\n"u8.ToArray(), "config.toml"));

        Assert.Contains("Runtime config has unsupported personality", error.Message, StringComparison.Ordinal);
    }
}
