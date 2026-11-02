using System.Text;

namespace Aec;

// Create the reviewed marker only. An uncommitted marker is not a completed
// data-format migration; a later separately reviewed step owns publication.
internal static class MigrationCompletionWriter
{
    public static string Write(MigrationCompletionPreview reviewed,
        Action? beforeWriteForTests = null, Action? afterWriteForTests = null)
        => Prepare(reviewed).Write(beforeWriteForTests, afterWriteForTests);

    internal static Prepared Prepare(MigrationCompletionPreview reviewed) => Prepared.Create(reviewed);

    // Keep freshly validated observations private through creation and commit.
    // Callers receive copies of proposed content, never these guard buffers.
    internal sealed class Prepared
    {
        private readonly MigrationCompletionPreview snapshot;
        private readonly byte[] content;
        private readonly Dictionary<string, byte[]?> protectedFiles;

        private Prepared(MigrationCompletionPreview fresh)
        {
            snapshot = fresh;
            content = Encoding.UTF8.GetBytes(fresh.MarkerContent);
            protectedFiles = fresh.ProtectedFiles.ToDictionary(item => item.Key, item => item.Value,
                StringComparer.Ordinal);
        }

        public string Repository => snapshot.Preparation.Preflight.Repository;
        public string ParentCommit => snapshot.Preparation.Preflight.Commit;
        public string Branch => snapshot.Branch;
        public byte[] CopyDesiredContent() => content.ToArray();

        internal static Prepared Create(MigrationCompletionPreview reviewed)
        {
            ArgumentNullException.ThrowIfNull(reviewed);
            var fresh = MigrationCompletionPreview.Read(reviewed.Preparation);
            VerifyReview(reviewed, fresh);
            return new Prepared(fresh);
        }

        public string Write(Action? beforeWriteForTests = null, Action? afterWriteForTests = null)
        {
            var path = Path.Combine(Repository, MigrationCompletionPreview.MarkerRelativePath);
            beforeWriteForTests?.Invoke();
            MigrationSourceValidationPreview.VerifyFinalState(snapshot.Preparation, Branch,
                snapshot.Index, new MigrationDirtySections([]), snapshot.Sources, protectedFiles);
            try
            {
                // CreateNew refuses a competing arrival; never truncate or replace it.
                AtomicFile.WriteNew(path, content, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                afterWriteForTests?.Invoke();
                VerifyWrittenForCommit(snapshot.Index);
                return path;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                              InvalidOperationException or InvalidDataException or ArgumentException)
            {
                throw new InvalidOperationException(
                    $"Completion marker write stopped; {path} may remain. Inspect the marker and checkout before retrying. " +
                    "No automatic rollback was attempted; no staging or commit was performed.", exception);
            }
        }

        public void VerifyWrittenForCommit(byte[] index) =>
            VerifyMarkerState(snapshot.Preparation, index,
                new MigrationDirtySections([MigrationCompletionPreview.MarkerRelativePath]));

        public void VerifyCommitted(string commit, byte[] index)
        {
            // Publication legitimately advances HEAD and makes the marker clean.
            // Ordinary preflight would reject the now-completed format; retain
            // the private original observations and admit only this transition.
            var committed = snapshot.Preparation with
            {
                Preflight = snapshot.Preparation.Preflight with { Commit = commit }
            };
            VerifyMarkerState(committed, index, new MigrationDirtySections([]));
        }

        private void VerifyMarkerState(MigrationPreparation preparation, byte[] index, MigrationDirtySections dirty)
        {
            MigrationSourceValidationPreview.VerifyFinalState(preparation, Branch,
                index, dirty, snapshot.Sources, protectedFiles, expectedCompletionMarker: content);
            var path = Path.Combine(Repository, MigrationCompletionPreview.MarkerRelativePath);
            if (!OperatingSystem.IsWindows() &&
                File.GetUnixFileMode(path) != (UnixFileMode.UserRead | UnixFileMode.UserWrite))
                throw new IOException("Completion marker permissions changed after creation.");
        }
    }

    private static void VerifyReview(MigrationCompletionPreview reviewed, MigrationCompletionPreview fresh)
    {
        if (reviewed.Branch != fresh.Branch) throw Changed("branch");
        if (reviewed.Preparation.Preflight != fresh.Preparation.Preflight) throw Changed("preflight");
        if (reviewed.Preparation.OriginalCommit != fresh.Preparation.OriginalCommit) throw Changed("original commit");
        if (!reviewed.Preparation.Harnesses.SequenceEqual(fresh.Preparation.Harnesses)) throw Changed("enrollment");
        if (!reviewed.Providers.SequenceEqual(fresh.Providers)) throw Changed("provider status");
        if (!reviewed.Index.AsSpan().SequenceEqual(fresh.Index)) throw Changed("index");
        if (reviewed.Sources.Count != fresh.Sources.Count) throw Changed("sources");
        for (var index = 0; index < fresh.Sources.Count; index++)
        {
            var expected = reviewed.Sources[index];
            var current = fresh.Sources[index];
            if (expected.RelativePath != current.RelativePath) throw Changed("source path");
            if (expected.CommittedMode != current.CommittedMode) throw Changed("source mode");
            if (!SameBytes(expected.CommittedContent, current.CommittedContent)) throw Changed("committed source");
            if (!SameBytes(expected.WorkingContent, current.WorkingContent)) throw Changed("working source");
        }
        if (reviewed.ProtectedFiles.Count != fresh.ProtectedFiles.Count) throw Changed("protected paths");
        foreach (var (path, bytes) in fresh.ProtectedFiles)
        {
            if (!reviewed.ProtectedFiles.TryGetValue(path, out var expected)) throw Changed("protected path");
            if (!SameBytes(expected, bytes)) throw Changed("protected bytes");
        }
    }

    private static bool SameBytes(byte[]? expected, byte[]? current)
    {
        if (expected is null) return current is null;
        return current is not null && expected.AsSpan().SequenceEqual(current);
    }

    private static InvalidOperationException Changed(string input) =>
        new($"Completion {input} changed after review; no marker was written.");
}
