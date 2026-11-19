namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class PortableCopilotBackupPreviewTests
{
    [Fact]
    public void LegacyRuntimeWithoutDigestStopsWithoutWriting()
    {
        using var layout = new SharedTargetStatusTests.StatusLayout("copilot");
        layout.WriteRuntime();
        var runtime = File.ReadAllBytes(layout.RuntimePath);
        var head = layout.Head();

        var error = Assert.Throws<InvalidOperationException>(() =>
            PortableCopilotBackupPreview.Create(layout.Repository, layout.ProviderHome));

        Assert.Contains("no last-applied target digest", error.Message,
            StringComparison.Ordinal);
        Assert.Equal(head, layout.Head());
        Assert.Equal(string.Empty, layout.GitStatus());
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
    }
}
