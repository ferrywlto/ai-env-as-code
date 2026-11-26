namespace Aec;

// Named Git repository operations shared across workflows.
internal static class GitRepository
{
    internal static void ValidateRepositoryRoot(
        string repository,
        string rootRequirement = "--repo must identify the Git repository root")
    {
        var metadata = GitProcess.RunRequired(
            repository,
            "Repository is not a Git work tree",
            "--no-replace-objects",
            "rev-parse",
            "--is-inside-work-tree",
            "--is-bare-repository",
            "--show-prefix").Output.Split('\n', 3);
        if (metadata.Length != 3 || !metadata[2].EndsWith('\n'))
        {
            throw new InvalidOperationException("Git returned incomplete repository metadata.");
        }

        if (!string.Equals(metadata[0].Trim(), "true", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Repository is not a Git work tree: {repository}");
        }

        if (!string.Equals(metadata[1].Trim(), "false", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Repository must not be bare: {repository}");
        }

        if (metadata[2].Trim().Length != 0)
        {
            throw new InvalidOperationException($"{rootRequirement}: {repository}");
        }
    }

    internal static string ResolveHeadCommit(string repository)
    {
        var result = InspectOptionalHeadCommit(repository, ignoreReplacementObjects: true);
        if (result.ExitCode == 1)
        {
            throw new InvalidOperationException("Apply requires a committed Git HEAD.");
        }

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Git could not resolve committed HEAD (exit code {result.ExitCode}).");
        }

        var commit = result.Output.Trim();
        return commit.Length == 0
            ? throw new InvalidOperationException("Git returned an empty commit identifier.")
            : commit;
    }

    internal static string ReadTreeEntry(
        string repository, string treeish, string path, string failureMessage) =>
        GitProcess.RequireSuccess(InspectTreeEntries(repository, treeish, path), failureMessage).Output;

    internal static GitResult InspectTreeEntries(
        string repository, string treeish, params string[] paths) =>
        GitProcess.Run(repository,
            ["--no-replace-objects", "ls-tree", "-z", treeish, "--", .. paths]);

    internal static string ReadNullDelimitedTreePathMatches(
        string repository, string commit, string path, string failureMessage) =>
        GitProcess.RunRequired(repository, failureMessage,
            "ls-tree", "--name-only", "-z", commit, "--", path).Output;

    internal static GitResult CompareStagedPathsToCommit(
        string repository, string commit, params string[] paths) =>
        GitProcess.Run(repository,
            ["--no-replace-objects", "--no-optional-locks", "diff", "--cached", "--quiet",
                "--exit-code", "--no-ext-diff", "--no-textconv", commit, "--", .. paths]);

    internal static GitResult InspectWorktreeIndexDifferences(
        string repository, params string[] paths) =>
        GitProcess.Run(repository,
            ["--no-replace-objects", "--no-optional-locks", "diff-files", "--raw", "-z",
                "--no-renames", "--no-ext-diff", "--no-textconv", "--", .. paths]);

    internal static GitResult HashWorkingFilesWithoutFilters(
        string repository, params string[] paths) =>
        HashWorkingFilesWithoutFilters(repository, ignoreReplacementObjects: true, paths);

    internal static GitResult HashWorkingFilesWithoutFilters(
        string repository, bool ignoreReplacementObjects, params string[] paths)
    {
        string[] arguments = ["hash-object", "--no-filters", "--", .. paths];
        return GitProcess.Run(repository,
            ignoreReplacementObjects ? ["--no-replace-objects", .. arguments] : arguments);
    }

    internal static GitResult InspectStagedChanges(string repository) =>
        GitProcess.Run(repository, "diff", "--cached", "--quiet", "--exit-code");

    internal static GitResult InspectSymbolicHead(string repository) =>
        GitProcess.Run(repository, "symbolic-ref", "--quiet", "HEAD");

    internal static string ReadStagedObjectId(
        string repository, string path, string failureMessage) =>
        GitProcess.RunRequired(repository, failureMessage, "rev-parse", $":{path}").Output.Trim();

    internal static string ReadTreeObjectId(
        string repository, string treeish, string path, string failureMessage) =>
        GitProcess.RunRequired(repository, failureMessage, "rev-parse", $"{treeish}:{path}").Output.Trim();

    internal static string ReadFirstParent(
        string repository, string revision, string failureMessage) =>
        GitProcess.RunRequired(repository, failureMessage, "rev-parse", $"{revision}^").Output.Trim();

    internal static byte[] ReadFileAtTreeish(
        string repository, string treeish, string path, string failureMessage) =>
        GitProcess.RunRequiredBytes(repository, failureMessage, "show", $"{treeish}:{path}");

    internal static void StagePaths(string repository, string failureMessage, params string[] paths) =>
        GitProcess.RunRequired(repository, failureMessage, ["add", "--", .. paths]);

    internal static string WriteIndexTree(string repository, string failureMessage) =>
        GitProcess.RunRequired(repository, failureMessage, "--no-replace-objects", "write-tree")
            .Output.Trim();

    internal static string CreateCommitFromTree(
        string repository, string tree, string? parent, bool sign, string message, string failureMessage)
    {
        var arguments = new List<string> { "--no-replace-objects", "commit-tree", tree };
        if (parent is not null)
        {
            arguments.AddRange(["-p", parent]);
        }

        if (sign)
        {
            arguments.Add("-S");
        }

        arguments.AddRange(["-m", message]);
        return GitProcess.RunRequired(repository, failureMessage, [.. arguments]).Output.Trim();
    }

    internal static void UpdateReferenceIfUnchanged(
        string repository, string reference, string newValue, string expectedOldValue,
        string reflogMessage, string failureMessage) =>
        GitProcess.RunRequired(repository, failureMessage,
            "--no-replace-objects", "update-ref", "-m", reflogMessage, reference, newValue, expectedOldValue);

    internal static string ReadIndexEntry(
        string repository, string path, string failureMessage) =>
        ReadIndexEntries(repository, failureMessage, path);

    internal static string ReadObjectSizeText(
        string repository, string objectId, string failureMessage) =>
        GitProcess.RunRequired(repository, failureMessage,
            "--no-replace-objects", "cat-file", "-s", objectId).Output.Trim();

    internal static byte[] ReadBlob(
        string repository, string objectId, string failureMessage) =>
        GitProcess.RunRequiredBytes(repository, failureMessage,
            "--no-replace-objects", "cat-file", "blob", objectId);

    internal static string[] ReadStagedPathsComparedToCommit(
        string repository, string commit, string failureMessage, bool ignoreReplacementObjects = true) =>
        ReadPaths(repository, failureMessage, ignoreReplacementObjects,
            ["diff", "--cached", "--no-renames", "--name-only", "-z", commit, "--"]);

    internal static string[] ReadChangedPathsBetweenCommits(
        string repository, string parent, string commit, string failureMessage,
        bool ignoreReplacementObjects = true) =>
        ReadPaths(repository, failureMessage, ignoreReplacementObjects,
            ["diff-tree", "--no-commit-id", "--no-renames", "--name-only", "-r", "-z", parent, commit, "--"]);

    private static string[] ReadPaths(
        string repository, string failureMessage, bool ignoreReplacementObjects, string[] arguments)
    {
        var result = GitProcess.RunRequired(repository, failureMessage,
            ignoreReplacementObjects ? ["--no-replace-objects", .. arguments] : arguments);
        return result.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
    }

    // Repository setup and history.
    internal static void InitializeEmptyRepository(string repository, string initialBranch, string failureMessage) =>
        GitProcess.RunRequired(repository, failureMessage,
            "init", "--quiet", "--template=", $"--initial-branch={initialBranch}");

    internal static void SetLocalConfiguration(
        string repository, string key, string value, string failureMessage) =>
        GitProcess.RunRequired(repository, failureMessage, "config", "--local", key, value);

    internal static void RequireGitAvailable() =>
        GitProcess.RunRequired(null, "Git is not available", "--version");

    internal static void RequireAuthorIdentity(string? repository) =>
        GitProcess.RunRequired(repository, "Git author identity is not configured", "var", "GIT_AUTHOR_IDENT");

    internal static void RequireCommitterIdentity(string? repository) =>
        GitProcess.RunRequired(repository, "Git committer identity is not configured", "var", "GIT_COMMITTER_IDENT");

    internal static byte[] ReadCommitObject(string repository, string commit, string failureMessage) =>
        GitProcess.RunRequiredBytes(repository, failureMessage, "--no-replace-objects", "cat-file", "commit", commit);

    internal static GitResult InspectFirstParentHistoryInChronologicalOrder(string repository) =>
        GitProcess.Run(repository, "--no-replace-objects", "rev-list", "--first-parent", "--reverse", "HEAD");

    internal static string ReadVerifiedHeadCommit(string repository, string failureMessage) =>
        GitProcess.RunRequired(repository, failureMessage,
            "--no-replace-objects", "rev-parse", "--verify", "HEAD^{commit}").Output.Trim();

    internal static string ReadRecursiveTreeEntries(string repository, string commit, string failureMessage) =>
        GitProcess.RunRequired(repository, failureMessage,
            "--no-replace-objects", "ls-tree", "-r", "-z", commit).Output;

    internal static string ReadMetadataDirectory(string repository, string failureMessage) =>
        GitProcess.RunRequired(repository, failureMessage, "rev-parse", "--absolute-git-dir").Output.Trim();

    internal static string ReadCommonMetadataDirectory(string repository, string failureMessage) =>
        GitProcess.RunRequired(repository, failureMessage, "rev-parse", "--git-common-dir").Output.Trim();

    internal static string ReadWorktreeRoot(string repository, string failureMessage) =>
        GitProcess.RunRequired(repository, failureMessage, "rev-parse", "--show-toplevel").Output.Trim();

    internal static string ReadCommitSubject(string repository, string commit, string failureMessage) =>
        GitProcess.RunRequired(repository, failureMessage,
            "--no-replace-objects", "log", "-1", "--format=%s", commit).Output.TrimEnd('\r', '\n');

    internal static GitResult ReadWorktreeStatus(
        string repository, string failureMessage, bool ignoreReplacementObjects, params string[] pathspecs)
    {
        string[] arguments = ["status", "--porcelain=v1", "-z", "--untracked-files=all", "--", .. pathspecs];
        return GitProcess.RunRequired(repository, failureMessage,
            ignoreReplacementObjects ? ["--no-replace-objects", .. arguments] : arguments);
    }

    // Backup and index inspection.
    internal static GitResult InspectSymbolicHeadWithoutReplacements(string repository) =>
        GitProcess.Run(repository, "--no-replace-objects", "symbolic-ref", "--quiet", "HEAD");

    internal static string ReadSymbolicHeadWithoutReplacements(string repository, string failureMessage) =>
        GitProcess.RequireSuccess(InspectSymbolicHeadWithoutReplacements(repository), failureMessage).Output.Trim();

    internal static string ReadUnmergedIndexEntries(string repository, string failureMessage) =>
        GitProcess.RunRequired(repository, failureMessage,
            "--no-replace-objects", "ls-files", "--unmerged", "-z").Output;

    internal static GitResult InspectMetadataPaths(string repository, params string[] names) =>
        GitProcess.Run(repository,
            ["--no-replace-objects", "rev-parse", .. names.SelectMany(name => new[] { "--git-path", name })]);

    internal static string ReadMetadataPath(string repository, string name, string failureMessage) =>
        GitProcess.RequireSuccess(InspectMetadataPaths(repository, name), failureMessage).Output.Trim();

    internal static string ReadPorcelainStatus(string repository, string failureMessage, params string[] paths) =>
        ReadWorktreeStatus(repository, failureMessage, ignoreReplacementObjects: true, paths).Output;

    internal static string ReadWorkingFileHashWithoutFilters(string repository, string path, string failureMessage) =>
        GitProcess.RequireSuccess(HashWorkingFilesWithoutFilters(repository, path), failureMessage).Output.Trim();

    internal static string ReadStagedObjectIdWithoutReplacements(string repository, string path, string failureMessage) =>
        GitProcess.RunRequired(repository, failureMessage,
            "--no-replace-objects", "rev-parse", $":{path}").Output.Trim();

    internal static string ReadTreeObjectIdWithoutReplacements(
        string repository, string treeish, string path, string failureMessage) =>
        GitProcess.RunRequired(repository, failureMessage,
            "--no-replace-objects", "rev-parse", $"{treeish}:{path}").Output.Trim();

    internal static void ForceStagePaths(string repository, string failureMessage, params string[] paths) =>
        GitProcess.RunRequired(repository, failureMessage,
            ["--no-replace-objects", "add", "--force", "--", .. paths]);

    internal static string ReadVerifiedHeadParent(string repository, string failureMessage) =>
        GitProcess.RunRequired(repository, failureMessage,
            "--no-replace-objects", "rev-parse", "--verify", "HEAD^").Output.Trim();

    internal static string[] ReadTreePaths(string repository, string tree, string failureMessage) =>
        ReadPaths(repository, failureMessage, ignoreReplacementObjects: true,
            ["ls-tree", "--name-only", "-r", "-z", tree]);

    internal static string[] ReadChangedTreePaths(string repository, string parent, string tree, string failureMessage) =>
        ReadPaths(repository, failureMessage, ignoreReplacementObjects: true,
            ["diff-tree", "--no-commit-id", "--name-only", "-r", "-z", parent, tree]);

    internal static GitResult InspectBooleanConfig(string repository, string key) =>
        GitProcess.Run(repository, "--no-replace-objects", "config", "--bool", "--get", key);

    internal static string ReadHeadSubject(string repository, string failureMessage) =>
        GitProcess.RunRequired(repository, failureMessage,
            "--no-replace-objects", "log", "-1", "--format=%s").Output.TrimEnd('\r', '\n');

    internal static GitResult InspectHeadReference(string repository) =>
        GitProcess.Run(repository, "--no-replace-objects", "rev-parse", "--verify", "--quiet", "HEAD");

    internal static GitResult InspectStagedChangesForPaths(string repository, params string[] paths) =>
        GitProcess.Run(repository,
            ["--no-replace-objects", "diff", "--cached", "--quiet", "--exit-code", "--", .. paths]);

    internal static string ReadHeadReference(string repository, string failureMessage) =>
        GitProcess.RunRequired(repository, failureMessage,
            "--no-replace-objects", "rev-parse", "--verify", "HEAD").Output.Trim();

    // Migration and checkout inspection.
    internal static GitResult ReadRemoteHead(string repository, string remote, TimeSpan? timeout = null) =>
        GitProcess.RunRemoteCommand(repository, ["ls-remote", "--symref", "--", remote, "HEAD"],
            "Remote HEAD query", timeout ?? TimeSpan.FromSeconds(30));

    internal static GitResult FetchRemoteCommit(string repository, string remote, string commit, TimeSpan? timeout = null) =>
        GitProcess.RunRemoteCommand(repository,
            ["fetch", "--no-tags", "--no-recurse-submodules", "--no-write-fetch-head", "--refmap=",
                "--no-auto-maintenance", "--no-write-commit-graph", "--", remote, commit],
            "Remote fetch", timeout ?? TimeSpan.FromMinutes(2));

    internal static string ReadObjectType(string repository, string objectId, string failureMessage) =>
        GitProcess.RunRequired(repository, failureMessage,
            "--no-replace-objects", "cat-file", "-t", objectId).Output.Trim();

    internal static GitResult InspectAncestry(string repository, string ancestor, string descendant) =>
        GitProcess.Run(repository, "--no-replace-objects", "merge-base", "--is-ancestor", ancestor, descendant);

    internal static string ReadCommitParentLine(string repository, string commit, string failureMessage) =>
        GitProcess.RunRequired(repository, failureMessage,
            "--no-replace-objects", "rev-list", "--parents", "-n", "1", commit).Output.Trim();

    internal static GitResult InspectUnfilteredStagedChanges(string repository) =>
        GitProcess.Run(repository, "diff", "--cached", "--quiet", "--exit-code", "--no-ext-diff", "--no-textconv");

    internal static string ReadIndexEntries(string repository, string failureMessage, params string[] paths) =>
        GitProcess.RunRequired(repository, failureMessage, ["ls-files", "--stage", "-z", "--", .. paths]).Output;

    internal static string ReadIgnoredUntrackedPaths(string repository, string failureMessage, params string[] paths) =>
        GitProcess.RunRequired(repository, failureMessage,
            ["ls-files", "--others", "--ignored", "--exclude-standard", "-z", "--", .. paths]).Output;

    internal static string ReadMigrationSectionStatus(string repository, string failureMessage) =>
        GitProcess.RunRequired(repository, failureMessage,
            "--no-optional-locks", "status", "--porcelain=v1", "-z", "--untracked-files=all", "--renames",
            "--ignore-submodules=none", "--", ".").Output;

    internal static string[] ListCommittedPathsUnder(string repository, string commit, string path, string failureMessage) =>
        GitProcess.RunRequired(repository, failureMessage,
            "--no-replace-objects", "ls-tree", "-r", "--name-only", "-z", commit, "--", path)
            .Output.Split('\0', StringSplitOptions.RemoveEmptyEntries);

    internal static string ReadMigrationCheckoutStatus(string repository, string failureMessage) =>
        GitProcess.RunRequired(repository, failureMessage,
            "--no-optional-locks", "-c", "core.fsmonitor=false", "status", "--porcelain=v1", "-z",
            "--untracked-files=all", "--ignore-submodules=none").Output;

    internal static GitResult FastForwardMigrationCheckout(string repository, string branch, string commit) =>
        GitProcess.Run(repository,
            "--no-replace-objects", "-c", "core.hooksPath=/dev/null",
            "-c", "core.fsmonitor=false", "-c", "core.autocrlf=false",
            "-c", "submodule.recurse=false", "-c", "merge.autostash=false",
            "-c", $"branch.{branch}.mergeOptions=", "merge", "--ff-only",
            "--no-autostash", "--no-edit", "--no-overwrite-ignore", "--no-stat", commit);

    internal static GitResult InspectExternalFilterConfiguration(string repository) =>
        GitProcess.Run(repository, "config", "--name-only", "--get-regexp", @"^filter\..*\.(clean|smudge|process)$");

    internal static GitResult InspectFsMonitorConfiguration(string repository) =>
        GitProcess.Run(repository, "config", "--type=bool", "--get", "core.fsmonitor");

    internal static string ReadMigrationCheckoutIndex(string repository, string failureMessage) =>
        GitProcess.RunRequired(repository, failureMessage,
            "--no-optional-locks", "-c", "core.fsmonitor=false", "ls-files", "--stage", "-z").Output;

    internal static GitResult InspectIgnoreRule(string repository, string path) =>
        GitProcess.Run(repository, "check-ignore", "--no-index", "--quiet", "--", path);

    internal static bool IsValidReferenceName(string repository, string name) =>
        GitProcess.Run(repository, "check-ref-format", name).ExitCode == 0;

    internal static GitResult InspectNullDelimitedConfigValues(string repository, string key) =>
        GitProcess.Run(repository, "config", "--null", "--get-all", key);

    // Portable target inspection.
    internal static GitResult InspectOptionalHeadCommit(
        string repository, bool ignoreReplacementObjects = false)
    {
        string[] arguments = ["rev-parse", "--verify", "--quiet", "HEAD^{commit}"];
        return GitProcess.Run(repository,
            ignoreReplacementObjects ? ["--no-replace-objects", .. arguments] : arguments);
    }

    internal static GitResult ListCommittedTargetEnrollments(string repository, string commit) =>
        GitProcess.RunRequired(repository, "Could not list committed target enrollments",
            "ls-tree", "-r", "--name-only", commit, "--", "environment/targets");

    internal static GitResult InspectStagedPathsAgainstCommit(
        string repository, string commit, params string[] paths) =>
        GitProcess.Run(repository,
            ["diff", "--cached", "--quiet", "--exit-code", commit, "--", .. paths]);

    internal static GitResult InspectTargetHistory(string repository, string commit, string path) =>
        GitProcess.RunRequired(repository, "Git could not inspect target history",
            "ls-tree", "--name-only", commit, "--", path);
}
