namespace Aec;

// DataCommits contains source/target commits; Commit is the final marker commit.
internal sealed record MigrationPostDoneResult(string Commit,
    IReadOnlyList<string> DataCommits, IReadOnlyList<MigrationProviderApplyResult> Providers);

// The caller owns the DONE prompt. Compose only the already-reviewed completion
// components here; preparation, capture and public CLI dispatch stay separate.
internal static class MigrationPostDoneCoordinator
{
    public static MigrationPostDoneResult Complete(MigrationPreparation preparation,
        Action<string>? afterPhaseForTests = null, Action<string>? afterProviderForTests = null)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        var current = preparation;
        var commits = new List<string>();
        var completed = new List<string>();
        var phase = "sources";

        void ComponentReturned()
        {
            completed.Add(phase);
            afterPhaseForTests?.Invoke(phase);
        }

        try
        {
            var sources = MigrationSourceValidationPreview.Read(current);
            current = sources.Preparation;
            current = Advance(current, MigrationSourceCommitter.Commit(sources), commits);
            ComponentReturned();

            foreach (var harness in current.Harnesses)
            {
                phase = $"target:{harness.Provider}";
                var target = MigrationTargetPreview.Read(current, harness.Provider);
                current = Advance(current, MigrationTargetCommitter.WriteAndCommit(target), commits);
                ComponentReturned();
            }

            phase = "apply";
            // All reviews share the final target HEAD and are prepared before
            // either provider writes. The apply coordinator retains that review.
            var reviews = current.Harnesses.Select(harness =>
                MigrationApplyPreview.Read(current, harness.Provider)).ToArray();
            var providers = MigrationApplyCoordinator.Apply(reviews, afterProviderForTests);
            ComponentReturned();

            phase = "status";
            var completion = MigrationCompletionPreview.Read(current);
            ComponentReturned();

            phase = "completion";
            var commit = MigrationCompletionCommitter.WriteAndCommit(completion);
            return new MigrationPostDoneResult(commit, commits.ToArray(), providers);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
                                          InvalidOperationException or InvalidDataException or ArgumentException)
        {
            var names = completed.Count == 0 ? "none" : string.Join(", ", completed);
            throw new InvalidOperationException(
                $"Post-DONE migration stopped during {phase}. Completed components: {names}. " +
                $"Last pinned data HEAD: {current.Preflight.Commit}. " +
                "Repository/index/runtime changes or commits may remain; inspect before restarting. " +
                "No automatic rollback, retry or push was attempted. " + exception.Message, exception);
        }
    }

    private static MigrationPreparation Advance(MigrationPreparation current,
        string? commit, List<string> commits)
    {
        // A no-op retains the pinned HEAD. Never adopt whatever HEAD happens
        // to be after a component returns; an outside commit must stop the flow.
        var expected = commit ?? current.Preflight.Commit;
        if (ApplyCommand.ResolveHeadCommit(current.Preflight.Repository) != expected)
            throw new InvalidOperationException(
                $"Migration HEAD changed at a commit handoff. Component returned: {commit ?? "no commit"}; " +
                $"expected HEAD: {expected}.");
        if (commit is not null) commits.Add(commit);
        return current with
        {
            Preflight = current.Preflight with { Commit = expected },
            DirtySections = new MigrationDirtySections([])
        };
    }
}
