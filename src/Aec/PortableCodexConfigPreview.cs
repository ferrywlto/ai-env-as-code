namespace Aec;

// This captures a proposed config edit, not permission to write it. A later
// writer must compare the committed and runtime bytes again before replacing.
internal sealed record PortableCodexConfigPreview(
    string RuntimePath,
    byte[] Canonical,
    byte[]? Runtime,
    byte[] DesiredRuntime,
    bool WouldReplace,
    bool Inserted)
{
    public static PortableCodexConfigPreview Create(PortableRuntimeSnapshot snapshot)
    {
        if (snapshot.Provider != "codex")
        {
            throw new ArgumentException("Codex config requires a Codex runtime snapshot.");
        }

        var canonicalPath = Path.Combine(
            snapshot.Repository, AecApplication.ConfigSourceRelativePath);
        AecApplication.EnsureNoLinksInExistingPath(
            Path.GetDirectoryName(canonicalPath)!, "Canonical config path");
        var canonical = ApplyCommand.ReadCommittedConfig(snapshot.Repository, snapshot.Commit);
        var desiredPersonality = CodexPersonalityConfig.ReadCanonical(canonical, canonicalPath);

        var providerHome = Path.GetDirectoryName(snapshot.RuntimePath)
            ?? throw new InvalidOperationException("Codex runtime target has no parent directory.");
        ApplyCommand.EnsureRuntimeOutsideRepository(
            snapshot.Repository, providerHome, "config.toml", "Codex runtime config");
        var runtimePath = Path.Combine(providerHome, "config.toml");
        var runtime = AecApplication.ReadOptionalTextFile(runtimePath, "Runtime config");
        var update = CodexPersonalityConfig.PlanRuntimeUpdate(
            runtime, runtimePath, desiredPersonality);

        // The target and config must be derived from one HEAD. This preview is
        // still stale as soon as it returns; the writer will revalidate later.
        if (!string.Equals(snapshot.Commit, ApplyCommand.ResolveHeadCommit(snapshot.Repository),
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Repository HEAD changed during Codex config preflight.");
        }

        return new PortableCodexConfigPreview(
            runtimePath,
            canonical,
            runtime,
            update.Content,
            update.Changed,
            update.Inserted);
    }
}
