namespace Aec;

// Write one reviewed repository target. Committing it and applying to runtime
// remain separate migration stages; caller-visible preview buffers authorize neither.
internal static class MigrationTargetWriter
{
    public static string? Write(MigrationTargetPreview reviewed)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        var fresh = MigrationTargetPreview.Read(reviewed.Preparation, reviewed.Harness.Provider);
        VerifyReview(reviewed, fresh);
        var repository = fresh.Preparation.Preflight.Repository;
        var path = Path.Combine(repository, fresh.TargetRelativePath);
        var parent = Path.GetDirectoryName(path)!;
        var index = MigrationLegacyCaptureWriter.ReadIndex(Path.Combine(repository, ".git", "index"));
        var protectedFiles = ReadProtectedFiles(fresh);
        var mode = ReadExpectedMode(path, fresh.WorkingContent);
        var createdDirectories = new List<string>();
        string? attempted = null;

        try
        {
            VerifyCurrentInputs(fresh, index, protectedFiles, false, mode);
            if (fresh.Action == MigrationTargetAction.Unchanged) return null;

            if (fresh.Action == MigrationTargetAction.Create)
            {
                foreach (var directory in MigrationScaffoldWriter.MissingDirectories(repository, parent))
                {
                    AecApplication.EnsureNoLinksInExistingPath(directory, "Migration target directory path");
                    attempted = Path.GetRelativePath(repository, directory);
                    Directory.CreateDirectory(directory);
                    createdDirectories.Add(attempted);
                    AecApplication.EnsureRealDirectory(directory, "Migration target directory");
                    attempted = null;
                }
                // Parent creation is a write boundary, not permission to trust
                // a stale file snapshot. Do not rescan history per directory.
                VerifyCurrentInputs(fresh, index, protectedFiles, false, mode);
            }

            AecApplication.EnsureNoLinksInExistingPath(parent, "Migration target destination path");
            AecApplication.EnsureRealDirectory(parent, "Migration target parent");
            attempted = fresh.TargetRelativePath;
            if (fresh.Action == MigrationTargetAction.Create)
                AtomicFile.WriteNew(path, fresh.DesiredContent, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            else
                AtomicFile.ReplaceIfUnchanged(path, fresh.WorkingContent, fresh.DesiredContent, "Migration target");

            // Ordinary previews rightly reject this now-dirty target. Admit only
            // its exact desired bytes here, with all other inputs still pinned.
            VerifyCurrentInputs(fresh, index, protectedFiles, true, mode);
            return fresh.TargetRelativePath;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          InvalidOperationException or InvalidDataException or ArgumentException)
        {
            throw new MigrationTargetWriteException(createdDirectories.ToArray(), attempted, exception);
        }
    }

    // Read-only seam for deterministic drift tests; Write owns fresh snapshots.
    internal static void VerifyCurrentInputs(MigrationTargetPreview baseline, byte[] originalIndex,
        Dictionary<string, byte[]?> protectedFiles, bool written, UnixFileMode? expectedMode)
    {
        var repository = baseline.Preparation.Preflight.Repository;
        var path = Path.Combine(repository, baseline.TargetRelativePath);
        var expectedFiles = new Dictionary<string, byte[]?>(protectedFiles, StringComparer.Ordinal)
        {
            [path] = written ? baseline.DesiredContent : baseline.WorkingContent
        };
        var dirty = new MigrationDirtySections(written ? [baseline.TargetRelativePath] : []);
        MigrationSourceValidationPreview.VerifyFinalState(baseline.Preparation, baseline.Branch,
            originalIndex, dirty, baseline.RequiredSources, expectedFiles, baseline.TargetRelativePath);
        if (!written && baseline.WorkingContent is null)
            MigrationScaffoldPreview.VerifyMissingPathNotIgnored(repository, baseline.TargetRelativePath);
        if (!OperatingSystem.IsWindows() && expectedMode is not null &&
            (written || baseline.WorkingContent is not null))
        {
            if (File.GetUnixFileMode(path) != expectedMode.Value)
                throw new InvalidOperationException("Migration target permissions changed during writing.");
        }
    }

    private static void VerifyReview(MigrationTargetPreview reviewed, MigrationTargetPreview fresh)
    {
        if (reviewed.Branch != fresh.Branch)
            throw new InvalidOperationException("Migration target branch changed after review.");
        if (reviewed.Platform != fresh.Platform)
            throw new InvalidOperationException("Migration target platform differs from review.");
        if (reviewed.Harness != fresh.Harness)
            throw new InvalidOperationException("Migration target harness differs from review.");
        if (reviewed.TargetRelativePath != fresh.TargetRelativePath)
            throw new InvalidOperationException("Migration target path differs from review.");
        if (reviewed.Action != fresh.Action)
            throw new InvalidOperationException("Migration target action differs from review.");
        if (reviewed.CommittedMode != fresh.CommittedMode)
            throw new InvalidOperationException("Migration target committed mode differs from review.");
        if (!SameBytes(reviewed.CommittedContent, fresh.CommittedContent))
            throw new InvalidOperationException("Migration target committed bytes differ from review.");
        if (!SameBytes(reviewed.WorkingContent, fresh.WorkingContent))
            throw new InvalidOperationException("Migration target working bytes differ from review.");
        if (!reviewed.DesiredContent.AsSpan().SequenceEqual(fresh.DesiredContent))
            throw new InvalidOperationException("Migration target desired bytes differ from review.");
        VerifySources(reviewed.Sources, fresh.Sources);
        VerifySources(reviewed.RequiredSources, fresh.RequiredSources);
    }

    private static void VerifySources(IReadOnlyList<MigrationValidatedSource> reviewed,
        IReadOnlyList<MigrationValidatedSource> fresh)
    {
        if (reviewed.Count != fresh.Count)
            throw new InvalidOperationException("Migration target source inventory differs from review.");
        for (var position = 0; position < fresh.Count; position++)
        {
            var before = reviewed[position];
            var current = fresh[position];
            if (before.RelativePath != current.RelativePath)
                throw new InvalidOperationException("Migration target source path differs from review.");
            if (before.CommittedMode != current.CommittedMode)
                throw new InvalidOperationException($"Migration source mode differs from review: {current.RelativePath}.");
            if (!SameBytes(before.CommittedContent, current.CommittedContent))
                throw new InvalidOperationException($"Migration source committed bytes differ: {current.RelativePath}.");
            if (!before.WorkingContent.AsSpan().SequenceEqual(current.WorkingContent))
                throw new InvalidOperationException($"Migration source working bytes differ: {current.RelativePath}.");
        }
    }

    private static Dictionary<string, byte[]?> ReadProtectedFiles(MigrationTargetPreview fresh)
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
            AecApplication.EnsureNoLinksInExistingPath(Path.GetDirectoryName(path)!, "Protected migration target path");
            files.Add(path, AecApplication.ReadOptionalTextFile(path, "Protected migration target"));
        }
        return files;
    }

    private static UnixFileMode? ReadExpectedMode(string path, byte[]? working)
    {
        if (OperatingSystem.IsWindows()) return null;
        return working is null ? UnixFileMode.UserRead | UnixFileMode.UserWrite : File.GetUnixFileMode(path);
    }

    private static bool SameBytes(byte[]? before, byte[]? current)
    {
        if (before is null) return current is null;
        if (current is null) return false;
        return before.AsSpan().SequenceEqual(current);
    }
}

internal sealed class MigrationTargetWriteException : IOException
{
    public MigrationTargetWriteException(IReadOnlyList<string> createdDirectories,
        string? attemptedPath, Exception inner)
        : base($"Migration target writing stopped. Created directories: {(createdDirectories.Count == 0 ? "none" : string.Join(", ", createdDirectories))}. " +
            (attemptedPath is null ? "" : $"Attempted path may have changed: {attemptedPath}. ") +
            "Inspect retained state before retrying. No rollback, staging, commit or runtime apply was attempted.", inner)
    {
        CreatedDirectories = createdDirectories;
        AttemptedPath = attemptedPath;
    }

    public IReadOnlyList<string> CreatedDirectories { get; }
    public string? AttemptedPath { get; }
}
