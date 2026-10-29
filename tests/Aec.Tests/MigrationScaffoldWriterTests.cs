using Layout = Aec.Tests.MigrationPortableCapturePreviewTests.Layout;

namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class MigrationScaffoldWriterTests
{
    private const string Shared = "environment/shared/instructions.md";

    [Fact]
    public void MissingSourcesBecomeEmptyFilesAndFreshRetryIsANoop()
    {
        using var layout = new Layout();
        var reviewed = Preview(layout);
        var expected = reviewed.Files.Select(file => file.RelativePath).ToArray();
        AssertOnlyAdditions(layout, expected, () =>
            Assert.Equal(expected, MigrationScaffoldWriter.Write(reviewed)));
        var retry = Preview(layout);
        layout.Files.AssertNoChanges(() => Assert.Empty(MigrationScaffoldWriter.Write(retry)));
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("copilot")]
    public void OnlyConfirmedProviderAndCurrentPlatformFilesAreCreated(string provider)
    {
        using var layout = new Layout();
        File.Delete(layout.Runtime(provider == "codex" ? "copilot" : "codex"));
        var reviewed = Preview(layout);
        string[] expected = [$"environment/platforms/{layout.Platform}/policy.md",
            $"environment/providers/{provider}/overlay.md", Shared];
        AssertOnlyAdditions(layout, expected, () =>
            Assert.Equal(expected, MigrationScaffoldWriter.Write(reviewed)));
    }

    [Fact]
    public void CleanExistingSourcesAndRenderedTargetsRemainByteExact()
    {
        using var layout = new Layout("codex", "copilot");
        var reviewed = Preview(layout);
        layout.Files.AssertNoChanges(() => Assert.Empty(MigrationScaffoldWriter.Write(reviewed)));
    }

    [Theory]
    [InlineData("untracked")]
    [InlineData("modified")]
    [InlineData("hidden")]
    [InlineData("empty")]
    public void PreservedDraftIsNotReplacedWhileOtherMissingSourcesAreCreated(string state)
    {
        using var layout = new Layout();
        layout.Files.Write(Shared, "Committed shared rule.\n");
        if (state is "modified" or "hidden") layout.Commit(Shared);
        if (state == "hidden") layout.Files.Git("update-index", "--assume-unchanged", "--", Shared);
        layout.Files.Write(Shared, state == "empty" ? "" : "Keep my reviewed draft.\n");
        var context = layout.Context() with { SectionChoice = MigrationSectionChoice.Preserve };
        var reviewed = MigrationScaffoldPreview.Read(context);
        var expected = reviewed.Files.Where(file => file.RelativePath != Shared)
            .Select(file => file.RelativePath).ToArray();
        AssertOnlyAdditions(layout, expected, () =>
            Assert.Equal(expected, MigrationScaffoldWriter.Write(reviewed)));
    }

    [Theory]
    [InlineData("staged-add")]
    [InlineData("staged-modified")]
    [InlineData("deleted")]
    [InlineData("staged-deleted")]
    [InlineData("no-choice")]
    [InlineData("overwrite")]
    public void AnyBlockedSourceStopsBeforeCreatingOtherMissingFilesOrDirectories(string state)
    {
        using var layout = new Layout();
        layout.Files.Write(Shared, "Original.\n");
        if (state is "staged-modified" or "deleted" or "staged-deleted") layout.Commit(Shared);
        layout.Files.Write(Shared, "Draft.\n");
        if (state is "deleted" or "staged-deleted") File.Delete(Path.Combine(layout.Repository, Shared));
        if (state is "staged-add" or "staged-modified" or "staged-deleted") layout.Files.Git("add", "--", Shared);
        var choice = state switch
        {
            "no-choice" => MigrationSectionChoice.Unchanged,
            "overwrite" => MigrationSectionChoice.Overwrite,
            _ => MigrationSectionChoice.Preserve
        };
        var reviewed = MigrationScaffoldPreview.Read(layout.Context() with { SectionChoice = choice });
        layout.Files.AssertNoChanges(() =>
        {
            var error = Assert.Throws<InvalidOperationException>(() => MigrationScaffoldWriter.Write(reviewed));
            Assert.Contains("before any creation", error.Message, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("head")]
    [InlineData("branch")]
    [InlineData("new-source")]
    [InlineData("draft-bytes")]
    [InlineData("enrollment")]
    [InlineData("marker")]
    [InlineData("ignored")]
    [InlineData("index")]
    public void StaleReviewStopsBeforeCreationAndPreservesNewState(string kind)
    {
        using var layout = new Layout();
        layout.Files.Write(Shared, "Reviewed draft.\n");
        var reviewed = Preview(layout);
        switch (kind)
        {
            case "head": layout.Files.Git("commit", "--allow-empty", "-m", "Later checkpoint"); break;
            case "branch": layout.Files.Git("switch", "-c", "other-branch"); break;
            case "new-source": layout.Files.Write($"environment/platforms/{layout.Platform}/policy.md", "Later draft.\n"); break;
            case "draft-bytes": layout.Files.Write(Shared, "Later draft.\n"); break;
            case "enrollment": File.Delete(layout.Runtime("copilot")); break;
            case "marker": layout.Files.Write(MigrationPreflight.FormatRelativePath, "2.0\n"); break;
            case "ignored": layout.Files.Write(".git/info/exclude", $"environment/platforms/{layout.Platform}/policy.md\n"); break;
            default: layout.Files.Git("add", "--", Shared); break;
        }
        layout.Files.AssertNoChanges(() =>
            Assert.Throws<InvalidOperationException>(() => MigrationScaffoldWriter.Write(reviewed)));
    }

    [Theory]
    [InlineData("path")]
    [InlineData("action")]
    [InlineData("bytes")]
    [InlineData("membership")]
    public void CallerModifiedInventoryCannotAuthorizeDifferentWrites(string kind)
    {
        using var layout = new Layout();
        layout.Files.Write(Shared, "Reviewed draft.\n");
        var reviewed = Preview(layout);
        var files = reviewed.Files.ToArray();
        switch (kind)
        {
            case "path": files[0] = files[0] with { RelativePath = "../escape.md" }; break;
            case "action": files[0] = files[0] with { Action = MigrationScaffoldAction.PreserveExisting }; break;
            case "bytes":
                var draft = Array.FindIndex(files, file => file.RelativePath == Shared);
                files[draft].WorkingContent![0] ^= 1;
                break;
            default: files = files[..^1]; break;
        }
        reviewed = reviewed with { Files = files };
        layout.Files.AssertNoChanges(() =>
            Assert.Throws<InvalidOperationException>(() => MigrationScaffoldWriter.Write(reviewed)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnixCreationFailureReportsRetainedFilesDirectoriesAndAttempt(bool earlierFiles)
    {
        // Unix directory permissions provide a real create denial. Windows ACL
        // denial needs separate native coverage; this is not a Windows claim.
        if (OperatingSystem.IsWindows()) return;
        using var layout = new Layout();
        var blocked = earlierFiles ? "environment/providers/copilot/overlay.md"
            : $"environment/platforms/{layout.Platform}/policy.md";
        var parent = Path.GetDirectoryName(Path.Combine(layout.Repository, blocked))!;
        Directory.CreateDirectory(parent);
        var reviewed = Preview(layout);
        var expected = reviewed.Files.TakeWhile(file => file.RelativePath != blocked)
            .Select(file => file.RelativePath).ToArray();
        var originalMode = File.GetUnixFileMode(parent);
        try
        {
            File.SetUnixFileMode(parent, originalMode &
                ~(UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite));
            AssertOnlyAdditions(layout, expected, () =>
            {
                var error = Assert.Throws<MigrationScaffoldWriteException>(() => MigrationScaffoldWriter.Write(reviewed));
                Assert.Equal(expected, error.CreatedPaths);
                Assert.Equal(blocked, error.AttemptedPath);
                Assert.IsType<UnauthorizedAccessException>(error.InnerException);
                foreach (var directory in error.CreatedDirectories)
                    Assert.True(Directory.Exists(Path.Combine(layout.Repository, directory)));
                Assert.Contains("No rollback", error.Message, StringComparison.Ordinal);
                Assert.False(File.Exists(Path.Combine(layout.Repository, blocked)));
            });
        }
        finally
        {
            File.SetUnixFileMode(parent, originalMode);
        }
    }

    private static MigrationScaffoldPreview Preview(Layout layout) => MigrationScaffoldPreview.Read(layout.Context());

    private static void AssertOnlyAdditions(Layout layout, string[] addedPaths, Action action)
    {
        var files = Directory.EnumerateFiles(layout.Files.Root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);
        var directories = Directory.GetDirectories(layout.Files.Root, "*", SearchOption.AllDirectories)
            .ToHashSet(StringComparer.Ordinal);
        var added = addedPaths.Select(path => Path.Combine(layout.Repository, path)).ToArray();
        var expectedDirectories = new HashSet<string>(directories, StringComparer.Ordinal);
        foreach (var file in added)
        {
            Assert.DoesNotContain(file, files.Keys);
            var directory = Path.GetDirectoryName(file)!;
            while (directory != layout.Repository)
            {
                expectedDirectories.Add(directory);
                directory = Path.GetDirectoryName(directory)!;
            }
        }

        action();

        Assert.Equal(files.Keys.Concat(added).Order(StringComparer.Ordinal),
            Directory.EnumerateFiles(layout.Files.Root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
        Assert.Equal(expectedDirectories.Order(StringComparer.Ordinal),
            Directory.GetDirectories(layout.Files.Root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
        foreach (var (path, bytes) in files) Assert.Equal(bytes, File.ReadAllBytes(path));
        foreach (var path in added) Assert.Empty(File.ReadAllBytes(path));
    }
}
