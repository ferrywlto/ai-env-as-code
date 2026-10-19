using System.Text;

namespace Aec;

// Commit only the reviewed changed source, affected enrolled targets, and
// (for Codex) managed config. Public Codex backup uses this flow; Copilot's
// guarded writer remains internal until its public command migrates.
internal static class PortableBackupCommitter
{
    private const string CommitMessage = "Backup portable instructions";
    private const string CodexCommitMessage = "Backup portable Codex environment";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static string Commit(
        PortableRuntimeSnapshot reviewed,
        PortableBackupAttributionPreview attribution,
        IReadOnlyList<string> renderedPaths)
        => CommitCore(reviewed, attribution, renderedPaths, null);

    public static string Commit(
        PortableCodexBackupPreview reviewed,
        IReadOnlyList<string> renderedPaths)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        return CommitCore(reviewed.Snapshot,
            reviewed.Instructions.Section is null ? null : reviewed.Instructions,
            renderedPaths, reviewed);
    }

    private static string CommitCore(
        PortableRuntimeSnapshot reviewed,
        PortableBackupAttributionPreview? attribution,
        IReadOnlyList<string> renderedPaths,
        PortableCodexBackupPreview? codex)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        ArgumentNullException.ThrowIfNull(renderedPaths);
        if (reviewed.Runtime is null ||
            (attribution is null && codex?.ConfigWouldChange != true) ||
            (attribution is not null &&
             (attribution.SourceRelativePath is null || attribution.ProposedContent is null)))
        {
            throw new InvalidOperationException("Backup commit requires a reviewed runtime change.");
        }

        if (codex is not null && reviewed.Provider != "codex")
        {
            throw new InvalidOperationException("Codex backup commit requires a Codex snapshot.");
        }

        var repository = reviewed.Repository;
        var branch = RequireLocalBranch(repository);
        EnsureHeadAndRuntime();
        // Refuse a mixed index before staging anything. Other unstaged files
        // remain untouched; only the paths returned by rendering are added.
        var staged = GitProcess.Run(repository, "diff", "--cached", "--quiet", "--exit-code");
        if (staged.ExitCode != 0)
        {
            throw new InvalidOperationException(
                "The repository already has staged changes; backup commit stopped before staging.");
        }

        VerifyReviewedFiles();
        var paths = (attribution is null
                ? Enumerable.Empty<string>()
                : [attribution.SourceRelativePath!])
            .Concat(renderedPaths)
            .Concat(codex?.ConfigWouldChange == true
                ? [AecApplication.ConfigSourceRelativePath]
                : [])
            .ToArray();
        var expected = new List<(string RelativePath, string Blob)>(paths.Length);
        var reviewedBlobs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            var absolute = Path.Combine(repository, path);
            AecApplication.EnsureNoLinksInExistingPath(
                Path.GetDirectoryName(absolute)!, "Backup commit path");
            var bytes = AecApplication.ReadRequiredTextFile(absolute, "Backup commit file");
            if (path == attribution?.SourceRelativePath &&
                !bytes.AsSpan().SequenceEqual(StrictUtf8.GetBytes(attribution.ProposedContent!)))
            {
                throw new InvalidOperationException("Authored source changed before staging.");
            }

            if (path == AecApplication.ConfigSourceRelativePath && codex is not null &&
                !bytes.AsSpan().SequenceEqual(codex.ProposedCanonicalConfig))
            {
                throw new InvalidOperationException("Canonical config changed before staging.");
            }

            reviewedBlobs.Add(path, GitProcess.RunRequired(repository,
                $"Could not hash reviewed backup file {path}",
                "hash-object", "--no-filters", "--", path).Output.Trim());
        }

        // A target could change between its first validation and hashing. This
        // second pass binds every captured blob to the reviewed render.
        VerifyReviewedFiles();
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
            repository, codex is null ? CommitMessage : CodexCommitMessage,
            expected, branch, reviewed.Commit);
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

        void VerifyReviewedFiles()
        {
            if (attribution is not null)
            {
                PortableBackupTargetWriter.VerifyRendered(reviewed, attribution, renderedPaths);
            }
            else if (renderedPaths.Count != 0)
            {
                throw new InvalidOperationException(
                    "A config-only backup must not stage rendered instruction targets.");
            }

            if (codex is not null)
            {
                var canonicalPath = Path.Combine(
                    repository, AecApplication.ConfigSourceRelativePath);
                AecApplication.EnsureNoLinksInExistingPath(
                    Path.GetDirectoryName(canonicalPath)!, "Canonical config path");
                var committed = GitProcess.RunRequiredBytes(repository,
                    "Could not read committed canonical config", "show",
                    $"{reviewed.Commit}:{AecApplication.ConfigSourceRelativePath}");
                var personality = CodexPersonalityConfig.ReadRuntime(
                    codex.RuntimeConfig, codex.RuntimeConfigPath)
                    ?? throw new InvalidOperationException(
                        "Reviewed runtime config has no managed root personality.");
                var planned = CodexPersonalityConfig.PlanCanonicalUpdate(
                    codex.CanonicalConfig, canonicalPath, personality);
                if (!committed.AsSpan().SequenceEqual(codex.CanonicalConfig) ||
                    !planned.Content.AsSpan().SequenceEqual(codex.ProposedCanonicalConfig) ||
                    planned.Changed != codex.ConfigWouldChange)
                {
                    throw new InvalidOperationException(
                        "Reviewed Codex config no longer matches its committed baseline.");
                }

                var expected = codex.ConfigWouldChange
                    ? codex.ProposedCanonicalConfig
                    : codex.CanonicalConfig;
                if (!AecApplication.ReadRequiredTextFile(canonicalPath, "Canonical config")
                        .AsSpan().SequenceEqual(expected))
                {
                    throw new InvalidOperationException(
                        "Canonical config changed after Codex backup review.");
                }
            }
        }

        void EnsureHeadAndRuntime()
        {
            if (!string.Equals(ApplyCommand.ResolveHeadCommit(repository), reviewed.Commit,
                    StringComparison.Ordinal) ||
                !AecApplication.ReadRequiredTextFile(
                    reviewed.RuntimePath, "Provider runtime instructions")
                    .AsSpan().SequenceEqual(reviewed.Runtime) ||
                codex is not null &&
                !AecApplication.ReadRequiredTextFile(
                    codex.RuntimeConfigPath, "Runtime config")
                    .AsSpan().SequenceEqual(codex.RuntimeConfig))
            {
                throw new InvalidOperationException(
                    "Backup HEAD or runtime changed after review; commit stopped.");
            }
        }
    }

    internal static string RequireLocalBranch(string repository)
    {
        var result = GitProcess.Run(repository, "symbolic-ref", "--quiet", "HEAD");
        if (result.ExitCode == 1)
        {
            throw new InvalidOperationException(
                "Backup requires a symbolic local Git branch; detached HEAD is not supported.");
        }

        var branch = result.Output.Trim();
        if (result.ExitCode != 0 || !branch.StartsWith("refs/heads/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Backup requires HEAD to reference a local Git branch under refs/heads/.");
        }

        return branch;
    }
}
