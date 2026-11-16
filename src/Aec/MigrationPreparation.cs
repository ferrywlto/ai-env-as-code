namespace Aec;

// The choice remains advisory: no section bytes are overwritten here, and a
// later writer must obtain fresh validation/approval rather than trust this record.
internal sealed record MigrationPreparation(MigrationPreflight Preflight,
    string OriginalCommit,
    IReadOnlyList<MigrationHarness> Harnesses, MigrationDirtySections DirtySections,
    MigrationSectionChoice SectionChoice)
{
    public static MigrationPreparation? Run(string repository, string targetVersion,
        TextReader input, TextWriter output, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return null;
        repository = AecApplication.RequireAbsolutePath(repository, "--repo");
        AecApplication.EnsureNoLinksInExistingPath(repository, "Repository path");
        AecApplication.EnsureRealDirectory(repository, "Repository");
        // Guard before the composed validators: Git status/diff can themselves
        // invoke configured filters or fsmonitor, not just the final checkout.
        MigrationFastForward.EnsureSafeGitConfiguration(repository);
        var original = MigrationPreflight.Read(repository, targetVersion);
        var harnesses = MigrationHarnessSelection.Read(original);
        var dirty = MigrationDirtySections.Read(original, harnesses);
        var reviewedSections = ReadSectionState(original, dirty);
        var choice = dirty.Prompt(input, output, cancellationToken);
        if (choice == MigrationSectionChoice.Cancelled || cancellationToken.IsCancellationRequested) return null;

        // The user may have changed the checkout while considering the prompt.
        // Revalidate before the first remote operation; a choice waives no guard.
        MigrationFastForward.EnsureSafeGitConfiguration(repository);
        if (MigrationPreflight.Read(repository, targetVersion) != original ||
            !MigrationHarnessSelection.Read(original).SequenceEqual(harnesses) ||
            !MigrationDirtySections.Read(original, harnesses).Paths.SequenceEqual(dirty.Paths) ||
            !ReadSectionState(original, dirty).SequenceEqual(reviewedSections))
        {
            throw new InvalidOperationException("Migration context changed during section review; restart preparation.");
        }
        if (cancellationToken.IsCancellationRequested) return null;
        var remote = MigrationRemotePreflight.Read(original);
        if (cancellationToken.IsCancellationRequested) return null;
        var fetched = MigrationFetchPreparation.Read(original, remote);
        if (cancellationToken.IsCancellationRequested) return null;

        MigrationPreflight prepared;
        try
        {
            prepared = MigrationFastForward.Execute(original, fetched, harnesses, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Execute checks cancellation only before advancement. Received
            // objects may remain, but no advanced commit is hidden by null.
            return null;
        }
        try
        {
            return new MigrationPreparation(prepared, original.Commit, harnesses,
                MigrationDirtySections.Read(prepared, harnesses), choice);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or IOException or InvalidDataException)
        {
            throw new InvalidOperationException("Migration final section validation failed; retrieved history is retained. Inspect the checkout before restarting. No rollback, capture or apply was attempted.", exception);
        }
    }

    private static string[] ReadSectionState(MigrationPreflight preflight, MigrationDirtySections dirty)
    {
        if (dirty.Paths.Count == 0) return [];
        // Path equality alone misses edits or staging changes during the prompt.
        // Capture exact working bytes (including absence) and staged blob entries;
        // Base64 is only a lossless in-memory comparison, never persisted content.
        var index = GitProcess.RunRequired(preflight.Repository, "Git could not inspect reviewed sections",
            ["ls-files", "--stage", "-z", "--", .. dirty.Paths]).Output;
        return [index, .. dirty.Paths.Select(path =>
        {
            var bytes = AecApplication.ReadOptionalTextFile(Path.Combine(preflight.Repository, path), "Migration section");
            return bytes is null ? "-" : Convert.ToBase64String(bytes);
        })];
    }
}
