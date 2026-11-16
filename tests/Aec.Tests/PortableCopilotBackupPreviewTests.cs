namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class PortableCopilotBackupPreviewTests
{
    [Fact]
    public void UnchangedRuntimeProducesNoProposal()
    {
        using var layout = NewLayout();
        var runtime = File.ReadAllBytes(layout.RuntimePath);
        var head = layout.Head();

        var preview = PortableCopilotBackupPreview.Create(
            layout.Repository, layout.ProviderHome);

        Assert.Equal("copilot", preview.Snapshot.Provider);
        Assert.Equal(head, preview.Snapshot.Commit);
        Assert.Null(preview.Instructions.Section);
        Assert.Null(preview.Instructions.SourceRelativePath);
        AssertUnchanged(layout, head, runtime);
    }

    [Theory]
    [InlineData("Shared approval.", "shared", "environment/shared/instructions.md")]
    [InlineData("Local paths.", "platform", "platform")]
    [InlineData("Provider mechanics.", "provider",
        "environment/providers/copilot/overlay.md")]
    public void OneEditedSectionProducesOneReadOnlyProposal(
        string original,
        string section,
        string expectedPath)
    {
        using var layout = NewLayout();
        File.WriteAllText(layout.RuntimePath, File.ReadAllText(layout.RuntimePath)
            .Replace(original, "Reviewed revision.", StringComparison.Ordinal));
        var runtime = File.ReadAllBytes(layout.RuntimePath);
        var head = layout.Head();

        var preview = PortableCopilotBackupPreview.Create(
            layout.Repository, layout.ProviderHome);

        Assert.Equal(section, preview.Instructions.Section);
        Assert.Equal(expectedPath == "platform"
            ? $"environment/platforms/{RenderCommand.CurrentPlatform()}/policy.md"
            : expectedPath, preview.Instructions.SourceRelativePath);
        Assert.Equal("Reviewed revision.\n", preview.Instructions.ProposedContent);
        AssertUnchanged(layout, head, runtime);
    }

    [Fact]
    public void MultipleEditedSectionsStopWithoutWriting()
    {
        using var layout = NewLayout();
        File.WriteAllText(layout.RuntimePath, File.ReadAllText(layout.RuntimePath)
            .Replace("Shared approval.", "Shared revision.", StringComparison.Ordinal)
            .Replace("Local paths.", "Local revision.", StringComparison.Ordinal));
        var runtime = File.ReadAllBytes(layout.RuntimePath);
        var head = layout.Head();

        var error = Assert.Throws<InvalidDataException>(() =>
            PortableCopilotBackupPreview.Create(layout.Repository, layout.ProviderHome));

        Assert.Contains("multiple authored instruction sections", error.Message,
            StringComparison.Ordinal);
        AssertUnchanged(layout, head, runtime);
    }

    [Fact]
    public void UnstampedRuntimeStopsWithoutWriting()
    {
        using var layout = new SharedTargetStatusTests.StatusLayout("copilot");
        layout.WriteRuntime();
        var runtime = File.ReadAllBytes(layout.RuntimePath);
        var head = layout.Head();

        var error = Assert.Throws<InvalidOperationException>(() =>
            PortableCopilotBackupPreview.Create(layout.Repository, layout.ProviderHome));

        Assert.Contains("no last-applied target digest", error.Message,
            StringComparison.Ordinal);
        AssertUnchanged(layout, head, runtime);
    }

    private static SharedTargetStatusTests.StatusLayout NewLayout()
    {
        var layout = new SharedTargetStatusTests.StatusLayout("copilot");
        layout.WriteRuntime(portableBlock: true);
        return layout;
    }

    private static void AssertUnchanged(
        SharedTargetStatusTests.StatusLayout layout,
        string head,
        byte[] runtime)
    {
        Assert.Equal(head, layout.Head());
        Assert.Equal(string.Empty, layout.GitStatus());
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
    }
}
