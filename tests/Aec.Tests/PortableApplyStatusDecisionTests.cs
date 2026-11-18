using System.Text;

namespace Aec.Tests;

[Trait("Category", "Unit")]
public sealed class PortableApplyStatusDecisionTests
{
    [Theory]
    [InlineData(AecIdentifiers.Codex, false, "in_sync")]
    [InlineData(AecIdentifiers.Codex, true, "different")]
    [InlineData(AecIdentifiers.Copilot, false, "in_sync")]
    [InlineData(AecIdentifiers.Copilot, true, "different")]
    public void ApplyAndStatusCompareExactInstructionBytes(
        string provider, bool hasDrift, string expectedStatus)
    {
        var snapshot = CreateSnapshot(provider);
        var expected = provider == AecIdentifiers.Codex
            ? AecInstructionBlock.MergeForPortableTarget(
                snapshot.Target, snapshot.Repository, snapshot.Platform, includeChatGptGuidance: false)
            : CopilotInstructionBlock.MergeForPortableTarget(
                snapshot.Target, snapshot.Repository, snapshot.Platform);
        var current = snapshot with
        {
            Runtime = hasDrift ? [.. expected, (byte)'\n'] : expected.ToArray()
        };

        var apply = PortableApplyPreview.PlanInstructions(current);

        Assert.Equal(expected, apply.DesiredRuntime);
        Assert.Equal(snapshot.RuntimePath, apply.RuntimePath);
        Assert.Equal(hasDrift, apply.WouldReplace);
        Assert.Equal(expectedStatus, PortableEnvironmentStatus.ClassifyInstructions(current));
    }

    [Theory]
    [InlineData(AecIdentifiers.Codex)]
    [InlineData(AecIdentifiers.Copilot)]
    public void MissingInstructionsHaveStatusAndRequireInitializationBeforeApply(string provider)
    {
        var snapshot = CreateSnapshot(provider);

        Assert.Equal("missing", PortableEnvironmentStatus.ClassifyInstructions(snapshot));
        var error = Assert.Throws<InvalidOperationException>(() => PortableApplyPreview.PlanInstructions(snapshot));
        Assert.Contains("Run ordinary provider init first", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "personality = \"friendly\"\n", true, true, "missing")]
    [InlineData("[profile]\r\npersonality = \"none\"\r\n",
        "personality = \"friendly\"\r\n[profile]\r\npersonality = \"none\"\r\n", true, true, "missing")]
    [InlineData("\uFEFFpersonality  = 'none' # comment\r\n[profile]\r\n",
        "\uFEFFpersonality  = \"friendly\" # comment\r\n[profile]\r\n", true, false, "different")]
    [InlineData("\uFEFFpersonality  = 'friendly' # comment\r\n[profile]\r\n",
        "\uFEFFpersonality  = 'friendly' # comment\r\n[profile]\r\n", false, false, "in_sync")]
    public void CodexConfigPlanPreservesUnmanagedBytesAndDeterminesStatus(
        string? runtime, string desired, bool changed, bool inserted, string expectedStatus)
    {
        const string runtimePath = "config.toml";
        var runtimeBytes = runtime is null ? null : Encoding.UTF8.GetBytes(runtime);
        var update = CodexPersonalityConfig.PlanRuntimeUpdate(runtimeBytes, runtimePath, CodexPersonality.Friendly);

        Assert.Equal(Encoding.UTF8.GetBytes(desired), update.Content);
        Assert.Equal(changed, update.Changed);
        Assert.Equal(inserted, update.Inserted);

        var canonical = "personality = \"friendly\"\n"u8.ToArray();
        var config = new PortableCodexConfigPreview(
            runtimePath, canonical, runtimeBytes, update.Content, update.Changed, update.Inserted);
        Assert.Equal(expectedStatus, PortableEnvironmentStatus.ClassifyConfig(config));
    }

    [Fact]
    public void DuplicateRootPersonalityIsRejectedBeforePlanningAWrite()
    {
        var runtime = "personality = \"none\"\npersonality = \"pragmatic\"\n"u8.ToArray();

        Assert.Throws<InvalidDataException>(() =>
            CodexPersonalityConfig.PlanRuntimeUpdate(runtime, "config.toml", CodexPersonality.Friendly));
    }

    [Theory]
    [InlineData("in_sync", null, true)]
    [InlineData("in_sync", "in_sync", true)]
    [InlineData("in_sync", "missing", false)]
    [InlineData("in_sync", "different", false)]
    [InlineData("different", "in_sync", false)]
    [InlineData("missing", "in_sync", false)]
    public void StatusIsInSyncOnlyWhenAllManagedArtifactsMatch(
        string instructions, string? config, bool expected)
    {
        Assert.Equal(expected, new PortableStatusReport(instructions, config).InSync);
    }

    private static PortableRuntimeSnapshot CreateSnapshot(string provider)
    {
        var target = Encoding.UTF8.GetBytes(InstructionSections.Render("Shared.\n", "Policy.\n", "Overlay.\n"));
        return new PortableRuntimeSnapshot(
            Path.GetFullPath("decision-test-repository"), AecIdentifiers.MacOsArm64, provider,
            Path.GetFullPath(provider == AecIdentifiers.Codex ? "AGENTS.md" : "copilot-instructions.md"),
            "test-commit", target, Runtime: null, BlockVersion: null);
    }
}
