namespace Aec.Tests;

[Trait("Category", "Unit")]
public sealed class MigrationPostDonePhaseTests
{
    private static readonly string[] PhaseOrder = ["sources", "target:codex", "target:copilot", "apply", "status"];

    [Theory]
    [InlineData("sources", 1, "sources-commit")]
    [InlineData("target:codex", 2, "target:codex-commit")]
    [InlineData("target:copilot", 3, "target:copilot-commit")]
    [InlineData("apply", 4, "target:copilot-commit")]
    [InlineData("status", 5, "target:copilot-commit")]
    public void PhaseFailureStopsLaterOperationsAndReportsThePinnedHead(string stop, int phaseCount, string expectedHead)
    {
        const string repository = "/data";
        const string originalHead = "original";
        var preparation = new MigrationPreparation(
            new MigrationPreflight(repository, originalHead, MigrationPreflight.LegacyFormat, MigrationPreflight.SectionedFormat),
            originalHead,
            [new MigrationHarness("codex", "/codex", "/codex/AGENTS.md"),
                new MigrationHarness("copilot", "/copilot", "/copilot/copilot-instructions.md")],
            new MigrationDirtySections(["environment/shared/instructions.md"]), MigrationSectionChoice.Preserve);
        var sourceReview = new MigrationSourceValidationPreview(preparation, "main", [], preparation.DirtySections.Paths);
        var head = originalHead;
        var operations = new List<string>();
        var returned = new List<string>();
        var injected = new InvalidOperationException("Injected component failure");

        void AssertPinnedInput(MigrationPreparation current)
        {
            Assert.Equal(head, current.Preflight.Commit);
            Assert.Equal(originalHead, current.OriginalCommit);
            Assert.Empty(current.DirtySections.Paths);
        }

        string Commit(string phase)
        {
            operations.Add(phase);
            head = $"{phase}-commit";
            return head;
        }

        var phases = new MigrationPostDoneCoordinator.Phases(
            current =>
            {
                Assert.Same(preparation, current);
                return sourceReview;
            },
            reviewed =>
            {
                Assert.Same(sourceReview, reviewed);
                return Commit("sources");
            },
            (current, provider) =>
            {
                AssertPinnedInput(current);
                return Commit($"target:{provider}");
            },
            (current, _) =>
            {
                AssertPinnedInput(current);
                operations.Add("apply");
                return [new MigrationProviderApplyResult("codex", "applied"), new MigrationProviderApplyResult("copilot", "applied")];
            },
            current =>
            {
                AssertPinnedInput(current);
                operations.Add("status");
                return new MigrationCompletionPreview(current, "main", [], [], [], new Dictionary<string, byte[]?>());
            },
            _ => Commit("completion"),
            currentRepository =>
            {
                Assert.Equal(repository, currentRepository);
                return head;
            });

        var error = Assert.Throws<InvalidOperationException>(() =>
            MigrationPostDoneCoordinator.Complete(phases, preparation, phase =>
            {
                returned.Add(phase);
                if (phase == stop) throw injected;
            }));

        var expectedPhases = PhaseOrder[..phaseCount];
        Assert.Equal(expectedPhases, operations);
        Assert.Equal(expectedPhases, returned);
        Assert.Same(injected, error.InnerException);
        Assert.Contains($"stopped during {stop}.", error.Message, StringComparison.Ordinal);
        Assert.Contains($"Completed components: {string.Join(", ", expectedPhases)}.", error.Message, StringComparison.Ordinal);
        Assert.Contains($"Last pinned data HEAD: {expectedHead}.", error.Message, StringComparison.Ordinal);
        Assert.Contains("No automatic rollback, retry or push was attempted.", error.Message, StringComparison.Ordinal);
    }
}
