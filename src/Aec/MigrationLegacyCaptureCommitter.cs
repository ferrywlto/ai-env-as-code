namespace Aec;

// Own the immediate write-to-commit boundary: a fresh preview cannot be read
// after writing because canonical working files intentionally differ from HEAD.
internal static class MigrationLegacyCaptureCommitter
{
    private const string CommitMessage = "Backup legacy environment before migration";

    public static string? WriteAndCommit(MigrationLegacyCapturePreview reviewed)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        var repository = reviewed.Preparation.Preflight.Repository;
        MigrationFastForward.EnsureSafeGitConfiguration(repository);
        EnsureEmptyIndex(repository);
        // Retain privately derived buffers; caller-owned arrays never authorize
        // staging. The writer independently compares reviewed and current inputs.
        var fresh = MigrationLegacyCapturePreview.Read(reviewed.Preparation);
        var parent = reviewed.Preparation.Preflight.Commit;
        var changed = fresh.Files.Where(file => file.HasChanges).ToArray();
        var paths = changed.Select(file => file.RelativePath).ToArray();
        try
        {
            var written = MigrationLegacyCaptureWriter.Write(reviewed);
            if (!written.SequenceEqual(paths))
            {
                throw new InvalidOperationException("Completed capture paths differ from the fresh proposal.");
            }
            VerifyInputs(parent);
            EnsureEmptyIndex(repository);
            if (paths.Length == 0) return null;

            var modes = changed.ToDictionary(file => file.RelativePath, file =>
            {
                var entry = GitProcess.RunRequired(repository, "Could not read legacy file mode",
                    "--no-replace-objects", "ls-tree", "-z", parent, "--", file.RelativePath).Output;
                return entry.Split(' ')[0];
            }, StringComparer.Ordinal);
            VerifyInputs(parent);
            EnsureEmptyIndex(repository);
            GitProcess.RunRequired(repository, "Could not stage captured canonical files",
                ["add", "--", .. paths]);

            // Reject newline transforms, executable-bit changes and any extra
            // index path rather than silently committing Git's transformed bytes.
            var expected = new List<(string RelativePath, string Blob)>();
            foreach (var file in changed)
            {
                var indexed = GitProcess.RunRequired(repository, "Could not inspect captured index entry",
                    "ls-files", "--stage", "-z", "--", file.RelativePath).Output;
                var blob = GitProcess.RunRequired(repository, "Could not resolve captured index blob",
                    "rev-parse", $":{file.RelativePath}").Output.Trim();
                var bytes = GitProcess.RunRequiredBytes(repository, "Could not read captured index blob",
                    "--no-replace-objects", "cat-file", "blob", blob);
                // Compare actual object bytes to private proposal buffers, not a
                // hash obtained from a working file that another process can edit.
                if (indexed != $"{modes[file.RelativePath]} {blob} 0\t{file.RelativePath}\0" ||
                    !bytes.AsSpan().SequenceEqual(file.ProposedCanonical))
                {
                    throw new InvalidOperationException("Staged capture bytes or file mode differ from review.");
                }
                expected.Add((file.RelativePath, blob));
            }
            VerifyPaths("diff", "--cached", "--no-renames", "--name-only", "-z", parent, "--");
            VerifyInputs(parent);
            BackupCommand.CommitStagedIndex(repository, CommitMessage,
                expected, reviewed.Branch, parent, modes);

            var commit = ApplyCommand.ResolveHeadCommit(repository);
            var actualParent = GitProcess.RunRequired(repository, "Could not inspect capture parent",
                "rev-parse", $"{commit}^").Output.Trim();
            if (actualParent != parent) throw new InvalidOperationException("Capture commit parent changed.");
            VerifyPaths("diff-tree", "--no-commit-id", "--no-renames", "--name-only", "-r", "-z", parent, commit, "--");
            foreach (var file in expected)
            {
                var entry = GitProcess.RunRequired(repository, "Could not inspect committed capture",
                    "--no-replace-objects", "ls-tree", "-z", commit, "--", file.RelativePath).Output;
                if (entry != $"{modes[file.RelativePath]} blob {file.Blob}\t{file.RelativePath}\0")
                {
                    throw new InvalidOperationException("Committed capture bytes or file mode differ from review.");
                }
            }
            VerifyInputs(commit);
            EnsureEmptyIndex(repository);
            return commit;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException or InvalidDataException)
        {
            // A signing/index/post-commit check can fail after earlier progress.
            // Never reset someone else's state to disguise that partial outcome.
            throw new InvalidOperationException("Legacy capture write/commit stopped. Working files, staged changes or a new commit may remain. Inspect HEAD, index and working files before retrying; no rollback or push was attempted.", exception);
        }

        void VerifyInputs(string expectedHead)
        {
            AecApplication.EnsureNoLinksInExistingPath(repository, "Repository path");
            AecApplication.EnsureNoLinksInExistingPath(Path.Combine(repository, ".git"), "Git metadata path");
            MigrationFastForward.EnsureSafeGitConfiguration(repository);
            if (ApplyCommand.ResolveHeadCommit(repository) != expectedHead ||
                MigrationLegacyCapturePreview.ReadBranch(repository) != reviewed.Branch ||
                AecApplication.ReadOptionalTextFile(Path.Combine(repository, MigrationPreflight.FormatRelativePath),
                    "Repository format marker") is not null ||
                !MigrationHarnessSelection.Read(fresh.Preparation.Preflight with { Commit = expectedHead })
                    .SequenceEqual(fresh.Preparation.Harnesses))
            {
                throw new InvalidOperationException("Capture HEAD, branch, format or local enrollment changed.");
            }
            foreach (var file in fresh.Files)
            {
                var canonical = Path.Combine(repository, file.RelativePath);
                AecApplication.EnsureNoLinksInExistingPath(Path.GetDirectoryName(canonical)!, "Canonical capture path");
                AecApplication.EnsureNoLinksInExistingPath(Path.GetDirectoryName(file.RuntimePath)!, "Runtime capture path");
                if (!AecApplication.ReadRequiredTextFile(canonical, "Canonical capture file")
                        .AsSpan().SequenceEqual(file.ProposedCanonical) ||
                    !AecApplication.ReadRequiredTextFile(file.RuntimePath, "Runtime capture file")
                        .AsSpan().SequenceEqual(file.Runtime))
                {
                    throw new InvalidOperationException("Canonical or runtime bytes changed after capture review.");
                }
            }
        }

        void VerifyPaths(params string[] arguments)
        {
            var actual = GitProcess.RunRequired(repository, "Could not inspect exact capture paths", arguments)
                .Output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
            if (!actual.Order(StringComparer.Ordinal).SequenceEqual(paths.Order(StringComparer.Ordinal)))
            {
                throw new InvalidOperationException("Capture changes are not the exact reviewed canonical path set.");
            }
        }
    }

    private static void EnsureEmptyIndex(string repository)
    {
        var result = GitProcess.Run(repository, "diff", "--cached", "--quiet", "--exit-code", "--no-ext-diff", "--no-textconv");
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException("Legacy capture requires an empty staged diff; inspect the existing index before retrying. No automatic unstaging was performed.");
        }
    }
}
