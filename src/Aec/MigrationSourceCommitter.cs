namespace Aec;

// Commit authored sections only. Targets, runtime and the working files remain
// untouched; later migration stages own rendering and deployment.
internal static class MigrationSourceCommitter
{
    private const string CommitMessage = "Initialize migration instruction sections";

    public static string? Commit(MigrationSourceValidationPreview reviewed)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        var fresh = MigrationSourceValidationPreview.Read(reviewed.Preparation);
        VerifyReview(reviewed, fresh);
        var repository = fresh.Preparation.Preflight.Repository;
        var parent = fresh.Preparation.Preflight.Commit;
        var branch = PortableBackupCommitter.RequireLocalBranch(repository);
        if (branch != fresh.Branch)
            throw new InvalidOperationException("Migration source branch changed before staging.");
        MigrationPortableCaptureWriter.EnsureEmptyIndex(repository);
        var index = ReadIndex(repository);
        var protectedFiles = ReadProtectedFiles(fresh);
        var paths = fresh.ChangedPaths.ToArray();
        var changedFiles = fresh.Files.Where(file => paths.Contains(file.RelativePath, StringComparer.Ordinal)).ToArray();
        var modes = changedFiles.ToDictionary(file => file.RelativePath,
            file => file.CommittedMode ?? "100644", StringComparer.Ordinal);
        var expectedFiles = changedFiles.ToDictionary(file => file.RelativePath,
            file => file.WorkingContent, StringComparer.Ordinal);
        MigrationSourceValidationPreview.VerifyFinalState(fresh.Preparation, fresh.Branch,
            index, fresh.Preparation.DirtySections, fresh.Files, protectedFiles);
        if (paths.Length == 0) return null;

        try
        {
            GitProcess.RunRequired(repository, "Could not stage reviewed migration sources",
                ["add", "--", .. paths]);
            var stagedFiles = MigrationCommitProof.VerifyStagedFiles(repository, parent, expectedFiles, modes);
            var stagedIndex = ReadIndex(repository);
            MigrationSourceValidationPreview.VerifyFinalState(fresh.Preparation, fresh.Branch,
                stagedIndex, new MigrationDirtySections(paths), fresh.Files, protectedFiles);

            // Publish only the verified index tree, preserving configured signing
            // and refusing a concurrent branch move. No working-file reread for commit.
            BackupCommand.CommitStagedIndex(repository, CommitMessage, stagedFiles,
                fresh.Branch, parent, modes);
            var commit = ApplyCommand.ResolveHeadCommit(repository);
            MigrationCommitProof.VerifyCommit(repository, parent, commit, stagedFiles, modes);

            // write-tree may legitimately refresh index cache extensions. Verify
            // its resulting content against HEAD, then pin it for final guards.
            MigrationPortableCaptureWriter.EnsureEmptyIndex(repository);
            var committedPreparation = fresh.Preparation with
            {
                Preflight = fresh.Preparation.Preflight with { Commit = commit }
            };
            MigrationSourceValidationPreview.VerifyFinalState(committedPreparation, fresh.Branch,
                ReadIndex(repository), new MigrationDirtySections([]), fresh.Files, protectedFiles);
            return commit;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          InvalidOperationException or InvalidDataException or ArgumentException)
        {
            throw new InvalidOperationException(
                "Migration source commit stopped; staged changes or a new commit may remain. " +
                "Inspect HEAD, index and working files before retrying; no rollback, push or runtime apply was attempted.",
                exception);
        }
    }

    private static void VerifyReview(MigrationSourceValidationPreview reviewed,
        MigrationSourceValidationPreview fresh)
    {
        if (reviewed.Branch != fresh.Branch)
            throw new InvalidOperationException("Migration source branch changed after review.");
        if (reviewed.Files.Count != fresh.Files.Count)
            throw new InvalidOperationException("Migration source inventory changed after review.");
        if (!reviewed.ChangedPaths.SequenceEqual(fresh.ChangedPaths, StringComparer.Ordinal))
            throw new InvalidOperationException("Migration source changed paths differ from review.");
        for (var position = 0; position < fresh.Files.Count; position++)
        {
            var before = reviewed.Files[position];
            var current = fresh.Files[position];
            if (before.RelativePath != current.RelativePath)
                throw new InvalidOperationException("Migration source paths differ from review.");
            if (before.CommittedMode != current.CommittedMode)
                throw new InvalidOperationException($"Migration source baseline mode changed: {current.RelativePath}.");
            if (!SameBytes(before.CommittedContent, current.CommittedContent))
                throw new InvalidOperationException($"Migration source baseline bytes changed: {current.RelativePath}.");
            if (!before.WorkingContent.AsSpan().SequenceEqual(current.WorkingContent))
                throw new InvalidOperationException($"Migration source bytes changed after review: {current.RelativePath}.");
        }
    }

    private static Dictionary<string, byte[]?> ReadProtectedFiles(MigrationSourceValidationPreview fresh)
    {
        var files = MigrationSourceValidationPreview.ReadProtectedFiles(fresh.Preparation);
        var repository = fresh.Preparation.Preflight.Repository;
        var targets = GitProcess.RunRequired(repository, "Could not list protected migration targets",
            "--no-replace-objects", "ls-tree", "-r", "--name-only", "-z",
            fresh.Preparation.Preflight.Commit, "--", "environment/targets").Output;
        foreach (var relative in targets.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            _ = MigrationPortableTargetWriter.ReadEnrollment(relative);
            var path = Path.Combine(repository, relative);
            AecApplication.EnsureNoLinksInExistingPath(Path.GetDirectoryName(path)!, "Protected target path");
            files.Add(path, AecApplication.ReadRequiredTextFile(path, "Protected migration target"));
        }
        return files;
    }

    private static byte[] ReadIndex(string repository) =>
        MigrationLegacyCaptureWriter.ReadIndex(Path.Combine(repository, ".git", "index"));

    private static bool SameBytes(byte[]? before, byte[]? current)
    {
        if (before is null) return current is null;
        if (current is null) return false;
        return before.AsSpan().SequenceEqual(current);
    }
}
