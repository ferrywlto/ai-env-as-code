namespace Aec;

// Reads working bytes only after verifying them against the selected Git commit.
internal static class CommittedFileReader
{
    internal static byte[][] ReadCommittedFiles(
        string repository, string commit, params (string Path, string Label)[] files)
    {
        byte[] ReadWorkingFile((string Path, string Label) file)
        {
            var path = Path.Combine(repository, file.Path);
            AecApplication.EnsureNoLinksInExistingPath(Path.GetDirectoryName(path)!, $"{file.Label} path");
            return AecApplication.ReadRequiredTextFile(path, file.Label);
        }

        byte[][] ReadIndividually() => files.Select(file =>
        {
            var path = Path.Combine(repository, file.Path);
            AecApplication.EnsureNoLinksInExistingPath(Path.GetDirectoryName(path)!, $"{file.Label} path");
            return ReadCommittedFile(repository, commit, file.Path, file.Label);
        }).ToArray();

        byte[][] before;
        try
        {
            before = files.Select(ReadWorkingFile).ToArray();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or
            InvalidOperationException or InvalidDataException)
        {
            _ = ReadIndividually();
            throw;
        }

        var paths = files.Select(file => file.Path).ToArray();
        var tree = Run(repository, ["ls-tree", "-z", commit, "--", .. paths]);
        var staged = Run(repository, ["--no-optional-locks", "diff", "--cached", "--quiet",
            "--exit-code", "--no-ext-diff", "--no-textconv", commit, "--", .. paths]);
        var modes = Run(repository, ["--no-optional-locks", "diff-files", "--raw", "-z",
            "--no-renames", "--no-ext-diff", "--no-textconv", "--", .. paths]);
        var hashes = Run(repository, ["hash-object", "--no-filters", "--", .. paths]);
        if (tree.ExitCode != 0 || staged.ExitCode != 0 || modes.ExitCode != 0 || hashes.ExitCode != 0 ||
            !BatchedBlobsMatch(tree.Output, hashes.Output, paths) || !BatchedModesMatch(modes.Output, paths))
        {
            // Preserve the single-path diagnostic. A transient mismatch remains a refusal.
            _ = ReadIndividually();
            throw new InvalidOperationException("Committed files changed while Git provenance was checked.");
        }

        for (var index = 0; index < files.Length; index++)
        {
            if (!before[index].AsSpan().SequenceEqual(ReadWorkingFile(files[index])))
            {
                throw new InvalidOperationException($"{files[index].Label} changed while its Git provenance was checked.");
            }
        }

        return before;
    }

    private static bool BatchedBlobsMatch(string tree, string hashes, string[] paths)
    {
        if (!tree.EndsWith('\0'))
        {
            return false;
        }

        var blobs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var entry in tree[..^1].Split('\0'))
        {
            var tab = entry.IndexOf('\t');
            if (tab < 0)
            {
                return false;
            }

            var fields = entry[..tab].Split(' ');
            if (fields.Length != 3 || fields[0] is not ("100644" or "100755") ||
                fields[1] != "blob" || fields[2].Length == 0 || !blobs.TryAdd(entry[(tab + 1)..], fields[2]))
            {
                return false;
            }
        }

        var working = hashes.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return blobs.Count == paths.Length && working.Length == paths.Length &&
            paths.Select((path, index) => blobs.TryGetValue(path, out var blob) && blob == working[index])
                .All(matches => matches);
    }

    private static bool BatchedModesMatch(string output, string[] paths)
    {
        if (output.Length == 0)
        {
            return true;
        }

        var records = output.Split('\0');
        if (records.Length % 2 != 1 || records[^1].Length != 0)
        {
            return false;
        }

        var unmatched = paths.ToHashSet(StringComparer.Ordinal);
        for (var index = 0; index < records.Length - 1; index += 2)
        {
            var fields = records[index].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // A stat-only modification retains the regular file mode. Raw bytes are checked separately.
            if (!unmatched.Remove(records[index + 1]) || fields.Length != 5 ||
                fields[0] is not (":100644" or ":100755") || fields[4] != "M" || fields[0][1..] != fields[1])
            {
                return false;
            }
        }

        return true;
    }

    internal static byte[] ReadCommittedFile(
        string repository,
        string commit,
        string relativePath,
        string label)
    {
        var sourcePath = Path.Combine(repository, relativePath);
        var source = AecApplication.ReadRequiredTextFile(sourcePath, label);
        var committedBlob = ResolveCommittedBlob(repository, commit, relativePath, label);

        // Check commit-to-index changes separately. Worktree content and mode
        // checks below must not refresh the index or reject stat-only changes.
        EnsureDiffIsClean(
            Run(
                repository,
                "--no-optional-locks",
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
        EnsureWorkingModeMatchesIndex(repository, relativePath, label);

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
                $"{label} has unstaged changes: bytes do not exactly match the committed Git blob; " +
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

    private static void EnsureWorkingModeMatchesIndex(string repository, string relativePath, string label)
    {
        // The plumbing command does not refresh the on-disk index. Let Git
        // interpret core.filemode and platform permissions instead of guessing
        // Unix executable semantics on Windows. Content is checked by raw hash.
        var changes = RunRequired(repository, $"Git could not inspect {label.ToLowerInvariant()} mode",
            "--no-optional-locks", "diff-files", "--raw", "-z", "--no-renames",
            "--no-ext-diff", "--no-textconv", "--", relativePath).Output;
        if (changes.Length == 0) return;

        // One raw record: metadata NUL exact-path NUL. A stat-only M record
        // with equal modes is not a content change; the raw hash decides that.
        var records = changes.Split('\0');
        if (records.Length != 3 || records[1] != relativePath || records[2].Length != 0)
            throw new InvalidOperationException($"Git returned malformed {label.ToLowerInvariant()} mode metadata.");
        var fields = records[0].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 5 || fields[0] is not (":100644" or ":100755"))
            throw new InvalidOperationException($"Git returned malformed {label.ToLowerInvariant()} mode metadata.");
        if (fields[4] != "M" || fields[0][1..] != fields[1])
            throw new InvalidOperationException($"{label} has unstaged changes (file mode or type).");
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

}
