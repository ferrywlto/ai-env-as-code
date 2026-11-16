namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class PortableCopilotBackupWriterTests
{
    [Fact]
    public void OneSectionDriftCommitsOnlyItsSourceAndAffectedTarget()
    {
        using var layout = NewLayout();
        File.WriteAllText(layout.RuntimePath, File.ReadAllText(layout.RuntimePath)
            .Replace("Shared approval.", "Shared revision.", StringComparison.Ordinal));
        var oldHead = layout.Head();
        var runtime = File.ReadAllBytes(layout.RuntimePath);
        var preview = PortableCopilotBackupPreview.Create(
            layout.Repository, layout.ProviderHome);

        var commit = PortableCopilotBackupWriter.WriteAndCommit(preview);

        Assert.Equal(commit, layout.Head());
        Assert.Equal(oldHead, GitProcess.RunRequired(layout.Repository,
            "Could not inspect backup parent", "rev-parse", "HEAD^").Output.Trim());
        Assert.Equal(new[]
        {
            "environment/shared/instructions.md",
            Path.GetRelativePath(layout.Repository, layout.TargetPath).Replace('\\', '/')
        }.OrderBy(path => path, StringComparer.Ordinal), CommittedPaths(layout));
        Assert.Equal("Shared revision.\n", File.ReadAllText(layout.SharedSource));
        Assert.Contains("Shared revision.", File.ReadAllText(layout.TargetPath),
            StringComparison.Ordinal);
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void ExistingStagedChangesStopBeforeCanonicalWrite()
    {
        using var layout = NewLayout();
        File.WriteAllText(layout.RuntimePath, File.ReadAllText(layout.RuntimePath)
            .Replace("Shared approval.", "Shared revision.", StringComparison.Ordinal));
        var preview = PortableCopilotBackupPreview.Create(
            layout.Repository, layout.ProviderHome);
        File.WriteAllText(Path.Combine(layout.Repository, "note.txt"), "Keep staged.\n");
        GitProcess.RunRequired(layout.Repository, "Could not stage fixture note",
            "add", "--", "note.txt");
        var oldHead = layout.Head();
        var oldSource = File.ReadAllBytes(layout.SharedSource);

        var error = Assert.Throws<InvalidOperationException>(() =>
            PortableCopilotBackupWriter.WriteAndCommit(preview));

        Assert.Contains("already has staged changes", error.Message,
            StringComparison.Ordinal);
        Assert.Equal(oldHead, layout.Head());
        Assert.Equal(oldSource, File.ReadAllBytes(layout.SharedSource));
        Assert.Equal("note.txt\n", GitProcess.RunRequired(layout.Repository,
            "Could not inspect staged paths", "diff", "--cached", "--name-only").Output);
    }

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
        var preview = PortableCopilotBackupPreview.Create(
            layout.Repository, layout.ProviderHome);
        var oldHead = layout.Head();

        Assert.Throws<InvalidOperationException>(() =>
            PortableCopilotBackupWriter.WriteAndCommit(preview));

        Assert.Equal(oldHead, layout.Head());
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    private static SharedTargetStatusTests.StatusLayout NewLayout()
    {
        var layout = new SharedTargetStatusTests.StatusLayout("copilot");
        layout.WriteRuntime(portableBlock: true);
        GitProcess.RunRequired(layout.Repository, "Could not set fixture Git author",
            "config", "--local", "user.name", "AEC Test");
        GitProcess.RunRequired(layout.Repository, "Could not set fixture Git email",
            "config", "--local", "user.email", "aec-test@example.invalid");
        GitProcess.RunRequired(layout.Repository, "Could not disable fixture signing",
            "config", "--local", "commit.gpgsign", "false");
        return layout;
    }

    private static string[] CommittedPaths(SharedTargetStatusTests.StatusLayout layout) =>
        GitProcess.RunRequired(layout.Repository, "Could not inspect backup paths",
            "diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD")
            .Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();
}
