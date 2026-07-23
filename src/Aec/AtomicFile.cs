namespace Aec;

internal static class AtomicFile
{
    public static void WriteNew(string path, byte[] content)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(content);
        stream.Flush(flushToDisk: true);
    }

    public static void ReplaceIfUnchanged(
        string path,
        byte[]? expectedCurrent,
        byte[] content,
        string label = "Runtime target")
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException($"{label} has no parent directory: {path}");
        var temporaryPath = Path.Combine(directory, $".AGENTS.md.aec-{Guid.NewGuid():N}");

        try
        {
            // Keeping the temporary file beside the target makes the final move stay on one filesystem.
            WriteNew(temporaryPath, content);

            // Re-read after preparation so a newer target edit is not replaced by our stale snapshot.
            var current = AecApplication.ReadOptionalTextFile(path, label);
            if (!MatchesSnapshot(current, expectedCurrent))
            {
                throw new IOException(
                    $"{label} changed during the operation; no data was overwritten.");
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

    private static bool MatchesSnapshot(byte[]? current, byte[]? expected)
    {
        if (current is null || expected is null)
        {
            return current is null && expected is null;
        }

        return current.AsSpan().SequenceEqual(expected);
    }
}
