namespace Aec;

public static class AecApplication
{
    private const int MaximumTextBytes = 1024 * 1024;
    private const string SourceRelativePath = "environment/providers/codex/AGENTS.md";

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        try
        {
            if (args.Length == 0)
            {
                throw new ArgumentException("A command is required. Use --help for usage.");
            }

            if (args.Length == 1 && args[0] is "help" or "--help" or "-h")
            {
                WriteUsage(output);
                return 0;
            }

            return args[0] switch
            {
                "status" => RunStatus(ParseStatusArguments(args), output),
                "init" => InitCommand.Run(ParseInitDirectory(args), output),
                _ => throw new ArgumentException($"Unknown command: {args[0]}")
            };
        }
        catch (Exception exception)
        {
            error.WriteLine($"error: {exception.Message}");
            return 1;
        }
    }

    private static int RunStatus(StatusOptions options, TextWriter output)
    {
        var repository = RequireAbsolutePath(options.Repository, "--repo");
        var codexHome = ResolveCodexHome(options.CodexHome);

        EnsureRealDirectory(repository, "Repository");
        EnsureSourceDirectories(repository);
        EnsureRealDirectory(codexHome, "Codex home");

        var sourcePath = Path.Combine(repository, SourceRelativePath);
        var targetPath = Path.Combine(codexHome, "AGENTS.md");
        var desired = ReadRequiredTextFile(sourcePath, "Canonical source");
        var current = ReadOptionalTextFile(targetPath, "Runtime target");

        if (current is null)
        {
            output.WriteLine("missing");
            return 2;
        }

        if (desired.AsSpan().SequenceEqual(current))
        {
            output.WriteLine("in_sync");
            return 0;
        }

        output.WriteLine("different");
        return 2;
    }

    private static StatusOptions ParseStatusArguments(string[] args)
    {
        string? repository = null;
        string? codexHome = null;

        for (var index = 1; index < args.Length; index++)
        {
            var option = args[index];
            if (option is not ("--repo" or "--codex-home"))
            {
                throw new ArgumentException($"Unknown argument: {option}");
            }

            if (index + 1 >= args.Length || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"{option} requires a value.");
            }

            var value = args[++index];
            if (option == "--repo")
            {
                if (repository is not null)
                {
                    throw new ArgumentException("--repo may be specified only once.");
                }

                repository = value;
            }
            else
            {
                if (codexHome is not null)
                {
                    throw new ArgumentException("--codex-home may be specified only once.");
                }

                codexHome = value;
            }
        }

        if (repository is null)
        {
            throw new ArgumentException("status requires --repo with the source-of-truth data repository.");
        }

        return new StatusOptions(repository, codexHome);
    }

    private static string ParseInitDirectory(string[] args)
    {
        if (args.Length > 2)
        {
            throw new ArgumentException("init accepts at most one directory.");
        }

        var currentDirectory = Environment.CurrentDirectory;
        if (args.Length == 1)
        {
            return currentDirectory;
        }

        var directory = args[1];
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("init requires a non-empty directory when one is supplied.");
        }

        return Path.GetFullPath(directory, currentDirectory);
    }

    private static string ResolveCodexHome(string? explicitPath)
    {
        if (explicitPath is not null)
        {
            return RequireAbsolutePath(explicitPath, "--codex-home");
        }

        var environmentPath = Environment.GetEnvironmentVariable("CODEX_HOME");
        if (!string.IsNullOrWhiteSpace(environmentPath))
        {
            return RequireAbsolutePath(environmentPath, "CODEX_HOME");
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile))
        {
            throw new InvalidOperationException("The user profile directory could not be resolved.");
        }

        return Path.Combine(userProfile, ".codex");
    }

    private static string RequireAbsolutePath(string path, string label)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException($"{label} requires a non-empty path.");
        }

        if (!Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException($"{label} must be an absolute path.");
        }

        return Path.GetFullPath(path);
    }

    private static void EnsureSourceDirectories(string repository)
    {
        var environment = Path.Combine(repository, "environment");
        var providers = Path.Combine(environment, "providers");
        var codex = Path.Combine(providers, "codex");

        EnsureRealDirectory(environment, "Source directory");
        EnsureRealDirectory(providers, "Source directory");
        EnsureRealDirectory(codex, "Source directory");
    }

    private static void EnsureRealDirectory(string path, string label)
    {
        var directory = new DirectoryInfo(path);
        directory.Refresh();

        if (directory.LinkTarget is not null)
        {
            throw new InvalidOperationException($"{label} must not be a symbolic link: {path}");
        }

        if (!directory.Exists)
        {
            throw new DirectoryNotFoundException($"{label} does not exist: {path}");
        }

        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException($"{label} must be a real directory: {path}");
        }
    }

    private static byte[] ReadRequiredTextFile(string path, string label)
    {
        return ReadTextFile(path, label)
            ?? throw new FileNotFoundException($"{label} does not exist: {path}");
    }

    private static byte[]? ReadOptionalTextFile(string path, string label)
    {
        return ReadTextFile(path, label);
    }

    private static byte[]? ReadTextFile(string path, string label)
    {
        var file = new FileInfo(path);
        file.Refresh();

        if (file.LinkTarget is not null)
        {
            throw new InvalidOperationException($"{label} must not be a symbolic link: {path}");
        }

        if (!file.Exists)
        {
            if (Directory.Exists(path))
            {
                throw new InvalidOperationException($"{label} must be a regular file: {path}");
            }

            return null;
        }

        if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidOperationException($"{label} must be a real file: {path}");
        }

        if (file.Length > MaximumTextBytes)
        {
            throw new InvalidDataException($"{label} exceeds 1 MiB: {path}");
        }

        if (file.Length == 0)
        {
            return [];
        }

        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.SequentialScan);
        using var content = new MemoryStream((int)file.Length);
        var buffer = new byte[4096];

        while (true)
        {
            var remaining = MaximumTextBytes + 1 - (int)content.Length;
            var read = stream.Read(buffer, 0, Math.Min(buffer.Length, remaining));
            if (read == 0)
            {
                break;
            }

            content.Write(buffer, 0, read);
            if (content.Length > MaximumTextBytes)
            {
                throw new InvalidDataException($"{label} exceeds 1 MiB: {path}");
            }
        }

        return content.ToArray();
    }

    private static void WriteUsage(TextWriter output)
    {
        output.WriteLine("Usage:");
        output.WriteLine("  aec init [directory]");
        output.WriteLine("  aec status --repo ABSOLUTE_PATH [--codex-home ABSOLUTE_PATH]");
    }

    private sealed record StatusOptions(string Repository, string? CodexHome);
}
