using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Aec;

internal static class AecInstructionBlock
{
    // Each adjacent pair is the same format with or without manual ChatGPT guidance.
    // These are managed-block format versions, not AEC CLI release versions.
    private const int LegacyStandardVersion = 3;
    private const int LegacyChatGptVersion = 4;
    private const int CanonicalStandardVersion = 5;
    private const int CanonicalChatGptVersion = 6;
    private const int PortableStandardVersion = 7;
    private const int PortableChatGptVersion = 8;
    private const int StampedPortableStandardVersion = 9;
    private const int StampedPortableChatGptVersion = 10;

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

    private static byte[] VersionBytes(int version) =>
        Encoding.ASCII.GetBytes(version.ToString(CultureInfo.InvariantCulture));

    private static string BeginMarker(int version) =>
        BeginPrefixText + version.ToString(CultureInfo.InvariantCulture) + MarkerSuffixText;

    public static byte[] Merge(byte[] content, string repository) =>
        Merge(
            content,
            CodexBlockLines(NormalizeRepository(repository)),
            VersionBytes(CanonicalStandardVersion),
            protectedOlderVersion: VersionBytes(LegacyChatGptVersion));

    public static byte[] MergeForChatGptProvider(byte[] content, string repository)
    {
        var normalizedRepository = NormalizeRepository(repository);
        return Merge(content, ChatGptBlockLines(normalizedRepository),
            VersionBytes(CanonicalChatGptVersion));
    }

    // 2.0 previews use new markers while ordinary init/apply retain their 1.x blocks.
    internal static byte[] MergeForPortableTarget(
        byte[] content,
        string repository,
        string platform,
        bool includeChatGptGuidance)
    {
        var normalizedRepository = NormalizeRepository(repository);
        platform = ValidatePlatform(platform);
        // Hash the authored target, not a pre-existing local AEC control block.
        var targetDigest = Convert.ToHexStringLower(SHA256.HashData(Remove(content).Content));
        return includeChatGptGuidance
            ? Merge(content, PortableChatGptBlockLines(normalizedRepository, platform,
                targetDigest), VersionBytes(StampedPortableChatGptVersion))
            : Merge(content, PortableCodexBlockLines(normalizedRepository, platform,
                targetDigest), VersionBytes(StampedPortableStandardVersion));
    }

    internal static RepositoryBinding? ReadRepositoryBinding(
        byte[] content,
        bool allowLegacyProviderUpgrade = false)
    {
        return ReadInitializedBlock(content, allowLegacyProviderUpgrade)?.Binding;
    }

    internal static bool IncludesChatGptGuidance(int? version) =>
        version is LegacyChatGptVersion or CanonicalChatGptVersion or
            PortableChatGptVersion or StampedPortableChatGptVersion;

    internal static RemovalResult Remove(byte[] content)
    {
        var block = ReadInitializedBlock(content, allowLegacyProviderUpgrade: false);
        if (block is null)
        {
            return new RemovalResult(content, Removed: false);
        }

        var bodyOffset = HasUtf8Bom(content) ? Utf8Bom.Length : 0;
        var suffixOffset = block.End;
        var newLine = Encoding.ASCII.GetBytes(block.NewLine);

        // Merge owns the separator after its block. At the top it inserted up to
        // two line endings; in-place blocks own the one ending their final marker.
        if (block.Start == bodyOffset &&
            content.AsSpan(suffixOffset).StartsWith(newLine) &&
            content.AsSpan(suffixOffset + newLine.Length).StartsWith(newLine))
        {
            suffixOffset += newLine.Length * 2;
        }
        else if (content.AsSpan(suffixOffset).StartsWith(newLine))
        {
            suffixOffset += newLine.Length;
        }

        var result = new byte[block.Start + content.Length - suffixOffset];
        content.AsSpan(0, block.Start).CopyTo(result);
        content.AsSpan(suffixOffset).CopyTo(result.AsSpan(block.Start));
        return new RemovalResult(result, Removed: true);
    }

    private static InitializedBlock? ReadInitializedBlock(
        byte[] content,
        bool allowLegacyProviderUpgrade)
    {
        ArgumentNullException.ThrowIfNull(content);
        ValidateText(content);

        var bodyOffset = HasUtf8Bom(content) ? Utf8Bom.Length : 0;
        var begin = FindOccurrences(content, BeginStem, bodyOffset);
        var end = FindOccurrences(content, EndStem, bodyOffset);
        if (begin.Count == 0 && end.Count == 0)
        {
            return null;
        }

        if (begin.Count != 1 || end.Count != 1)
        {
            throw UnsupportedInitializedBlock();
        }

        MarkerLine beginLine;
        MarkerLine endLine;
        try
        {
            beginLine = ReadMarkerLine(content, begin.FirstIndex, bodyOffset, "AEC begin marker");
            endLine = ReadMarkerLine(content, end.FirstIndex, bodyOffset, "AEC end marker");
        }
        catch (InvalidDataException exception)
        {
            throw UnsupportedInitializedBlock(exception);
        }

        if (!endLine.Content.SequenceEqual(EndMarker) ||
            end.FirstIndex <= begin.FirstIndex ||
            end.FirstIndex < beginLine.ContentEnd)
        {
            throw UnsupportedInitializedBlock();
        }

        var version = ReadSupportedInitializedVersion(
            beginLine.Content,
            allowLegacyProviderUpgrade);
        if (version is null)
        {
            return null;
        }

        var newLine = beginLine.NewLine ?? throw UnsupportedInitializedBlock();
        var suffixOffset = end.FirstIndex + EndMarker.Length;
        var blockText = StrictUtf8.GetString(
            content,
            begin.FirstIndex,
            suffixOffset - begin.FirstIndex);
        var lines = blockText.Split(newLine, StringSplitOptions.None);
        var expectedLineCount = version switch
        {
            LegacyStandardVersion => 8,
            LegacyChatGptVersion => 12,
            CanonicalStandardVersion => 14,
            CanonicalChatGptVersion => 18,
            PortableStandardVersion => 16,
            PortableChatGptVersion => 20,
            StampedPortableStandardVersion => 17,
            StampedPortableChatGptVersion => 21,
            _ => throw UnsupportedInitializedBlock()
        };
        if (lines.Length != expectedLineCount)
        {
            throw UnsupportedInitializedBlock();
        }

        const string repositoryPrefix = "The AEC data repository selected by `--repo` is `";
        const string repositorySuffix = "`.";
        var repositoryLine = lines[version <= LegacyChatGptVersion ? 3 : 5];
        if (!repositoryLine.StartsWith(repositoryPrefix, StringComparison.Ordinal) ||
            !repositoryLine.EndsWith(repositorySuffix, StringComparison.Ordinal))
        {
            throw UnsupportedInitializedBlock();
        }

        var repository = repositoryLine[
            repositoryPrefix.Length..^repositorySuffix.Length];
        string normalizedRepository;
        try
        {
            normalizedRepository = NormalizeRepository(repository);
        }
        catch (ArgumentException exception)
        {
            throw UnsupportedInitializedBlock(exception);
        }

        string? platform = null;
        if (version is PortableStandardVersion or PortableChatGptVersion or
            StampedPortableStandardVersion or StampedPortableChatGptVersion)
        {
            const string platformPrefix = "The selected AEC platform is `";
            var platformLine = lines[7];
            if (!platformLine.StartsWith(platformPrefix, StringComparison.Ordinal) ||
                !platformLine.EndsWith(repositorySuffix, StringComparison.Ordinal))
            {
                throw UnsupportedInitializedBlock();
            }

            try
            {
                platform = ValidatePlatform(
                    platformLine[platformPrefix.Length..^repositorySuffix.Length]);
            }
            catch (ArgumentException exception)
            {
                throw UnsupportedInitializedBlock(exception);
            }
        }

        string? targetDigest = null;
        if (version is StampedPortableStandardVersion or StampedPortableChatGptVersion)
        {
            const string digestPrefix = "The last-applied target SHA-256 is `";
            var digestLine = lines[9];
            if (!digestLine.StartsWith(digestPrefix, StringComparison.Ordinal) ||
                !digestLine.EndsWith(repositorySuffix, StringComparison.Ordinal))
            {
                throw UnsupportedInitializedBlock();
            }

            targetDigest = digestLine[digestPrefix.Length..^repositorySuffix.Length];
            if (!IsLowerHexSha256(targetDigest))
            {
                throw UnsupportedInitializedBlock();
            }
        }

        // Re-rendering makes recognition strict: only blocks emitted by a supported AEC
        // version can authorize applying or rebinding a pulled repository.
        var expectedLines = version switch
        {
            LegacyStandardVersion => LegacyCodexBlockLines(normalizedRepository),
            LegacyChatGptVersion => LegacyChatGptBlockLines(normalizedRepository),
            CanonicalStandardVersion => CodexBlockLines(normalizedRepository),
            CanonicalChatGptVersion => ChatGptBlockLines(normalizedRepository),
            PortableStandardVersion => PortableCodexBlockLines(normalizedRepository, platform!),
            PortableChatGptVersion => PortableChatGptBlockLines(normalizedRepository, platform!),
            StampedPortableStandardVersion => PortableCodexBlockLines(
                normalizedRepository, platform!, targetDigest),
            StampedPortableChatGptVersion => PortableChatGptBlockLines(
                normalizedRepository, platform!, targetDigest),
            _ => throw UnsupportedInitializedBlock()
        };
        var expectedBlock = RenderCurrentBlock(newLine, expectedLines);
        if (!content.AsSpan(begin.FirstIndex, suffixOffset - begin.FirstIndex)
                .SequenceEqual(expectedBlock))
        {
            throw UnsupportedInitializedBlock();
        }

        return new InitializedBlock(
            new RepositoryBinding(version.Value, normalizedRepository, platform, targetDigest),
            begin.FirstIndex,
            suffixOffset,
            newLine);
    }

    internal static byte[] RebindRepository(byte[] content, string repository)
    {
        var binding = ReadRepositoryBinding(content)
            ?? throw UnsupportedInitializedBlock();
        var normalizedRepository = NormalizeRepository(repository);

        return binding.Version switch
        {
            LegacyStandardVersion or CanonicalStandardVersion => Merge(
                content, CodexBlockLines(normalizedRepository),
                VersionBytes(CanonicalStandardVersion)),
            LegacyChatGptVersion or CanonicalChatGptVersion => Merge(
                content, ChatGptBlockLines(normalizedRepository),
                VersionBytes(CanonicalChatGptVersion)),
            PortableStandardVersion => Merge(
                content, PortableCodexBlockLines(normalizedRepository, binding.Platform!),
                VersionBytes(PortableStandardVersion)),
            PortableChatGptVersion => Merge(
                content, PortableChatGptBlockLines(normalizedRepository, binding.Platform!),
                VersionBytes(PortableChatGptVersion)),
            StampedPortableStandardVersion => Merge(
                content, PortableCodexBlockLines(normalizedRepository, binding.Platform!,
                    binding.TargetDigest), VersionBytes(StampedPortableStandardVersion)),
            StampedPortableChatGptVersion => Merge(
                content, PortableChatGptBlockLines(normalizedRepository, binding.Platform!,
                    binding.TargetDigest), VersionBytes(StampedPortableChatGptVersion)),
            _ => throw UnsupportedInitializedBlock()
        };
    }

    internal static byte[] MergeForVersion(byte[] content, string repository, int version)
    {
        var normalizedRepository = NormalizeRepository(repository);
        return version switch
        {
            LegacyStandardVersion => Merge(content, LegacyCodexBlockLines(normalizedRepository),
                VersionBytes(LegacyStandardVersion)),
            LegacyChatGptVersion => Merge(content, LegacyChatGptBlockLines(normalizedRepository),
                VersionBytes(LegacyChatGptVersion)),
            CanonicalStandardVersion => Merge(content, CodexBlockLines(normalizedRepository),
                VersionBytes(CanonicalStandardVersion)),
            CanonicalChatGptVersion => Merge(content, ChatGptBlockLines(normalizedRepository),
                VersionBytes(CanonicalChatGptVersion)),
            _ => throw UnsupportedInitializedBlock()
        };
    }

    internal static bool RepositoryPathsEqual(string left, string right)
    {
        // macOS may use a case-sensitive APFS volume, so only Windows can safely
        // treat differently-cased absolute paths as the same binding.
        var comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(
            NormalizeRepository(left),
            NormalizeRepository(right),
            comparison);
    }

    private static string[] CodexBlockLines(string normalizedRepository)
    {
        var repository = normalizedRepository;
        var canonicalInstructions = Path.Combine(
            repository,
            "environment",
            "providers",
            "codex",
            "AGENTS.md");

        return
        [
            BeginMarker(CanonicalStandardVersion),
            "## AI Environment as Code",
            string.Empty,
            "Use the `$aec` skill for changes to managed personal Codex instructions or configuration.",
            string.Empty,
            $"The AEC data repository selected by `--repo` is `{repository}`.",
            "Treat its Git commit history as the source of truth.",
            $"The canonical Codex instructions are `{canonicalInstructions}`.",
            "Preserve instructions outside this managed block; do not edit the runtime `AGENTS.md` as the source of truth.",
            string.Empty,
            "For repository-to-runtime changes, edit and commit the canonical source, run `aec apply`, then verify with `aec status`.",
            "Use `aec backup` only for an explicitly authorized runtime-to-repository capture.",
            "If AEC is unavailable or validation fails, stop without changing managed runtime state.",
            EndMarkerText
        ];
    }

    private static string[] LegacyCodexBlockLines(string normalizedRepository) =>
    [
        BeginMarker(LegacyStandardVersion),
        "## AI Environment as Code",
        string.Empty,
        $"The AEC data repository selected by `--repo` is `{normalizedRepository}`.",
        "Treat that repository's Git commit history as the source of truth.",
        "Preserve instructions outside this managed block.",
        "Use `aec status` to inspect drift and `aec backup` to record approved runtime changes.",
        EndMarkerText
    ];

    private static string[] ChatGptBlockLines(string normalizedRepository)
    {
        var repository = normalizedRepository;
        return
        [
            BeginMarker(CanonicalChatGptVersion),
            .. CodexBlockLines(repository)[1..^1],
            string.Empty,
            .. ChatGptGuidanceLines(repository),
            EndMarkerText
        ];
    }

    private static string[] PortableCodexBlockLines(
        string repository,
        string platform,
        string? targetDigest = null)
    {
        var target = Path.Combine(repository, "environment", "targets", platform,
            "codex", "AGENTS.md");
        return
        [
            targetDigest is null
                ? BeginMarker(PortableStandardVersion)
                : BeginMarker(StampedPortableStandardVersion),
            "## AI Environment as Code",
            string.Empty,
            "Use the `$aec` skill for changes to managed personal Codex instructions or configuration.",
            string.Empty,
            $"The AEC data repository selected by `--repo` is `{repository}`.",
            "Treat its Git commit history as the source of truth.",
            $"The selected AEC platform is `{platform}`.",
            $"The committed Codex instruction target is `{target}`.",
            .. targetDigest is null
                ? []
                : new[] { $"The last-applied target SHA-256 is `{targetDigest}`." },
            "Edit the authored shared, platform, or Codex overlay source; run `aec render`, review, and commit the generated target. Do not edit the target directly.",
            "Preserve instructions outside this managed block; do not edit the runtime `AGENTS.md` as the source of truth.",
            string.Empty,
            "For repository-to-runtime changes, run `aec apply`, then verify with `aec status`.",
            "Use `aec backup` only for an explicitly authorized runtime-to-repository capture.",
            "If AEC is unavailable or validation fails, stop without changing managed runtime state.",
            EndMarkerText
        ];
    }

    private static string[] PortableChatGptBlockLines(
        string repository,
        string platform,
        string? targetDigest = null) =>
    [
        targetDigest is null
            ? BeginMarker(PortableChatGptVersion)
            : BeginMarker(StampedPortableChatGptVersion),
        .. PortableCodexBlockLines(repository, platform, targetDigest)[1..^1],
        string.Empty,
        .. ChatGptGuidanceLines(repository),
        EndMarkerText
    ];

    private static string[] LegacyChatGptBlockLines(string normalizedRepository)
    {
        var repository = normalizedRepository;
        return
        [
            BeginMarker(LegacyChatGptVersion),
            .. LegacyCodexBlockLines(repository)[1..^1],
            string.Empty,
            .. ChatGptGuidanceLines(repository),
            EndMarkerText
        ];
    }

    private static string[] ChatGptGuidanceLines(string repository)
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
            $"Manual ChatGPT instruction backups live under `{providerDirectory}`.",
            "If you detect uncommitted changes there, say that a manual backup is pending and ask before running AEC validation, exact-path staging, commit, and push.",
            "Never automatically capture from or deploy to ChatGPT, and never claim account-side runtime verification."
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

    private static string ValidatePlatform(string platform) => platform switch
    {
        "macos-arm64" or "windows-x64" or "linux-arm64" => platform,
        _ => throw new ArgumentException($"Unsupported AEC platform: {platform}")
    };

    private static byte[] Merge(
        byte[] content,
        string[] currentBlockLines,
        ReadOnlySpan<byte> currentVersion,
        ReadOnlySpan<byte> protectedOlderVersion = default)
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

        var existingVersion = beginLine.Content[BeginPrefix.Length..^MarkerSuffix.Length];
        if (!protectedOlderVersion.IsEmpty && existingVersion.SequenceEqual(protectedOlderVersion))
        {
            throw new InvalidDataException(
                "Instructions contain a provider-aware AEC block that ordinary initialization must preserve.");
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

    private static int? ReadSupportedInitializedVersion(
        ReadOnlySpan<byte> beginLine,
        bool allowLegacyProviderUpgrade)
    {
        VersionKind relativeToVersionThree;
        try
        {
            relativeToVersionThree = ReadVersion(beginLine,
                VersionBytes(LegacyStandardVersion));
        }
        catch (InvalidDataException exception)
        {
            throw UnsupportedInitializedBlock(exception);
        }

        if (relativeToVersionThree == VersionKind.Older)
        {
            // Provider initialization owns the existing v0-v2 migration to v6.
            return allowLegacyProviderUpgrade ? null : throw UnsupportedInitializedBlock();
        }

        if (relativeToVersionThree == VersionKind.Current)
        {
            return LegacyStandardVersion;
        }

        var version = beginLine[BeginPrefix.Length..^MarkerSuffix.Length];
        if (version.SequenceEqual(VersionBytes(LegacyChatGptVersion)))
            return LegacyChatGptVersion;
        if (version.SequenceEqual(VersionBytes(CanonicalStandardVersion)))
            return CanonicalStandardVersion;
        if (version.SequenceEqual(VersionBytes(CanonicalChatGptVersion)))
            return CanonicalChatGptVersion;
        if (version.SequenceEqual(VersionBytes(PortableStandardVersion)))
            return PortableStandardVersion;
        if (version.SequenceEqual(VersionBytes(PortableChatGptVersion)))
            return PortableChatGptVersion;
        if (version.SequenceEqual(VersionBytes(StampedPortableStandardVersion)))
            return StampedPortableStandardVersion;
        if (version.SequenceEqual(VersionBytes(StampedPortableChatGptVersion)))
            return StampedPortableChatGptVersion;

        throw UnsupportedInitializedBlock();
    }

    private static InvalidDataException UnsupportedInitializedBlock(Exception? inner = null)
    {
        const string message =
            "Canonical instructions do not contain a supported initialized AEC block.";
        return inner is null
            ? new InvalidDataException(message)
            : new InvalidDataException(message, inner);
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

    private static bool IsLowerHexSha256(string value) =>
        value.Length == 64 && value.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');

    internal sealed record RepositoryBinding(
        int Version,
        string Repository,
        string? Platform = null,
        string? TargetDigest = null);

    internal sealed record RemovalResult(byte[] Content, bool Removed);

    private sealed record InitializedBlock(
        RepositoryBinding Binding,
        int Start,
        int End,
        string NewLine);

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
