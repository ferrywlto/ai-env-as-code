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

            var commit = ApplyCommand.ResolveHeadCommit(repository);
            VerifyCommitParent(repository, commit, parent);
            VerifyPaths(repository, changedPaths,
                "diff-tree", "--no-commit-id", "--no-renames", "--name-only", "-r", "-z", parent, commit, "--");
            VerifyCommittedFiles(repository, commit, stagedFiles, baselineModes);
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
            var result = GitProcess.RunRequired(repository, "Could not read portable capture file mode",
                "--no-replace-objects", "ls-tree", "-z", parent, "--", relativePath);
            var treeEntry = result.Output;
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
        VerifyPaths(repository, changedPaths,
            "diff", "--cached", "--no-renames", "--name-only", "-z", parent, "--");
        var stagedFiles = new List<(string RelativePath, string Blob)>();
        foreach (var relativePath in changedPaths)
        {
            var blobResult = GitProcess.RunRequired(repository, "Could not resolve staged portable capture blob",
                "--no-replace-objects", "rev-parse", $":{relativePath}");
            var stagedBlob = blobResult.Output.Trim();
            var indexResult = GitProcess.RunRequired(repository, "Could not inspect portable capture index mode",
                "ls-files", "--stage", "-z", "--", relativePath);
            var indexEntry = indexResult.Output;
            var sizeResult = GitProcess.RunRequired(repository, "Could not inspect staged portable blob size",
                "--no-replace-objects", "cat-file", "-s", stagedBlob);
            var sizeText = sizeResult.Output.Trim();

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
            var stagedContent = GitProcess.RunRequiredBytes(repository, "Could not read staged portable capture blob",
                "--no-replace-objects", "cat-file", "blob", stagedBlob);
            if (!stagedContent.AsSpan().SequenceEqual(proposedContent))
            {
                throw new InvalidOperationException($"Staged portable capture bytes differ from review: {relativePath}.");
            }

            stagedFiles.Add((relativePath, stagedBlob));
        }
        return stagedFiles;
    }

    private static void VerifyCommitParent(string repository, string commit, string parent)
    {
        var result = GitProcess.RunRequired(repository, "Could not inspect portable capture parent",
            "--no-replace-objects", "rev-list", "--parents", "-n", "1", commit);
        var actualParents = result.Output.Trim();
        var expectedParents = $"{commit} {parent}";
        if (actualParents != expectedParents)
        {
            throw new InvalidOperationException("Portable capture did not create exactly one child of the reviewed commit.");
        }
    }

    private static void VerifyCommittedFiles(
        string repository, string commit, IReadOnlyList<(string RelativePath, string Blob)> stagedFiles,
        Dictionary<string, string> baselineModes)
    {
        foreach (var (relativePath, stagedBlob) in stagedFiles)
        {
            var result = GitProcess.RunRequired(repository, "Could not inspect committed portable capture",
                "--no-replace-objects", "ls-tree", "-z", commit, "--", relativePath);
            var actualEntry = result.Output;
            var expectedEntry = $"{baselineModes[relativePath]} blob {stagedBlob}\t{relativePath}\0";
            if (actualEntry != expectedEntry)
            {
                throw new InvalidOperationException($"Committed portable capture bytes or mode differ from review: {relativePath}.");
            }
        }
    }

    private static void VerifyPaths(
        string repository, IReadOnlyList<string> expectedPaths, params string[] arguments)
    {
        var result = GitProcess.RunRequired(repository, "Could not inspect exact portable capture paths",
            ["--no-replace-objects", .. arguments]);
        var actualPaths = result.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Order(StringComparer.Ordinal).ToArray();
        if (!actualPaths.SequenceEqual(expectedPaths, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Portable capture changes are not the exact reviewed path set.");
        }
    }
}
