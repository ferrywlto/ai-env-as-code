namespace Aec;

// Keep managed runtime files outside the selected data repository.
internal static class RuntimePathGuard
{
    internal static void EnsureRuntimeOutsideRepository(string repository, string codexHome)
    {
        EnsureRuntimeOutsideRepository(
            repository,
            codexHome,
            "AGENTS.md",
            "Codex runtime target");
    }

    internal static void EnsureRuntimeOutsideRepository(
        string repository,
        string runtimeHome,
        string runtimeFileName,
        string label)
    {
        var runtimePath = Path.GetFullPath(Path.Combine(runtimeHome, runtimeFileName));
        if (IsPathInsideDirectory(repository, runtimePath))
        {
            throw new InvalidOperationException($"{label} must be outside the data repository.");
        }
    }

    internal static bool IsPathInsideDirectory(string directory, string path)
    {
        var directoryRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        var candidate = Path.GetFullPath(path);
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;

        if (string.Equals(candidate, directoryRoot, comparison))
        {
            return true;
        }

        // Filesystem roots already end in a separator; appending another would break containment.
        var directoryPrefix = Path.EndsInDirectorySeparator(directoryRoot)
            ? directoryRoot
            : $"{directoryRoot}{Path.DirectorySeparatorChar}";
        return candidate.StartsWith(directoryPrefix, comparison);
    }

}
