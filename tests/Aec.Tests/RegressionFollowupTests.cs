namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class RegressionFollowupTests
{
    [Fact]
    public void HelpListsAllSupportedCommandsExactly() =>
        ChatGptInitTests.HelpListsAllSupportedCommandsExactly();

    [Theory]
    [InlineData("copilot")]
    [InlineData("codex")]
    [InlineData("source")]
    [InlineData("target")]
    [InlineData("exception")]
    public void DriftAfterFirstProviderStopsSecondAndReportsPartialState(string mutation) =>
        MigrationApplyCoordinatorTests.DriftAfterFirstProviderStopsSecondAndReportsPartialState(mutation);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FreshInvocationPreservesDraftsAndCompletesAfterInterruption(bool invalidDraft) =>
        MigrationCommandTests.FreshInvocationPreservesDraftsAndCompletesAfterInterruption(invalidDraft);

    [Fact]
    public void FreshInvocationRefusesUncommittedMarkerThenKeepsPriorCommitsOnRecovery() =>
        MigrationCommandTests.FreshInvocationRefusesUncommittedMarkerThenKeepsPriorCommitsOnRecovery();

    [Theory]
    [InlineData("head")]
    [InlineData("branch")]
    [InlineData("runtime")]
    [InlineData("canonical")]
    [InlineData("marker")]
    [InlineData("enrollment")]
    [InlineData("proposal")]
    [InlineData("path")]
    [InlineData("missing-row")]
    public void StaleOrTamperedReviewStopsBeforeWriteStageOrCommit(string change) =>
        MigrationLegacyCaptureCommitterTests.StaleOrTamperedReviewStopsBeforeWriteStageOrCommit(change);

    [Theory]
    [InlineData("head")]
    [InlineData("branch")]
    [InlineData("runtime")]
    [InlineData("other-runtime")]
    [InlineData("config")]
    [InlineData("marker")]
    [InlineData("canonical")]
    public void StaleInputsStopBeforeAnyReplacement(string kind) =>
        MigrationPortableSourceWriterTests.StaleInputsStopBeforeAnyReplacement(kind);

    [Theory]
    [InlineData("dirty-target")]
    [InlineData("dirty-source")]
    [InlineData("missing-target")]
    [InlineData("inconsistent-target")]
    [InlineData("invalid-enrollment")]
    [InlineData("invalid-provider")]
    [InlineData("invalid-filename")]
    [InlineData("invalid-structure")]
    [InlineData("oversize-render")]
    public void OtherPlatformProblemsAreRejectedDuringPreparation(string kind) =>
        MigrationPortableTargetWriterTests.OtherPlatformProblemsAreRejectedDuringPreparation(kind);

    [Theory]
    [InlineData("head")]
    [InlineData("branch")]
    [InlineData("runtime")]
    [InlineData("other-runtime")]
    [InlineData("runtime-config")]
    [InlineData("marker")]
    [InlineData("source")]
    [InlineData("other-platform-source")]
    [InlineData("canonical-config")]
    [InlineData("target")]
    [InlineData("other-platform-target")]
    [InlineData("staged")]
    [InlineData("raw-index")]
    public void StaleInputsStopBeforeAnyTargetReplacement(string kind) =>
        MigrationPortableTargetWriterTests.StaleInputsStopBeforeAnyTargetReplacement(kind);

    [Theory]
    [InlineData("both", 3)]
    [InlineData("codex", 2)]
    public void CommitsSourcesTargetsThenMarkerAndAppliesEnrolledProviders(string selected, int count) =>
        MigrationPostDoneCoordinatorTests.CommitsSourcesTargetsThenMarkerAndAppliesEnrolledProviders(selected, count);

    [Theory]
    [InlineData("sources", 1)]
    [InlineData("target:codex", 2)]
    [InlineData("target:copilot", 3)]
    [InlineData("apply", 3)]
    [InlineData("status", 3)]
    public void ComponentFailureStopsWithoutMarkerOrLaterWrites(string stop, int expectedCommits) =>
        MigrationPostDoneCoordinatorTests.ComponentFailureStopsWithoutMarkerOrLaterWrites(stop, expectedCommits);

    [Theory]
    [InlineData("head")]
    [InlineData("branch")]
    [InlineData("context")]
    [InlineData("platform")]
    [InlineData("harness")]
    [InlineData("path")]
    [InlineData("action")]
    [InlineData("proposal")]
    [InlineData("baseline")]
    [InlineData("source-snapshot")]
    [InlineData("source-membership")]
    [InlineData("index")]
    [InlineData("target-ignored")]
    [InlineData("other-overlay")]
    [InlineData("other-enrollment")]
    public void StaleOrTamperedReviewIsRejectedWithoutWriterChanges(string state) =>
        MigrationTargetWriterTests.StaleOrTamperedReviewIsRejectedWithoutWriterChanges(state);
}
