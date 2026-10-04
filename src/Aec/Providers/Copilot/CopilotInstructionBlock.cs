using System.Text;

namespace Aec;

// Copilot has its own personal-instructions file, so its marker namespace is
// deliberately separate from the managed block AEC places in Codex AGENTS.md.
internal static class CopilotInstructionBlock
{
    private const string BeginMarkerText = "<!-- AEC:COPILOT:BEGIN version=1 -->";
    private const string PortableBeginMarkerText = "<!-- AEC:COPILOT:BEGIN version=2 -->";
    private const string EndMarkerText = "<!-- AEC:COPILOT:END -->";
    private const string RepositoryPrefix = "The AEC data repository selected by `--repo` is `";
    private const string RepositorySuffix = "`.";

    private static readonly byte[] BeginMarker = Encoding.ASCII.GetBytes(BeginMarkerText);
    private static readonly byte[] PortableBeginMarker =
        Encoding.ASCII.GetBytes(PortableBeginMarkerText);
    private static readonly byte[] EndMarker = Encoding.ASCII.GetBytes(EndMarkerText);
    private static readonly byte[] BeginStem = "<!-- AEC:COPILOT:BEGIN"u8.ToArray();
    private static readonly byte[] EndStem = "<!-- AEC:COPILOT:END"u8.ToArray();
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static byte[] Merge(byte[] content, string repository) =>
        MergeVersion(content, NormalizeRepository(repository), version: 1, platform: null);

    // Generate a v2 local block without changing the public 1.x merge path.
    internal static byte[] MergeForPortableTarget(
        byte[] content,
        string repository,
        string platform)
    {
        var merged = MergeVersion(content, NormalizeRepository(repository), version: 2,
            ValidatePlatform(platform));
        if (merged.Length > AecApplication.MaximumTextBytes)
        {
            throw new InvalidDataException("Merged Copilot instructions exceed 1 MiB.");
        }

        return merged;
    }

    private static byte[] MergeVersion(
        byte[] content,
        string normalizedRepository,
        int version,
        string? platform)
    {
        ValidateText(content);
        var block = ReadBlock(content);
        if (block is null)
        {
            return Prepend(content, RenderBlock(
                DetectNewLine(content), normalizedRepository, version, platform));
        }

        if (!AecInstructionBlock.RepositoryPathsEqual(block.Repository, normalizedRepository))
        {
            throw new InvalidDataException(
                "Copilot instructions are bound to a different data repository.");
        }

        var expected = RenderBlock(block.NewLine, normalizedRepository, version, platform);
        if (block.Version == version &&
            content.AsSpan(block.Start, block.End - block.Start).SequenceEqual(expected))
        {
            return content.ToArray();
        }

        throw new InvalidDataException(
            "Copilot instructions contain an unsupported managed AEC block.");
    }

    public static string? ReadRepositoryBinding(byte[] content)
    {
        ValidateText(content);
        return ReadBlock(content)?.Repository;
    }

    internal static int? ReadManagedVersion(byte[] content)
    {
        ValidateText(content);
        return ReadBlock(content)?.Version;
    }

    private static ManagedBlock? ReadBlock(byte[] content)
    {
        var bodyOffset = content.AsSpan().StartsWith(Utf8Bom) ? Utf8Bom.Length : 0;
        var begin = FindOccurrences(content, BeginStem, bodyOffset);
        var end = FindOccurrences(content, EndStem, bodyOffset);
        if (begin.Count == 0 && end.Count == 0)
        {
            return null;
        }

        if (begin.Count != 1 || end.Count != 1)
        {
            throw new InvalidDataException("Copilot instructions contain malformed or duplicate AEC block markers.");
        }

        var start = begin[0];
        var finish = end[0] + EndMarker.Length;
        var version = IsExactMarkerLine(content, start, BeginMarker) ? 1 :
            IsExactMarkerLine(content, start, PortableBeginMarker) ? 2 : 0;
        var beginMarker = version == 1 ? BeginMarker : PortableBeginMarker;
        if (!IsLineStart(content, start, bodyOffset) || version == 0 ||
            !IsLineStart(content, end[0], bodyOffset) ||
            !IsExactMarkerLine(content, end[0], EndMarker) ||
            end[0] <= start)
        {
            throw new InvalidDataException("Copilot instructions contain a malformed AEC block.");
        }

        var newLine = ReadNewLine(content, start + beginMarker.Length)
            ?? throw new InvalidDataException("Copilot instructions contain a malformed AEC block.");
        var text = StrictUtf8.GetString(content, start, finish - start);
        var lines = text.Split(newLine, StringSplitOptions.None);
        if (lines.Length != (version == 1 ? 12 : 16) ||
            lines[0] != (version == 1 ? BeginMarkerText : PortableBeginMarkerText) ||
            lines[^1] != EndMarkerText)
        {
            throw new InvalidDataException("Copilot instructions contain an unsupported managed AEC block.");
        }

        var repositoryLine = lines[5];
        if (!repositoryLine.StartsWith(RepositoryPrefix, StringComparison.Ordinal) ||
            !repositoryLine.EndsWith(RepositorySuffix, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Copilot instructions contain an unsupported managed AEC block.");
        }

        var repository = NormalizeRepository(repositoryLine[RepositoryPrefix.Length..^RepositorySuffix.Length]);
        string? platform = null;
        if (version == 2)
        {
            const string platformPrefix = "The selected AEC platform is `";
            var platformLine = lines[7];
            if (!platformLine.StartsWith(platformPrefix, StringComparison.Ordinal) ||
                !platformLine.EndsWith(RepositorySuffix, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Copilot instructions contain an unsupported managed AEC block.");
            }

            try
            {
                platform = ValidatePlatform(
                    platformLine[platformPrefix.Length..^RepositorySuffix.Length]);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidDataException(
                    "Copilot instructions contain an unsupported managed AEC block.", exception);
            }
        }

        var expected = RenderBlock(newLine, repository, version, platform);
        if (!content.AsSpan(start, finish - start).SequenceEqual(expected))
        {
            throw new InvalidDataException("Copilot instructions contain an unsupported managed AEC block.");
        }

        return new ManagedBlock(start, finish, newLine, repository, version);
    }

    private static byte[] RenderBlock(
        string newLine,
        string repository,
        int version,
        string? platform)
    {
        if (version == 2)
        {
            var target = Path.Combine(repository, "environment", "targets", platform!,
                "copilot", "copilot-instructions.md");
            return StrictUtf8.GetBytes(string.Join(newLine,
            [
                PortableBeginMarkerText,
                "## AI Environment as Code",
                string.Empty,
                "Use the `/aec` skill for managed personal GitHub Copilot instructions.",
                string.Empty,
                $"The AEC data repository selected by `--repo` is `{repository}`.",
                "Treat its Git commit history as the source of truth.",
                $"The selected AEC platform is `{platform}`.",
                $"The committed Copilot instruction target is `{target}`.",
                "Edit the authored shared, platform, or Copilot overlay source; run `aec render`, review, and commit the generated target. Do not edit the target directly.",
                "Preserve instructions outside this managed block; do not edit the runtime `copilot-instructions.md` as the source of truth.",
                string.Empty,
                "For repository-to-runtime changes, run `aec apply`, then verify with `aec status`.",
                "Use `aec backup` only for an explicitly authorized runtime-to-repository capture.",
                "Copilot support is alpha; use `/aec` only for currently supported AEC operations.",
                EndMarkerText
            ]));
        }

        var canonical = Path.Combine(repository, AecApplication.CopilotSourceRelativePath);
        var text = string.Join(newLine,
        [
            BeginMarkerText,
            "## AI Environment as Code",
            string.Empty,
            "Use the `/aec` skill for managed personal GitHub Copilot instructions.",
            string.Empty,
            $"The AEC data repository selected by `--repo` is `{repository}`.",
            "Treat its Git commit history as the source of truth.",
            $"The canonical Copilot instructions are `{canonical}`.",
            "Preserve instructions outside this managed block; do not edit the runtime `copilot-instructions.md` as the source of truth.",
            string.Empty,
            "Copilot support is alpha; use `/aec` only for currently supported AEC operations.",
            EndMarkerText
        ]);
        return StrictUtf8.GetBytes(text);
    }

    private static byte[] Prepend(byte[] content, byte[] block)
    {
        var bodyOffset = content.AsSpan().StartsWith(Utf8Bom) ? Utf8Bom.Length : 0;
        var newLine = Encoding.ASCII.GetBytes(DetectNewLine(content));
        var bodyLength = content.Length - bodyOffset;
        var separatorLength = bodyLength == 0 ? newLine.Length : newLine.Length * 2;
        var result = new byte[checked(content.Length + block.Length + separatorLength)];
        content.AsSpan(0, bodyOffset).CopyTo(result);
        block.CopyTo(result.AsSpan(bodyOffset));
        var destination = bodyOffset + block.Length;
        newLine.CopyTo(result.AsSpan(destination));
        destination += newLine.Length;
        if (bodyLength != 0)
        {
            newLine.CopyTo(result.AsSpan(destination));
            destination += newLine.Length;
            content.AsSpan(bodyOffset).CopyTo(result.AsSpan(destination));
        }

        return result;
    }

    private static void ValidateText(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        _ = StrictUtf8.GetString(content);
    }

    private static string NormalizeRepository(string repository)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repository);
        if (repository.Contains('`') || repository.Contains('\r') || repository.Contains('\n'))
        {
            throw new ArgumentException("Repository path cannot be embedded in managed instructions.");
        }

        if (!Path.IsPathFullyQualified(repository))
        {
            throw new ArgumentException("Repository must be an absolute path.");
        }

        return Path.TrimEndingDirectorySeparator(Path.GetFullPath(repository));
    }

    private static string ValidatePlatform(string platform) => platform switch
    {
        "macos-arm64" or "windows-x64" or "linux-arm64" => platform,
        _ => throw new ArgumentException($"Unsupported AEC platform: {platform}")
    };

    private static List<int> FindOccurrences(byte[] content, byte[] value, int start)
    {
        var matches = new List<int>();
        var offset = start;
        while (offset <= content.Length - value.Length)
        {
            var index = content.AsSpan(offset).IndexOf(value);
            if (index < 0)
            {
                break;
            }

            offset += index;
            matches.Add(offset);
            offset++;
        }

        return matches;
    }

    private static bool IsLineStart(byte[] content, int offset, int bodyOffset) =>
        offset == bodyOffset || content[offset - 1] == (byte)'\n';

    private static bool IsExactMarkerLine(byte[] content, int offset, byte[] marker)
    {
        if (!content.AsSpan(offset).StartsWith(marker))
        {
            return false;
        }

        var after = offset + marker.Length;
        return after == content.Length || content[after] is (byte)'\n' or (byte)'\r';
    }

    private static string DetectNewLine(byte[] content) =>
        content.AsSpan().IndexOf("\r\n"u8) >= 0 ? "\r\n" : "\n";

    private static string? ReadNewLine(byte[] content, int offset)
    {
        if (offset >= content.Length)
        {
            return null;
        }

        if (content[offset] == (byte)'\n')
        {
            return "\n";
        }

        return offset + 1 < content.Length && content[offset] == (byte)'\r' &&
               content[offset + 1] == (byte)'\n'
            ? "\r\n"
            : null;
    }

    private sealed record ManagedBlock(
        int Start,
        int End,
        string NewLine,
        string Repository,
        int Version);
}
