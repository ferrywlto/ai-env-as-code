namespace Aec;

// Publish only the marker created from this fresh private review context.
// An independently present marker is never adopted as a continuation token.
internal static class MigrationCompletionCommitter
{
    private const string CommitMessage = "Record completed AEC data migration";

    public static string WriteAndCommit(MigrationCompletionPreview reviewed,
        Action? afterWriteForTests = null, Action? afterStageForTests = null,
        Action? afterCommitForTests = null)
    {
        var writer = MigrationCompletionWriter.Prepare(reviewed);
        var repository = writer.Repository;
        var parent = writer.ParentCommit;
        var branch = PortableBackupCommitter.RequireLocalBranch(repository);
        if (branch != writer.Branch)
            throw new InvalidOperationException("Completion branch changed before marker creation.");
        MigrationPortableCaptureWriter.EnsureEmptyIndex(repository);
        var relative = MigrationCompletionPreview.MarkerRelativePath;
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal)
        {
            [relative] = writer.CopyDesiredContent()
        };
        var modes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [relative] = MigrationCompletionPreview.MarkerGitMode
        };
        try
        {
            writer.Write(afterWriteForTests: afterWriteForTests);
            GitProcess.RunRequired(repository, "Could not stage migration completion marker", "add", "--", relative);
            afterStageForTests?.Invoke();
            var staged = MigrationCommitProof.VerifyStagedFiles(repository, parent, files, modes);
            writer.VerifyWrittenForCommit(ReadIndex(repository));

            // Respect configured signing and publish the verified staged blobs,
            // not a new read of mutable working files.
            BackupCommand.CommitStagedIndex(repository, CommitMessage, staged, branch, parent, modes);
            var commit = ApplyCommand.ResolveHeadCommit(repository);
            MigrationCommitProof.VerifyCommit(repository, parent, commit, staged, modes);
            afterCommitForTests?.Invoke();
            MigrationPortableCaptureWriter.EnsureEmptyIndex(repository);
            writer.VerifyCommitted(commit, ReadIndex(repository));
            return commit;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          InvalidOperationException or InvalidDataException or ArgumentException)
        {
            throw new InvalidOperationException(
                "Completion marker commit stopped; the marker, staged changes or a new commit may remain. " +
                "Inspect HEAD, index and working files before retrying. No automatic rollback was attempted; " +
                "no push or runtime deployment was performed.", exception);
        }
    }

    private static byte[] ReadIndex(string repository) =>
        MigrationLegacyCaptureWriter.ReadIndex(Path.Combine(repository, ".git", "index"));
}
