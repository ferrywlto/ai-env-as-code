using System.Text;

namespace Aec;

// Combine portable proposals against one committed baseline. Only authored
// working files change: target rendering, staging and committing are later steps.
internal static class MigrationPortableSourceWriter
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static IReadOnlyList<string> Write(MigrationPortableCapturePreview reviewed)
    {
        var fresh = MigrationPortableCaptureWriter.RecheckPreview(reviewed);
        var context = fresh.Preparation.Preflight;
        var repository = context.Repository;
        var indexPath = Path.Combine(repository, ".git", "index");
        var index = MigrationLegacyCaptureWriter.ReadIndex(indexPath);
        var originals = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        var proposals = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var runtimes = new Dictionary<string, byte[]?>(StringComparer.Ordinal);

        // Freeze unchanged inputs too. Once a source is intentionally dirty,
        // ordinary committed-target validators cannot be rerun to check the batch.
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
                    ReadFile(Path.Combine(repository, AecApplication.CopilotSourceRelativePath)));
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

        var written = new List<string>();
        string? attempted = null;
        try
        {
            VerifyInputs();
            foreach (var (relative, content) in proposals.OrderBy(file => file.Key, StringComparer.Ordinal))
            {
                VerifyInputs();
                var path = Path.Combine(repository, relative);
                AecApplication.EnsureNoLinksInExistingPath(Path.GetDirectoryName(path)!, "Authored source path");
                attempted = relative;
                AtomicFile.ReplaceIfUnchanged(path, originals[relative], content, "Portable authored source");
                written.Add(relative);
                attempted = null;
                VerifyInputs();
            }
            return written.ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException or ArgumentException)
        {
            throw new MigrationPortableSourceWriteException(written.ToArray(), attempted, exception);
        }

        byte[] ObserveCommitted(string relative)
        {
            var before = MigrationLegacyCapturePreview.ReadHistoricalFile(repository, context.Commit, relative);
            var actual = ReadFile(Path.Combine(repository, relative));
            if (!SameBytes(before, actual) ||
                originals.TryGetValue(relative, out var existing) && !SameBytes(existing, before))
            {
                throw new InvalidOperationException("Portable source baseline changed before writing; no files were replaced.");
            }
            originals[relative] = before;
            return before;
        }

        void AddInstructions(PortableRuntimeSnapshot? snapshot, PortableBackupAttributionPreview? attribution)
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

        void AddProposal(string relative, byte[] content)
        {
            if (content.Length > AecApplication.MaximumTextBytes)
                throw new InvalidDataException("Portable authored proposal exceeds 1 MiB.");
            _ = ObserveCommitted(relative);
            if (proposals.TryGetValue(relative, out var existing) && !SameBytes(existing, content))
                throw new InvalidOperationException($"Conflicting portable proposals for {relative}; no files were replaced.");
            if (!SameBytes(originals[relative], content)) proposals[relative] = content;
        }

        void VerifyInputs()
        {
            AecApplication.EnsureNoLinksInExistingPath(repository, "Repository path");
            AecApplication.EnsureNoLinksInExistingPath(Path.Combine(repository, ".git"), "Git metadata path");
            MigrationPortableCaptureWriter.EnsureEmptyIndex(repository);
            if (ApplyCommand.ResolveHeadCommit(repository) != context.Commit ||
                MigrationLegacyCapturePreview.ReadBranch(repository) != fresh.Branch ||
                !MigrationLegacyCaptureWriter.ReadIndex(indexPath).AsSpan().SequenceEqual(index) ||
                ReadFile(Path.Combine(repository, MigrationPreflight.FormatRelativePath)) is not null ||
                !MigrationHarnessSelection.Read(context).SequenceEqual(fresh.Preparation.Harnesses) ||
                runtimes.Any(file => !SameBytes(file.Value, ReadFile(file.Key))))
            {
                throw new InvalidOperationException("Migration HEAD, branch, index, marker, enrollment or runtime changed; no further sources will be replaced.");
            }
            foreach (var (relative, before) in originals)
            {
                var expected = written.Contains(relative) ? proposals[relative] : before;
                if (!SameBytes(expected, ReadFile(Path.Combine(repository, relative))))
                    throw new InvalidOperationException("Portable canonical inputs changed; no further sources will be replaced.");
            }
        }
    }

    private static byte[]? ReadFile(string path)
    {
        AecApplication.EnsureNoLinksInExistingPath(Path.GetDirectoryName(path)!, "Capture input path");
        return AecApplication.ReadOptionalTextFile(path, "Capture input file");
    }

    private static bool SameBytes(byte[]? first, byte[]? second) =>
        first is null ? second is null : second is not null && first.AsSpan().SequenceEqual(second);
}

internal sealed class MigrationPortableSourceWriteException : IOException
{
    public MigrationPortableSourceWriteException(IReadOnlyList<string> writtenPaths, string? attemptedPath, Exception inner)
        : base($"Portable authored capture stopped. Completed replacements: {(writtenPaths.Count == 0 ? "none" : string.Join(", ", writtenPaths))}. " +
            (attemptedPath is null ? "" : $"Attempted path may also have changed: {attemptedPath}. ") +
            "Inspect working files before retrying. No rollback, rendering, staging, commit or runtime apply was attempted.", inner)
    {
        WrittenPaths = writtenPaths;
        AttemptedPath = attemptedPath;
    }

    public IReadOnlyList<string> WrittenPaths { get; }
    public string? AttemptedPath { get; }
}
