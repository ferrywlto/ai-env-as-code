namespace Aec;

// A reviewed preflight snapshot permits one instruction-file replacement,
// never a repository write. Public provider apply routes through this guard.
internal static class PortableInstructionWriter
{
    public static string Apply(PortableRuntimeSnapshot reviewed)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        if (reviewed.Runtime is null)
        {
            throw new InvalidOperationException(
                "Provider runtime instructions are missing. Run ordinary provider init first.");
        }

        var providerHome = Path.GetDirectoryName(reviewed.RuntimePath)
            ?? throw new InvalidOperationException("Provider runtime target has no parent directory.");
        var current = PortableRuntimeSnapshot.Read(
            reviewed.Repository, reviewed.Provider, providerHome);
        if (current.Runtime is null ||
            !string.Equals(current.RuntimePath, reviewed.RuntimePath, StringComparison.Ordinal) ||
            !string.Equals(current.Platform, reviewed.Platform, StringComparison.Ordinal) ||
            !string.Equals(current.Commit, reviewed.Commit, StringComparison.Ordinal) ||
            current.BlockVersion != reviewed.BlockVersion ||
            !current.Target.AsSpan().SequenceEqual(reviewed.Target) ||
            !current.Runtime.AsSpan().SequenceEqual(reviewed.Runtime))
        {
            throw new InvalidOperationException(
                "Portable apply inputs changed after preflight; no runtime instruction was replaced.");
        }

        var desired = PortableApplyPreview.RenderDesiredRuntime(current);
        if (current.Runtime.AsSpan().SequenceEqual(desired))
        {
            return "unchanged";
        }

        // Recheck repository provenance after composing the write. The
        // compare-before-replace helper separately rechecks the runtime file.
        var finalTarget = CommittedTargetValidator.Read(
            current.Repository, current.Platform, current.Provider);
        if (!string.Equals(current.Commit, ApplyCommand.ResolveHeadCommit(current.Repository),
                StringComparison.Ordinal) ||
            !finalTarget.AsSpan().SequenceEqual(current.Target))
        {
            throw new InvalidOperationException(
                "Portable apply target changed before the runtime replacement.");
        }

        // The atomic helper rejects edits visible at its final byte comparison.
        // External writers do not honor a shared lock, so this is not a true
        // cross-process compare-and-swap of the final rename.
        AtomicFile.ReplaceIfUnchanged(
            current.RuntimePath,
            current.Runtime,
            desired,
            "Provider runtime instructions");
        return "applied";
    }
}
