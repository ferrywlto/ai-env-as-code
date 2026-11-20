namespace Aec.Tests;

[Trait("Category", "Unit")]
public sealed class MigrationScaffoldReviewTests
{
    [Fact]
    public void IdenticalReviewsWithIndependentBuffersAreAccepted()
    {
        MigrationScaffoldWriter.VerifyReview(CreateReview(), CreateReview());
    }

    [Fact]
    public void ChangedBranchIsRejected()
    {
        var fresh = CreateReview();
        var reviewed = fresh with { Branch = "other-branch" };

        var error = Assert.Throws<InvalidOperationException>(() => MigrationScaffoldWriter.VerifyReview(reviewed, fresh));
        Assert.Contains("Branch changed after scaffold review", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ChangedInventoryMembershipIsRejected()
    {
        var fresh = CreateReview();
        var reviewed = fresh with { Files = [] };

        var error = Assert.Throws<InvalidOperationException>(() => MigrationScaffoldWriter.VerifyReview(reviewed, fresh));
        Assert.Contains("Scaffold inventory membership changed", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("path", "Scaffold inventory paths changed")]
    [InlineData("mode", "Scaffold committed mode changed")]
    [InlineData("committed bytes", "Scaffold committed bytes changed")]
    [InlineData("index entry", "Scaffold index entry changed")]
    [InlineData("staged changes", "Scaffold staging changed")]
    [InlineData("working bytes", "Scaffold source changed")]
    [InlineData("action", "Scaffold action changed")]
    public void ChangedInventoryFileIsRejected(string change, string expectedDiagnostic)
    {
        var fresh = CreateReview();
        var file = Assert.Single(fresh.Files);
        var changed = change switch
        {
            "path" => file with { RelativePath = "../escape.md" },
            "mode" => file with { CommittedMode = "100755" },
            "committed bytes" => file with { CommittedContent = "Different committed text."u8.ToArray() },
            "index entry" => file with { IndexEntry = "100644 different-blob 0" },
            "staged changes" => file with { HasStagedChanges = true },
            "working bytes" => file with { WorkingContent = "Different draft text."u8.ToArray() },
            "action" => file with { Action = MigrationScaffoldAction.ResetEmpty },
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };
        var reviewed = fresh with { Files = [changed] };

        var error = Assert.Throws<InvalidOperationException>(() => MigrationScaffoldWriter.VerifyReview(reviewed, fresh));
        Assert.Contains(expectedDiagnostic, error.Message, StringComparison.Ordinal);
    }

    private static MigrationScaffoldPreview CreateReview()
    {
        const string path = "environment/shared/instructions.md";
        const string commit = "reviewed-commit";
        var preflight = new MigrationPreflight("/data", commit, MigrationPreflight.LegacyFormat,
            MigrationPreflight.SectionedFormat);
        var preparation = new MigrationPreparation(preflight, commit, [],
            new MigrationDirtySections([path]), MigrationSectionChoice.Preserve);
        var file = new MigrationScaffoldFile(path, "Committed text."u8.ToArray(), "100644",
            "100644 reviewed-blob 0", "Draft text."u8.ToArray(), false, MigrationScaffoldAction.PreserveDirty);
        return new MigrationScaffoldPreview(preparation, "main", [file]);
    }
}
