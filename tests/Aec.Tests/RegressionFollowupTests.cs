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

    [Fact]
    public void FreshInvocationRefusesUncommittedMarkerThenKeepsPriorCommitsOnRecovery() =>
        MigrationCommandTests.FreshInvocationRefusesUncommittedMarkerThenKeepsPriorCommitsOnRecovery();

    [Fact]
    public void StaleOrTamperedReviewStopsBeforeWriteStageOrCommit() =>
        MigrationLegacyCaptureCommitterTests.StaleOrTamperedReviewStopsBeforeWriteStageOrCommit();

    [Theory]
    [InlineData("dirty-target", "Dirty path is outside the selected migration sections")]
    [InlineData("dirty-source", "Dirty path is outside the selected migration sections")]
    [InlineData("missing-target", "Dirty path is outside the selected migration sections")]
    [InlineData("inconsistent-target", "Committed target does not match its authored sources")]
    [InlineData("oversize-render", "Rendered target exceeds 1 MiB")]
    public void OtherPlatformProblemsAreRejectedDuringPreparation(string kind, string expectedDiagnostic) =>
        MigrationPortableTargetWriterTests.OtherPlatformProblemsAreRejectedDuringPreparation(kind, expectedDiagnostic);

    [Theory]
    [InlineData("head")]
    [InlineData("branch")]
    [InlineData("other-runtime")]
    [InlineData("marker")]
    [InlineData("other-platform")]
    [InlineData("staged")]
    [InlineData("raw-index")]
    public void StaleInputsStopBeforeAnyTargetReplacement(string kind) =>
        MigrationPortableTargetWriterTests.StaleInputsStopBeforeAnyTargetReplacement(kind);

    [Theory]
    [InlineData("both", 3)]
    [InlineData("codex", 2)]
    public void CommitsSourcesTargetsThenMarkerAndAppliesEnrolledProviders(string selected, int count) =>
        MigrationPostDoneCoordinatorTests.CommitsSourcesTargetsThenMarkerAndAppliesEnrolledProviders(selected, count);

    [Fact]
    public void ComponentFailureStopsWithoutMarkerOrLaterWrites() =>
        MigrationPostDoneCoordinatorTests.ComponentFailureStopsWithoutMarkerOrLaterWrites();

    [Theory]
    [InlineData("branch")]
    [InlineData("path")]
    [InlineData("index")]
    [InlineData("other-overlay")]
    public void StaleOrTamperedReviewIsRejectedWithoutWriterChanges(string state) =>
        MigrationTargetWriterTests.StaleOrTamperedReviewIsRejectedWithoutWriterChanges(state);
}
