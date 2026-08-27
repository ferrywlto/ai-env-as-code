namespace Aec;

internal static class AtomicFile
{
    public static void WriteNew(
        string path,
        byte[] content,
        UnixFileMode? unixCreateMode = null)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None
        };
        if (!OperatingSystem.IsWindows() && unixCreateMode is not null)
        {
            options.UnixCreateMode = unixCreateMode.Value;
        }

        using (var stream = new FileStream(path, options))
        {
            stream.Write(content);
            stream.Flush(flushToDisk: true);
        }

        if (!OperatingSystem.IsWindows() && unixCreateMode is not null)
        {
            // UnixCreateMode is filtered by umask, so set and verify the exact
            // intended bits before the temporary inode replaces the target.
            File.SetUnixFileMode(path, unixCreateMode.Value);
            if (File.GetUnixFileMode(path) != unixCreateMode.Value)
            {
                throw new IOException($"Temporary file permissions could not be verified: {path}");
            }
        }
    }

    public static void ReplaceIfUnchanged(
        string path,
        byte[]? expectedCurrent,
        byte[] content,
        string label = "Runtime target",
        UnixFileMode? missingFileMode = null)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException($"{label} has no parent directory: {path}");
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(path)}.aec-{Guid.NewGuid():N}");
        var expectedMode = ResolveUnixMode(path, expectedCurrent, missingFileMode);

        try
        {
            // Keeping the temporary file beside the target makes the final move stay on one filesystem.
            WriteNew(temporaryPath, content, expectedMode);

            // Re-read after preparation so a newer target edit is not replaced by our stale snapshot.
            var current = AecApplication.ReadOptionalTextFile(path, label);
            if (!MatchesSnapshot(current, expectedCurrent))
            {
                throw new IOException(
                    $"{label} changed during the operation; no data was overwritten.");
            }

            if (!OperatingSystem.IsWindows() &&
                expectedCurrent is not null &&
                expectedMode is not null &&
                File.GetUnixFileMode(path) != expectedMode.Value)
            {
                throw new IOException(
                    $"{label} permissions changed during the operation; no data was overwritten.");
            }

            // A missing snapshot uses a non-overwriting move, so a last-moment creation fails safely.
            File.Move(temporaryPath, path, overwrite: expectedCurrent is not null);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        var written = AecApplication.ReadRequiredTextFile(path, label);
        if (!written.AsSpan().SequenceEqual(content))
        {
            throw new IOException($"{label} verification failed after writing.");
        }
    }

    public static void DeleteIfUnchanged(
        string path,
        byte[] expectedCurrent,
        string label)
    {
        UnixFileMode? expectedMode = OperatingSystem.IsWindows()
            ? null
            : File.GetUnixFileMode(path);

        // Re-read immediately before deletion so a user edit made after preflight
        // is rejected instead of being mistaken for the recognized managed file.
        var current = AecApplication.ReadRequiredTextFile(path, label);
        if (!current.AsSpan().SequenceEqual(expectedCurrent))
        {
            throw new IOException(
                $"{label} changed during the operation; no data was deleted.");
        }

        if (!OperatingSystem.IsWindows() &&
            expectedMode is not null &&
            File.GetUnixFileMode(path) != expectedMode.Value)
        {
            throw new IOException(
                $"{label} permissions changed during the operation; no data was deleted.");
        }

        File.Delete(path);
        if (File.Exists(path) || Directory.Exists(path))
        {
            throw new IOException($"{label} verification failed after deletion: {path}");
        }
    }

    private static UnixFileMode? ResolveUnixMode(
        string path,
        byte[]? expectedCurrent,
        UnixFileMode? missingFileMode)
    {
        if (OperatingSystem.IsWindows())
        {
            return null;
        }

        // Existing personal configuration may contain secrets, so retain its
        // permission bits instead of inheriting the process's broader default mode.
        return expectedCurrent is null
            ? missingFileMode
            : File.GetUnixFileMode(path);
    }

    private static bool MatchesSnapshot(byte[]? current, byte[]? expected)
    {
        if (current is null || expected is null)
        {
            return current is null && expected is null;
        }

        return current.AsSpan().SequenceEqual(expected);
    }
}
