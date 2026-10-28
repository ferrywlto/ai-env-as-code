using System.Text;

namespace Aec;

// Read-only foundation, not the interactive migrate command. Dirty section
// ownership, local harnesses and remote state belong to later preflight steps.
internal sealed record MigrationPreflight(
    string Repository,
    string Commit,
    Version CurrentFormat,
    Version TargetFormat)
{
    internal const string FormatRelativePath = ".aec-version";

    // Legacy CLI releases did not record data versions. 1.0 names that layout
    // generation, not a historical CLI or managed-block version.
    internal static readonly Version LegacyFormat = new(1, 0);
    internal static readonly Version SectionedFormat = new(2, 0);

    public static MigrationPreflight Read(string repository, string targetVersion)
    {
        var target = ParseFormatVersion(targetVersion, "Migration target");
        repository = AecApplication.RequireAbsolutePath(repository, "--repo");
        AecApplication.EnsureNoLinksInExistingPath(repository, "Repository path");
        AecApplication.EnsureRealDirectory(repository, "Repository");
        var gitDirectory = Path.Combine(repository, ".git");
        AecApplication.EnsureRealDirectory(gitDirectory, "Git directory");
        InitCommand.EnsureContainedGitMetadata(repository, gitDirectory);
        BackupCommand.ValidateRepository(repository);
        var commit = ApplyCommand.ResolveHeadCommit(repository);
        InitCommand.ValidateInitializationHistory(repository);

        var markerPath = Path.Combine(repository, FormatRelativePath);
        var marker = AecApplication.ReadOptionalTextFile(markerPath, "Repository format marker");
        var tracked = GitProcess.RunRequired(repository, "Git could not inspect the format marker",
            "ls-tree", "--name-only", "-z", commit, "--", FormatRelativePath).Output.Length != 0;
        var current = LegacyFormat;
        if (marker is not null || tracked)
        {
            // Untracked, staged, deleted or transformed markers must fail, never
            // fall back to legacy. A section directory alone proves no completion.
            var bytes = ApplyCommand.ReadCommittedFile(repository, commit,
                FormatRelativePath, "Repository format marker");
            current = ParseFormatVersion(new UTF8Encoding(false, true).GetString(bytes)
                .TrimEnd('\r', '\n'), "Repository format marker");
            if (current != SectionedFormat)
            {
                // No legacy release issued a marker; 1.0 is recognized only by
                // absence plus the validated legacy environment, not a new claim.
                throw new InvalidDataException(
                    "Repository format marker does not declare a supported completed data format.");
            }
        }

        if (current == LegacyFormat)
        {
            // Historical initialization alone cannot prove later commits left a
            // usable legacy environment. Validate today's committed managed files
            // without rejecting unrelated draft section edits.
            var source = ApplyCommand.ReadCommittedSource(repository, commit);
            _ = AecInstructionBlock.ReadRepositoryBinding(source)
                ?? throw new InvalidDataException(
                    "Canonical instructions do not contain a supported initialized AEC block.");
            var config = ApplyCommand.ReadCommittedConfig(repository, commit);
            _ = CodexPersonalityConfig.ReadCanonical(config,
                Path.Combine(repository, AecApplication.ConfigSourceRelativePath));
        }

        var binaryVersion = typeof(AecApplication).Assembly.GetName().Version
            ?? throw new InvalidOperationException("Application version could not be resolved.");
        ValidateVersions(current, target, binaryVersion);
        if (!string.Equals(commit, ApplyCommand.ResolveHeadCommit(repository), StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Repository HEAD changed during migration preflight.");
        }

        return new MigrationPreflight(repository, commit, current, target);
    }

    internal static Version ParseFormatVersion(string value, string label)
    {
        // Data formats use canonical major.minor, not CLI patch/prerelease text.
        if (!Version.TryParse(value, out var version) || version.Build != -1 ||
            !string.Equals(version.ToString(2), value, StringComparison.Ordinal))
        {
            throw new ArgumentException($"{label} must be a canonical major.minor version, such as 2.0.");
        }

        return version;
    }

    internal static void ValidateVersions(Version current, Version target, Version binaryVersion)
    {
        if (target == current)
        {
            throw new InvalidOperationException($"Repository data format is already {target}.");
        }
        if (target < current)
        {
            throw new InvalidOperationException($"Repository format downgrade from {current} to {target} is not supported.");
        }

        // A 2.0.x prerelease binary can migrate to format 2.0. Patch releases do
        // not require data migrations; a newer binary still needs a known path.
        var capability = new Version(binaryVersion.Major, binaryVersion.Minor);
        if (target > capability)
        {
            throw new InvalidOperationException($"This binary cannot migrate to data format {target}.");
        }
        if (current != LegacyFormat || target != SectionedFormat)
        {
            throw new InvalidOperationException($"Migration from data format {current} to {target} is not supported.");
        }
    }
}
