using System.Text;

namespace Aec;

internal static class AecInstructionBlock
{
    private const string BeginStemText = "<!-- AEC:BEGIN";
    private const string BeginPrefixText = "<!-- AEC:BEGIN version=";
    private const string MarkerSuffixText = " -->";
    private const string EndMarkerText = "<!-- AEC:END -->";

    private static readonly byte[] BeginStem = Encoding.ASCII.GetBytes(BeginStemText);
    private static readonly byte[] BeginPrefix = Encoding.ASCII.GetBytes(BeginPrefixText);
    private static readonly byte[] MarkerSuffix = Encoding.ASCII.GetBytes(MarkerSuffixText);
    private static readonly byte[] EndStem = Encoding.ASCII.GetBytes("<!-- AEC:END");
    private static readonly byte[] EndMarker = Encoding.ASCII.GetBytes(EndMarkerText);
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static byte[] Merge(byte[] content, string repository) =>
        Merge(content, CodexBlockLines(NormalizeRepository(repository)), "3"u8);

    public static byte[] MergeForChatGptProvider(byte[] content, string repository)
    {
        var normalizedRepository = NormalizeRepository(repository);
        return Merge(content, ChatGptBlockLines(normalizedRepository), "4"u8);
    }

    private static string[] CodexBlockLines(string repository) =>
    [
        "<!-- AEC:BEGIN version=3 -->",
        "## AI Environment as Code",
        string.Empty,
        $"The AEC data repository selected by `--repo` is `{repository}`.",
        "Treat that repository's Git commit history as the source of truth.",
        "Preserve instructions outside this managed block.",
        "Use `aec status` to inspect drift and `aec backup` to record approved runtime changes.",
        EndMarkerText
    ];

    private static string[] ChatGptBlockLines(string repository)
    {
        var providerDirectory = Path.Combine(
            repository,
            "environment",
            "providers",
            "chatgpt");
        if (!Path.EndsInDirectorySeparator(providerDirectory))
        {
            providerDirectory += Path.DirectorySeparatorChar;
        }

        return
        [
            "<!-- AEC:BEGIN version=4 -->",
            "## AI Environment as Code",
            string.Empty,
            $"The AEC data repository selected by `--repo` is `{repository}`.",
            "Treat that repository's Git commit history as the source of truth.",
            "Preserve instructions outside this managed block.",
            "Use `aec status` to inspect drift and `aec backup` to record approved runtime changes.",
            string.Empty,
            $"Manual ChatGPT instruction backups live under `{providerDirectory}`.",
            "If you detect uncommitted changes there, say that a manual backup is pending and ask before running AEC validation, exact-path staging, commit, and push.",
            "Never automatically capture from or deploy to ChatGPT, and never claim account-side runtime verification.",
            EndMarkerText
        ];
    }

    private static string NormalizeRepository(string repository)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        // The path is embedded in a Markdown code span inside a line-oriented managed block.
        if (repository.Contains('`') || repository.Contains('\r') || repository.Contains('\n'))
        {
            throw new ArgumentException(
                "Repository path contains characters that cannot be embedded in managed instructions.",
                nameof(repository));
        }

        if (!Path.IsPathFullyQualified(repository))
        {
            throw new ArgumentException("Repository must be an absolute path.", nameof(repository));
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(repository));
    }

    private static byte[] Merge(
        byte[] content,
        string[] currentBlockLines,
        ReadOnlySpan<byte> currentVersion)
    {
        ArgumentNullException.ThrowIfNull(content);
        ValidateText(content);

        var bodyOffset = HasUtf8Bom(content) ? 3 : 0;
        var begin = FindOccurrences(content, BeginStem, bodyOffset);
        var end = FindOccurrences(content, EndStem, bodyOffset);

        if (begin.Count == 0 && end.Count == 0)
        {
            return Prepend(content, bodyOffset, currentBlockLines);
        }

        if (begin.Count != 1 || end.Count != 1)
        {
            throw new InvalidDataException(
                "Instructions contain malformed or duplicate AEC block markers.");
        }

        var beginLine = ReadMarkerLine(content, begin.FirstIndex, bodyOffset, "AEC begin marker");
        var endLine = ReadMarkerLine(content, end.FirstIndex, bodyOffset, "AEC end marker");

        if (!endLine.Content.SequenceEqual(EndMarker))
        {
            throw new InvalidDataException("Instructions contain a malformed AEC end marker.");
        }

        if (end.FirstIndex <= begin.FirstIndex || end.FirstIndex < beginLine.ContentEnd)
        {
            throw new InvalidDataException("Instructions contain reversed AEC block markers.");
        }

        var version = ReadVersion(beginLine.Content, currentVersion);
        if (version == VersionKind.Future)
        {
            throw new InvalidDataException(
                "Instructions contain a newer unsupported AEC block version.");
        }

        var newLine = beginLine.NewLine ?? DetectNewLine(content.AsSpan(bodyOffset));
        var block = RenderCurrentBlock(newLine, currentBlockLines);
        var suffixOffset = end.FirstIndex + EndMarker.Length;
        if (version == VersionKind.Current &&
            content.AsSpan(begin.FirstIndex, suffixOffset - begin.FirstIndex).SequenceEqual(block))
        {
            return content.ToArray();
        }

        var appendFinalNewLine = suffixOffset == content.Length;
        var resultLength = checked(
            begin.FirstIndex +
            block.Length +
            (appendFinalNewLine ? Encoding.ASCII.GetByteCount(newLine) : 0) +
            (content.Length - suffixOffset));
        EnsureAllowedLength(resultLength);

        var result = new byte[resultLength];
        var destination = 0;
        content.AsSpan(0, begin.FirstIndex).CopyTo(result.AsSpan(destination));
        destination += begin.FirstIndex;
        block.CopyTo(result.AsSpan(destination));
        destination += block.Length;

        if (appendFinalNewLine)
        {
            destination += Encoding.ASCII.GetBytes(newLine, result.AsSpan(destination));
        }

        content.AsSpan(suffixOffset).CopyTo(result.AsSpan(destination));
        return result;
    }

    private static byte[] Prepend(
        byte[] content,
        int bodyOffset,
        string[] currentBlockLines)
    {
        var newLine = DetectNewLine(content.AsSpan(bodyOffset));
        var newLineBytes = Encoding.ASCII.GetBytes(newLine);
        var block = RenderCurrentBlock(newLine, currentBlockLines);
        var bodyLength = content.Length - bodyOffset;
        var separatorLength = bodyLength == 0 ? newLineBytes.Length : newLineBytes.Length * 2;
        var resultLength = checked(bodyOffset + block.Length + separatorLength + bodyLength);
        EnsureAllowedLength(resultLength);

        var result = new byte[resultLength];
        content.AsSpan(0, bodyOffset).CopyTo(result);
        var destination = bodyOffset;
        block.CopyTo(result.AsSpan(destination));
        destination += block.Length;
        newLineBytes.CopyTo(result.AsSpan(destination));
        destination += newLineBytes.Length;

        if (bodyLength != 0)
        {
            newLineBytes.CopyTo(result.AsSpan(destination));
            destination += newLineBytes.Length;
            content.AsSpan(bodyOffset).CopyTo(result.AsSpan(destination));
        }

        return result;
    }

    private static MarkerLine ReadMarkerLine(
        byte[] content,
        int start,
        int bodyOffset,
        string label)
    {
        if (start != bodyOffset && content[start - 1] != (byte)'\n')
        {
            throw new InvalidDataException($"{label} must start at the beginning of a line.");
        }

        var lineFeedOffset = content.AsSpan(start).IndexOf((byte)'\n');
        var lineEnd = lineFeedOffset < 0 ? content.Length : start + lineFeedOffset;
        var contentEnd = lineEnd > start && content[lineEnd - 1] == (byte)'\r'
            ? lineEnd - 1
            : lineEnd;
        var newLine = lineFeedOffset < 0
            ? null
            : contentEnd < lineEnd
                ? "\r\n"
                : "\n";

        return new MarkerLine(content.AsSpan(start, contentEnd - start), contentEnd, newLine);
    }

    private static VersionKind ReadVersion(
        ReadOnlySpan<byte> beginLine,
        ReadOnlySpan<byte> currentVersion)
    {
        if (!beginLine.StartsWith(BeginPrefix) || !beginLine.EndsWith(MarkerSuffix))
        {
            throw new InvalidDataException("Instructions contain a malformed AEC begin marker.");
        }

        var version = beginLine[BeginPrefix.Length..^MarkerSuffix.Length];
        if (version.Length == 0 ||
            (version.Length > 1 && version[0] == (byte)'0') ||
            !ContainsOnlyAsciiDigits(version))
        {
            throw new InvalidDataException("Instructions contain an invalid AEC block version.");
        }

        // Decimal length is compared first so very large future versions never need parsing.
        if (version.Length != currentVersion.Length)
        {
            return version.Length < currentVersion.Length
                ? VersionKind.Older
                : VersionKind.Future;
        }

        var comparison = version.SequenceCompareTo(currentVersion);
        return comparison switch
        {
            < 0 => VersionKind.Older,
            0 => VersionKind.Current,
            _ => VersionKind.Future
        };
    }

    private static void ValidateText(byte[] content)
    {
        if (content.AsSpan().IndexOf((byte)0) >= 0)
        {
            throw new InvalidDataException("Instructions contain a NUL byte.");
        }

        try
        {
            _ = StrictUtf8.GetCharCount(content);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException("Instructions are not valid UTF-8.", exception);
        }
    }

    private static byte[] RenderCurrentBlock(string newLine, string[] currentBlockLines)
    {
        return Encoding.UTF8.GetBytes(string.Join(newLine, currentBlockLines));
    }

    private static bool ContainsOnlyAsciiDigits(ReadOnlySpan<byte> value)
    {
        foreach (var character in value)
        {
            if (character is < (byte)'0' or > (byte)'9')
            {
                return false;
            }
        }

        return true;
    }

    private static string DetectNewLine(ReadOnlySpan<byte> content)
    {
        var lineFeed = content.IndexOf((byte)'\n');
        return lineFeed > 0 && content[lineFeed - 1] == (byte)'\r' ? "\r\n" : "\n";
    }

    private static Occurrences FindOccurrences(
        ReadOnlySpan<byte> content,
        ReadOnlySpan<byte> marker,
        int offset)
    {
        var count = 0;
        var firstIndex = -1;

        while (offset <= content.Length - marker.Length)
        {
            var relativeIndex = content[offset..].IndexOf(marker);
            if (relativeIndex < 0)
            {
                break;
            }

            var index = offset + relativeIndex;
            firstIndex = count == 0 ? index : firstIndex;
            count++;
            offset = index + marker.Length;
        }

        return new Occurrences(count, firstIndex);
    }

    private static bool HasUtf8Bom(ReadOnlySpan<byte> content)
    {
        return content.StartsWith(Utf8Bom);
    }

    private static void EnsureAllowedLength(int length)
    {
        if (length > AecApplication.MaximumTextBytes)
        {
            throw new InvalidDataException("Merged instructions exceed 1 MiB.");
        }
    }

    private enum VersionKind
    {
        Older,
        Current,
        Future
    }

    private readonly record struct Occurrences(int Count, int FirstIndex);

    private readonly ref struct MarkerLine(
        ReadOnlySpan<byte> content,
        int contentEnd,
        string? newLine)
    {
        public ReadOnlySpan<byte> Content { get; } = content;

        public int ContentEnd { get; } = contentEnd;

        public string? NewLine { get; } = newLine;
    }
}
