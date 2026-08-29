namespace Aec;

internal static class BackupCommand
{
    internal const string CommitMessage = "Backup Codex AGENTS.md";
    internal const string OrdinaryCommitMessage = "Backup Codex environment";
    private const string OutsideSourcePathspec =
        ":(top,exclude,literal)environment/providers/codex/AGENTS.md";
    private const string OutsideConfigSourcePathspec =
        ":(top,exclude,literal)environment/providers/codex/config.toml";

    public static int Run(
        string repository,
        string codexHome,
        TextWriter output,
        TextWriter warning)
    {
        return RunManagedEnvironment(
            repository,
            codexHome,
            output,
            warning,
            initialization: null);
    }

    internal static int RunForInitialization(
        string repository,
        string codexHome,
        CodexPersonality? expectedRuntimePersonality,
        TextWriter output)
    {
        return RunManagedEnvironment(
            repository,
            codexHome,
            output,
            TextWriter.Null,
            new InitializationBackupExpectation(expectedRuntimePersonality));
    }

    private static int RunManagedEnvironment(
        string repository,
        string codexHome,
        TextWriter output,
        TextWriter warning,
        InitializationBackupExpectation? initialization)
    {
        ValidateRepository(repository);
        EnsureNoChangesOutsideManagedSources(repository);

        var sourcePath = Path.Combine(repository, AecApplication.SourceRelativePath);
        var runtimePath = Path.Combine(codexHome, "AGENTS.md");
        var configSourcePath = Path.Combine(repository, AecApplication.ConfigSourceRelativePath);
        var runtimeConfigPath = Path.Combine(codexHome, "config.toml");

        // Read and validate every input before replacing either canonical file. This
        // prevents a bad or incomplete runtime config from partially capturing AGENTS.md.
        var source = AecApplication.ReadRequiredTextFile(sourcePath, "Canonical source");
        var runtime = AecApplication.ReadRequiredTextFile(runtimePath, "Runtime target");
        var configSource = AecApplication.ReadOptionalTextFile(
            configSourcePath,
            "Canonical config");
        var runtimeConfig = AecApplication.ReadOptionalTextFile(
            runtimeConfigPath,
            "Runtime config");
        var runtimePersonality = runtimeConfig is null
            ? null
            : CodexPersonalityConfig.ReadRuntime(runtimeConfig, runtimeConfigPath);
        CodexPersonality desiredPersonality;
        if (initialization is not null)
        {
            if (runtimePersonality != initialization.RuntimePersonality)
            {
                throw new InvalidOperationException(
                    "Runtime personality changed after initialization preflight; " +
                    "the baseline was not committed.");
            }

            desiredPersonality = runtimePersonality ?? CodexPersonality.None;
        }
        else if (runtimePersonality is null)
        {
            warning.WriteLine(
                "warning: runtime config does not declare root `personality`; " +
                "backup stopped without changing the repository.");
            return 1;
        }
        else
        {
            desiredPersonality = runtimePersonality.Value;
        }

        var configUpdate = CodexPersonalityConfig.PlanCanonicalUpdate(
            configSource,
            configSourcePath,
            desiredPersonality);

        if (!source.AsSpan().SequenceEqual(runtime))
        {
            AtomicFile.ReplaceIfUnchanged(
                sourcePath,
                source,
                runtime,
                "Canonical source");
        }

        if (configUpdate.Changed)
        {
            AtomicFile.ReplaceIfUnchanged(
                configSourcePath,
                configSource,
                configUpdate.Content,
                "Canonical config");
        }

        StageManagedSources(repository);
        EnsureNoChangesOutsideManagedSources(repository);
        var expectedSourceBlob = EnsureStagedBytesMatchWorkingFile(
            repository,
            AecApplication.SourceRelativePath,
            "canonical source");
        var expectedConfigBlob = EnsureStagedBytesMatchWorkingFile(
            repository,
            AecApplication.ConfigSourceRelativePath,
            "canonical config");

        if (HasHead(repository) && !HasStagedManagedChange(repository))
        {
            output.WriteLine("unchanged");
            return 0;
        }

        CommitStagedIndex(
            repository,
            OrdinaryCommitMessage,
            [
                (AecApplication.SourceRelativePath, expectedSourceBlob),
                (AecApplication.ConfigSourceRelativePath, expectedConfigBlob)
            ]);

        var head = ResolveHead(repository);
        VerifyManagedCommit(
            repository,
            expectedSourceBlob,
            expectedConfigBlob,
            OrdinaryCommitMessage);
        output.WriteLine($"committed {head}");
        return 0;
    }

    internal static string CommitCanonicalSource(
        string repository,
        string commitMessage,
        string expectedParent,
        byte[] expectedSource,
        bool allowEmpty)
    {
        ArgumentNullException.ThrowIfNull(expectedSource);
        ValidateRepository(repository);
        EnsureNoChangesOutsideSource(repository);
        EnsureBranchMatches(repository, "refs/heads/main");
        EnsureHeadMatches(repository, expectedParent);

        EnsureSourceMatchesExpected(repository, expectedSource);
        StageCanonicalSource(repository);
        var expectedBlob = EnsureStagedBytesMatchSource(repository);
        EnsureBlobMatchesExpected(repository, expectedBlob, expectedSource);
        if (!allowEmpty && !HasStagedSourceChange(repository))
        {
            throw new InvalidOperationException("Canonical source has no change to commit.");
        }

        // Recheck the parent after staging so a concurrent checkout cannot redirect the commit.
        EnsureBranchMatches(repository, "refs/heads/main");
        EnsureHeadMatches(repository, expectedParent);
        CommitStagedIndex(
            repository,
            commitMessage,
            [(AecApplication.SourceRelativePath, expectedBlob)],
            "refs/heads/main",
            expectedParent);

        var head = ResolveHead(repository);
        var actualParent = RunRequired(
            repository,
            "Git could not verify the initialization commit parent",
            "rev-parse",
            "--verify",
            "HEAD^").Output.Trim();
        if (!string.Equals(actualParent, expectedParent, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Initialization commit parent changed during the operation.");
        }

        VerifyCommit(repository, expectedBlob, commitMessage);
        EnsureBranchMatches(repository, "refs/heads/main");
        EnsureNoChangesOutsideSource(repository);
        return head;
    }

    internal static string CommitCanonicalEnvironment(
        string repository,
        string commitMessage,
        string expectedParent,
        byte[] expectedSource,
        byte[] expectedConfig,
        bool allowEmpty)
    {
        ArgumentNullException.ThrowIfNull(expectedSource);
        ArgumentNullException.ThrowIfNull(expectedConfig);
        ValidateRepository(repository);
        EnsureNoChangesOutsideManagedSources(repository);
        EnsureBranchMatches(repository, "refs/heads/main");
        EnsureHeadMatches(repository, expectedParent);

        EnsureWorkingFileMatchesExpected(
            repository,
            AecApplication.SourceRelativePath,
            expectedSource,
            "Canonical source");
        EnsureWorkingFileMatchesExpected(
            repository,
            AecApplication.ConfigSourceRelativePath,
            expectedConfig,
            "Canonical config");
        StageManagedSources(repository);
        var expectedSourceBlob = EnsureStagedBytesMatchWorkingFile(
            repository,
            AecApplication.SourceRelativePath,
            "canonical source");
        var expectedConfigBlob = EnsureStagedBytesMatchWorkingFile(
            repository,
            AecApplication.ConfigSourceRelativePath,
            "canonical config");
        EnsureBlobMatchesExpected(
            repository,
            expectedSourceBlob,
            expectedSource,
            "canonical source");
        EnsureBlobMatchesExpected(
            repository,
            expectedConfigBlob,
            expectedConfig,
            "canonical config");
        if (!allowEmpty && !HasStagedManagedChange(repository))
        {
            throw new InvalidOperationException(
                "Canonical environment has no change to commit.");
        }

        EnsureNoChangesOutsideManagedSources(repository);
        EnsureBranchMatches(repository, "refs/heads/main");
        EnsureHeadMatches(repository, expectedParent);
        CommitStagedIndex(
            repository,
            commitMessage,
            [
                (AecApplication.SourceRelativePath, expectedSourceBlob),
                (AecApplication.ConfigSourceRelativePath, expectedConfigBlob)
            ],
            "refs/heads/main",
            expectedParent);

        var head = ResolveHead(repository);
        VerifyCommitParent(repository, expectedParent);
        VerifyManagedCommit(
            repository,
            expectedSourceBlob,
            expectedConfigBlob,
            commitMessage);
        EnsureBranchMatches(repository, "refs/heads/main");
        EnsureNoChangesOutsideManagedSources(repository);
        return head;
    }

    internal static void ValidateRepository(string repository)
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

        var branch = Run(repository, "symbolic-ref", "--quiet", "HEAD");
        if (branch.ExitCode == 1)
        {
            throw new InvalidOperationException("Backup requires a symbolic Git branch; detached HEAD is not supported.");
        }

        if (branch.ExitCode != 0 || branch.Output.Trim().Length == 0)
        {
            throw new InvalidOperationException(
                $"Git could not resolve the current symbolic branch (exit code {branch.ExitCode}).");
        }

        if (!branch.Output.Trim().StartsWith("refs/heads/", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Backup requires HEAD to reference a local Git branch under refs/heads/.");
        }

        var unmerged = RunRequired(
            repository,
            "Git could not inspect unmerged files",
            "ls-files",
            "--unmerged",
            "-z");
        if (unmerged.Output.Length != 0)
        {
            throw new InvalidOperationException("Backup cannot run while the repository has unmerged files.");
        }

        foreach (var operation in new[]
                 {
                     "MERGE_HEAD",
                     "CHERRY_PICK_HEAD",
                     "REVERT_HEAD",
                     "BISECT_LOG",
                     "rebase-merge",
                     "rebase-apply",
                     "sequencer"
                 })
        {
            var gitPath = RunRequired(
                repository,
                $"Git could not inspect operation {operation}",
                "rev-parse",
                "--git-path",
                operation).Output.Trim();
            if (gitPath.Length == 0)
            {
                throw new InvalidOperationException($"Git returned an empty path for operation {operation}.");
            }

            var operationPath = Path.IsPathFullyQualified(gitPath)
                ? gitPath
                : Path.GetFullPath(gitPath, repository);
            if (File.Exists(operationPath) || Directory.Exists(operationPath))
            {
                throw new InvalidOperationException(
                    $"Backup cannot run while Git operation {operation} is active.");
            }
        }
    }

    internal static void EnsureNoChangesOutsideSource(string repository)
    {
        var status = RunRequired(
            repository,
            "Git could not inspect repository changes",
            "status",
            "--porcelain=v1",
            "-z",
            "--untracked-files=all",
            "--",
            ".",
            OutsideSourcePathspec);

        if (status.Output.Length != 0)
        {
            throw new InvalidOperationException(
                "Repository has changes outside environment/providers/codex/AGENTS.md.");
        }
    }

    internal static void EnsureNoChangesOutsideManagedSources(string repository)
    {
        var status = RunRequired(
            repository,
            "Git could not inspect repository changes",
            "status",
            "--porcelain=v1",
            "-z",
            "--untracked-files=all",
            "--",
            ".",
            OutsideSourcePathspec,
            OutsideConfigSourcePathspec);

        if (status.Output.Length != 0)
        {
            throw new InvalidOperationException(
                "Repository has changes outside the managed Codex environment files.");
        }
    }

    private static string EnsureStagedBytesMatchSource(string repository)
    {
        var sourceHash = RunRequired(
            repository,
            "Git could not hash the canonical source",
            "hash-object",
            "--no-filters",
            "--",
            AecApplication.SourceRelativePath).Output.Trim();
        var stagedHash = RunRequired(
            repository,
            "Git could not inspect the staged canonical source",
            "rev-parse",
            $":{AecApplication.SourceRelativePath}").Output.Trim();

        if (!string.Equals(sourceHash, stagedHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Git filters changed the canonical source bytes while staging; backup stopped before commit.");
        }

        return sourceHash;
    }

    private static string EnsureStagedBytesMatchWorkingFile(
        string repository,
        string relativePath,
        string label)
    {
        var sourceHash = RunRequired(
            repository,
            $"Git could not hash the {label}",
            "hash-object",
            "--no-filters",
            "--",
            relativePath).Output.Trim();
        var stagedHash = RunRequired(
            repository,
            $"Git could not inspect the staged {label}",
            "rev-parse",
            $":{relativePath}").Output.Trim();

        if (!string.Equals(sourceHash, stagedHash, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Git filters changed the {label} bytes while staging; backup stopped before commit.");
        }

        return sourceHash;
    }

    private static void StageCanonicalSource(string repository)
    {
        RunRequired(
            repository,
            "Git could not stage the canonical source",
            "add",
            "--force",
            "--",
            AecApplication.SourceRelativePath);
    }

    private static void StageManagedSources(string repository)
    {
        RunRequired(
            repository,
            "Git could not stage the managed Codex environment files",
            "add",
            "--force",
            "--",
            AecApplication.SourceRelativePath,
            AecApplication.ConfigSourceRelativePath);
    }

    private static void EnsureSourceMatchesExpected(string repository, byte[] expected)
    {
        var sourcePath = Path.Combine(repository, AecApplication.SourceRelativePath);
        var source = AecApplication.ReadRequiredTextFile(sourcePath, "Canonical source");
        if (!source.AsSpan().SequenceEqual(expected))
        {
            throw new InvalidOperationException(
                "Canonical source changed before the initialization commit.");
        }
    }

    private static void EnsureWorkingFileMatchesExpected(
        string repository,
        string relativePath,
        byte[] expected,
        string label)
    {
        var path = Path.Combine(repository, relativePath);
        var actual = AecApplication.ReadRequiredTextFile(path, label);
        if (!actual.AsSpan().SequenceEqual(expected))
        {
            throw new InvalidOperationException(
                $"{label} changed before the initialization commit.");
        }
    }

    private static void EnsureBlobMatchesExpected(
        string repository,
        string blob,
        byte[] expected)
    {
        EnsureBlobMatchesExpected(
            repository,
            blob,
            expected,
            "canonical source");
    }

    private static void EnsureBlobMatchesExpected(
        string repository,
        string blob,
        byte[] expected,
        string label)
    {
        var staged = RunRequiredBytes(
            repository,
            $"Git could not read the staged {label}",
            "cat-file",
            "blob",
            blob);
        if (!staged.AsSpan().SequenceEqual(expected))
        {
            throw new InvalidOperationException(
                $"Staged {label} does not match the expected initialization content.");
        }
    }

    private static void VerifyCommitParent(string repository, string expectedParent)
    {
        var actualParent = RunRequired(
            repository,
            "Git could not verify the initialization commit parent",
            "rev-parse",
            "--verify",
            "HEAD^").Output.Trim();
        if (!string.Equals(actualParent, expectedParent, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Initialization commit parent changed during the operation.");
        }
    }

    internal static void CommitStagedIndex(
        string repository,
        string commitMessage,
        IReadOnlyList<(string RelativePath, string Blob)> expectedFiles)
    {
        var expectedBranch = ResolveBranch(repository);
        var expectedParent = HasHead(repository) ? ResolveHead(repository) : null;
        CommitStagedIndex(
            repository,
            commitMessage,
            expectedFiles,
            expectedBranch,
            expectedParent);
    }

    private static void CommitStagedIndex(
        string repository,
        string commitMessage,
        IReadOnlyList<(string RelativePath, string Blob)> expectedFiles,
        string expectedBranch,
        string? expectedParent)
    {
        if (expectedFiles.Count == 0)
        {
            throw new ArgumentException("At least one verified staged file is required.");
        }

        EnsureBranchMatches(repository, expectedBranch);
        EnsureParentMatches(repository, expectedParent);
        var expectedTree = RunRequired(
            repository,
            "Git could not pin the verified canonical index",
            "write-tree").Output.Trim();
        VerifyPinnedTree(repository, expectedTree, expectedParent, expectedFiles);

        var arguments = new List<string> { "commit-tree", expectedTree };
        if (expectedParent is not null)
        {
            arguments.AddRange(["-p", expectedParent]);
        }

        if (ShouldSignCommit(repository))
        {
            // commit-tree does not read commit.gpgSign automatically. Supplying
            // -S retains Git's configured signing format, key, and failure behavior.
            arguments.Add("-S");
        }

        arguments.AddRange(["-m", commitMessage]);

        // commit-tree records the already-verified index tree. Unlike
        // `git commit --only`, it cannot reread a concurrently changed working file.
        var commit = RunRequired(
            repository,
            "Git could not commit the canonical source",
            [.. arguments]).Output.Trim();
        if (commit.Length == 0)
        {
            throw new InvalidOperationException("Git returned an empty canonical commit identifier.");
        }

        EnsureBranchMatches(repository, expectedBranch);
        EnsureParentMatches(repository, expectedParent);

        // update-ref compares the old value before moving the branch, so a
        // concurrent commit cannot be silently replaced by this backup.
        var expectedOld = expectedParent ?? new string('0', commit.Length);
        RunRequired(
            repository,
            "Git could not publish the canonical source commit",
            "update-ref",
            "-m",
            $"commit: {commitMessage}",
            expectedBranch,
            commit,
            expectedOld);
        EnsureBranchMatches(repository, expectedBranch);
    }

    private static void VerifyPinnedTree(
        string repository,
        string tree,
        string? parent,
        IReadOnlyList<(string RelativePath, string Blob)> expectedFiles)
    {
        var allowedPaths = new HashSet<string>(
            expectedFiles.Select(file => file.RelativePath),
            StringComparer.Ordinal);
        if (allowedPaths.Count != expectedFiles.Count)
        {
            throw new ArgumentException("Verified staged file paths must be unique.");
        }

        var changedPaths = parent is null
            ? RunRequired(
                repository,
                "Git could not inspect the pinned initial tree",
                "ls-tree",
                "--name-only",
                "-r",
                "-z",
                tree).Output
            : RunRequired(
                repository,
                "Git could not inspect the pinned canonical tree",
                "diff-tree",
                "--no-commit-id",
                "--name-only",
                "-r",
                "-z",
                parent,
                tree).Output;

        foreach (var path in changedPaths.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!allowedPaths.Contains(path))
            {
                throw new InvalidOperationException(
                    "Pinned Git index contains changes outside the approved canonical paths; " +
                    "commit stopped before moving the branch.");
            }
        }

        foreach (var expected in expectedFiles)
        {
            var actualBlob = RunRequired(
                repository,
                $"Git could not inspect pinned canonical path {expected.RelativePath}",
                "rev-parse",
                $"{tree}:{expected.RelativePath}").Output.Trim();
            if (!string.Equals(actualBlob, expected.Blob, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Pinned canonical path changed after verification: {expected.RelativePath}");
            }
        }
    }

    private static bool ShouldSignCommit(string repository)
    {
        var result = Run(
            repository,
            "config",
            "--bool",
            "--get",
            "commit.gpgSign");
        if (result.ExitCode == 1)
        {
            return false;
        }

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Git could not read commit.gpgSign (exit code {result.ExitCode}).");
        }

        return result.Output.Trim() switch
        {
            "true" => true,
            "false" => false,
            var value => throw new InvalidOperationException(
                $"Git returned an invalid commit.gpgSign value: {value}")
        };
    }

    private static void VerifyCommit(
        string repository,
        string expectedBlob,
        string expectedSubject)
    {
        var committedBlob = RunRequired(
            repository,
            "Git could not inspect the committed canonical source",
            "rev-parse",
            $"HEAD:{AecApplication.SourceRelativePath}").Output.Trim();
        if (!string.Equals(expectedBlob, committedBlob, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Committed canonical source bytes do not match the expected source.");
        }

        var subject = RunRequired(
            repository,
            "Git could not inspect the canonical source commit message",
            "log",
            "-1",
            "--format=%s").Output.TrimEnd('\r', '\n');
        if (!string.Equals(subject, expectedSubject, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Canonical source commit subject does not match the required message.");
        }

        var sourceStatus = RunRequired(
            repository,
            "Git could not verify the canonical source after commit",
            "status",
            "--porcelain=v1",
            "-z",
            "--untracked-files=all",
            "--",
            AecApplication.SourceRelativePath);
        if (sourceStatus.Output.Length != 0)
        {
            throw new InvalidOperationException(
                "Canonical source is not clean after its commit.");
        }
    }

    private static void VerifyManagedCommit(
        string repository,
        string expectedSourceBlob,
        string expectedConfigBlob,
        string expectedSubject)
    {
        VerifyCommittedBlob(
            repository,
            AecApplication.SourceRelativePath,
            expectedSourceBlob,
            "canonical source");
        VerifyCommittedBlob(
            repository,
            AecApplication.ConfigSourceRelativePath,
            expectedConfigBlob,
            "canonical config");

        var subject = RunRequired(
            repository,
            "Git could not inspect the managed environment commit message",
            "log",
            "-1",
            "--format=%s").Output.TrimEnd('\r', '\n');
        if (!string.Equals(subject, expectedSubject, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Managed environment commit subject does not match the required message.");
        }

        var status = RunRequired(
            repository,
            "Git could not verify the managed environment after commit",
            "status",
            "--porcelain=v1",
            "-z",
            "--untracked-files=all",
            "--",
            AecApplication.SourceRelativePath,
            AecApplication.ConfigSourceRelativePath);
        if (status.Output.Length != 0)
        {
            throw new InvalidOperationException(
                "Managed Codex environment files are not clean after their commit.");
        }
    }

    private static void VerifyCommittedBlob(
        string repository,
        string relativePath,
        string expectedBlob,
        string label)
    {
        var committedBlob = RunRequired(
            repository,
            $"Git could not inspect the committed {label}",
            "rev-parse",
            $"HEAD:{relativePath}").Output.Trim();
        if (!string.Equals(expectedBlob, committedBlob, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Committed {label} bytes do not match the expected source.");
        }
    }

    private static bool HasHead(string repository)
    {
        var result = Run(repository, "rev-parse", "--verify", "--quiet", "HEAD");
        return result.ExitCode switch
        {
            0 => true,
            1 => false,
            _ => throw new InvalidOperationException(
                $"Git could not inspect HEAD (exit code {result.ExitCode}).")
        };
    }

    private static bool HasStagedSourceChange(string repository)
    {
        var result = Run(
            repository,
            "diff",
            "--cached",
            "--quiet",
            "--exit-code",
            "--",
            AecApplication.SourceRelativePath);

        return result.ExitCode switch
        {
            0 => false,
            1 => true,
            _ => throw new InvalidOperationException(
                $"Git could not inspect the staged canonical source (exit code {result.ExitCode}).")
        };
    }

    private static bool HasStagedManagedChange(string repository)
    {
        var result = Run(
            repository,
            "diff",
            "--cached",
            "--quiet",
            "--exit-code",
            "--",
            AecApplication.SourceRelativePath,
            AecApplication.ConfigSourceRelativePath);

        return result.ExitCode switch
        {
            0 => false,
            1 => true,
            _ => throw new InvalidOperationException(
                $"Git could not inspect staged managed environment files (exit code {result.ExitCode}).")
        };
    }

    private static string ResolveHead(string repository)
    {
        var head = RunRequired(
            repository,
            "Git could not resolve the new commit",
            "rev-parse",
            "--verify",
            "HEAD").Output.Trim();
        return head.Length == 0
            ? throw new InvalidOperationException("Git returned an empty commit identifier.")
            : head;
    }

    private static void EnsureHeadMatches(string repository, string expected)
    {
        var actual = ResolveHead(repository);
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Repository HEAD changed during canonical source commit.");
        }
    }

    private static void EnsureParentMatches(string repository, string? expected)
    {
        if (expected is null)
        {
            if (HasHead(repository))
            {
                throw new InvalidOperationException(
                    "Repository HEAD changed before the canonical commit was published.");
            }

            return;
        }

        EnsureHeadMatches(repository, expected);
    }

    private static string ResolveBranch(string repository)
    {
        var branch = RunRequired(
            repository,
            "Git could not resolve the branch for the canonical commit",
            "symbolic-ref",
            "--quiet",
            "HEAD").Output.Trim();
        return branch.Length == 0
            ? throw new InvalidOperationException("Git returned an empty canonical branch name.")
            : branch;
    }

    private static void EnsureBranchMatches(string repository, string expected)
    {
        var actual = RunRequired(
            repository,
            "Git could not verify the canonical source commit branch",
            "symbolic-ref",
            "--quiet",
            "HEAD").Output.Trim();
        if (!string.Equals(actual, expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Repository branch changed during the canonical source commit.");
        }
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

    private static byte[] RunRequiredBytes(
        string repository,
        string failureMessage,
        params string[] arguments)
    {
        return GitProcess.RunRequiredBytes(
            repository,
            failureMessage,
            ["--no-replace-objects", .. arguments]);
    }

    private sealed record InitializationBackupExpectation(
        CodexPersonality? RuntimePersonality);
}
