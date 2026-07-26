using System.Text;

namespace Aec.Tests;

public sealed class AecInstructionBlockTests
{
    private static readonly string Repository = Path.Combine(
        Path.GetTempPath(),
        "aec instruction block tests",
        "data repository");

    private static string CodexLatestLf => $"""
        <!-- AEC:BEGIN version=3 -->
        ## AI Environment as Code

        The AEC data repository selected by `--repo` is `{Repository}`.
        Treat that repository's Git commit history as the source of truth.
        Preserve instructions outside this managed block.
        Use `aec status` to inspect drift and `aec backup` to record approved runtime changes.
        <!-- AEC:END -->
        """;

    private static string ChatGptLatestLf => $"""
        <!-- AEC:BEGIN version=4 -->
        ## AI Environment as Code

        The AEC data repository selected by `--repo` is `{Repository}`.
        Treat that repository's Git commit history as the source of truth.
        Preserve instructions outside this managed block.
        Use `aec status` to inspect drift and `aec backup` to record approved runtime changes.

        Manual ChatGPT instruction backups live under `{Path.Combine(Repository, "environment", "providers", "chatgpt")}{Path.DirectorySeparatorChar}`.
        If you detect uncommitted changes there, say that a manual backup is pending and ask before running AEC validation, exact-path staging, commit, and push.
        Never automatically capture from or deploy to ChatGPT, and never claim account-side runtime verification.
        <!-- AEC:END -->
        """;

    [Fact]
    public void PrependsTheCurrentBlockAndOneBlankLine()
    {
        var original = Utf8("# Existing\n\nKeep this.\n");

        var merged = AecInstructionBlock.Merge(original, Repository);

        Assert.Equal(Utf8($"{CodexLatestLf}\n\n# Existing\n\nKeep this.\n"), merged);
    }

    [Fact]
    public void UsesTheExistingCrLfStyleForTheManagedBlock()
    {
        var original = Utf8("# Existing\r\nKeep this.\r\n");

        var merged = AecInstructionBlock.Merge(original, Repository);

        var expectedBlock = CodexLatestLf.ReplaceLineEndings("\r\n");
        Assert.Equal(Utf8($"{expectedBlock}\r\n\r\n# Existing\r\nKeep this.\r\n"), merged);
    }

    [Fact]
    public void PreservesUtf8BomAtByteZero()
    {
        var original = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Utf8("Existing\n")).ToArray();

        var merged = AecInstructionBlock.Merge(original, Repository);

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, merged[..3]);
        Assert.Equal(Utf8($"{CodexLatestLf}\n\nExisting\n"), merged[3..]);
    }

    [Fact]
    public void ExactCurrentBlockIsPreservedByteForByte()
    {
        var original = Utf8(
            $"prefix\n{CodexLatestLf}\nsuffix\n");

        var merged = AecInstructionBlock.Merge(original, Repository);

        Assert.Equal(original, merged);
    }

    [Fact]
    public void CurrentVersionWithAnotherRepositoryIsReplaced()
    {
        var otherRepository = Path.Combine(Path.GetTempPath(), "other data repository");
        var original = Utf8(
            $"prefix\n{CodexLatestLf.Replace(Repository, otherRepository, StringComparison.Ordinal)}\nsuffix\n");

        var merged = AecInstructionBlock.Merge(original, Repository);

        Assert.Equal(Utf8($"prefix\n{CodexLatestLf}\nsuffix\n"), merged);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void OlderVersionIsReplacedInPlaceWithoutChangingOtherBytes(int version)
    {
        var original = Utf8(
            $"prefix\r\n<!-- AEC:BEGIN version={version} -->\r\nold\r\n<!-- AEC:END -->\r\nsuffix\r\n");

        var merged = AecInstructionBlock.Merge(original, Repository);

        var expectedBlock = CodexLatestLf.ReplaceLineEndings("\r\n");
        Assert.Equal(Utf8($"prefix\r\n{expectedBlock}\r\nsuffix\r\n"), merged);
    }

    [Fact]
    public void ChatGptProviderUpgradesTheCodexBlockToVersionFour()
    {
        var original = Utf8(
            "prefix\n<!-- AEC:BEGIN version=3 -->\ncustom body\n<!-- AEC:END -->\nsuffix\n");

        var merged = AecInstructionBlock.MergeForChatGptProvider(original, Repository);

        Assert.Equal(Utf8($"prefix\n{ChatGptLatestLf}\nsuffix\n"), merged);
    }

    [Fact]
    public void ChatGptProviderPreservesItsExactCurrentBlockByteForByte()
    {
        var original = Utf8(
            $"prefix\n{ChatGptLatestLf}\nsuffix\n");

        var merged = AecInstructionBlock.MergeForChatGptProvider(original, Repository);

        Assert.Equal(original, merged);
    }

    [Fact]
    public void ChatGptProviderReconcilesItsCurrentVersionAfterRepositoryMove()
    {
        var oldRepository = Path.Combine(Path.GetTempPath(), "old data repository");
        var original = Utf8(
            $"prefix\n{ChatGptLatestLf.Replace(Repository, oldRepository, StringComparison.Ordinal)}\nsuffix\n");

        var merged = AecInstructionBlock.MergeForChatGptProvider(original, Repository);

        Assert.Equal(Utf8($"prefix\n{ChatGptLatestLf}\nsuffix\n"), merged);
    }

    [Theory]
    [InlineData("`")]
    [InlineData("\n")]
    [InlineData("\r")]
    public void RejectsRepositoryPathsThatCannotBeSafelyEmbedded(string unsafeCharacter)
    {
        var repository = Repository + unsafeCharacter + "suffix";

        var exception = Assert.Throws<ArgumentException>(() =>
            AecInstructionBlock.Merge([], repository));

        Assert.Contains("cannot be embedded", exception.Message, StringComparison.Ordinal);
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
        Assert.Throws<InvalidDataException>(() =>
            AecInstructionBlock.Merge(Utf8(content), Repository));
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

        Assert.Throws<InvalidDataException>(() =>
            AecInstructionBlock.Merge(content, Repository));
    }

    [Fact]
    public void RejectsNewerVersions()
    {
        var content = Utf8(
            "<!-- AEC:BEGIN version=999999999999999999999 -->\nfuture\n<!-- AEC:END -->\n");

        var exception = Assert.Throws<InvalidDataException>(() =>
            AecInstructionBlock.Merge(content, Repository));

        Assert.Contains("newer unsupported", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(InvalidText))]
    public void RejectsInvalidInstructionText(byte[] content)
    {
        Assert.Throws<InvalidDataException>(() =>
            AecInstructionBlock.Merge(content, Repository));
    }

    [Fact]
    public void RejectsMergedContentOverOneMiB()
    {
        var content = Enumerable.Repeat((byte)'a', AecApplication.MaximumTextBytes).ToArray();

        var exception = Assert.Throws<InvalidDataException>(() =>
            AecInstructionBlock.Merge(content, Repository));

        Assert.Contains("exceed 1 MiB", exception.Message, StringComparison.Ordinal);
    }

    public static TheoryData<byte[]> InvalidText => new()
    {
        new byte[] { 0xC3, 0x28 },
        new byte[] { (byte)'a', 0, (byte)'b' }
    };

    private static byte[] Utf8(string value) => Encoding.UTF8.GetBytes(value);
}
