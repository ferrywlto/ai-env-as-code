using System.Reflection;

namespace Aec;

public static class AecApplication
{
    internal const int MaximumTextBytes = 1024 * 1024;
    internal const string SourceRelativePath = "environment/providers/codex/AGENTS.md";
    internal const string ConfigSourceRelativePath = "environment/providers/codex/config.toml";
    internal const string CopilotSourceRelativePath =
        "environment/providers/copilot/copilot-instructions.md";

    public static int Run(string[] args, TextWriter output, TextWriter error) =>
        Run(args, Console.In, output, error);

    internal static int Run(string[] args, TextReader input, TextWriter output, TextWriter error,
        CancellationToken cancellationToken = default)
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
                "migrate" => RunMigration(args, input, output, error, cancellationToken),
                "render" => RenderCommand.Run(
                    RequireAbsolutePath(ParseRenderRepository(args), "--repo"), output),
                AecIdentifiers.Codex or AecIdentifiers.Copilot or AecIdentifiers.ChatGpt =>
                    RunProvider(args, output, error),
                "init" or "status" or "backup" or "apply" or "uninstall" or "skill" =>
                    throw LegacyCommandError(args),
                _ => throw new ArgumentException($"Unknown command: {args[0]}")
            };
        }
        catch (Exception exception)
        {
            error.WriteLine($"error: {exception.Message}");
            return 1;
        }
    }

    private static int RunMigration(string[] args, TextReader input, TextWriter output, TextWriter error,
        CancellationToken cancellationToken)
    {
        if (args.Length != 4 || args[2] != "--repo")
            throw new ArgumentException("Usage: aec migrate VERSION --repo ABSOLUTE_PATH");

        var repository = RequireAbsolutePath(args[3], "--repo");
        using var filterScope = GitProcess.SuppressMigrationFilters();
        output.WriteLine("Close affected harness sessions and pause other writers to this repository and its managed runtime files until migration finishes. Use a separate terminal and a plain editor. Concurrent changes can leave migration partially complete.");
        output.Flush();

        var preparation = MigrationPreparation.Run(repository, args[1], input, output, cancellationToken);
        if (preparation is not null && !cancellationToken.IsCancellationRequested)
        {
            var scaffold = MigrationScaffoldCoordinator.CaptureAndScaffold(preparation);
            var result = MigrationPostDoneCoordinator.WaitAndComplete(scaffold, input, output, cancellationToken);
            if (result is not null)
            {
                output.WriteLine($"Migration completed at {result.Commit}.");
                return 0;
            }
        }

        error.WriteLine("Migration stopped before completion. Inspect the checkout before starting again.");
        return 1;
    }

    private static int RunProvider(string[] args, TextWriter output, TextWriter warning)
    {
        var provider = args[0];
        if (args.Length < 2)
        {
            throw new ArgumentException($"A {provider} command is required. Use `aec help` for usage.");
        }

        var command = args[1];
        if (provider == AecIdentifiers.ChatGpt)
        {
            if (command != "init")
            {
                throw new ArgumentException($"Unknown {provider} command: {command}");
            }

            var options = ParseProviderRepositoryArguments(args);
            return ChatGptInitCommand.Run(RequireAbsolutePath(options.Repository, "--repo"), output);
        }

        return command switch
        {
            "init" => RunInit(ParseProviderRepositoryArguments(args), output, warning),
            "status" => RunStatus(ParseProviderRepositoryArguments(args), output),
            "backup" => RunBackup(ParseProviderRepositoryArguments(args), output, warning),
            "apply" => RunApply(ParseProviderRepositoryArguments(args), output, warning),
            "upgrade" when provider == AecIdentifiers.Codex =>
                RunCodexUpgrade(ParseCodexHomeArguments(args), output),
            "uninstall" when provider == AecIdentifiers.Codex =>
                UninstallCommand.Run(ResolveCodexHome(ParseCodexHomeArguments(args)), output),
            _ => throw new ArgumentException($"Unknown {provider} command: {command}")
        };
    }

    // Recognize old forms only to provide a diagnostic. They never reach a workflow.
    private static ArgumentException LegacyCommandError(string[] args)
    {
        var replacement = args[0] switch
        {
            "uninstall" => "codex uninstall [--home PATH]",
            "skill" when args.Length > 1 && args[1] == "upgrade" => "codex upgrade [--home PATH]",
            "init" or "status" or "backup" or "apply" => LegacyProviderReplacement(args),
            _ => null
        };

        return new ArgumentException(replacement is null
            ? "Legacy command syntax. Use `aec help` for usage."
            : $"Legacy command syntax. Use `aec {replacement}`.");
    }

    private static string? LegacyProviderReplacement(string[] args)
    {
        var command = args[0];
        var providers = args.Skip(1).Where(arg => arg.StartsWith("--provider", StringComparison.Ordinal)).ToArray();
        var provider = providers.Length switch
        {
            0 => AecIdentifiers.Codex,
            1 when providers[0] == "--provider=copilot" => AecIdentifiers.Copilot,
            1 when providers[0] == "--provider=chatgpt" && command == "init" => AecIdentifiers.ChatGpt,
            _ => null
        };
        if (provider is null ||
            (provider != AecIdentifiers.Codex && args.Contains("--codex-home")) ||
            (provider != AecIdentifiers.Copilot && args.Contains("--copilot-home")))
        {
            return null;
        }

        var replacement = $"{provider} {command} --repo PATH";
        if (args.Contains("--codex-home") || args.Contains("--copilot-home"))
        {
            replacement += " --home PATH";
        }

        if (command == "init")
        {
            if (args.Contains("--enroll-shared"))
            {
                replacement += " --enroll-shared";
            }

            if (args.Contains("--force-path-change"))
            {
                replacement += " --force-path-change";
            }
        }

        return replacement;
    }

    private static int RunStatus(ProviderRepositoryOptions options, TextWriter output)
    {
        var repository = RequireAbsolutePath(options.Repository, "--repo");
        if (options.Provider == AecIdentifiers.Copilot)
        {
            var copilotHome = ResolveCopilotHome(options.Home);
            var status = PortableEnvironmentStatus.Inspect(
                repository, AecIdentifiers.Copilot, copilotHome);
            output.WriteLine($"copilot/copilot-instructions.md {status.Instructions}");
            return status.InSync ? 0 : 2;
        }

        var codexHome = ResolveCodexHome(options.Home);
        var codexStatus = PortableEnvironmentStatus.Inspect(
            repository, AecIdentifiers.Codex, codexHome);
        output.WriteLine($"codex/AGENTS.md   {codexStatus.Instructions}");
        output.WriteLine($"codex/config.toml {codexStatus.Config}");
        return codexStatus.InSync ? 0 : 2;
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

    private static int RunCodexUpgrade(string? home, TextWriter output)
    {
        var changed = AecSkillInstaller.Upgrade(ResolveCodexHome(home));
        output.WriteLine(changed ? "upgraded" : "unchanged");
        return 0;
    }

    private static int RunBackup(
        ProviderRepositoryOptions options,
        TextWriter output,
        TextWriter warning)
    {
        var repository = RequireAbsolutePath(options.Repository, "--repo");

        if (options.Provider == AecIdentifiers.Copilot)
        {
            var copilotHome = ResolveCopilotHome(options.Home);
            var copilotPreview = PortableCopilotBackupPreview.Create(repository, copilotHome);
            PortableBackupCommitter.RequireLocalBranch(repository);
            if (copilotPreview.Instructions.Section is null)
            {
                output.WriteLine("unchanged");
                return 0;
            }

            var copilotCommit = PortableCopilotBackupWriter.WriteAndCommit(copilotPreview);
            output.WriteLine($"committed {copilotCommit}");
            return 0;
        }

        var codexHome = ResolveCodexHome(options.Home);
        PortableCodexBackupPreview preview;
        try
        {
            preview = PortableCodexBackupPreview.Create(repository, codexHome);
        }
        catch (MissingRuntimePersonalityException missing)
        {
            warning.WriteLine($"warning: {missing.Message}");
            return 1;
        }

        PortableBackupCommitter.RequireLocalBranch(repository);
        if (!preview.HasChanges)
        {
            output.WriteLine("unchanged");
            return 0;
        }

        var commit = PortableCodexBackupWriter.WriteAndCommit(preview);
        output.WriteLine($"committed {commit}");
        return 0;
    }

    private static int RunInit(
        ProviderRepositoryOptions options,
        TextWriter output,
        TextWriter warning)
    {
        var repository = RequireAbsolutePath(options.Repository, "--repo");

        if (options.EnrollShared)
        {
            var provider = options.Provider;
            var providerHome = provider == AecIdentifiers.Copilot
                ? ResolveCopilotHome(options.Home)
                : ResolveCodexHome(options.Home);
            return SharedEnrollmentCommand.Run(repository, provider, providerHome, output);
        }

        if (options.Provider == AecIdentifiers.Copilot)
        {
            var copilotHome = ResolveCopilotHome(options.Home);
            if (options.Home is null)
            {
                EnsureOtherImplicitHarnessUsesRepository(repository, AecIdentifiers.Codex);
            }

            return CopilotInitCommand.Run(
                repository, copilotHome, options.ForcePathChange, output);
        }

        var codexHome = ResolveCodexHome(options.Home);
        if (options.Home is null)
        {
            EnsureOtherImplicitHarnessUsesRepository(repository, AecIdentifiers.Copilot);
        }

        return InitCommand.Run(
            repository,
            codexHome,
            options.ForcePathChange,
            output,
            warning);
    }

    private static void EnsureOtherImplicitHarnessUsesRepository(
        string repository,
        string otherProvider)
    {
        // Explicit custom homes are outside this guard: there is no safe way to
        // discover an arbitrary other-harness directory on the same machine.
        var otherHome = otherProvider == AecIdentifiers.Codex
            ? ResolveCodexHome(null)
            : ResolveCopilotHome(null);
        EnsureNoLinksInExistingPath(otherHome, $"{otherProvider} home path");
        if (!Directory.Exists(otherHome))
        {
            return;
        }

        EnsureRealDirectory(otherHome, $"{otherProvider} home");
        var runtimePath = Path.Combine(otherHome,
            otherProvider == AecIdentifiers.Codex ? "AGENTS.md" : "copilot-instructions.md");
        var runtime = ReadOptionalTextFile(runtimePath,
            $"{otherProvider} runtime instructions");
        if (runtime is null)
        {
            return;
        }

        var recordedRepository = otherProvider == AecIdentifiers.Codex
            ? AecInstructionBlock.ReadRepositoryBinding(runtime)?.Repository
            : CopilotInstructionBlock.ReadRepositoryBinding(runtime);
        if (recordedRepository is not null &&
            !AecInstructionBlock.RepositoryPathsEqual(recordedRepository, repository))
        {
            throw new InvalidOperationException(
                $"Default {otherProvider} runtime is bound to a different AEC data repository. " +
                $"Recorded repository: {recordedRepository}. " +
                $"Selected repository: {repository}. " +
                "Resolve both default harnesses to one checkout before initializing; " +
                "--force-path-change cannot override this conflict.");
        }
    }

    private static int RunApply(
        ProviderRepositoryOptions options,
        TextWriter output,
        TextWriter warning)
    {
        var repository = RequireAbsolutePath(options.Repository, "--repo");

        if (options.Provider == AecIdentifiers.Copilot)
        {
            var copilotHome = ResolveCopilotHome(options.Home);
            // Ordinary apply consumes the enrolled, committed rendered target;
            // provider init retains its separate first-installation flow.
            var copilotPreview = PortableApplyPreview.Create(repository, AecIdentifiers.Copilot, copilotHome);
            var result = PortableInstructionWriter.Apply(copilotPreview.Snapshot);
            if (PortableApplyPreview.Create(repository, AecIdentifiers.Copilot, copilotHome).WouldReplace)
            {
                throw new IOException("Copilot runtime instructions differ after apply.");
            }

            output.WriteLine(result);
            return 0;
        }

        var codexHome = ResolveCodexHome(options.Home);
        // Ordinary apply now consumes the enrolled rendered target. Init and
        // attachment retain their checkpointed legacy writer separately.
        var preview = PortableApplyPreview.Create(repository, AecIdentifiers.Codex, codexHome);
        if (preview.CodexConfig?.Inserted == true)
        {
            warning.WriteLine(
                "warning: runtime config does not declare root `personality`; " +
                "the committed value will be added.");
        }

        output.WriteLine(PortableCodexWriter.Apply(preview));
        return 0;
    }

    private static string ParseRenderRepository(string[] args)
    {
        if (args.Length == 1)
        {
            throw new ArgumentException(
                "render requires --repo with the source-of-truth data repository.");
        }

        if (args.Length != 3 || args[1] != "--repo")
        {
            throw new ArgumentException($"Unknown render argument: {args[1]}");
        }

        if (args[2].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException("--repo requires a value.");
        }

        return args[2];
    }

    private static ProviderRepositoryOptions ParseProviderRepositoryArguments(string[] args)
    {
        var provider = args[0];
        var command = args[1];
        var local = provider != AecIdentifiers.ChatGpt;
        string? repository = null;
        string? home = null;
        var forcePathChange = false;
        var enrollShared = false;

        for (var index = 2; index < args.Length; index++)
        {
            var argument = args[index];
            switch (argument)
            {
                case "--repo":
                    if (repository is not null)
                    {
                        throw new ArgumentException("--repo may be specified only once.");
                    }

                    repository = ReadRequiredOptionValue(args, ref index, argument);
                    break;
                case "--home" when local:
                    if (home is not null)
                    {
                        throw new ArgumentException("--home may be specified only once.");
                    }

                    home = ReadRequiredOptionValue(args, ref index, argument);
                    break;
                case "--force-path-change" when local && command == "init" && !forcePathChange:
                    forcePathChange = true;
                    break;
                case "--enroll-shared" when local && command == "init" && !enrollShared:
                    enrollShared = true;
                    break;
                case "--force-path-change" or "--enroll-shared" when local && command == "init":
                    throw new ArgumentException($"{argument} may be specified only once.");
                default:
                    throw new ArgumentException($"Unknown argument: {argument}");
            }
        }

        if (repository is null)
        {
            throw new ArgumentException(
                $"{command} requires --repo with the source-of-truth data repository.");
        }

        if (enrollShared && forcePathChange)
        {
            throw new ArgumentException("--enroll-shared cannot be combined with --force-path-change.");
        }

        return new ProviderRepositoryOptions(repository, home, provider, forcePathChange, enrollShared);
    }

    private static string? ParseCodexHomeArguments(string[] args)
    {
        if (args.Length == 2)
        {
            return null;
        }

        if (args[2] != "--home")
        {
            throw new ArgumentException($"Unknown argument: {args[2]}");
        }

        var index = 2;
        var home = ReadRequiredOptionValue(args, ref index, "--home");
        if (args.Length > 4)
        {
            throw new ArgumentException(args[4] == "--home"
                ? "--home may be specified only once."
                : $"Unknown argument: {args[4]}");
        }

        return home;
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
            return RequireAbsolutePath(explicitPath, "--home");
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

    internal static string ResolveCopilotHome(string? explicitPath)
    {
        if (explicitPath is not null)
        {
            return RequireAbsolutePath(explicitPath, "--home");
        }

        var environmentPath = Environment.GetEnvironmentVariable("COPILOT_HOME");
        if (!string.IsNullOrWhiteSpace(environmentPath))
        {
            return RequireAbsolutePath(environmentPath, "COPILOT_HOME");
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(userProfile))
        {
            throw new InvalidOperationException("The user profile directory could not be resolved.");
        }

        return Path.Combine(userProfile, ".copilot");
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

    internal static void EnsureSourceDirectories(string repository)
    {
        var environment = Path.Combine(repository, "environment");
        var providers = Path.Combine(environment, "providers");
        var codex = Path.Combine(providers, AecIdentifiers.Codex);

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
              aec render --repo ABSOLUTE_PATH
              aec migrate VERSION --repo ABSOLUTE_PATH
              aec codex init --repo ABSOLUTE_PATH [--home ABSOLUTE_PATH] [--force-path-change | --enroll-shared]
              aec copilot init --repo ABSOLUTE_PATH [--home ABSOLUTE_PATH] [--force-path-change | --enroll-shared]
              aec chatgpt init --repo ABSOLUTE_PATH
              aec codex (status | backup | apply) --repo ABSOLUTE_PATH [--home ABSOLUTE_PATH]
              aec copilot (status | backup | apply) --repo ABSOLUTE_PATH [--home ABSOLUTE_PATH]
              aec codex upgrade [--home ABSOLUTE_PATH]
              aec codex uninstall [--home ABSOLUTE_PATH]

            Codex upgrade updates only the bundled AEC skill guidance.

            Shared instruction scope:
              shared: every enrolled harness
              platform: local paths or OS rules
              provider: one harness's mechanics
            Review and commit authored sources before --enroll-shared; it does not deploy.
            """;

        output.WriteLine(usage.ReplaceLineEndings(output.NewLine));
    }

    private static void WriteVersion(TextWriter output)
    {
        var assembly = typeof(AecApplication).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion;
        if (string.IsNullOrWhiteSpace(version))
        {
            version = assembly.GetName().Version?.ToString(fieldCount: 3);
        }

        output.WriteLine(version ?? throw new InvalidOperationException(
            "Application version could not be resolved."));
    }

    private sealed record ProviderRepositoryOptions(
        string Repository,
        string? Home,
        string Provider,
        bool ForcePathChange,
        bool EnrollShared);
}
