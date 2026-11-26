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
        var result = GitProcess.Run(repository, "--no-replace-objects", "rev-parse", "--verify", "--quiet", "HEAD^{commit}");
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
        GitProcess.RunRequired(repository, failureMessage,
            "ls-files", "--stage", "-z", "--", path).Output;

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
}
