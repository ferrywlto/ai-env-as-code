namespace Aec.Tests;

[Trait("Category", "Unit")]
public sealed class MigrationLegacyCaptureReviewTests
{
    [Fact]
    public void EquivalentReviewsWithIndependentBuffersAreAccepted()
    {
        var reviewed = CreateReview();
        var fresh = CreateReview();

        MigrationLegacyCaptureWriter.EnsureUnchangedInputs(reviewed, fresh);
    }

    [Theory]
    [InlineData("branch")]
    [InlineData("missing row")]
    [InlineData("row order")]
    public void ChangedReviewShapeIsRejected(string change)
    {
        var fresh = CreateReview();
        var reviewed = change switch
        {
            "branch" => fresh with { Branch = "refs/heads/other" },
            "missing row" => fresh with { Files = [fresh.Files[0]] },
            "row order" => fresh with { Files = [fresh.Files[1], fresh.Files[0]] },
            _ => throw new ArgumentOutOfRangeException(nameof(change), change, null)
        };

        AssertRejects(reviewed, fresh);
    }

    [Theory]
    [InlineData("provider")]
    [InlineData("canonical path")]
    [InlineData("runtime path")]
    [InlineData("canonical bytes")]
    [InlineData("runtime bytes")]
    [InlineData("proposal bytes")]
    public void ChangedFileReviewIsRejected(string change)
    {
        var fresh = CreateReview();
        var file = fresh.Files[0];
        var changed = change switch
        {
            "provider" => file with { Provider = AecIdentifiers.Copilot },
            "canonical path" => file with { RelativePath = "../outside.md" },
            "runtime path" => file with { RuntimePath = "/elsewhere/AGENTS.md" },
            "canonical bytes" => file with { Canonical = "Unreviewed canonical content."u8.ToArray() },
            "runtime bytes" => file with { Runtime = "Unreviewed runtime content."u8.ToArray() },
            "proposal bytes" => file with { ProposedCanonical = "Unreviewed proposal."u8.ToArray() },
            _ => throw new ArgumentOutOfRangeException(nameof(change), change, null)
        };

        AssertRejects(fresh with { Files = [changed, fresh.Files[1]] }, fresh);
    }

    private static void AssertRejects(MigrationLegacyCapturePreview reviewed, MigrationLegacyCapturePreview fresh)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            MigrationLegacyCaptureWriter.EnsureUnchangedInputs(reviewed, fresh));
        Assert.Contains("Legacy capture inputs or proposal changed after review", error.Message, StringComparison.Ordinal);
    }

    private static MigrationLegacyCapturePreview CreateReview()
    {
        const string commit = "reviewed-commit";
        var preflight = new MigrationPreflight("/data", commit, MigrationPreflight.LegacyFormat,
            MigrationPreflight.SectionedFormat);
        var preparation = new MigrationPreparation(preflight, commit, [], new MigrationDirtySections([]),
            MigrationSectionChoice.Unchanged);
        return new MigrationLegacyCapturePreview(preparation, "refs/heads/main",
            [
                new MigrationLegacyCaptureFile(AecIdentifiers.Codex, AecApplication.SourceRelativePath,
                    "/codex/AGENTS.md", "Committed Codex."u8.ToArray(), "Edited Codex."u8.ToArray(),
                    "Edited Codex."u8.ToArray()),
                new MigrationLegacyCaptureFile(AecIdentifiers.Copilot, AecApplication.CopilotSourceRelativePath,
                    "/copilot/copilot-instructions.md", "Committed Copilot."u8.ToArray(),
                    "Committed Copilot."u8.ToArray(), "Committed Copilot."u8.ToArray())
            ]);
    }
}
