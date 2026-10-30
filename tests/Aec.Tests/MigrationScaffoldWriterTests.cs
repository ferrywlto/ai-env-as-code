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
    [InlineData("staged-overwrite")]
    public void AnyBlockedSourceStopsBeforeCreatingOtherMissingFilesOrDirectories(string state)
    {
        using var layout = new Layout();
        layout.Files.Write(Shared, "Original.\n");
        if (state is "staged-modified" or "deleted" or "staged-deleted") layout.Commit(Shared);
        layout.Files.Write(Shared, "Draft.\n");
        if (state is "deleted" or "staged-deleted") File.Delete(Path.Combine(layout.Repository, Shared));
        if (state is "staged-add" or "staged-modified" or "staged-deleted" or "staged-overwrite") layout.Files.Git("add", "--", Shared);
        var choice = state switch
        {
            "no-choice" => MigrationSectionChoice.Unchanged,
            "staged-overwrite" => MigrationSectionChoice.Overwrite,
            _ => MigrationSectionChoice.Preserve
        };
        var reviewed = MigrationScaffoldPreview.Read(layout.Context() with { SectionChoice = choice });
        layout.Files.AssertNoChanges(() =>
        {
            var error = Assert.Throws<InvalidOperationException>(() => MigrationScaffoldWriter.Write(reviewed));
            Assert.Contains("before any write", error.Message, StringComparison.Ordinal);
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
                Assert.Equal(expected, error.WrittenPaths);
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

    [Theory]
    [InlineData("head")]
    [InlineData("branch")]
    [InlineData("index-flags")]
    [InlineData("marker")]
    [InlineData("enrollment")]
    [InlineData("runtime")]
    [InlineData("runtime-config")]
    [InlineData("legacy-agents")]
    [InlineData("legacy-config")]
    [InlineData("legacy-copilot")]
    [InlineData("hidden-section")]
    [InlineData("unexpected-dirt")]
    [InlineData("ignored-missing")]
    [InlineData("alternates")]
    [InlineData("unsafe-filter")]
    [InlineData("metadata-routing")]
    public void FocusedGuardRejectsBetweenStepChangesWithoutChangingFiles(string kind)
    {
        using var layout = new Layout();
        if (kind == "hidden-section")
        {
            layout.Files.Write(Shared, "Committed shared section.\n");
            layout.Commit(Shared);
            layout.Files.Git("update-index", "--assume-unchanged", "--", Shared);
        }
        var baseline = Preview(layout);
        var index = MigrationLegacyCaptureWriter.ReadIndex(Path.Combine(layout.Repository, ".git", "index"));
        var protectedFiles = MigrationScaffoldWriter.ReadProtectedFiles(baseline);
        switch (kind)
        {
            case "head": layout.Files.Git("commit", "--allow-empty", "-m", "Later checkpoint"); break;
            case "branch": layout.Files.Git("switch", "-c", "other-branch"); break;
            case "index-flags": layout.Files.Git("update-index", "--assume-unchanged", "--", "notes.md"); break;
            case "marker": layout.Files.Write(MigrationPreflight.FormatRelativePath, "2.0\n"); break;
            case "enrollment": File.Delete(layout.Runtime("copilot")); break;
            case "runtime": File.AppendAllText(layout.Runtime("codex"), "Later runtime edit.\n"); break;
            case "runtime-config": File.AppendAllText(layout.ConfigRuntime, "# Later config edit.\n"); break;
            case "legacy-agents": File.AppendAllText(Path.Combine(layout.Repository, AecApplication.SourceRelativePath), "Later legacy edit.\n"); break;
            case "legacy-config": File.AppendAllText(Path.Combine(layout.Repository, AecApplication.ConfigSourceRelativePath), "# Later legacy edit.\n"); break;
            case "legacy-copilot": File.AppendAllText(Path.Combine(layout.Repository, AecApplication.CopilotSourceRelativePath), "Later legacy edit.\n"); break;
            case "hidden-section": layout.Files.Write(Shared, "Hidden later edit.\n"); break;
            case "unexpected-dirt": layout.Files.Write("unexpected.md", "Unreviewed edit.\n"); break;
            case "ignored-missing": layout.Files.Write(".git/info/exclude", Shared + "\n"); break;
            case "alternates": layout.Files.Write(".git/objects/info/alternates", ""); break;
            case "unsafe-filter": layout.Files.Git("config", "filter.untrusted.clean", "unexpected-helper"); break;
            default:
                var other = Path.Combine(layout.Files.Root, "other-work-tree");
                Directory.CreateDirectory(other);
                layout.Files.Git("config", "core.worktree", other);
                break;
        }
        layout.Files.AssertNoChanges(() => Assert.Throws<InvalidOperationException>(() =>
            MigrationScaffoldWriter.VerifyCurrentInputs(baseline, index, protectedFiles, [], [])));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FocusedGuardAdmitsOnlyExactEmptyCompletedCreations(bool changedContent)
    {
        using var layout = new Layout();
        var baseline = Preview(layout);
        var index = MigrationLegacyCaptureWriter.ReadIndex(Path.Combine(layout.Repository, ".git", "index"));
        var protectedFiles = MigrationScaffoldWriter.ReadProtectedFiles(baseline);
        var created = baseline.Files[0].RelativePath;
        layout.Files.Write(created, changedContent ? "Unexpected content.\n" : "");
        layout.Files.AssertNoChanges(() =>
        {
            if (changedContent)
                Assert.Throws<InvalidOperationException>(() => MigrationScaffoldWriter.VerifyCurrentInputs(baseline, index, protectedFiles, [created], []));
            else
                MigrationScaffoldWriter.VerifyCurrentInputs(baseline, index, protectedFiles, [created], []);
        });
    }

    [Fact]
    public void FocusedGuardDoesNotRepeatGitHistoryOrBlobReads()
    {
        using var layout = new Layout();
        var baseline = Preview(layout);
        var index = MigrationLegacyCaptureWriter.ReadIndex(Path.Combine(layout.Repository, ".git", "index"));
        var protectedFiles = MigrationScaffoldWriter.ReadProtectedFiles(baseline);
        var tracePath = Path.Combine(layout.Files.Root, "guard-git.trace");
        var previousTrace = Environment.GetEnvironmentVariable("GIT_TRACE");
        try
        {
            // Built-in Git tracing verifies the work removed, without a timer,
            // production observer hook or dependency on machine performance.
            Environment.SetEnvironmentVariable("GIT_TRACE", tracePath);
            MigrationScaffoldWriter.VerifyCurrentInputs(baseline, index, protectedFiles, [], []);
        }
        finally
        {
            Environment.SetEnvironmentVariable("GIT_TRACE", previousTrace);
        }
        var trace = File.ReadAllText(tracePath);
        Assert.Contains(" status ", trace, StringComparison.Ordinal);
        Assert.DoesNotContain("rev-list", trace, StringComparison.Ordinal);
        Assert.DoesNotContain("cat-file", trace, StringComparison.Ordinal);
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
