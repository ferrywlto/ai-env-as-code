namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class PortableBackupCommitterTests
{
    [Theory]
    [InlineData("shared", "codex", 6)]
    [InlineData("platform", "codex", 2)]
    [InlineData("provider", "copilot", 3)]
    public void CommitsOnlyReviewedSourceAndAffectedEnrolledTargets(
        string section, string provider, int targetCount)
    {
        using var layout = new PortableBackupTargetWriterTests.Layout(localProvider: provider);
        var (reviewed, attribution) = layout.SeedReviewedSource(section);
        var targets = layout.SeedRenderedTargets(attribution);
        File.WriteAllText(Path.Combine(layout.Repository, "unrelated-note.txt"), "Keep me untracked.\n");

        var commit = PortableBackupCommitter.Commit(reviewed, attribution, targets);

        Assert.Equal(targetCount, targets.Count);
        Assert.Equal(commit, layout.Head());
        Assert.Equal(reviewed.Commit, GitProcess.RunRequired(layout.Repository,
            "Could not read backup parent", "rev-parse", "HEAD^").Output.Trim());
        var committedPaths = GitProcess.RunRequired(layout.Repository,
            "Could not list committed paths", "diff-tree", "--no-commit-id", "--name-only",
            "-r", "HEAD").Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(new[] { attribution.SourceRelativePath! }.Concat(targets)
            .OrderBy(path => path, StringComparer.Ordinal),
            committedPaths.OrderBy(path => path, StringComparer.Ordinal));
        Assert.Equal("Keep me untracked.\n",
            File.ReadAllText(Path.Combine(layout.Repository, "unrelated-note.txt")));
        Assert.Empty(GitProcess.RunRequired(layout.Repository,
            "Could not inspect remaining index", "diff", "--cached", "--name-only").Output);
    }

    [Fact]
    public void MissingAffectedTargetStopsBeforeStaging()
    {
        using var layout = new PortableBackupTargetWriterTests.Layout();
        var (reviewed, attribution) = layout.SeedReviewedSource("shared");
        var targets = layout.SeedRenderedTargets(attribution);

        var error = Assert.Throws<InvalidOperationException>(() =>
            PortableBackupCommitter.Commit(reviewed, attribution, targets.Skip(1).ToArray()));
        Assert.Contains("Rendered target paths do not match all affected committed enrollments", error.Message, StringComparison.Ordinal);

        Assert.Equal(reviewed.Commit, layout.Head());
        Assert.Empty(GitProcess.RunRequired(layout.Repository,
            "Could not inspect fixture index", "diff", "--cached", "--name-only").Output);
    }

    [Fact]
    public void ExistingUnrelatedStageStopsBeforeAddingBackupPaths()
    {
        using var layout = new PortableBackupTargetWriterTests.Layout(localEnrollmentOnly: true);
        var (reviewed, attribution) = layout.SeedReviewedSource("shared");
        var targets = layout.SeedRenderedTargets(attribution);
        File.WriteAllText(Path.Combine(layout.Repository, "unrelated-note.txt"), "Staged note.\n");
        GitProcess.RunRequired(layout.Repository, "Could not stage unrelated fixture file",
            "add", "--", "unrelated-note.txt");

        var error = Assert.Throws<InvalidOperationException>(() =>
            PortableBackupCommitter.Commit(reviewed, attribution, targets));
        Assert.Contains("The repository already has staged changes", error.Message, StringComparison.Ordinal);

        Assert.Equal(reviewed.Commit, layout.Head());
        Assert.Equal("unrelated-note.txt\n", GitProcess.RunRequired(layout.Repository,
            "Could not inspect fixture index", "diff", "--cached", "--name-only").Output);
    }

    [Fact]
    public void TargetDriftStopsBeforeStaging()
    {
        using var layout = new PortableBackupTargetWriterTests.Layout(localEnrollmentOnly: true);
        var (reviewed, attribution) = layout.SeedReviewedSource("shared");
        var targets = layout.SeedRenderedTargets(attribution);
        File.AppendAllText(Path.Combine(layout.Repository, targets[0]), "Concurrent edit.\n");

        var error = Assert.Throws<InvalidOperationException>(() =>
            PortableBackupCommitter.Commit(reviewed, attribution, targets));
        Assert.Contains("A rendered target changed after replacement", error.Message, StringComparison.Ordinal);

        Assert.Equal(reviewed.Commit, layout.Head());
        Assert.Empty(GitProcess.RunRequired(layout.Repository,
            "Could not inspect fixture index", "diff", "--cached", "--name-only").Output);
    }

    [Fact]
    public void RuntimeDriftStopsBeforeStaging()
    {
        using var layout = new PortableBackupTargetWriterTests.Layout(localEnrollmentOnly: true);
        var (reviewed, attribution) = layout.SeedReviewedSource("shared");
        var targets = layout.SeedRenderedTargets(attribution);
        File.AppendAllText(layout.RuntimePath, "Concurrent edit.\n");

        var error = Assert.Throws<InvalidOperationException>(() =>
            PortableBackupCommitter.Commit(reviewed, attribution, targets));
        Assert.Contains("Backup HEAD or runtime changed after review", error.Message, StringComparison.Ordinal);

        Assert.Equal(reviewed.Commit, layout.Head());
        Assert.Empty(GitProcess.RunRequired(layout.Repository,
            "Could not inspect fixture index", "diff", "--cached", "--name-only").Output);
    }
}
