namespace Aec.Tests;

// These tests exercise only the in-memory confirmation prompt; they do not run migration work.
public sealed class MigrationDoneGateTests
{
    [Fact]
    public void ExactDoneConfirmsAndLeavesLaterInputUnread()
    {
        using var input = new StringReader("DONE\nleave this unread\n");
        using var output = new StringWriter();

        var result = MigrationDoneGate.Wait(input, output);

        Assert.Equal(MigrationDoneResult.Done, result);
        Assert.Equal("leave this unread", input.ReadLine());
        AssertPromptExplainsValidationIsNotCompletion(output.ToString());
    }

    [Theory]
    [InlineData("DONE\n")]
    [InlineData("DONE\r\n")]
    public void ExactDoneWorksWithLineEndingVariants(string text)
    {
        using var input = new StringReader(text);
        using var output = new StringWriter();

        Assert.Equal(MigrationDoneResult.Done, MigrationDoneGate.Wait(input, output));
    }

    [Theory]
    [InlineData("done\nDONE\n")]
    [InlineData("Done\nDONE\n")]
    [InlineData(" DONE\nDONE\n")]
    [InlineData("DONE \nDONE\n")]
    [InlineData("\nDONE\n")]
    public void InvalidOrBlankLineDoesNotConfirmAndNextExactDoneDoes(string text)
    {
        using var input = new StringReader(text);
        using var output = new StringWriter();

        Assert.Equal(MigrationDoneResult.Done, MigrationDoneGate.Wait(input, output));

        Assert.Null(input.ReadLine());
        Assert.Contains("DONE", output.ToString(), StringComparison.Ordinal);
        AssertPromptExplainsValidationIsNotCompletion(output.ToString());
    }

    [Fact]
    public void RepeatedInvalidLinesEachReceiveAReminder()
    {
        using var input = new StringReader("no\nnot yet\nDONE\n");
        using var output = new StringWriter();

        Assert.Equal(MigrationDoneResult.Done, MigrationDoneGate.Wait(input, output));

        var reminder = "Not confirmed. Enter exactly DONE when your sections are ready.";
        Assert.Equal(2, output.ToString().Split(reminder).Length - 1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not done\n")]
    public void EndOfInputCannotConfirm(string text)
    {
        using var input = new StringReader(text);
        using var output = new StringWriter();

        Assert.Equal(MigrationDoneResult.Cancelled, MigrationDoneGate.Wait(input, output));
        Assert.Contains("DONE", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void PreCancelledTokenDoesNotReadOrWrite()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var input = new StringReader("DONE\n");
        using var output = new StringWriter();

        Assert.Equal(MigrationDoneResult.Cancelled,
            MigrationDoneGate.Wait(input, output, cancellation.Token));

        Assert.Equal("DONE", input.ReadLine());
        Assert.Equal(string.Empty, output.ToString());
    }

    [Fact]
    public void CancellationObservedAfterReadDiscardsDoneLine()
    {
        using var cancellation = new CancellationTokenSource();
        using var input = new CancellingReader("DONE\n", cancellation);
        using var output = new StringWriter();

        Assert.Equal(MigrationDoneResult.Cancelled,
            MigrationDoneGate.Wait(input, output, cancellation.Token));
    }

    private static void AssertPromptExplainsValidationIsNotCompletion(string prompt)
    {
        Assert.Contains("validation", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("does not complete migration", prompt, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("prepared sections", prompt, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class CancellingReader(string text, CancellationTokenSource cancellation) : StringReader(text)
    {
        public override string? ReadLine()
        {
            var line = base.ReadLine();
            cancellation.Cancel();
            return line;
        }
    }
}
