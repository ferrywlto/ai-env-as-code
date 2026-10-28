using System.Text;

namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class MigrationLegacyCaptureCommitterTests
{
    [Fact]
    public void NoChangeMakesNoCommitAndLeavesEntireFixtureUntouched()
    {
        using var layout = new MigrationRepositoryFixture(includeCopilotCanonical: true);
        var preview = Preview(layout);
        layout.AssertNoChanges(() => Assert.Null(MigrationLegacyCaptureCommitter.WriteAndCommit(preview)));
    }

    [Fact]
    public void CommitsOnlyChangedLegacyFilesPreservingRuntimeAndAuthoredDrafts()
    {
        using var layout = new MigrationRepositoryFixture(includeCopilotCanonical: true);
        File.AppendAllText(CodexRuntime(layout), "Codex runtime edit.\n");
        File.AppendAllText(layout.CopilotRuntime, "Copilot runtime edit.\n");
        File.WriteAllText(ConfigRuntime(layout), "personality = 'pragmatic'\nmodel = 'machine-only'\n");
        const string draft = "environment/shared/instructions.md";
        layout.Write(draft, "Unstaged authored draft.\n");
        var runtime = layout.RuntimeBytes();
        var preview = Preview(layout);
        var parent = layout.Head();
        var remote = layout.Git("rev-parse", "refs/remotes/shared/main");

        var commit = MigrationLegacyCaptureCommitter.WriteAndCommit(preview);

        Assert.NotNull(commit);
        Assert.Equal(commit, layout.Head());
        Assert.Equal(parent, layout.Git("rev-parse", "HEAD^").Trim());
        Assert.Equal("Backup legacy environment before migration", layout.Git("log", "-1", "--format=%s").Trim());
        Assert.Equal(preview.Files.Where(file => file.HasChanges).Select(file => file.RelativePath).Order(StringComparer.Ordinal),
            layout.Git("diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal));
        foreach (var file in preview.Files)
        {
            Assert.Equal(file.ProposedCanonical, GitProcess.RunRequiredBytes(layout.Repository,
                "Read captured bytes", "show", $"HEAD:{file.RelativePath}"));
        }
        Assert.DoesNotContain("machine-only", File.ReadAllText(Path.Combine(layout.Repository,
            AecApplication.ConfigSourceRelativePath)), StringComparison.Ordinal);
        Assert.Equal("Unstaged authored draft.\n", File.ReadAllText(Path.Combine(layout.Repository, draft)));
        Assert.Equal("Committed fixture notes.\n", File.ReadAllText(Path.Combine(layout.Repository, "notes.md")));
        Assert.Equal(remote, layout.Git("rev-parse", "refs/remotes/shared/main"));
        Assert.Empty(layout.Git("diff", "--cached", "--name-only"));
        Assert.False(File.Exists(Path.Combine(layout.Repository, MigrationPreflight.FormatRelativePath)));
        layout.AssertRuntime(runtime);
    }

    [Fact]
    public void ConfigOnlyCaptureDoesNotCommitUnchangedInstructionFiles()
    {
        using var layout = new MigrationRepositoryFixture();
        File.Delete(layout.CopilotRuntime);
        File.WriteAllText(ConfigRuntime(layout), "personality = 'none'\nmodel = 'private'\n");
        var preview = Preview(layout);

        Assert.NotNull(MigrationLegacyCaptureCommitter.WriteAndCommit(preview));

        Assert.Equal(AecApplication.ConfigSourceRelativePath,
            layout.Git("diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD").Trim());
        Assert.False(File.Exists(Path.Combine(layout.Repository, AecApplication.CopilotSourceRelativePath)));
    }

    [Fact]
    public void ExistingStagedDraftStopsBeforeAnyCanonicalWriteOrIndexChange()
    {
        using var layout = new MigrationRepositoryFixture();
        File.Delete(layout.CopilotRuntime);
        File.AppendAllText(CodexRuntime(layout), "Reviewed runtime edit.\n");
        const string draft = "environment/shared/instructions.md";
        layout.Write(draft, "Staged draft.\n");
        layout.Git("add", "--", draft);
        layout.Write(draft, "Staged plus unstaged draft.\n");
        var preview = Preview(layout);

        layout.AssertNoChanges(() =>
        {
            var error = Assert.Throws<InvalidOperationException>(() => MigrationLegacyCaptureCommitter.WriteAndCommit(preview));
            Assert.Contains("empty staged diff", error.Message, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("head")]
    [InlineData("branch")]
    [InlineData("runtime")]
    [InlineData("canonical")]
    [InlineData("marker")]
    [InlineData("enrollment")]
    [InlineData("proposal")]
    [InlineData("path")]
    [InlineData("missing-row")]
    public void StaleOrTamperedReviewStopsBeforeWriteStageOrCommit(string change)
    {
        using var layout = new MigrationRepositoryFixture(includeCopilotCanonical: true);
        File.AppendAllText(CodexRuntime(layout), "Reviewed edit.\n");
        var preview = Preview(layout);
        switch (change)
        {
            case "head": layout.Git("commit", "--allow-empty", "-m", "Later commit"); break;
            case "branch": layout.Git("switch", "-c", "other-branch"); break;
            case "runtime": File.AppendAllText(layout.CopilotRuntime, "Later edit.\n"); break;
            case "canonical": layout.Write(AecApplication.ConfigSourceRelativePath, "personality = 'none'\n"); break;
            case "marker": layout.Write(MigrationPreflight.FormatRelativePath, "2.0\n"); break;
            case "enrollment": File.Delete(layout.CopilotRuntime); break;
            case "proposal": preview = preview with { Files = preview.Files.Select((file, index) => index == 0
                ? file with { ProposedCanonical = Encoding.UTF8.GetBytes("Unreviewed bytes.\n") } : file).ToArray() }; break;
            case "path": preview = preview with { Files = preview.Files.Select((file, index) => index == 0
                ? file with { RelativePath = "../outside.md" } : file).ToArray() }; break;
            default: preview = preview with { Files = preview.Files.Skip(1).ToArray() }; break;
        }

        layout.AssertNoChanges(() => Assert.Throws<InvalidOperationException>(() =>
            MigrationLegacyCaptureCommitter.WriteAndCommit(preview)));
    }

    [Fact]
    public void SigningFailureRetainsExactStagedCaptureWithoutRollbackOrCommit()
    {
        using var layout = new MigrationRepositoryFixture();
        File.Delete(layout.CopilotRuntime);
        File.AppendAllText(CodexRuntime(layout), "Captured edit.\n");
        var preview = Preview(layout);
        var parent = layout.Head();
        var runtime = layout.RuntimeBytes();
        layout.Git("config", "gpg.format", "openpgp");
        layout.Git("config", "gpg.program", Path.Combine(layout.Root, "missing-signing-program"));
        layout.Git("config", "commit.gpgSign", "true");

        var error = Assert.Throws<InvalidOperationException>(() => MigrationLegacyCaptureCommitter.WriteAndCommit(preview));

        Assert.Contains("staged changes or a new commit may remain", error.Message, StringComparison.Ordinal);
        Assert.NotNull(error.InnerException);
        Assert.Equal(parent, layout.Head());
        Assert.Equal(AecApplication.SourceRelativePath, layout.Git("diff", "--cached", "--name-only").Trim());
        Assert.Equal(preview.Files[0].ProposedCanonical, File.ReadAllBytes(Path.Combine(layout.Repository,
            AecApplication.SourceRelativePath)));
        Assert.Equal(preview.Files[0].ProposedCanonical, GitProcess.RunRequiredBytes(layout.Repository,
            "Read retained staged bytes", "show", $":{AecApplication.SourceRelativePath}"));
        layout.AssertRuntime(runtime);
    }

    [Fact]
    public void NewlineTransformationStopsBeforePublishingAndRetainsIndexForInspection()
    {
        using var layout = new MigrationRepositoryFixture();
        File.Delete(layout.CopilotRuntime);
        File.AppendAllText(CodexRuntime(layout), "Captured CRLF edit.\r\n");
        var preview = Preview(layout);
        var parent = layout.Head();
        File.WriteAllText(Path.Combine(layout.Repository, ".git", "info", "attributes"),
            $"{AecApplication.SourceRelativePath} text eol=lf\n");

        var error = Assert.Throws<InvalidOperationException>(() => MigrationLegacyCaptureCommitter.WriteAndCommit(preview));

        Assert.Contains("Staged capture bytes or file mode differ", error.InnerException!.Message, StringComparison.Ordinal);
        Assert.Equal(parent, layout.Head());
        Assert.Equal(AecApplication.SourceRelativePath, layout.Git("diff", "--cached", "--name-only").Trim());
        Assert.Equal(preview.Files[0].ProposedCanonical, File.ReadAllBytes(Path.Combine(layout.Repository,
            AecApplication.SourceRelativePath)));
    }

    [Fact]
    public void PinnedTreeModeMismatchStopsBeforePublishing()
    {
        using var layout = new MigrationRepositoryFixture();
        var parent = layout.Head();
        var path = AecApplication.SourceRelativePath;
        var mode = layout.Git("ls-tree", "HEAD", "--", path).Split(' ')[0];
        File.AppendAllText(Path.Combine(layout.Repository, path), "Canonical edit.\n");
        layout.Git("add", "--", path);
        layout.Git("update-index", mode == "100644" ? "--chmod=+x" : "--chmod=-x", "--", path);
        var blob = layout.Git("rev-parse", $":{path}").Trim();

        var error = Assert.Throws<InvalidOperationException>(() => BackupCommand.CommitStagedIndex(
            layout.Repository, "Must not publish", [(path, blob)], MigrationLegacyCapturePreview.ReadBranch(layout.Repository),
            parent, new Dictionary<string, string>(StringComparer.Ordinal) { [path] = mode }));

        Assert.Contains("Pinned canonical file mode or blob", error.Message, StringComparison.Ordinal);
        Assert.Equal(parent, layout.Head());
        Assert.Equal(path, layout.Git("diff", "--cached", "--name-only").Trim());
    }

    private static MigrationLegacyCapturePreview Preview(MigrationRepositoryFixture layout)
    {
        var preflight = MigrationPreflight.Read(layout.Repository, "2.0");
        var harnesses = MigrationHarnessSelection.Read(preflight);
        var dirty = MigrationDirtySections.Read(preflight, harnesses);
        return MigrationLegacyCapturePreview.Read(new MigrationPreparation(preflight, preflight.Commit, harnesses,
            dirty, dirty.Paths.Count == 0 ? MigrationSectionChoice.Unchanged : MigrationSectionChoice.Preserve));
    }

    private static string CodexRuntime(MigrationRepositoryFixture layout) => Path.Combine(layout.Root, "codex-home", "AGENTS.md");
    private static string ConfigRuntime(MigrationRepositoryFixture layout) => Path.Combine(layout.Root, "codex-home", "config.toml");
}
