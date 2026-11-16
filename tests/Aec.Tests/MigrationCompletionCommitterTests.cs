using System.Text;
using Layout = Aec.Tests.MigrationPortableCapturePreviewTests.Layout;

namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class MigrationCompletionCommitterTests
{
    private const string Marker = MigrationCompletionPreview.MarkerRelativePath;
    private static readonly byte[] MarkerBytes = Encoding.UTF8.GetBytes("2.0\n");
    private static readonly string[] MarkerPaths = [Marker];

    [Theory]
    [InlineData("both")]
    [InlineData("codex")]
    public void CommitsOnlyReviewedMarker(string selected)
    {
        using var layout = selected == "both" ? new Layout("codex", "copilot") : new Layout(selected);
        if (selected != "both") File.Delete(layout.Runtime("copilot"));
        var reviewed = MigrationCompletionPreview.Read(layout.Context());
        var parent = layout.Files.Head();
        var before = Capture(layout, true);

        var commit = MigrationCompletionCommitter.WriteAndCommit(reviewed);

        Assert.Equal(commit, layout.Files.Head());
        Assert.Equal($"{commit} {parent}", layout.Files.Git("--no-replace-objects", "rev-list", "--parents", "-n", "1", commit).Trim());
        Assert.Equal(MarkerPaths, Paths(layout, "diff-tree", "--no-commit-id", "--no-renames", "--name-only", "-r", parent, commit));
        Assert.StartsWith("100644 blob ", layout.Files.Git("ls-tree", commit, "--", Marker), StringComparison.Ordinal);
        Assert.Equal(MarkerBytes, GitProcess.RunRequiredBytes(layout.Repository, "Read committed marker", "--no-replace-objects", "show", $"{commit}:{Marker}"));
        var markerPath = Path.Combine(layout.Repository, Marker);
        Assert.Equal(MarkerBytes, File.ReadAllBytes(markerPath));
        if (!OperatingSystem.IsWindows()) Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(markerPath));
        Assert.Empty(Paths(layout, "diff", "--cached", "--no-renames", "--name-only"));
        Assert.Empty(layout.Files.Git("status", "--porcelain"));
        AssertAddedOnlyMarker(layout, before, markerPath);
    }

    [Theory]
    [InlineData("runtime")]
    [InlineData("head")]
    [InlineData("index")]
    public void StaleReviewRefusesWithoutChangingFixture(string mutation)
    {
        using var layout = new Layout("codex", "copilot");
        var reviewed = MigrationCompletionPreview.Read(layout.Context());
        switch (mutation)
        {
            case "runtime": File.AppendAllText(layout.Runtime("codex"), "External edit.\n"); break;
            case "head": layout.Files.Git("commit", "--allow-empty", "-m", "Later checkpoint"); break;
            case "index": layout.Files.Write("notes.md", "Unrelated content.\n"); layout.Files.Git("add", "--", "notes.md"); break;
        }
        var head = layout.Files.Head();
        var before = Capture(layout, false);
        Assert.ThrowsAny<InvalidOperationException>(() => MigrationCompletionCommitter.WriteAndCommit(reviewed));
        Assert.Equal(head, layout.Files.Head());
        AssertSnapshot(layout, before);
    }

    [Fact]
    public void SigningFailureRetainsOnlyMarkerStagedWithoutCommit()
    {
        using var layout = new Layout("codex", "copilot");
        var reviewed = MigrationCompletionPreview.Read(layout.Context());
        var parent = layout.Files.Head();
        layout.Files.Git("config", "gpg.format", "openpgp");
        layout.Files.Git("config", "gpg.program", Path.Combine(layout.Files.Root, "missing-signing-program"));
        layout.Files.Git("config", "commit.gpgSign", "true");
        var before = Capture(layout, true);
        var error = Assert.Throws<InvalidOperationException>(() => MigrationCompletionCommitter.WriteAndCommit(reviewed));
        Assert.Contains("may remain", error.Message, StringComparison.Ordinal);
        Assert.Equal(parent, layout.Files.Head());
        Assert.Equal(MarkerPaths, Paths(layout, "diff", "--cached", "--no-renames", "--name-only"));
        Assert.Equal(MarkerBytes, GitProcess.RunRequiredBytes(layout.Repository, "Read staged marker", "show", $":{Marker}"));
        var markerPath = Path.Combine(layout.Repository, Marker);
        Assert.Equal(MarkerBytes, File.ReadAllBytes(markerPath));
        AssertAddedOnlyMarker(layout, before, markerPath);
    }

    [Fact]
    public void PostWriteDriftRefusesBeforeStagingAndRetainsState()
    {
        using var layout = new Layout("codex", "copilot");
        var reviewed = MigrationCompletionPreview.Read(layout.Context());
        var parent = layout.Files.Head();
        var indexPath = Path.Combine(layout.Repository, ".git", "index");
        var index = File.ReadAllBytes(indexPath);
        FixtureSnapshot? afterMutation = null;
        var error = Assert.Throws<InvalidOperationException>(() => MigrationCompletionCommitter.WriteAndCommit(reviewed,
            afterWriteForTests: () =>
            {
                File.AppendAllText(layout.Runtime("codex"), "External edit.\n");
                afterMutation = Capture(layout, false);
            }));
        Assert.NotNull(afterMutation);
        AssertSnapshot(layout, afterMutation);
        Assert.Equal(index, File.ReadAllBytes(indexPath));
        Assert.Equal(parent, layout.Files.Head());
        Assert.Empty(Paths(layout, "diff", "--cached", "--no-renames", "--name-only"));
        Assert.Equal(MarkerBytes, File.ReadAllBytes(Path.Combine(layout.Repository, Marker)));
        Assert.Contains("may remain", error.Message, StringComparison.Ordinal);
        Assert.Contains("No automatic rollback", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("marker")]
    [InlineData("unrelated")]
    public void StageDriftRefusesCommitAndRetainsState(string mutation)
    {
        using var layout = new Layout("codex", "copilot");
        var reviewed = MigrationCompletionPreview.Read(layout.Context());
        var parent = layout.Files.Head();
        FixtureSnapshot? afterMutation = null;
        var error = Assert.Throws<InvalidOperationException>(() => MigrationCompletionCommitter.WriteAndCommit(reviewed,
            afterStageForTests: () =>
            {
                if (mutation == "marker") File.AppendAllText(Path.Combine(layout.Repository, Marker), "External edit.\n");
                else
                {
                    layout.Files.Write("notes.md", "Unrelated content.\n");
                    layout.Files.Git("add", "--", "notes.md");
                }
                afterMutation = Capture(layout, false);
            }));
        Assert.NotNull(afterMutation);
        Assert.Contains("may remain", error.Message, StringComparison.Ordinal);
        Assert.Contains("No automatic rollback", error.Message, StringComparison.Ordinal);
        Assert.Equal(parent, layout.Files.Head());
        AssertSnapshot(layout, afterMutation);
    }

    [Theory]
    [InlineData("marker")]
    [InlineData("runtime")]
    [InlineData("head")]
    public void PostCommitDriftReportsAndRetainsState(string mutation)
    {
        using var layout = new Layout("codex", "copilot");
        var reviewed = MigrationCompletionPreview.Read(layout.Context());
        var parent = layout.Files.Head();
        FixtureSnapshot? afterMutation = null;
        string? observedHead = null;
        var error = Assert.Throws<InvalidOperationException>(() => MigrationCompletionCommitter.WriteAndCommit(reviewed,
            afterCommitForTests: () =>
            {
                switch (mutation)
                {
                    case "marker": File.AppendAllText(Path.Combine(layout.Repository, Marker), "External edit.\n"); break;
                    case "runtime": File.AppendAllText(layout.Runtime("codex"), "External edit.\n"); break;
                    case "head": layout.Files.Git("commit", "--allow-empty", "-m", "Concurrent checkpoint"); break;
                }
                observedHead = layout.Files.Head();
                afterMutation = Capture(layout, false);
            }));
        Assert.NotNull(afterMutation);
        Assert.NotEqual(parent, observedHead);
        Assert.Contains("may remain", error.Message, StringComparison.Ordinal);
        Assert.Contains("No automatic rollback", error.Message, StringComparison.Ordinal);
        Assert.Equal(observedHead, layout.Files.Head());
        AssertSnapshot(layout, afterMutation);
    }

    private static string[] Paths(Layout layout, params string[] args) =>
        layout.Files.Git(args).Split('\n', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal).ToArray();

    private static FixtureSnapshot Capture(Layout layout, bool excludeLocalGit)
    {
        var git = Path.Combine(layout.Repository, ".git");
        var prefix = git + Path.DirectorySeparatorChar;
        bool Include(string path) => !excludeLocalGit ||
            (!string.Equals(path, git, StringComparison.Ordinal) && !path.StartsWith(prefix, StringComparison.Ordinal));
        var files = Directory.EnumerateFiles(layout.Files.Root, "*", SearchOption.AllDirectories).Where(Include)
            .ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);
        var directories = Directory.EnumerateDirectories(layout.Files.Root, "*", SearchOption.AllDirectories).Where(Include)
            .Order(StringComparer.Ordinal).ToArray();
        return new FixtureSnapshot(files, directories);
    }

    private static void AssertAddedOnlyMarker(Layout layout, FixtureSnapshot before, string markerPath)
    {
        var after = Capture(layout, true);
        Assert.Equal(before.Files.Keys.Append(markerPath).Order(StringComparer.Ordinal), after.Files.Keys.Order(StringComparer.Ordinal));
        foreach (var (path, bytes) in before.Files) Assert.Equal(bytes, after.Files[path]);
        Assert.Equal(before.Directories, after.Directories);
    }

    private static void AssertSnapshot(Layout layout, FixtureSnapshot expected)
    {
        var actual = Capture(layout, false);
        Assert.Equal(expected.Files.Keys.Order(StringComparer.Ordinal), actual.Files.Keys.Order(StringComparer.Ordinal));
        foreach (var (path, bytes) in expected.Files) Assert.Equal(bytes, actual.Files[path]);
        Assert.Equal(expected.Directories, actual.Directories);
    }

    private sealed record FixtureSnapshot(Dictionary<string, byte[]> Files, string[] Directories);
}
