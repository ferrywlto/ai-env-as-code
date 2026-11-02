namespace Aec;

internal sealed record MigrationProviderStatus(string Provider, PortableStatusReport Status);

// A successful observation is not migration completion or permission to write
// a format marker. That later step must validate its own current inputs.
internal static class MigrationStatusGate
{
    public static IReadOnlyList<MigrationProviderStatus> Verify(
        MigrationPreparation preparation, Action<string>? afterProviderForTests = null)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        var sources = MigrationSourceValidationPreview.Read(preparation);
        if (sources.ChangedPaths.Count != 0)
            throw new InvalidOperationException("Commit required sources before verifying migration status.");
        var current = sources.Preparation;
        var repository = current.Preflight.Repository;
        var index = MigrationLegacyCaptureWriter.ReadIndex(Path.Combine(repository, ".git", "index"));
        var protectedFiles = MigrationSourceValidationPreview.ReadProtectedFiles(current);
        foreach (var harness in current.Harnesses)
        {
            var relative = $"environment/targets/{RenderCommand.CurrentPlatform()}/{harness.Provider}/" +
                Path.GetFileName(harness.RuntimePath);
            // Pin actual committed target bytes, not a replacement Git object
            // or an independently edited working target.
            protectedFiles.Add(Path.Combine(repository, relative),
                MigrationLegacyCapturePreview.ReadHistoricalFile(repository, current.Preflight.Commit, relative));
            if (harness.Provider == "codex")
            {
                var configPath = AecApplication.ConfigSourceRelativePath;
                protectedFiles[Path.Combine(repository, configPath)] =
                    MigrationLegacyCapturePreview.ReadHistoricalFile(repository, current.Preflight.Commit, configPath);
            }
        }

        var reports = new List<MigrationProviderStatus>();
        foreach (var harness in current.Harnesses)
        {
            var status = PortableEnvironmentStatus.Inspect(repository, harness.Provider, harness.Home);
            reports.Add(new MigrationProviderStatus(harness.Provider, status));
            afterProviderForTests?.Invoke(harness.Provider);
        }

        // One phase-wide guard prevents combining observations from different
        // commits or runtime states. Individual status reads remain read-only.
        MigrationSourceValidationPreview.VerifyFinalState(current, sources.Branch,
            index, new MigrationDirtySections([]), sources.Files, protectedFiles);
        foreach (var report in reports)
        {
            if (!report.Status.InSync)
                throw new InvalidOperationException(
                    $"Migration status is not in sync for {report.Provider}: " +
                    $"instructions={report.Status.Instructions}, config={report.Status.Config ?? "not applicable"}. " +
                    "Migration remains incomplete; no repair or completion marker was written.");
        }
        return reports.ToArray();
    }
}
