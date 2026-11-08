using System.Globalization;

namespace Aec;

internal sealed record MigrationLegacyCaptureFile(string Provider, string RelativePath,
    string RuntimePath, byte[] Canonical, byte[] Runtime, byte[] ProposedCanonical)
{
    public bool HasChanges => !Canonical.AsSpan().SequenceEqual(ProposedCanonical);
}

// Read-only, legacy-only proposal. No caller may reuse these bytes for a write
// without freshly validating HEAD, canonical bytes, bindings and runtime inputs.
internal sealed record MigrationLegacyCapturePreview(MigrationPreparation Preparation, string Branch,
    IReadOnlyList<MigrationLegacyCaptureFile> Files)
{
    public bool HasChanges => Files.Any(file => file.HasChanges);

    public static MigrationLegacyCapturePreview Read(MigrationPreparation preparation)
    {
        var expected = preparation.Preflight;
        var repository = expected.Repository;
        MigrationFastForward.EnsureSafeGitConfiguration(repository);
        if (MigrationPreflight.Read(repository, expected.TargetFormat.ToString(2)) != expected ||
            preparation.Harnesses.Count == 0 ||
            !MigrationHarnessSelection.Read(expected).SequenceEqual(preparation.Harnesses))
        {
            throw new InvalidOperationException("Migration capture context changed; restart preparation.");
        }
        var branch = ReadBranch(repository);
        _ = MigrationDirtySections.Read(expected, preparation.Harnesses);
        EnsureOriginalCommit(repository, preparation.OriginalCommit, expected.Commit);

        var files = new List<MigrationLegacyCaptureFile>();
        foreach (var harness in preparation.Harnesses)
        {
            var relative = harness.Provider == AecIdentifiers.Codex
                ? AecApplication.SourceRelativePath : AecApplication.CopilotSourceRelativePath;
            var before = ReadHistoricalFile(repository, preparation.OriginalCommit, relative);
            var canonical = ReadCurrentFile(expected, relative);
            AecApplication.EnsureNoLinksInExistingPath(harness.Home, "Runtime instruction home path");
            var runtime = AecApplication.ReadRequiredTextFile(harness.RuntimePath, "Runtime instructions");
            ValidateLegacyInstructions(harness.Provider, before, "Original canonical instructions");
            ValidateLegacyInstructions(harness.Provider, canonical, "Current canonical instructions");
            ValidateLegacyInstructions(harness.Provider, runtime, "Runtime instructions", repository);
            var matches = canonical.AsSpan().SequenceEqual(runtime);
            EnsureNoRetrievedConflict(!before.AsSpan().SequenceEqual(canonical), matches, relative);
            files.Add(new MigrationLegacyCaptureFile(harness.Provider, relative, harness.RuntimePath,
                canonical, runtime, matches ? canonical : runtime));

            if (harness.Provider == AecIdentifiers.Codex) files.Add(ReadPersonality(preparation, harness));
        }

        // A later invalid input must never expose a partial set of proposals.
        // Recheck local identity before returning; this is still not write authority.
        if (MigrationPreflight.Read(repository, expected.TargetFormat.ToString(2)) != expected ||
            !MigrationHarnessSelection.Read(expected).SequenceEqual(preparation.Harnesses) ||
            ReadBranch(repository) != branch)
        {
            throw new InvalidOperationException("Migration context changed during capture preview.");
        }
        return new MigrationLegacyCapturePreview(preparation, branch, files);
    }

    internal static string ReadBranch(string repository) => GitProcess.RunRequired(repository,
        "Git could not inspect migration branch", "symbolic-ref", "--quiet", "HEAD").Output.TrimEnd('\r', '\n');

    private static MigrationLegacyCaptureFile ReadPersonality(MigrationPreparation preparation, MigrationHarness harness)
    {
        var current = preparation.Preflight;
        var relative = AecApplication.ConfigSourceRelativePath;
        var canonicalPath = Path.Combine(current.Repository, relative);
        var before = ReadHistoricalFile(current.Repository, preparation.OriginalCommit, relative);
        var canonical = ReadCurrentFile(current, relative);
        var originalValue = CodexPersonalityConfig.ReadCanonical(before, canonicalPath);
        var currentValue = CodexPersonalityConfig.ReadCanonical(canonical, canonicalPath);
        AecApplication.EnsureNoLinksInExistingPath(harness.Home, "Runtime config home path");
        ApplyCommand.EnsureRuntimeOutsideRepository(current.Repository, harness.Home,
            "config.toml", "Codex runtime config");
        var runtimePath = Path.Combine(harness.Home, "config.toml");
        var runtime = AecApplication.ReadOptionalTextFile(runtimePath, "Runtime config");
        if (runtime is null || CodexPersonalityConfig.ReadRuntime(runtime, runtimePath) is not { } runtimeValue)
        {
            throw new MissingRuntimePersonalityException(
                "runtime config does not declare root `personality`; migration capture stopped without changing the repository.");
        }
        EnsureNoRetrievedConflict(originalValue != currentValue, runtimeValue == currentValue, relative);
        // Only the managed value moves. Keep pulled canonical formatting and
        // never propose persisting unrelated, possibly machine-specific settings.
        var update = CodexPersonalityConfig.PlanCanonicalUpdate(canonical, canonicalPath, runtimeValue);
        return new MigrationLegacyCaptureFile(harness.Provider, relative, runtimePath,
            canonical, runtime, update.Content);
    }

    private static void EnsureNoRetrievedConflict(bool canonicalChanged, bool runtimeMatches, string relative)
    {
        if (canonicalChanged && !runtimeMatches)
        {
            throw new InvalidOperationException($"Retrieved canonical content differs from runtime: {relative}. Reconcile it before migration capture; no merge or overwrite was performed.");
        }
    }

    private static void ValidateLegacyInstructions(string provider, byte[] content, string label, string? localRepository = null)
    {
        var codex = provider == AecIdentifiers.Codex ? AecInstructionBlock.ReadRepositoryBinding(content) : null;
        var copilot = provider == AecIdentifiers.Copilot ? CopilotInstructionBlock.ReadManagedBinding(content) : null;
        var repository = codex?.Repository ?? copilot?.Repository
            ?? throw new InvalidDataException($"{label} contain no supported AEC binding.");
        if ((codex?.Platform ?? copilot?.Platform) is not null)
        {
            throw new InvalidOperationException($"{label} use portable sections; legacy capture cannot copy generated instructions. A format-compatible capture handoff is required.");
        }
        // Canonical 1.x blocks may retain another machine's checkout binding.
        // Local runtime must match this checkout; never rebind it in a preview.
        if (localRepository is not null && !AecInstructionBlock.RepositoryPathsEqual(repository, localRepository))
        {
            throw new InvalidOperationException($"{label} are bound to another data repository.");
        }
    }

    internal static void EnsureOriginalCommit(string repository, string original, string current)
    {
        if (original.Length is not (40 or 64) || !original.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f'))
        {
            throw new InvalidDataException("Original migration commit is not a full Git object ID.");
        }
        var type = GitProcess.RunRequired(repository, "Git could not inspect original migration commit",
            "--no-replace-objects", "cat-file", "-t", original).Output.Trim();
        if (type != "commit") throw new InvalidDataException("Original migration object is not a commit.");
        var ancestry = GitProcess.Run(repository, "--no-replace-objects", "merge-base", "--is-ancestor", original, current);
        if (ancestry.ExitCode != 0)
        {
            throw new InvalidOperationException(ancestry.ExitCode == 1
                ? "Original migration commit is not an ancestor of the prepared checkout."
                : "Git could not verify original migration ancestry.");
        }
    }

    private static byte[] ReadCurrentFile(MigrationPreflight current, string relative)
    {
        AecApplication.EnsureNoLinksInExistingPath(
            Path.GetDirectoryName(Path.Combine(current.Repository, relative))!, "Canonical migration source path");
        var bytes = ApplyCommand.ReadCommittedFile(current.Repository, current.Commit, relative, "Canonical migration source");
        if (!bytes.AsSpan().SequenceEqual(ReadHistoricalFile(current.Repository, current.Commit, relative)))
        {
            throw new InvalidOperationException("Canonical bytes differ from the actual Git object; replacement objects are not capture provenance.");
        }
        return bytes;
    }

    internal static byte[] ReadHistoricalFile(string repository, string commit, string relative)
    {
        // Inspect tree kind before reading bytes: git show alone also accepts
        // links, oversized blobs and replacement objects as if they were files.
        var tree = GitProcess.RunRequired(repository, "Git could not inspect historical migration source",
            "--no-replace-objects", "ls-tree", "-z", commit, "--", relative).Output;
        var tab = tree.IndexOf('\t');
        if (tab < 0 || !tree.EndsWith('\0') || tree[..^1].Contains('\0') || tree[(tab + 1)..^1] != relative)
        {
            throw new InvalidDataException($"Historical migration source is not one exact file: {relative}.");
        }
        var fields = tree[..tab].Split(' ');
        if (fields.Length != 3 || fields[0] is not ("100644" or "100755") || fields[1] != "blob")
        {
            throw new InvalidDataException($"Historical migration source must be a regular Git file: {relative}.");
        }
        var size = GitProcess.RunRequired(repository, "Git could not inspect historical source size",
            "--no-replace-objects", "cat-file", "-s", fields[2]).Output.Trim();
        if (!long.TryParse(size, NumberStyles.None, CultureInfo.InvariantCulture, out var length) || length > AecApplication.MaximumTextBytes)
        {
            throw new InvalidDataException($"Historical migration source exceeds the supported text size: {relative}.");
        }
        return GitProcess.RunRequiredBytes(repository, "Git could not read historical migration source",
            "--no-replace-objects", "cat-file", "blob", fields[2]);
    }
}
