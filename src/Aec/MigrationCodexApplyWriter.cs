namespace Aec;

// Reuse ordinary Codex config-first apply, adding migration-specific guards
// at its actual write boundaries. This stage never changes repository data.
internal static class MigrationCodexApplyWriter
{
    public static string Apply(MigrationApplyPreview reviewed,
        Action? afterConfigWriteForTests = null, Action? afterWriteForTests = null)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        if (reviewed.Harness.Provider != AecIdentifiers.Codex || reviewed.Apply.Snapshot.Provider != AecIdentifiers.Codex ||
            reviewed.Apply.CodexConfig is null)
            throw new ArgumentException("A reviewed Codex migration apply preview is required.", nameof(reviewed));

        var fresh = MigrationApplyPreview.Read(reviewed.Preparation, AecIdentifiers.Codex);
        MigrationApplyReview.Verify(reviewed, fresh);
        var config = fresh.Apply.CodexConfig!;
        var repository = fresh.Preparation.Preflight.Repository;
        var index = MigrationLegacyCaptureWriter.ReadIndex(Path.Combine(repository, ".git", "index"));
        var protectedFiles = MigrationSourceValidationPreview.ReadProtectedFiles(fresh.Preparation);
        var targetPath = Path.Combine(repository, "environment", "targets",
            fresh.Apply.Snapshot.Platform, AecIdentifiers.Codex, "AGENTS.md");
        protectedFiles.Add(targetPath, fresh.Apply.Snapshot.Target);

        void VerifyState() => MigrationSourceValidationPreview.VerifyFinalState(
            fresh.Preparation, fresh.Branch, index, new MigrationDirtySections([]),
            fresh.RequiredSources, protectedFiles);

        void VerifyBeforeInstructions()
        {
            // Ordinary apply has completed and verified its config step. Admit
            // only that known change before checking the instruction boundary.
            protectedFiles[config.RuntimePath] = config.DesiredRuntime;
            VerifyState();
        }

        VerifyState();
        // Keep ordinary apply's precise pre-write and partial-write errors.
        // Production guards are separate from callbacks used to simulate edits.
        var result = PortableCodexWriter.Apply(fresh.Apply, afterConfigWriteForTests,
            beforeConfigWrite: VerifyState, beforeInstructionWrite: VerifyBeforeInstructions);

        protectedFiles[config.RuntimePath] = config.DesiredRuntime;
        protectedFiles[fresh.Harness.RuntimePath] = fresh.Apply.DesiredRuntime;
        try
        {
            // This hook observes instruction replacement; the ordinary writer
            // owns the separate after-config hook, including config-only apply.
            if (fresh.Apply.WouldReplace) afterWriteForTests?.Invoke();
            VerifyState();
        }
        catch (Exception exception) when (result == "applied" &&
            exception is (IOException or UnauthorizedAccessException or InvalidOperationException or
                InvalidDataException or ArgumentException))
        {
            throw new InvalidOperationException(
                $"Codex migration apply stopped after writing. Runtime config at {config.RuntimePath} " +
                $"and instructions at {fresh.Harness.RuntimePath} may be updated; inspect both before retrying. " +
                "No automatic rollback was attempted.", exception);
        }
        return result;
    }
}
