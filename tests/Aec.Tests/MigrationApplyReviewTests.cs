namespace Aec.Tests;

[Trait("Category", "Unit")]
public sealed class MigrationApplyReviewTests
{
    [Fact]
    public void EmptyReviewSetIsRejected()
    {
        var error = Assert.Throws<ArgumentException>(() => MigrationApplyCoordinator.Apply([]));
        Assert.Contains("requires reviewed enrolled providers", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("missing", "Review every enrolled provider")]
    [InlineData("duplicate", "Duplicate migration apply provider")]
    [InlineData("preparation", "different original commits")]
    [InlineData("unsupported", "Unsupported migration apply provider")]
    public void InvalidReviewSetIsRejected(string kind, string diagnostic)
    {
        var codex = CreateReview(AecIdentifiers.Codex);
        var copilot = CreateReview(AecIdentifiers.Copilot);
        var preparation = codex.Preparation with { Harnesses = [codex.Harness, copilot.Harness] };
        codex = codex with { Preparation = preparation };
        copilot = copilot with { Preparation = preparation };
        MigrationApplyPreview[] reviews = kind switch
        {
            "missing" => [codex],
            "duplicate" => [codex, codex],
            "preparation" => [codex, copilot with { Preparation = preparation with { OriginalCommit = "different" } }],
            "unsupported" => [codex, copilot with { Harness = copilot.Harness with { Provider = "other" } }],
            _ => throw new ArgumentOutOfRangeException(nameof(kind))
        };

        var error = Assert.Throws<ArgumentException>(() => MigrationApplyCoordinator.Apply(reviews));
        Assert.Contains(diagnostic, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CodexWriterRejectsAnInvalidReview(bool missingConfig)
    {
        var review = CreateReview(missingConfig ? AecIdentifiers.Codex : AecIdentifiers.Copilot);
        if (missingConfig)
            review = review with { Apply = review.Apply with { CodexConfig = null } };

        var error = Assert.Throws<ArgumentException>(() => MigrationCodexApplyWriter.Apply(review));
        Assert.Contains("reviewed Codex migration apply preview is required", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CopilotWriterRejectsACodexReview()
    {
        var review = CreateReview(AecIdentifiers.Codex);
        var error = Assert.Throws<InvalidOperationException>(() => MigrationCopilotApplyWriter.Apply(review));
        Assert.Equal("A reviewed Copilot migration apply preview is required.", error.Message);
    }

    [Fact]
    public void UnsupportedPreviewProviderIsRejected()
    {
        var review = CreateReview(AecIdentifiers.Codex);
        var error = Assert.Throws<ArgumentException>(() =>
            MigrationApplyPreview.Read(review.Preparation, "other"));
        Assert.Contains("Unsupported migration apply provider: other", error.Message, StringComparison.Ordinal);
    }

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

    [Theory]
    [InlineData("codex")]
    [InlineData("target")]
    [InlineData("exception")]
    public void FailureAfterFirstProviderReportsPartialApplyAndStopsSecond(string mutation)
    {
        var codex = CreateReview(AecIdentifiers.Codex);
        var copilot = CreateReview(AecIdentifiers.Copilot);
        var preparation = codex.Preparation with { Harnesses = [codex.Harness, copilot.Harness] };
        var config = codex.Apply.CodexConfig! with { DesiredRuntime = [2] };
        codex = codex with { Preparation = preparation, Apply = codex.Apply with { CodexConfig = config } };
        copilot = copilot with { Preparation = preparation };
        var repository = preparation.Preflight.Repository;
        var codexRuntime = codex.Harness.RuntimePath;
        var copilotRuntime = copilot.Harness.RuntimePath;
        var canonicalConfig = Path.Combine(repository, AecApplication.ConfigSourceRelativePath);
        var codexTarget = Path.Combine(repository, "environment", "targets",
            codex.Apply.Snapshot.Platform, codex.Harness.Provider, "AGENTS.md");
        var copilotTarget = Path.Combine(repository, "environment", "targets",
            copilot.Apply.Snapshot.Platform, copilot.Harness.Provider, "copilot-instructions.md");
        var protectedFiles = new Dictionary<string, byte[]?>(StringComparer.Ordinal)
        {
            [codexRuntime] = null,
            [copilotRuntime] = null,
            [config.RuntimePath] = null,
            [canonicalConfig] = null
        };
        var observed = new Dictionary<string, byte[]?>(StringComparer.Ordinal)
        {
            [codexRuntime] = codex.Apply.Snapshot.Runtime,
            [copilotRuntime] = copilot.Apply.Snapshot.Runtime,
            [config.RuntimePath] = config.Runtime,
            [canonicalConfig] = config.Canonical,
            [codexTarget] = codex.Apply.Snapshot.Target,
            [copilotTarget] = copilot.Apply.Snapshot.Target
        };
        var changedPath = mutation switch
        {
            "codex" => codexRuntime,
            "target" => copilotTarget,
            "exception" => null,
            _ => throw new ArgumentOutOfRangeException(nameof(mutation), mutation, null)
        };
        var writers = new List<string>();
        var callbacks = new List<string>();
        var verificationCount = 0;
        var injected = new IOException("Injected boundary failure");
        var externalBytes = "External change."u8.ToArray();

        var error = Assert.Throws<InvalidOperationException>(() =>
            MigrationApplyCoordinator.ApplyReviewedProviders(
                [codex, copilot], protectedFiles,
                preview =>
                {
                    var provider = preview.Harness.Provider;
                    writers.Add(provider);
                    observed[preview.Harness.RuntimePath] = preview.Apply.DesiredRuntime;
                    if (preview.Apply.CodexConfig is { } proposal)
                        observed[proposal.RuntimePath] = proposal.DesiredRuntime;
                    return "applied";
                },
                () =>
                {
                    verificationCount++;
                    MigrationSourceValidationPreview.VerifyProtectedFiles(protectedFiles, file => observed[file]);
                },
                provider =>
                {
                    callbacks.Add(provider);
                    if (changedPath is null) throw injected;
                    observed[changedPath] = externalBytes;
                }));

        Assert.Equal(new[] { AecIdentifiers.Codex }, writers);
        Assert.Equal(writers, callbacks);
        Assert.Equal(changedPath is null ? 1 : 2, verificationCount);
        Assert.Contains("while completing codex. Completed providers: codex (applied).", error.Message,
            StringComparison.Ordinal);
        Assert.Contains(codexRuntime, error.Message, StringComparison.Ordinal);
        Assert.Contains(copilotRuntime, error.Message, StringComparison.Ordinal);
        Assert.Contains(config.RuntimePath, error.Message, StringComparison.Ordinal);
        Assert.Contains("No automatic rollback was attempted", error.Message, StringComparison.Ordinal);
        Assert.Equal(codex.Apply.DesiredRuntime, protectedFiles[codexRuntime]);
        Assert.Equal(config.DesiredRuntime, protectedFiles[config.RuntimePath]);
        Assert.Equal(config.Canonical, protectedFiles[canonicalConfig]);
        Assert.Equal(copilot.Apply.Snapshot.Runtime, protectedFiles[copilotRuntime]);
        Assert.Equal(codex.Apply.Snapshot.Target, protectedFiles[codexTarget]);
        Assert.Equal(copilot.Apply.Snapshot.Target, protectedFiles[copilotTarget]);
        Assert.Equal(copilot.Apply.Snapshot.Runtime, observed[copilotRuntime]);

        if (changedPath is null)
        {
            Assert.Same(injected, error.InnerException);
            Assert.Equal(codex.Apply.DesiredRuntime, observed[codexRuntime]);
        }
        else
        {
            var cause = Assert.IsType<InvalidOperationException>(error.InnerException);
            Assert.Equal($"Protected migration input changed during source validation: {changedPath}.", cause.Message);
            Assert.Equal(externalBytes, observed[changedPath]);
        }
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
