using System.Text;

namespace Aec.Tests;

[Trait("Category", "Unit")]
public sealed class PortableApplyStatusDecisionTests
{
    [Fact]
    public void FilesystemRootContainsDescendantsForApplySafety()
    {
        var root = Path.GetPathRoot(Path.GetFullPath(Path.GetTempPath()))!;
        var descendant = Path.Combine(root, "aec-test-descendant", "AGENTS.md");

        Assert.True(ApplyCommand.IsPathInsideDirectory(root, descendant));
    }

    [Theory]
    [InlineData(AecIdentifiers.Codex, false, false, "in_sync")]
    [InlineData(AecIdentifiers.Codex, false, true, "different")]
    [InlineData(AecIdentifiers.Codex, true, false, "in_sync")]
    [InlineData(AecIdentifiers.Copilot, false, false, "in_sync")]
    [InlineData(AecIdentifiers.Copilot, false, true, "different")]
    public void ApplyAndStatusCompareExactInstructionBytes(
        string provider, bool includeChatGptGuidance, bool hasDrift, string expectedStatus)
    {
        var snapshot = CreateSnapshot(provider);
        var expected = provider == AecIdentifiers.Codex
            ? AecInstructionBlock.MergeForPortableTarget(
                snapshot.Target, snapshot.Repository, snapshot.Platform, includeChatGptGuidance)
            : CopilotInstructionBlock.MergeForPortableTarget(
                snapshot.Target, snapshot.Repository, snapshot.Platform);
        byte[] runtime = hasDrift ? [.. expected, (byte)'\n'] : expected.ToArray();
        var current = WithRuntime(snapshot, runtime);

        var apply = PortableApplyPreview.PlanInstructions(current);

        Assert.Equal(expected, apply.DesiredRuntime);
        Assert.Equal(snapshot.RuntimePath, apply.RuntimePath);
        Assert.Equal(hasDrift, apply.WouldReplace);
        Assert.Equal(expectedStatus, PortableEnvironmentStatus.ClassifyInstructions(current));
    }

    [Theory]
    [InlineData(AecIdentifiers.Codex, false, 9)]
    [InlineData(AecIdentifiers.Codex, true, 10)]
    [InlineData(AecIdentifiers.Copilot, false, 3)]
    public void LegacyRuntimeIsDifferentAndUpgradesToThePortableBlock(
        string provider, bool includeChatGptGuidance, int expectedVersion)
    {
        var snapshot = CreateSnapshot(provider);
        var legacy = provider switch
        {
            AecIdentifiers.Codex when includeChatGptGuidance =>
                AecInstructionBlock.MergeForChatGptProvider(snapshot.Target, snapshot.Repository),
            AecIdentifiers.Codex => AecInstructionBlock.Merge(snapshot.Target, snapshot.Repository),
            _ => CopilotInstructionBlock.Merge(snapshot.Target, snapshot.Repository)
        };
        var current = WithRuntime(snapshot, legacy);

        var preview = PortableApplyPreview.PlanInstructions(current);
        var desired = Encoding.UTF8.GetString(preview.DesiredRuntime);

        Assert.True(preview.WouldReplace);
        Assert.Equal("different", PortableEnvironmentStatus.ClassifyInstructions(current));
        Assert.Equal(expectedVersion, PortableRuntimeSnapshot.ValidateRuntimeBinding(
            snapshot.Repository, snapshot.Platform, provider, preview.DesiredRuntime));
        Assert.True(preview.DesiredRuntime.AsSpan().EndsWith(snapshot.Target));
        Assert.Equal(includeChatGptGuidance,
            desired.Contains("Manual ChatGPT instruction backups live under", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(AecIdentifiers.Codex)]
    [InlineData(AecIdentifiers.Copilot)]
    public void ApplyStatusRejectsUnboundInstructions(string provider)
    {
        var snapshot = CreateSnapshot(provider);

        var error = Assert.Throws<InvalidOperationException>(() =>
            WithRuntime(snapshot, "Local text without an AEC block.\n"u8.ToArray()));

        Assert.Contains("not bound to the selected AEC repository", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AecIdentifiers.Codex)]
    [InlineData(AecIdentifiers.Copilot)]
    public void ApplyStatusRejectsInstructionsBoundToAnotherPlatform(string provider)
    {
        var snapshot = CreateSnapshot(provider);
        var runtime = PortableApplyPreview.RenderDesiredRuntime(
            snapshot with { Platform = "windows-x64" });

        var error = Assert.Throws<InvalidOperationException>(() => WithRuntime(snapshot, runtime));

        Assert.Contains("bound to a different AEC platform", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CodexWriterRejectsCopilotPreview()
    {
        var snapshot = CreateSnapshot(AecIdentifiers.Copilot);
        var preview = PortableApplyPreview.PlanInstructions(
            WithRuntime(snapshot, PortableApplyPreview.RenderDesiredRuntime(snapshot)));

        Assert.Throws<ArgumentException>(() => PortableCodexWriter.Apply(preview));
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

    private static PortableRuntimeSnapshot WithRuntime(PortableRuntimeSnapshot snapshot, byte[] runtime) =>
        snapshot with
        {
            Runtime = runtime,
            BlockVersion = PortableRuntimeSnapshot.ValidateRuntimeBinding(
                snapshot.Repository, snapshot.Platform, snapshot.Provider, runtime)
        };

    private static PortableRuntimeSnapshot CreateSnapshot(string provider)
    {
        var target = Encoding.UTF8.GetBytes(InstructionSections.Render("Shared.\n", "Policy.\n", "Overlay.\n"));
        return new PortableRuntimeSnapshot(
            Path.GetFullPath("decision-test-repository"), AecIdentifiers.MacOsArm64, provider,
            Path.GetFullPath(provider == AecIdentifiers.Codex ? "AGENTS.md" : "copilot-instructions.md"),
            "test-commit", target, Runtime: null, BlockVersion: null);
    }
}
