namespace Aec;

// Only canonical working files change here. Staging and committing belong to a
// later boundary; individual atomic replacements are not a batch transaction.
internal static class MigrationLegacyCaptureWriter
{
    public static IReadOnlyList<string> Write(MigrationLegacyCapturePreview reviewed)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        var fresh = MigrationLegacyCapturePreview.Read(reviewed.Preparation);
        if (fresh.Branch != reviewed.Branch || fresh.Files.Count != reviewed.Files.Count ||
            !fresh.Files.Zip(reviewed.Files).All(pair => SameFile(pair.First, pair.Second)))
        {
            throw new InvalidOperationException("Legacy capture inputs or proposal changed after review; no files were replaced.");
        }

        // Use freshly derived buffers, not mutable byte arrays held by a caller.
        var repository = fresh.Preparation.Preflight.Repository;
        var head = fresh.Preparation.Preflight.Commit;
        var branch = fresh.Branch;
        var indexPath = Path.Combine(repository, ".git", "index");
        var index = ReadIndex(indexPath);
        var written = new List<string>();
        string? attempted = null;
        try
        {
            VerifyInputs();
            foreach (var file in fresh.Files.Where(file => file.HasChanges))
            {
                VerifyInputs();
                var path = Path.Combine(repository, file.RelativePath);
                AecApplication.EnsureNoLinksInExistingPath(Path.GetDirectoryName(path)!, "Legacy capture destination path");
                attempted = file.RelativePath;
                AtomicFile.ReplaceIfUnchanged(path, file.Canonical, file.ProposedCanonical, "Legacy canonical source");
                written.Add(file.RelativePath);
                attempted = null;
                VerifyInputs();
            }
            return written.ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or InvalidDataException)
        {
            // AtomicFile can fail during its post-write verification. Report the
            // attempted path separately; do not claim that it stayed unchanged.
            throw new MigrationLegacyCaptureWriteException(written.ToArray(), attempted, exception);
        }

        void VerifyInputs()
        {
            AecApplication.EnsureNoLinksInExistingPath(repository, "Repository path");
            AecApplication.EnsureNoLinksInExistingPath(Path.Combine(repository, ".git"), "Git metadata path");
            if (ApplyCommand.ResolveHeadCommit(repository) != head || MigrationLegacyCapturePreview.ReadBranch(repository) != branch ||
                !ReadIndex(indexPath).AsSpan().SequenceEqual(index))
            {
                throw new InvalidOperationException("Git HEAD, branch or index changed; no further capture files will be replaced.");
            }
            if (AecApplication.ReadOptionalTextFile(Path.Combine(repository, MigrationPreflight.FormatRelativePath),
                "Repository format marker") is not null)
            {
                throw new InvalidOperationException("Repository format marker appeared during legacy capture.");
            }
            if (!MigrationHarnessSelection.Read(fresh.Preparation.Preflight).SequenceEqual(fresh.Preparation.Harnesses))
            {
                throw new InvalidOperationException("Local harness enrollment changed during legacy capture.");
            }
            foreach (var file in fresh.Files)
            {
                var canonicalPath = Path.Combine(repository, file.RelativePath);
                AecApplication.EnsureNoLinksInExistingPath(Path.GetDirectoryName(canonicalPath)!, "Canonical source path");
                AecApplication.EnsureNoLinksInExistingPath(Path.GetDirectoryName(file.RuntimePath)!, "Runtime source path");
                var canonical = AecApplication.ReadRequiredTextFile(canonicalPath, "Canonical source");
                var runtime = AecApplication.ReadRequiredTextFile(file.RuntimePath, "Runtime source");
                var expected = written.Contains(file.RelativePath) ? file.ProposedCanonical : file.Canonical;
                if (!canonical.AsSpan().SequenceEqual(expected) || !runtime.AsSpan().SequenceEqual(file.Runtime))
                {
                    throw new InvalidOperationException("Canonical or runtime bytes changed during legacy capture; no further files will be replaced.");
                }
            }
        }
    }

    private static bool SameFile(MigrationLegacyCaptureFile current, MigrationLegacyCaptureFile reviewed) =>
        current.Provider == reviewed.Provider && current.RelativePath == reviewed.RelativePath &&
        current.RuntimePath == reviewed.RuntimePath &&
        current.Canonical.AsSpan().SequenceEqual(reviewed.Canonical) &&
        current.Runtime.AsSpan().SequenceEqual(reviewed.Runtime) &&
        current.ProposedCanonical.AsSpan().SequenceEqual(reviewed.ProposedCanonical);

    internal static byte[] ReadIndex(string path)
    {
        var file = new FileInfo(path);
        file.Refresh();
        if (file.LinkTarget is not null || (file.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException("Git index must not be a link during migration capture.");
        }
        return File.ReadAllBytes(path);
    }
}

internal sealed class MigrationLegacyCaptureWriteException : IOException
{
    public MigrationLegacyCaptureWriteException(IReadOnlyList<string> writtenPaths, string? attemptedPath, Exception inner)
        : base($"Legacy capture stopped. Completed replacements: {(writtenPaths.Count == 0 ? "none" : string.Join(", ", writtenPaths))}. " +
            (attemptedPath is null ? "" : $"Attempted path may also have changed: {attemptedPath}. ") +
            "Inspect the working files before retrying. No rollback, staging or commit was attempted.", inner)
    {
        WrittenPaths = writtenPaths;
        AttemptedPath = attemptedPath;
    }

    public IReadOnlyList<string> WrittenPaths { get; }
    public string? AttemptedPath { get; }
}
