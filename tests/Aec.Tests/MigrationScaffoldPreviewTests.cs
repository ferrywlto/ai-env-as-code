using System.Text;
using Layout = Aec.Tests.MigrationPortableCapturePreviewTests.Layout;

namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class MigrationScaffoldPreviewTests
{
    private const string Shared = "environment/shared/instructions.md";

    [Fact]
    public void MissingSourcesProposeExactlyEmptySeedsWithoutCreatingDirectories()
    {
        using var layout = new Layout();
        var context = layout.Context();
        string[] expected = [Shared, $"environment/platforms/{layout.Platform}/policy.md",
            "environment/providers/codex/overlay.md", "environment/providers/copilot/overlay.md"];

        layout.Files.AssertNoChanges(() =>
        {
            var preview = MigrationScaffoldPreview.Read(context);
            Assert.Equal(expected.Order(StringComparer.Ordinal), preview.Files.Select(file => file.RelativePath));
            Assert.Equal(layout.Files.Git("symbolic-ref", "HEAD").Trim(), preview.Branch);
            Assert.All(preview.Files, file =>
            {
                Assert.Equal(MigrationScaffoldAction.CreateEmpty, file.Action);
                Assert.Null(file.CommittedContent);
                Assert.Null(file.CommittedMode);
                Assert.Empty(file.IndexEntry);
                Assert.Null(file.WorkingContent);
                Assert.Empty(Assert.IsType<byte[]>(file.InitialContent));
                Assert.False(file.HasStagedChanges);
                Assert.False(file.BlockedForFutureWrite);
            });
        });
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("copilot")]
    public void OnlyLocalHarnessAndCurrentPlatformSourcesAreListed(string provider)
    {
        using var layout = new Layout();
        var otherProvider = provider == "codex" ? "copilot" : "codex";
        File.Delete(layout.Runtime(otherProvider));
        var otherPlatform = layout.Platform == "windows-x64" ? "linux-arm64" : "windows-x64";
        var unusedPolicy = $"environment/platforms/{otherPlatform}/policy.md";
        var unusedOverlay = $"environment/providers/{otherProvider}/overlay.md";
        layout.Files.Write(unusedPolicy, "Other machine policy.\n");
        layout.Files.Write(unusedOverlay, "Not locally enrolled.\n");
        layout.Commit(unusedPolicy, unusedOverlay);
        var context = layout.Context();

        layout.Files.AssertNoChanges(() =>
        {
            var preview = MigrationScaffoldPreview.Read(context);
            Assert.Equal(3, preview.Files.Count);
            Assert.Contains(preview.Files, file => file.RelativePath == $"environment/providers/{provider}/overlay.md");
            Assert.DoesNotContain(preview.Files, file => file.RelativePath == unusedPolicy || file.RelativePath == unusedOverlay);
        });
    }

    [Theory]
    [InlineData(nameof(MigrationSectionChoice.Unchanged))]
    [InlineData(nameof(MigrationSectionChoice.Preserve))]
    [InlineData(nameof(MigrationSectionChoice.Overwrite))]
    public void CleanExistingSourcesArePreservedUnderEveryNonCancelledChoice(string choiceName)
    {
        var choice = Enum.Parse<MigrationSectionChoice>(choiceName);
        using var layout = new Layout("codex", "copilot");
        var context = layout.Context() with { SectionChoice = choice };
        layout.Files.AssertNoChanges(() =>
        {
            var preview = MigrationScaffoldPreview.Read(context);
            Assert.All(preview.Files, file =>
            {
                Assert.Equal(MigrationScaffoldAction.PreserveExisting, file.Action);
                Assert.Equal(file.CommittedContent, file.WorkingContent);
                Assert.True(file.CommittedMode is "100644" or "100755");
                Assert.NotEmpty(file.IndexEntry);
                Assert.Null(file.InitialContent);
                Assert.False(file.HasStagedChanges);
                Assert.False(file.BlockedForFutureWrite);
            });
        });
    }

    [Theory]
    [InlineData("untracked", false, nameof(MigrationScaffoldAction.PreserveDirty))]
    [InlineData("staged-add", true, nameof(MigrationScaffoldAction.PreserveDirty))]
    [InlineData("modified", false, nameof(MigrationScaffoldAction.PreserveDirty))]
    [InlineData("staged-modified", true, nameof(MigrationScaffoldAction.PreserveDirty))]
    [InlineData("staged-and-unstaged", true, nameof(MigrationScaffoldAction.PreserveDirty))]
    [InlineData("deleted", false, nameof(MigrationScaffoldAction.PreserveDeletion))]
    [InlineData("staged-deleted", true, nameof(MigrationScaffoldAction.PreserveDeletion))]
    public void PreserveKeepsDirtyBytesAndDeletionsAndNeverUnstages(string state, bool staged, string actionName)
    {
        var action = Enum.Parse<MigrationScaffoldAction>(actionName);
        using var layout = new Layout();
        layout.Files.Write(Shared, "Committed section.\n");
        if (state is not ("untracked" or "staged-add")) layout.Commit(Shared);
        layout.Files.Write(Shared, "Draft section.\n");
        if (state is "staged-add" or "staged-modified" or "staged-and-unstaged") layout.Files.Git("add", "--", Shared);
        if (state == "staged-and-unstaged") layout.Files.Write(Shared, "Unstaged revision.\n");
        if (state is "deleted" or "staged-deleted") File.Delete(Path.Combine(layout.Repository, Shared));
        if (state == "staged-deleted") layout.Files.Git("add", "--", Shared);
        var context = layout.Context() with { SectionChoice = MigrationSectionChoice.Preserve };

        layout.Files.AssertNoChanges(() =>
        {
            var preview = MigrationScaffoldPreview.Read(context);
            var file = Assert.Single(preview.Files, file => file.RelativePath == Shared);
            Assert.Equal(action, file.Action);
            Assert.Equal(staged, file.HasStagedChanges);
            Assert.Equal(staged || action == MigrationScaffoldAction.PreserveDeletion, file.BlockedForFutureWrite);
            Assert.Null(file.InitialContent);
            if (action == MigrationScaffoldAction.PreserveDeletion) Assert.Null(file.WorkingContent);
            else Assert.Equal(File.ReadAllBytes(Path.Combine(layout.Repository, Shared)), file.WorkingContent);
        });
    }

    [Theory]
    [InlineData(nameof(MigrationSectionChoice.Unchanged), nameof(MigrationScaffoldAction.NeedsDirtyChoice))]
    [InlineData(nameof(MigrationSectionChoice.Overwrite), nameof(MigrationScaffoldAction.NeedsOverwriteReview))]
    public void DirtyContentNeverBecomesAnImplicitEmptySeed(string choiceName, string actionName)
    {
        var choice = Enum.Parse<MigrationSectionChoice>(choiceName);
        var action = Enum.Parse<MigrationScaffoldAction>(actionName);
        using var layout = new Layout();
        layout.Files.Write(Shared, "Keep this draft.\n");
        var context = layout.Context() with { SectionChoice = choice };
        layout.Files.AssertNoChanges(() =>
        {
            var file = Assert.Single(MigrationScaffoldPreview.Read(context).Files, file => file.RelativePath == Shared);
            Assert.Equal(action, file.Action);
            Assert.True(file.BlockedForFutureWrite);
            Assert.Null(file.InitialContent);
            Assert.Equal(Encoding.UTF8.GetBytes("Keep this draft.\n"), file.WorkingContent);
        });
    }

    [Fact]
    public void HiddenWorkingEditIsReportedAsNeedingAChoice()
    {
        using var layout = new Layout();
        layout.Files.Write(Shared, "Committed section.\n");
        layout.Commit(Shared);
        layout.Files.Git("update-index", "--assume-unchanged", "--", Shared);
        layout.Files.Write(Shared, "Hidden draft.\n");
        var context = layout.Context();
        Assert.Empty(context.DirtySections.Paths);

        layout.Files.AssertNoChanges(() =>
        {
            var file = Assert.Single(MigrationScaffoldPreview.Read(context).Files, file => file.RelativePath == Shared);
            Assert.Equal(MigrationScaffoldAction.NeedsDirtyChoice, file.Action);
            Assert.Equal(Encoding.UTF8.GetBytes("Committed section.\n"), file.CommittedContent);
            Assert.Equal(Encoding.UTF8.GetBytes("Hidden draft.\n"), file.WorkingContent);
            Assert.True(file.BlockedForFutureWrite);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CaptureRequiresARefreshedHeadAndDoesNotRequireReapplyingPortableDigests(bool portable)
    {
        using var layout = portable ? new Layout("codex", "copilot") : new Layout();
        if (portable) layout.EditRuntime("codex", "Provider mechanics.", "Captured portable revision.");
        else File.AppendAllText(layout.Runtime("codex"), "Captured legacy revision.\n");
        var before = layout.Context();
        var commit = portable
            ? MigrationPortableCaptureCommitter.WriteAndCommit(MigrationPortableCapturePreview.Read(before))
            : MigrationLegacyCaptureCommitter.WriteAndCommit(MigrationLegacyCapturePreview.Read(before));
        Assert.NotNull(commit);
        var refreshed = layout.Context(before.OriginalCommit);

        layout.Files.AssertNoChanges(() =>
        {
            Assert.Throws<InvalidOperationException>(() => MigrationScaffoldPreview.Read(before));
            var preview = MigrationScaffoldPreview.Read(refreshed);
            Assert.Equal(commit, preview.Preparation.Preflight.Commit);
            Assert.Equal(4, preview.Files.Count);
            Assert.All(preview.Files, file => Assert.Equal(portable
                ? MigrationScaffoldAction.PreserveExisting : MigrationScaffoldAction.CreateEmpty, file.Action));
        });
    }

    [Theory]
    [InlineData("head")]
    [InlineData("enrollment")]
    [InlineData("dirty-paths")]
    [InlineData("marker")]
    public void StalePreparedContextStopsWithoutChanges(string kind)
    {
        using var layout = new Layout();
        var context = layout.Context();
        switch (kind)
        {
            case "head": layout.Files.Git("commit", "--allow-empty", "-m", "New checkpoint"); break;
            case "enrollment": File.Delete(layout.Runtime("copilot")); break;
            case "dirty-paths": layout.Files.Write(Shared, "New unreviewed path.\n"); break;
            default: layout.Files.Write(MigrationPreflight.FormatRelativePath, "2.0\n"); break;
        }
        layout.Files.AssertNoChanges(() => Assert.Throws<InvalidOperationException>(() => MigrationScaffoldPreview.Read(context)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IndexedAndCommittedLinksAreRejectedWithoutAWorkingSymlink(bool committed)
    {
        using var layout = new Layout();
        var context = layout.Context();
        layout.Files.Write(Shared, "Link payload.\n");
        var blob = layout.Files.Git("hash-object", "-w", "--", Shared).Trim();
        layout.Files.Git("update-index", "--add", "--cacheinfo", "120000", blob, Shared);
        if (committed)
        {
            layout.Files.Git("commit", "--quiet", "-m", "Invalid link checkpoint");
            context = context with { Preflight = MigrationPreflight.Read(layout.Repository, "2.0") };
        }
        layout.Files.AssertNoChanges(() => Assert.Throws<InvalidOperationException>(() => MigrationScaffoldPreview.Read(context)));
    }

    [Fact]
    public void EvenMissingIgnoredSourcesAreRejectedBeforeProposingSeeds()
    {
        using var layout = new Layout();
        layout.Files.Write(".git/info/exclude", Shared + "\n");
        var context = layout.Context();
        layout.Files.AssertNoChanges(() =>
        {
            var error = Assert.Throws<InvalidOperationException>(() => MigrationScaffoldPreview.Read(context));
            Assert.Contains("ignored by Git", error.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void CancelledReviewDoesNotProduceAnInventory()
    {
        using var layout = new Layout();
        var context = layout.Context() with { SectionChoice = MigrationSectionChoice.Cancelled };
        layout.Files.AssertNoChanges(() => Assert.Throws<InvalidOperationException>(() => MigrationScaffoldPreview.Read(context)));
    }
}
