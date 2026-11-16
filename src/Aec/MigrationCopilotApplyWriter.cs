namespace Aec;

// Apply one confirmed local Copilot runtime. Migration history, sources and
// targets remain repository data; this stage never stages or commits them.
internal static class MigrationCopilotApplyWriter
{
    public static string Apply(MigrationApplyPreview reviewed, Action? afterWriteForTests = null)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        if (reviewed.Harness.Provider != AecIdentifiers.Copilot || reviewed.Apply.Snapshot.Provider != AecIdentifiers.Copilot ||
            reviewed.Apply.CodexConfig is not null)
            throw new InvalidOperationException("A reviewed Copilot migration apply preview is required.");

        var fresh = MigrationApplyPreview.Read(reviewed.Preparation, AecIdentifiers.Copilot);
        MigrationApplyReview.Verify(reviewed, fresh);
        var repository = fresh.Preparation.Preflight.Repository;
        var index = MigrationLegacyCaptureWriter.ReadIndex(Path.Combine(repository, ".git", "index"));
        var protectedFiles = MigrationSourceValidationPreview.ReadProtectedFiles(fresh.Preparation);
        var targetPath = Path.Combine(repository, "environment", "targets",
            fresh.Apply.Snapshot.Platform, AecIdentifiers.Copilot, "copilot-instructions.md");
        protectedFiles.Add(targetPath, fresh.Apply.Snapshot.Target);

        // History was checked at entry. Reuse the bounded mutable-state guard
        // at the two write boundaries, not a full history scan around each step.
        MigrationSourceValidationPreview.VerifyFinalState(fresh.Preparation, fresh.Branch,
            index, new MigrationDirtySections([]), fresh.RequiredSources, protectedFiles);
        try
        {
            var result = PortableInstructionWriter.Apply(fresh.Apply.Snapshot);
            if (result == "applied") afterWriteForTests?.Invoke();

            // Only this expected runtime change is admitted. All repository,
            // raw-index, Codex and other protected bytes keep their entry values.
            protectedFiles[fresh.Harness.RuntimePath] = fresh.Apply.DesiredRuntime;
            MigrationSourceValidationPreview.VerifyFinalState(fresh.Preparation, fresh.Branch,
                index, new MigrationDirtySections([]), fresh.RequiredSources, protectedFiles);
            return result;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          InvalidOperationException or InvalidDataException or ArgumentException)
        {
            // The existing writer can fail after its final move. Preserve the
            // observable state, including external edits; never claim rollback.
            throw new InvalidOperationException(
                $"Copilot migration apply stopped. Runtime instructions at {fresh.Harness.RuntimePath} " +
                "may be updated; inspect them before retrying. No automatic rollback was attempted.", exception);
        }
    }
}
