namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class PortableCodexBackupWriterTests
{
    [Fact]
    public void ConfigOnlyBackupCommitsOnlyManagedCanonicalConfig()
    {
        using var layout = NewLayout();
        File.WriteAllText(RuntimeConfigPath(layout),
            "personality = \"friendly\"\n[other]\nkey = 1\n");
        var oldHead = layout.Head();
        var target = File.ReadAllBytes(layout.TargetPath);
        var runtime = File.ReadAllBytes(layout.RuntimePath);
        var runtimeConfig = File.ReadAllBytes(RuntimeConfigPath(layout));
        var preview = PortableCodexBackupPreview.Create(
            layout.Repository, layout.ProviderHome);

        var commit = PortableCodexBackupWriter.WriteAndCommit(preview);

        Assert.Equal(commit, layout.Head());
        Assert.Equal(oldHead, Parent(layout));
        Assert.Equal([AecApplication.ConfigSourceRelativePath], CommittedPaths(layout));
        Assert.Equal("personality = \"friendly\"\n",
            File.ReadAllText(layout.CanonicalConfigPath));
        Assert.Equal(target, File.ReadAllBytes(layout.TargetPath));
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(runtimeConfig, File.ReadAllBytes(RuntimeConfigPath(layout)));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void InstructionAndConfigDriftBecomeOneExactPathCommit()
    {
        using var layout = NewLayout();
        File.WriteAllText(layout.RuntimePath, File.ReadAllText(layout.RuntimePath)
            .Replace("Shared approval.", "Shared revision.", StringComparison.Ordinal));
        File.WriteAllText(RuntimeConfigPath(layout), "personality = \"pragmatic\"\n");
        var oldHead = layout.Head();
        var runtime = File.ReadAllBytes(layout.RuntimePath);
        var runtimeConfig = File.ReadAllBytes(RuntimeConfigPath(layout));
        var preview = PortableCodexBackupPreview.Create(
            layout.Repository, layout.ProviderHome);

        var commit = PortableCodexBackupWriter.WriteAndCommit(preview);

        Assert.Equal(commit, layout.Head());
        Assert.Equal(oldHead, Parent(layout));
        Assert.Equal(new[]
        {
            AecApplication.ConfigSourceRelativePath,
            "environment/shared/instructions.md",
            Path.GetRelativePath(layout.Repository, layout.TargetPath).Replace('\\', '/')
        }.OrderBy(path => path, StringComparer.Ordinal), CommittedPaths(layout));
        Assert.Equal("Shared revision.\n", File.ReadAllText(layout.SharedSource));
        Assert.Contains("Shared revision.", File.ReadAllText(layout.TargetPath),
            StringComparison.Ordinal);
        Assert.Equal("personality = \"pragmatic\"\n",
            File.ReadAllText(layout.CanonicalConfigPath));
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(runtimeConfig, File.ReadAllBytes(RuntimeConfigPath(layout)));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

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

        Assert.Throws<InvalidOperationException>(() =>
            PortableCodexBackupWriter.WriteAndCommit(preview));

        Assert.Equal(oldHead, layout.Head());
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    private static SharedTargetStatusTests.StatusLayout NewLayout()
    {
        var layout = new SharedTargetStatusTests.StatusLayout("codex");
        layout.WriteRuntime(portableBlock: true);
        File.WriteAllText(RuntimeConfigPath(layout), "personality = \"none\"\n");
        GitProcess.RunRequired(layout.Repository, "Could not set fixture Git author",
            "config", "--local", "user.name", "AEC Test");
        GitProcess.RunRequired(layout.Repository, "Could not set fixture Git email",
            "config", "--local", "user.email", "aec-test@example.invalid");
        GitProcess.RunRequired(layout.Repository, "Could not disable fixture signing",
            "config", "--local", "commit.gpgsign", "false");
        return layout;
    }

    private static string RuntimeConfigPath(SharedTargetStatusTests.StatusLayout layout) =>
        Path.Combine(layout.ProviderHome, "config.toml");

    private static string Parent(SharedTargetStatusTests.StatusLayout layout) =>
        GitProcess.RunRequired(layout.Repository, "Could not inspect backup parent",
            "rev-parse", "HEAD^").Output.Trim();

    private static string[] CommittedPaths(SharedTargetStatusTests.StatusLayout layout) =>
        GitProcess.RunRequired(layout.Repository, "Could not inspect backup paths",
            "diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD")
            .Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();
}
