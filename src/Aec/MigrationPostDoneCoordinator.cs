namespace Aec;

// DataCommits contains source/target commits; Commit is the final marker commit.
internal sealed record MigrationPostDoneResult(string Commit,
    IReadOnlyList<string> DataCommits, IReadOnlyList<MigrationProviderApplyResult> Providers);

internal static class MigrationPostDoneCoordinator
{
    private static readonly Phases RepositoryPhases = new(
        MigrationSourceValidationPreview.Read,
        MigrationSourceCommitter.Commit,
        (current, provider) => MigrationTargetCommitter.WriteAndCommit(MigrationTargetPreview.Read(current, provider)),
        ApplyProviders,
        current => MigrationCompletionPreview.Read(current),
        completion => MigrationCompletionCommitter.WriteAndCommit(completion),
        ApplyCommand.ResolveHeadCommit);

    public static MigrationPostDoneResult? WaitAndComplete(MigrationScaffoldResult scaffold,
        TextReader input, TextWriter output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scaffold);
        if (MigrationDoneGate.Wait(input, output, cancellationToken) != MigrationDoneResult.Done ||
            cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        return Complete(scaffold.Preparation);
    }

    public static MigrationPostDoneResult Complete(MigrationPreparation preparation,
        Action<string>? afterPhaseForTests = null, Action<string>? afterProviderForTests = null) =>
        Complete(RepositoryPhases, preparation, afterPhaseForTests, afterProviderForTests);

    internal static MigrationPostDoneResult Complete(Phases phases, MigrationPreparation preparation,
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
            var sources = phases.ReviewSources(current);
            current = sources.Preparation;
            current = Advance(current, phases.CommitSources(sources), commits, phases.ReadHead);
            ComponentReturned();

            foreach (var harness in current.Harnesses)
            {
                phase = $"target:{harness.Provider}";
                current = Advance(current, phases.CommitTarget(current, harness.Provider), commits, phases.ReadHead);
                ComponentReturned();
            }

            phase = "apply";
            var providers = phases.Apply(current, afterProviderForTests);
            ComponentReturned();

            phase = "status";
            var completion = phases.ReviewCompletion(current);
            ComponentReturned();

            phase = "completion";
            var commit = phases.CommitCompletion(completion);
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

    private static IReadOnlyList<MigrationProviderApplyResult> ApplyProviders(
        MigrationPreparation current, Action<string>? afterProviderForTests)
    {
        // All reviews share the final target HEAD and are prepared before
        // either provider writes. The apply coordinator retains that review.
        var reviews = current.Harnesses.Select(harness =>
            MigrationApplyPreview.Read(current, harness.Provider)).ToArray();
        return MigrationApplyCoordinator.Apply(reviews, afterProviderForTests);
    }

    private static MigrationPreparation Advance(MigrationPreparation current,
        string? commit, List<string> commits, Func<string, string> readHead)
    {
        // A no-op retains the pinned HEAD. Never adopt whatever HEAD happens
        // to be after a component returns; an outside commit must stop the flow.
        var expected = commit ?? current.Preflight.Commit;
        if (readHead(current.Preflight.Repository) != expected)
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

    internal sealed record Phases(
        Func<MigrationPreparation, MigrationSourceValidationPreview> ReviewSources,
        Func<MigrationSourceValidationPreview, string?> CommitSources,
        Func<MigrationPreparation, string, string?> CommitTarget,
        Func<MigrationPreparation, Action<string>?, IReadOnlyList<MigrationProviderApplyResult>> Apply,
        Func<MigrationPreparation, MigrationCompletionPreview> ReviewCompletion,
        Func<MigrationCompletionPreview, string> CommitCompletion,
        Func<string, string> ReadHead);
}
