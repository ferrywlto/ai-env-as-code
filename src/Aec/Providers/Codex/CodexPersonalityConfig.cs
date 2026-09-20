using System.Text;

namespace Aec;

internal enum CodexPersonality
{
    None,
    Friendly,
    Pragmatic
}

internal readonly record struct RuntimeConfigUpdate(
    byte[] Content,
    bool Changed,
    bool Inserted);

internal static class CodexPersonalityConfig
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    internal static CodexPersonality ReadCanonical(byte[] content, string path)
    {
        return Read(content, "Canonical config", path, canonical: true).Personality
            ?? throw new InvalidDataException(
                $"Canonical config does not declare personality: {path}");
    }

    internal static CodexPersonality? ReadRuntime(byte[] content, string path)
    {
        return Read(content, "Runtime config", path, canonical: false).Personality;
    }

    internal static RuntimeConfigUpdate PlanRuntimeUpdate(
        byte[]? content,
        string path,
        CodexPersonality desired)
    {
        if (content is null)
        {
            return CreateUpdate(
                StrictUtf8.GetBytes($"personality = \"{ToConfigValue(desired)}\"\n"),
                path,
                "Runtime config",
                inserted: true);
        }

        var parsed = Read(content, "Runtime config", path, canonical: false);
        if (parsed.Personality == desired)
        {
            return new RuntimeConfigUpdate(content, Changed: false, Inserted: false);
        }

        var value = $"\"{ToConfigValue(desired)}\"";
        string updated;
        var inserted = parsed.Personality is null;
        if (inserted)
        {
            // A root assignment placed before the untouched body cannot accidentally
            // become part of a table already present in the runtime configuration.
            updated = $"personality = {value}{DetectLineEnding(parsed.Text)}{parsed.Text}";
        }
        else
        {
            updated = string.Concat(
                parsed.Text.AsSpan(0, parsed.ValueStart),
                value,
                parsed.Text.AsSpan(parsed.ValueStart + parsed.ValueLength));
        }

        return CreateUpdate(
            Encode(updated, parsed.HasBom),
            path,
            "Runtime config",
            inserted);
    }

    internal static RuntimeConfigUpdate PlanCanonicalUpdate(
        byte[]? content,
        string path,
        CodexPersonality desired)
    {
        if (content is null)
        {
            return CreateUpdate(
                StrictUtf8.GetBytes($"personality = \"{ToConfigValue(desired)}\"\n"),
                path,
                "Canonical config",
                inserted: true);
        }

        var parsed = Read(content, "Canonical config", path, canonical: true);
        if (parsed.Personality is null)
        {
            throw new InvalidDataException(
                $"Canonical config does not declare personality: {path}");
        }

        if (parsed.Personality == desired)
        {
            return new RuntimeConfigUpdate(content, Changed: false, Inserted: false);
        }

        // Replace only the managed TOML value so comments, spacing, line endings,
        // and a possible UTF-8 BOM remain exactly as the user authored them.
        var value = $"\"{ToConfigValue(desired)}\"";
        var updated = string.Concat(
            parsed.Text.AsSpan(0, parsed.ValueStart),
            value,
            parsed.Text.AsSpan(parsed.ValueStart + parsed.ValueLength));
        return CreateUpdate(
            Encode(updated, parsed.HasBom),
            path,
            "Canonical config",
            inserted: false);
    }

    private static RuntimeConfigUpdate CreateUpdate(
        byte[] content,
        string path,
        string label,
        bool inserted)
    {
        // Validate planned bytes before either command can mutate its destination.
        if (content.Length > AecApplication.MaximumTextBytes)
        {
            throw new InvalidDataException(
                $"{label} would exceed 1 MiB after updating personality: {path}");
        }

        return new RuntimeConfigUpdate(content, Changed: true, Inserted: inserted);
    }

    private static ParsedConfig Read(
        byte[] content,
        string label,
        string path,
        bool canonical)
    {
        var decoded = Decode(content, label, path);
        var text = decoded.Text;
        CodexPersonality? personality = null;
        var valueStart = -1;
        var valueLength = 0;
        var textOffset = 0;
        var inTableSection = false;
        var valueState = new ValueState();
        using var reader = new StringReader(text);

        while (reader.ReadLine() is { } line)
        {
            var lineStart = textOffset;
            textOffset += line.Length;
            if (textOffset < text.Length && text[textOffset] == '\r')
            {
                textOffset++;
                if (textOffset < text.Length && text[textOffset] == '\n')
                {
                    textOffset++;
                }
            }
            else if (textOffset < text.Length && text[textOffset] == '\n')
            {
                textOffset++;
            }

            if (valueState.IsActive)
            {
                ConsumeUnmanagedValue(line, ref valueState, label, path);
                continue;
            }

            var lineSpan = line.AsSpan();
            var trimmed = TrimTomlSpace(lineSpan);
            var trimmedStart = lineSpan.Length - TrimTomlSpaceStart(lineSpan).Length;
            if (trimmed.IsEmpty || trimmed[0] == '#')
            {
                continue;
            }

            if (inTableSection)
            {
                if (trimmed[0] == '[')
                {
                    EnsureTableDoesNotConflictWithPersonality(trimmed, label, path);
                    continue;
                }

                var tableEquals = FindAssignmentEquals(trimmed);
                if (tableEquals < 0)
                {
                    throw new InvalidDataException(
                        $"{label} contains ambiguous table TOML: {path}");
                }

                // Track multiline table values so text that resembles a later
                // header inside a value is never interpreted as an actual table.
                ConsumeUnmanagedValue(
                    trimmed[(tableEquals + 1)..],
                    ref valueState,
                    label,
                    path);
                continue;
            }

            if (trimmed[0] == '[')
            {
                if (canonical)
                {
                    throw new InvalidDataException(
                        $"Canonical config contains an unmanaged setting: {path}");
                }

                EnsureTableDoesNotConflictWithPersonality(trimmed, label, path);
                // Root assignments cannot resume after a TOML table header. Keep
                // scanning headers for conflicts while ignoring table-scoped keys.
                inTableSection = true;
                continue;
            }

            var equals = FindAssignmentEquals(trimmed);
            if (equals < 0)
            {
                throw new InvalidDataException(
                    $"{label} contains ambiguous root TOML: {path}");
            }

            var key = ReadRootKey(trimmed[..equals], label, path);
            if (key.FirstSegment == "personality")
            {
                if (key.SegmentCount != 1)
                {
                    throw new InvalidDataException(
                        $"{label} has an ambiguous personality declaration: {path}");
                }

                var current = ParsePersonality(
                    trimmed[(equals + 1)..],
                    label,
                    path,
                    out var relativeValueStart,
                    out var currentValueLength);
                if (personality is not null)
                {
                    throw new InvalidDataException(
                        $"{label} declares personality more than once: {path}");
                }

                personality = current;
                valueStart = lineStart + trimmedStart + equals + 1 + relativeValueStart;
                valueLength = currentValueLength;
                continue;
            }

            if (canonical)
            {
                throw new InvalidDataException(
                    $"Canonical config contains an unmanaged setting: {path}");
            }

            ConsumeUnmanagedValue(trimmed[(equals + 1)..], ref valueState, label, path);
        }

        if (valueState.IsActive)
        {
            throw new InvalidDataException(
                $"{label} contains an unterminated root value: {path}");
        }

        return new ParsedConfig(
            text,
            decoded.HasBom,
            personality,
            valueStart,
            valueLength);
    }

    private static DecodedConfig Decode(byte[] content, string label, string path)
    {
        string text;
        try
        {
            text = StrictUtf8.GetString(content);
        }
        catch (DecoderFallbackException exception)
        {
            throw new InvalidDataException($"{label} is not valid UTF-8: {path}", exception);
        }

        var hasBom = text.Length > 0 && text[0] == '\uFEFF';
        if (hasBom)
        {
            text = text[1..];
        }

        // TOML permits tab, line feed, and carriage return as raw controls. Other
        // control characters must be escaped even when they appear in comments.
        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];
            if ((current < ' ' && current is not ('\t' or '\n' or '\r')) || current == '\u007F')
            {
                throw new InvalidDataException(
                    $"{label} contains a forbidden control character: {path}");
            }

            if (current == '\r' &&
                (index + 1 >= text.Length || text[index + 1] != '\n'))
            {
                throw new InvalidDataException(
                    $"{label} contains a lone carriage return: {path}");
            }
        }

        return new DecodedConfig(text, hasBom);
    }

    private static void EnsureTableDoesNotConflictWithPersonality(
        ReadOnlySpan<char> line,
        string label,
        string path)
    {
        var arrayTable = line.StartsWith("[[", StringComparison.Ordinal);
        var contentStart = arrayTable ? 2 : 1;
        var closing = FindTableHeaderEnd(line, contentStart, arrayTable, label, path);
        var key = ReadRootKey(line[contentStart..closing], label, path);
        if (key.FirstSegment == "personality")
        {
            throw new InvalidDataException(
                $"{label} contains a table that conflicts with personality: {path}");
        }

        var closingLength = arrayTable ? 2 : 1;
        var trailing = TrimTomlSpaceStart(line[(closing + closingLength)..]);
        if (!trailing.IsEmpty && trailing[0] != '#')
        {
            throw new InvalidDataException($"{label} contains an ambiguous table header: {path}");
        }
    }

    private static int FindTableHeaderEnd(
        ReadOnlySpan<char> line,
        int contentStart,
        bool arrayTable,
        string label,
        string path)
    {
        char quote = '\0';
        var escaped = false;
        for (var index = contentStart; index < line.Length; index++)
        {
            var current = line[index];
            if (quote != '\0')
            {
                if (quote == '\"' && !escaped && current == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (!escaped && current == quote)
                {
                    quote = '\0';
                }

                escaped = false;
                continue;
            }

            if (current is '\"' or '\'')
            {
                quote = current;
                continue;
            }

            if (current != ']')
            {
                continue;
            }

            if (!arrayTable || index + 1 < line.Length && line[index + 1] == ']')
            {
                return index;
            }

            break;
        }

        throw new InvalidDataException($"{label} contains an ambiguous table header: {path}");
    }

    private static int FindAssignmentEquals(ReadOnlySpan<char> line)
    {
        char quote = '\0';
        var escaped = false;

        for (var index = 0; index < line.Length; index++)
        {
            var current = line[index];
            if (quote != '\0')
            {
                if (quote == '\"' && !escaped && current == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (!escaped && current == quote)
                {
                    quote = '\0';
                }

                escaped = false;
                continue;
            }

            if (current is '\"' or '\'')
            {
                quote = current;
            }
            else if (current == '=')
            {
                return index;
            }
            else if (current == '#')
            {
                break;
            }
        }

        return -1;
    }

    private static RootKey ReadRootKey(
        ReadOnlySpan<char> source,
        string label,
        string path)
    {
        var remaining = TrimTomlSpace(source);
        string? firstSegment = null;
        var segmentCount = 0;

        while (true)
        {
            if (remaining.IsEmpty)
            {
                throw new InvalidDataException($"{label} contains an empty root key: {path}");
            }

            string segment;
            int consumed;
            if (remaining[0] == '\"')
            {
                segment = ReadBasicString(remaining, label, path, out consumed);
            }
            else if (remaining[0] == '\'')
            {
                segment = ReadLiteralString(remaining, label, path, out consumed);
            }
            else
            {
                consumed = 0;
                while (consumed < remaining.Length && IsBareKeyCharacter(remaining[consumed]))
                {
                    consumed++;
                }

                if (consumed == 0)
                {
                    throw new InvalidDataException(
                        $"{label} contains an ambiguous root key: {path}");
                }

                segment = remaining[..consumed].ToString();
            }

            firstSegment ??= segment;
            segmentCount++;
            remaining = TrimTomlSpaceStart(remaining[consumed..]);
            if (remaining.IsEmpty)
            {
                return new RootKey(firstSegment, segmentCount);
            }

            if (remaining[0] != '.')
            {
                throw new InvalidDataException($"{label} contains an ambiguous root key: {path}");
            }

            remaining = TrimTomlSpaceStart(remaining[1..]);
        }
    }

    private static CodexPersonality ParsePersonality(
        ReadOnlySpan<char> source,
        string label,
        string path,
        out int valueStart,
        out int valueLength)
    {
        var remaining = TrimTomlSpaceStart(source);
        valueStart = source.Length - remaining.Length;
        if (remaining.StartsWith("\"\"\"", StringComparison.Ordinal) ||
            remaining.StartsWith("'''", StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                $"{label} has an ambiguous personality declaration: {path}");
        }

        string value;
        int consumed;
        if (!remaining.IsEmpty && remaining[0] == '\"')
        {
            value = ReadBasicString(remaining, label, path, out consumed);
        }
        else if (!remaining.IsEmpty && remaining[0] == '\'')
        {
            value = ReadLiteralString(remaining, label, path, out consumed);
        }
        else
        {
            throw new InvalidDataException(
                $"{label} has an ambiguous personality declaration: {path}");
        }

        var trailing = TrimTomlSpaceStart(remaining[consumed..]);
        if (!trailing.IsEmpty && trailing[0] != '#')
        {
            throw new InvalidDataException(
                $"{label} has an ambiguous personality declaration: {path}");
        }

        valueLength = consumed;

        return value switch
        {
            "none" => CodexPersonality.None,
            "friendly" => CodexPersonality.Friendly,
            "pragmatic" => CodexPersonality.Pragmatic,
            _ => throw new InvalidDataException(
                $"{label} has unsupported personality '{value}': {path}")
        };
    }

    private static string ToConfigValue(CodexPersonality personality)
    {
        return personality switch
        {
            CodexPersonality.None => "none",
            CodexPersonality.Friendly => "friendly",
            CodexPersonality.Pragmatic => "pragmatic",
            _ => throw new ArgumentOutOfRangeException(nameof(personality))
        };
    }

    private static string DetectLineEnding(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\n')
            {
                return index > 0 && text[index - 1] == '\r' ? "\r\n" : "\n";
            }

            if (text[index] == '\r')
            {
                return "\r\n";
            }
        }

        return "\n";
    }

    private static byte[] Encode(string text, bool includeBom)
    {
        var body = StrictUtf8.GetBytes(text);
        return includeBom
            ? [0xEF, 0xBB, 0xBF, .. body]
            : body;
    }

    private static string ReadBasicString(
        ReadOnlySpan<char> source,
        string label,
        string path,
        out int consumed)
    {
        var value = new StringBuilder();
        for (var index = 1; index < source.Length; index++)
        {
            var current = source[index];
            if (current == '\"')
            {
                consumed = index + 1;
                return value.ToString();
            }

            if (current != '\\')
            {
                value.Append(current);
                continue;
            }

            if (++index >= source.Length)
            {
                break;
            }

            var escape = source[index];
            switch (escape)
            {
                case 'b': value.Append('\b'); break;
                case 't': value.Append('\t'); break;
                case 'n': value.Append('\n'); break;
                case 'f': value.Append('\f'); break;
                case 'r': value.Append('\r'); break;
                case '\"': value.Append('\"'); break;
                case '\\': value.Append('\\'); break;
                case 'u':
                case 'U':
                    var digits = escape == 'u' ? 4 : 8;
                    if (index + digits >= source.Length)
                    {
                        throw InvalidString(label, path);
                    }

                    var codePoint = ReadHex(source.Slice(index + 1, digits), label, path);
                    if (codePoint is > 0x10FFFF or >= 0xD800 and <= 0xDFFF)
                    {
                        throw InvalidString(label, path);
                    }

                    value.Append(char.ConvertFromUtf32(codePoint));
                    index += digits;
                    break;
                default:
                    throw InvalidString(label, path);
            }
        }

        throw InvalidString(label, path);
    }

    private static string ReadLiteralString(
        ReadOnlySpan<char> source,
        string label,
        string path,
        out int consumed)
    {
        var closing = source[1..].IndexOf('\'');
        if (closing < 0)
        {
            throw InvalidString(label, path);
        }

        closing++;
        consumed = closing + 1;
        return source[1..closing].ToString();
    }

    private static int ReadHex(ReadOnlySpan<char> digits, string label, string path)
    {
        var value = 0;
        foreach (var digit in digits)
        {
            var current = digit switch
            {
                >= '0' and <= '9' => digit - '0',
                >= 'a' and <= 'f' => digit - 'a' + 10,
                >= 'A' and <= 'F' => digit - 'A' + 10,
                _ => -1
            };
            if (current < 0)
            {
                throw InvalidString(label, path);
            }

            value = (value * 16) + current;
        }

        return value;
    }

    private static InvalidDataException InvalidString(string label, string path)
    {
        return new InvalidDataException($"{label} contains an invalid TOML string: {path}");
    }

    private static bool IsBareKeyCharacter(char value)
    {
        return value is >= 'a' and <= 'z' or
            >= 'A' and <= 'Z' or
            >= '0' and <= '9' or
            '_' or '-';
    }

    private static ReadOnlySpan<char> TrimTomlSpace(ReadOnlySpan<char> value)
    {
        var start = 0;
        while (start < value.Length && IsTomlSpace(value[start]))
        {
            start++;
        }

        var end = value.Length;
        while (end > start && IsTomlSpace(value[end - 1]))
        {
            end--;
        }

        return value[start..end];
    }

    private static ReadOnlySpan<char> TrimTomlSpaceStart(ReadOnlySpan<char> value)
    {
        var start = 0;
        while (start < value.Length && IsTomlSpace(value[start]))
        {
            start++;
        }

        return value[start..];
    }

    private static bool IsTomlSpace(char value) => value is ' ' or '\t';

    private static void ConsumeUnmanagedValue(
        ReadOnlySpan<char> text,
        ref ValueState state,
        string label,
        string path)
    {
        for (var index = 0; index < text.Length; index++)
        {
            var current = text[index];
            if (state.StringKind != TomlStringKind.None)
            {
                if (ConsumeStringCharacter(text, ref index, ref state))
                {
                    continue;
                }

                state.StringKind = TomlStringKind.None;
                continue;
            }

            if (IsTomlSpace(current))
            {
                continue;
            }

            if (current == '#')
            {
                break;
            }

            state.SawValue = true;
            if (current is '\"' or '\'')
            {
                var multiline = index + 2 < text.Length &&
                                text[index + 1] == current &&
                                text[index + 2] == current;
                state.StringKind = (current, multiline) switch
                {
                    ('\"', false) => TomlStringKind.Basic,
                    ('\"', true) => TomlStringKind.MultilineBasic,
                    ('\'', false) => TomlStringKind.Literal,
                    _ => TomlStringKind.MultilineLiteral
                };

                if (multiline)
                {
                    index += 2;
                }

                continue;
            }

            switch (current)
            {
                case '[':
                    state.ArrayDepth++;
                    break;
                case ']':
                    if (state.ArrayDepth == 0)
                    {
                        throw new InvalidDataException(
                            $"{label} contains ambiguous root TOML: {path}");
                    }

                    state.ArrayDepth--;
                    break;
                case '{':
                    state.InlineTableDepth++;
                    break;
                case '}':
                    if (state.InlineTableDepth == 0)
                    {
                        throw new InvalidDataException(
                            $"{label} contains ambiguous root TOML: {path}");
                    }

                    state.InlineTableDepth--;
                    break;
            }
        }

        if (state.StringKind is TomlStringKind.Basic or TomlStringKind.Literal)
        {
            throw new InvalidDataException($"{label} contains ambiguous root TOML: {path}");
        }

        if (!state.SawValue)
        {
            throw new InvalidDataException($"{label} contains an empty root value: {path}");
        }

        if (!state.IsActive)
        {
            state = default;
        }
    }

    private static bool ConsumeStringCharacter(
        ReadOnlySpan<char> text,
        ref int index,
        ref ValueState state)
    {
        var current = text[index];
        if ((state.StringKind is TomlStringKind.Basic or TomlStringKind.MultilineBasic) &&
            current == '\\')
        {
            if (index + 1 < text.Length)
            {
                index++;
            }

            return true;
        }

        var quote = state.StringKind is TomlStringKind.Literal or TomlStringKind.MultilineLiteral
            ? '\''
            : '\"';
        var multiline = state.StringKind is
            TomlStringKind.MultilineBasic or TomlStringKind.MultilineLiteral;
        if (current != quote)
        {
            return true;
        }

        if (!multiline)
        {
            return false;
        }

        var quoteCount = 1;
        while (index + quoteCount < text.Length && text[index + quoteCount] == quote)
        {
            quoteCount++;
        }

        if (quoteCount < 3)
        {
            return true;
        }

        index += quoteCount - 1;
        return false;
    }

    private readonly record struct RootKey(string FirstSegment, int SegmentCount);

    private readonly record struct DecodedConfig(string Text, bool HasBom);

    private readonly record struct ParsedConfig(
        string Text,
        bool HasBom,
        CodexPersonality? Personality,
        int ValueStart,
        int ValueLength);

    private enum TomlStringKind
    {
        None,
        Basic,
        Literal,
        MultilineBasic,
        MultilineLiteral
    }

    private struct ValueState
    {
        internal int ArrayDepth;
        internal int InlineTableDepth;
        internal TomlStringKind StringKind;
        internal bool SawValue;

        internal readonly bool IsActive =>
            ArrayDepth > 0 ||
            InlineTableDepth > 0 ||
            StringKind is TomlStringKind.MultilineBasic or TomlStringKind.MultilineLiteral;
    }
}
