using System.Text;

namespace Aec.Tests;

[Trait("Category", "Unit")]
public sealed class PortableBackupAttributionTests
{
    [Theory]
    [InlineData(AecIdentifiers.Codex)]
    [InlineData(AecIdentifiers.Copilot)]
    public void UnchangedRuntimeProducesNoProposal(string provider)
    {
        var snapshot = CreateAppliedSnapshot(provider);

        var proposal = PortableBackupAttributionPreview.Create(snapshot);

        Assert.Null(proposal.Section);
        Assert.Null(proposal.SourceRelativePath);
        Assert.Null(proposal.ProposedContent);
    }

    [Theory]
    [InlineData(AecIdentifiers.Codex, "Shared source.", "shared", "environment/shared/instructions.md")]
    [InlineData(AecIdentifiers.Copilot, "Shared source.", "shared", "environment/shared/instructions.md")]
    [InlineData(AecIdentifiers.Codex, "Platform policy.", "platform", "environment/platforms/macos-arm64/policy.md")]
    [InlineData(AecIdentifiers.Copilot, "Platform policy.", "platform", "environment/platforms/macos-arm64/policy.md")]
    [InlineData(AecIdentifiers.Codex, "Provider overlay.", "provider", "environment/providers/codex/overlay.md")]
    [InlineData(AecIdentifiers.Copilot, "Provider overlay.", "provider", "environment/providers/copilot/overlay.md")]
    public void OneEditedSectionProposesItsAuthoredSource(
        string provider, string originalText, string section, string sourcePath)
    {
        var snapshot = CreateAppliedSnapshot(provider);
        var runtime = Encoding.UTF8.GetString(snapshot.Runtime!);
        var edited = runtime.Replace(originalText, "Reviewed change.", StringComparison.Ordinal);

        var proposal = PortableBackupAttributionPreview.Create(
            snapshot with { Runtime = Encoding.UTF8.GetBytes(edited) });

        Assert.Equal(section, proposal.Section);
        Assert.Equal(sourcePath, proposal.SourceRelativePath);
        Assert.Equal("Reviewed change.\n", proposal.ProposedContent);
    }

    [Theory]
    [InlineData(AecIdentifiers.Codex)]
    [InlineData(AecIdentifiers.Copilot)]
    public void EditsToMultipleSectionsAreRejected(string provider)
    {
        var snapshot = CreateAppliedSnapshot(provider);
        var edited = Encoding.UTF8.GetString(snapshot.Runtime!)
            .Replace("Shared source.", "Shared revision.", StringComparison.Ordinal)
            .Replace("Provider overlay.", "Provider revision.", StringComparison.Ordinal);

        var error = Assert.Throws<InvalidDataException>(() =>
            PortableBackupAttributionPreview.Create(
                snapshot with { Runtime = Encoding.UTF8.GetBytes(edited) }));

        Assert.Contains("multiple authored instruction sections", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AecIdentifiers.Codex)]
    [InlineData(AecIdentifiers.Copilot)]
    public void OlderAppliedTargetIsRejected(string provider)
    {
        var snapshot = CreateAppliedSnapshot(provider);
        var currentTarget = Encoding.UTF8.GetBytes(InstructionSections.Render(
            "New committed shared source.\n", "Platform policy.\n", "Provider overlay.\n"));

        var error = Assert.Throws<InvalidOperationException>(() =>
            PortableBackupAttributionPreview.Create(snapshot with { Target = currentTarget }));

        Assert.Contains("last applied from an older target", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AecIdentifiers.Codex)]
    [InlineData(AecIdentifiers.Copilot)]
    public void RuntimeWithoutAppliedTargetDigestIsRejected(string provider)
    {
        var snapshot = CreateAppliedSnapshot(provider);
        var unstampedRuntime = provider == AecIdentifiers.Codex
            ? AecInstructionBlock.Merge(snapshot.Target, snapshot.Repository)
            : CopilotInstructionBlock.Merge(snapshot.Target, snapshot.Repository);

        var error = Assert.Throws<InvalidOperationException>(() =>
            PortableBackupAttributionPreview.Create(snapshot with { Runtime = unstampedRuntime }));

        Assert.Contains("no last-applied target digest", error.Message, StringComparison.Ordinal);
    }

    private static PortableRuntimeSnapshot CreateAppliedSnapshot(string provider)
    {
        var repository = Path.GetFullPath("attribution-test-repository");
        var runtimePath = Path.GetFullPath(
            provider == AecIdentifiers.Codex ? "AGENTS.md" : "copilot-instructions.md");
        var target = Encoding.UTF8.GetBytes(InstructionSections.Render(
            "Shared source.\n", "Platform policy.\n", "Provider overlay.\n"));
        var snapshot = new PortableRuntimeSnapshot(
            repository, AecIdentifiers.MacOsArm64, provider, runtimePath, "test-commit", target, null, null);
        return snapshot with { Runtime = PortableApplyPreview.RenderDesiredRuntime(snapshot) };
    }
}
