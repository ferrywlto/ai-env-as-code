using System.Text;

namespace Aec;

// Render only committed enrollments affected by one reviewed backup source edit.
// This repository-only step is not connected to the public render or backup CLI.
internal static class PortableBackupTargetWriter
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly string[] Platforms = ["macos-arm64", "windows-x64", "linux-arm64"];

    public static IReadOnlyList<string> Render(
        PortableRuntimeSnapshot reviewed,
        PortableBackupAttributionPreview attribution)
        => Process(reviewed, attribution, null);

    // The commit step calls the same enrollment/render calculation after the
    // writes, so a caller cannot omit an affected platform or provider target.
    internal static IReadOnlyList<string> VerifyRendered(
        PortableRuntimeSnapshot reviewed,
        PortableBackupAttributionPreview attribution,
        IReadOnlyList<string> renderedPaths)
        => Process(reviewed, attribution, renderedPaths);

    private static string[] Process(
        PortableRuntimeSnapshot reviewed,
        PortableBackupAttributionPreview attribution,
        IReadOnlyList<string>? renderedPaths)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        ArgumentNullException.ThrowIfNull(attribution);
        if (reviewed.Runtime is null || attribution.ProposedContent is null)
        {
            throw new InvalidOperationException("Backup rendering requires a reviewed runtime edit.");
        }

        var sourcePath = attribution.Section switch
        {
            "shared" => "environment/shared/instructions.md",
            "platform" => $"environment/platforms/{reviewed.Platform}/policy.md",
            "provider" => $"environment/providers/{reviewed.Provider}/overlay.md",
            _ => throw new InvalidOperationException("Backup rendering requires one authored section.")
        };
        if (!string.Equals(sourcePath, attribution.SourceRelativePath, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Reviewed backup source path does not match its section.");
        }

        AecApplication.EnsureNoLinksInExistingPath(reviewed.Repository, "Repository path");
        ApplyCommand.ValidateRepositoryRoot(reviewed.Repository);
        var providerHome = Path.GetDirectoryName(reviewed.RuntimePath)
            ?? throw new InvalidOperationException("Provider runtime target has no parent directory.");
        AecApplication.EnsureNoLinksInExistingPath(providerHome, "Provider home path");
        ApplyCommand.EnsureRuntimeOutsideRepository(
            reviewed.Repository, providerHome, Path.GetFileName(reviewed.RuntimePath),
            "Provider runtime target");
        var sources = new Dictionary<string, Source>(StringComparer.Ordinal);
        var changes = new List<TargetChange>();
        var enrolled = GitProcess.RunRequired(
            reviewed.Repository, "Could not list committed target enrollments",
            "ls-tree", "-r", "--name-only", reviewed.Commit, "--", "environment/targets")
            .Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var path in enrolled.OrderBy(value => value, StringComparer.Ordinal))
        {
            var parts = path.TrimEnd('\r').Split('/');
            if (parts.Length != 5 || parts[0] != "environment" || parts[1] != "targets" ||
                !Platforms.Contains(parts[2], StringComparer.Ordinal) ||
                parts[3] is not ("codex" or "copilot") ||
                parts[4] != (parts[3] == "codex" ? "AGENTS.md" : "copilot-instructions.md"))
            {
                continue;
            }

            var platform = parts[2];
            var provider = parts[3];
            if (attribution.Section == "platform" && platform != reviewed.Platform ||
                attribution.Section == "provider" && provider != reviewed.Provider)
            {
                continue;
            }

            var shared = ReadSource("environment/shared/instructions.md");
            var policy = ReadSource($"environment/platforms/{platform}/policy.md");
            var overlay = ReadSource($"environment/providers/{provider}/overlay.md");
            var baseline = StrictUtf8.GetBytes(InstructionSections.Render(
                shared.Baseline, policy.Baseline, overlay.Baseline));
            var desired = StrictUtf8.GetBytes(InstructionSections.Render(
                shared.Current, policy.Current, overlay.Current));
            if (desired.Length > AecApplication.MaximumTextBytes)
            {
                throw new InvalidDataException($"Rendered target exceeds 1 MiB: {path}");
            }

            var targetPath = Path.Combine(reviewed.Repository, path);
            AecApplication.EnsureNoLinksInExistingPath(
                Path.GetDirectoryName(targetPath)!, "Rendered target path");
            var current = renderedPaths is null
                ? ApplyCommand.ReadCommittedFile(
                    reviewed.Repository, reviewed.Commit, path, "Rendered target")
                : GitProcess.RunRequiredBytes(reviewed.Repository,
                    "Could not read committed rendered target", "show", $"{reviewed.Commit}:{path}");
            if (!current.AsSpan().SequenceEqual(baseline))
            {
                throw new InvalidDataException(
                    $"Committed target does not match its authored sources: {path}");
            }

            if (!current.AsSpan().SequenceEqual(desired))
            {
                changes.Add(new TargetChange(path, targetPath, current, desired));
            }
        }

        if (changes.Count == 0)
        {
            throw new InvalidOperationException("No enrolled target consumes the reviewed backup edit.");
        }

        if (renderedPaths is not null)
        {
            var expected = changes.Select(change => change.RelativePath).ToArray();
            if (!expected.SequenceEqual(renderedPaths, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    "Rendered target paths do not match all affected committed enrollments.");
            }

            foreach (var change in changes)
            {
                AecApplication.EnsureNoLinksInExistingPath(
                    Path.GetDirectoryName(change.Path)!, "Rendered target path");
            }

            VerifyInputs(changes);
            return expected;
        }

        VerifyInputs([]);
        var written = new List<TargetChange>();
        foreach (var change in changes)
        {
            try
            {
                VerifyInputs(written);
                AecApplication.EnsureNoLinksInExistingPath(
                    Path.GetDirectoryName(change.Path)!, "Rendered target path");
                AtomicFile.ReplaceIfUnchanged(
                    change.Path, change.Current, change.Desired, "Rendered target");
                written.Add(change);
            }
            catch (Exception error) when (error is IOException or InvalidOperationException or
                                          InvalidDataException or UnauthorizedAccessException)
            {
                // Earlier target replacements are not rolled back. The failed
                // replacement can itself have reached its final move.
                throw new IOException(
                    $"Backup rendering stopped at {change.RelativePath}; this and earlier " +
                    "targets may have changed. Inspect the repository before retrying.", error);
            }
        }

        try
        {
            VerifyInputs(written);
        }
        catch (Exception error) when (error is IOException or InvalidOperationException or
                                      InvalidDataException or UnauthorizedAccessException)
        {
            throw new IOException(
                "Backup rendering verification failed after target replacements; " +
                "targets may have changed. Inspect the repository before retrying.", error);
        }
        return written.Select(change => change.RelativePath).ToArray();

        Source ReadSource(string relativePath)
        {
            if (sources.TryGetValue(relativePath, out var cached))
            {
                return cached;
            }

            var path = Path.Combine(reviewed.Repository, relativePath);
            AecApplication.EnsureNoLinksInExistingPath(
                Path.GetDirectoryName(path)!, "Authored source path");
            var current = AecApplication.ReadRequiredTextFile(path, "Authored source");
            byte[] baseline;
            if (relativePath == sourcePath)
            {
                baseline = GitProcess.RunRequiredBytes(
                    reviewed.Repository, "Could not read committed authored source",
                    "show", $"{reviewed.Commit}:{relativePath}");
                if (!current.AsSpan().SequenceEqual(StrictUtf8.GetBytes(attribution.ProposedContent)))
                {
                    throw new InvalidOperationException(
                        "Authored source differs from the reviewed backup edit.");
                }
            }
            else
            {
                baseline = ApplyCommand.ReadCommittedFile(
                    reviewed.Repository, reviewed.Commit, relativePath, "Authored source");
            }

            var result = new Source(
                current, StrictUtf8.GetString(baseline), StrictUtf8.GetString(current));
            sources.Add(relativePath, result);
            return result;
        }

        void VerifyInputs(IReadOnlyList<TargetChange> previousWrites)
        {
            if (!string.Equals(reviewed.Commit,
                    ApplyCommand.ResolveHeadCommit(reviewed.Repository), StringComparison.Ordinal) ||
                !AecApplication.ReadRequiredTextFile(
                    reviewed.RuntimePath, "Provider runtime instructions")
                    .AsSpan().SequenceEqual(reviewed.Runtime))
            {
                throw new InvalidOperationException(
                    "Backup inputs changed while rendering targets.");
            }

            foreach (var (path, source) in sources)
            {
                AecApplication.EnsureNoLinksInExistingPath(
                    Path.GetDirectoryName(Path.Combine(reviewed.Repository, path))!,
                    "Authored source path");
                if (!AecApplication.ReadRequiredTextFile(
                        Path.Combine(reviewed.Repository, path), "Authored source")
                        .AsSpan().SequenceEqual(source.Bytes))
                {
                    throw new InvalidOperationException(
                        $"Authored source changed while rendering targets: {path}");
                }
            }

            // Staged source or target changes would make a later exact-path
            // backup commit ambiguous, even if working bytes still match.
            var paths = sources.Keys.Concat(changes.Select(change => change.RelativePath))
                .Distinct(StringComparer.Ordinal).ToArray();
            var staged = GitProcess.Run(reviewed.Repository,
                ["diff", "--cached", "--quiet", "--exit-code", reviewed.Commit, "--", .. paths]);
            if (staged.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    "An authored source or affected target has staged changes.");
            }

            foreach (var change in previousWrites)
            {
                if (!AecApplication.ReadRequiredTextFile(change.Path, "Rendered target")
                        .AsSpan().SequenceEqual(change.Desired))
                {
                    throw new InvalidOperationException(
                        $"A rendered target changed after replacement: {change.RelativePath}");
                }
            }
        }
    }

    private sealed record Source(byte[] Bytes, string Baseline, string Current);
    private sealed record TargetChange(string RelativePath, string Path, byte[] Current, byte[] Desired);
}
