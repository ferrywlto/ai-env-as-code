using System.Text;

namespace Aec;

// Prepare before authored writes, then replace only affected existing targets.
// Keeping the plan private prevents callers from substituting desired bytes.
internal sealed class MigrationPortableTargetWriter
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly MigrationPortableCapturePlan plan;
    private readonly TargetChange[] changes;

    private MigrationPortableTargetWriter(MigrationPortableCapturePlan plan, TargetChange[] changes)
    {
        this.plan = plan;
        this.changes = changes;
    }

    public static MigrationPortableTargetWriter Prepare(MigrationPortableCapturePreview reviewed) =>
        Prepare(MigrationPortableCapturePlan.Prepare(reviewed));

    internal static MigrationPortableTargetWriter Prepare(MigrationPortableCapturePlan plan)
    {
        var proposals = plan.ChangedSourcePaths;
        var changes = new List<TargetChange>();
        var enrolled = GitProcess.RunRequired(plan.Repository, "Could not list committed migration targets",
            "--no-replace-objects", "ls-tree", "-r", "--name-only", "-z", plan.Commit, "--", "environment/targets")
            .Output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        foreach (var relative in enrolled.Order(StringComparer.Ordinal))
        {
            var (platform, provider) = ReadEnrollment(relative);

            // Freeze even unaffected targets, but render only the union of
            // consumers of changed sources. Config alone does not affect targets.
            var before = plan.ObserveCommitted(relative);
            string[] sources = ["environment/shared/instructions.md",
                $"environment/platforms/{platform}/policy.md",
                $"environment/providers/{provider}/overlay.md"];
            if (!sources.Any(source => proposals.Contains(source, StringComparer.Ordinal))) continue;
            var shared = plan.ReadSource(sources[0]);
            var policy = plan.ReadSource(sources[1]);
            var overlay = plan.ReadSource(sources[2]);
            var baseline = StrictUtf8.GetBytes(InstructionSections.Render(shared.Original, policy.Original, overlay.Original));
            var desired = StrictUtf8.GetBytes(InstructionSections.Render(shared.Desired, policy.Desired, overlay.Desired));
            if (!before.AsSpan().SequenceEqual(baseline))
            {
                throw new InvalidDataException($"Committed target does not match its authored sources: {relative}");
            }
            if (desired.Length > AecApplication.MaximumTextBytes)
            {
                throw new InvalidDataException($"Rendered target exceeds 1 MiB: {relative}");
            }
            if (!before.AsSpan().SequenceEqual(desired))
            {
                changes.Add(new TargetChange(relative, before, desired));
            }
        }
        plan.Verify([]);
        return new MigrationPortableTargetWriter(plan, changes.ToArray());
    }

    private static (string Platform, string Provider) ReadEnrollment(string relativePath)
    {
        var parts = relativePath.Split('/');
        if (parts.Length != 5)
        {
            throw new InvalidDataException($"Unsupported committed target path structure: {relativePath}");
        }
        if (parts[0] != "environment" || parts[1] != "targets")
        {
            throw new InvalidDataException($"Unsupported committed target directory: {relativePath}");
        }

        var platform = parts[2];
        if (platform is not ("macos-arm64" or "windows-x64" or "linux-arm64"))
        {
            throw new InvalidDataException($"Unsupported committed target platform: {relativePath}");
        }
        var provider = parts[3];
        if (provider is not ("codex" or "copilot"))
        {
            throw new InvalidDataException($"Unsupported committed target provider: {relativePath}");
        }

        var expectedFileName = provider == "codex" ? "AGENTS.md" : "copilot-instructions.md";
        if (parts[4] != expectedFileName)
        {
            throw new InvalidDataException($"Unsupported committed target file name: {relativePath}");
        }
        return (platform, provider);
    }

    internal Dictionary<string, byte[]> CopyProposals() => changes.ToDictionary(
        change => change.RelativePath, change => change.Desired.ToArray(), StringComparer.Ordinal);

    internal void VerifyRendered() => plan.Verify(plan.ChangedSourcePaths, CopyProposals());

    internal void VerifyForCommit(string expectedHead) =>
        plan.VerifyForCommit(plan.ChangedSourcePaths, CopyProposals(), expectedHead);

    public IReadOnlyList<string> Write(IReadOnlyList<string> writtenSourcePaths)
    {
        ArgumentNullException.ThrowIfNull(writtenSourcePaths);
        var sources = writtenSourcePaths.Order(StringComparer.Ordinal).ToArray();
        if (!sources.SequenceEqual(plan.ChangedSourcePaths, StringComparer.Ordinal))
            throw new InvalidOperationException("Target rendering requires exactly the completed authored/config proposal paths.");

        var written = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        string? attempted = null;
        try
        {
            plan.Verify(sources, written);
            foreach (var change in changes)
            {
                plan.Verify(sources, written);
                var path = Path.Combine(plan.Repository, change.RelativePath);
                AecApplication.EnsureNoLinksInExistingPath(Path.GetDirectoryName(path)!, "Rendered target path");
                attempted = change.RelativePath;
                AtomicFile.ReplaceIfUnchanged(path, change.Before, change.Desired, "Rendered migration target");
                written.Add(change.RelativePath, change.Desired);
                attempted = null;
                plan.Verify(sources, written);
            }
            return written.Keys.ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException or ArgumentException)
        {
            throw new MigrationPortableTargetWriteException(written.Keys.ToArray(), attempted, exception);
        }
    }

    private sealed record TargetChange(string RelativePath, byte[] Before, byte[] Desired);
}

internal sealed class MigrationPortableTargetWriteException : IOException
{
    public MigrationPortableTargetWriteException(IReadOnlyList<string> writtenPaths, string? attemptedPath, Exception inner)
        : base($"Portable target rendering stopped. Completed replacements: {(writtenPaths.Count == 0 ? "none" : string.Join(", ", writtenPaths))}. " +
            (attemptedPath is null ? "" : $"Attempted path may also have changed: {attemptedPath}. ") +
            "Inspect working files before retrying. No rollback, authored-file write, staging, commit or runtime apply was attempted.", inner)
    {
        WrittenPaths = writtenPaths;
        AttemptedPath = attemptedPath;
    }

    public IReadOnlyList<string> WrittenPaths { get; }
    public string? AttemptedPath { get; }
}
