namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class PortableCodexBackupPreviewTests
{
    [Fact]
    public void UnchangedRuntimeProducesNoProposedChanges()
    {
        using var layout = NewLayout();
        var runtimeConfig = RuntimeConfigPath(layout);
        var head = layout.Head();
        var instructions = File.ReadAllBytes(layout.RuntimePath);
        var config = File.ReadAllBytes(runtimeConfig);

        var preview = PortableCodexBackupPreview.Create(
            layout.Repository, layout.ProviderHome);

        Assert.False(preview.HasChanges);
        Assert.Null(preview.Instructions.Section);
        Assert.False(preview.ConfigWouldChange);
        Assert.Equal(config, preview.ProposedCanonicalConfig);
        AssertUnchanged(layout, runtimeConfig, head, instructions, config);
    }

    [Fact]
    public void ConfigOnlyDriftChangesOnlyTheManagedCanonicalValue()
    {
        using var layout = NewLayout();
        var runtimeConfig = RuntimeConfigPath(layout);
        File.WriteAllText(runtimeConfig, "personality = 'friendly' # runtime\n[other]\nkey = 1\n");
        var head = layout.Head();
        var instructions = File.ReadAllBytes(layout.RuntimePath);
        var config = File.ReadAllBytes(runtimeConfig);

        var preview = PortableCodexBackupPreview.Create(
            layout.Repository, layout.ProviderHome);

        Assert.True(preview.HasChanges);
        Assert.Null(preview.Instructions.Section);
        Assert.True(preview.ConfigWouldChange);
        Assert.Equal("personality = \"friendly\"\n",
            System.Text.Encoding.UTF8.GetString(preview.ProposedCanonicalConfig));
        Assert.Equal("personality = \"none\"\n",
            File.ReadAllText(layout.CanonicalConfigPath));
        AssertUnchanged(layout, runtimeConfig, head, instructions, config);
    }

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
    public void MissingRuntimePersonalityStopsWithoutWriting()
    {
        using var layout = NewLayout();
        var runtimeConfig = RuntimeConfigPath(layout);
        File.WriteAllText(runtimeConfig, "[other]\nkey = 1\n");
        var head = layout.Head();
        var instructions = File.ReadAllBytes(layout.RuntimePath);
        var config = File.ReadAllBytes(runtimeConfig);

        var error = Assert.Throws<InvalidOperationException>(() =>
            PortableCodexBackupPreview.Create(layout.Repository, layout.ProviderHome));

        Assert.Contains("does not declare root `personality`", error.Message,
            StringComparison.Ordinal);
        AssertUnchanged(layout, runtimeConfig, head, instructions, config);
    }

    [Fact]
    public void MissingRuntimeConfigStopsWithTheSameClearError()
    {
        using var layout = NewLayout();
        var runtimeConfig = RuntimeConfigPath(layout);
        File.Delete(runtimeConfig);
        var head = layout.Head();
        var instructions = File.ReadAllBytes(layout.RuntimePath);

        var error = Assert.Throws<InvalidOperationException>(() =>
            PortableCodexBackupPreview.Create(layout.Repository, layout.ProviderHome));

        Assert.Contains("does not declare root `personality`", error.Message,
            StringComparison.Ordinal);
        Assert.False(File.Exists(runtimeConfig));
        Assert.Equal(instructions, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(head, layout.Head());
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void MultipleInstructionSectionChangesStopEvenWhenConfigAlsoDiffers()
    {
        using var layout = NewLayout();
        var runtimeConfig = RuntimeConfigPath(layout);
        File.WriteAllText(layout.RuntimePath, File.ReadAllText(layout.RuntimePath)
            .Replace("Shared approval.", "Shared revision.", StringComparison.Ordinal)
            .Replace("Local paths.", "Local revision.", StringComparison.Ordinal));
        File.WriteAllText(runtimeConfig, "personality = \"friendly\"\n");
        var head = layout.Head();
        var instructions = File.ReadAllBytes(layout.RuntimePath);
        var config = File.ReadAllBytes(runtimeConfig);

        var error = Assert.Throws<InvalidDataException>(() =>
            PortableCodexBackupPreview.Create(layout.Repository, layout.ProviderHome));

        Assert.Contains("multiple authored instruction sections", error.Message,
            StringComparison.Ordinal);
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

        Assert.Throws<InvalidOperationException>(() =>
            PortableCodexBackupPreview.Create(layout.Repository, layout.ProviderHome));

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
