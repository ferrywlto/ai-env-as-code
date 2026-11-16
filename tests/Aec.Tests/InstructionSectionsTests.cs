namespace Aec.Tests;

public sealed class InstructionSectionsTests
{
    [Fact]
    public void RoundTripsExactAuthoredTextAcrossThreeSections()
    {
        const string shared = "Approval first.\r\nKeep two spaces.  \n";
        const string platform = "Mac roots:\n- /Users/example/Work\n";
        const string provider = "Use provider model 🌱";

        var rendered = InstructionSections.Render(shared, platform, provider);
        var parsed = InstructionSections.Parse(rendered, hasPlatform: true, hasProvider: true);

        Assert.Equal(shared, parsed.Shared);
        Assert.Equal(platform, parsed.Platform);
        Assert.Equal(provider, parsed.Provider);
        Assert.Equal(rendered, InstructionSections.Render(parsed.Shared, parsed.Platform, parsed.Provider));
    }

    [Fact]
    public void MissingOptionalSectionsCreateNoPlaceholders()
    {
        var rendered = InstructionSections.Render("Shared", null, null);

        Assert.DoesNotContain("PLATFORM", rendered, StringComparison.Ordinal);
        Assert.DoesNotContain("PROVIDER", rendered, StringComparison.Ordinal);
        Assert.Equal(new InstructionSections.Parts("Shared", null, null),
            InstructionSections.Parse(rendered, hasPlatform: false, hasProvider: false));
    }

    [Fact]
    public void ProviderSectionMayExistWithoutAPlatformSection()
    {
        var rendered = InstructionSections.Render("Shared", null, "Provider");

        Assert.Equal(new InstructionSections.Parts("Shared", null, "Provider"),
            InstructionSections.Parse(rendered, hasPlatform: false, hasProvider: true));
    }

    [Fact]
    public void RejectsSourceMarkerCollisionAndUnexpectedRuntimeText()
    {
        Assert.Throws<InvalidDataException>(() =>
            InstructionSections.Render("<!-- AEC:SOURCE:SHARED:END -->", null, null));

        var rendered = InstructionSections.Render("Shared", "Platform", "Provider");
        Assert.Throws<InvalidDataException>(() =>
            InstructionSections.Parse(rendered + "\nUnowned text", true, true));
        Assert.Throws<InvalidDataException>(() =>
            InstructionSections.Parse(rendered.Replace("PLATFORM:BEGIN", "PROVIDER:BEGIN",
                StringComparison.Ordinal), true, true));
    }

    [Fact]
    public void RejectsStructuralChangeWhenExpectedSectionIsMissing()
    {
        var rendered = InstructionSections.Render("Shared", null, "Provider");

        Assert.Throws<InvalidDataException>(() =>
            InstructionSections.Parse(rendered, hasPlatform: true, hasProvider: true));
    }
}
