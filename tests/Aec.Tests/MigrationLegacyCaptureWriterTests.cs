using System.Text;

namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class MigrationLegacyCaptureWriterTests
{
    [Fact]
    public void NoChangeLeavesTheEntireFixtureUntouched()
    {
        using var layout = new MigrationRepositoryFixture(includeCopilotCanonical: true);
        var preview = Preview(layout);

        layout.AssertNoChanges(() => Assert.Empty(MigrationLegacyCaptureWriter.Write(preview)));
    }

    [Fact]
    public void WritesOnlyReviewedLegacyFilesPreservingDraftsRuntimeAndGit()
    {
        using var layout = new MigrationRepositoryFixture(includeCopilotCanonical: true);
        File.AppendAllText(Runtime(layout, "codex"), "Codex runtime edit.\n");
        File.AppendAllText(Runtime(layout, "copilot"), "Copilot runtime edit.\n");
        File.WriteAllText(ConfigRuntime(layout), "personality = 'pragmatic'\nmodel = 'machine-only'\n");
        var draft = "environment/shared/instructions.md";
        layout.Write(draft, "Staged authored draft.\n");
        layout.Git("add", "--", draft);
        layout.Write(draft, "Staged and unstaged authored draft.\n");
        var preview = Preview(layout);
        var changed = preview.Files.Where(file => file.HasChanges).Select(file => file.RelativePath).ToArray();

        AssertOnlyChanged(layout, changed, () =>
        {
            var written = MigrationLegacyCaptureWriter.Write(preview);
            Assert.Collection(written,
                path => Assert.Equal(AecApplication.SourceRelativePath, path),
                path => Assert.Equal(AecApplication.ConfigSourceRelativePath, path),
                path => Assert.Equal(AecApplication.CopilotSourceRelativePath, path));
            foreach (var file in preview.Files) Assert.Equal(file.ProposedCanonical,
                File.ReadAllBytes(Path.Combine(layout.Repository, file.RelativePath)));
        });
        Assert.DoesNotContain("machine-only", File.ReadAllText(Path.Combine(layout.Repository,
            AecApplication.ConfigSourceRelativePath)), StringComparison.Ordinal);
    }

    [Fact]
    public void DoesNotCreateUnselectedProviderFiles()
    {
        using var layout = new MigrationRepositoryFixture();
        File.Delete(layout.CopilotRuntime);
        File.AppendAllText(Runtime(layout, "codex"), "Codex-only runtime edit.\n");
        var preview = Preview(layout);
        var changed = preview.Files.Where(file => file.HasChanges).Select(file => file.RelativePath).ToArray();

        AssertOnlyChanged(layout, changed, () => Assert.Equal(AecApplication.SourceRelativePath,
            Assert.Single(MigrationLegacyCaptureWriter.Write(preview))));
        Assert.False(File.Exists(Path.Combine(layout.Repository, AecApplication.CopilotSourceRelativePath)));
    }

    [Theory]
    [InlineData("head", "capture context changed")]
    [InlineData("branch", "inputs or proposal changed")]
    [InlineData("runtime-codex", "inputs or proposal changed")]
    [InlineData("runtime-copilot", "inputs or proposal changed")]
    [InlineData("runtime-config", "inputs or proposal changed")]
    [InlineData("canonical", "unstaged changes")]
    [InlineData("canonical-config", "unstaged changes")]
    [InlineData("binding", "different AEC data repository")]
    [InlineData("enrollment", "capture context changed")]
    [InlineData("marker", "single committed Git file")]
    public void StaleInputsStopBeforeAnyReplacement(string change, string expectedMessage)
    {
        using var layout = new MigrationRepositoryFixture(includeCopilotCanonical: true);
        File.AppendAllText(Runtime(layout, "codex"), "Reviewed runtime edit.\n");
        var preview = Preview(layout);
        switch (change)
        {
            case "head": layout.Git("commit", "--allow-empty", "-m", "User checkpoint"); break;
            case "branch": layout.Git("switch", "-c", "other-branch"); break;
            case "runtime-codex": File.AppendAllText(Runtime(layout, "codex"), "Later edit.\n"); break;
            case "runtime-copilot": File.AppendAllText(Runtime(layout, "copilot"), "Later edit.\n"); break;
            case "runtime-config": File.AppendAllText(ConfigRuntime(layout), "model = 'new-machine-setting'\n"); break;
            case "canonical": File.AppendAllText(Path.Combine(layout.Repository, AecApplication.SourceRelativePath), "User canonical edit.\n"); break;
            case "canonical-config": File.AppendAllText(Path.Combine(layout.Repository, AecApplication.ConfigSourceRelativePath), "# User comment\n"); break;
            case "binding": File.WriteAllBytes(Runtime(layout, "codex"), AecInstructionBlock.Merge(
                Encoding.UTF8.GetBytes("Other binding.\n"), Path.Combine(layout.Root, "other-data"))); break;
            case "enrollment": File.Delete(layout.CopilotRuntime); break;
            default: layout.Write(MigrationPreflight.FormatRelativePath, "2.0\n"); break;
        }

        layout.AssertNoChanges(() =>
        {
            var error = Assert.Throws<InvalidOperationException>(() => MigrationLegacyCaptureWriter.Write(preview));
            Assert.Contains(expectedMessage, error.Message, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("content")]
    [InlineData("path")]
    [InlineData("missing-row")]
    public void AlteredReviewedProposalsCannotAuthorizeWrites(string alteration)
    {
        using var layout = new MigrationRepositoryFixture(includeCopilotCanonical: true);
        File.AppendAllText(Runtime(layout, "codex"), "Reviewed edit.\n");
        var preview = Preview(layout);
        var files = preview.Files.ToArray();
        if (alteration == "content") files[0] = files[0] with { ProposedCanonical = Encoding.UTF8.GetBytes("Unreviewed proposal.\n") };
        if (alteration == "path") files[0] = files[0] with { RelativePath = "../escape.md" };
        if (alteration == "missing-row") files = files.Skip(1).ToArray();
        var altered = preview with { Files = files };

        layout.AssertNoChanges(() =>
        {
            var error = Assert.Throws<InvalidOperationException>(() => MigrationLegacyCaptureWriter.Write(altered));
            Assert.Contains("inputs or proposal changed", error.Message, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WriteFailureReportsCompletedAndAttemptedPathsWithoutRollback(bool earlierWrite)
    {
        using var layout = new MigrationRepositoryFixture(includeCopilotCanonical: true);
        if (earlierWrite) File.AppendAllText(Runtime(layout, "codex"), "First replacement.\n");
        File.AppendAllText(Runtime(layout, "copilot"), "Blocked later replacement.\n");
        var preview = Preview(layout);
        var blocked = Path.Combine(layout.Repository, AecApplication.CopilotSourceRelativePath);
        var previousCopilot = File.ReadAllBytes(blocked);
        var allowed = preview.Files.Where(file => earlierWrite && file.RelativePath == AecApplication.SourceRelativePath)
            .Select(file => file.RelativePath).ToArray();

        AssertOnlyChanged(layout, allowed, () => BlockReplacement(blocked, () =>
        {
            var error = Assert.Throws<MigrationLegacyCaptureWriteException>(() => MigrationLegacyCaptureWriter.Write(preview));
            Assert.Equal(AecApplication.CopilotSourceRelativePath, error.AttemptedPath);
            Assert.Equal(earlierWrite ? 1 : 0, error.WrittenPaths.Count);
            if (earlierWrite) Assert.Equal(AecApplication.SourceRelativePath, Assert.Single(error.WrittenPaths));
            Assert.Contains("No rollback, staging or commit", error.Message, StringComparison.Ordinal);
            Assert.NotNull(error.InnerException);
        }));
        Assert.Equal(previousCopilot, File.ReadAllBytes(blocked));
    }

    private static MigrationLegacyCapturePreview Preview(MigrationRepositoryFixture layout)
    {
        var preflight = MigrationPreflight.Read(layout.Repository, "2.0");
        var harnesses = MigrationHarnessSelection.Read(preflight);
        var dirty = MigrationDirtySections.Read(preflight, harnesses);
        var prepared = new MigrationPreparation(preflight, preflight.Commit, harnesses, dirty,
            dirty.Paths.Count == 0 ? MigrationSectionChoice.Unchanged : MigrationSectionChoice.Preserve);
        return MigrationLegacyCapturePreview.Read(prepared);
    }

    private static string Runtime(MigrationRepositoryFixture layout, string provider) => provider == "codex"
        ? Path.Combine(layout.Root, "codex-home", "AGENTS.md") : layout.CopilotRuntime;

    private static string ConfigRuntime(MigrationRepositoryFixture layout) => Path.Combine(layout.Root, "codex-home", "config.toml");

    private static void AssertOnlyChanged(MigrationRepositoryFixture layout, IReadOnlyList<string> allowed, Action action)
    {
        var files = Directory.EnumerateFiles(layout.Root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        var directories = Directory.GetDirectories(layout.Root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();
        var changed = allowed.Select(path => Path.Combine(layout.Repository, path)).ToHashSet(StringComparer.Ordinal);
        action();
        Assert.Equal(files.Keys.Order(StringComparer.Ordinal), Directory.EnumerateFiles(layout.Root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
        Assert.Equal(directories, Directory.GetDirectories(layout.Root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
        foreach (var (path, bytes) in files)
        {
            var current = File.ReadAllBytes(path);
            if (changed.Contains(path)) Assert.False(bytes.AsSpan().SequenceEqual(current));
            else Assert.Equal(bytes, current);
        }
    }

    internal static void BlockReplacement(string path, Action action)
    {
        if (OperatingSystem.IsWindows())
        {
            // Windows disallows replacing a file held without delete sharing.
            using var held = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            action();
            return;
        }
        var directory = Path.GetDirectoryName(path)!;
        var mode = File.GetUnixFileMode(directory);
        try
        {
            // Existing bytes remain readable for preflight, but a later atomic
            // replacement cannot create its sibling temporary file here.
            File.SetUnixFileMode(directory, mode & ~(UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite));
            action();
        }
        finally
        {
            File.SetUnixFileMode(directory, mode);
        }
    }
}
