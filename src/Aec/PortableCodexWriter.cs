namespace Aec;

// Internal 2.0 writer only. Public apply still uses the committed 1.x files.
internal static class PortableCodexWriter
{
    public static string Apply(
        PortableApplyPreview reviewed,
        Action? afterConfigWriteForTests = null)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        if (reviewed.Snapshot.Provider != "codex" || reviewed.CodexConfig is null)
        {
            throw new ArgumentException("A reviewed Codex apply preview is required.");
        }

        var providerHome = Path.GetDirectoryName(reviewed.RuntimePath)
            ?? throw new InvalidOperationException("Codex runtime target has no parent directory.");
        var current = PortableApplyPreview.Create(
            reviewed.Snapshot.Repository, "codex", providerHome);
        if (!MatchesReview(reviewed, current))
        {
            throw new InvalidOperationException(
                "Codex apply inputs changed after preflight; AEC replaced no runtime file.");
        }

        var config = current.CodexConfig!;
        if (!current.WouldReplace && !config.WouldReplace)
        {
            return "unchanged";
        }

        if (config.WouldReplace)
        {
            try
            {
                AtomicFile.ReplaceIfUnchanged(
                    config.RuntimePath,
                    config.Runtime,
                    config.DesiredRuntime,
                    "Runtime config",
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch (Exception ex) when (IsApplyFailure(ex))
            {
                // Replacement can fail after its final move. Do not claim the
                // config is untouched, but instructions have not been written.
                throw new IOException(
                    $"Codex config replacement failed; AEC did not replace instructions. " +
                    $"Review runtime config at {config.RuntimePath} before retrying.", ex);
            }
        }

        var instructionAttempted = false;
        try
        {
            // Recheck both files and Git after the first replacement. A changed
            // instruction or unrelated config edit must not be silently lost.
            if (config.WouldReplace)
            {
                afterConfigWriteForTests?.Invoke();
            }
            var afterConfig = PortableApplyPreview.Create(
                current.Snapshot.Repository, "codex", providerHome);
            if (!SameInstructionSnapshot(current.Snapshot, afterConfig.Snapshot) ||
                !Same(config.Canonical, afterConfig.CodexConfig!.Canonical) ||
                !Same(config.DesiredRuntime, afterConfig.CodexConfig.Runtime))
            {
                throw new InvalidOperationException(
                    "Codex apply inputs changed after the config step.");
            }

            if (current.WouldReplace)
            {
                instructionAttempted = true;
                PortableInstructionWriter.Apply(current.Snapshot);
            }

            var final = PortableApplyPreview.Create(
                current.Snapshot.Repository, "codex", providerHome);
            if (!string.Equals(current.Snapshot.Commit, final.Snapshot.Commit,
                    StringComparison.Ordinal) ||
                !Same(current.Snapshot.Target, final.Snapshot.Target) ||
                !Same(config.Canonical, final.CodexConfig!.Canonical) ||
                !Same(config.DesiredRuntime, final.CodexConfig.Runtime) ||
                final.WouldReplace || final.CodexConfig.WouldReplace)
            {
                throw new InvalidOperationException(
                    "Codex runtime changed during final apply verification.");
            }
        }
        catch (Exception ex) when (IsApplyFailure(ex))
        {
            if (config.WouldReplace)
            {
                throw new InvalidOperationException(
                    $"Codex apply stopped after the config step. Config at {config.RuntimePath} " +
                    $"may be updated; instructions at {current.RuntimePath} may not be. " +
                    "Inspect both runtime files before retrying; no automatic rollback was attempted.",
                    ex);
            }

            if (instructionAttempted)
            {
                throw new InvalidOperationException(
                    $"Codex instruction replacement stopped. Config at {config.RuntimePath} " +
                    $"was not changed by AEC; instructions at {current.RuntimePath} may be updated. " +
                    "Inspect runtime before retrying; no automatic rollback was attempted.",
                    ex);
            }

            throw;
        }

        return "applied";
    }

    private static bool MatchesReview(
        PortableApplyPreview reviewed,
        PortableApplyPreview current) =>
        SameInstructionSnapshot(reviewed.Snapshot, current.Snapshot) &&
        string.Equals(reviewed.RuntimePath, current.RuntimePath, StringComparison.Ordinal) &&
        Same(reviewed.DesiredRuntime, current.DesiredRuntime) &&
        reviewed.WouldReplace == current.WouldReplace &&
        string.Equals(reviewed.CodexConfig!.RuntimePath, current.CodexConfig!.RuntimePath,
            StringComparison.Ordinal) &&
        Same(reviewed.CodexConfig.Canonical, current.CodexConfig.Canonical) &&
        Same(reviewed.CodexConfig.Runtime, current.CodexConfig.Runtime) &&
        Same(reviewed.CodexConfig.DesiredRuntime, current.CodexConfig.DesiredRuntime) &&
        reviewed.CodexConfig.WouldReplace == current.CodexConfig.WouldReplace &&
        reviewed.CodexConfig.Inserted == current.CodexConfig.Inserted;

    private static bool SameInstructionSnapshot(
        PortableRuntimeSnapshot expected,
        PortableRuntimeSnapshot current) =>
        string.Equals(expected.Repository, current.Repository, StringComparison.Ordinal) &&
        string.Equals(expected.Platform, current.Platform, StringComparison.Ordinal) &&
        string.Equals(expected.Provider, current.Provider, StringComparison.Ordinal) &&
        string.Equals(expected.RuntimePath, current.RuntimePath, StringComparison.Ordinal) &&
        string.Equals(expected.Commit, current.Commit, StringComparison.Ordinal) &&
        expected.BlockVersion == current.BlockVersion &&
        Same(expected.Target, current.Target) &&
        Same(expected.Runtime, current.Runtime);

    private static bool Same(byte[]? expected, byte[]? current) =>
        expected is null ? current is null :
        current is not null && expected.AsSpan().SequenceEqual(current);

    private static bool IsApplyFailure(Exception error) =>
        error is IOException or InvalidOperationException or InvalidDataException;
}
