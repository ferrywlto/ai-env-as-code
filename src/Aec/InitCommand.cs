namespace Aec;

internal static class InitCommand
{
    public static int Run(string directoryPath, string codexHome, TextWriter output)
    {
        ValidateTarget(directoryPath);
        AecApplication.EnsureRealDirectory(codexHome, "Codex home");

        var runtimePath = Path.Combine(codexHome, "AGENTS.md");
        var runtime = AecApplication.ReadOptionalTextFile(runtimePath, "Runtime target") ?? [];
        var merged = AecInstructionBlock.Merge(runtime);

        EnsureGitIsAvailable();

        Directory.CreateDirectory(directoryPath);
        GitProcess.RunRequired(
            directoryPath,
            "Git init failed",
            "init",
            "--quiet",
            "--template=",
            "--initial-branch=main");
        EnsureGitDirectory(directoryPath);

        var sourceDirectory = Path.Combine(directoryPath, "environment", "providers", "codex");
        Directory.CreateDirectory(sourceDirectory);
        var sourcePath = Path.Combine(sourceDirectory, "AGENTS.md");
        WriteNewFile(sourcePath, merged);

        if (!runtime.AsSpan().SequenceEqual(merged))
        {
            ReplaceFileIfUnchanged(runtimePath, runtime, merged);
        }

        VerifyContent(sourcePath, merged, "Canonical source");
        VerifyContent(runtimePath, merged, "Runtime target");

        output.WriteLine("initialized");
        return 0;
    }

    private static void WriteNewFile(string path, byte[] content)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        stream.Write(content);
        stream.Flush(flushToDisk: true);
    }

    internal static void ReplaceFileIfUnchanged(
        string path,
        byte[] expectedCurrent,
        byte[] content)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException($"Runtime target has no parent directory: {path}");
        var temporaryPath = Path.Combine(directory, $".AGENTS.md.aec-init-{Guid.NewGuid():N}");

        try
        {
            WriteNewFile(temporaryPath, content);
            var current = AecApplication.ReadOptionalTextFile(path, "Runtime target") ?? [];
            if (!current.AsSpan().SequenceEqual(expectedCurrent))
            {
                throw new IOException(
                    "Runtime target changed during initialization; no runtime data was overwritten.");
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void VerifyContent(string path, byte[] expected, string label)
    {
        var actual = AecApplication.ReadRequiredTextFile(path, label);
        if (!actual.AsSpan().SequenceEqual(expected))
        {
            throw new IOException($"{label} verification failed after initialization.");
        }
    }

    private static void ValidateTarget(string path)
    {
        EnsureNoSymbolicLinksInExistingPath(path);
        var directory = new DirectoryInfo(path);
        directory.Refresh();

        if (directory.LinkTarget is not null)
        {
            throw new InvalidOperationException($"Target directory must not be a symbolic link: {path}");
        }

        if (directory.Exists)
        {
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException($"Target directory must be a real directory: {path}");
            }

            if (directory.EnumerateFileSystemInfos().Any())
            {
                throw new InvalidOperationException($"Target directory is not empty: {path}");
            }

            return;
        }

        var file = new FileInfo(path);
        file.Refresh();
        if (file.LinkTarget is not null)
        {
            throw new InvalidOperationException($"Target directory must not be a symbolic link: {path}");
        }

        if (file.Exists)
        {
            throw new InvalidOperationException($"Target path is not a directory: {path}");
        }
    }

    private static void EnsureNoSymbolicLinksInExistingPath(string path)
    {
        var root = Path.GetPathRoot(path)
            ?? throw new InvalidOperationException($"Target directory has no filesystem root: {path}");
        var current = root;
        var relative = Path.GetRelativePath(root, path);

        foreach (var part in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            var directory = new DirectoryInfo(current);
            directory.Refresh();

            if (directory.LinkTarget is not null)
            {
                throw new InvalidOperationException(
                    $"Target directory path must not contain a symbolic link: {current}");
            }

            if (directory.Exists)
            {
                if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException(
                        $"Target directory path must not contain a reparse point: {current}");
                }

                continue;
            }

            var file = new FileInfo(current);
            file.Refresh();
            if (file.LinkTarget is not null)
            {
                throw new InvalidOperationException(
                    $"Target directory path must not contain a symbolic link: {current}");
            }

            if (file.Exists)
            {
                throw new InvalidOperationException($"Path component is not a directory: {current}");
            }

            break;
        }
    }

    private static void EnsureGitIsAvailable()
    {
        try
        {
            GitProcess.RunRequired(null, "Git is not available", "--version");
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            throw new InvalidOperationException("Git could not be started.", exception);
        }
    }

    private static void EnsureGitDirectory(string repository)
    {
        var path = Path.Combine(repository, ".git");
        var directory = new DirectoryInfo(path);
        directory.Refresh();

        if (!directory.Exists || directory.LinkTarget is not null ||
            (directory.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException($"Git did not create a valid repository: {repository}");
        }
    }

}
