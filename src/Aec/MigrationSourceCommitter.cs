using System.Globalization;

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
        MigrationSourceValidationPreview.VerifyFinalState(fresh.Preparation, fresh.Branch,
            index, fresh.Preparation.DirtySections, fresh.Files, protectedFiles);
        if (paths.Length == 0) return null;

        try
        {
            GitProcess.RunRequired(repository, "Could not stage reviewed migration sources",
                ["add", "--", .. paths]);
            var stagedFiles = VerifyStagedFiles(repository, parent, changedFiles, modes);
            var stagedIndex = ReadIndex(repository);
            MigrationSourceValidationPreview.VerifyFinalState(fresh.Preparation, fresh.Branch,
                stagedIndex, new MigrationDirtySections(paths), fresh.Files, protectedFiles);

            // Publish only the verified index tree, preserving configured signing
            // and refusing a concurrent branch move. No working-file reread for commit.
            BackupCommand.CommitStagedIndex(repository, CommitMessage, stagedFiles,
                fresh.Branch, parent, modes);
            var commit = ApplyCommand.ResolveHeadCommit(repository);
            VerifyCommit(repository, parent, commit, stagedFiles, modes);

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

    private static List<(string RelativePath, string Blob)> VerifyStagedFiles(string repository,
        string parent, MigrationValidatedSource[] files, Dictionary<string, string> modes)
    {
        var paths = files.Select(file => file.RelativePath).ToArray();
        VerifyPaths(repository, paths, "diff", "--cached", "--no-renames", "--name-only", "-z", parent, "--");
        var staged = new List<(string RelativePath, string Blob)>();
        foreach (var file in files)
        {
            var relative = file.RelativePath;
            var blob = GitProcess.RunRequired(repository, "Could not resolve staged migration source",
                "--no-replace-objects", "rev-parse", $":{relative}").Output.Trim();
            var entry = MigrationScaffoldPreview.ReadIndexEntry(repository, relative);
            if (entry != $"{modes[relative]} {blob} 0\t{relative}\0")
                throw new InvalidOperationException($"Staged migration source mode differs from review: {relative}; expected {modes[relative]}.");
            var size = GitProcess.RunRequired(repository, "Could not inspect staged source size",
                "--no-replace-objects", "cat-file", "-s", blob).Output.Trim();
            if (!int.TryParse(size, NumberStyles.None, CultureInfo.InvariantCulture, out var length))
                throw new InvalidDataException($"Invalid staged migration source size: {relative}.");
            if (length != file.WorkingContent.Length)
                throw new InvalidOperationException($"Staged migration source byte length differs from review: {relative}.");
            var content = GitProcess.RunRequiredBytes(repository, "Could not read staged migration source",
                "--no-replace-objects", "cat-file", "blob", blob);
            if (!content.AsSpan().SequenceEqual(file.WorkingContent))
                throw new InvalidOperationException($"Staged migration source bytes differ from review: {relative}.");
            staged.Add((relative, blob));
        }
        return staged;
    }

    private static void VerifyCommit(string repository, string parent, string commit,
        IReadOnlyList<(string RelativePath, string Blob)> files, Dictionary<string, string> modes)
    {
        var parents = GitProcess.RunRequired(repository, "Could not inspect migration source commit parent",
            "--no-replace-objects", "rev-list", "--parents", "-n", "1", commit).Output.Trim();
        if (parents != $"{commit} {parent}")
            throw new InvalidOperationException("Migration source commit is not one child of the reviewed HEAD.");
        var paths = files.Select(file => file.RelativePath).ToArray();
        VerifyPaths(repository, paths, "diff-tree", "--no-commit-id", "--no-renames", "--name-only", "-r", "-z", parent, commit, "--");
        foreach (var (relative, blob) in files)
        {
            var entry = GitProcess.RunRequired(repository, "Could not inspect committed migration source",
                "--no-replace-objects", "ls-tree", "-z", commit, "--", relative).Output;
            if (entry != $"{modes[relative]} blob {blob}\t{relative}\0")
                throw new InvalidOperationException($"Committed migration source bytes or mode differ from review: {relative}.");
        }
    }

    private static void VerifyPaths(string repository, string[] expected, params string[] arguments)
    {
        var output = GitProcess.RunRequired(repository, "Could not inspect exact migration source paths",
            ["--no-replace-objects", .. arguments]).Output;
        var paths = output.Split('\0', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal);
        if (!paths.SequenceEqual(expected.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidOperationException("Migration source changes are not the exact reviewed path set.");
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
