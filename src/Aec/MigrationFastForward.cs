namespace Aec;

// Internal migration step only. Failure after advancement retains the retrieved
// commit for inspection; it never attempts reset, rollback, capture or deployment.
internal static class MigrationFastForward
{
    public static MigrationPreflight Execute(MigrationPreflight approved,
        MigrationFetchPreparation fetched, IReadOnlyList<MigrationHarness> harnesses,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        EnsureSafeGitConfiguration(approved.Repository);
        var current = MigrationPreflight.Read(approved.Repository, approved.TargetFormat.ToString(2));
        if (current != approved)
        {
            throw new InvalidOperationException("Migration format or HEAD changed; restart preflight.");
        }
        EnsureSameHarnesses(current, harnesses);
        if (MigrationRemotePreflight.Read(current) != fetched.Remote ||
            MigrationFetchPreparation.ReadAncestry(current, fetched.Remote) != fetched.Ancestry)
        {
            throw new InvalidOperationException("Fetched upstream snapshot changed; restart migration preflight.");
        }

        if (fetched.Ancestry == MigrationAncestry.Behind)
        {
            var status = GitProcess.RunRequired(current.Repository, "Git could not inspect migration checkout",
                "--no-optional-locks", "-c", "core.fsmonitor=false", "status", "--porcelain=v1", "-z",
                "--untracked-files=all", "--ignore-submodules=none").Output;
            if (status.Length != 0)
            {
                throw new InvalidOperationException("Migration fast-forward requires a clean checkout; preserve or resolve local edits first. No stash or discard was performed.");
            }
            EnsureSafeGitConfiguration(current.Repository);
            if (MigrationRemotePreflight.Read(current) != fetched.Remote)
            {
                throw new InvalidOperationException("Fetched upstream changed before advancement; restart migration preflight.");
            }
            // A cancellation during the bounded remote checks must not advance
            // HEAD. After merge succeeds, return its real result, never hide it.
            cancellationToken.ThrowIfCancellationRequested();

            var branch = fetched.Remote.BranchRef["refs/heads/".Length..];
            // Per-command settings leave user config intact. Disable hooks and
            // branch merge options, autostash and recursive submodule updates.
            // Git's default overwrites ignored files: explicitly refuse instead.
            var merge = GitProcess.Run(current.Repository,
                "--no-replace-objects", "-c", "core.hooksPath=/dev/null",
                "-c", "core.fsmonitor=false", "-c", "core.autocrlf=false",
                "-c", "submodule.recurse=false", "-c", "merge.autostash=false",
                "-c", $"branch.{branch}.mergeOptions=", "merge", "--ff-only",
                "--no-autostash", "--no-edit", "--no-overwrite-ignore", "--no-stat",
                fetched.Remote.AdvertisedCommit);
            if (merge.ExitCode != 0)
            {
                throw new InvalidOperationException("Migration fast-forward failed; inspect the checkout before retrying. No rollback, capture or apply was attempted.");
            }
        }

        var expected = fetched.Ancestry == MigrationAncestry.Behind
            ? current with { Commit = fetched.Remote.AdvertisedCommit } : current;
        try
        {
            MigrationRemotePreflight.EnsureSnapshot(expected, fetched.Remote.BranchRef);
            var validated = MigrationPreflight.Read(current.Repository, current.TargetFormat.ToString(2));
            if (validated != expected)
            {
                throw new InvalidOperationException("Repository format changed after migration preparation.");
            }
            EnsureSameHarnesses(validated, harnesses);
            return validated;
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or IOException or InvalidDataException)
        {
            throw new InvalidOperationException("Migration post-advancement validation failed; retrieved history is retained. Inspect the checkout before restarting. No rollback, capture or apply was attempted.", exception);
        }
    }

    private static void EnsureSameHarnesses(MigrationPreflight current, IReadOnlyList<MigrationHarness> approved)
    {
        var selected = MigrationHarnessSelection.Read(current);
        if (selected.Count == 0 || !selected.SequenceEqual(approved))
        {
            throw new InvalidOperationException("Local harness enrollment changed or is absent; restart migration preflight.");
        }
    }

    internal static void EnsureSafeGitConfiguration(string repository)
    {
        // Filters can execute arbitrary commands during status or checkout, even
        // with hooks disabled. Keep this narrow step conservative: refuse them
        // instead of altering the user's configuration or running their tools.
        var filters = GitProcess.Run(repository, "config", "--name-only", "--get-regexp",
            @"^filter\..*\.(clean|smudge|process)$");
        if (filters.ExitCode != 1)
        {
            throw new InvalidOperationException("Migration fast-forward does not run configured external Git filters; review the checkout configuration first.");
        }
        var monitor = GitProcess.Run(repository, "config", "--type=bool", "--get", "core.fsmonitor");
        if (monitor.ExitCode != 1 && (monitor.ExitCode != 0 || monitor.Output.Trim() != "false"))
        {
            throw new InvalidOperationException("Migration fast-forward requires Git fsmonitor to be disabled; review the checkout configuration first.");
        }
    }
}
