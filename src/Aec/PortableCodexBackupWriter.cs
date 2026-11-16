namespace Aec;

// Public Codex backup uses this guarded 2.0 writer after read-only preflight.
// Each file is guarded individually; a failed later step may leave earlier
// canonical replacements uncommitted, so callers must inspect before retrying.
internal static class PortableCodexBackupWriter
{
    public static string WriteAndCommit(PortableCodexBackupPreview reviewed)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        var current = RecheckPreview(reviewed);
        if (!current.HasChanges)
        {
            throw new InvalidOperationException("Codex backup has no reviewed changes.");
        }

        // A detached or non-local symbolic HEAD cannot receive the commit.
        // Check it before replacing any canonical source.
        PortableBackupCommitter.RequireLocalBranch(current.Snapshot.Repository);

        // Refuse an existing index before any source replacement, not only at
        // the final commit, so unrelated staged work is never mixed into it.
        var staged = GitProcess.Run(current.Snapshot.Repository,
            "diff", "--cached", "--quiet", "--exit-code");
        if (staged.ExitCode != 0)
        {
            throw new InvalidOperationException(
                "The repository already has staged changes; Codex backup stopped before writing.");
        }

        var writeStarted = false;
        try
        {
            if (current.ConfigWouldChange)
            {
                writeStarted = true;
                var canonicalPath = Path.Combine(
                    current.Snapshot.Repository, AecApplication.ConfigSourceRelativePath);
                AtomicFile.ReplaceIfUnchanged(canonicalPath, current.CanonicalConfig,
                    current.ProposedCanonicalConfig, "Canonical config");
            }

            IReadOnlyList<string> renderedPaths = [];
            if (current.Instructions.Section is not null)
            {
                // The existing source writer rechecks instruction provenance;
                // the target writer verifies every affected enrollment.
                writeStarted = true;
                PortableBackupSourceWriter.Write(current.Snapshot, current.Instructions);
                renderedPaths = PortableBackupTargetWriter.Render(
                    current.Snapshot, current.Instructions);
            }

            return PortableBackupCommitter.Commit(current, renderedPaths);
        }
        catch (Exception error) when (writeStarted &&
                                      error is IOException or InvalidOperationException or
                                          InvalidDataException or UnauthorizedAccessException)
        {
            throw new IOException(
                "Codex backup did not complete; canonical files or the Git index may have " +
                "changed. Inspect the repository before retrying.", error);
        }
    }

    private static PortableCodexBackupPreview RecheckPreview(
        PortableCodexBackupPreview reviewed)
    {
        var providerHome = Path.GetDirectoryName(reviewed.Snapshot.RuntimePath)
            ?? throw new InvalidOperationException("Codex runtime target has no parent directory.");
        var current = PortableCodexBackupPreview.Create(
            reviewed.Snapshot.Repository, providerHome);
        var original = reviewed.Snapshot;
        var fresh = current.Snapshot;

        // The preview's plan is derived from these inputs. Use the fresh plan
        // after comparing them, instead of trusting or comparing every derived field.
        if (!string.Equals(original.Repository, fresh.Repository, StringComparison.Ordinal) ||
            !string.Equals(original.RuntimePath, fresh.RuntimePath, StringComparison.Ordinal) ||
            !string.Equals(original.Commit, fresh.Commit, StringComparison.Ordinal) ||
            original.Runtime is null || fresh.Runtime is null ||
            !original.Runtime.AsSpan().SequenceEqual(fresh.Runtime) ||
            !reviewed.RuntimeConfig.AsSpan().SequenceEqual(current.RuntimeConfig))
        {
            throw new InvalidOperationException(
                "Codex backup inputs changed after review; no canonical file was replaced.");
        }

        return current;
    }
}
