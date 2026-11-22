namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class PortableCodexBackupWriterTests
{
    [Fact]
    public void ExistingStagedChangesStopBeforeCanonicalWrite()
    {
        using var layout = NewLayout();
        File.WriteAllText(RuntimeConfigPath(layout), "personality = \"friendly\"\n");
        var preview = PortableCodexBackupPreview.Create(
            layout.Repository, layout.ProviderHome);
        File.WriteAllText(Path.Combine(layout.Repository, "note.txt"), "Keep staged.\n");
        GitProcess.RunRequired(layout.Repository, "Could not stage fixture note",
            "add", "--", "note.txt");
        var oldHead = layout.Head();
        var oldConfig = File.ReadAllBytes(layout.CanonicalConfigPath);

        var error = Assert.Throws<InvalidOperationException>(() =>
            PortableCodexBackupWriter.WriteAndCommit(preview));

        Assert.Contains("already has staged changes", error.Message,
            StringComparison.Ordinal);
        Assert.Equal(oldHead, layout.Head());
        Assert.Equal(oldConfig, File.ReadAllBytes(layout.CanonicalConfigPath));
        Assert.Equal("note.txt\n", GitProcess.RunRequired(layout.Repository,
            "Could not inspect staged paths", "diff", "--cached", "--name-only").Output);
    }

    [Fact]
    public void RuntimeConfigChangeAfterReviewStopsBeforeCanonicalWrite()
    {
        using var layout = NewLayout();
        File.WriteAllText(RuntimeConfigPath(layout), "personality = \"friendly\"\n");
        var preview = PortableCodexBackupPreview.Create(
            layout.Repository, layout.ProviderHome);
        File.WriteAllText(RuntimeConfigPath(layout), "personality = \"pragmatic\"\n");
        var oldHead = layout.Head();
        var oldConfig = File.ReadAllBytes(layout.CanonicalConfigPath);

        var error = Assert.Throws<InvalidOperationException>(() =>
            PortableCodexBackupWriter.WriteAndCommit(preview));

        Assert.Contains("inputs changed after review", error.Message,
            StringComparison.Ordinal);
        Assert.Equal(oldHead, layout.Head());
        Assert.Equal(oldConfig, File.ReadAllBytes(layout.CanonicalConfigPath));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void RecheckUsesFreshDerivedProposalRatherThanReviewedMutableBytes()
    {
        using var layout = NewLayout();
        File.WriteAllText(RuntimeConfigPath(layout), "personality = \"friendly\"\n");
        var reviewed = PortableCodexBackupPreview.Create(
            layout.Repository, layout.ProviderHome) with
        {
            ProposedCanonicalConfig = System.Text.Encoding.UTF8.GetBytes(
                "personality = \"pragmatic\"\n")
        };

        PortableCodexBackupWriter.WriteAndCommit(reviewed);

        Assert.Equal("personality = \"friendly\"\n",
            File.ReadAllText(layout.CanonicalConfigPath));
        Assert.Equal([AecApplication.ConfigSourceRelativePath], CommittedPaths(layout));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void NoChangeDoesNotCreateAnEmptyCommit()
    {
        using var layout = NewLayout();
        var preview = PortableCodexBackupPreview.Create(
            layout.Repository, layout.ProviderHome);
        var oldHead = layout.Head();
        var runtimePath = RuntimeConfigPath(layout);
        var runtime = File.ReadAllBytes(layout.RuntimePath);
        var config = File.ReadAllBytes(runtimePath);

        Assert.False(preview.HasChanges);
        Assert.Null(preview.Instructions.Section);
        Assert.False(preview.ConfigWouldChange);
        Assert.Equal(config, preview.ProposedCanonicalConfig);
        var error = Assert.Throws<InvalidOperationException>(() =>
            PortableCodexBackupWriter.WriteAndCommit(preview));

        Assert.Contains("no reviewed changes", error.Message, StringComparison.Ordinal);
        Assert.Equal(oldHead, layout.Head());
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(config, File.ReadAllBytes(runtimePath));
        Assert.Equal(string.Empty, layout.GitStatus());
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

    private static string[] CommittedPaths(SharedTargetStatusTests.StatusLayout layout) =>
        GitProcess.RunRequired(layout.Repository, "Could not inspect backup paths",
            "diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD")
            .Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();
}
