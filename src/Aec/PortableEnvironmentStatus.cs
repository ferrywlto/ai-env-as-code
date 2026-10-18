namespace Aec;

internal sealed record PortableStatusReport(string Instructions, string? Config)
{
    public bool InSync => Instructions == "in_sync" && (Config is null or "in_sync");
}

// Compare only enrolled, committed target data with the selected local runtime.
// The per-file report preserves the CLI's useful missing/different distinction.
internal static class PortableEnvironmentStatus
{
    public static PortableStatusReport Inspect(
        string repository, string provider, string providerHome)
    {
        var snapshot = PortableRuntimeSnapshot.Read(repository, provider, providerHome);
        var instructions = snapshot.Runtime is null
            ? "missing"
            : snapshot.Runtime.AsSpan().SequenceEqual(
                PortableApplyPreview.RenderDesiredRuntime(snapshot))
                ? "in_sync"
                : "different";

        if (provider == "copilot")
        {
            return new PortableStatusReport(instructions, null);
        }

        // Codex status validates the committed config even when instructions
        // are missing, so one invalid artifact cannot hide behind another.
        var config = PortableCodexConfigPreview.Create(snapshot);
        var configStatus = config.Inserted
            ? "missing"
            : config.WouldReplace ? "different" : "in_sync";
        return new PortableStatusReport(instructions, configStatus);
    }
}
