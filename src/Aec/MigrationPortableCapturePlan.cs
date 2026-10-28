using System.Text;

namespace Aec;

// Private, in-memory bridge between the authored-file and target-only stages.
// Prepare while sources are still clean: normal backup previews cannot be
// recreated after those sources intentionally become dirty.
internal sealed class MigrationPortableCapturePlan
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly MigrationPortableCapturePreview fresh;
    private readonly byte[] index;
    private readonly Dictionary<string, byte[]?> originals = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> proposals = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]?> runtimes = new(StringComparer.Ordinal);

    private MigrationPortableCapturePlan(MigrationPortableCapturePreview fresh)
    {
        this.fresh = fresh with
        {
            Preparation = fresh.Preparation with { Harnesses = fresh.Preparation.Harnesses.ToArray() }
        };
        index = MigrationLegacyCaptureWriter.ReadIndex(Path.Combine(Repository, ".git", "index"));
        _ = ObserveCommitted(AecApplication.SourceRelativePath);
        _ = ObserveCommitted(AecApplication.ConfigSourceRelativePath);
        foreach (var harness in fresh.Preparation.Harnesses)
        {
            var snapshot = harness.Provider == "codex" ? fresh.Codex?.Snapshot : fresh.Copilot?.Snapshot;
            runtimes.Add(harness.RuntimePath, snapshot?.Runtime ?? ReadFile(harness.RuntimePath));
            if (harness.Provider == "codex")
            {
                var path = Path.Combine(harness.Home, "config.toml");
                runtimes.Add(path, fresh.Codex?.RuntimeConfig ?? ReadFile(path));
            }
            else
            {
                originals.TryAdd(AecApplication.CopilotSourceRelativePath,
                    ReadFile(Path.Combine(Repository, AecApplication.CopilotSourceRelativePath)));
            }
            if (snapshot is null) continue;
            _ = ObserveCommitted("environment/shared/instructions.md");
            _ = ObserveCommitted($"environment/platforms/{snapshot.Platform}/policy.md");
            _ = ObserveCommitted($"environment/providers/{snapshot.Provider}/overlay.md");
            _ = ObserveCommitted($"environment/targets/{snapshot.Platform}/{snapshot.Provider}/{Path.GetFileName(snapshot.RuntimePath)}");
        }

        AddInstructions(fresh.Codex?.Snapshot, fresh.Codex?.Instructions);
        AddInstructions(fresh.Copilot?.Snapshot, fresh.Copilot?.Instructions);
        if (fresh.Codex?.ConfigWouldChange == true)
            AddProposal(AecApplication.ConfigSourceRelativePath, fresh.Codex.ProposedCanonicalConfig);
        Verify([]);
    }

    internal string Repository => fresh.Preparation.Preflight.Repository;
    internal string Commit => fresh.Preparation.Preflight.Commit;
    internal string[] ChangedSourcePaths => proposals.Keys.Order(StringComparer.Ordinal).ToArray();

    internal static MigrationPortableCapturePlan Prepare(MigrationPortableCapturePreview reviewed) =>
        new(MigrationPortableCaptureWriter.RecheckPreview(reviewed));

    internal IReadOnlyList<string> WriteSources()
    {
        var written = new List<string>();
        string? attempted = null;
        try
        {
            Verify(written);
            foreach (var relative in ChangedSourcePaths)
            {
                Verify(written);
                var path = Path.Combine(Repository, relative);
                AecApplication.EnsureNoLinksInExistingPath(Path.GetDirectoryName(path)!, "Authored source path");
                attempted = relative;
                AtomicFile.ReplaceIfUnchanged(path, originals[relative], proposals[relative], "Portable authored source");
                written.Add(relative);
                attempted = null;
                Verify(written);
            }
            return written.ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException or ArgumentException)
        {
            throw new MigrationPortableSourceWriteException(written.ToArray(), attempted, exception);
        }
    }

    internal byte[] ObserveCommitted(string relative)
    {
        if (originals.TryGetValue(relative, out var observed) && observed is not null) return observed;
        var before = MigrationLegacyCapturePreview.ReadHistoricalFile(Repository, Commit, relative);
        if (!SameBytes(before, ReadFile(Path.Combine(Repository, relative))))
            throw new InvalidOperationException("Portable source baseline changed before writing; no files were replaced.");
        originals[relative] = before;
        return before;
    }

    internal (string Original, string Desired) ReadSource(string relative)
    {
        var before = ObserveCommitted(relative);
        var desired = proposals.TryGetValue(relative, out var proposal) ? proposal : before;
        return (StrictUtf8.GetString(before), StrictUtf8.GetString(desired));
    }

    internal void Verify(IReadOnlyList<string> writtenSources, IReadOnlyDictionary<string, byte[]>? writtenTargets = null)
    {
        AecApplication.EnsureNoLinksInExistingPath(Repository, "Repository path");
        AecApplication.EnsureNoLinksInExistingPath(Path.Combine(Repository, ".git"), "Git metadata path");
        MigrationFastForward.EnsureSafeGitConfiguration(Repository);
        MigrationPortableCaptureWriter.EnsureEmptyIndex(Repository);
        if (ApplyCommand.ResolveHeadCommit(Repository) != Commit ||
            MigrationLegacyCapturePreview.ReadBranch(Repository) != fresh.Branch ||
            !MigrationLegacyCaptureWriter.ReadIndex(Path.Combine(Repository, ".git", "index")).AsSpan().SequenceEqual(index) ||
            ReadFile(Path.Combine(Repository, MigrationPreflight.FormatRelativePath)) is not null ||
            !MigrationHarnessSelection.Read(fresh.Preparation.Preflight).SequenceEqual(fresh.Preparation.Harnesses) ||
            runtimes.Any(file => !SameBytes(file.Value, ReadFile(file.Key))))
        {
            throw new InvalidOperationException("Migration HEAD, branch, index, marker, enrollment or runtime changed; no further files will be replaced.");
        }
        foreach (var (relative, before) in originals)
        {
            var expected = writtenTargets is not null && writtenTargets.TryGetValue(relative, out var target)
                ? target : writtenSources.Contains(relative) ? proposals[relative] : before;
            if (!SameBytes(expected, ReadFile(Path.Combine(Repository, relative))))
                throw new InvalidOperationException("Portable canonical inputs changed; no further files will be replaced.");
        }
    }

    private void AddInstructions(PortableRuntimeSnapshot? snapshot, PortableBackupAttributionPreview? attribution)
    {
        if (snapshot is null || attribution?.Section is null) return;
        var relative = attribution.Section switch
        {
            "shared" => "environment/shared/instructions.md",
            "platform" => $"environment/platforms/{snapshot.Platform}/policy.md",
            "provider" => $"environment/providers/{snapshot.Provider}/overlay.md",
            _ => throw new InvalidOperationException("Unsupported portable capture section.")
        };
        if (attribution.SourceRelativePath != relative || attribution.ProposedContent is null)
            throw new InvalidOperationException("Portable capture proposal does not match its authored section.");
        AddProposal(relative, StrictUtf8.GetBytes(attribution.ProposedContent));
    }

    private void AddProposal(string relative, byte[] content)
    {
        if (content.Length > AecApplication.MaximumTextBytes)
            throw new InvalidDataException("Portable authored proposal exceeds 1 MiB.");
        _ = ObserveCommitted(relative);
        if (proposals.TryGetValue(relative, out var existing) && !SameBytes(existing, content))
            throw new InvalidOperationException($"Conflicting portable proposals for {relative}; no files were replaced.");
        if (!SameBytes(originals[relative], content)) proposals[relative] = content;
    }

    private static byte[]? ReadFile(string path)
    {
        AecApplication.EnsureNoLinksInExistingPath(Path.GetDirectoryName(path)!, "Capture input path");
        return AecApplication.ReadOptionalTextFile(path, "Capture input file");
    }

    private static bool SameBytes(byte[]? first, byte[]? second) =>
        first is null ? second is null : second is not null && first.AsSpan().SequenceEqual(second);
}
