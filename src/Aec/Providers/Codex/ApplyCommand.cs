namespace Aec;

internal static class ApplyCommand
{
    public static int Run(
        string repository,
        string codexHome,
        TextWriter output,
        TextWriter warning)
    {
        return Run(
            repository,
            codexHome,
            output,
            warning,
            initialization: null,
            repositoryExpectation: null);
    }

    internal static int RunForInitialization(
        string repository,
        string codexHome,
        TextWriter output,
        byte[] expectedRuntime,
        CodexPersonality? expectedRuntimePersonality,
        string expectedCommit,
        byte[] expectedSource,
        byte[] expectedConfig)
    {
        ArgumentNullException.ThrowIfNull(expectedRuntime);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedCommit);
        ArgumentNullException.ThrowIfNull(expectedSource);
        ArgumentNullException.ThrowIfNull(expectedConfig);
        return Run(
            repository,
            codexHome,
            output,
            TextWriter.Null,
            initialization: new InitializationExpectation(
                expectedRuntime,
                expectedRuntimePersonality,
                expectedCommit,
                expectedSource,
                expectedConfig),
            repositoryExpectation: null);
    }

    internal static int RunForAttachment(
        string repository,
        string codexHome,
        TextWriter output,
        TextWriter warning,
        byte[]? expectedRuntime,
        CodexPersonality? expectedRuntimePersonality,
        string expectedCommit,
        byte[] expectedSource,
        byte[] expectedConfig,
        byte[]? expectedLocalRuntimeSource = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedCommit);
        ArgumentNullException.ThrowIfNull(expectedSource);
        ArgumentNullException.ThrowIfNull(expectedConfig);
        return Run(
            repository,
            codexHome,
            output,
            warning,
            initialization: null,
            repositoryExpectation: new RepositoryExpectation(
                expectedRuntime,
                expectedRuntimePersonality,
                expectedCommit,
                expectedSource,
                expectedConfig,
                expectedLocalRuntimeSource));
    }

    private static int Run(
        string repository,
        string codexHome,
        TextWriter output,
        TextWriter warning,
        InitializationExpectation? initialization,
        RepositoryExpectation? repositoryExpectation)
    {
        EnsureRuntimeOutsideRepository(repository, codexHome);
        ValidateRepositoryRoot(repository);

        var commit = ResolveHeadCommit(repository);
        if (initialization is not null &&
            !string.Equals(commit, initialization.Commit, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Repository HEAD changed after the initialization commit.");
        }
        if (repositoryExpectation is not null &&
            !string.Equals(commit, repositoryExpectation.Commit, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Repository HEAD changed before attachment apply.");
        }

        var source = ReadCommittedSource(repository, commit);
        if (initialization is not null &&
            !source.AsSpan().SequenceEqual(initialization.Source))
        {
            throw new InvalidOperationException(
                "Committed source does not match the expected initialization content.");
        }
        if (repositoryExpectation is not null &&
            !source.AsSpan().SequenceEqual(repositoryExpectation.Source))
        {
            throw new InvalidOperationException(
                "Committed source changed before attachment apply.");
        }

        var binding = AecInstructionBlock.ReadRepositoryBinding(source);
        var desiredRuntimeSource = source;
        if (binding is not null &&
            !AecInstructionBlock.RepositoryPathsEqual(binding.Repository, repository))
        {
            // Only confirmed init attachment may substitute a machine-local
            // binding. The committed bytes remain pinned and are never edited.
            var localSource = repositoryExpectation?.LocalRuntimeSource;
            if (localSource is null ||
                !AecInstructionBlock.RebindRepository(source, repository)
                    .AsSpan().SequenceEqual(localSource))
            {
                throw new InvalidOperationException(
                    "Committed AEC instructions are bound to a different data repository. " +
                    $"Recorded repository: {binding.Repository}. " +
                    $"Selected repository: {repository}. " +
                    "Run `aec init` with the selected --repo path before applying it.");
            }

            desiredRuntimeSource = localSource;
        }

        var canonicalConfig = ReadCommittedConfig(repository, commit);
        if (initialization is not null &&
            !canonicalConfig.AsSpan().SequenceEqual(initialization.Config))
        {
            throw new InvalidOperationException(
                "Committed config does not match the expected initialization content.");
        }
        if (repositoryExpectation is not null &&
            !canonicalConfig.AsSpan().SequenceEqual(repositoryExpectation.Config))
        {
            throw new InvalidOperationException(
                "Committed config changed before attachment apply.");
        }

        var desiredPersonality = CodexPersonalityConfig.ReadCanonical(
            canonicalConfig,
            Path.Combine(repository, AecApplication.ConfigSourceRelativePath));

        var runtimePath = Path.Combine(codexHome, "AGENTS.md");
        var runtime = AecApplication.ReadOptionalTextFile(runtimePath, "Runtime target");
        var runtimeConfigPath = Path.Combine(codexHome, "config.toml");
        var runtimeConfig = AecApplication.ReadOptionalTextFile(runtimeConfigPath, "Runtime config");
        var runtimePersonality = runtimeConfig is null
            ? null
            : CodexPersonalityConfig.ReadRuntime(runtimeConfig, runtimeConfigPath);
        // Build and validate the config edit before either runtime file is
        // changed, so malformed TOML cannot cause a partial apply.
        var configUpdate = CodexPersonalityConfig.PlanRuntimeUpdate(
            runtimeConfig,
            runtimeConfigPath,
            desiredPersonality);

        // Revalidate provenance after observing runtime so a concurrent checkout or canonical edit stops apply.
        var currentCommit = ResolveHeadCommit(repository);
        if (!string.Equals(commit, currentCommit, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Repository HEAD changed during apply.");
        }

        var refreshedSource = ReadCommittedSource(repository, commit);
        if (!refreshedSource.AsSpan().SequenceEqual(source))
        {
            throw new InvalidOperationException("Canonical source changed during apply.");
        }

        var refreshedConfig = ReadCommittedConfig(repository, commit);
        if (!refreshedConfig.AsSpan().SequenceEqual(canonicalConfig))
        {
            throw new InvalidOperationException("Canonical config changed during apply.");
        }

        var agentsChanged = runtime is null || !runtime.AsSpan().SequenceEqual(desiredRuntimeSource);
        if (!agentsChanged && !configUpdate.Changed)
        {
            output.WriteLine("unchanged");
            return 0;
        }

        // Init may overwrite only the exact runtime bytes captured by its baseline commit.
        if (initialization is not null &&
            (runtime is null || !runtime.AsSpan().SequenceEqual(initialization.Runtime)) &&
            (runtime is null || !runtime.AsSpan().SequenceEqual(desiredRuntimeSource)))
        {
            throw new InvalidOperationException(
                "Runtime target changed after the initialization backup; committed source was not applied.");
        }

        if (initialization is not null &&
            runtimePersonality != initialization.RuntimePersonality &&
            runtimePersonality != desiredPersonality)
        {
            throw new InvalidOperationException(
                "Runtime personality changed after the initialization backup; " +
                "the committed environment was not applied.");
        }

        // Attachment uses the runtime snapshot taken before skill installation or
        // a local path rebind. The already-desired state is also accepted so a
        // previously interrupted apply remains safely repeatable.
        if (repositoryExpectation is not null &&
            !MatchesOptionalBytes(runtime, repositoryExpectation.Runtime) &&
            (runtime is null || !runtime.AsSpan().SequenceEqual(desiredRuntimeSource)))
        {
            throw new InvalidOperationException(
                "Runtime target changed during AEC attachment; " +
                "the committed environment was not applied.");
        }

        if (repositoryExpectation is not null &&
            runtimePersonality != repositoryExpectation.RuntimePersonality &&
            runtimePersonality != desiredPersonality)
        {
            throw new InvalidOperationException(
                "Runtime personality changed during AEC attachment; " +
                "the committed environment was not applied.");
        }

        if (configUpdate.Changed)
        {
            if (configUpdate.Inserted)
            {
                warning.WriteLine(
                    "warning: runtime config does not declare root `personality`; " +
                    "the committed value will be added.");
            }

            // Each target has its own compare-before-replace guard. A later
            // interruption is safe to resume with another status/apply cycle.
            AtomicFile.ReplaceIfUnchanged(
                runtimeConfigPath,
                runtimeConfig,
                configUpdate.Content,
                "Runtime config",
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        if (agentsChanged)
        {
            AtomicFile.ReplaceIfUnchanged(
                runtimePath,
                initialization?.Runtime ?? runtime,
                desiredRuntimeSource);
        }

        output.WriteLine("applied");
        return 0;
    }

    internal static void ValidateRepositoryRoot(
        string repository,
        string rootRequirement = "--repo must identify the Git repository root")
    {
        var metadata = GitProcess.RunRequired(
            repository,
            "Repository is not a Git work tree",
            "--no-replace-objects",
            "rev-parse",
            "--is-inside-work-tree",
            "--is-bare-repository",
            "--show-prefix").Output.Split('\n', 3);
        if (metadata.Length != 3 || !metadata[2].EndsWith('\n'))
        {
            throw new InvalidOperationException("Git returned incomplete repository metadata.");
        }

        if (!string.Equals(metadata[0].Trim(), "true", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Repository is not a Git work tree: {repository}");
        }

        if (!string.Equals(metadata[1].Trim(), "false", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Repository must not be bare: {repository}");
        }

        if (metadata[2].Trim().Length != 0)
        {
            throw new InvalidOperationException($"{rootRequirement}: {repository}");
        }
    }

    internal static string ResolveHeadCommit(string repository)
    {
        var result = GitProcess.Run(repository, "--no-replace-objects", "rev-parse", "--verify", "--quiet", "HEAD^{commit}");
        if (result.ExitCode == 1)
        {
            throw new InvalidOperationException("Apply requires a committed Git HEAD.");
        }

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Git could not resolve committed HEAD (exit code {result.ExitCode}).");
        }

        var commit = result.Output.Trim();
        return commit.Length == 0
            ? throw new InvalidOperationException("Git returned an empty commit identifier.")
            : commit;
    }

    internal static byte[] ReadCommittedSource(string repository, string commit)
    {
        return CommittedFileReader.ReadCommittedFile(
            repository,
            commit,
            AecApplication.SourceRelativePath,
            "Canonical source");
    }

    internal static byte[] ReadCommittedConfig(string repository, string commit)
    {
        return CommittedFileReader.ReadCommittedFile(
            repository,
            commit,
            AecApplication.ConfigSourceRelativePath,
            "Canonical config");
    }

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

    private sealed record InitializationExpectation(
        byte[] Runtime,
        CodexPersonality? RuntimePersonality,
        string Commit,
        byte[] Source,
        byte[] Config);

    private static bool MatchesOptionalBytes(byte[]? left, byte[]? right)
    {
        return left is null
            ? right is null
            : right is not null && left.AsSpan().SequenceEqual(right);
    }

    private sealed record RepositoryExpectation(
        byte[]? Runtime,
        CodexPersonality? RuntimePersonality,
        string Commit,
        byte[] Source,
        byte[] Config,
        byte[]? LocalRuntimeSource);
}
