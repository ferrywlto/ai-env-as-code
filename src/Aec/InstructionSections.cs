namespace Aec;

// Section markers are part of the rendered file, not of the user-authored sources.
// Their exact boundaries let backup attribute a runtime edit without guessing.
internal static class InstructionSections
{
    private const string MarkerStem = "<!-- AEC:SOURCE:";

    internal sealed record Parts(string Shared, string? Platform, string? Provider);

    public static string Render(string shared, string? platform, string? provider)
    {
        ArgumentNullException.ThrowIfNull(shared);
        var sections = new List<string> { Wrap("SHARED", shared) };
        if (platform is not null)
        {
            sections.Add(Wrap("PLATFORM", platform));
        }

        if (provider is not null)
        {
            sections.Add(Wrap("PROVIDER", provider));
        }

        return string.Join("\n\n", sections);
    }

    public static Parts Parse(string content, bool hasPlatform, bool hasProvider)
    {
        ArgumentNullException.ThrowIfNull(content);
        var position = 0;
        var shared = Read(content, "SHARED", ref position);
        var platform = hasPlatform ? ReadNext(content, "PLATFORM", ref position) : null;
        var provider = hasProvider ? ReadNext(content, "PROVIDER", ref position) : null;

        var parts = new Parts(shared, platform, provider);
        // The exact round trip rejects reordered, duplicate or altered markers,
        // extra text, and ambiguous marker-like text inside an authored section.
        if (position != content.Length ||
            !string.Equals(Render(parts.Shared, parts.Platform, parts.Provider), content,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException("Rendered instructions contain unsupported section boundaries.");
        }

        return parts;
    }

    private static string Wrap(string name, string content)
    {
        if (content.Contains(MarkerStem, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Instruction source contains an AEC section marker.");
        }

        return $"<!-- AEC:SOURCE:{name}:BEGIN -->\n{content}\n<!-- AEC:SOURCE:{name}:END -->";
    }

    private static string ReadNext(string content, string name, ref int position)
    {
        if (!content.AsSpan(position).StartsWith("\n\n", StringComparison.Ordinal))
        {
            throw new InvalidDataException("Rendered instructions are missing a section separator.");
        }

        position += 2;
        return Read(content, name, ref position);
    }

    private static string Read(string content, string name, ref int position)
    {
        var begin = $"<!-- AEC:SOURCE:{name}:BEGIN -->\n";
        var end = $"\n<!-- AEC:SOURCE:{name}:END -->";
        if (!content.AsSpan(position).StartsWith(begin, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"Rendered instructions are missing the {name} begin marker.");
        }

        position += begin.Length;
        var endOffset = content.IndexOf(end, position, StringComparison.Ordinal);
        if (endOffset < 0)
        {
            throw new InvalidDataException($"Rendered instructions are missing the {name} end marker.");
        }

        var value = content[position..endOffset];
        position = endOffset + end.Length;
        return value;
    }
}
