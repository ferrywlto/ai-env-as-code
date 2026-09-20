namespace Aec.Tests;

public sealed class CopilotInstructionBlockTests
{
    private static readonly string Repository = Path.Combine(
        Path.GetTempPath(),
        "aec-copilot-block-tests",
        "repository");

    [Fact]
    public void PrependsTheManagedBlockWithoutChangingBomOrCrLfBytes()
    {
        var original = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat("# Existing\r\nKeep this.\r\n"u8.ToArray())
            .ToArray();

        var merged = CopilotInstructionBlock.Merge(original, Repository);

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, merged[..3]);
        Assert.Contains(
            "<!-- AEC:COPILOT:BEGIN version=1 -->\r\n",
            System.Text.Encoding.UTF8.GetString(merged),
            StringComparison.Ordinal);
        Assert.True(merged.AsSpan(3).EndsWith("# Existing\r\nKeep this.\r\n"u8));
    }

    [Fact]
    public void RecognizesOnlyTheExactEmittedBlock()
    {
        var managed = CopilotInstructionBlock.Merge("Original.\n"u8.ToArray(), Repository);

        Assert.Equal(Repository, CopilotInstructionBlock.ReadRepositoryBinding(managed));
        Assert.Equal(managed, CopilotInstructionBlock.Merge(managed, Repository));

        var malformed = "<!-- AEC:COPILOT:BEGIN version=1 -->\ninvalid\n<!-- AEC:COPILOT:END -->\n"u8.ToArray();
        Assert.Throws<InvalidDataException>(() => CopilotInstructionBlock.ReadRepositoryBinding(malformed));
    }
}
