namespace Aec;

internal static class InitCommand
{
    private const string InitializationCommitMessage = "Initialize AEC instructions";
    private const string RebindCommitMessage = "Rebind AEC repository path";

    public static int Run(
        string directoryPath,
        string codexHome,
        bool forcePathChange,
        TextWriter output)
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
                output);
        }

        if (!isFreshTarget && forcePathChange)
        {
            throw new InvalidOperationException(
                "--force-path-change requires an existing initialized repository, not a partial baseline.");
        }

        // Fresh and partial-baseline flows begin by capturing the live runtime.
        _ = AecApplication.ReadRequiredTextFile(runtimePath, "Runtime target");

        BaselineSnapshot baseline;
        if (isFreshTarget)
        {
            EnsureFreshCommitIdentity(directoryPath, targetWasMissing);
            AecSkillInstaller.Install(codexHome);
            InitializeRepository(directoryPath);

            BackupCommand.RunForInitialization(directoryPath, codexHome, TextWriter.Null);
            baseline = LoadBaseline(directoryPath, runtimePath);
        }
        else
        {
            try
            {
                baseline = LoadBaseline(directoryPath, runtimePath);
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

        var initializationCommit = BackupCommand.CommitCanonicalSource(
            directoryPath,
            InitializationCommitMessage,
            baseline.Commit,
            merged,
            allowEmpty: true);
        ApplyCommand.RunForInitialization(
            directoryPath,
            codexHome,
            TextWriter.Null,
            baseline.Content,
            initializationCommit,
            merged);

        VerifyInitializationResult(
            directoryPath,
            sourcePath,
            runtimePath,
            initializationCommit,
            merged);

        output.WriteLine("initialized");
        return 0;
    }

    private static int AttachCompletedRepository(
        string repository,
        string codexHome,
        string runtimePath,
        bool forcePathChange,
        CompletedSnapshot completed,
        TextWriter output)
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

        // Preflight an existing runtime before installing the skill or committing a
        // rebind; Apply repeats the read to protect against concurrent changes.
        _ = AecApplication.ReadOptionalTextFile(runtimePath, "Runtime target");

        if (pathMatches)
        {
            AecSkillInstaller.Install(codexHome);
            ApplyCommand.RunForAttachment(
                repository,
                codexHome,
                TextWriter.Null,
                completed.Commit,
                completed.Content);
            VerifyInitializationResult(
                repository,
                Path.Combine(repository, AecApplication.SourceRelativePath),
                runtimePath,
                completed.Commit,
                completed.Content);
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
            commit,
            rebound);
        VerifyInitializationResult(
            repository,
            sourcePath,
            runtimePath,
            commit,
            rebound);

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
        BackupCommand.EnsureNoChangesOutsideSource(repository);

        var commit = ApplyCommand.ResolveHeadCommit(repository);
        var content = ApplyCommand.ReadCommittedSource(repository, commit);
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
        return new CompletedSnapshot(commit, content, binding);
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
        return string.Equals(
                   ReadCommitSubject(repository, history[0]),
                   BackupCommand.CommitMessage,
                   StringComparison.Ordinal) &&
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
        _ = ReadOnlyBaselineTreeEntry(repository, baselineCommit);

        var parent = GitProcess.RunRequired(
            repository,
            "Git could not inspect the AEC initialization parent",
            "--no-replace-objects",
            "rev-parse",
            "--verify",
            $"{initializationCommit}^").Output.Trim();
        if (!string.Equals(parent, baselineCommit, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "AEC initialization commit does not directly follow its baseline commit.");
        }

        var initializationEntry = ReadOnlyBaselineTreeEntry(repository, initializationCommit);
        var initializedContent = ReadBoundedBlob(
            repository,
            initializationEntry.ObjectId,
            "AEC initialization canonical source");
        if (AecInstructionBlock.ReadRepositoryBinding(initializedContent) is null)
        {
            throw new InvalidDataException(
                "AEC initialization commit does not contain a supported initialized block.");
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
        byte[] expectedContent)
    {
        var head = ResolveBaselineHead(repository, "Git could not verify initialized HEAD");
        if (!string.Equals(head, expectedCommit, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Repository HEAD changed after the initialization commit.");
        }

        EnsureMainBranch(repository);
        BackupCommand.EnsureNoChangesOutsideSource(repository);
        var sourceStatus = GitProcess.RunRequired(
            repository,
            "Git could not verify the initialized canonical source",
            "status",
            "--porcelain=v1",
            "-z",
            "--untracked-files=all",
            "--",
            AecApplication.SourceRelativePath);
        if (sourceStatus.Output.Length != 0)
        {
            throw new InvalidOperationException(
                "Canonical source is not clean after initialization.");
        }

        VerifyContent(sourcePath, expectedContent, "Canonical source");
        VerifyContent(runtimePath, expectedContent, "Runtime target");
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

    private static BaselineSnapshot LoadBaseline(string repository, string runtimePath)
    {
        EnsureExactBaselineLayout(repository);
        BackupCommand.ValidateRepository(repository);
        BackupCommand.EnsureNoChangesOutsideSource(repository);
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
        if (!string.Equals(subject, BackupCommand.CommitMessage, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Initialization baseline commit subject must be '{BackupCommand.CommitMessage}'.");
        }

        var treeEntry = ReadOnlyBaselineTreeEntry(repository, commit);
        var content = ReadBoundedBlob(
            repository,
            treeEntry.ObjectId,
            "Initialization baseline canonical source");

        var runtime = AecApplication.ReadRequiredTextFile(runtimePath, "Runtime target");
        if (!runtime.AsSpan().SequenceEqual(content))
        {
            throw new InvalidOperationException(
                "Current runtime does not match the committed initialization baseline.");
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
        return new BaselineSnapshot(commit, content, workingSource);
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

    private static GitTreeEntry ReadOnlyBaselineTreeEntry(
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
        if (records.Length != 1)
        {
            throw new InvalidOperationException(
                "Initialization baseline must commit only the canonical source.");
        }

        var tab = records[0].IndexOf('\t');
        var fields = tab < 0
            ? []
            : records[0][..tab].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var path = tab < 0 ? string.Empty : records[0][(tab + 1)..];
        if (fields.Length != 3 ||
            fields[0] is not ("100644" or "100755") ||
            fields[1] != "blob" ||
            fields[2].Length == 0 ||
            !string.Equals(path, AecApplication.SourceRelativePath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Initialization baseline tree does not contain the canonical source exactly.");
        }

        return new GitTreeEntry(fields[2]);
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
        EnsureOnlyEntries(
            Path.Combine(repository, "environment", "providers", "codex"),
            "AGENTS.md");
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

    private sealed record BaselineSnapshot(string Commit, byte[] Content, byte[] WorkingSource);

    private sealed record CompletedSnapshot(
        string Commit,
        byte[] Content,
        AecInstructionBlock.RepositoryBinding Binding);

    private sealed record GitTreeEntry(string ObjectId);
}
