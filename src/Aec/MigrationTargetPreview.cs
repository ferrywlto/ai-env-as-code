using System.Text;

namespace Aec;

internal enum MigrationTargetAction { Create, Unchanged, Regenerate }

// Advisory observations for one local harness, not permission to write. A later
// writer must freshly validate the sources, target and migration context.
internal sealed record MigrationTargetPreview(MigrationPreparation Preparation,
    string Branch, string Platform, MigrationHarness Harness,
    IReadOnlyList<MigrationValidatedSource> Sources, string TargetRelativePath,
    byte[]? CommittedContent, string? CommittedMode, byte[]? WorkingContent,
    byte[] DesiredContent, MigrationTargetAction Action)
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    // Sources is the selected composition; the writer also freezes required
    // sources for other confirmed harnesses without rereading their history.
    internal IReadOnlyList<MigrationValidatedSource> RequiredSources { get; init; } = [];

    public static MigrationTargetPreview Read(MigrationPreparation preparation, string provider)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        var fileName = provider switch
        {
            "codex" => "AGENTS.md",
            "copilot" => "copilot-instructions.md",
            _ => throw new ArgumentException($"Unsupported migration target provider: {provider}.", nameof(provider))
        };

        var validated = MigrationSourceValidationPreview.Read(preparation);
        if (validated.ChangedPaths.Count != 0)
            throw new InvalidOperationException("Commit all required instruction sources before previewing a migration target.");
        var harness = validated.Preparation.Harnesses.SingleOrDefault(item => item.Provider == provider)
            ?? throw new InvalidOperationException($"Migration target provider is not locally enrolled: {provider}.");

        var repository = validated.Preparation.Preflight.Repository;
        var platform = RenderCommand.CurrentPlatform();
        var relative = $"environment/targets/{platform}/{provider}/{fileName}";
        var path = Path.Combine(repository, relative);
        var index = MigrationLegacyCaptureWriter.ReadIndex(Path.Combine(repository, ".git", "index"));
        var protectedFiles = MigrationSourceValidationPreview.ReadProtectedFiles(validated.Preparation);

        // Read the old target against its own Git baseline, not against the new
        // sources. A source-only commit legitimately makes an old target stale.
        AecApplication.EnsureNoLinksInExistingPath(Path.GetDirectoryName(path)!, "Migration target path");
        var baseline = MigrationScaffoldPreview.ReadCommittedSection(validated.Preparation.Preflight, relative);
        var indexEntry = MigrationScaffoldPreview.ReadIndexEntry(repository, relative);
        if (indexEntry != baseline.ExpectedIndexEntry)
            throw new InvalidOperationException($"Migration target has staged changes: {relative}.");
        var working = AecApplication.ReadOptionalTextFile(path, "Migration target");
        if (!SameBytes(baseline.Content, working))
            throw new InvalidOperationException($"Migration target differs from its committed baseline: {relative}.");
        protectedFiles.Add(path, working);

        if (provider == "copilot")
        {
            var canonical = MigrationLegacyCapturePreview.ReadHistoricalFile(repository,
                validated.Preparation.Preflight.Commit, AecApplication.CopilotSourceRelativePath);
            if (CopilotInstructionBlock.ReadRepositoryBinding(canonical) is null)
                throw new InvalidDataException("Canonical Copilot instructions have no supported AEC block.");
        }

        string[] sourcePaths = ["environment/shared/instructions.md",
            $"environment/platforms/{platform}/policy.md", $"environment/providers/{provider}/overlay.md"];
        var sources = sourcePaths.Select(source =>
            validated.Files.Single(file => file.RelativePath == source)).ToArray();
        var desired = StrictUtf8.GetBytes(InstructionSections.Render(
            StrictUtf8.GetString(sources[0].CommittedContent!),
            StrictUtf8.GetString(sources[1].CommittedContent!),
            StrictUtf8.GetString(sources[2].CommittedContent!)));
        if (desired.Length > AecApplication.MaximumTextBytes)
            throw new InvalidDataException($"Rendered migration target exceeds 1 MiB: {relative}.");

        var action = MigrationTargetAction.Create;
        if (baseline.Content is not null)
        {
            action = baseline.Content.AsSpan().SequenceEqual(desired)
                ? MigrationTargetAction.Unchanged : MigrationTargetAction.Regenerate;
        }

        // The raw-index snapshot also pins the target's stage-0 entry. Reuse
        // one mutable-state guard, not another full history/source preview.
        // Protected observations cover this read only.
        MigrationSourceValidationPreview.VerifyFinalState(validated.Preparation, validated.Branch,
            index, new MigrationDirtySections([]), validated.Files, protectedFiles);
        if (baseline.Content is null)
            MigrationScaffoldPreview.VerifyMissingPathNotIgnored(repository, relative);
        return new MigrationTargetPreview(validated.Preparation, validated.Branch, platform, harness,
            sources, relative, baseline.Content, baseline.Mode, working, desired, action)
        {
            RequiredSources = validated.Files
        };
    }

    private static bool SameBytes(byte[]? before, byte[]? current)
    {
        if (before is null) return current is null;
        if (current is null) return false;
        return before.AsSpan().SequenceEqual(current);
    }
}
