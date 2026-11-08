namespace Aec;

// An interrupted migration may already have portable runtime instructions while
// its completion marker is absent. Preview those through section-aware backup,
// never through legacy whole-file capture. Null means absent or legacy, not unchanged.
internal sealed record MigrationPortableCapturePreview(MigrationPreparation Preparation,
    string Branch, PortableCodexBackupPreview? Codex, PortableCopilotBackupPreview? Copilot)
{
    public bool HasChanges => Codex?.HasChanges == true || Copilot?.Instructions.Section is not null;

    public static MigrationPortableCapturePreview Read(MigrationPreparation preparation)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        var expected = preparation.Preflight;
        var repository = expected.Repository;
        ValidateContext();
        var branch = MigrationLegacyCapturePreview.ReadBranch(repository);
        MigrationLegacyCapturePreview.EnsureOriginalCommit(repository, preparation.OriginalCommit, expected.Commit);
        var runtimes = preparation.Harnesses.ToDictionary(harness => harness.Provider, ReadRuntime, StringComparer.Ordinal);
        PortableCodexBackupPreview? codex = null;
        PortableCopilotBackupPreview? copilot = null;
        foreach (var harness in preparation.Harnesses)
        {
            var bytes = runtimes[harness.Provider];
            var platform = harness.Provider == AecIdentifiers.Codex
                ? AecInstructionBlock.ReadRepositoryBinding(bytes)?.Platform
                : CopilotInstructionBlock.ReadManagedBinding(bytes)?.Platform;
            if (platform is null) continue;

            if (harness.Provider == AecIdentifiers.Codex)
            {
                codex = PortableCodexBackupPreview.Create(repository, harness.Home);
                ValidateSnapshot(codex.Snapshot);
                var relative = AecApplication.ConfigSourceRelativePath;
                var path = Path.Combine(repository, relative);
                var original = CodexPersonalityConfig.ReadCanonical(
                    MigrationLegacyCapturePreview.ReadHistoricalFile(repository, preparation.OriginalCommit, relative), path);
                var current = CodexPersonalityConfig.ReadCanonical(codex.CanonicalConfig, path);
                var runtime = CodexPersonalityConfig.ReadRuntime(codex.RuntimeConfig, codex.RuntimeConfigPath);
                if (!codex.CanonicalConfig.AsSpan().SequenceEqual(
                        MigrationLegacyCapturePreview.ReadHistoricalFile(repository, expected.Commit, relative)))
                {
                    throw new InvalidOperationException("Canonical personality bytes differ from actual Git history.");
                }
                if (original != current && runtime != current)
                {
                    throw new InvalidOperationException("Retrieved canonical personality differs from runtime; reconcile before portable migration capture.");
                }
            }
            else
            {
                copilot = PortableCopilotBackupPreview.Create(repository, harness.Home);
                ValidateSnapshot(copilot.Snapshot);
            }
        }

        // One source can be shared by both previews. Matching proposals are
        // unambiguous; disagreeing runtime edits must never pick a winner.
        if (codex?.Instructions.SourceRelativePath is { } source &&
            source == copilot?.Instructions.SourceRelativePath &&
            codex.Instructions.ProposedContent != copilot.Instructions.ProposedContent)
        {
            throw new InvalidOperationException($"Portable runtime proposals conflict for {source}; reconcile before capture.");
        }

        ValidateContext();
        if (MigrationLegacyCapturePreview.ReadBranch(repository) != branch ||
            preparation.Harnesses.Any(harness => !ReadRuntime(harness).AsSpan().SequenceEqual(runtimes[harness.Provider])))
        {
            throw new InvalidOperationException("Migration branch or runtime bytes changed during portable capture preview.");
        }
        if (codex is not null)
        {
            VerifyTarget(codex.Snapshot);
            AecApplication.EnsureNoLinksInExistingPath(Path.GetDirectoryName(codex.RuntimeConfigPath)!, "Runtime config path");
            if (!AecApplication.ReadRequiredTextFile(codex.RuntimeConfigPath, "Runtime config")
                    .AsSpan().SequenceEqual(codex.RuntimeConfig) ||
                !ApplyCommand.ReadCommittedConfig(repository, expected.Commit).AsSpan().SequenceEqual(codex.CanonicalConfig))
            {
                throw new InvalidOperationException("Codex config changed during portable capture preview.");
            }
        }
        if (copilot is not null) VerifyTarget(copilot.Snapshot);
        if (ApplyCommand.ResolveHeadCommit(repository) != expected.Commit ||
            MigrationLegacyCapturePreview.ReadBranch(repository) != branch)
        {
            throw new InvalidOperationException("Migration HEAD or branch changed during portable capture preview.");
        }
        return new MigrationPortableCapturePreview(preparation, branch, codex, copilot);

        void ValidateContext()
        {
            MigrationFastForward.EnsureSafeGitConfiguration(repository);
            if (MigrationPreflight.Read(repository, expected.TargetFormat.ToString(2)) != expected ||
                preparation.Harnesses.Count == 0 ||
                !MigrationHarnessSelection.Read(expected).SequenceEqual(preparation.Harnesses))
            {
                throw new InvalidOperationException("Migration context changed; restart portable capture preview.");
            }
            _ = MigrationDirtySections.Read(expected, preparation.Harnesses);
        }

        void ValidateSnapshot(PortableRuntimeSnapshot snapshot)
        {
            if (snapshot.Commit != expected.Commit ||
                !runtimes[snapshot.Provider].AsSpan().SequenceEqual(snapshot.Runtime))
            {
                throw new InvalidOperationException("Portable runtime snapshot differs from migration preparation.");
            }
            // Existing validators prove clean working/index bytes. Compare those
            // bytes to real bounded tree blobs too, so replacement refs cannot
            // supply a fictitious authored/target baseline.
            string[] paths = ["environment/shared/instructions.md",
                $"environment/platforms/{snapshot.Platform}/policy.md",
                $"environment/providers/{snapshot.Provider}/overlay.md",
                $"environment/targets/{snapshot.Platform}/{snapshot.Provider}/{Path.GetFileName(snapshot.RuntimePath)}"];
            foreach (var path in paths)
            {
                var actual = MigrationLegacyCapturePreview.ReadHistoricalFile(repository, expected.Commit, path);
                var absolute = Path.Combine(repository, path);
                AecApplication.EnsureNoLinksInExistingPath(Path.GetDirectoryName(absolute)!, "Portable canonical path");
                var working = AecApplication.ReadRequiredTextFile(absolute, "Portable canonical file");
                if (!working.AsSpan().SequenceEqual(actual))
                {
                    throw new InvalidOperationException("Portable canonical bytes differ from actual Git history.");
                }
            }
        }

        void VerifyTarget(PortableRuntimeSnapshot snapshot)
        {
            if (!CommittedTargetValidator.Read(repository, snapshot.Platform, snapshot.Provider).AsSpan().SequenceEqual(snapshot.Target))
            {
                throw new InvalidOperationException("Committed target changed during portable capture preview.");
            }
        }
    }

    private static byte[] ReadRuntime(MigrationHarness harness)
    {
        AecApplication.EnsureNoLinksInExistingPath(harness.Home, "Runtime home path");
        return AecApplication.ReadRequiredTextFile(harness.RuntimePath, "Runtime instructions");
    }
}
