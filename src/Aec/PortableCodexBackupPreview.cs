namespace Aec;

// This combines two read-only observations. The writer revalidates their
// runtime inputs and HEAD before changing either canonical source file.
internal sealed record PortableCodexBackupPreview(
    PortableRuntimeSnapshot Snapshot,
    PortableBackupAttributionPreview Instructions,
    string RuntimeConfigPath,
    byte[] CanonicalConfig,
    byte[] RuntimeConfig,
    byte[] ProposedCanonicalConfig,
    bool ConfigWouldChange)
{
    public bool HasChanges => Instructions.Section is not null || ConfigWouldChange;

    public static PortableCodexBackupPreview Create(string repository, string codexHome)
    {
        var snapshot = PortableRuntimeSnapshot.Read(repository, "codex", codexHome);
        var instructions = PortableBackupAttributionPreview.Create(snapshot);

        var canonicalPath = Path.Combine(
            snapshot.Repository, AecApplication.ConfigSourceRelativePath);
        AecApplication.EnsureNoLinksInExistingPath(
            Path.GetDirectoryName(canonicalPath)!, "Canonical config path");
        var canonical = ApplyCommand.ReadCommittedConfig(snapshot.Repository, snapshot.Commit);

        var providerHome = Path.GetDirectoryName(snapshot.RuntimePath)
            ?? throw new InvalidOperationException("Codex runtime target has no parent directory.");
        ApplyCommand.EnsureRuntimeOutsideRepository(
            snapshot.Repository, providerHome, "config.toml", "Codex runtime config");
        var runtimePath = Path.Combine(providerHome, "config.toml");
        var runtime = AecApplication.ReadOptionalTextFile(runtimePath, "Runtime config");
        if (runtime is null ||
            CodexPersonalityConfig.ReadRuntime(runtime, runtimePath) is not { } personality)
        {
            throw new MissingRuntimePersonalityException(
                "runtime config does not declare root `personality`; " +
                "backup stopped without changing the repository.");
        }

        var update = CodexPersonalityConfig.PlanCanonicalUpdate(
            canonical, canonicalPath, personality);
        if (!string.Equals(snapshot.Commit, ApplyCommand.ResolveHeadCommit(snapshot.Repository),
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Repository HEAD changed during Codex backup preflight.");
        }

        return new PortableCodexBackupPreview(
            snapshot,
            instructions,
            runtimePath,
            canonical,
            runtime,
            update.Content,
            update.Changed);
    }
}

internal sealed class MissingRuntimePersonalityException(string message)
    : InvalidOperationException(message);
