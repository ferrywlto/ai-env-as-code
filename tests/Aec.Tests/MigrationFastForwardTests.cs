using System.Text;

namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class MigrationFastForwardTests
{
    [Theory]
    [InlineData("Equal")]
    [InlineData("Behind")]
    [InlineData("Ahead")]
    public void AdvancesOnlyCleanBehindCheckoutWithoutCapturingRuntime(string ancestry)
    {
        using var layout = new MigrationRepositoryFixture();
        if (ancestry == "Behind") layout.AdvanceRemote();
        if (ancestry == "Ahead") layout.Git("commit", "--allow-empty", "-m", "Local work");
        var (preflight, fetched, harnesses) = layout.Prepare();
        var runtime = layout.RuntimeBytes();
        var refs = layout.Git("for-each-ref", "--format=%(refname) %(objectname)", "refs/remotes");

        var result = MigrationFastForward.Execute(preflight, fetched, harnesses);

        Assert.Equal(ancestry == "Behind" ? fetched.Remote.AdvertisedCommit : preflight.Commit, result.Commit);
        Assert.Equal(result.Commit, layout.Head());
        Assert.Equal("refs/heads/main", layout.Git("symbolic-ref", "HEAD").Trim());
        Assert.Equal(refs, layout.Git("for-each-ref", "--format=%(refname) %(objectname)", "refs/remotes"));
        Assert.Empty(layout.Git("status", "--porcelain"));
        layout.AssertRuntime(runtime);
    }

    [Theory]
    [InlineData("tracked")]
    [InlineData("staged")]
    [InlineData("untracked")]
    [InlineData("section")]
    public void BehindStopsForDirtyCheckoutWithoutStashOrDiscard(string kind)
    {
        using var layout = new MigrationRepositoryFixture();
        layout.AdvanceRemote();
        var (preflight, fetched, harnesses) = layout.Prepare();
        var path = kind == "section" ? "environment/shared/instructions.md"
            : kind == "untracked" ? "local-draft.md" : "notes.md";
        layout.Write(path, "User draft must remain.\n");
        if (kind == "staged") layout.Git("add", "--", path);

        layout.AssertNoChanges(() =>
        {
            var error = Assert.Throws<InvalidOperationException>(() => MigrationFastForward.Execute(preflight, fetched, harnesses));
            Assert.Contains("clean checkout", error.Message, StringComparison.Ordinal);
        });
        Assert.Empty(layout.Git("stash", "list"));
    }

    [Theory]
    [InlineData("Equal")]
    [InlineData("Ahead")]
    public void NoAdvancementPreservesEligibleSectionDraft(string ancestry)
    {
        using var layout = new MigrationRepositoryFixture();
        if (ancestry == "Ahead") layout.Git("commit", "--allow-empty", "-m", "Local work");
        var (preflight, fetched, harnesses) = layout.Prepare();
        layout.Write("environment/shared/instructions.md", "Preserved draft.\n");

        layout.AssertNoChanges(() => Assert.Equal(preflight,
            MigrationFastForward.Execute(preflight, fetched, harnesses)));
    }

    [Theory]
    [InlineData("head")]
    [InlineData("branch")]
    [InlineData("remote")]
    [InlineData("runtime")]
    [InlineData("selection")]
    public void ChangedSnapshotStopsBeforeAdvancement(string change)
    {
        using var layout = new MigrationRepositoryFixture();
        layout.AdvanceRemote();
        var (preflight, fetched, harnesses) = layout.Prepare();
        switch (change)
        {
            case "head": layout.Git("commit", "--allow-empty", "-m", "Changed checkpoint"); break;
            case "branch": layout.Git("switch", "-c", "review"); break;
            case "remote": layout.AdvanceRemote(); break;
            case "runtime": File.WriteAllBytes(layout.CopilotRuntime,
                CopilotInstructionBlock.Merge(Encoding.UTF8.GetBytes("Other checkout.\n"),
                    Path.Combine(layout.Root, "other-data"))); break;
            default: File.Delete(layout.CopilotRuntime); break;
        }

        layout.AssertNoChanges(() => Assert.Throws<InvalidOperationException>(() =>
            MigrationFastForward.Execute(preflight, fetched, harnesses)));
    }

    [Fact]
    public void RecomputedAncestryRejectsAStaleOrIncorrectPreview()
    {
        using var layout = new MigrationRepositoryFixture();
        layout.AdvanceRemote();
        var (preflight, fetched, harnesses) = layout.Prepare();

        layout.AssertNoChanges(() => Assert.Throws<InvalidOperationException>(() =>
            MigrationFastForward.Execute(preflight, fetched with { Ancestry = MigrationAncestry.Equal }, harnesses)));
    }

    [Fact]
    public void IgnoredIncomingCollisionIsNeverOverwritten()
    {
        using var layout = new MigrationRepositoryFixture();
        layout.AdvanceRemote(writer => File.WriteAllText(Path.Combine(writer, "collision.md"), "Remote version.\n"));
        var (preflight, fetched, harnesses) = layout.Prepare();
        File.AppendAllText(Path.Combine(layout.Repository, ".git", "info", "exclude"), "\ncollision.md\n");
        layout.Write("collision.md", "Ignored local content.\n");
        var runtime = layout.RuntimeBytes();

        var error = Assert.Throws<InvalidOperationException>(() => MigrationFastForward.Execute(preflight, fetched, harnesses));

        Assert.Contains("fast-forward failed", error.Message, StringComparison.Ordinal);
        Assert.Equal(preflight.Commit, layout.Head());
        Assert.Equal("Ignored local content.\n", File.ReadAllText(Path.Combine(layout.Repository, "collision.md")));
        Assert.Empty(layout.Git("status", "--porcelain"));
        layout.AssertRuntime(runtime);
    }

    [Theory]
    [InlineData("marker")]
    [InlineData("instructions")]
    [InlineData("config")]
    public void InvalidPulledFormatStopsButRetainsAdvancedHistory(string kind)
    {
        using var layout = new MigrationRepositoryFixture();
        layout.AdvanceRemote(writer =>
        {
            var path = kind == "marker" ? MigrationPreflight.FormatRelativePath
                : kind == "config" ? AecApplication.ConfigSourceRelativePath : AecApplication.SourceRelativePath;
            File.WriteAllText(Path.Combine(writer, path), kind == "marker" ? "2.0\n" : "Invalid managed content.\n");
        });
        var (preflight, fetched, harnesses) = layout.Prepare();
        var runtime = layout.RuntimeBytes();

        var error = Assert.Throws<InvalidOperationException>(() => MigrationFastForward.Execute(preflight, fetched, harnesses));

        Assert.Contains("retrieved history is retained", error.Message, StringComparison.Ordinal);
        Assert.NotNull(error.InnerException);
        Assert.Equal(fetched.Remote.AdvertisedCommit, layout.Head());
        Assert.Empty(layout.Git("status", "--porcelain"));
        Assert.Equal(0, GitProcess.Run(layout.Repository, "merge-base", "--is-ancestor", preflight.Commit, layout.Head()).ExitCode);
        layout.AssertRuntime(runtime);
    }

    [Theory]
    [InlineData("filter.fixture.clean")]
    [InlineData("filter.fixture.smudge")]
    [InlineData("filter.fixture.process")]
    [InlineData("core.fsmonitor")]
    public void ExternalCheckoutCommandsAreRefusedBeforeTheyCanRun(string key)
    {
        using var layout = new MigrationRepositoryFixture();
        layout.AdvanceRemote();
        var (preflight, fetched, harnesses) = layout.Prepare();
        var sentinel = Path.Combine(layout.Root, "external-command-ran");
        layout.Git("config", key, $"touch '{sentinel}'");
        File.WriteAllText(Path.Combine(layout.Repository, ".git", "info", "attributes"), "* filter=fixture\n");

        layout.AssertNoChanges(() => Assert.Throws<InvalidOperationException>(() =>
            MigrationFastForward.Execute(preflight, fetched, harnesses)));
        Assert.False(File.Exists(sentinel));
    }

    [Fact]
    public void UnusedConfiguredFilterDoesNotBlockMigrationFastForward()
    {
        using var layout = new MigrationRepositoryFixture();
        var unrelated = Path.Combine(layout.Repository, AecApplication.ConfigSourceRelativePath);
        var originalBytes = File.ReadAllBytes(unrelated);
        var notes = Path.Combine(layout.Repository, "notes.md");
        var expectedNotes = File.ReadAllBytes(notes).Concat(Encoding.UTF8.GetBytes("Remote revision.\n")).ToArray();
        layout.AdvanceRemote();
        var (preflight, fetched, harnesses) = layout.Prepare();
        var sentinel = Path.Combine(layout.Root, "filter-ran");
        layout.Git("config", "filter.fixture.clean", $"touch '{sentinel}'");
        MigrationPreflight result;
        using (GitProcess.SuppressMigrationFilters())
            result = MigrationFastForward.Execute(preflight, fetched, harnesses);

        Assert.Equal(fetched.Remote.AdvertisedCommit, result.Commit);
        Assert.Equal(originalBytes, File.ReadAllBytes(unrelated));
        Assert.Equal(expectedNotes, File.ReadAllBytes(notes));
        Assert.False(File.Exists(sentinel));
        Assert.Contains("touch", layout.Git("config", "filter.fixture.clean"));
    }

    [Theory]
    [InlineData("--assume-unchanged")]
    [InlineData("--skip-worktree")]
    public void FastForwardDoesNotDiscardLocallyEditedFlaggedFiles(string flag)
    {
        using var layout = new MigrationRepositoryFixture();
        layout.AdvanceRemote();
        var (preflight, fetched, harnesses) = layout.Prepare();
        layout.Git("update-index", flag, "notes.md");
        var path = Path.Combine(layout.Repository, "notes.md");
        const string personalContent = "Local content must survive.\n";
        File.WriteAllText(path, personalContent);
        using var scope = GitProcess.SuppressMigrationFilters();

        var failure = Record.Exception(() => MigrationFastForward.Execute(preflight, fetched, harnesses));

        Assert.True(failure is null or InvalidOperationException, failure?.ToString());
        Assert.Equal(personalContent, File.ReadAllText(path));
    }

    [Fact]
    public void IncomingSubmoduleIsRejectedBeforeFastForward()
    {
        using var layout = new MigrationRepositoryFixture();
        layout.AdvanceRemote(writer =>
        {
            var submodule = Path.Combine(writer, "nested");
            Directory.CreateDirectory(submodule);
            GitProcess.RunRequired(submodule, "Submodule init failed", "init", "--quiet");
            GitProcess.RunRequired(submodule, "Submodule name failed", "config", "user.name", "Fixture");
            GitProcess.RunRequired(submodule, "Submodule email failed", "config", "user.email", "fixture@example.invalid");
            GitProcess.RunRequired(submodule, "Submodule commit failed", "commit", "--quiet", "--allow-empty", "-m", "Fixture");
        });
        var (preflight, fetched, harnesses) = layout.Prepare();
        using var scope = GitProcess.SuppressMigrationFilters();

        var error = Assert.Throws<InvalidOperationException>(() =>
            MigrationFastForward.Execute(preflight, fetched, harnesses));

        Assert.Contains("submodules", error.Message);
        Assert.Equal(preflight.Commit, layout.Head());
    }

    [Fact]
    public void SubmoduleInIndexIsRejectedBeforeStatus()
    {
        using var layout = new MigrationRepositoryFixture();
        var commit = layout.Head();
        layout.Git("update-index", "--add", "--cacheinfo", $"160000,{commit},submodule");
        using var scope = GitProcess.SuppressMigrationFilters();

        var error = Assert.Throws<InvalidOperationException>(() =>
            MigrationFastForward.EnsureSafeGitConfiguration(layout.Repository));

        Assert.Contains("submodules", error.Message);
    }

    [Fact]
    public void IncomingFilterCannotRunDuringFastForward()
    {
        using var layout = new MigrationRepositoryFixture();
        layout.AdvanceRemote(writer =>
        {
            File.WriteAllText(Path.Combine(writer, ".gitattributes"), "incoming.txt filter=fixture\n");
            File.WriteAllText(Path.Combine(writer, "incoming.txt"), "incoming content\n");
        });
        var (preflight, fetched, harnesses) = layout.Prepare();
        var sentinel = Path.Combine(layout.Root, "filter-ran");
        layout.Git("config", "filter.fixture.smudge", $"touch '{sentinel}'");
        using var scope = GitProcess.SuppressMigrationFilters();

        Assert.Throws<InvalidOperationException>(() => MigrationFastForward.Execute(preflight, fetched, harnesses));
        Assert.False(File.Exists(sentinel));
        Assert.Equal(preflight.Commit, layout.Head());
    }

    [Fact]
    [Trait("Category", "MigrationReview")]
    public void SelectedFilterFailsWithoutRunningItsCommand()
    {
        using var layout = new MigrationRepositoryFixture();
        var sentinel = Path.Combine(layout.Root, "filter-ran");
        layout.Git("config", "filter.Fixture.clean", $"touch '{sentinel}'");
        layout.Git("config", "filter.fixture.clean", $"touch '{sentinel}'");
        const string generated = "environment/shared/instructions.md";
        File.WriteAllText(Path.Combine(layout.Repository, ".git", "info", "attributes"), $"{generated} filter=fixture\n");
        var path = Path.Combine(layout.Repository, generated);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "content");
        using var scope = GitProcess.SuppressMigrationFilters();

        var result = GitProcess.Run(layout.Repository, "add", "--", generated);

        Assert.NotEqual(0, result.ExitCode);
        Assert.False(File.Exists(sentinel));
        Assert.Empty(layout.Git("diff", "--cached", "--name-only"));
    }

    [Fact]
    public void HooksAndBranchMergeDefaultsCannotChangeTheDirection()
    {
        using var layout = new MigrationRepositoryFixture();
        layout.AdvanceRemote();
        var (preflight, fetched, harnesses) = layout.Prepare();
        var hooks = Path.Combine(layout.Root, "custom-hooks");
        Directory.CreateDirectory(hooks);
        var sentinel = Path.Combine(layout.Root, "hook-ran");
        var hook = Path.Combine(hooks, "post-merge");
        File.WriteAllText(hook, $"#!/bin/sh\ntouch '{sentinel}'\n");
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(hook,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        layout.Git("config", "core.hooksPath", hooks);
        layout.Git("config", "merge.autostash", "true");
        layout.Git("config", "branch.main.mergeOptions", "--squash");
        var runtime = layout.RuntimeBytes();

        var result = MigrationFastForward.Execute(preflight, fetched, harnesses);

        Assert.Equal(fetched.Remote.AdvertisedCommit, result.Commit);
        Assert.False(File.Exists(sentinel));
        Assert.Equal(hooks, layout.Git("config", "core.hooksPath").Trim());
        Assert.Equal("--squash", layout.Git("config", "branch.main.mergeOptions").Trim());
        Assert.Empty(layout.Git("status", "--porcelain"));
        Assert.Empty(layout.Git("stash", "list"));
        layout.AssertRuntime(runtime);
    }

}
