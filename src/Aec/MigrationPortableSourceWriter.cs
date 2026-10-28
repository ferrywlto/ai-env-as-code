namespace Aec;

// Combine portable proposals against one committed baseline. Only authored
// working files change: target rendering, staging and committing are later steps.
internal static class MigrationPortableSourceWriter
{
    public static IReadOnlyList<string> Write(MigrationPortableCapturePreview reviewed) =>
        MigrationPortableCapturePlan.Prepare(reviewed).WriteSources();
}

internal sealed class MigrationPortableSourceWriteException : IOException
{
    public MigrationPortableSourceWriteException(IReadOnlyList<string> writtenPaths, string? attemptedPath, Exception inner)
        : base($"Portable authored capture stopped. Completed replacements: {(writtenPaths.Count == 0 ? "none" : string.Join(", ", writtenPaths))}. " +
            (attemptedPath is null ? "" : $"Attempted path may also have changed: {attemptedPath}. ") +
            "Inspect working files before retrying. No rollback, rendering, staging, commit or runtime apply was attempted.", inner)
    {
        WrittenPaths = writtenPaths;
        AttemptedPath = attemptedPath;
    }

    public IReadOnlyList<string> WrittenPaths { get; }
    public string? AttemptedPath { get; }
}
