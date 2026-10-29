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
            // Also validates a no-op. Full checks belong at file-write
            // boundaries; directory creation below uses local path guards.
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

        void VerifyInputs()
        {
            // Our own new files legitimately add dirty paths. Admit only those
            // exact additions; every other preview safety check still runs.
            var dirtyPaths = fresh.Preparation.DirtySections.Paths.Concat(createdFiles)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
            var preparation = fresh.Preparation with
            {
                DirtySections = new MigrationDirtySections(dirtyPaths),
                SectionChoice = MigrationSectionChoice.Preserve
            };
            var current = MigrationScaffoldPreview.Read(preparation);
            VerifyBranch(fresh.Branch, current.Branch);
            VerifyFileCount(fresh.Files.Count, current.Files.Count);
            for (var position = 0; position < fresh.Files.Count; position++)
            {
                var original = fresh.Files[position];
                var observed = current.Files[position];
                VerifyFileIdentity(original, observed);
                byte[]? expectedContent = createdFiles.Contains(original.RelativePath)
                    ? [] : original.WorkingContent;
                if (!SameBytes(expectedContent, observed.WorkingContent))
                {
                    throw new InvalidOperationException($"Scaffold working bytes changed: {original.RelativePath}.");
                }
            }
            var currentIndex = MigrationLegacyCaptureWriter.ReadIndex(indexPath);
            if (!index.AsSpan().SequenceEqual(currentIndex))
            {
                throw new InvalidOperationException("Git index changed during scaffold creation.");
            }
            foreach (var (path, content) in protectedFiles)
            {
                if (!SameBytes(content, ReadFile(path)))
                {
                    throw new InvalidOperationException($"Protected migration input changed during scaffold creation: {path}.");
                }
            }
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

    private static Dictionary<string, byte[]?> ReadProtectedFiles(MigrationScaffoldPreview fresh)
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
