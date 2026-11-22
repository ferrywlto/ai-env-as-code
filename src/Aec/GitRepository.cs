namespace Aec;

// Repository identity and committed HEAD checks shared across workflows.
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

}
