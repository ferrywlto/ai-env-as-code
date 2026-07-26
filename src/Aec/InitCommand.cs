namespace Aec;

internal static class InitCommand
{
    public static int Run(string directoryPath, string codexHome, TextWriter output)
    {
        ValidateTarget(directoryPath);
        AecApplication.EnsureNoLinksInExistingPath(codexHome, "Codex home path");
        AecApplication.EnsureRealDirectory(codexHome, "Codex home");

        var runtimePath = Path.Combine(codexHome, "AGENTS.md");
        var runtime = AecApplication.ReadOptionalTextFile(runtimePath, "Runtime target");
        var merged = AecInstructionBlock.Merge(runtime ?? []);

        EnsureGitIsAvailable();
        AecSkillInstaller.Install(codexHome);

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
        AtomicFile.WriteNew(sourcePath, merged);

        if (runtime is null || !runtime.AsSpan().SequenceEqual(merged))
        {
            AtomicFile.ReplaceIfUnchanged(runtimePath, runtime, merged);
        }

        VerifyContent(sourcePath, merged, "Canonical source");
        VerifyContent(runtimePath, merged, "Runtime target");

        output.WriteLine("initialized");
        return 0;
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
        AecApplication.EnsureNoLinksInExistingPath(path, "Target directory path");
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
