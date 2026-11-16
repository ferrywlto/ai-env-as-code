namespace Aec;

/// <summary>Composes explicitly selected instruction sources without accessing runtime or Git.</summary>
internal static class InstructionComposer
{
    public static string Render(
        string aecBlock,
        string sharedPolicy,
        string? platformPolicy = null,
        string? providerPolicy = null)
    {
        ArgumentNullException.ThrowIfNull(aecBlock);
        ArgumentNullException.ThrowIfNull(sharedPolicy);

        // Preserve authored whitespace and line endings: trimming or normalizing
        // could change Markdown meaning. Only separators belong to the composer.
        // Empty optional sources require neither placeholder files nor headings.
        string?[] sections = [aecBlock, sharedPolicy, platformPolicy, providerPolicy];
        return string.Join("\n\n", sections.Where(section => !string.IsNullOrEmpty(section)));
    }
}
