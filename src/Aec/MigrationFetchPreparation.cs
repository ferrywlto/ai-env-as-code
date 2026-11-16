namespace Aec;

internal enum MigrationAncestry { Equal, Behind, Ahead }

// Fetching can add objects even when a later check stops. It never advances the
// checkout, and this preview is not permission to reuse a stale tip for a merge.
internal sealed record MigrationFetchPreparation(MigrationRemotePreflight Remote, MigrationAncestry Ancestry)
{
    public static MigrationFetchPreparation Read(MigrationPreflight preflight, MigrationRemotePreflight approvedRemote)
    {
        EnsureSameRemote(preflight, approvedRemote);
        // Fetch the verified default-branch tip by immutable object ID. Empty
        // refmap and disabled FETCH_HEAD prevent configured wildcard/mirror
        // refspecs from redirecting updates into local branches or other refs.
        var fetched = GitProcess.FetchRemoteCommit(preflight.Repository, approvedRemote.Remote,
            approvedRemote.AdvertisedCommit);
        if (fetched.ExitCode != 0)
        {
            throw new InvalidOperationException("Migration fetch failed; local branch was not advanced. Check transport and remote availability.");
        }
        EnsureSameRemote(preflight, approvedRemote);

        return new MigrationFetchPreparation(approvedRemote, ReadAncestry(preflight, approvedRemote));
    }

    internal static MigrationAncestry ReadAncestry(MigrationPreflight preflight, MigrationRemotePreflight approvedRemote)
    {
        var type = GitProcess.RunRequired(preflight.Repository, "Git could not inspect the fetched object",
            "--no-replace-objects", "cat-file", "-t", approvedRemote.AdvertisedCommit).Output.Trim();
        if (type != "commit") throw new InvalidDataException("Fetched default-branch tip is not a commit.");

        MigrationAncestry ancestry;
        if (string.Equals(preflight.Commit, approvedRemote.AdvertisedCommit, StringComparison.Ordinal))
        {
            ancestry = MigrationAncestry.Equal;
        }
        else if (IsAncestor(preflight.Repository, preflight.Commit, approvedRemote.AdvertisedCommit))
        {
            ancestry = MigrationAncestry.Behind;
        }
        else if (IsAncestor(preflight.Repository, approvedRemote.AdvertisedCommit, preflight.Commit))
        {
            ancestry = MigrationAncestry.Ahead;
        }
        else
        {
            throw new InvalidOperationException("Local and remote history diverged; migration stopped without merging or advancing the branch.");
        }

        MigrationRemotePreflight.EnsureSnapshot(preflight, approvedRemote.BranchRef);
        return ancestry;
    }

    private static void EnsureSameRemote(MigrationPreflight preflight, MigrationRemotePreflight approved)
    {
        // Record equality includes the configured URL as well as remote/branch/tip;
        // diagnostics must not print the record because its URL may hold credentials.
        if (MigrationRemotePreflight.Read(preflight) != approved)
        {
            throw new InvalidOperationException("Selected upstream, default branch or advertised tip changed; restart migration preflight.");
        }
    }

    private static bool IsAncestor(string repository, string ancestor, string descendant)
    {
        var result = GitProcess.Run(repository, "--no-replace-objects", "merge-base", "--is-ancestor", ancestor, descendant);
        return result.ExitCode switch
        {
            0 => true,
            1 => false,
            _ => throw new InvalidOperationException("Git could not determine fetched history ancestry; migration stopped.")
        };
    }
}
