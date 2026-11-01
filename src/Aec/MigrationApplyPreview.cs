namespace Aec;

// Read-only migration context around the existing portable apply proposal.
// A later writer must freshly validate these observations, not trust this record.
internal sealed record MigrationApplyPreview(MigrationPreparation Preparation,
    string Branch, MigrationHarness Harness,
    IReadOnlyList<MigrationValidatedSource> RequiredSources, PortableApplyPreview Apply)
{
    public static MigrationApplyPreview Read(MigrationPreparation preparation, string provider)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        if (provider is not ("codex" or "copilot"))
            throw new ArgumentException($"Unsupported migration apply provider: {provider}.", nameof(provider));

        var sources = MigrationSourceValidationPreview.Read(preparation);
        if (sources.ChangedPaths.Count != 0)
            throw new InvalidOperationException("Commit required instruction sources before previewing migration apply.");
        var harness = sources.Preparation.Harnesses.SingleOrDefault(item => item.Provider == provider)
            ?? throw new InvalidOperationException($"Migration apply provider is not locally enrolled: {provider}.");
        var repository = sources.Preparation.Preflight.Repository;
        var index = MigrationLegacyCaptureWriter.ReadIndex(Path.Combine(repository, ".git", "index"));
        var protectedFiles = MigrationSourceValidationPreview.ReadProtectedFiles(sources.Preparation);

        // Reuse normal apply's binding, committed composition and managed-config
        // rules. A legacy runtime may be proposed for migration, never rebound.
        var apply = PortableApplyPreview.Create(repository, provider, harness.Home);
        if (apply.Snapshot.Commit != sources.Preparation.Preflight.Commit)
            throw new InvalidOperationException("Migration apply HEAD changed during preview.");
        if (!SameBytes(apply.Snapshot.Runtime, protectedFiles[harness.RuntimePath]))
            throw new InvalidOperationException("Runtime instructions changed during migration apply preview.");

        var fileName = Path.GetFileName(harness.RuntimePath);
        var relativeTarget = $"environment/targets/{apply.Snapshot.Platform}/{provider}/{fileName}";
        // Normal apply readers are reused, but migration also proves actual Git
        // object provenance, without replacement objects or unbounded reads.
        var committedTarget = MigrationLegacyCapturePreview.ReadHistoricalFile(repository,
            sources.Preparation.Preflight.Commit, relativeTarget);
        if (!committedTarget.AsSpan().SequenceEqual(apply.Snapshot.Target))
            throw new InvalidOperationException("Migration apply target differs from the actual committed Git object.");

        if (apply.CodexConfig is { } config)
        {
            var canonicalPath = Path.Combine(repository, AecApplication.ConfigSourceRelativePath);
            if (!SameBytes(config.Canonical, protectedFiles[canonicalPath]))
                throw new InvalidOperationException("Canonical config changed during migration apply preview.");
            if (!SameBytes(config.Runtime, protectedFiles[config.RuntimePath]))
                throw new InvalidOperationException("Runtime config changed during migration apply preview.");
            var committedConfig = MigrationLegacyCapturePreview.ReadHistoricalFile(repository,
                sources.Preparation.Preflight.Commit, AecApplication.ConfigSourceRelativePath);
            if (!committedConfig.AsSpan().SequenceEqual(config.Canonical))
                throw new InvalidOperationException("Migration apply config differs from the actual committed Git object.");
        }

        // Pin the same committed target that normal apply used. Checking all
        // mutable observations once at return avoids another full history scan.
        var targetPath = Path.Combine(repository, relativeTarget);
        protectedFiles.Add(targetPath, apply.Snapshot.Target);
        MigrationSourceValidationPreview.VerifyFinalState(sources.Preparation, sources.Branch,
            index, new MigrationDirtySections([]), sources.Files, protectedFiles);
        return new MigrationApplyPreview(sources.Preparation, sources.Branch, harness,
            sources.Files, apply);
    }

    private static bool SameBytes(byte[]? before, byte[]? current)
    {
        if (before is null) return current is null;
        if (current is null) return false;
        return before.AsSpan().SequenceEqual(current);
    }
}
