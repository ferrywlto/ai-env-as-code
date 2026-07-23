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
        byte[] content)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException($"Runtime target has no parent directory: {path}");
        var temporaryPath = Path.Combine(directory, $".AGENTS.md.aec-{Guid.NewGuid():N}");

        try
        {
            // Keeping the temporary file beside the target makes the final move stay on one filesystem.
            WriteNew(temporaryPath, content);

            // Re-read after preparation so a newer runtime edit is not replaced by our stale snapshot.
            var current = AecApplication.ReadOptionalTextFile(path, "Runtime target");
            if (!MatchesSnapshot(current, expectedCurrent))
            {
                throw new IOException(
                    "Runtime target changed during the operation; no runtime data was overwritten.");
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

        var written = AecApplication.ReadRequiredTextFile(path, "Runtime target");
        if (!written.AsSpan().SequenceEqual(content))
        {
            throw new IOException("Runtime target verification failed after writing.");
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
