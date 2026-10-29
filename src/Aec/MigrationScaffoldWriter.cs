namespace Aec;

// Missing-file creation only. No draft replacement, staging, commit or deployment.
internal static class MigrationScaffoldWriter
{
    private static readonly string[] ProtectedSourcePaths =
    [AecApplication.SourceRelativePath, AecApplication.ConfigSourceRelativePath,
        AecApplication.CopilotSourceRelativePath];

    public static IReadOnlyList<string> Write(MigrationScaffoldPreview reviewed)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        var fresh = MigrationScaffoldPreview.Read(reviewed.Preparation);
        VerifyReview(reviewed, fresh);
        foreach (var file in fresh.Files)
        {
            if (file.BlockedForFutureWrite)
            {
                throw new InvalidOperationException($"Scaffold source needs review before any creation: {file.RelativePath} ({file.Action}).");
            }
        }

        // Use fresh observations, never caller-supplied paths or seed buffers.
        var repository = fresh.Preparation.Preflight.Repository;
        var indexPath = Path.Combine(repository, ".git", "index");
        var index = MigrationLegacyCaptureWriter.ReadIndex(indexPath);
        var protectedFiles = ReadProtectedFiles(fresh);
        var createdFiles = new List<string>();
        var createdDirectories = new List<string>();
        string? attemptedPath = null;
        try
        {
            // Also validates a no-op. Eligibility/history was checked once by
            // the fresh preview; these boundaries inspect mutable state only.
            VerifyInputs();
            foreach (var file in fresh.Files)
            {
                if (file.Action != MigrationScaffoldAction.CreateEmpty) continue;
                var path = Path.Combine(repository, file.RelativePath);
                var parent = Path.GetDirectoryName(path)!;
                foreach (var directory in MissingDirectories(repository, parent))
                {
                    AecApplication.EnsureNoLinksInExistingPath(directory, "Scaffold directory path");
                    attemptedPath = Path.GetRelativePath(repository, directory);
                    Directory.CreateDirectory(directory);
                    createdDirectories.Add(attemptedPath);
                    AecApplication.EnsureRealDirectory(directory, "Scaffold directory");
                    attemptedPath = null;
                }

                VerifyInputs();
                AecApplication.EnsureNoLinksInExistingPath(parent, "Scaffold destination path");
                AecApplication.EnsureRealDirectory(parent, "Scaffold parent");
                attemptedPath = file.RelativePath;
                // CreateNew refuses a concurrent arrival; no truncate/replace fallback.
                AtomicFile.WriteNew(path, []);
                createdFiles.Add(file.RelativePath);
                attemptedPath = null;
                VerifyInputs();
            }
            // Every created file already passed its post-write validation.
            return createdFiles.ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or InvalidDataException)
        {
            throw new MigrationScaffoldWriteException(createdFiles.ToArray(),
                createdDirectories.ToArray(), attemptedPath, exception);
        }

        void VerifyInputs() => VerifyCurrentInputs(fresh, index, protectedFiles, createdFiles);
    }

    // Read-only and internal for deterministic between-step guard tests. Write
    // owns the fresh baseline and snapshots; callers cannot supply write authority.
    internal static void VerifyCurrentInputs(MigrationScaffoldPreview baseline,
        byte[] originalIndex, Dictionary<string, byte[]?> protectedFiles,
        IReadOnlyList<string> createdFiles)
    {
        var preparation = baseline.Preparation;
        var repository = preparation.Preflight.Repository;
        AecApplication.EnsureNoLinksInExistingPath(repository, "Scaffold repository path");
        AecApplication.EnsureRealDirectory(repository, "Scaffold repository");
        var gitDirectory = Path.Combine(repository, ".git");
        AecApplication.EnsureNoLinksInExistingPath(gitDirectory, "Scaffold Git path");
        AecApplication.EnsureRealDirectory(gitDirectory, "Scaffold Git directory");
        MigrationFastForward.EnsureSafeGitConfiguration(repository);
        InitCommand.EnsureContainedGitMetadata(repository, gitDirectory);
        VerifyGitState(baseline, originalIndex);
        VerifyMarkerAbsent(repository);
        VerifyEnrollment(preparation);
        VerifyDirtyPaths(preparation, createdFiles);
        VerifyWorkingSections(baseline, createdFiles);
        VerifyProtectedFiles(protectedFiles);
        VerifyGitState(baseline, originalIndex);
    }

    private static void VerifyGitState(MigrationScaffoldPreview baseline, byte[] originalIndex)
    {
        var repository = baseline.Preparation.Preflight.Repository;
        if (ApplyCommand.ResolveHeadCommit(repository) != baseline.Preparation.Preflight.Commit)
            throw new InvalidOperationException("HEAD changed during scaffold creation.");
        VerifyBranch(baseline.Branch, MigrationLegacyCapturePreview.ReadBranch(repository));
        var currentIndex = MigrationLegacyCaptureWriter.ReadIndex(Path.Combine(repository, ".git", "index"));
        if (!originalIndex.AsSpan().SequenceEqual(currentIndex))
            throw new InvalidOperationException("Git index changed during scaffold creation.");
    }

    private static void VerifyMarkerAbsent(string repository)
    {
        if (ReadFile(Path.Combine(repository, MigrationPreflight.FormatRelativePath)) is not null)
            throw new InvalidOperationException("Migration completion marker appeared during scaffold creation.");
    }

    private static void VerifyEnrollment(MigrationPreparation preparation)
    {
        var current = MigrationHarnessSelection.Read(preparation.Preflight);
        if (!current.SequenceEqual(preparation.Harnesses))
            throw new InvalidOperationException("Local harness enrollment changed during scaffold creation.");
    }

    private static void VerifyDirtyPaths(MigrationPreparation preparation, IReadOnlyList<string> createdFiles)
    {
        // Only our completed creations may extend the reviewed dirty-path set.
        // Reuse conflict/type/rename/ignore restrictions without a history scan.
        var expected = preparation.DirtySections.Paths.Concat(createdFiles)
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        var current = MigrationDirtySections.Read(preparation.Preflight, preparation.Harnesses);
        if (!current.Paths.SequenceEqual(expected, StringComparer.Ordinal))
            throw new InvalidOperationException("Dirty paths changed during scaffold creation.");
    }

    private static void VerifyWorkingSections(MigrationScaffoldPreview baseline, IReadOnlyList<string> createdFiles)
    {
        var repository = baseline.Preparation.Preflight.Repository;
        foreach (var file in baseline.Files)
        {
            byte[]? expected = createdFiles.Contains(file.RelativePath) ? [] : file.WorkingContent;
            var current = ReadFile(Path.Combine(repository, file.RelativePath));
            if (!SameBytes(expected, current))
                throw new InvalidOperationException($"Scaffold working bytes changed: {file.RelativePath}.");
            if (expected is null)
                MigrationScaffoldPreview.VerifyMissingPathNotIgnored(repository, file.RelativePath);
        }
    }

    private static void VerifyProtectedFiles(Dictionary<string, byte[]?> protectedFiles)
    {
        foreach (var (path, content) in protectedFiles)
        {
            if (!SameBytes(content, ReadFile(path)))
                throw new InvalidOperationException($"Protected migration input changed during scaffold creation: {path}.");
        }
    }

    private static void VerifyReview(MigrationScaffoldPreview reviewed, MigrationScaffoldPreview fresh)
    {
        VerifyBranch(reviewed.Branch, fresh.Branch);
        VerifyFileCount(reviewed.Files.Count, fresh.Files.Count);
        for (var position = 0; position < fresh.Files.Count; position++)
        {
            var original = reviewed.Files[position];
            var current = fresh.Files[position];
            VerifyFileIdentity(original, current);
            if (!SameBytes(original.WorkingContent, current.WorkingContent))
            {
                throw new InvalidOperationException($"Scaffold source changed after review: {current.RelativePath}.");
            }
            if (original.Action != current.Action)
            {
                throw new InvalidOperationException($"Scaffold action changed after review: {current.RelativePath}.");
            }
        }
    }

    private static void VerifyFileIdentity(MigrationScaffoldFile expected, MigrationScaffoldFile current)
    {
        if (expected.RelativePath != current.RelativePath)
            throw new InvalidOperationException("Scaffold inventory paths changed after review.");
        if (expected.CommittedMode != current.CommittedMode)
            throw new InvalidOperationException($"Scaffold committed mode changed: {current.RelativePath}.");
        if (!SameBytes(expected.CommittedContent, current.CommittedContent))
            throw new InvalidOperationException($"Scaffold committed bytes changed: {current.RelativePath}.");
        if (expected.IndexEntry != current.IndexEntry)
            throw new InvalidOperationException($"Scaffold index entry changed: {current.RelativePath}.");
        if (expected.HasStagedChanges != current.HasStagedChanges)
            throw new InvalidOperationException($"Scaffold staging changed: {current.RelativePath}.");
    }

    private static void VerifyBranch(string expected, string current)
    {
        if (expected != current)
            throw new InvalidOperationException("Branch changed after scaffold review.");
    }

    private static void VerifyFileCount(int expected, int current)
    {
        if (expected != current)
            throw new InvalidOperationException("Scaffold inventory membership changed after review.");
    }

    private static List<string> MissingDirectories(string repository, string parent)
    {
        var missing = new List<string>();
        var directory = parent;
        while (directory != repository && !Directory.Exists(directory))
        {
            missing.Add(directory);
            directory = Path.GetDirectoryName(directory)
                ?? throw new InvalidOperationException("Scaffold parent escaped the repository.");
        }
        missing.Reverse();
        return missing;
    }

    internal static Dictionary<string, byte[]?> ReadProtectedFiles(MigrationScaffoldPreview fresh)
    {
        var files = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        var repository = fresh.Preparation.Preflight.Repository;
        foreach (var relative in ProtectedSourcePaths)
        {
            var path = Path.Combine(repository, relative);
            files.Add(path, ReadFile(path));
        }
        // These snapshots protect this operation, not runtime equality with an
        // earlier preview: runtime bytes are not stored in that advisory record.
        foreach (var harness in fresh.Preparation.Harnesses)
        {
            files.Add(harness.RuntimePath, ReadFile(harness.RuntimePath));
            if (harness.Provider == "codex")
            {
                var path = Path.Combine(harness.Home, "config.toml");
                files.Add(path, ReadFile(path));
            }
        }
        return files;
    }

    private static byte[]? ReadFile(string path)
    {
        AecApplication.EnsureNoLinksInExistingPath(Path.GetDirectoryName(path)!, "Scaffold protected path");
        return AecApplication.ReadOptionalTextFile(path, "Scaffold protected file");
    }

    private static bool SameBytes(byte[]? first, byte[]? second)
    {
        if (first is null) return second is null;
        if (second is null) return false;
        return first.AsSpan().SequenceEqual(second);
    }
}

internal sealed class MigrationScaffoldWriteException : IOException
{
    public MigrationScaffoldWriteException(IReadOnlyList<string> createdPaths,
        IReadOnlyList<string> createdDirectories, string? attemptedPath, Exception inner)
        : base($"Scaffold creation stopped. Completed files: {string.Join(", ", createdPaths)}. " +
            $"Created directories: {string.Join(", ", createdDirectories)}. " +
            (attemptedPath is null ? "" : $"Attempted path may also exist: {attemptedPath}. ") +
            "Inspect retained paths before retrying. No rollback, staging, commit or runtime write was attempted.", inner)
    {
        CreatedPaths = createdPaths;
        CreatedDirectories = createdDirectories;
        AttemptedPath = attemptedPath;
    }

    public IReadOnlyList<string> CreatedPaths { get; }
    public IReadOnlyList<string> CreatedDirectories { get; }
    public string? AttemptedPath { get; }
}
