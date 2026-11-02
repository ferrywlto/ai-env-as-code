using System.Text;

namespace Aec;

internal sealed record MigrationValidatedSource(string RelativePath,
    byte[]? CommittedContent, string? CommittedMode, byte[] WorkingContent);

// Post-DONE observations only. User edits are expected; old scaffold actions
// must never erase them. A later writer must obtain fresh validation.
internal sealed record MigrationSourceValidationPreview(MigrationPreparation Preparation,
    string Branch, IReadOnlyList<MigrationValidatedSource> Files, IReadOnlyList<string> ChangedPaths)
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static MigrationSourceValidationPreview Read(MigrationPreparation preparation)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        if (preparation.SectionChoice == MigrationSectionChoice.Cancelled)
            throw new InvalidOperationException("Cancelled migration preparation cannot validate sources.");
        if (!Enum.IsDefined(preparation.SectionChoice))
            throw new ArgumentOutOfRangeException(nameof(preparation));

        var harnesses = preparation.Harnesses.ToArray();
        var expected = preparation with { Harnesses = harnesses };
        VerifyEntry(expected);
        var repository = expected.Preflight.Repository;
        var branch = MigrationLegacyCapturePreview.ReadBranch(repository);
        var index = ReadIndex(repository);
        var protectedFiles = ReadProtectedFiles(expected);

        // Refresh current owned dirt instead of comparing it with the pre-start
        // list. The user was explicitly asked to edit these files before DONE.
        var dirty = MigrationDirtySections.Read(expected.Preflight, harnesses);
        var files = new List<MigrationValidatedSource>();
        foreach (var relative in MigrationScaffoldPreview.RequiredPaths(harnesses))
        {
            files.Add(ReadSource(expected.Preflight, relative));
        }
        ValidateComposition(files, harnesses);
        VerifyFinalState(expected, branch, index, dirty, files, protectedFiles);

        var changed = new List<string>();
        foreach (var file in files)
        {
            // Null and empty differ: a newly authored zero-byte source still
            // needs a future commit. Direct bytes also reveal hidden Git edits.
            if (file.CommittedContent is null ||
                !file.CommittedContent.AsSpan().SequenceEqual(file.WorkingContent) ||
                dirty.Paths.Contains(file.RelativePath, StringComparer.Ordinal))
                changed.Add(file.RelativePath);
        }
        return new MigrationSourceValidationPreview(expected with { DirtySections = dirty },
            branch, files.ToArray(), changed.ToArray());
    }

    private static void VerifyEntry(MigrationPreparation expected)
    {
        var preflight = expected.Preflight;
        MigrationFastForward.EnsureSafeGitConfiguration(preflight.Repository);
        if (MigrationPreflight.Read(preflight.Repository, preflight.TargetFormat.ToString(2)) != preflight)
            throw new InvalidOperationException("Post-DONE migration context changed; refresh the post-capture preflight.");
        if (expected.Harnesses.Count == 0)
            throw new InvalidOperationException("Source validation requires confirmed local harnesses.");
        VerifyEnrollment(expected);
        MigrationLegacyCapturePreview.EnsureOriginalCommit(preflight.Repository,
            expected.OriginalCommit, preflight.Commit);
    }

    private static MigrationValidatedSource ReadSource(MigrationPreflight preflight, string relative)
    {
        var committed = MigrationScaffoldPreview.ReadCommittedSection(preflight, relative);
        var indexEntry = MigrationScaffoldPreview.ReadIndexEntry(preflight.Repository, relative);
        if (indexEntry != committed.ExpectedIndexEntry)
            throw new InvalidOperationException($"Staged source changes stop post-DONE validation: {relative}.");

        var content = ReadFile(Path.Combine(preflight.Repository, relative))
            ?? throw new InvalidOperationException($"Required instruction source is missing: {relative}.");
        return new MigrationValidatedSource(relative, committed.Content, committed.Mode, content);
    }

    private static void ValidateComposition(List<MigrationValidatedSource> files, MigrationHarness[] harnesses)
    {
        var text = files.ToDictionary(file => file.RelativePath,
            file => StrictUtf8.GetString(file.WorkingContent), StringComparer.Ordinal);
        var shared = text["environment/shared/instructions.md"];
        var policy = text[$"environment/platforms/{RenderCommand.CurrentPlatform()}/policy.md"];
        foreach (var harness in harnesses)
        {
            var overlay = text[$"environment/providers/{harness.Provider}/overlay.md"];
            // Reuse rendering's marker rules without writing a target. Empty
            // and whitespace sections are structurally valid, not proof of coverage.
            var rendered = InstructionSections.Render(shared, policy, overlay);
            if (StrictUtf8.GetByteCount(rendered) > AecApplication.MaximumTextBytes)
                throw new InvalidDataException($"Rendered instructions exceed 1 MiB: {harness.Provider}.");
        }
    }

    // Read-only boundary exposed internally for deterministic drift tests. This
    // validates observations, never grants write authority to caller buffers.
    internal static void VerifyFinalState(MigrationPreparation expected, string branch,
        byte[] index, MigrationDirtySections dirty, IReadOnlyList<MigrationValidatedSource> files,
        Dictionary<string, byte[]?> protectedFiles, string? targetRelativePath = null,
        byte[]? expectedCompletionMarker = null)
    {
        var repository = expected.Preflight.Repository;
        // History and committed blobs were checked once at entry. Recheck only
        // mutable state here; no full-preview/history rebuild around each file.
        MigrationFastForward.EnsureSafeGitConfiguration(repository);
        AecApplication.EnsureNoLinksInExistingPath(repository, "Source repository path");
        AecApplication.EnsureRealDirectory(repository, "Source repository");
        var gitDirectory = Path.Combine(repository, ".git");
        AecApplication.EnsureNoLinksInExistingPath(gitDirectory, "Source Git path");
        AecApplication.EnsureRealDirectory(gitDirectory, "Source Git directory");
        InitCommand.EnsureContainedGitMetadata(repository, gitDirectory);
        var marker = ReadFile(Path.Combine(repository, MigrationPreflight.FormatRelativePath));
        if (!SameBytes(expectedCompletionMarker, marker))
            throw new InvalidOperationException("Migration completion marker appeared during source validation.");
        VerifyEnrollment(expected);
        foreach (var file in files)
        {
            if (!SameBytes(file.WorkingContent, ReadFile(Path.Combine(repository, file.RelativePath))))
                throw new InvalidOperationException($"Instruction source changed during validation: {file.RelativePath}.");
        }
        foreach (var (path, content) in protectedFiles)
        {
            if (!SameBytes(content, ReadFile(path)))
                throw new InvalidOperationException($"Protected migration input changed during source validation: {path}.");
        }
        var currentDirty = expectedCompletionMarker is not null
            ? MigrationDirtySections.ReadForCompletion(expected.Preflight, expected.Harnesses)
            : targetRelativePath is null
            ? MigrationDirtySections.Read(expected.Preflight, expected.Harnesses)
            : MigrationDirtySections.ReadForTarget(expected.Preflight, expected.Harnesses, targetRelativePath);
        if (!currentDirty.Paths.SequenceEqual(dirty.Paths, StringComparer.Ordinal))
            throw new InvalidOperationException("Dirty paths changed during source validation.");
        if (!index.AsSpan().SequenceEqual(ReadIndex(repository)))
            throw new InvalidOperationException("Git index changed during source validation.");
        if (ApplyCommand.ResolveHeadCommit(repository) != expected.Preflight.Commit)
            throw new InvalidOperationException("HEAD changed during source validation.");
        if (MigrationLegacyCapturePreview.ReadBranch(repository) != branch)
            throw new InvalidOperationException("Branch changed during source validation.");
    }

    private static void VerifyEnrollment(MigrationPreparation expected)
    {
        if (!MigrationHarnessSelection.Read(expected.Preflight).SequenceEqual(expected.Harnesses))
            throw new InvalidOperationException("Local harness enrollment changed during source validation.");
    }

    internal static Dictionary<string, byte[]?> ReadProtectedFiles(MigrationPreparation expected)
    {
        var files = new Dictionary<string, byte[]?>(StringComparer.Ordinal);
        string[] legacy = [AecApplication.SourceRelativePath, AecApplication.ConfigSourceRelativePath,
            AecApplication.CopilotSourceRelativePath];
        foreach (var relative in legacy)
        {
            var path = Path.Combine(expected.Preflight.Repository, relative);
            files.Add(path, ReadFile(path));
        }
        foreach (var harness in expected.Harnesses)
        {
            files.Add(harness.RuntimePath, ReadFile(harness.RuntimePath));
            if (harness.Provider == "codex")
            {
                var path = Path.Combine(harness.Home, "config.toml");
                files.Add(path, ReadFile(path));
            }
        }
        return files;
    }

    private static byte[] ReadIndex(string repository) =>
        MigrationLegacyCaptureWriter.ReadIndex(Path.Combine(repository, ".git", "index"));

    private static byte[]? ReadFile(string path)
    {
        AecApplication.EnsureNoLinksInExistingPath(Path.GetDirectoryName(path)!, "Source validation input path");
        return AecApplication.ReadOptionalTextFile(path, "Source validation input");
    }

    private static bool SameBytes(byte[]? expected, byte[]? current)
    {
        if (expected is null) return current is null;
        if (current is null) return false;
        return expected.AsSpan().SequenceEqual(current);
    }
}
