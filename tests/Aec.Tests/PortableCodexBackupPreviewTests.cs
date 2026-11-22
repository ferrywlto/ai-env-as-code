namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class PortableCodexBackupPreviewTests
{
    [Fact]
    public void OneInstructionSectionAndConfigDriftAreBothPreviewedWithoutWriting()
    {
        using var layout = NewLayout();
        var runtimeConfig = RuntimeConfigPath(layout);
        File.WriteAllText(layout.RuntimePath, File.ReadAllText(layout.RuntimePath)
            .Replace("Shared approval.", "Shared revision.", StringComparison.Ordinal));
        File.WriteAllText(runtimeConfig, "personality = \"pragmatic\"\n");
        var head = layout.Head();
        var instructions = File.ReadAllBytes(layout.RuntimePath);
        var config = File.ReadAllBytes(runtimeConfig);

        var preview = PortableCodexBackupPreview.Create(
            layout.Repository, layout.ProviderHome);

        Assert.True(preview.HasChanges);
        Assert.Equal("shared", preview.Instructions.Section);
        Assert.Equal("environment/shared/instructions.md",
            preview.Instructions.SourceRelativePath);
        Assert.Equal("Shared revision.\n", preview.Instructions.ProposedContent);
        Assert.True(preview.ConfigWouldChange);
        Assert.Equal("personality = \"pragmatic\"\n",
            System.Text.Encoding.UTF8.GetString(preview.ProposedCanonicalConfig));
        AssertUnchanged(layout, runtimeConfig, head, instructions, config);
    }

    [Fact]
    public void DirtyCanonicalConfigStopsBeforeProposingChanges()
    {
        using var layout = NewLayout();
        var runtimeConfig = RuntimeConfigPath(layout);
        File.WriteAllText(layout.CanonicalConfigPath, "personality = \"friendly\"\n");
        var head = layout.Head();
        var status = layout.GitStatus();
        var instructions = File.ReadAllBytes(layout.RuntimePath);
        var config = File.ReadAllBytes(runtimeConfig);

        var error = Assert.Throws<InvalidOperationException>(() =>
            PortableCodexBackupPreview.Create(layout.Repository, layout.ProviderHome));

        Assert.Contains("unstaged changes", error.Message, StringComparison.Ordinal);
        Assert.Equal(status, layout.GitStatus());
        Assert.Equal(head, layout.Head());
        Assert.Equal(instructions, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(config, File.ReadAllBytes(runtimeConfig));
    }

    private static SharedTargetStatusTests.StatusLayout NewLayout()
    {
        var layout = new SharedTargetStatusTests.StatusLayout("codex");
        layout.WriteRuntime(portableBlock: true);
        File.WriteAllText(RuntimeConfigPath(layout), "personality = \"none\"\n");
        return layout;
    }

    private static string RuntimeConfigPath(SharedTargetStatusTests.StatusLayout layout) =>
        Path.Combine(layout.ProviderHome, "config.toml");

    private static void AssertUnchanged(
        SharedTargetStatusTests.StatusLayout layout,
        string runtimeConfigPath,
        string head,
        byte[] instructions,
        byte[] config)
    {
        Assert.Equal(head, layout.Head());
        Assert.Equal(string.Empty, layout.GitStatus());
        Assert.Equal(instructions, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(config, File.ReadAllBytes(runtimeConfigPath));
    }
}
