namespace Aec;

internal static class MigrationCaptureCoordinator
{
    public static MigrationPreparation Capture(MigrationPreparation preparation)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        var preflight = preparation.Preflight;
        var repository = preflight.Repository;
        MigrationFastForward.EnsureSafeGitConfiguration(repository);
        if (MigrationPreflight.Read(repository, preflight.TargetFormat.ToString(2)) != preflight ||
            preparation.Harnesses.Count == 0 ||
            !MigrationHarnessSelection.Read(preflight).SequenceEqual(preparation.Harnesses))
        {
            throw new InvalidOperationException("Migration capture context changed or has no enrolled harnesses; restart preparation.");
        }

        var branch = MigrationLegacyCapturePreview.ReadBranch(repository);
        var formats = preparation.Harnesses.Select(harness => (harness.Provider, Portable: IsPortable(harness))).ToArray();
        var portable = formats[0].Portable;
        if (formats.Any(format => format.Portable != portable))
        {
            var details = string.Join(", ", formats.Select(format =>
                $"{format.Provider} ({(format.Portable ? "portable" : "legacy")})"));
            throw new InvalidOperationException($"Enrolled migration runtimes have mixed formats: {details}. " +
                "Inspect both runtimes before restarting. No capture files were written.");
        }

        string? commit;
        if (portable)
        {
            var preview = MigrationPortableCapturePreview.Read(preparation);
            // This preview normally skips legacy runtimes. Require every enrolled
            // provider here so a format change cannot silently narrow capture.
            if (preview.Branch != branch || preparation.Harnesses.Any(harness =>
                harness.Provider == AecIdentifiers.Codex ? preview.Codex is null : preview.Copilot is null))
            {
                throw new InvalidOperationException("Migration branch or runtime format changed during capture routing; restart preparation.");
            }
            commit = MigrationPortableCaptureCommitter.WriteAndCommit(preview);
        }
        else
        {
            var preview = MigrationLegacyCapturePreview.Read(preparation);
            if (preview.Branch != branch)
                throw new InvalidOperationException("Migration branch changed during capture routing; restart preparation.");
            commit = MigrationLegacyCaptureCommitter.WriteAndCommit(preview);
        }

        // Each committer proves its child and exact paths. At the handoff, do
        // not adopt an unrelated HEAD or refresh the operator's section choice.
        var expected = commit ?? preflight.Commit;
        if (ApplyCommand.ResolveHeadCommit(repository) != expected ||
            MigrationLegacyCapturePreview.ReadBranch(repository) != branch)
        {
            throw new InvalidOperationException($"Migration HEAD or branch changed at the capture handoff. " +
                $"Expected HEAD: {expected}. Inspect the checkout before restarting.");
        }

        return commit is null ? preparation : preparation with { Preflight = preflight with { Commit = commit } };
    }

    private static bool IsPortable(MigrationHarness harness)
    {
        AecApplication.EnsureNoLinksInExistingPath(harness.Home, "Runtime home path");
        var runtime = AecApplication.ReadRequiredTextFile(harness.RuntimePath, "Runtime instructions");
        return harness.Provider == AecIdentifiers.Codex
            ? AecInstructionBlock.ReadRepositoryBinding(runtime)?.Platform is not null
            : CopilotInstructionBlock.ReadManagedBinding(runtime)?.Platform is not null;
    }
}
