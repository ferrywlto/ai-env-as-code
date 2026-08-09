namespace Aec;

internal static class InitCommand
{
    private const string InitializationCommitMessage = "Initialize AEC instructions";
    private const string RebindCommitMessage = "Rebind AEC repository path";

    public static int Run(
        string directoryPath,
        string codexHome,
        bool forcePathChange,
        TextWriter output,
        TextWriter warning)
    {
        var targetWasMissing = !Directory.Exists(directoryPath);
        var isFreshTarget = IsFreshTarget(directoryPath);
        AecApplication.EnsureNoLinksInExistingPath(codexHome, "Codex home path");
        AecApplication.EnsureRealDirectory(codexHome, "Codex home");
        ApplyCommand.EnsureRuntimeOutsideRepository(directoryPath, codexHome);

        if (isFreshTarget && forcePathChange)
        {
            throw new InvalidOperationException(
                "--force-path-change requires an existing initialized repository.");
        }

        var runtimePath = Path.Combine(codexHome, "AGENTS.md");
        EnsureGitIsAvailable();

        if (!isFreshTarget && HasCompletedInitializationHistory(directoryPath))
        {
            CompletedSnapshot completed;
            try
            {
                completed = LoadCompletedRepository(directoryPath);
            }
            catch (Exception exception)
            {
                throw NotAttachable(directoryPath, exception);
            }

            return AttachCompletedRepository(
                directoryPath,
                codexHome,
                runtimePath,
                forcePathChange,
                completed,
                output,
                warning);
        }

        if (!isFreshTarget && forcePathChange)
        {
            throw new InvalidOperationException(
                "--force-path-change requires an existing initialized repository, not a partial baseline.");
        }

        // Fresh and partial-baseline flows validate all managed runtime input before
        // installing the skill or creating repository state.
        _ = AecApplication.ReadRequiredTextFile(runtimePath, "Runtime target");
        var runtimeConfigPath = Path.Combine(codexHome, "config.toml");
        var runtimeConfig = AecApplication.ReadOptionalTextFile(
            runtimeConfigPath,
            "Runtime config");
        var runtimePersonality = runtimeConfig is null
            ? null
            : CodexPersonalityConfig.ReadRuntime(runtimeConfig, runtimeConfigPath);
        if (runtimePersonality is null)
        {
            warning.WriteLine(
                "warning: runtime config does not declare root `personality`; " +
                "`personality = \"none\"` will be enrolled and added after " +
                "the initialization commits.");
        }
        // Validate the eventual runtime edit before skill or repository mutation.
        // Apply plans it again later to retain its compare-before-replace guard.
        _ = CodexPersonalityConfig.PlanRuntimeUpdate(
            runtimeConfig,
            runtimeConfigPath,
            runtimePersonality ?? CodexPersonality.None);

        BaselineSnapshot baseline;
        if (isFreshTarget)
        {
            EnsureFreshCommitIdentity(directoryPath, targetWasMissing);
            AecSkillInstaller.Install(codexHome);
            InitializeRepository(directoryPath);

            BackupCommand.RunForInitialization(
                directoryPath,
                codexHome,
                runtimePersonality,
                TextWriter.Null);
            baseline = LoadBaseline(
                directoryPath,
                runtimePath,
                runtimePersonality);
        }
        else
        {
            try
            {
                baseline = LoadBaseline(
                    directoryPath,
                    runtimePath,
                    runtimePersonality);
            }
            catch (Exception exception)
            {
                throw NotResumable(directoryPath, exception);
            }

            EnsureCommitIdentity(directoryPath);
        }

        var merged = AecInstructionBlock.Merge(baseline.Content, directoryPath);
        if (!baseline.WorkingSource.AsSpan().SequenceEqual(baseline.Content) &&
            !baseline.WorkingSource.AsSpan().SequenceEqual(merged))
        {
            var exception = new InvalidOperationException(
                "Canonical source is neither the committed baseline nor the expected managed instructions.");
            throw isFreshTarget ? exception : NotResumable(directoryPath, exception);
        }

        if (!isFreshTarget)
        {
            AecSkillInstaller.Install(codexHome);
            GitProcess.RunRequired(
                directoryPath,
                "Git could not configure exact instruction bytes",
                "config",
                "--local",
                "core.autocrlf",
                "false");
        }

        var sourcePath = Path.Combine(directoryPath, AecApplication.SourceRelativePath);
        if (!baseline.WorkingSource.AsSpan().SequenceEqual(merged))
        {
            AtomicFile.ReplaceIfUnchanged(
                sourcePath,
                baseline.WorkingSource,
                merged,
                "Canonical source");
        }

        var configSourcePath = Path.Combine(
            directoryPath,
            AecApplication.ConfigSourceRelativePath);
        if (!MatchesOptionalBytes(baseline.WorkingConfig, baseline.Config))
        {
            AtomicFile.ReplaceIfUnchanged(
                configSourcePath,
                baseline.WorkingConfig,
                baseline.Config,
                "Canonical config");
        }

        var initializationCommit = BackupCommand.CommitCanonicalEnvironment(
            directoryPath,
            InitializationCommitMessage,
            baseline.Commit,
            merged,
            baseline.Config,
            allowEmpty: true);
        ApplyCommand.RunForInitialization(
            directoryPath,
            codexHome,
            TextWriter.Null,
            baseline.Content,
            baseline.RuntimePersonality,
            initializationCommit,
            merged,
            baseline.Config);

        VerifyInitializationResult(
            directoryPath,
            sourcePath,
            runtimePath,
            initializationCommit,
            merged,
            baseline.Config);

        output.WriteLine("initialized");
        return 0;
    }

    private static int AttachCompletedRepository(
        string repository,
        string codexHome,
        string runtimePath,
        bool forcePathChange,
        CompletedSnapshot completed,
        TextWriter output,
        TextWriter warning)
    {
        var pathMatches = AecInstructionBlock.RepositoryPathsEqual(
            completed.Binding.Repository,
            repository);
        if (!pathMatches && !forcePathChange)
        {
            throw new InvalidOperationException(
                "Initialized AEC instructions are bound to a different data repository. " +
                $"Recorded repository: {completed.Binding.Repository}. " +
                $"Selected repository: {repository}. " +
                "Confirm the path change, then rerun `aec init` with --force-path-change.");
        }

        // Preflight both managed runtime inputs before installing the skill or
        // committing a rebind; Apply repeats the reads before changing either file.
        var runtime = AecApplication.ReadOptionalTextFile(runtimePath, "Runtime target");
        var runtimeConfigPath = Path.Combine(codexHome, "config.toml");
        var runtimeConfig = AecApplication.ReadOptionalTextFile(
            runtimeConfigPath,
            "Runtime config");
        var runtimePersonality = runtimeConfig is null
            ? null
            : CodexPersonalityConfig.ReadRuntime(runtimeConfig, runtimeConfigPath);
        var desiredPersonality = CodexPersonalityConfig.ReadCanonical(
            completed.Config,
            Path.Combine(repository, AecApplication.ConfigSourceRelativePath));
        // A deterministic planning failure must happen before skill installation
        // and, for a moved repository, before the rebind commit.
        _ = CodexPersonalityConfig.PlanRuntimeUpdate(
            runtimeConfig,
            runtimeConfigPath,
            desiredPersonality);

        if (pathMatches)
        {
            AecSkillInstaller.Install(codexHome);
            ApplyCommand.RunForAttachment(
                repository,
                codexHome,
                TextWriter.Null,
                warning,
                runtime,
                runtimePersonality,
                completed.Commit,
                completed.Content,
                completed.Config);
            VerifyInitializationResult(
                repository,
                Path.Combine(repository, AecApplication.SourceRelativePath),
                runtimePath,
                completed.Commit,
                completed.Content,
                completed.Config);
            output.WriteLine("initialized");
            return 0;
        }

        EnsureCommitIdentity(repository);
        AecSkillInstaller.Install(codexHome);
        GitProcess.RunRequired(
            repository,
            "Git could not configure exact instruction bytes",
            "config",
            "--local",
            "core.autocrlf",
            "false");

        var sourcePath = Path.Combine(repository, AecApplication.SourceRelativePath);
        var rebound = AecInstructionBlock.RebindRepository(completed.Content, repository);
        AtomicFile.ReplaceIfUnchanged(
            sourcePath,
            completed.Content,
            rebound,
            "Canonical source");

        var commit = BackupCommand.CommitCanonicalSource(
            repository,
            RebindCommitMessage,
            completed.Commit,
            rebound,
            allowEmpty: false);
        ApplyCommand.RunForAttachment(
            repository,
            codexHome,
            TextWriter.Null,
            warning,
            runtime,
            runtimePersonality,
            commit,
            rebound,
            completed.Config);
        VerifyInitializationResult(
            repository,
            sourcePath,
            runtimePath,
            commit,
            rebound,
            completed.Config);

        output.WriteLine("initialized");
        return 0;
    }

    private static CompletedSnapshot LoadCompletedRepository(string repository)
    {
        AecApplication.EnsureRealDirectory(repository, "Repository");
        var gitDirectory = Path.Combine(repository, ".git");
        AecApplication.EnsureRealDirectory(gitDirectory, "Git directory");
        AecApplication.EnsureSourceDirectories(repository);
        EnsureContainedGitMetadata(repository, gitDirectory);
        BackupCommand.ValidateRepository(repository);
        EnsureMainBranch(repository);
        BackupCommand.EnsureNoChangesOutsideManagedSources(repository);

        var commit = ApplyCommand.ResolveHeadCommit(repository);
        var content = ApplyCommand.ReadCommittedSource(repository, commit);
        var config = ApplyCommand.ReadCommittedConfig(repository, commit);
        _ = CodexPersonalityConfig.ReadCanonical(
            config,
            Path.Combine(repository, AecApplication.ConfigSourceRelativePath));
        var binding = AecInstructionBlock.ReadRepositoryBinding(content)
            ?? throw new InvalidDataException(
                "Canonical instructions do not contain a supported initialized AEC block.");

        var history = ReadFirstParentHistory(repository, required: true);
        if (history.Length < 2 || !HasExpectedInitializationSubjects(repository, history))
        {
            throw new InvalidOperationException(
                "Repository does not contain the expected AEC initialization history.");
        }

        ValidateInitializationHistory(repository, history[0], history[1]);

        // Pin the inspected snapshot after all history checks so a concurrent checkout
        // cannot silently redirect the later attachment apply.
        var currentCommit = ApplyCommand.ResolveHeadCommit(repository);
        if (!string.Equals(commit, currentCommit, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Repository HEAD changed while completed initialization was inspected.");
        }

        EnsureMainBranch(repository);
        return new CompletedSnapshot(commit, content, config, binding);
    }

    private static bool HasCompletedInitializationHistory(string repository)
    {
        // A one-commit baseline may already contain a current block; the mandatory
        // second commit is what distinguishes a completed initialization.
        var history = ReadFirstParentHistory(repository, required: false);
        return history.Length >= 2 && HasExpectedInitializationSubjects(repository, history);
    }

    private static string[] ReadFirstParentHistory(string repository, bool required)
    {
        // First-parent order keeps the original lifecycle commits stable even when
        // later provider work introduces merge commits.
        var result = GitProcess.Run(
            repository,
            "--no-replace-objects",
            "rev-list",
            "--first-parent",
            "--reverse",
            "HEAD");
        if (result.ExitCode != 0)
        {
            if (required)
            {
                throw new InvalidOperationException(
                    $"Git could not inspect AEC initialization history (exit code {result.ExitCode}).");
            }

            return [];
        }

        return result.Output.Split(
            ['\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static bool HasExpectedInitializationSubjects(
        string repository,
        string[] history)
    {
        var baselineSubject = ReadCommitSubject(repository, history[0]);
        return (string.Equals(
                    baselineSubject,
                    BackupCommand.CommitMessage,
                    StringComparison.Ordinal) ||
                string.Equals(
                    baselineSubject,
                    BackupCommand.OrdinaryCommitMessage,
                    StringComparison.Ordinal)) &&
               string.Equals(
                   ReadCommitSubject(repository, history[1]),
                   InitializationCommitMessage,
                   StringComparison.Ordinal);
    }

    private static string ReadCommitSubject(string repository, string commit)
    {
        return GitProcess.RunRequired(
            repository,
            "Git could not inspect AEC initialization history",
            "--no-replace-objects",
            "log",
            "-1",
            "--format=%s",
            commit).Output.TrimEnd('\r', '\n');
    }

    private static void ValidateInitializationHistory(
        string repository,
        string baselineCommit,
        string initializationCommit)
    {
        var baselineObject = GitProcess.RunRequiredBytes(
            repository,
            "Git could not inspect the AEC baseline commit",
            "--no-replace-objects",
            "cat-file",
            "commit",
            baselineCommit);
        EnsureRootCommit(baselineObject);
        var baselineSubject = ReadCommitSubject(repository, baselineCommit);
        var baselineFormat = baselineSubject switch
        {
            BackupCommand.CommitMessage => BaselineFormat.LegacyAgentsOnly,
            BackupCommand.OrdinaryCommitMessage => BaselineFormat.ManagedEnvironment,
            _ => throw new InvalidOperationException(
                "AEC initialization baseline has an unsupported commit subject.")
        };
        var baselineEntries = ReadTreeEntries(repository, baselineCommit);
        EnsureBaselineTreeShape(baselineEntries, baselineFormat);
        var baselineContent = ReadBoundedBlob(
            repository,
            baselineEntries[AecApplication.SourceRelativePath].ObjectId,
            "AEC initialization baseline canonical source");

        var initializationObject = GitProcess.RunRequiredBytes(
            repository,
            "Git could not inspect the AEC initialization commit",
            "--no-replace-objects",
            "cat-file",
            "commit",
            initializationCommit);
        EnsureSingleParent(initializationObject, baselineCommit);

        var initializationEntries = ReadTreeEntries(repository, initializationCommit);
        var hasSource = initializationEntries.TryGetValue(
            AecApplication.SourceRelativePath,
            out var initializationEntry);
        var hasConfig = initializationEntries.TryGetValue(
            AecApplication.ConfigSourceRelativePath,
            out var initializationConfigEntry);
        var expectedEntryCount = hasConfig ? 2 : 1;
        if (!hasSource || initializationEntries.Count != expectedEntryCount ||
            (baselineFormat == BaselineFormat.ManagedEnvironment && !hasConfig))
        {
            throw new InvalidOperationException(
                "AEC initialization commit contains an unexpected tree shape.");
        }

        var initializedContent = ReadBoundedBlob(
            repository,
            initializationEntry!.ObjectId,
            "AEC initialization canonical source");
        var binding = AecInstructionBlock.ReadRepositoryBinding(initializedContent);
        if (binding is null)
        {
            throw new InvalidDataException(
                "AEC initialization commit does not contain a supported initialized block.");
        }

        var expectedInitializedContent = binding.Version == 3
            ? AecInstructionBlock.Merge(baselineContent, binding.Repository)
            : AecInstructionBlock.MergeForChatGptProvider(
                baselineContent,
                binding.Repository);
        if (!initializedContent.AsSpan().SequenceEqual(expectedInitializedContent))
        {
            throw new InvalidOperationException(
                "AEC initialization commit changed an unmanaged instruction.");
        }

        if (hasConfig)
        {
            var initializedConfig = ReadBoundedBlob(
                repository,
                initializationConfigEntry!.ObjectId,
                "AEC initialization canonical config");
            _ = CodexPersonalityConfig.ReadCanonical(
                initializedConfig,
                Path.Combine(repository, AecApplication.ConfigSourceRelativePath));

            if (baselineFormat == BaselineFormat.ManagedEnvironment &&
                !string.Equals(
                    baselineEntries[AecApplication.ConfigSourceRelativePath].ObjectId,
                    initializationConfigEntry.ObjectId,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "AEC initialization commit changed the baseline canonical config.");
            }
        }
    }

    private static byte[] ReadBoundedBlob(
        string repository,
        string objectId,
        string label)
    {
        var sizeText = GitProcess.RunRequired(
            repository,
            $"Git could not inspect {label} size",
            "--no-replace-objects",
            "cat-file",
            "-s",
            objectId).Output.Trim();
        if (!long.TryParse(sizeText, out var size) ||
            size < 0 ||
            size > AecApplication.MaximumTextBytes)
        {
            throw new InvalidDataException($"{label} exceeds 1 MiB.");
        }

        var content = GitProcess.RunRequiredBytes(
            repository,
            $"Git could not read {label}",
            "--no-replace-objects",
            "cat-file",
            "blob",
            objectId);
        if (content.LongLength != size)
        {
            throw new InvalidDataException($"{label} size changed while it was read.");
        }

        return content;
    }

    private static void VerifyContent(string path, byte[] expected, string label)
    {
        var actual = AecApplication.ReadRequiredTextFile(path, label);
        if (!actual.AsSpan().SequenceEqual(expected))
        {
            throw new IOException($"{label} verification failed after initialization.");
        }
    }

    private static bool IsFreshTarget(string path)
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
                return false;
            }

            return true;
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

        return true;
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

    private static void EnsureCommitIdentity(string? workingDirectory)
    {
        GitProcess.RunRequired(
            workingDirectory,
            "Git author identity is not configured",
            "var",
            "GIT_AUTHOR_IDENT");
        GitProcess.RunRequired(
            workingDirectory,
            "Git committer identity is not configured",
            "var",
            "GIT_COMMITTER_IDENT");
    }

    private static void EnsureFreshCommitIdentity(
        string repository,
        bool removeRepositoryAfterProbe)
    {
        try
        {
            Directory.CreateDirectory(repository);
            GitProcess.RunRequired(
                repository,
                "Git identity preflight repository could not be initialized",
                "init",
                "--quiet",
                "--template=",
                "--initial-branch=main");
            EnsureGitDirectory(repository);
            EnsureCommitIdentity(repository);
        }
        finally
        {
            // Restore the originally missing or empty target before any durable initialization.
            var gitDirectory = Path.Combine(repository, ".git");
            if (Directory.Exists(gitDirectory))
            {
                Directory.Delete(gitDirectory, recursive: true);
            }

            if (removeRepositoryAfterProbe && Directory.Exists(repository))
            {
                Directory.Delete(repository);
            }
        }
    }

    private static void InitializeRepository(string repository)
    {
        Directory.CreateDirectory(repository);
        GitProcess.RunRequired(
            repository,
            "Git init failed",
            "init",
            "--quiet",
            "--template=",
            "--initial-branch=main");
        EnsureGitDirectory(repository);

        // Disable checkout conversion because both initialization commits must retain exact bytes.
        GitProcess.RunRequired(
            repository,
            "Git could not configure exact instruction bytes",
            "config",
            "--local",
            "core.autocrlf",
            "false");

        var sourcePath = Path.Combine(repository, AecApplication.SourceRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        AtomicFile.WriteNew(sourcePath, []);
    }

    private static void VerifyInitializationResult(
        string repository,
        string sourcePath,
        string runtimePath,
        string expectedCommit,
        byte[] expectedContent,
        byte[] expectedConfig)
    {
        var head = ResolveBaselineHead(repository, "Git could not verify initialized HEAD");
        if (!string.Equals(head, expectedCommit, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Repository HEAD changed after the initialization commit.");
        }

        EnsureMainBranch(repository);
        BackupCommand.EnsureNoChangesOutsideManagedSources(repository);
        var managedStatus = GitProcess.RunRequired(
            repository,
            "Git could not verify the initialized canonical environment",
            "status",
            "--porcelain=v1",
            "-z",
            "--untracked-files=all",
            "--",
            AecApplication.SourceRelativePath,
            AecApplication.ConfigSourceRelativePath);
        if (managedStatus.Output.Length != 0)
        {
            throw new InvalidOperationException(
                "Canonical environment is not clean after initialization.");
        }

        VerifyContent(sourcePath, expectedContent, "Canonical source");
        VerifyContent(runtimePath, expectedContent, "Runtime target");
        var configSourcePath = Path.Combine(
            repository,
            AecApplication.ConfigSourceRelativePath);
        VerifyContent(configSourcePath, expectedConfig, "Canonical config");

        var desiredPersonality = CodexPersonalityConfig.ReadCanonical(
            expectedConfig,
            configSourcePath);
        var runtimeConfigPath = Path.Combine(
            Path.GetDirectoryName(runtimePath)
                ?? throw new InvalidOperationException("Runtime target has no parent directory."),
            "config.toml");
        var runtimeConfig = AecApplication.ReadRequiredTextFile(
            runtimeConfigPath,
            "Runtime config");
        if (CodexPersonalityConfig.ReadRuntime(runtimeConfig, runtimeConfigPath) !=
            desiredPersonality)
        {
            throw new IOException(
                "Runtime personality verification failed after initialization.");
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

    private static BaselineSnapshot LoadBaseline(
        string repository,
        string runtimePath,
        CodexPersonality? runtimePersonality)
    {
        EnsureExactBaselineLayout(repository);
        BackupCommand.ValidateRepository(repository);
        BackupCommand.EnsureNoChangesOutsideManagedSources(repository);
        var commit = ResolveBaselineHead(
            repository,
            "Git could not resolve the initialization baseline");

        EnsureMainBranch(repository);

        var commitObject = GitProcess.RunRequiredBytes(
            repository,
            "Git could not inspect initialization history",
            "--no-replace-objects",
            "cat-file",
            "commit",
            commit);
        EnsureRootCommit(commitObject);

        var subject = GitProcess.RunRequired(
            repository,
            "Git could not inspect the initialization baseline subject",
            "--no-replace-objects",
            "log",
            "-1",
            "--format=%s",
            commit).Output.TrimEnd('\r', '\n');
        var format = subject switch
        {
            BackupCommand.CommitMessage => BaselineFormat.LegacyAgentsOnly,
            BackupCommand.OrdinaryCommitMessage => BaselineFormat.ManagedEnvironment,
            _ => throw new InvalidOperationException(
                "Initialization baseline commit subject must be " +
                $"'{BackupCommand.CommitMessage}' or " +
                $"'{BackupCommand.OrdinaryCommitMessage}'.")
        };

        var treeEntries = ReadTreeEntries(repository, commit);
        EnsureBaselineTreeShape(treeEntries, format);
        var sourceEntry = treeEntries[AecApplication.SourceRelativePath];
        var content = ReadBoundedBlob(
            repository,
            sourceEntry.ObjectId,
            "Initialization baseline canonical source");

        var runtime = AecApplication.ReadRequiredTextFile(runtimePath, "Runtime target");
        if (!runtime.AsSpan().SequenceEqual(content))
        {
            throw new InvalidOperationException(
                "Current runtime does not match the committed initialization baseline.");
        }

        var desiredPersonality = runtimePersonality ?? CodexPersonality.None;
        var configPath = Path.Combine(repository, AecApplication.ConfigSourceRelativePath);
        var workingConfig = AecApplication.ReadOptionalTextFile(configPath, "Canonical config");
        byte[] config;
        if (format == BaselineFormat.ManagedEnvironment)
        {
            config = ReadBoundedBlob(
                repository,
                treeEntries[AecApplication.ConfigSourceRelativePath].ObjectId,
                "Initialization baseline canonical config");
            var committedPersonality = CodexPersonalityConfig.ReadCanonical(config, configPath);
            if (committedPersonality != desiredPersonality)
            {
                throw new InvalidOperationException(
                    "Current runtime personality does not match the committed initialization baseline.");
            }

            if (workingConfig is null || !workingConfig.AsSpan().SequenceEqual(config))
            {
                throw new InvalidOperationException(
                    "Canonical config does not match the committed initialization baseline.");
            }
        }
        else
        {
            if (workingConfig is null)
            {
                config = CodexPersonalityConfig.PlanCanonicalUpdate(
                    content: null,
                    configPath,
                    desiredPersonality).Content;
            }
            else
            {
                // A legacy root commit has no config blob. Once a failed resume
                // has prepared one in the work tree, its exact value becomes the
                // retry checkpoint and must not silently recapture later drift.
                var pendingPersonality = CodexPersonalityConfig.ReadCanonical(
                    workingConfig,
                    configPath);
                if (pendingPersonality != desiredPersonality)
                {
                    throw new InvalidOperationException(
                        "Current runtime personality does not match the pending " +
                        "initialization config.");
                }

                config = workingConfig;
            }
        }

        var sourcePath = Path.Combine(repository, AecApplication.SourceRelativePath);
        var workingSource = AecApplication.ReadRequiredTextFile(sourcePath, "Canonical source");
        var currentCommit = ResolveBaselineHead(
            repository,
            "Git could not revalidate the initialization baseline");
        if (!string.Equals(currentCommit, commit, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Repository HEAD changed while the initialization baseline was inspected.");
        }

        EnsureMainBranch(repository);
        return new BaselineSnapshot(
            commit,
            content,
            workingSource,
            config,
            workingConfig,
            runtimePersonality);
    }

    private static void EnsureMainBranch(string repository)
    {
        var branch = GitProcess.RunRequired(
            repository,
            "Git could not inspect the initialization branch",
            "symbolic-ref",
            "--quiet",
            "HEAD").Output.Trim();
        if (!string.Equals(branch, "refs/heads/main", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Initialization baseline must be on branch main.");
        }
    }

    private static string ResolveBaselineHead(string repository, string failureMessage)
    {
        var commit = GitProcess.RunRequired(
            repository,
            failureMessage,
            "--no-replace-objects",
            "rev-parse",
            "--verify",
            "HEAD^{commit}").Output.Trim();
        return commit.Length == 0
            ? throw new InvalidOperationException("Git returned an empty commit identifier.")
            : commit;
    }

    private static void EnsureRootCommit(byte[] commitObject)
    {
        var remaining = commitObject.AsSpan();
        while (true)
        {
            var lineEnd = remaining.IndexOf((byte)'\n');
            if (lineEnd < 0)
            {
                throw new InvalidDataException(
                    "Initialization baseline has malformed commit metadata.");
            }

            var line = remaining[..lineEnd];
            if (line.Length == 0)
            {
                return;
            }

            if (line.StartsWith("parent "u8))
            {
                throw new InvalidOperationException(
                    "Initialization baseline must contain exactly one root commit.");
            }

            remaining = remaining[(lineEnd + 1)..];
        }
    }

    private static void EnsureSingleParent(
        byte[] commitObject,
        string expectedParent)
    {
        var remaining = commitObject.AsSpan();
        ReadOnlySpan<byte> actualParent = default;
        var parentCount = 0;
        while (true)
        {
            var lineEnd = remaining.IndexOf((byte)'\n');
            if (lineEnd < 0)
            {
                throw new InvalidDataException(
                    "AEC initialization commit has malformed metadata.");
            }

            var line = remaining[..lineEnd];
            if (line.Length == 0)
            {
                break;
            }

            if (line.StartsWith("parent "u8))
            {
                parentCount++;
                actualParent = line["parent "u8.Length..];
            }

            remaining = remaining[(lineEnd + 1)..];
        }

        var expectedParentBytes = System.Text.Encoding.ASCII.GetBytes(expectedParent);
        if (parentCount != 1 || !actualParent.SequenceEqual(expectedParentBytes))
        {
            throw new InvalidOperationException(
                "AEC initialization commit must have exactly one parent, its baseline commit.");
        }
    }

    private static Dictionary<string, GitTreeEntry> ReadTreeEntries(
        string repository,
        string commit)
    {
        var tree = GitProcess.RunRequired(
            repository,
            "Git could not inspect the initialization baseline tree",
            "--no-replace-objects",
            "ls-tree",
            "-r",
            "-z",
            commit).Output;
        var records = tree.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        var entries = new Dictionary<string, GitTreeEntry>(StringComparer.Ordinal);
        foreach (var record in records)
        {
            var tab = record.IndexOf('\t');
            var fields = tab < 0
                ? []
                : record[..tab].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var path = tab < 0 ? string.Empty : record[(tab + 1)..];
            if (fields.Length != 3 ||
                fields[0] is not ("100644" or "100755") ||
                fields[1] != "blob" ||
                fields[2].Length == 0 ||
                path.Length == 0 ||
                !entries.TryAdd(path, new GitTreeEntry(fields[2])))
            {
                throw new InvalidOperationException(
                    "Initialization history contains an invalid Git tree entry.");
            }
        }

        return entries;
    }

    private static void EnsureBaselineTreeShape(
        IReadOnlyDictionary<string, GitTreeEntry> entries,
        BaselineFormat format)
    {
        var expected = format == BaselineFormat.ManagedEnvironment
            ? new[]
            {
                AecApplication.SourceRelativePath,
                AecApplication.ConfigSourceRelativePath
            }
            : [AecApplication.SourceRelativePath];
        if (!entries.Keys.Order(StringComparer.Ordinal).SequenceEqual(
                expected.Order(StringComparer.Ordinal),
                StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                format == BaselineFormat.ManagedEnvironment
                    ? "Managed initialization baseline must commit exactly AGENTS.md and config.toml."
                    : "Legacy initialization baseline must commit only AGENTS.md.");
        }
    }

    private static void EnsureExactBaselineLayout(string repository)
    {
        AecApplication.EnsureRealDirectory(repository, "Repository");
        var gitDirectory = Path.Combine(repository, ".git");
        AecApplication.EnsureRealDirectory(gitDirectory, "Git directory");
        AecApplication.EnsureSourceDirectories(repository);

        EnsureOnlyEntries(repository, ".git", "environment");
        EnsureOnlyEntries(Path.Combine(repository, "environment"), "providers");
        EnsureOnlyEntries(Path.Combine(repository, "environment", "providers"), "codex");
        var codexDirectory = Path.Combine(repository, "environment", "providers", "codex");
        var codexEntries = Directory
            .EnumerateFileSystemEntries(codexDirectory)
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var agentsOnly = new[] { "AGENTS.md" };
        var managedEnvironment = new[] { "AGENTS.md", "config.toml" };
        if (!codexEntries.SequenceEqual(agentsOnly, StringComparer.Ordinal) &&
            !codexEntries.SequenceEqual(managedEnvironment, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Initialization baseline has unexpected entries under: {codexDirectory}");
        }
        EnsureContainedGitMetadata(repository, gitDirectory);
    }

    private static void EnsureContainedGitMetadata(string repository, string gitDirectory)
    {
        EnsureSamePath(
            gitDirectory,
            GitProcess.RunRequired(
                repository,
                "Git could not resolve its metadata directory",
                "rev-parse",
                "--absolute-git-dir").Output.Trim(),
            "Git metadata directory");

        var commonDirectory = GitProcess.RunRequired(
            repository,
            "Git could not resolve its common metadata directory",
            "rev-parse",
            "--git-common-dir").Output.Trim();
        var resolvedCommonDirectory = Path.IsPathFullyQualified(commonDirectory)
            ? commonDirectory
            : Path.GetFullPath(commonDirectory, repository);
        EnsureSamePath(gitDirectory, resolvedCommonDirectory, "Git common metadata directory");

        EnsureSamePath(
            repository,
            GitProcess.RunRequired(
                repository,
                "Git could not resolve its work tree",
                "rev-parse",
                "--show-toplevel").Output.Trim(),
            "Git work tree");

        // A fresh AEC repository has no linked metadata; reject paths that could redirect writes.
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(gitDirectory));
        while (pending.Count > 0)
        {
            foreach (var entry in pending.Pop().EnumerateFileSystemInfos())
            {
                entry.Refresh();
                if (entry.LinkTarget is not null ||
                    (entry.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException(
                        $"Initialization baseline Git metadata must not contain links: {entry.FullName}");
                }

                if (entry is DirectoryInfo child)
                {
                    pending.Push(child);
                }
            }
        }

        var alternates = Path.Combine(gitDirectory, "objects", "info", "alternates");
        if (File.Exists(alternates))
        {
            throw new InvalidOperationException(
                "Initialization baseline Git object alternates are not supported.");
        }
    }

    private static void EnsureSamePath(string expected, string actual, string label)
    {
        if (actual.Length == 0)
        {
            throw new InvalidOperationException($"{label} resolved to an empty path.");
        }

        var expectedPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(expected));
        var actualPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(actual));
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        if (!string.Equals(expectedPath, actualPath, comparison))
        {
            throw new InvalidOperationException(
                $"{label} must remain inside the selected repository.");
        }
    }

    private static void EnsureOnlyEntries(string directory, params string[] expectedNames)
    {
        var actual = Directory
            .EnumerateFileSystemEntries(directory)
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var expected = expectedNames.Order(StringComparer.Ordinal).ToArray();
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
        {
            throw new InvalidOperationException(
                $"Initialization baseline has unexpected entries under: {directory}");
        }
    }

    private static InvalidOperationException NotResumable(string path, Exception exception)
    {
        return new InvalidOperationException(
            $"Target directory is not empty and is not a resumable baseline-only initialization: " +
            $"{path}. {exception.Message}",
            exception);
    }

    private static InvalidOperationException NotAttachable(string path, Exception exception)
    {
        return new InvalidOperationException(
            $"Existing repository is not a valid completed AEC initialization: {path}. " +
            exception.Message,
            exception);
    }

    private static bool MatchesOptionalBytes(byte[]? current, byte[] expected)
    {
        return current is not null && current.AsSpan().SequenceEqual(expected);
    }

    private sealed record BaselineSnapshot(
        string Commit,
        byte[] Content,
        byte[] WorkingSource,
        byte[] Config,
        byte[]? WorkingConfig,
        CodexPersonality? RuntimePersonality);

    private sealed record CompletedSnapshot(
        string Commit,
        byte[] Content,
        byte[] Config,
        AecInstructionBlock.RepositoryBinding Binding);

    private sealed record GitTreeEntry(string ObjectId);

    private enum BaselineFormat
    {
        LegacyAgentsOnly,
        ManagedEnvironment
    }
}
