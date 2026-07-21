namespace Aec;

internal static class BackupCommand
{
    private const string CommitMessage = "Backup Codex AGENTS.md";
    private const string OutsideSourcePathspec =
        ":(top,exclude,literal)environment/providers/codex/AGENTS.md";

    public static int Run(string repository, string codexHome, TextWriter output)
    {
        ValidateRepository(repository);
        EnsureNoChangesOutsideSource(repository);

        var sourcePath = Path.Combine(repository, AecApplication.SourceRelativePath);
        var runtimePath = Path.Combine(codexHome, "AGENTS.md");
        var source = AecApplication.ReadRequiredTextFile(sourcePath, "Canonical source");
        var runtime = AecApplication.ReadRequiredTextFile(runtimePath, "Runtime target");

        if (!source.AsSpan().SequenceEqual(runtime))
        {
            ReplaceSource(sourcePath, runtime);
        }

        GitProcess.RunRequired(
            repository,
            "Git could not stage the canonical source",
            "add",
            "--force",
            "--",
            AecApplication.SourceRelativePath);

        EnsureNoChangesOutsideSource(repository);
        var expectedBlob = EnsureStagedBytesMatchSource(repository);

        if (HasHead(repository) && !HasStagedSourceChange(repository))
        {
            output.WriteLine("unchanged");
            return 0;
        }

        CommitWithoutHooks(repository);

        var head = GitProcess.RunRequired(
            repository,
            "Git could not resolve the new commit",
            "rev-parse",
            "--verify",
            "HEAD").Output.Trim();

        if (head.Length == 0)
        {
            throw new InvalidOperationException("Git returned an empty commit identifier.");
        }

        VerifyCommit(repository, expectedBlob);
        output.WriteLine($"committed {head}");
        return 0;
    }

    private static void ValidateRepository(string repository)
    {
        var insideWorkTree = GitProcess.RunRequired(
            repository,
            "Repository is not a Git work tree",
            "rev-parse",
            "--is-inside-work-tree").Output.Trim();
        if (!string.Equals(insideWorkTree, "true", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Repository is not a Git work tree: {repository}");
        }

        var bare = GitProcess.RunRequired(
            repository,
            "Git could not inspect the repository",
            "rev-parse",
            "--is-bare-repository").Output.Trim();
        if (!string.Equals(bare, "false", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Repository must not be bare: {repository}");
        }

        var prefix = GitProcess.RunRequired(
            repository,
            "Git could not locate the repository root",
            "rev-parse",
            "--show-prefix").Output;
        if (prefix.Trim().Length != 0)
        {
            throw new InvalidOperationException($"--repo must identify the Git repository root: {repository}");
        }

        var branch = GitProcess.Run(repository, "symbolic-ref", "--quiet", "HEAD");
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

        var unmerged = GitProcess.RunRequired(
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
            var gitPath = GitProcess.RunRequired(
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

    private static void EnsureNoChangesOutsideSource(string repository)
    {
        var status = GitProcess.RunRequired(
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

    private static void ReplaceSource(string sourcePath, byte[] content)
    {
        var directory = Path.GetDirectoryName(sourcePath)
            ?? throw new InvalidOperationException($"Canonical source has no parent directory: {sourcePath}");
        var temporaryPath = Path.Combine(directory, $".AGENTS.md.aec-{Guid.NewGuid():N}");

        try
        {
            using (var stream = new FileStream(
                       temporaryPath,
                       FileMode.CreateNew,
                       FileAccess.Write,
                       FileShare.None))
            {
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, sourcePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        var written = AecApplication.ReadRequiredTextFile(sourcePath, "Canonical source");
        if (!written.AsSpan().SequenceEqual(content))
        {
            throw new IOException("Canonical source verification failed after writing the runtime data.");
        }
    }

    private static string EnsureStagedBytesMatchSource(string repository)
    {
        var sourceHash = GitProcess.RunRequired(
            repository,
            "Git could not hash the canonical source",
            "hash-object",
            "--no-filters",
            "--",
            AecApplication.SourceRelativePath).Output.Trim();
        var stagedHash = GitProcess.RunRequired(
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

    private static void CommitWithoutHooks(string repository)
    {
        var hooksDirectory = Directory.CreateTempSubdirectory("aec-hooks-");

        try
        {
            GitProcess.RunRequired(
                repository,
                "Git could not commit the canonical source",
                "-c",
                $"core.hooksPath={hooksDirectory.FullName}",
                "commit",
                "--quiet",
                "--only",
                "--message",
                CommitMessage,
                "--",
                AecApplication.SourceRelativePath);
        }
        finally
        {
            try
            {
                hooksDirectory.Delete(recursive: true);
            }
            catch
            {
                // Temporary-hook cleanup must not hide the Git outcome.
            }
        }
    }

    private static void VerifyCommit(string repository, string expectedBlob)
    {
        var committedBlob = GitProcess.RunRequired(
            repository,
            "Git could not inspect the committed canonical source",
            "rev-parse",
            $"HEAD:{AecApplication.SourceRelativePath}").Output.Trim();
        if (!string.Equals(expectedBlob, committedBlob, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Committed canonical source bytes do not match the runtime data.");
        }

        var subject = GitProcess.RunRequired(
            repository,
            "Git could not inspect the backup commit message",
            "log",
            "-1",
            "--format=%s").Output.TrimEnd('\r', '\n');
        if (!string.Equals(subject, CommitMessage, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Backup commit subject does not match the required message.");
        }

        var sourceStatus = GitProcess.RunRequired(
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
                "Canonical source is not clean after the backup commit.");
        }
    }

    private static bool HasHead(string repository)
    {
        var result = GitProcess.Run(repository, "rev-parse", "--verify", "--quiet", "HEAD");
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
        var result = GitProcess.Run(
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
}
