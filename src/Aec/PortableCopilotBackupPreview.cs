namespace Aec;

// Copilot backup first reviews the selected committed target and attributes at
// most one runtime instruction edit. A later writer must revalidate before any
// canonical replacement; this preview never writes or commits.
internal sealed record PortableCopilotBackupPreview(
    PortableRuntimeSnapshot Snapshot,
    PortableBackupAttributionPreview Instructions)
{
    public static PortableCopilotBackupPreview Create(string repository, string copilotHome)
    {
        var snapshot = PortableRuntimeSnapshot.Read(repository, "copilot", copilotHome);
        var instructions = PortableBackupAttributionPreview.Create(snapshot);
        if (!string.Equals(snapshot.Commit, ApplyCommand.ResolveHeadCommit(snapshot.Repository),
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Repository HEAD changed during Copilot backup preflight.");
        }

        return new PortableCopilotBackupPreview(snapshot, instructions);
    }
}
