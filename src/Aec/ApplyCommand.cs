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
            manageCodexConfig: true,
            initialization: null,
            repositoryExpectation: null);
    }

    internal static int RunForInitialization(
        string repository,
        string codexHome,
        TextWriter output,
        byte[] expectedRuntime,
        string expectedCommit,
        byte[] expectedSource)
    {
        ArgumentNullException.ThrowIfNull(expectedRuntime);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedCommit);
        ArgumentNullException.ThrowIfNull(expectedSource);
        return Run(
            repository,
            codexHome,
            output,
            TextWriter.Null,
            manageCodexConfig: false,
            initialization: new InitializationExpectation(
                expectedRuntime,
                expectedCommit,
                expectedSource),
            repositoryExpectation: null);
    }

    internal static int RunForAttachment(
        string repository,
        string codexHome,
        TextWriter output,
        string expectedCommit,
        byte[] expectedSource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedCommit);
        ArgumentNullException.ThrowIfNull(expectedSource);
        return Run(
            repository,
            codexHome,
            output,
            TextWriter.Null,
            manageCodexConfig: false,
            initialization: null,
            repositoryExpectation: new RepositoryExpectation(expectedCommit, expectedSource));
    }

    private static int Run(
        string repository,
        string codexHome,
        TextWriter output,
        TextWriter warning,
        bool manageCodexConfig,
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
        if (binding is not null &&
            !AecInstructionBlock.RepositoryPathsEqual(binding.Repository, repository))
        {
            throw new InvalidOperationException(
                "Committed AEC instructions are bound to a different data repository. " +
                $"Recorded repository: {binding.Repository}. " +
                $"Selected repository: {repository}. " +
                "Run `aec init` with the selected --repo path before applying it.");
        }

        byte[]? canonicalConfig = null;
        CodexPersonality? desiredPersonality = null;
        if (manageCodexConfig)
        {
            canonicalConfig = ReadCommittedConfig(repository, commit);
            desiredPersonality = CodexPersonalityConfig.ReadCanonical(
                canonicalConfig,
                Path.Combine(repository, AecApplication.ConfigSourceRelativePath));
        }

        var runtimePath = Path.Combine(codexHome, "AGENTS.md");
        var runtime = AecApplication.ReadOptionalTextFile(runtimePath, "Runtime target");
        var runtimeConfigPath = Path.Combine(codexHome, "config.toml");
        byte[]? runtimeConfig = null;
        RuntimeConfigUpdate configUpdate = default;
        if (manageCodexConfig)
        {
            runtimeConfig = AecApplication.ReadOptionalTextFile(runtimeConfigPath, "Runtime config");
            // Build and validate the config edit before either runtime file is
            // changed, so malformed TOML cannot cause a partial ordinary apply.
            configUpdate = CodexPersonalityConfig.PlanRuntimeUpdate(
                runtimeConfig,
                runtimeConfigPath,
                desiredPersonality!.Value);
        }

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

        if (manageCodexConfig)
        {
            var refreshedConfig = ReadCommittedConfig(repository, commit);
            if (!refreshedConfig.AsSpan().SequenceEqual(canonicalConfig))
            {
                throw new InvalidOperationException("Canonical config changed during apply.");
            }
        }

        var agentsChanged = runtime is null || !runtime.AsSpan().SequenceEqual(source);
        if (!agentsChanged && (!manageCodexConfig || !configUpdate.Changed))
        {
            output.WriteLine("unchanged");
            return 0;
        }

        // Init may overwrite only the exact runtime bytes captured by its baseline commit.
        if (initialization is not null &&
            (runtime is null || !runtime.AsSpan().SequenceEqual(initialization.Runtime)))
        {
            throw new InvalidOperationException(
                "Runtime target changed after the initialization backup; committed source was not applied.");
        }

        if (manageCodexConfig && configUpdate.Changed)
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
                source);
        }

        output.WriteLine("applied");
        return 0;
    }

    internal static void ValidateRepositoryRoot(string repository)
    {
        var insideWorkTree = RunRequired(
            repository,
            "Repository is not a Git work tree",
            "rev-parse",
            "--is-inside-work-tree").Output.Trim();
        if (!string.Equals(insideWorkTree, "true", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Repository is not a Git work tree: {repository}");
        }

        var bare = RunRequired(
            repository,
            "Git could not inspect the repository",
            "rev-parse",
            "--is-bare-repository").Output.Trim();
        if (!string.Equals(bare, "false", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Repository must not be bare: {repository}");
        }

        var prefix = RunRequired(
            repository,
            "Git could not locate the repository root",
            "rev-parse",
            "--show-prefix").Output;
        if (prefix.Trim().Length != 0)
        {
            throw new InvalidOperationException($"--repo must identify the Git repository root: {repository}");
        }
    }

    internal static string ResolveHeadCommit(string repository)
    {
        var result = Run(repository, "rev-parse", "--verify", "--quiet", "HEAD^{commit}");
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
        return ReadCommittedFile(
            repository,
            commit,
            AecApplication.SourceRelativePath,
            "Canonical source");
    }

    private static byte[] ReadCommittedConfig(string repository, string commit)
    {
        return ReadCommittedFile(
            repository,
            commit,
            AecApplication.ConfigSourceRelativePath,
            "Canonical config");
    }

    private static byte[] ReadCommittedFile(
        string repository,
        string commit,
        string relativePath,
        string label)
    {
        var sourcePath = Path.Combine(repository, relativePath);
        var source = AecApplication.ReadRequiredTextFile(sourcePath, label);
        var committedBlob = ResolveCommittedBlob(repository, commit, relativePath, label);

        EnsureDiffIsClean(
            Run(
                repository,
                "diff",
                "--cached",
                "--quiet",
                "--exit-code",
                "--no-ext-diff",
                "--no-textconv",
                commit,
                "--",
                relativePath),
            $"{label} has staged changes.",
            $"Git could not inspect staged {label.ToLowerInvariant()} changes");
        EnsureDiffIsClean(
            Run(
                repository,
                "diff",
                "--quiet",
                "--exit-code",
                "--no-ext-diff",
                "--no-textconv",
                "--",
                relativePath),
            $"{label} has unstaged changes.",
            $"Git could not inspect unstaged {label.ToLowerInvariant()} changes");

        // Git's normal diff can hide clean/smudge or line-ending transforms; raw hashing cannot.
        var workingBlob = RunRequired(
            repository,
            $"Git could not hash the {label.ToLowerInvariant()}",
            "hash-object",
            "--no-filters",
            "--",
            relativePath).Output.Trim();
        if (!string.Equals(committedBlob, workingBlob, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"{label} bytes do not exactly match the committed Git blob; " +
                "filters or file changes are present.");
        }

        var reread = AecApplication.ReadRequiredTextFile(sourcePath, label);
        if (!reread.AsSpan().SequenceEqual(source))
        {
            throw new InvalidOperationException(
                $"{label} changed while its Git provenance was checked.");
        }

        return source;
    }

    private static string ResolveCommittedBlob(
        string repository,
        string commit,
        string relativePath,
        string label)
    {
        var tree = RunRequired(
            repository,
            "Git could not inspect the committed canonical source",
            "ls-tree",
            "-z",
            commit,
            "--",
            relativePath).Output;
        if (tree.Length == 0 || !tree.EndsWith('\0') || tree[..^1].Contains('\0'))
        {
            throw new InvalidOperationException($"{label} is not a single committed Git file.");
        }

        var record = tree[..^1];
        var tab = record.IndexOf('\t');
        if (tab < 0 ||
            !string.Equals(record[(tab + 1)..], relativePath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Git returned malformed {label.ToLowerInvariant()} metadata.");
        }

        var fields = record[..tab].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 3 ||
            fields[0] is not ("100644" or "100755") ||
            fields[1] != "blob" ||
            fields[2].Length == 0)
        {
            throw new InvalidOperationException($"{label} must be a committed regular Git file.");
        }

        return fields[2];
    }

    private static void EnsureDiffIsClean(
        GitResult result,
        string dirtyMessage,
        string failureMessage)
    {
        switch (result.ExitCode)
        {
            case 0:
                return;
            case 1:
                throw new InvalidOperationException(dirtyMessage);
            default:
                throw new InvalidOperationException($"{failureMessage} (exit code {result.ExitCode}).");
        }
    }

    internal static void EnsureRuntimeOutsideRepository(string repository, string codexHome)
    {
        var runtimePath = Path.GetFullPath(Path.Combine(codexHome, "AGENTS.md"));
        if (IsPathInsideDirectory(repository, runtimePath))
        {
            throw new InvalidOperationException("Codex runtime target must be outside the data repository.");
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

    private static GitResult Run(string repository, params string[] arguments)
    {
        return GitProcess.Run(repository, ["--no-replace-objects", .. arguments]);
    }

    private static GitResult RunRequired(
        string repository,
        string failureMessage,
        params string[] arguments)
    {
        return GitProcess.RunRequired(
            repository,
            failureMessage,
            ["--no-replace-objects", .. arguments]);
    }

    private sealed record InitializationExpectation(
        byte[] Runtime,
        string Commit,
        byte[] Source);

    private sealed record RepositoryExpectation(string Commit, byte[] Source);
}
