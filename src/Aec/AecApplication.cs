namespace Aec;

public static class AecApplication
{
    internal const int MaximumTextBytes = 1024 * 1024;
    internal const string SourceRelativePath = "environment/providers/codex/AGENTS.md";
    internal const string ConfigSourceRelativePath = "environment/providers/codex/config.toml";

    public static int Run(string[] args, TextWriter output, TextWriter error)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(error);

        try
        {
            if (args.Length == 0)
            {
                throw new ArgumentException("A command is required. Use `aec help` for usage.");
            }

            if (args.Length == 1 && args[0] is "help" or "--help" or "-h")
            {
                WriteUsage(output);
                return 0;
            }

            return args[0] switch
            {
                "version" => RunVersion(args, output),
                "skill" => RunSkill(args, output),
                "uninstall" => RunUninstall(ParseCodexHomeArguments(args, 1), output),
                "status" => RunStatus(ParseRepositoryArguments(args, "status"), output),
                "backup" => RunBackup(ParseRepositoryArguments(args, "backup"), output, error),
                "apply" => RunApply(ParseRepositoryArguments(args, "apply"), output, error),
                "init" => RunInit(ParseInitArguments(args), output, error),
                _ => throw new ArgumentException($"Unknown command: {args[0]}")
            };
        }
        catch (Exception exception)
        {
            error.WriteLine($"error: {exception.Message}");
            return 1;
        }
    }

    private static int RunStatus(RepositoryOptions options, TextWriter output)
    {
        var repository = RequireAbsolutePath(options.Repository, "--repo");
        var codexHome = ResolveCodexHome(options.CodexHome);

        EnsureNoLinksInManagedRoots(repository, codexHome);
        ApplyCommand.EnsureRuntimeOutsideRepository(repository, codexHome);
        EnsureRealDirectory(repository, "Repository");
        EnsureSourceDirectories(repository);
        EnsureRealDirectory(codexHome, "Codex home");

        var sourcePath = Path.Combine(repository, SourceRelativePath);
        var targetPath = Path.Combine(codexHome, "AGENTS.md");
        var desired = ReadRequiredTextFile(sourcePath, "Canonical source");
        var current = ReadOptionalTextFile(targetPath, "Runtime target");
        var agentsStatus = CompareExactBytes(desired, current);

        var canonicalConfigPath = Path.Combine(repository, ConfigSourceRelativePath);
        var canonicalConfig = ReadOptionalTextFile(canonicalConfigPath, "Canonical config")
            ?? throw new FileNotFoundException(
                "Canonical config does not exist: " + canonicalConfigPath +
                ". Create it with only a supported root `personality` value " +
                "(`none`, `friendly`, or `pragmatic`) before running status.");
        var desiredPersonality = CodexPersonalityConfig.ReadCanonical(
            canonicalConfig,
            canonicalConfigPath);

        var runtimeConfigPath = Path.Combine(codexHome, "config.toml");
        var runtimeConfig = ReadOptionalTextFile(runtimeConfigPath, "Runtime config");
        var currentPersonality = runtimeConfig is null
            ? null
            : CodexPersonalityConfig.ReadRuntime(runtimeConfig, runtimeConfigPath);
        var configStatus = currentPersonality is null
            ? "missing"
            : currentPersonality == desiredPersonality
                ? "in_sync"
                : "different";

        // Validate both artifacts before writing so an invalid config never leaves
        // callers with a misleading partial status report.
        output.WriteLine($"codex/AGENTS.md   {agentsStatus}");
        output.WriteLine($"codex/config.toml {configStatus}");
        return agentsStatus == "in_sync" && configStatus == "in_sync" ? 0 : 2;
    }

    private static int RunVersion(string[] args, TextWriter output)
    {
        if (args.Length > 1)
        {
            throw new ArgumentException($"Unknown argument: {args[1]}");
        }

        WriteVersion(output);
        return 0;
    }

    private static int RunSkill(string[] args, TextWriter output)
    {
        if (args.Length == 1)
        {
            throw new ArgumentException(
                "skill requires a subcommand. Use `aec help` for usage.");
        }

        if (args[1] != "upgrade")
        {
            throw new ArgumentException($"Unknown skill command: {args[1]}");
        }

        var options = ParseCodexHomeArguments(args, 2);
        var codexHome = ResolveCodexHome(options.CodexHome);
        var changed = AecSkillInstaller.Upgrade(codexHome);
        output.WriteLine(changed ? "upgraded" : "unchanged");
        return 0;
    }

    private static int RunUninstall(CodexHomeOptions options, TextWriter output)
    {
        var codexHome = ResolveCodexHome(options.CodexHome);
        return UninstallCommand.Run(codexHome, output);
    }

    private static string CompareExactBytes(byte[] desired, byte[]? current)
    {
        if (current is null)
        {
            return "missing";
        }

        return desired.AsSpan().SequenceEqual(current) ? "in_sync" : "different";
    }

    private static int RunBackup(
        RepositoryOptions options,
        TextWriter output,
        TextWriter warning)
    {
        var repository = RequireAbsolutePath(options.Repository, "--repo");
        var codexHome = ResolveCodexHome(options.CodexHome);

        EnsureNoLinksInManagedRoots(repository, codexHome);
        ApplyCommand.EnsureRuntimeOutsideRepository(repository, codexHome);
        EnsureRealDirectory(repository, "Repository");
        EnsureSourceDirectories(repository);
        EnsureRealDirectory(codexHome, "Codex home");

        return BackupCommand.Run(repository, codexHome, output, warning);
    }

    private static int RunInit(
        InitOptions options,
        TextWriter output,
        TextWriter warning)
    {
        var repository = RequireAbsolutePath(options.Repository, "--repo");

        // Provider initialization is repository-only and must never resolve or inspect runtime state.
        if (options.Provider == "chatgpt")
        {
            return ChatGptInitCommand.Run(repository, output);
        }

        var codexHome = ResolveCodexHome(options.CodexHome);
        return InitCommand.Run(
            repository,
            codexHome,
            options.ForcePathChange,
            output,
            warning);
    }

    private static int RunApply(
        RepositoryOptions options,
        TextWriter output,
        TextWriter warning)
    {
        var repository = RequireAbsolutePath(options.Repository, "--repo");
        var codexHome = ResolveCodexHome(options.CodexHome);

        EnsureNoLinksInManagedRoots(repository, codexHome);
        EnsureRealDirectory(repository, "Repository");
        EnsureSourceDirectories(repository);
        EnsureRealDirectory(codexHome, "Codex home");

        return ApplyCommand.Run(repository, codexHome, output, warning);
    }

    private static RepositoryOptions ParseRepositoryArguments(string[] args, string command)
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

            var value = ReadRequiredOptionValue(args, ref index, option);
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
            throw new ArgumentException(
                $"{command} requires --repo with the source-of-truth data repository.");
        }

        return new RepositoryOptions(repository, codexHome);
    }

    private static InitOptions ParseInitArguments(string[] args)
    {
        string? repository = null;
        string? codexHome = null;
        string? provider = null;
        var forcePathChange = false;

        for (var index = 1; index < args.Length; index++)
        {
            var argument = args[index];
            if (argument == "--repo")
            {
                if (repository is not null)
                {
                    throw new ArgumentException("--repo may be specified only once.");
                }

                repository = ReadRequiredOptionValue(args, ref index, argument);
                continue;
            }

            if (argument.StartsWith("--provider=", StringComparison.Ordinal))
            {
                if (provider is not null)
                {
                    throw new ArgumentException("--provider may be specified only once.");
                }

                provider = argument["--provider=".Length..];
                if (provider != "chatgpt")
                {
                    throw new ArgumentException($"Unsupported provider: {provider}");
                }

                continue;
            }

            if (argument == "--codex-home")
            {
                if (codexHome is not null)
                {
                    throw new ArgumentException("--codex-home may be specified only once.");
                }

                codexHome = ReadRequiredOptionValue(args, ref index, argument);
                continue;
            }

            if (argument == "--force-path-change")
            {
                if (forcePathChange)
                {
                    throw new ArgumentException(
                        "--force-path-change may be specified only once.");
                }

                forcePathChange = true;
                continue;
            }

            if (argument.StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Unknown argument: {argument}");
            }

            throw new ArgumentException($"Unknown argument: {argument}");
        }

        if (repository is null)
        {
            throw new ArgumentException(
                "init requires --repo with the source-of-truth data repository.");
        }

        if (provider is not null && codexHome is not null)
        {
            throw new ArgumentException("--codex-home is not valid with --provider=chatgpt.");
        }

        if (provider is not null && forcePathChange)
        {
            throw new ArgumentException(
                "--force-path-change is not valid with --provider=chatgpt.");
        }

        return new InitOptions(
            repository,
            codexHome,
            provider,
            forcePathChange);
    }

    private static CodexHomeOptions ParseCodexHomeArguments(string[] args, int startIndex)
    {
        string? codexHome = null;

        for (var index = startIndex; index < args.Length; index++)
        {
            var argument = args[index];
            if (argument != "--codex-home")
            {
                throw new ArgumentException($"Unknown argument: {argument}");
            }

            if (codexHome is not null)
            {
                throw new ArgumentException("--codex-home may be specified only once.");
            }

            codexHome = ReadRequiredOptionValue(args, ref index, argument);
        }

        return new CodexHomeOptions(codexHome);
    }

    private static string ReadRequiredOptionValue(
        string[] args,
        ref int optionIndex,
        string option)
    {
        if (optionIndex + 1 >= args.Length ||
            args[optionIndex + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"{option} requires a value.");
        }

        return args[++optionIndex];
    }

    internal static string ResolveCodexHome(string? explicitPath)
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

    internal static string RequireAbsolutePath(string path, string label)
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

    private static void EnsureNoLinksInManagedRoots(string repository, string codexHome)
    {
        EnsureNoLinksInExistingPath(repository, "Repository path");
        EnsureNoLinksInExistingPath(codexHome, "Codex home path");
    }

    internal static void EnsureSourceDirectories(string repository)
    {
        var environment = Path.Combine(repository, "environment");
        var providers = Path.Combine(environment, "providers");
        var codex = Path.Combine(providers, "codex");

        EnsureRealDirectory(environment, "Source directory");
        EnsureRealDirectory(providers, "Source directory");
        EnsureRealDirectory(codex, "Source directory");
    }

    internal static void EnsureRealDirectory(string path, string label)
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

    internal static void EnsureNoLinksInExistingPath(string path, string label)
    {
        var root = Path.GetPathRoot(path)
            ?? throw new InvalidOperationException($"{label} has no filesystem root: {path}");
        var current = root;
        var relative = Path.GetRelativePath(root, path);

        // Leaf metadata does not expose a linked ancestor, so inspect every existing component.
        foreach (var part in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, part);
            var directory = new DirectoryInfo(current);
            directory.Refresh();

            if (directory.LinkTarget is not null)
            {
                throw new InvalidOperationException($"{label} must not contain a symbolic link: {current}");
            }

            if (directory.Exists)
            {
                if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException($"{label} must not contain a reparse point: {current}");
                }

                continue;
            }

            var file = new FileInfo(current);
            file.Refresh();
            if (file.LinkTarget is not null)
            {
                throw new InvalidOperationException($"{label} must not contain a symbolic link: {current}");
            }

            if (file.Exists)
            {
                throw new InvalidOperationException($"{label} component is not a directory: {current}");
            }

            break;
        }
    }

    internal static byte[] ReadRequiredTextFile(string path, string label)
    {
        return ReadTextFile(path, label)
            ?? throw new FileNotFoundException($"{label} does not exist: {path}");
    }

    internal static byte[]? ReadOptionalTextFile(string path, string label)
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
        const string usage = """
            Usage:
              aec help
              aec version
              aec skill upgrade [--codex-home ABSOLUTE_PATH]
              aec uninstall [--codex-home ABSOLUTE_PATH]
              aec init --repo ABSOLUTE_PATH [--codex-home ABSOLUTE_PATH] [--force-path-change]
              aec init --repo ABSOLUTE_PATH --provider=chatgpt
              aec status --repo ABSOLUTE_PATH [--codex-home ABSOLUTE_PATH]
              aec backup --repo ABSOLUTE_PATH [--codex-home ABSOLUTE_PATH]
              aec apply --repo ABSOLUTE_PATH [--codex-home ABSOLUTE_PATH]
            """;

        output.WriteLine(usage.ReplaceLineEndings(output.NewLine));
    }

    private static void WriteVersion(TextWriter output)
    {
        var version = typeof(AecApplication).Assembly.GetName().Version
            ?? throw new InvalidOperationException("Application version could not be resolved.");
        output.WriteLine(version.ToString(fieldCount: 3));
    }

    private sealed record RepositoryOptions(string Repository, string? CodexHome);

    private sealed record CodexHomeOptions(string? CodexHome);

    private sealed record InitOptions(
        string Repository,
        string? CodexHome,
        string? Provider,
        bool ForcePathChange);
}
