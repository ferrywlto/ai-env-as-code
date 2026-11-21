namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class RegressionFollowupTests
{
    [Fact]
    public void HelpListsAllSupportedCommandsExactly() =>
        ChatGptInitTests.HelpListsAllSupportedCommandsExactly();

    [Theory]
    [InlineData("copilot")]
    [InlineData("source")]
    [Trait("Category", "LegacyMigration")]
    public void DriftAfterFirstProviderStopsSecondAndReportsPartialState(string mutation) =>
        MigrationApplyCoordinatorTests.DriftAfterFirstProviderStopsSecondAndReportsPartialState(mutation);

    [Fact]
    [Trait("Category", "LegacyMigration")]
    public void FreshInvocationRefusesUncommittedMarkerThenKeepsPriorCommitsOnRecovery() =>
        MigrationCommandTests.FreshInvocationRefusesUncommittedMarkerThenKeepsPriorCommitsOnRecovery();

    [Fact]
    [Trait("Category", "LegacyMigration")]
    public void StaleOrTamperedReviewStopsBeforeWriteStageOrCommit() =>
        MigrationLegacyCaptureCommitterTests.StaleOrTamperedReviewStopsBeforeWriteStageOrCommit();

    [Theory]
    [InlineData("dirty-target", "Dirty path is outside the selected migration sections")]
    [InlineData("dirty-source", "Dirty path is outside the selected migration sections")]
    [InlineData("missing-target", "Dirty path is outside the selected migration sections")]
    [InlineData("inconsistent-target", "Committed target does not match its authored sources")]
    [InlineData("oversize-render", "Rendered target exceeds 1 MiB")]
    [Trait("Category", "LegacyMigration")]
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
    [Trait("Category", "LegacyMigration")]
    public void StaleInputsStopBeforeAnyTargetReplacement(string kind) =>
        MigrationPortableTargetWriterTests.StaleInputsStopBeforeAnyTargetReplacement(kind);

    [Theory]
    [InlineData("both", 3)]
    [InlineData("codex", 2)]
    [Trait("Category", "LegacyMigration")]
    public void CommitsSourcesTargetsThenMarkerAndAppliesEnrolledProviders(string selected, int count) =>
        MigrationPostDoneCoordinatorTests.CommitsSourcesTargetsThenMarkerAndAppliesEnrolledProviders(selected, count);

    [Fact]
    [Trait("Category", "LegacyMigration")]
    public void ComponentFailureStopsWithoutMarkerOrLaterWrites() =>
        MigrationPostDoneCoordinatorTests.ComponentFailureStopsWithoutMarkerOrLaterWrites();

    [Theory]
    [InlineData("branch")]
    [InlineData("path")]
    [InlineData("index")]
    [InlineData("other-overlay")]
    [Trait("Category", "LegacyMigration")]
    public void StaleOrTamperedReviewIsRejectedWithoutWriterChanges(string state) =>
        MigrationTargetWriterTests.StaleOrTamperedReviewIsRejectedWithoutWriterChanges(state);
}
