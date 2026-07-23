using System.Text;

namespace Aec.Tests;

public sealed class AecInstructionBlockTests
{
    private const string CodexLatestLf = """
        <!-- AEC:BEGIN version=1 -->
        ## AI Environment as Code

        Treat the AEC data repository's Git commit history as the source of truth.
        Preserve instructions outside this managed block.
        Use `aec status` to inspect drift and `aec backup` to record approved runtime changes.
        <!-- AEC:END -->
        """;

    private const string ChatGptLatestLf = """
        <!-- AEC:BEGIN version=2 -->
        ## AI Environment as Code

        Treat the AEC data repository's Git commit history as the source of truth.
        Preserve instructions outside this managed block.
        Use `aec status` to inspect drift and `aec backup` to record approved runtime changes.

        Manual ChatGPT instruction backups live under `environment/providers/chatgpt/`.
        If you detect uncommitted changes there, say that a manual backup is pending and ask before running AEC validation, exact-path staging, commit, and push.
        Never automatically capture from or deploy to ChatGPT, and never claim account-side runtime verification.
        <!-- AEC:END -->
        """;

    [Fact]
    public void PrependsTheCurrentBlockAndOneBlankLine()
    {
        var original = Utf8("# Existing\n\nKeep this.\n");

        var merged = AecInstructionBlock.Merge(original);

        Assert.Equal(Utf8($"{CodexLatestLf}\n\n# Existing\n\nKeep this.\n"), merged);
    }

    [Fact]
    public void UsesTheExistingCrLfStyleForTheManagedBlock()
    {
        var original = Utf8("# Existing\r\nKeep this.\r\n");

        var merged = AecInstructionBlock.Merge(original);

        var expectedBlock = CodexLatestLf.ReplaceLineEndings("\r\n");
        Assert.Equal(Utf8($"{expectedBlock}\r\n\r\n# Existing\r\nKeep this.\r\n"), merged);
    }

    [Fact]
    public void PreservesUtf8BomAtByteZero()
    {
        var original = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Utf8("Existing\n")).ToArray();

        var merged = AecInstructionBlock.Merge(original);

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, merged[..3]);
        Assert.Equal(Utf8($"{CodexLatestLf}\n\nExisting\n"), merged[3..]);
    }

    [Fact]
    public void CurrentVersionIsPreservedByteForByte()
    {
        var original = Utf8(
            "prefix\n<!-- AEC:BEGIN version=1 -->\ncustom current body\n<!-- AEC:END -->\nsuffix\n");

        var merged = AecInstructionBlock.Merge(original);

        Assert.Equal(original, merged);
    }

    [Theory]
    [InlineData(0)]
    public void OlderVersionIsReplacedInPlaceWithoutChangingOtherBytes(int version)
    {
        var original = Utf8(
            $"prefix\r\n<!-- AEC:BEGIN version={version} -->\r\nold\r\n<!-- AEC:END -->\r\nsuffix\r\n");

        var merged = AecInstructionBlock.Merge(original);

        var expectedBlock = CodexLatestLf.ReplaceLineEndings("\r\n");
        Assert.Equal(Utf8($"prefix\r\n{expectedBlock}\r\nsuffix\r\n"), merged);
    }

    [Fact]
    public void ChatGptProviderUpgradesTheCodexBlockToVersionTwo()
    {
        var original = Utf8(
            "prefix\n<!-- AEC:BEGIN version=1 -->\ncustom body\n<!-- AEC:END -->\nsuffix\n");

        var merged = AecInstructionBlock.MergeForChatGptProvider(original);

        Assert.Equal(Utf8($"prefix\n{ChatGptLatestLf}\nsuffix\n"), merged);
    }

    [Fact]
    public void ChatGptProviderPreservesItsCurrentVersionByteForByte()
    {
        var original = Utf8(
            "prefix\n<!-- AEC:BEGIN version=2 -->\ncustom current body\n<!-- AEC:END -->\nsuffix\n");

        var merged = AecInstructionBlock.MergeForChatGptProvider(original);

        Assert.Equal(original, merged);
    }

    [Theory]
    [InlineData("<!-- AEC:BEGIN version=1 -->\nmissing end\n")]
    [InlineData("<!-- AEC:END -->\nmissing begin\n")]
    [InlineData(" <!-- AEC:BEGIN version=1 -->\nbody\n<!-- AEC:END -->\n")]
    [InlineData("<!-- AEC:BEGIN version=1 --> extra\nbody\n<!-- AEC:END -->\n")]
    [InlineData("<!-- AEC:BEGIN version=1 -->\nbody\n<!-- AEC:END --> extra\n")]
    [InlineData("<!-- AEC:END -->\n<!-- AEC:BEGIN version=1 -->\n")]
    [InlineData("<!-- AEC:BEGIN version=1 -->\n<!-- AEC:BEGIN version=1 -->\n<!-- AEC:END -->\n")]
    public void RejectsMalformedOrAmbiguousMarkers(string content)
    {
        Assert.Throws<InvalidDataException>(() => AecInstructionBlock.Merge(Utf8(content)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("01")]
    [InlineData("-1")]
    [InlineData(" 1")]
    public void RejectsInvalidVersions(string version)
    {
        var content = Utf8(
            $"<!-- AEC:BEGIN version={version} -->\nbody\n<!-- AEC:END -->\n");

        Assert.Throws<InvalidDataException>(() => AecInstructionBlock.Merge(content));
    }

    [Fact]
    public void RejectsNewerVersions()
    {
        var content = Utf8(
            "<!-- AEC:BEGIN version=999999999999999999999 -->\nfuture\n<!-- AEC:END -->\n");

        var exception = Assert.Throws<InvalidDataException>(() => AecInstructionBlock.Merge(content));

        Assert.Contains("newer unsupported", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(InvalidText))]
    public void RejectsInvalidInstructionText(byte[] content)
    {
        Assert.Throws<InvalidDataException>(() => AecInstructionBlock.Merge(content));
    }

    [Fact]
    public void RejectsMergedContentOverOneMiB()
    {
        var content = Enumerable.Repeat((byte)'a', AecApplication.MaximumTextBytes).ToArray();

        var exception = Assert.Throws<InvalidDataException>(() => AecInstructionBlock.Merge(content));

        Assert.Contains("exceed 1 MiB", exception.Message, StringComparison.Ordinal);
    }

    public static TheoryData<byte[]> InvalidText => new()
    {
        new byte[] { 0xC3, 0x28 },
        new byte[] { (byte)'a', 0, (byte)'b' }
    };

    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);
}
