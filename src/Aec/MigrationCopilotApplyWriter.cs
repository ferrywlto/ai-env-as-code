namespace Aec;

// Apply one confirmed local Copilot runtime. Migration history, sources and
// targets remain repository data; this stage never stages or commits them.
internal static class MigrationCopilotApplyWriter
{
    public static string Apply(MigrationApplyPreview reviewed, Action? afterWriteForTests = null)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        if (reviewed.Harness.Provider != "copilot" || reviewed.Apply.Snapshot.Provider != "copilot" ||
            reviewed.Apply.CodexConfig is not null)
            throw new InvalidOperationException("A reviewed Copilot migration apply preview is required.");

        var fresh = MigrationApplyPreview.Read(reviewed.Preparation, "copilot");
        VerifyReview(reviewed, fresh);
        var repository = fresh.Preparation.Preflight.Repository;
        var index = MigrationLegacyCaptureWriter.ReadIndex(Path.Combine(repository, ".git", "index"));
        var protectedFiles = MigrationSourceValidationPreview.ReadProtectedFiles(fresh.Preparation);
        var targetPath = Path.Combine(repository, "environment", "targets",
            fresh.Apply.Snapshot.Platform, "copilot", "copilot-instructions.md");
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

    private static void VerifyReview(MigrationApplyPreview reviewed, MigrationApplyPreview fresh)
    {
        RequireSameValue(reviewed.Branch, fresh.Branch, "branch");
        if (reviewed.Harness != fresh.Harness) throw Changed("local harness");
        var expected = reviewed.Apply.Snapshot;
        var current = fresh.Apply.Snapshot;
        RequireSameValue(expected.Repository, current.Repository, "repository");
        RequireSameValue(expected.Platform, current.Platform, "platform");
        RequireSameValue(expected.Provider, current.Provider, "provider");
        RequireSameValue(expected.RuntimePath, current.RuntimePath, "runtime path");
        RequireSameValue(expected.Commit, current.Commit, "commit");
        if (expected.BlockVersion != current.BlockVersion) throw Changed("runtime AEC block version");
        RequireSameBytes(expected.Target, current.Target, "committed target");
        RequireSameBytes(expected.Runtime, current.Runtime, "runtime instructions");
        RequireSameValue(reviewed.Apply.RuntimePath, fresh.Apply.RuntimePath, "apply destination");
        RequireSameBytes(reviewed.Apply.DesiredRuntime, fresh.Apply.DesiredRuntime, "desired instructions");
        if (reviewed.Apply.WouldReplace != fresh.Apply.WouldReplace) throw Changed("apply action");

        if (reviewed.RequiredSources.Count != fresh.RequiredSources.Count) throw Changed("required sources");
        for (var index = 0; index < fresh.RequiredSources.Count; index++)
        {
            var expectedSource = reviewed.RequiredSources[index];
            var currentSource = fresh.RequiredSources[index];
            RequireSameValue(expectedSource.RelativePath, currentSource.RelativePath, "source path");
            RequireSameValue(expectedSource.CommittedMode, currentSource.CommittedMode, "source Git mode");
            RequireSameBytes(expectedSource.CommittedContent, currentSource.CommittedContent, "committed source");
            RequireSameBytes(expectedSource.WorkingContent, currentSource.WorkingContent, "working source");
        }
    }

    private static void RequireSameValue(string? expected, string? current, string input)
    {
        if (!string.Equals(expected, current, StringComparison.Ordinal)) throw Changed(input);
    }

    private static void RequireSameBytes(byte[]? expected, byte[]? current, string input)
    {
        if (expected is null)
        {
            if (current is not null) throw Changed(input);
            return;
        }
        if (current is null || !expected.AsSpan().SequenceEqual(current)) throw Changed(input);
    }

    private static InvalidOperationException Changed(string input) =>
        new($"Copilot migration apply {input} changed after preview; no runtime instruction was replaced.");
}
