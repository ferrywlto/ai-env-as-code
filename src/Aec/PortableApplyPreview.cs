namespace Aec;

// A preview is not an authorization to write. A future apply must revalidate
// the committed target and runtime immediately before compare-before-replace.
internal sealed record PortableApplyPreview(
    PortableRuntimeSnapshot Snapshot,
    string RuntimePath,
    byte[] DesiredRuntime,
    bool WouldReplace,
    PortableCodexConfigPreview? CodexConfig)
{
    public static PortableApplyPreview Create(
        string repository,
        string provider,
        string providerHome)
    {
        var snapshot = PortableRuntimeSnapshot.Read(repository, provider, providerHome);
        if (snapshot.Runtime is null)
        {
            throw new InvalidOperationException(
                "Provider runtime instructions are missing. Run ordinary provider init first.");
        }

        var desired = RenderDesiredRuntime(snapshot);
        var config = provider == "codex"
            ? PortableCodexConfigPreview.Create(snapshot)
            : null;
        return new PortableApplyPreview(
            snapshot,
            snapshot.RuntimePath,
            desired,
            !snapshot.Runtime.AsSpan().SequenceEqual(desired),
            config);
    }

    internal static byte[] RenderDesiredRuntime(PortableRuntimeSnapshot snapshot) =>
        snapshot.Provider switch
        {
            "codex" => AecInstructionBlock.MergeForPortableTarget(
                snapshot.Target,
                snapshot.Repository,
                snapshot.Platform,
                snapshot.BlockVersion is 4 or 6 or 8 or 10),
            "copilot" => CopilotInstructionBlock.MergeForPortableTarget(
                snapshot.Target,
                snapshot.Repository,
                snapshot.Platform),
            _ => throw new ArgumentException($"Unsupported provider: {snapshot.Provider}")
        };
}
