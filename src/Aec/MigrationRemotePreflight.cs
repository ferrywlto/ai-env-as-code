namespace Aec;

// An advertised tip is advisory, not an ancestry or fast-forward result. Later
// fetch/pull preparation must revalidate it; this helper never updates Git refs.
internal sealed record MigrationRemotePreflight(string Remote, string BranchRef, string AdvertisedCommit, string RemoteUrl)
{
    public static MigrationRemotePreflight Read(MigrationPreflight preflight)
    {
        var repository = preflight.Repository;
        var branch = ReadBranch(repository);
        EnsureSnapshot(preflight, branch);
        var branchName = branch["refs/heads/".Length..];
        var remote = ReadSingleConfig(repository, $"branch.{branchName}.remote");
        var merge = ReadSingleConfig(repository, $"branch.{branchName}.merge");
        if (remote == "." || remote.StartsWith('-') ||
            GitProcess.Run(repository, "check-ref-format", $"refs/remotes/{remote}/aec").ExitCode != 0)
        {
            throw new InvalidOperationException("Migration requires one named upstream remote, not local-only or invalid tracking.");
        }
        if (!string.Equals(branch, merge, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Current branch and configured upstream branch differ; use the remote default-branch checkout for migration.");
        }

        // Require a real configured remote. Otherwise ls-remote can interpret an
        // undefined remote name as a repository path/URL instead of an upstream.
        var url = ReadSingleConfig(repository, $"remote.{remote}.url");
        var advertised = GitProcess.ReadRemoteHead(repository, remote);
        if (advertised.ExitCode != 0)
        {
            // Transport stderr can contain credential-bearing URLs. Report the
            // operation boundary without echoing those values into diagnostics.
            throw new InvalidOperationException("The configured upstream remote could not advertise HEAD; check connectivity and authentication.");
        }
        var (defaultBranch, commit) = ParseAdvertisement(advertised.Output);
        if (!string.Equals(branch, defaultBranch, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Migration requires the remote default-branch checkout. Current: {branch}. Default: {defaultBranch}.");
        }

        EnsureSnapshot(preflight, branch);
        return new MigrationRemotePreflight(remote, defaultBranch, commit, url);
    }

    internal static (string BranchRef, string Commit) ParseAdvertisement(string advertisement)
    {
        string? branch = null;
        string? commit = null;
        foreach (var line in advertisement.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.Split('\t');
            if (fields.Length != 2 || fields[1] != "HEAD") throw InvalidAdvertisement();
            if (fields[0].StartsWith("ref: refs/heads/", StringComparison.Ordinal) && branch is null)
            {
                branch = fields[0]["ref: ".Length..];
                if (branch.Length == "refs/heads/".Length) throw InvalidAdvertisement();
            }
            else if (commit is null && fields[0].Length is 40 or 64 &&
                     fields[0].All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f') &&
                     fields[0].Any(character => character != '0'))
            {
                commit = fields[0];
            }
            else
            {
                throw InvalidAdvertisement();
            }
        }
        return branch is not null && commit is not null ? (branch, commit) : throw InvalidAdvertisement();
    }

    private static InvalidDataException InvalidAdvertisement() =>
        new("Remote HEAD must advertise exactly one default branch and one valid commit; unresolved or ambiguous HEAD stops migration.");

    private static string ReadSingleConfig(string repository, string key)
    {
        var result = GitProcess.Run(repository, "config", "--null", "--get-all", key);
        var values = result.Output.Split('\0');
        if (result.ExitCode != 0 || values.Length != 2 || values[1].Length != 0 || string.IsNullOrWhiteSpace(values[0]))
        {
            throw new InvalidOperationException($"Migration requires exactly one nonempty configured value for {key}.");
        }
        return values[0];
    }

    private static string ReadBranch(string repository)
    {
        var result = GitProcess.Run(repository, "symbolic-ref", "--quiet", "HEAD");
        var branch = result.Output.TrimEnd('\r', '\n');
        return result.ExitCode == 0 && branch.StartsWith("refs/heads/", StringComparison.Ordinal) && branch.Length > "refs/heads/".Length
            ? branch : throw new InvalidOperationException("Migration requires a checked-out local branch; detached HEAD is not supported.");
    }

    internal static void EnsureSnapshot(MigrationPreflight preflight, string branch)
    {
        if (!string.Equals(preflight.Commit, ApplyCommand.ResolveHeadCommit(preflight.Repository), StringComparison.Ordinal) ||
            !string.Equals(branch, ReadBranch(preflight.Repository), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Repository HEAD or branch changed during migration remote preflight.");
        }
    }
}
