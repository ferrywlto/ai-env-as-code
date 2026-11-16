namespace Aec;

internal enum MigrationSectionChoice { Unchanged, Preserve, Overwrite, Cancelled }

// This is an advisory read/prompt boundary, never permission to reuse a stale
// snapshot for writes. Later preparation must revalidate HEAD and dirty paths.
internal sealed record MigrationDirtySections(IReadOnlyList<string> Paths)
{
    public static MigrationDirtySections Read(
        MigrationPreflight preflight, IReadOnlyList<MigrationHarness> harnesses)
        => ReadCore(preflight, harnesses, null);

    // Only the target writer admits its one intentional target change. Ordinary
    // source preparation must continue rejecting every dirty target.
    internal static MigrationDirtySections ReadForTarget(MigrationPreflight preflight,
        IReadOnlyList<MigrationHarness> harnesses, string targetRelativePath)
    {
        var (platform, provider) = MigrationPortableTargetWriter.ReadEnrollment(targetRelativePath);
        if (platform != RenderCommand.CurrentPlatform())
            throw new InvalidOperationException("Migration target must use the current platform.");
        if (!harnesses.Any(harness => harness.Provider == provider))
            throw new InvalidOperationException("Migration target must belong to a confirmed local harness.");
        return ReadCore(preflight, harnesses, targetRelativePath);
    }

    // Completion admits only its fixed marker, not arbitrary extra dirty paths.
    internal static MigrationDirtySections ReadForCompletion(MigrationPreflight preflight,
        IReadOnlyList<MigrationHarness> harnesses) => ReadCore(preflight, harnesses, null, completion: true);

    private static MigrationDirtySections ReadCore(MigrationPreflight preflight,
        IReadOnlyList<MigrationHarness> harnesses, string? targetRelativePath, bool completion = false)
    {
        if (harnesses.Count == 0)
        {
            throw new InvalidOperationException("Migration requires a locally enrolled harness.");
        }

        var allowed = new HashSet<string>(StringComparer.Ordinal)
        {
            "environment/shared/instructions.md",
            $"environment/platforms/{RenderCommand.CurrentPlatform()}/policy.md"
        };
        foreach (var harness in harnesses)
        {
            if (harness.Provider is not (AecIdentifiers.Codex or AecIdentifiers.Copilot))
            {
                throw new ArgumentException($"Unsupported migration provider: {harness.Provider}");
            }
            allowed.Add($"environment/providers/{harness.Provider}/overlay.md");
        }
        if (targetRelativePath is not null) allowed.Add(targetRelativePath);
        if (completion) allowed.Add(MigrationPreflight.FormatRelativePath);

        foreach (var path in allowed)
        {
            var fullPath = Path.Combine(preflight.Repository, path);
            AecApplication.EnsureNoLinksInExistingPath(Path.GetDirectoryName(fullPath)!, "Migration section path");
            _ = AecApplication.ReadOptionalTextFile(fullPath, "Migration section");
        }

        // These are fixed AEC paths, not user-supplied patterns. ls-tree does not
        // support all pathspec magic accepted by status/ls-files.
        var pathspecs = allowed.ToArray();
        // Working files alone cannot reveal a staged/deleted symlink or gitlink.
        // Validate both committed and indexed kinds without requiring clean bytes.
        EnsureRegularEntries(GitProcess.RunRequired(preflight.Repository, "Git could not inspect section history",
            ["--no-replace-objects", "ls-tree", "-z", preflight.Commit, "--", .. pathspecs]).Output);
        EnsureRegularEntries(GitProcess.RunRequired(preflight.Repository, "Git could not inspect section index",
            ["ls-files", "--stage", "-z", "--", .. pathspecs]).Output);
        var ignored = GitProcess.RunRequired(preflight.Repository, "Git could not inspect ignored sections",
            ["ls-files", "--others", "--ignored", "--exclude-standard", "-z", "--", .. pathspecs]);
        if (ignored.Output.Length != 0)
        {
            throw new InvalidOperationException("Migration section files are ignored by Git; review ignore rules first.");
        }

        var status = GitProcess.RunRequired(preflight.Repository, "Git could not inspect migration changes",
            "--no-optional-locks", "status", "--porcelain=v1", "-z", "--untracked-files=all", "--renames",
            "--ignore-submodules=none", "--", ".").Output;
        var dirty = new HashSet<string>(StringComparer.Ordinal);
        foreach (var record in Records(status))
        {
            // Reject R/C before their second NUL-delimited pathname can be mistaken
            // for a normal record. Only ordinary add/modify/delete/untracked qualify.
            var untracked = record.StartsWith("?? ", StringComparison.Ordinal);
            if (record.Length < 4 || record[2] != ' ' ||
                (!untracked && (record[..2] is "  " or "AA" or "DD" ||
                    record[..2].Any(code => code is not (' ' or 'A' or 'M' or 'D')))))
            {
                throw new InvalidOperationException("Unsafe Git status: renames, copies, conflicts and type changes stop migration.");
            }
            var path = record[3..];
            if (!allowed.Contains(path))
            {
                throw new InvalidOperationException($"Dirty path is outside the selected migration sections: {path}");
            }
            dirty.Add(path);
        }

        if (!string.Equals(preflight.Commit, ApplyCommand.ResolveHeadCommit(preflight.Repository), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Repository HEAD changed during migration section preflight.");
        }
        return new MigrationDirtySections(dirty.Order(StringComparer.Ordinal).ToArray());
    }

    public MigrationSectionChoice Prompt(TextReader input, TextWriter output, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested) return MigrationSectionChoice.Cancelled;
        if (Paths.Count == 0) return MigrationSectionChoice.Unchanged;

        output.WriteLine("Dirty migration section files:");
        foreach (var path in Paths) output.WriteLine($"- {path}");
        output.WriteLine("""
            Preserve keeps these edits. Overwrite proposes resetting dirty section files to empty Markdown for you to refill.
            Clean files are preserved.
            Discarded uncommitted section edits may not be recoverable from Git history.
            This prompt records your choice only; it does not change any files.
            """);
        while (!cancellationToken.IsCancellationRequested)
        {
            output.WriteLine("Type Preserve or Overwrite (no default):");
            var answer = input.ReadLine();
            if (answer is null || cancellationToken.IsCancellationRequested) return MigrationSectionChoice.Cancelled;
            if (answer == nameof(MigrationSectionChoice.Preserve)) return MigrationSectionChoice.Preserve;
            if (answer == nameof(MigrationSectionChoice.Overwrite)) return MigrationSectionChoice.Overwrite;
            output.WriteLine("No choice recorded. Enter exactly Preserve or Overwrite.");
        }
        return MigrationSectionChoice.Cancelled;
    }

    private static string[] Records(string value)
    {
        if (value.Length == 0) return [];
        if (!value.EndsWith('\0')) throw new InvalidDataException("Git returned truncated migration metadata.");
        return value[..^1].Split('\0');
    }

    private static void EnsureRegularEntries(string metadata)
    {
        foreach (var record in Records(metadata))
        {
            if ((!record.StartsWith("100644 ", StringComparison.Ordinal) &&
                 !record.StartsWith("100755 ", StringComparison.Ordinal)) || !record.Contains('\t'))
            {
                throw new InvalidOperationException("Migration sections must be regular Git files, not links or submodules.");
            }
        }
    }
}
