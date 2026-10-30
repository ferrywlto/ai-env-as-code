using Layout = Aec.Tests.MigrationPortableCapturePreviewTests.Layout;

namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class MigrationScaffoldResetWriterTests
{
    private const string Shared = "environment/shared/instructions.md";

    [Theory]
    [InlineData("untracked")]
    [InlineData("modified")]
    [InlineData("deleted")]
    [InlineData("hidden")]
    [InlineData("committed-empty")]
    public void ExplicitOverwriteResetsOnlyReviewedDirtySection(string state)
    {
        using var layout = state == "untracked" ? new Layout() : new Layout("codex", "copilot");
        if (state == "untracked") CommitOtherSections(layout);
        if (state == "committed-empty")
        {
            layout.Files.Write(Shared, "");
            layout.Commit(Shared);
        }
        if (state == "hidden") layout.Files.Git("update-index", "--assume-unchanged", "--", Shared);
        if (state == "deleted") File.Delete(Path.Combine(layout.Repository, Shared));
        else layout.Files.Write(Shared, "Reviewed draft to discard.\n");
        var reviewed = Preview(layout);
        AssertOnlySectionWrites(layout, [Shared], () =>
            Assert.Equal(Shared, Assert.Single(MigrationScaffoldWriter.Write(reviewed))));
        if (state == "committed-empty")
        {
            Assert.DoesNotContain(Shared, MigrationDirtySections.Read(
                reviewed.Preparation.Preflight, reviewed.Preparation.Harnesses).Paths);
        }
    }

    [Fact]
    public void AlreadyEmptyResetDoesNotReplaceTheFileOrReportAWrite()
    {
        using var layout = new Layout("codex", "copilot");
        layout.Files.Write(Shared, "");
        var reviewed = Preview(layout);
        var path = Path.Combine(layout.Repository, Shared);
        var modified = File.GetLastWriteTimeUtc(path);
        layout.Files.AssertNoChanges(() => Assert.Empty(MigrationScaffoldWriter.Write(reviewed)));
        Assert.Equal(modified, File.GetLastWriteTimeUtc(path));
    }

    [Fact]
    public void ResetAndMissingFileCreationShareOneVerifiedPlan()
    {
        using var layout = new Layout();
        layout.Files.Write(Shared, "Draft to reset.\n");
        var reviewed = Preview(layout);
        var expected = reviewed.Files.Select(file => file.RelativePath).ToArray();
        AssertOnlySectionWrites(layout, expected, () =>
            Assert.Equal(expected, MigrationScaffoldWriter.Write(reviewed)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void StaleOrForgedResetStopsWithoutWriting(bool forged)
    {
        using var layout = new Layout("codex", "copilot");
        if (!forged) layout.Files.Write(Shared, "Reviewed draft.\n");
        var reviewed = Preview(layout);
        if (forged)
        {
            reviewed = reviewed with
            {
                Files = reviewed.Files.Select(file => file.RelativePath == Shared
                    ? file with { Action = MigrationScaffoldAction.ResetEmpty } : file).ToArray()
            };
        }
        else layout.Files.Write(Shared, "Unreviewed later draft.\n");
        layout.Files.AssertNoChanges(() => Assert.Throws<InvalidOperationException>(() =>
            MigrationScaffoldWriter.Write(reviewed)));
    }

    [Theory]
    [InlineData(UnixFileMode.UserRead | UnixFileMode.UserWrite)]
    [InlineData(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead)]
    public void UnixExistingResetRetainsExactPermissions(UnixFileMode mode)
    {
        if (OperatingSystem.IsWindows()) return;
        using var layout = new Layout("codex", "copilot");
        layout.Files.Write(Shared, "Discard this draft.\n");
        var path = Path.Combine(layout.Repository, Shared);
        File.SetUnixFileMode(path, mode);
        var reviewed = Preview(layout);
        AssertOnlySectionWrites(layout, [Shared], () => MigrationScaffoldWriter.Write(reviewed));
        Assert.Equal(mode, File.GetUnixFileMode(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnixDeletedResetIsPrivateAndNeverExecutable(bool executable)
    {
        if (OperatingSystem.IsWindows()) return;
        using var layout = new Layout("codex", "copilot");
        if (executable)
        {
            layout.Files.Git("update-index", "--chmod=+x", "--", Shared);
            layout.Files.Git("commit", "--quiet", "-m", "Record executable section");
        }
        var path = Path.Combine(layout.Repository, Shared);
        File.Delete(path);
        var reviewed = Preview(layout);
        var section = Assert.Single(reviewed.Files, file => file.RelativePath == Shared);
        Assert.Equal(executable ? "100755" : "100644", section.CommittedMode);

        AssertOnlySectionWrites(layout, [Shared], () => MigrationScaffoldWriter.Write(reviewed));

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResetFailureReportsWrittenAndAttemptedPathsWithoutRollback(bool earlierReset)
    {
        using var layout = new Layout("codex", "copilot");
        const string first = "environment/providers/codex/overlay.md";
        const string blocked = "environment/providers/copilot/overlay.md";
        if (earlierReset) layout.Files.Write(first, "First draft.\n");
        layout.Files.Write(blocked, "Blocked draft.\n");
        var reviewed = Preview(layout);
        string[] expected = earlierReset ? [first] : [];
        layout.Files.AssertOnlyChanges(expected, () => MigrationLegacyCaptureWriterTests.BlockReplacement(
            Path.Combine(layout.Repository, blocked), () =>
            {
                var error = Assert.Throws<MigrationScaffoldWriteException>(() => MigrationScaffoldWriter.Write(reviewed));
                Assert.Equal(expected, error.WrittenPaths);
                Assert.Equal(blocked, error.AttemptedPath);
                Assert.Empty(error.CreatedDirectories);
                Assert.NotNull(error.InnerException);
                Assert.Equal("Blocked draft.\n", File.ReadAllText(Path.Combine(layout.Repository, blocked)));
            }));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnlyCompletedResetMayRemoveAnOriginalDirtyPath(bool completed)
    {
        using var layout = new Layout("codex", "copilot");
        layout.Files.Write(Shared, "");
        layout.Commit(Shared);
        layout.Files.Write(Shared, "Dirty draft.\n");
        var baseline = Preview(layout);
        var index = MigrationLegacyCaptureWriter.ReadIndex(Path.Combine(layout.Repository, ".git", "index"));
        var protectedFiles = MigrationScaffoldWriter.ReadProtectedFiles(baseline);
        layout.Files.Write(Shared, "");
        layout.Files.AssertNoChanges(() =>
        {
            if (completed)
                MigrationScaffoldWriter.VerifyCurrentInputs(baseline, index, protectedFiles, [], [Shared]);
            else
                Assert.Throws<InvalidOperationException>(() => MigrationScaffoldWriter.VerifyCurrentInputs(
                    baseline, index, protectedFiles, [], []));
        });
    }

    [Fact]
    public void ProgressCannotTurnACleanSourceIntoAnAuthorizedReset()
    {
        using var layout = new Layout("codex", "copilot");
        var baseline = Preview(layout);
        var index = MigrationLegacyCaptureWriter.ReadIndex(Path.Combine(layout.Repository, ".git", "index"));
        var protectedFiles = MigrationScaffoldWriter.ReadProtectedFiles(baseline);
        layout.Files.AssertNoChanges(() => Assert.Throws<InvalidOperationException>(() =>
            MigrationScaffoldWriter.VerifyCurrentInputs(baseline, index, protectedFiles, [], [Shared])));
    }

    private static MigrationScaffoldPreview Preview(Layout layout) =>
        MigrationScaffoldPreview.Read(layout.Context() with { SectionChoice = MigrationSectionChoice.Overwrite });

    private static void CommitOtherSections(Layout layout)
    {
        string[] paths = [$"environment/platforms/{layout.Platform}/policy.md",
            "environment/providers/codex/overlay.md", "environment/providers/copilot/overlay.md"];
        foreach (var path in paths) layout.Files.Write(path, "Keep this clean section.\n");
        layout.Commit(paths);
    }

    private static void AssertOnlySectionWrites(Layout layout, string[] writtenPaths, Action action)
    {
        var files = Directory.EnumerateFiles(layout.Files.Root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);
        var expectedDirectories = Directory.GetDirectories(layout.Files.Root, "*", SearchOption.AllDirectories)
            .ToHashSet(StringComparer.Ordinal);
        var written = writtenPaths.Select(path => Path.Combine(layout.Repository, path)).ToHashSet(StringComparer.Ordinal);
        foreach (var path in written)
        {
            var directory = Path.GetDirectoryName(path)!;
            while (directory != layout.Repository)
            {
                expectedDirectories.Add(directory);
                directory = Path.GetDirectoryName(directory)!;
            }
        }
        action();
        Assert.Equal(files.Keys.Union(written, StringComparer.Ordinal).Order(StringComparer.Ordinal),
            Directory.EnumerateFiles(layout.Files.Root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
        Assert.Equal(expectedDirectories.Order(StringComparer.Ordinal),
            Directory.GetDirectories(layout.Files.Root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
        foreach (var (path, bytes) in files)
        {
            if (written.Contains(path)) Assert.Empty(File.ReadAllBytes(path));
            else Assert.Equal(bytes, File.ReadAllBytes(path));
        }
        foreach (var path in written) Assert.Empty(File.ReadAllBytes(path));
    }
}
