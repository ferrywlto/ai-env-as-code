namespace Aec.Tests;

public sealed class InstructionComposerTests
{
    [Fact]
    public void RendersSectionsInOrderWithExactlyTwoLfSeparators()
    {
        var result = InstructionComposer.Render("AEC", "Shared", "Platform", "Provider");

        Assert.Equal("AEC\n\nShared\n\nPlatform\n\nProvider", result);
    }

    [Fact]
    public void OptionalSectionsCanBeOmitted()
    {
        Assert.Equal("AEC\n\nShared", InstructionComposer.Render("AEC", "Shared"));
    }

    [Theory]
    [InlineData(null, null, "AEC\n\nShared")]
    [InlineData("", "", "AEC\n\nShared")]
    [InlineData(null, "Provider", "AEC\n\nShared\n\nProvider")]
    [InlineData("", "Provider", "AEC\n\nShared\n\nProvider")]
    [InlineData("Platform", null, "AEC\n\nShared\n\nPlatform")]
    [InlineData("Platform", "", "AEC\n\nShared\n\nPlatform")]
    public void OmitsNullOrEmptyOptionalSectionsWithoutExtraSeparators(
        string? platformPolicy, string? providerPolicy, string expected)
    {
        Assert.Equal(expected, InstructionComposer.Render("AEC", "Shared", platformPolicy, providerPolicy));
    }

    [Theory]
    [InlineData("", "", null, null, "")]
    [InlineData("", "", "", "", "")]
    [InlineData("AEC", "", null, null, "AEC")]
    [InlineData("", "Shared", null, null, "Shared")]
    [InlineData("", "", "Platform", null, "Platform")]
    [InlineData("", "", null, "Provider", "Provider")]
    [InlineData("AEC", "", "Platform", "Provider", "AEC\n\nPlatform\n\nProvider")]
    [InlineData("", "Shared", "Platform", "Provider", "Shared\n\nPlatform\n\nProvider")]
    public void OmitsEmptyRequiredSectionsWithoutLeadingOrTrailingSeparators(
        string aecBlock, string sharedPolicy, string? platformPolicy, string? providerPolicy, string expected)
    {
        Assert.Equal(expected, InstructionComposer.Render(aecBlock, sharedPolicy, platformPolicy, providerPolicy));
    }

    [Fact]
    public void PreservesWhitespaceOnlySectionsInEveryPosition()
    {
        Assert.Equal(" \n\n\t\n\n\r\n\n\n  ", InstructionComposer.Render(" ", "\t", "\r\n", "  "));
    }

    [Fact]
    public void PreservesEverySuppliedCharacterIncludingMixedLineEndingsAndUnicode()
    {
        const string aecBlock = "\uFEFF  <!-- AEC -->\r\n\t";
        const string sharedPolicy = "\n 共用政策 🌱\r\nKeep trailing spaces.  \n";
        const string platformPolicy = "\tPlatform\rline\r\n";
        const string providerPolicy = "\n Provider café e\u0301\t\r\n";

        var result = InstructionComposer.Render(aecBlock, sharedPolicy, platformPolicy, providerPolicy);

        Assert.Equal(
            "\uFEFF  <!-- AEC -->\r\n\t\n\n\n 共用政策 🌱\r\nKeep trailing spaces.  \n\n\n\tPlatform\rline\r\n\n\n\n Provider café e\u0301\t\r\n",
            result);
    }

    [Fact]
    public void RejectsNullAecBlock()
    {
        Assert.Throws<ArgumentNullException>(() => InstructionComposer.Render(null!, "Shared"));
    }

    [Fact]
    public void RejectsNullSharedPolicy()
    {
        Assert.Throws<ArgumentNullException>(() => InstructionComposer.Render("AEC", null!));
    }

    [Fact]
    public void RepeatedRenderingIsDeterministicAndIndependentOfOtherCalls()
    {
        const string expected = "AEC\n\nShared\n\nPlatform\n\nProvider";

        Assert.Equal(expected, InstructionComposer.Render("AEC", "Shared", "Platform", "Provider"));
        Assert.Equal("Other", InstructionComposer.Render("", "Other"));
        Assert.Equal(expected, InstructionComposer.Render("AEC", "Shared", "Platform", "Provider"));
    }

    [Fact]
    public void TwoProvidersReuseTheSameSharedPolicyWithDistinctOverlays()
    {
        const string sharedPolicy = "# Shared policy\r\nPreserve this exactly.  ";

        var codex = InstructionComposer.Render("AEC", sharedPolicy, "macOS", "Codex overlay");
        var copilot = InstructionComposer.Render("AEC", sharedPolicy, "macOS", "Copilot overlay");

        Assert.Equal("AEC\n\n# Shared policy\r\nPreserve this exactly.  \n\nmacOS\n\nCodex overlay", codex);
        Assert.Equal("AEC\n\n# Shared policy\r\nPreserve this exactly.  \n\nmacOS\n\nCopilot overlay", copilot);
    }
}
