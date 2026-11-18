namespace Aec.Tests;

[Trait("Category", "Unit")]
public sealed class MigrationApplyReviewTests
{
    [Theory]
    [InlineData(AecIdentifiers.Codex)]
    [InlineData(AecIdentifiers.Copilot)]
    public void SeparatelyConstructedReviewsWithEqualBytesAreAccepted(string provider)
    {
        var reviewed = CreateReview(provider);
        var fresh = CreateReview(provider);

        MigrationApplyReview.Verify(reviewed, fresh);
    }

    [Theory]
    [InlineData("branch")]
    [InlineData("local harness")]
    [InlineData("commit")]
    [InlineData("runtime AEC block version")]
    [InlineData("committed target")]
    [InlineData("runtime instructions")]
    [InlineData("desired instructions")]
    [InlineData("apply action")]
    [InlineData("required sources")]
    [InlineData("source path")]
    [InlineData("source Git mode")]
    [InlineData("committed source")]
    [InlineData("working source")]
    public void ChangedReviewInputsAreRejected(string input)
    {
        var reviewed = CreateReview(AecIdentifiers.Codex);
        var apply = reviewed.Apply;
        var snapshot = apply.Snapshot;
        var source = Assert.Single(reviewed.RequiredSources);
        var fresh = input switch
        {
            "branch" => reviewed with { Branch = "other-branch" },
            "local harness" => reviewed with { Harness = reviewed.Harness with { Home = "other-home" } },
            "commit" => reviewed with { Apply = apply with { Snapshot = snapshot with { Commit = "other-commit" } } },
            "runtime AEC block version" => reviewed with
            {
                Apply = apply with { Snapshot = snapshot with { BlockVersion = 7 } }
            },
            "committed target" => reviewed with
            {
                Apply = apply with { Snapshot = snapshot with { Target = "Target"u8.ToArray() } }
            },
            "runtime instructions" => reviewed with
            {
                Apply = apply with { Snapshot = snapshot with { Runtime = "Runtime"u8.ToArray() } }
            },
            "desired instructions" => reviewed with { Apply = apply with { DesiredRuntime = "Desired"u8.ToArray() } },
            "apply action" => reviewed with { Apply = apply with { WouldReplace = false } },
            "required sources" => reviewed with { RequiredSources = [] },
            "source path" => reviewed with { RequiredSources = [source with { RelativePath = "other.md" }] },
            "source Git mode" => reviewed with { RequiredSources = [source with { CommittedMode = "100755" }] },
            "committed source" => reviewed with
            {
                RequiredSources = [source with { CommittedContent = "Source"u8.ToArray() }]
            },
            "working source" => reviewed with
            {
                RequiredSources = [source with { WorkingContent = "Source"u8.ToArray() }]
            },
            _ => throw new ArgumentOutOfRangeException(nameof(input))
        };

        AssertRefused(reviewed, fresh, input);
    }

    [Theory]
    [InlineData("config proposal")]
    [InlineData("canonical config")]
    [InlineData("runtime config")]
    [InlineData("desired config")]
    [InlineData("config action")]
    [InlineData("config insertion")]
    public void ChangedCodexConfigPlanIsRejected(string input)
    {
        var reviewed = CreateReview(AecIdentifiers.Codex);
        var config = reviewed.Apply.CodexConfig!;
        var changed = input switch
        {
            "config proposal" => null,
            "canonical config" => config with { Canonical = [2] },
            "runtime config" => config with { Runtime = [2] },
            "desired config" => config with { DesiredRuntime = [2] },
            "config action" => config with { WouldReplace = false },
            "config insertion" => config with { Inserted = true },
            _ => throw new ArgumentOutOfRangeException(nameof(input))
        };
        var fresh = reviewed with { Apply = reviewed.Apply with { CodexConfig = changed } };

        AssertRefused(reviewed, fresh, input);
    }

    [Fact]
    public void MissingRuntimeAndEmptyRuntimeAreDistinctInBothDirections()
    {
        var reviewed = CreateReview(AecIdentifiers.Copilot);
        var apply = reviewed.Apply;
        var missing = reviewed with { Apply = apply with { Snapshot = apply.Snapshot with { Runtime = null } } };
        var empty = reviewed with { Apply = apply with { Snapshot = apply.Snapshot with { Runtime = [] } } };

        AssertRefused(missing, empty, "runtime instructions");
        AssertRefused(empty, missing, "runtime instructions");
    }

    private static void AssertRefused(MigrationApplyPreview reviewed, MigrationApplyPreview fresh, string input)
    {
        var error = Assert.Throws<InvalidOperationException>(() => MigrationApplyReview.Verify(reviewed, fresh));
        Assert.Equal($"Migration apply {input} changed after preview; no runtime file was replaced.", error.Message);
    }

    private static MigrationApplyPreview CreateReview(string provider)
    {
        const string commit = "reviewed-commit";
        var repository = Path.GetFullPath("migration-review-repository");
        var home = Path.GetFullPath("migration-review-home");
        var runtimePath = Path.Combine(home,
            provider == AecIdentifiers.Codex ? "AGENTS.md" : "copilot-instructions.md");
        var harness = new MigrationHarness(provider, home, runtimePath);
        var preflight = new MigrationPreflight(
            repository, commit, MigrationPreflight.LegacyFormat, MigrationPreflight.SectionedFormat);
        var preparation = new MigrationPreparation(
            preflight, commit, [harness], new MigrationDirtySections([]), MigrationSectionChoice.Unchanged);
        var snapshot = new PortableRuntimeSnapshot(
            repository, AecIdentifiers.MacOsArm64, provider, runtimePath, commit,
            "target"u8.ToArray(), "runtime"u8.ToArray(), BlockVersion: 1);
        var config = provider == AecIdentifiers.Codex
            ? new PortableCodexConfigPreview(
                Path.Combine(home, "config.toml"), [1], [1], [1], WouldReplace: true, Inserted: false)
            : null;
        var apply = new PortableApplyPreview(
            snapshot, runtimePath, "desired"u8.ToArray(), WouldReplace: true, CodexConfig: config);
        var source = new MigrationValidatedSource("environment/shared/instructions.md",
            "source"u8.ToArray(), "100644", "source"u8.ToArray());

        return new MigrationApplyPreview(preparation, "refs/heads/main", harness, [source], apply);
    }
}
