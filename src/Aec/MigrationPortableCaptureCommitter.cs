using System.Globalization;

namespace Aec;

// Join the existing authored and target-only stages against one private plan.
// This is not public migration orchestration and never deploys runtime files.
internal static class MigrationPortableCaptureCommitter
{
    private const string CommitMessage = "Backup portable environment before migration";

    public static string? WriteAndCommit(MigrationPortableCapturePreview reviewed)
    {
        var capturePlan = MigrationPortableCapturePlan.Prepare(reviewed);
        var targetWriter = MigrationPortableTargetWriter.Prepare(capturePlan);
        var repository = capturePlan.Repository;
        var parent = capturePlan.Commit;
        var proposedFiles = capturePlan.CopyProposals();
        foreach (var (relativePath, proposedContent) in targetWriter.CopyProposals())
        {
            proposedFiles.Add(relativePath, proposedContent);
        }

        var changedPaths = proposedFiles.Keys.Order(StringComparer.Ordinal).ToArray();
        var baselineModes = ReadBaselineModes(repository, parent, changedPaths);

        try
        {
            var writtenSources = capturePlan.WriteSources();
            var renderedTargets = targetWriter.Write(writtenSources);
            var completedPaths = writtenSources.Concat(renderedTargets).Order(StringComparer.Ordinal).ToArray();
            if (!completedPaths.SequenceEqual(changedPaths, StringComparer.Ordinal))
            {
                throw new InvalidOperationException("Completed capture paths differ from the private proposal.");
            }

            targetWriter.VerifyRendered();
            if (changedPaths.Length == 0)
            {
                return null;
            }

            GitProcess.RunRequired(repository, "Could not stage combined portable capture",
                ["add", "--", .. changedPaths]);
            var stagedFiles = VerifyStagedFiles(repository, parent, changedPaths, baselineModes, proposedFiles);
            targetWriter.VerifyForCommit(parent);

            // The shared helper pins the verified index tree, preserves signing
            // and compares the branch's old value before publishing the commit.
            BackupCommand.CommitStagedIndex(
                repository, CommitMessage, stagedFiles, capturePlan.Branch, parent, baselineModes);

            var commit = GitRepository.ResolveHeadCommit(repository);
            MigrationCommitProof.VerifyCommit(
                repository, parent, commit, stagedFiles, baselineModes, portableCapture: true);
            targetWriter.VerifyForCommit(commit);
            MigrationPortableCaptureWriter.EnsureEmptyIndex(repository);
            return commit;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          InvalidOperationException or InvalidDataException or ArgumentException)
        {
            throw new IOException(
                "Combined portable capture write/commit stopped. Working files, staged changes or a new commit may remain. " +
                "Inspect HEAD, index and working files before retrying; no rollback, push or runtime apply was attempted.",
                exception);
        }
    }

    private static Dictionary<string, string> ReadBaselineModes(
        string repository, string parent, IReadOnlyList<string> changedPaths)
    {
        var baselineModes = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var relativePath in changedPaths)
        {
            var treeEntry = GitRepository.ReadTreeEntry(repository, parent, relativePath,
                "Could not read portable capture file mode");
            var baselineMode = treeEntry.Split(' ')[0];
            if (baselineMode is not ("100644" or "100755"))
            {
                throw new InvalidOperationException("Portable capture requires existing regular Git files.");
            }
            baselineModes.Add(relativePath, baselineMode);
        }
        return baselineModes;
    }

    private static List<(string RelativePath, string Blob)> VerifyStagedFiles(
        string repository, string parent, IReadOnlyList<string> changedPaths,
        Dictionary<string, string> baselineModes,
        Dictionary<string, byte[]> proposedFiles)
    {
        MigrationCommitProof.VerifyStagedPaths(
            repository, parent, changedPaths.ToArray(), portableCapture: true);
        var stagedFiles = new List<(string RelativePath, string Blob)>();
        foreach (var relativePath in changedPaths)
        {
            var blobResult = GitProcess.RunRequired(repository, "Could not resolve staged portable capture blob",
                "--no-replace-objects", "rev-parse", $":{relativePath}");
            var stagedBlob = blobResult.Output.Trim();
            var indexEntry = GitRepository.ReadIndexEntry(repository, relativePath,
                "Could not inspect portable capture index mode");
            var sizeText = GitRepository.ReadObjectSizeText(repository, stagedBlob,
                "Could not inspect staged portable blob size");

            var expectedEntry = $"{baselineModes[relativePath]} {stagedBlob} 0\t{relativePath}\0";
            if (indexEntry != expectedEntry)
            {
                throw new InvalidOperationException($"Staged portable capture index entry differs from review: {relativePath}.");
            }
            if (!int.TryParse(sizeText, NumberStyles.None, CultureInfo.InvariantCulture, out var stagedLength))
            {
                throw new InvalidOperationException($"Git returned an invalid staged blob size: {relativePath}.");
            }

            // Check the immutable object's size before reading it. Expected
            // content comes from the private plan, not mutable working files.
            var proposedContent = proposedFiles[relativePath];
            if (stagedLength != proposedContent.Length)
            {
                throw new InvalidOperationException($"Staged portable capture byte length differs from review: {relativePath}.");
            }
            var stagedContent = GitRepository.ReadBlob(repository, stagedBlob,
                "Could not read staged portable capture blob");
            if (!stagedContent.AsSpan().SequenceEqual(proposedContent))
            {
                throw new InvalidOperationException($"Staged portable capture bytes differ from review: {relativePath}.");
            }

            stagedFiles.Add((relativePath, stagedBlob));
        }
        return stagedFiles;
    }

}
