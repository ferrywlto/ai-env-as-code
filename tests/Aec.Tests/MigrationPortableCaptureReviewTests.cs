namespace Aec.Tests;

[Trait("Category", "Unit")]
public sealed class MigrationPortableCaptureReviewTests
{
    [Fact]
    public void EquivalentReviewsWithIndependentBuffersAreAccepted()
    {
        var reviewed = CreateReview();
        var fresh = CreateReview();

        MigrationPortableCaptureWriter.EnsureUnchangedInputs(reviewed, fresh);
    }

    [Theory]
    [InlineData("branch")]
    [InlineData("Codex presence")]
    [InlineData("Copilot presence")]
    [InlineData("committed target")]
    [InlineData("Codex runtime")]
    [InlineData("Copilot runtime")]
    [InlineData("canonical config")]
    [InlineData("runtime config")]
    public void ChangedCaptureInputsAreRejected(string input)
    {
        var fresh = CreateReview();
        var codex = fresh.Codex!;
        var copilot = fresh.Copilot!;
        var reviewed = input switch
        {
            "branch" => fresh with { Branch = "other-branch" },
            "Codex presence" => fresh with { Codex = null },
            "Copilot presence" => fresh with { Copilot = null },
            "committed target" => fresh with
            {
                Codex = codex with { Snapshot = codex.Snapshot with { Target = "Unreviewed target."u8.ToArray() } }
            },
            "Codex runtime" => fresh with
            {
                Codex = codex with { Snapshot = codex.Snapshot with { Runtime = "Unreviewed runtime."u8.ToArray() } }
            },
            "Copilot runtime" => fresh with
            {
                Copilot = copilot with { Snapshot = copilot.Snapshot with { Runtime = "Unreviewed runtime."u8.ToArray() } }
            },
            "canonical config" => fresh with { Codex = codex with { CanonicalConfig = "personality = 'none'"u8.ToArray() } },
            "runtime config" => fresh with { Codex = codex with { RuntimeConfig = "personality = 'none'"u8.ToArray() } },
            _ => throw new ArgumentOutOfRangeException(nameof(input))
        };

        var error = Assert.Throws<InvalidOperationException>(() =>
            MigrationPortableCaptureWriter.EnsureUnchangedInputs(reviewed, fresh));
        Assert.Contains("Portable migration capture inputs changed after review", error.Message, StringComparison.Ordinal);
    }

    private static MigrationPortableCapturePreview CreateReview()
    {
        const string repository = "/data";
        const string commit = "reviewed-commit";
        var preflight = new MigrationPreflight(repository, commit, MigrationPreflight.LegacyFormat,
            MigrationPreflight.SectionedFormat);
        var preparation = new MigrationPreparation(preflight, commit, [],
            new MigrationDirtySections([]), MigrationSectionChoice.Unchanged);
        var instructions = new PortableBackupAttributionPreview(null, null, null);
        var codex = new PortableRuntimeSnapshot(repository, AecIdentifiers.MacOsArm64,
            AecIdentifiers.Codex, "/codex/AGENTS.md", commit,
            "Codex target."u8.ToArray(), "Codex runtime."u8.ToArray(), 10);
        var copilot = new PortableRuntimeSnapshot(repository, AecIdentifiers.MacOsArm64,
            AecIdentifiers.Copilot, "/copilot/copilot-instructions.md", commit,
            "Copilot target."u8.ToArray(), "Copilot runtime."u8.ToArray(), 3);
        var config = "personality = 'pragmatic'"u8;

        return new MigrationPortableCapturePreview(preparation, "main",
            new PortableCodexBackupPreview(codex, instructions, "/codex/config.toml",
                config.ToArray(), config.ToArray(), config.ToArray(), ConfigWouldChange: false),
            new PortableCopilotBackupPreview(copilot, instructions));
    }
}
