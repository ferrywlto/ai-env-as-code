namespace Aec;

internal enum MigrationScaffoldAction
{
    CreateEmpty,
    PreserveExisting,
    PreserveDirty,
    PreserveDeletion,
    NeedsDirtyChoice,
    ResetEmpty
}

internal sealed record MigrationScaffoldFile(string RelativePath, byte[]? CommittedContent,
    string? CommittedMode, string IndexEntry, byte[]? WorkingContent,
    bool HasStagedChanges, MigrationScaffoldAction Action)
{
    // Zero bytes describe a missing-file seed or an explicitly chosen reset.
    // Null means no content proposal, never deletion or permission to unstage.
    public byte[]? ProposedContent
    {
        get
        {
            if (HasStagedChanges) return null;
            return Action is MigrationScaffoldAction.CreateEmpty or MigrationScaffoldAction.ResetEmpty ? [] : null;
        }
    }

    public bool BlockedForFutureWrite
    {
        get
        {
            if (HasStagedChanges) return true;
            // Reset proposals remain blocked: the current writer creates only
            // missing files. Executing resets is a separately reviewed boundary.
            return Action is MigrationScaffoldAction.PreserveDeletion or
                MigrationScaffoldAction.NeedsDirtyChoice or MigrationScaffoldAction.ResetEmpty;
        }
    }
}

// Inventory only. A later writer must obtain fresh validation and review;
// neither this record nor its caller-visible byte arrays authorize writes.
internal sealed record MigrationScaffoldPreview(MigrationPreparation Preparation,
    string Branch, IReadOnlyList<MigrationScaffoldFile> Files)
{
    public static MigrationScaffoldPreview Read(MigrationPreparation preparation)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        VerifySectionChoice(preparation.SectionChoice);
        var harnesses = preparation.Harnesses.ToArray();
        var dirtyPaths = preparation.DirtySections.Paths.ToArray();
        var expected = preparation with
        {
            Harnesses = harnesses,
            DirtySections = new MigrationDirtySections(dirtyPaths)
        };
        VerifyContext(expected);
        var repository = expected.Preflight.Repository;
        MigrationLegacyCapturePreview.EnsureOriginalCommit(repository,
            expected.OriginalCommit, expected.Preflight.Commit);
        var branch = MigrationLegacyCapturePreview.ReadBranch(repository);
        var indexPath = Path.Combine(repository, ".git", "index");
        var originalIndex = MigrationLegacyCaptureWriter.ReadIndex(indexPath);
        var runtimes = ReadRuntimes(harnesses);
        var files = new List<MigrationScaffoldFile>();
        foreach (var relativePath in RequiredPaths(harnesses))
        {
            files.Add(ReadSection(expected, relativePath));
        }

        // Pin exact observed bytes as well as Git's dirty path list. This also
        // catches edits hidden by assume-unchanged or made during the inventory.
        VerifyContext(expected);
        VerifyIndex(indexPath, originalIndex);
        VerifyRuntimes(runtimes);
        VerifySections(repository, files);
        VerifyHeadAndBranch(expected.Preflight, branch);
        return new MigrationScaffoldPreview(expected, branch, files.ToArray());
    }

    private static void VerifySectionChoice(MigrationSectionChoice choice)
    {
        if (choice == MigrationSectionChoice.Cancelled)
        {
            throw new InvalidOperationException("Migration section review was cancelled; no scaffold preview was prepared.");
        }
        if (!Enum.IsDefined(choice))
        {
            throw new ArgumentOutOfRangeException(nameof(choice));
        }
    }

    private static void VerifyContext(MigrationPreparation expected)
    {
        var preflight = expected.Preflight;
        MigrationFastForward.EnsureSafeGitConfiguration(preflight.Repository);
        var current = MigrationPreflight.Read(preflight.Repository, preflight.TargetFormat.ToString(2));
        if (current != preflight)
        {
            throw new InvalidOperationException("Migration scaffold context changed; refresh the post-capture preflight.");
        }
        if (expected.Harnesses.Count == 0)
        {
            throw new InvalidOperationException("Migration scaffolding requires confirmed local harnesses.");
        }
        var currentHarnesses = MigrationHarnessSelection.Read(preflight);
        if (!currentHarnesses.SequenceEqual(expected.Harnesses))
        {
            throw new InvalidOperationException("Migration scaffold harness enrollment changed.");
        }
        var currentDirty = MigrationDirtySections.Read(preflight, currentHarnesses);
        if (!currentDirty.Paths.SequenceEqual(expected.DirtySections.Paths, StringComparer.Ordinal))
        {
            throw new InvalidOperationException("Migration scaffold dirty paths changed; review the current section state.");
        }
    }

    private static string[] RequiredPaths(MigrationHarness[] harnesses)
    {
        var paths = new List<string>
        {
            "environment/shared/instructions.md",
            $"environment/platforms/{RenderCommand.CurrentPlatform()}/policy.md"
        };
        foreach (var harness in harnesses)
        {
            paths.Add($"environment/providers/{harness.Provider}/overlay.md");
        }
        return paths.Order(StringComparer.Ordinal).ToArray();
    }

    private static MigrationScaffoldFile ReadSection(MigrationPreparation expected, string relativePath)
    {
        var repository = expected.Preflight.Repository;
        var committed = ReadCommittedSection(expected.Preflight, relativePath);
        var indexEntry = ReadIndexEntry(repository, relativePath);
        var workingContent = ReadFile(Path.Combine(repository, relativePath));
        var hasStagedChanges = indexEntry != committed.ExpectedIndexEntry;
        var workingDiffers = !SameBytes(committed.Content, workingContent);
        var isDirty = hasStagedChanges || workingDiffers || expected.DirtySections.Paths.Contains(relativePath);
        var action = ChooseAction(isDirty, workingContent, expected.SectionChoice);
        if (action == MigrationScaffoldAction.CreateEmpty)
        {
            VerifyMissingPathNotIgnored(repository, relativePath);
        }
        return new MigrationScaffoldFile(relativePath, committed.Content, committed.Mode,
            indexEntry, workingContent, hasStagedChanges, action);
    }

    private static (byte[]? Content, string? Mode, string ExpectedIndexEntry) ReadCommittedSection(
        MigrationPreflight preflight, string relativePath)
    {
        var result = GitProcess.RunRequired(preflight.Repository, "Could not inspect scaffold source history",
            "--no-replace-objects", "ls-tree", "-z", preflight.Commit, "--", relativePath);
        var treeEntry = result.Output;
        if (treeEntry.Length == 0) return (null, null, "");

        // Reuse the bounded regular-blob validator before reading its metadata.
        // The pinned commit is read without replacement objects in both calls.
        var content = MigrationLegacyCapturePreview.ReadHistoricalFile(preflight.Repository, preflight.Commit, relativePath);
        var metadata = treeEntry[..treeEntry.IndexOf('\t')].Split(' ');
        var mode = metadata[0];
        var blob = metadata[2];
        return (content, mode, $"{mode} {blob} 0\t{relativePath}\0");
    }

    internal static MigrationScaffoldAction ChooseAction(
        bool isDirty, byte[]? workingContent, MigrationSectionChoice choice)
    {
        if (!isDirty)
        {
            if (workingContent is null) return MigrationScaffoldAction.CreateEmpty;
            return MigrationScaffoldAction.PreserveExisting;
        }
        if (choice == MigrationSectionChoice.Preserve)
        {
            if (workingContent is null) return MigrationScaffoldAction.PreserveDeletion;
            return MigrationScaffoldAction.PreserveDirty;
        }
        if (choice == MigrationSectionChoice.Overwrite)
        {
            // This describes pre-start retry handling only. It must not be
            // reapplied to the user's expected section edits before DONE.
            return MigrationScaffoldAction.ResetEmpty;
        }
        return MigrationScaffoldAction.NeedsDirtyChoice;
    }

    internal static void VerifyMissingPathNotIgnored(string repository, string relativePath)
    {
        var ignored = GitProcess.Run(repository, "check-ignore", "--no-index", "--quiet", "--", relativePath);
        if (ignored.ExitCode == 0)
        {
            throw new InvalidOperationException($"Missing scaffold source is ignored by Git: {relativePath}.");
        }
        if (ignored.ExitCode != 1)
        {
            throw new InvalidOperationException($"Could not inspect scaffold ignore rules: {relativePath}.");
        }
    }

    private static Dictionary<string, byte[]?> ReadRuntimes(MigrationHarness[] harnesses)
    {
        var snapshots = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        foreach (var harness in harnesses)
        {
            snapshots.Add(harness.RuntimePath, ReadFile(harness.RuntimePath));
            if (harness.Provider == "codex")
            {
                var configPath = Path.Combine(harness.Home, "config.toml");
                snapshots.Add(configPath, ReadFile(configPath));
            }
        }
        return snapshots;
    }

    private static void VerifyIndex(string path, byte[] originalIndex)
    {
        var currentIndex = MigrationLegacyCaptureWriter.ReadIndex(path);
        if (!SameBytes(originalIndex, currentIndex))
        {
            throw new InvalidOperationException("Git index changed during scaffold preview.");
        }
    }

    private static void VerifyRuntimes(Dictionary<string, byte[]?> snapshots)
    {
        foreach (var (path, expectedContent) in snapshots)
        {
            var currentContent = ReadFile(path);
            if (!SameBytes(expectedContent, currentContent))
            {
                throw new InvalidOperationException($"Runtime input changed during scaffold preview: {path}.");
            }
        }
    }

    private static void VerifySections(string repository, List<MigrationScaffoldFile> files)
    {
        foreach (var file in files)
        {
            var workingContent = ReadFile(Path.Combine(repository, file.RelativePath));
            if (!SameBytes(file.WorkingContent, workingContent))
            {
                throw new InvalidOperationException($"Authored source changed during scaffold preview: {file.RelativePath}.");
            }
            var indexEntry = ReadIndexEntry(repository, file.RelativePath);
            if (file.IndexEntry != indexEntry)
            {
                throw new InvalidOperationException($"Indexed source changed during scaffold preview: {file.RelativePath}.");
            }
        }
    }

    private static void VerifyHeadAndBranch(MigrationPreflight expected, string branch)
    {
        if (ApplyCommand.ResolveHeadCommit(expected.Repository) != expected.Commit)
        {
            throw new InvalidOperationException("HEAD changed during scaffold preview.");
        }
        if (MigrationLegacyCapturePreview.ReadBranch(expected.Repository) != branch)
        {
            throw new InvalidOperationException("Branch changed during scaffold preview.");
        }
    }

    private static string ReadIndexEntry(string repository, string relativePath) =>
        GitProcess.RunRequired(repository, "Could not inspect scaffold index entry",
            "ls-files", "--stage", "-z", "--", relativePath).Output;

    private static byte[]? ReadFile(string path)
    {
        AecApplication.EnsureNoLinksInExistingPath(Path.GetDirectoryName(path)!, "Scaffold input path");
        return AecApplication.ReadOptionalTextFile(path, "Scaffold input file");
    }

    private static bool SameBytes(byte[]? first, byte[]? second)
    {
        if (first is null) return second is null;
        if (second is null) return false;
        return first.AsSpan().SequenceEqual(second);
    }
}
