namespace Aec;

internal sealed record MigrationProviderApplyResult(string Provider, string Result);

// Coordinate only runtime deployment. Existing provider writers retain their
// own safeguards; repository commits and migration completion are separate steps.
internal static class MigrationApplyCoordinator
{
    public static IReadOnlyList<MigrationProviderApplyResult> Apply(
        IReadOnlyList<MigrationApplyPreview> reviewed, Action<string>? afterProviderForTests = null)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        var fresh = RefreshReviews(reviewed);
        var preparation = fresh[0].Preparation;
        var repository = preparation.Preflight.Repository;
        var index = MigrationLegacyCaptureWriter.ReadIndex(Path.Combine(repository, ".git", "index"));
        var protectedFiles = MigrationSourceValidationPreview.ReadProtectedFiles(preparation);
        foreach (var preview in fresh)
        {
            // Pin every selected provider, not just the one currently writing.
            // Use fresh preview bytes so later reads cannot admit unnoticed drift.
            protectedFiles[preview.Harness.RuntimePath] = preview.Apply.Snapshot.Runtime;
            var target = Path.Combine(repository, "environment", "targets",
                preview.Apply.Snapshot.Platform, preview.Harness.Provider,
                Path.GetFileName(preview.Harness.RuntimePath));
            protectedFiles.Add(target, preview.Apply.Snapshot.Target);
            if (preview.Apply.CodexConfig is { } config)
            {
                protectedFiles[config.RuntimePath] = config.Runtime;
                protectedFiles[Path.Combine(repository, AecApplication.ConfigSourceRelativePath)] = config.Canonical;
            }
        }

        void VerifyState() => MigrationSourceValidationPreview.VerifyFinalState(
            preparation, fresh[0].Branch, index, new MigrationDirtySections([]),
            fresh[0].RequiredSources, protectedFiles);

        VerifyState();
        var completed = new List<MigrationProviderApplyResult>();
        foreach (var preview in fresh)
        {
            var provider = preview.Harness.Provider;
            try
            {
                var result = provider == AecIdentifiers.Codex
                    ? MigrationCodexApplyWriter.Apply(preview)
                    : MigrationCopilotApplyWriter.Apply(preview);

                // Admit only a successful writer's expected bytes. The next
                // provider keeps its original review, not a newly accepted edit.
                protectedFiles[preview.Harness.RuntimePath] = preview.Apply.DesiredRuntime;
                if (preview.Apply.CodexConfig is { } config)
                    protectedFiles[config.RuntimePath] = config.DesiredRuntime;
                completed.Add(new MigrationProviderApplyResult(provider, result));
                afterProviderForTests?.Invoke(provider);
                VerifyState();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                              InvalidOperationException or InvalidDataException or ArgumentException)
            {
                var names = completed.Count == 0 ? "none" : string.Join(", ", completed.Select(item =>
                    $"{item.Provider} ({item.Result})"));
                var paths = fresh.Select(item => item.Harness.RuntimePath).ToList();
                var codexConfig = fresh.Select(item => item.Apply.CodexConfig).FirstOrDefault(config => config is not null);
                if (codexConfig is not null) paths.Add(codexConfig.RuntimePath);
                throw new InvalidOperationException(
                    $"Migration apply stopped while completing {provider}. Completed providers: {names}. " +
                    $"Runtime files may be updated; inspect {string.Join(", ", paths)}. " +
                    "Later providers were not applied by this coordinator. No automatic rollback was attempted.", exception);
            }
        }
        return completed.ToArray();
    }

    private static MigrationApplyPreview[] RefreshReviews(IReadOnlyList<MigrationApplyPreview> reviewed)
    {
        if (reviewed.Count == 0 || reviewed.Any(preview => preview is null))
            throw new ArgumentException("Migration apply requires reviewed enrolled providers.", nameof(reviewed));
        var reviews = reviewed.ToArray();
        var preparation = reviews[0].Preparation;
        if (reviews.Length != preparation.Harnesses.Count)
            throw new ArgumentException("Review every enrolled provider before migration apply.", nameof(reviewed));

        var providers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var preview in reviews)
        {
            var provider = preview.Harness.Provider;
            if (provider is not (AecIdentifiers.Codex or AecIdentifiers.Copilot))
                throw new ArgumentException($"Unsupported migration apply provider: {provider}.", nameof(reviewed));
            if (!providers.Add(provider))
                throw new ArgumentException($"Duplicate migration apply provider: {provider}.", nameof(reviewed));
            VerifySamePreparation(preparation, preview.Preparation);
        }

        // Check every review before the first write, in Codex then Copilot order.
        // Each writer will refresh again at its independently safe entry point.
        var fresh = new List<MigrationApplyPreview>();
        foreach (var provider in new[] { AecIdentifiers.Codex, AecIdentifiers.Copilot })
        {
            var preview = reviews.SingleOrDefault(item => item.Harness.Provider == provider);
            if (preview is null) continue;
            var current = MigrationApplyPreview.Read(preparation, provider);
            MigrationApplyReview.Verify(preview, current);
            fresh.Add(current);
        }
        return fresh.ToArray();
    }

    private static void VerifySamePreparation(MigrationPreparation expected, MigrationPreparation current)
    {
        // Collection-valued records use reference equality by default. Compare
        // their contents so separately read, equivalent reviews remain valid.
        if (expected.Preflight != current.Preflight)
            throw new ArgumentException("Migration apply reviews use different preflights.");
        if (expected.OriginalCommit != current.OriginalCommit)
            throw new ArgumentException("Migration apply reviews use different original commits.");
        if (!expected.Harnesses.SequenceEqual(current.Harnesses))
            throw new ArgumentException("Migration apply reviews use different enrolled providers.");
        if (expected.SectionChoice != current.SectionChoice)
            throw new ArgumentException("Migration apply reviews use different section choices.");
        if (!expected.DirtySections.Paths.SequenceEqual(current.DirtySections.Paths, StringComparer.Ordinal))
            throw new ArgumentException("Migration apply reviews use different dirty-section paths.");
    }
}
