using System.Diagnostics;

namespace Aec;

internal static class InitCommand
{
    private static readonly string[] GitLocationVariables =
    [
        "GIT_DIR",
        "GIT_WORK_TREE",
        "GIT_COMMON_DIR",
        "GIT_OBJECT_DIRECTORY",
        "GIT_INDEX_FILE",
        "GIT_ALTERNATE_OBJECT_DIRECTORIES",
        "GIT_TEMPLATE_DIR"
    ];

    public static int Run(string directoryPath, TextWriter output)
    {
        ValidateTarget(directoryPath);
        EnsureGitIsAvailable();

        Directory.CreateDirectory(directoryPath);
        RunGit(
            directoryPath,
            ["init", "--quiet", "--template=", "--initial-branch=main"],
            "Git init failed");
        EnsureGitDirectory(directoryPath);

        var sourceDirectory = Path.Combine(directoryPath, "environment", "providers", "codex");
        Directory.CreateDirectory(sourceDirectory);
        var sourcePath = Path.Combine(sourceDirectory, "AGENTS.md");
        using (new FileStream(sourcePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
        }

        output.WriteLine("initialized");
        return 0;
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
            RunGit(null, ["--version"], "Git is not available");
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

    private static void RunGit(string? workingDirectory, string[] arguments, string failureMessage)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory ?? string.Empty,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var variable in GitLocationVariables)
        {
            startInfo.Environment.Remove(variable);
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Git could not be started.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        var output = standardOutput.GetAwaiter().GetResult();
        var error = standardError.GetAwaiter().GetResult();

        if (process.ExitCode == 0)
        {
            return;
        }

        var details = string.IsNullOrWhiteSpace(error) ? output : error;
        details = details.ReplaceLineEndings(" ").Trim();
        var suffix = details.Length == 0 ? string.Empty : $": {details}";
        throw new InvalidOperationException($"{failureMessage} with exit code {process.ExitCode}{suffix}");
    }
}
