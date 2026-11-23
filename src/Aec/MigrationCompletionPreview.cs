namespace Aec;

// Advisory review data only. A later writer must refresh all observations;
// neither caller-owned buffers nor a successful preview authorize completion.
internal sealed record MigrationCompletionPreview(
    MigrationPreparation Preparation,
    string Branch,
    IReadOnlyList<MigrationProviderStatus> Providers,
    IReadOnlyList<MigrationValidatedSource> Sources,
    byte[] Index,
    IReadOnlyDictionary<string, byte[]?> ProtectedFiles)
{
    public const string MarkerRelativePath = MigrationPreflight.FormatRelativePath;
    public const string MarkerGitMode = "100644";
    public string MarkerContent => Preparation.Preflight.TargetFormat.ToString(2) + "\n";

    public static MigrationCompletionPreview Read(MigrationPreparation preparation,
        Action? beforeFinalCheckForTests = null)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        var sources = MigrationSourceValidationPreview.Read(preparation);
        if (sources.ChangedPaths.Count != 0)
            throw new InvalidOperationException("Commit required sources before previewing migration completion.");
        var current = sources.Preparation;
        var repository = current.Preflight.Repository;
        var index = MigrationLegacyCaptureWriter.ReadIndex(Path.Combine(repository, ".git", "index"));
        var protectedFiles = MigrationSourceValidationPreview.ReadProtectedFiles(current);
        MigrationStatusGate.PinHistoricalFiles(current, protectedFiles);

        var providers = MigrationStatusGate.Verify(current);
        beforeFinalCheckForTests?.Invoke();
        // Check the whole preview boundary, not each read. This detects
        // persistent drift; it provides neither a lock nor an atomic snapshot.
        MigrationSourceValidationPreview.VerifyFinalState(current, sources.Branch,
            index, new MigrationDirtySections([]), sources.Files, protectedFiles);
        return new MigrationCompletionPreview(current, sources.Branch, providers,
            sources.Files, index, protectedFiles);
    }
}
