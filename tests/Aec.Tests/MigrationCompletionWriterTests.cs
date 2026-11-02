using Layout = Aec.Tests.MigrationPortableCapturePreviewTests.Layout;

namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class MigrationCompletionWriterTests
{
    [Theory]
    [InlineData("both")]
    [InlineData("codex")]
    [InlineData("copilot")]
    public void CreatesOnlyPrivateUncommittedMarker(string selected)
    {
        using var layout = selected == "both" ? new Layout("codex", "copilot") : new Layout(selected);
        if (selected != "both") File.Delete(layout.Runtime(selected == "codex" ? "copilot" : "codex"));
        var preview = MigrationCompletionPreview.Read(layout.Context());
        var before = Snapshot(layout.Files.Root);
        var directories = Directories(layout.Files.Root);
        var marker = Path.Combine(layout.Repository, MigrationCompletionPreview.MarkerRelativePath);

        Assert.Equal(marker, MigrationCompletionWriter.Write(preview));

        var after = Snapshot(layout.Files.Root);
        Assert.Equal(before.Keys.Append(marker).Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
        foreach (var (path, bytes) in before) Assert.Equal(bytes, after[path]);
        Assert.Equal(directories, Directories(layout.Files.Root));
        Assert.Equal("2.0\n", File.ReadAllText(marker));
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(marker));
    }

    [Theory]
    [InlineData("runtime")]
    [InlineData("head")]
    [InlineData("index-buffer")]
    [InlineData("existing-marker")]
    public void StaleReviewIsRejectedWithoutChanges(string mutation)
    {
        using var layout = new Layout("codex", "copilot");
        var preview = MigrationCompletionPreview.Read(layout.Context());
        switch (mutation)
        {
            case "runtime": File.AppendAllText(layout.Runtime("codex"), "External edit.\n"); break;
            case "head": layout.Files.Git("commit", "--allow-empty", "-m", "Later checkpoint"); break;
            case "index-buffer": preview.Index[0] ^= 1; break;
            case "existing-marker": layout.Files.Write(MigrationCompletionPreview.MarkerRelativePath, "Existing marker.\n"); break;
        }
        layout.Files.AssertNoChanges(() =>
            Assert.ThrowsAny<InvalidOperationException>(() => MigrationCompletionWriter.Write(preview)));
    }

    [Fact]
    public void CompetingMarkerIsNeverOverwritten()
    {
        using var layout = new Layout("codex", "copilot");
        var preview = MigrationCompletionPreview.Read(layout.Context());
        Dictionary<string, byte[]>? afterArrival = null;
        string[]? directories = null;
        var marker = Path.Combine(layout.Repository, MigrationCompletionPreview.MarkerRelativePath);
        Assert.ThrowsAny<InvalidOperationException>(() => MigrationCompletionWriter.Write(preview, () =>
        {
            File.WriteAllText(marker, "Competing marker.\n");
            afterArrival = Snapshot(layout.Files.Root);
            directories = Directories(layout.Files.Root);
        }));
        Assert.NotNull(afterArrival);
        AssertSnapshot(layout.Files.Root, afterArrival, directories!);
        Assert.Equal("Competing marker.\n", File.ReadAllText(marker));
    }

    [Theory]
    [InlineData("marker")]
    [InlineData("runtime")]
    [InlineData("source")]
    [InlineData("head")]
    [InlineData("index")]
    public void PostWriteFailureRetainsInspectableState(string mutation)
    {
        using var layout = new Layout("codex", "copilot");
        var preview = MigrationCompletionPreview.Read(layout.Context());
        var marker = Path.Combine(layout.Repository, MigrationCompletionPreview.MarkerRelativePath);
        Dictionary<string, byte[]>? afterMutation = null;
        string[]? directories = null;
        var error = Assert.Throws<InvalidOperationException>(() => MigrationCompletionWriter.Write(preview,
            afterWriteForTests: () =>
            {
                Assert.True(File.Exists(marker));
                switch (mutation)
                {
                    case "marker": File.AppendAllText(marker, "External edit.\n"); break;
                    case "runtime": File.AppendAllText(layout.Runtime("codex"), "External edit.\n"); break;
                    case "source": layout.Files.Write("environment/shared/instructions.md", "External source.\n"); break;
                    case "head": layout.Files.Git("commit", "--allow-empty", "-m", "Concurrent checkpoint"); break;
                    case "index": layout.Files.Git("add", "--", MigrationCompletionPreview.MarkerRelativePath); break;
                }
                afterMutation = Snapshot(layout.Files.Root);
                directories = Directories(layout.Files.Root);
            }));
        Assert.NotNull(afterMutation);
        AssertSnapshot(layout.Files.Root, afterMutation, directories!);
        Assert.Contains("may remain", error.Message, StringComparison.Ordinal);
        Assert.Contains("No automatic rollback", error.Message, StringComparison.Ordinal);
        Assert.True(File.Exists(marker));
    }

    [Fact]
    public void IgnoredMarkerStopsWithCreatedFileRetained()
    {
        using var layout = new Layout("codex", "copilot");
        File.AppendAllText(Path.Combine(layout.Repository, ".git", "info", "exclude"), "\n.aec-version\n");
        var preview = MigrationCompletionPreview.Read(layout.Context());
        var marker = Path.Combine(layout.Repository, MigrationCompletionPreview.MarkerRelativePath);
        Dictionary<string, byte[]>? afterWrite = null;
        string[]? directories = null;
        var error = Assert.Throws<InvalidOperationException>(() => MigrationCompletionWriter.Write(preview,
            afterWriteForTests: () =>
            {
                afterWrite = Snapshot(layout.Files.Root);
                directories = Directories(layout.Files.Root);
            }));
        Assert.NotNull(afterWrite);
        AssertSnapshot(layout.Files.Root, afterWrite, directories!);
        Assert.Equal("2.0\n", File.ReadAllText(marker));
        Assert.Contains("may remain", error.Message, StringComparison.Ordinal);
    }

    private static Dictionary<string, byte[]> Snapshot(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);

    private static string[] Directories(string root) =>
        Directory.GetDirectories(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();

    private static void AssertSnapshot(string root, Dictionary<string, byte[]> expected, string[] directories)
    {
        var actual = Snapshot(root);
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), actual.Keys.Order(StringComparer.Ordinal));
        foreach (var (path, bytes) in expected) Assert.Equal(bytes, actual[path]);
        Assert.Equal(directories, Directories(root));
    }
}
