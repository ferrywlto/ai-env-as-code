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
