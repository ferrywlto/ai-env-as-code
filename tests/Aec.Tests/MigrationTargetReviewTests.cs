namespace Aec.Tests;

[Trait("Category", "Unit")]
public sealed class MigrationTargetReviewTests
{
    [Fact]
    public void EquivalentReviewsWithSeparateByteBuffersAreAccepted()
    {
        MigrationTargetWriter.VerifyReview(CreatePreview(), CreatePreview());
    }

    [Theory]
    [InlineData("branch", "Migration target branch changed after review.")]
    [InlineData("platform", "Migration target platform differs from review.")]
    [InlineData("harness", "Migration target harness differs from review.")]
    [InlineData("path", "Migration target path differs from review.")]
    [InlineData("action", "Migration target action differs from review.")]
    [InlineData("proposal", "Migration target desired bytes differ from review.")]
    [InlineData("baseline", "Migration target committed bytes differ from review.")]
    [InlineData("mode", "Migration target committed mode differs from review.")]
    [InlineData("working bytes", "Migration target working bytes differ from review.")]
    public void ChangedTargetReviewIsRejected(string change, string expectedDiagnostic)
    {
        var fresh = CreatePreview();
        var reviewed = change switch
        {
            "branch" => fresh with { Branch = "other-branch" },
            "platform" => fresh with { Platform = AecIdentifiers.WindowsX64 },
            "harness" => fresh with { Harness = fresh.Harness with { Provider = AecIdentifiers.Copilot } },
            "path" => fresh with { TargetRelativePath = "environment/targets/outside.md" },
            "action" => fresh with { Action = MigrationTargetAction.Regenerate },
            "proposal" => fresh with { DesiredContent = "Forged proposal.\n"u8.ToArray() },
            "baseline" => fresh with { CommittedContent = "Forged baseline.\n"u8.ToArray() },
            "mode" => fresh with { CommittedMode = "100755" },
            "working bytes" => fresh with { WorkingContent = "Forged working target.\n"u8.ToArray() },
            _ => throw new ArgumentOutOfRangeException(nameof(change))
        };

        var error = Assert.Throws<InvalidOperationException>(() => MigrationTargetWriter.VerifyReview(reviewed, fresh));

        Assert.Equal(expectedDiagnostic, error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChangedSourceBytesAreRejectedInBothInventories(bool required)
    {
        var fresh = CreatePreview();
        var sources = (required ? fresh.RequiredSources : fresh.Sources).ToArray();
        var changedPath = sources[^1].RelativePath;
        sources[^1] = sources[^1] with { WorkingContent = "Forged source.\n"u8.ToArray() };
        var reviewed = required ? fresh with { RequiredSources = sources } : fresh with { Sources = sources };

        var error = Assert.Throws<InvalidOperationException>(() => MigrationTargetWriter.VerifyReview(reviewed, fresh));

        Assert.Equal($"Migration source working bytes differ: {changedPath}.", error.Message);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RemovedSourceIsRejectedInBothInventories(bool required)
    {
        var fresh = CreatePreview();
        var reviewed = required
            ? fresh with { RequiredSources = fresh.RequiredSources.SkipLast(1).ToArray() }
            : fresh with { Sources = fresh.Sources.SkipLast(1).ToArray() };

        var error = Assert.Throws<InvalidOperationException>(() => MigrationTargetWriter.VerifyReview(reviewed, fresh));

        Assert.Equal("Migration target source inventory differs from review.", error.Message);
    }

    private static MigrationTargetPreview CreatePreview()
    {
        const string commit = "reviewed-commit";
        const string platform = AecIdentifiers.MacOsArm64;
        var preflight = new MigrationPreflight("/data", commit, MigrationPreflight.LegacyFormat,
            MigrationPreflight.SectionedFormat);
        var codex = new MigrationHarness(AecIdentifiers.Codex, "/codex", "/codex/AGENTS.md");
        var copilot = new MigrationHarness(AecIdentifiers.Copilot, "/copilot", "/copilot/copilot-instructions.md");
        var preparation = new MigrationPreparation(preflight, commit, [codex, copilot],
            new MigrationDirtySections([]), MigrationSectionChoice.Unchanged);
        MigrationValidatedSource[] sources =
        [
            CreateSource("environment/shared/instructions.md"),
            CreateSource($"environment/platforms/{platform}/policy.md"),
            CreateSource("environment/providers/codex/overlay.md")
        ];
        ReadOnlySpan<byte> target = "Reviewed target.\n"u8;
        return new MigrationTargetPreview(preparation, "main", platform, codex, sources,
            $"environment/targets/{platform}/codex/AGENTS.md", target.ToArray(), "100644",
            target.ToArray(), target.ToArray(), MigrationTargetAction.Unchanged)
        {
            RequiredSources = [.. sources, CreateSource("environment/providers/copilot/overlay.md")]
        };
    }

    private static MigrationValidatedSource CreateSource(string path) =>
        new(path, "Reviewed source.\n"u8.ToArray(), "100644", "Reviewed source.\n"u8.ToArray());
}
