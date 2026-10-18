using System.Text;

namespace Aec;

// This is an internal, explicit approval boundary. It commits only the one
// reviewed source and its complete set of affected enrolled targets. Public
// backup is not wired to this flow yet.
internal static class PortableBackupCommitter
{
    private const string CommitMessage = "Backup portable instructions";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static string Commit(
        PortableRuntimeSnapshot reviewed,
        PortableBackupAttributionPreview attribution,
        IReadOnlyList<string> renderedPaths)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        ArgumentNullException.ThrowIfNull(attribution);
        ArgumentNullException.ThrowIfNull(renderedPaths);
        if (attribution.SourceRelativePath is null || attribution.ProposedContent is null ||
            reviewed.Runtime is null)
        {
            throw new InvalidOperationException("Backup commit requires one reviewed runtime edit.");
        }

        var repository = reviewed.Repository;
        var branch = GitProcess.RunRequired(repository,
            "Could not identify the backup branch", "symbolic-ref", "--quiet", "HEAD")
            .Output.Trim();
        EnsureHeadAndRuntime();
        // Refuse a mixed index before staging anything. Other unstaged files
        // remain untouched; only the paths returned by rendering are added.
        var staged = GitProcess.Run(repository, "diff", "--cached", "--quiet", "--exit-code");
        if (staged.ExitCode != 0)
        {
            throw new InvalidOperationException(
                "The repository already has staged changes; backup commit stopped before staging.");
        }

        PortableBackupTargetWriter.VerifyRendered(reviewed, attribution, renderedPaths);
        var paths = new[] { attribution.SourceRelativePath }.Concat(renderedPaths).ToArray();
        var expected = new List<(string RelativePath, string Blob)>(paths.Length);
        var reviewedBlobs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var absolute = Path.Combine(repository, path);
            AecApplication.EnsureNoLinksInExistingPath(
                Path.GetDirectoryName(absolute)!, "Backup commit path");
            var bytes = AecApplication.ReadRequiredTextFile(absolute, "Backup commit file");
            if (path == attribution.SourceRelativePath &&
                !bytes.AsSpan().SequenceEqual(StrictUtf8.GetBytes(attribution.ProposedContent)))
            {
                throw new InvalidOperationException("Authored source changed before staging.");
            }

            reviewedBlobs.Add(path, GitProcess.RunRequired(repository,
                $"Could not hash reviewed backup file {path}",
                "hash-object", "--no-filters", "--", path).Output.Trim());
        }

        // A target could change between its first validation and hashing. This
        // second pass binds every captured blob to the reviewed render.
        PortableBackupTargetWriter.VerifyRendered(reviewed, attribution, renderedPaths);
        EnsureHeadAndRuntime();
        GitProcess.RunRequired(repository, "Could not stage reviewed backup files",
            ["add", "--", .. paths]);
        foreach (var path in paths)
        {
            var working = GitProcess.RunRequired(repository,
                $"Could not hash backup file {path}", "hash-object", "--no-filters", "--", path)
                .Output.Trim();
            var indexed = GitProcess.RunRequired(repository,
                $"Could not read staged backup file {path}", "rev-parse", $":{path}")
                .Output.Trim();
            if (!string.Equals(working, indexed, StringComparison.Ordinal) ||
                !string.Equals(indexed, reviewedBlobs[path], StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Staged backup bytes differ from working bytes: {path}. Inspect the index.");
            }

            expected.Add((path, indexed));
        }

        EnsureHeadAndRuntime();
        // The shared commit helper pins and verifies the index tree, checks the
        // exact path set, and updates the branch only if its parent is unchanged.
        BackupCommand.CommitStagedIndex(
            repository, CommitMessage, expected, branch, reviewed.Commit);
        var commit = ApplyCommand.ResolveHeadCommit(repository);
        var parent = GitProcess.RunRequired(repository,
            "Could not inspect backup commit parent", "rev-parse", "HEAD^").Output.Trim();
        if (!string.Equals(parent, reviewed.Commit, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Backup commit has an unexpected parent.");
        }

        foreach (var (path, blob) in expected)
        {
            var committed = GitProcess.RunRequired(repository,
                $"Could not inspect committed backup file {path}", "rev-parse", $"HEAD:{path}")
                .Output.Trim();
            if (!string.Equals(committed, blob, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Committed backup bytes differ: {path}.");
            }
        }

        return commit;

        void EnsureHeadAndRuntime()
        {
            if (!string.Equals(ApplyCommand.ResolveHeadCommit(repository), reviewed.Commit,
                    StringComparison.Ordinal) ||
                !AecApplication.ReadRequiredTextFile(
                    reviewed.RuntimePath, "Provider runtime instructions")
                    .AsSpan().SequenceEqual(reviewed.Runtime))
            {
                throw new InvalidOperationException(
                    "Backup HEAD or runtime changed after review; commit stopped.");
            }
        }
    }
}
