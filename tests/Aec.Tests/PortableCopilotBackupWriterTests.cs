namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class PortableCopilotBackupWriterTests
{
    [Fact]
    public void RuntimeChangeAfterReviewStopsBeforeCanonicalWrite()
    {
        using var layout = NewLayout();
        File.WriteAllText(layout.RuntimePath, File.ReadAllText(layout.RuntimePath)
            .Replace("Shared approval.", "First revision.", StringComparison.Ordinal));
        var preview = PortableCopilotBackupPreview.Create(
            layout.Repository, layout.ProviderHome);
        File.WriteAllText(layout.RuntimePath, File.ReadAllText(layout.RuntimePath)
            .Replace("First revision.", "Later revision.", StringComparison.Ordinal));
        var oldHead = layout.Head();
        var oldSource = File.ReadAllBytes(layout.SharedSource);

        var error = Assert.Throws<InvalidOperationException>(() =>
            PortableCopilotBackupWriter.WriteAndCommit(preview));

        Assert.Contains("inputs changed after review", error.Message,
            StringComparison.Ordinal);
        Assert.Equal(oldHead, layout.Head());
        Assert.Equal(oldSource, File.ReadAllBytes(layout.SharedSource));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void HeadChangeAfterReviewStopsBeforeCanonicalWrite()
    {
        using var layout = NewLayout();
        File.WriteAllText(layout.RuntimePath, File.ReadAllText(layout.RuntimePath)
            .Replace("Shared approval.", "Shared revision.", StringComparison.Ordinal));
        var preview = PortableCopilotBackupPreview.Create(
            layout.Repository, layout.ProviderHome);
        layout.CommitEmpty();
        var newHead = layout.Head();
        var oldSource = File.ReadAllBytes(layout.SharedSource);

        var error = Assert.Throws<InvalidOperationException>(() =>
            PortableCopilotBackupWriter.WriteAndCommit(preview));

        Assert.Contains("inputs changed after review", error.Message,
            StringComparison.Ordinal);
        Assert.Equal(newHead, layout.Head());
        Assert.Equal(oldSource, File.ReadAllBytes(layout.SharedSource));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void DetachedHeadStopsBeforeCanonicalWrite()
    {
        using var layout = NewLayout();
        File.WriteAllText(layout.RuntimePath, File.ReadAllText(layout.RuntimePath)
            .Replace("Shared approval.", "Shared revision.", StringComparison.Ordinal));
        var preview = PortableCopilotBackupPreview.Create(
            layout.Repository, layout.ProviderHome);
        GitProcess.RunRequired(layout.Repository, "Could not detach fixture HEAD",
            "switch", "--detach", "--quiet");
        var oldHead = layout.Head();
        var oldSource = File.ReadAllBytes(layout.SharedSource);

        var error = Assert.Throws<InvalidOperationException>(() =>
            PortableCopilotBackupWriter.WriteAndCommit(preview));

        Assert.Contains("symbolic local Git branch", error.Message,
            StringComparison.Ordinal);
        Assert.Equal(oldHead, layout.Head());
        Assert.Equal(oldSource, File.ReadAllBytes(layout.SharedSource));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void NoChangeDoesNotCreateAnEmptyCommit()
    {
        using var layout = NewLayout();
        var oldHead = layout.Head();
        var runtime = File.ReadAllBytes(layout.RuntimePath);
        var preview = PortableCopilotBackupPreview.Create(
            layout.Repository, layout.ProviderHome);

        Assert.Equal(AecIdentifiers.Copilot, preview.Snapshot.Provider);
        Assert.Equal(oldHead, preview.Snapshot.Commit);
        Assert.Null(preview.Instructions.Section);
        Assert.Null(preview.Instructions.SourceRelativePath);
        Assert.Throws<InvalidOperationException>(() =>
            PortableCopilotBackupWriter.WriteAndCommit(preview));

        Assert.Equal(oldHead, layout.Head());
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    private static SharedTargetStatusTests.StatusLayout NewLayout()
    {
        var layout = new SharedTargetStatusTests.StatusLayout("copilot");
        layout.WriteRuntime(portableBlock: true);
        return layout;
    }
}
