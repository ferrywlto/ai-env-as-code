namespace Aec;

// The public Copilot backup command is not routed here yet. Recheck a reviewed
// preview before touching canonical files, then commit only the attributed
// source and its affected enrolled targets.
internal static class PortableCopilotBackupWriter
{
    public static string WriteAndCommit(PortableCopilotBackupPreview reviewed)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        var current = RecheckPreview(reviewed);
        if (current.Instructions.Section is null)
        {
            throw new InvalidOperationException("Copilot backup has no reviewed changes.");
        }

        var repository = current.Snapshot.Repository;
        PortableBackupCommitter.RequireLocalBranch(repository);
        var staged = GitProcess.Run(repository, "diff", "--cached", "--quiet", "--exit-code");
        if (staged.ExitCode != 0)
        {
            throw new InvalidOperationException(
                "The repository already has staged changes; Copilot backup stopped before writing.");
        }

        try
        {
            PortableBackupSourceWriter.Write(current.Snapshot, current.Instructions);
            var renderedPaths = PortableBackupTargetWriter.Render(
                current.Snapshot, current.Instructions);
            return PortableBackupCommitter.Commit(
                current.Snapshot, current.Instructions, renderedPaths);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or
                                      InvalidDataException or UnauthorizedAccessException)
        {
            throw new IOException(
                "Copilot backup did not complete; canonical files or the Git index may have " +
                "changed. Inspect the repository before retrying.", error);
        }
    }

    private static PortableCopilotBackupPreview RecheckPreview(
        PortableCopilotBackupPreview reviewed)
    {
        var original = reviewed.Snapshot;
        var providerHome = Path.GetDirectoryName(original.RuntimePath)
            ?? throw new InvalidOperationException(
                "Copilot runtime target has no parent directory.");
        var current = PortableCopilotBackupPreview.Create(original.Repository, providerHome);
        var fresh = current.Snapshot;

        // Attribution is derived from the reviewed snapshot. Compare the
        // underlying inputs, then use the fresh plan instead of trusting
        // mutable fields in the caller's proposal.
        if (!string.Equals(original.Repository, fresh.Repository, StringComparison.Ordinal) ||
            !string.Equals(original.RuntimePath, fresh.RuntimePath, StringComparison.Ordinal) ||
            !string.Equals(original.Commit, fresh.Commit, StringComparison.Ordinal) ||
            original.Runtime is null || fresh.Runtime is null ||
            !original.Runtime.AsSpan().SequenceEqual(fresh.Runtime))
        {
            throw new InvalidOperationException(
                "Copilot backup inputs changed after review; no canonical file was replaced.");
        }

        return current;
    }
}
