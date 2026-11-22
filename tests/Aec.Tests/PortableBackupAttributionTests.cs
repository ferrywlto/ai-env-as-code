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

    [Theory]
    [InlineData(AecIdentifiers.Codex, false, 9, 7)]
    [InlineData(AecIdentifiers.Codex, true, 10, 8)]
    [InlineData(AecIdentifiers.Copilot, false, 3, 2)]
    public void EarlierPortableBlockWithoutDigestIsRejected(
        string provider, bool includeChatGptGuidance, int stampedVersion, int earlierVersion)
    {
        var snapshot = CreateAppliedSnapshot(provider, includeChatGptGuidance);
        var runtime = snapshot.Runtime!;
        var digest = provider == AecIdentifiers.Codex
            ? AecInstructionBlock.ReadRepositoryBinding(runtime)!.TargetDigest!
            : CopilotInstructionBlock.ReadManagedBinding(runtime)!.TargetDigest!;
        var unstamped = Encoding.UTF8.GetString(runtime)
            .Replace($"version={stampedVersion}", $"version={earlierVersion}", StringComparison.Ordinal)
            .Replace($"The last-applied target SHA-256 is `{digest}`.\n", "", StringComparison.Ordinal);
        var unstampedBytes = Encoding.UTF8.GetBytes(unstamped);
        var version = PortableRuntimeSnapshot.ValidateRuntimeBinding(
            snapshot.Repository, snapshot.Platform, provider, unstampedBytes);
        Assert.Equal(earlierVersion, version);

        var error = Assert.Throws<InvalidOperationException>(() =>
            PortableBackupAttributionPreview.Create(snapshot with
            {
                Runtime = unstampedBytes,
                BlockVersion = version
            }));

        Assert.Contains("no last-applied target digest", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AecIdentifiers.Codex, "source-marker")]
    [InlineData(AecIdentifiers.Codex, "section-boundary")]
    [InlineData(AecIdentifiers.Codex, "control-block")]
    [InlineData(AecIdentifiers.Codex, "outside-text")]
    [InlineData(AecIdentifiers.Copilot, "source-marker")]
    [InlineData(AecIdentifiers.Copilot, "section-boundary")]
    [InlineData(AecIdentifiers.Copilot, "control-block")]
    [InlineData(AecIdentifiers.Copilot, "outside-text")]
    public void UnattributableRuntimeChangesAreRejected(string provider, string mutation)
    {
        var snapshot = CreateAppliedSnapshot(provider);
        var runtime = Encoding.UTF8.GetString(snapshot.Runtime!);
        var edited = mutation switch
        {
            "source-marker" => runtime.Replace("Shared source.",
                "Shared source.\n<!-- AEC:SOURCE:SHARED:BEGIN -->", StringComparison.Ordinal),
            "section-boundary" => runtime.Replace(
                "<!-- AEC:SOURCE:PLATFORM:BEGIN -->",
                "<!-- AEC:SOURCE:PROVIDER:BEGIN -->", StringComparison.Ordinal),
            "control-block" => runtime.Replace(
                "Treat its Git commit history as the source of truth.",
                "Treat its Git commit history as one source of truth.", StringComparison.Ordinal),
            "outside-text" => runtime + "\nUnowned runtime text.",
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };

        Assert.Throws<InvalidDataException>(() =>
            PortableBackupAttributionPreview.Create(snapshot with { Runtime = Encoding.UTF8.GetBytes(edited) }));
    }

    [Fact]
    public void SourceWriterRequiresAnAttributedChange()
    {
        var snapshot = CreateAppliedSnapshot(AecIdentifiers.Codex);
        var attribution = PortableBackupAttributionPreview.Create(snapshot);

        var error = Assert.Throws<InvalidOperationException>(() =>
            PortableBackupSourceWriter.Write(snapshot, attribution));

        Assert.Contains("requires one reviewed runtime instruction section change",
            error.Message, StringComparison.Ordinal);
    }

    private static PortableRuntimeSnapshot CreateAppliedSnapshot(string provider, bool includeChatGptGuidance = false)
    {
        var repository = Path.GetFullPath("attribution-test-repository");
        var runtimePath = Path.GetFullPath(
            provider == AecIdentifiers.Codex ? "AGENTS.md" : "copilot-instructions.md");
        var target = Encoding.UTF8.GetBytes(InstructionSections.Render(
            "Shared source.\n", "Platform policy.\n", "Provider overlay.\n"));
        var snapshot = new PortableRuntimeSnapshot(
            repository, AecIdentifiers.MacOsArm64, provider, runtimePath, "test-commit", target, null, null);
        var runtime = includeChatGptGuidance
            ? AecInstructionBlock.MergeForPortableTarget(target, repository, snapshot.Platform, includeChatGptGuidance)
            : PortableApplyPreview.RenderDesiredRuntime(snapshot);
        var version = PortableRuntimeSnapshot.ValidateRuntimeBinding(repository, snapshot.Platform, provider, runtime);
        return snapshot with { Runtime = runtime, BlockVersion = version };
    }
}
