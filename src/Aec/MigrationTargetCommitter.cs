namespace Aec;

// Prepare before writing so commit proofs retain the same private snapshots.
// Independently dirty targets are not accepted as a substitute for this handoff.
internal static class MigrationTargetCommitter
{
    private const string CommitMessage = "Render migration instruction target";

    public static string? WriteAndCommit(MigrationTargetPreview reviewed)
    {
        var writer = MigrationTargetWriter.Prepare(reviewed);
        var repository = writer.Repository;
        var parent = writer.ParentCommit;
        var branch = PortableBackupCommitter.RequireLocalBranch(repository);
        if (branch != writer.Branch)
            throw new InvalidOperationException("Migration target branch changed before writing.");
        MigrationPortableCaptureWriter.EnsureEmptyIndex(repository);
        var expectedFiles = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [writer.RelativePath] = writer.CopyDesiredContent()
        };
        var modes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [writer.RelativePath] = writer.GitMode
        };

        try
        {
            if (writer.Write() is null) return null;
            GitProcess.RunRequired(repository, "Could not stage migration instruction target",
                "add", "--", writer.RelativePath);
            var staged = MigrationCommitProof.VerifyStagedFiles(repository, parent, expectedFiles, modes);
            writer.VerifyWrittenForCommit(ReadIndex(repository));

            // Reuse signing and verified-tree publication; never let Git reread
            // mutable working files as the content authority for this commit.
            BackupCommand.CommitStagedIndex(repository, CommitMessage, staged, branch, parent, modes);
            var commit = ApplyCommand.ResolveHeadCommit(repository);
            MigrationCommitProof.VerifyCommit(repository, parent, commit, staged, modes);

            // write-tree can refresh index cache extensions legitimately. Prove
            // the resulting index is clean before pinning it for final guards.
            MigrationPortableCaptureWriter.EnsureEmptyIndex(repository);
            writer.VerifyCommitted(commit, ReadIndex(repository));
            return commit;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          InvalidOperationException or InvalidDataException or ArgumentException)
        {
            throw new InvalidOperationException(
                "Migration target write/commit stopped; working files, staged changes or a new commit may remain. " +
                "Inspect HEAD, index and working files before retrying; no rollback, push or runtime apply was attempted.",
                exception);
        }
    }

    private static byte[] ReadIndex(string repository) =>
        MigrationLegacyCaptureWriter.ReadIndex(Path.Combine(repository, ".git", "index"));
}
