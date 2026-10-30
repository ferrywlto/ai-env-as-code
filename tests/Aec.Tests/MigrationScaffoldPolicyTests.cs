namespace Aec.Tests;

// Policy tests use no Git process, filesystem fixture or process-wide settings.
public sealed class MigrationScaffoldPolicyTests
{
    [Theory]
    [InlineData(false, false, nameof(MigrationSectionChoice.Unchanged), nameof(MigrationScaffoldAction.CreateEmpty))]
    [InlineData(false, false, nameof(MigrationSectionChoice.Preserve), nameof(MigrationScaffoldAction.CreateEmpty))]
    [InlineData(false, false, nameof(MigrationSectionChoice.Overwrite), nameof(MigrationScaffoldAction.CreateEmpty))]
    [InlineData(false, true, nameof(MigrationSectionChoice.Unchanged), nameof(MigrationScaffoldAction.PreserveExisting))]
    [InlineData(false, true, nameof(MigrationSectionChoice.Preserve), nameof(MigrationScaffoldAction.PreserveExisting))]
    [InlineData(false, true, nameof(MigrationSectionChoice.Overwrite), nameof(MigrationScaffoldAction.PreserveExisting))]
    [InlineData(true, true, nameof(MigrationSectionChoice.Unchanged), nameof(MigrationScaffoldAction.NeedsDirtyChoice))]
    [InlineData(true, false, nameof(MigrationSectionChoice.Unchanged), nameof(MigrationScaffoldAction.NeedsDirtyChoice))]
    [InlineData(true, true, nameof(MigrationSectionChoice.Preserve), nameof(MigrationScaffoldAction.PreserveDirty))]
    [InlineData(true, false, nameof(MigrationSectionChoice.Preserve), nameof(MigrationScaffoldAction.PreserveDeletion))]
    [InlineData(true, true, nameof(MigrationSectionChoice.Overwrite), nameof(MigrationScaffoldAction.ResetEmpty))]
    [InlineData(true, false, nameof(MigrationSectionChoice.Overwrite), nameof(MigrationScaffoldAction.ResetEmpty))]
    public void ChoiceOnlyAffectsDirtySources(bool dirty, bool exists, string choiceName, string actionName)
    {
        byte[]? content = exists ? [] : null;
        var action = MigrationScaffoldPreview.ChooseAction(dirty, content, Enum.Parse<MigrationSectionChoice>(choiceName));
        Assert.Equal(Enum.Parse<MigrationScaffoldAction>(actionName), action);
    }

    [Theory]
    [InlineData(nameof(MigrationScaffoldAction.CreateEmpty), false, true, false)]
    [InlineData(nameof(MigrationScaffoldAction.CreateEmpty), true, false, true)]
    [InlineData(nameof(MigrationScaffoldAction.ResetEmpty), false, true, true)]
    [InlineData(nameof(MigrationScaffoldAction.ResetEmpty), true, false, true)]
    [InlineData(nameof(MigrationScaffoldAction.PreserveExisting), false, false, false)]
    [InlineData(nameof(MigrationScaffoldAction.PreserveDirty), false, false, false)]
    [InlineData(nameof(MigrationScaffoldAction.PreserveDirty), true, false, true)]
    [InlineData(nameof(MigrationScaffoldAction.PreserveDeletion), false, false, true)]
    [InlineData(nameof(MigrationScaffoldAction.NeedsDirtyChoice), false, false, true)]
    public void ProposalBytesNeverAuthorizeAResetOrUnstage(string actionName, bool staged, bool hasProposal, bool blocked)
    {
        var file = new MigrationScaffoldFile("environment/shared/instructions.md",
            null, null, "", null, staged, Enum.Parse<MigrationScaffoldAction>(actionName));
        if (hasProposal) Assert.Empty(Assert.IsType<byte[]>(file.ProposedContent));
        else Assert.Null(file.ProposedContent);
        Assert.Equal(blocked, file.BlockedForFutureWrite);
    }

    [Fact]
    public void OverwritePromptWarnsThatDiscardedDraftsMayNotBeRecoverable()
    {
        var dirty = new MigrationDirtySections(["environment/shared/instructions.md"]);
        using var input = new StringReader("Overwrite\n");
        using var output = new StringWriter();
        Assert.Equal(MigrationSectionChoice.Overwrite, dirty.Prompt(input, output));
        Assert.Contains("empty Markdown for you to refill", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("Clean files are preserved", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("may not be recoverable from Git history", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("does not change any files", output.ToString(), StringComparison.Ordinal);
    }
}
