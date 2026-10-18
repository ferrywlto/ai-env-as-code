namespace Aec;

// Internal 2.0 status. Public status keeps its 1.x contract until the
// provider-specific apply and backup migrations are ready together.
internal static class PortableEnvironmentStatus
{
    public static string Inspect(string repository, string provider, string providerHome)
    {
        var snapshot = PortableRuntimeSnapshot.Read(repository, provider, providerHome);
        if (snapshot.Runtime is null)
        {
            return "missing";
        }

        var desiredInstructions = PortableApplyPreview.RenderDesiredRuntime(snapshot);
        var instructionsDiffer = !snapshot.Runtime.AsSpan().SequenceEqual(desiredInstructions);
        // The config preview compares only Codex's managed root personality.
        // It validates canonical and runtime TOML without writing either file.
        var configDiffers = provider == "codex" &&
            PortableCodexConfigPreview.Create(snapshot).WouldReplace;
        return instructionsDiffer || configDiffers ? "different" : "in_sync";
    }
}
