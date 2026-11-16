namespace Aec;

// Read-only v2 instruction comparison. Public status remains on the legacy
// source until apply can use the same portable target in the 2.0 cutover.
internal static class SharedTargetStatus
{
    public static string Inspect(string repository, string provider, string providerHome)
    {
        var snapshot = PortableRuntimeSnapshot.Read(repository, provider, providerHome);
        if (snapshot.Runtime is null)
        {
            return "missing";
        }

        // Status and the future apply must agree on the same v2 bytes. A
        // supported legacy block is readable but reports drift until upgraded.
        var expected = PortableApplyPreview.RenderDesiredRuntime(snapshot);
        return snapshot.Runtime.AsSpan().SequenceEqual(expected) ? "in_sync" : "different";
    }
}
