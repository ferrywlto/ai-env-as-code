namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class MigrationFetchPreparationTests
{
    [Theory]
    [InlineData("Equal")]
    [InlineData("Behind")]
    [InlineData("Ahead")]
    public void ClassifiesFetchedHistoryWithoutAdvancingTheCheckout(string expected)
    {
        using var layout = new FetchLayout();
        if (expected == "Behind") layout.AdvanceRemote("refs/heads/trunk", layout.Head());
        if (expected == "Ahead") layout.Git("commit", "--quiet", "--allow-empty", "-m", "Local-only work");
        var preflight = layout.Snapshot();
        var remote = MigrationRemotePreflight.Read(preflight);
        layout.WriteDraftAndStageIt();

        layout.AssertOnlyLocalObjectsMayChange(() =>
        {
            var result = MigrationFetchPreparation.Read(preflight, remote);

            Assert.Equal(expected, result.Ancestry.ToString());
            Assert.Equal(remote, result.Remote);
            Assert.Equal(preflight.Commit, layout.Head());
            Assert.Equal("commit", layout.Git("--no-replace-objects", "cat-file", "-t", remote.AdvertisedCommit).Trim());
        });
    }

    [Fact]
    public void DivergenceStopsButReceivedObjectsMayRemain()
    {
        using var layout = new FetchLayout();
        var original = layout.Head();
        var tip = layout.AdvanceRemote("refs/heads/trunk", original);
        layout.Git("commit", "--quiet", "--allow-empty", "-m", "Separate local work");
        var preflight = layout.Snapshot();
        var remote = MigrationRemotePreflight.Read(preflight);

        layout.AssertOnlyLocalObjectsMayChange(() =>
        {
            var error = Assert.Throws<InvalidOperationException>(() => MigrationFetchPreparation.Read(preflight, remote));
            Assert.Contains("history diverged", error.Message, StringComparison.Ordinal);
            Assert.Equal("commit", layout.Git("cat-file", "-t", tip).Trim());
        });
    }

    [Theory]
    [InlineData("tip")]
    [InlineData("url")]
    [InlineData("remote-name")]
    [InlineData("default")]
    [InlineData("branch")]
    [InlineData("head")]
    public void ChangedPreflightInputsStopBeforeFetching(string change)
    {
        using var layout = new FetchLayout();
        var preflight = layout.Snapshot();
        var remote = MigrationRemotePreflight.Read(preflight);
        switch (change)
        {
            case "tip": layout.AdvanceRemote("refs/heads/trunk", layout.Head()); break;
            case "url":
                var other = Path.Combine(layout.Root, "same history different remote.git");
                layout.Git("clone", "--quiet", "--bare", "--template=", layout.Remote, other);
                layout.Git("config", "remote.shared.url", other);
                break;
            case "remote-name":
                layout.Git("remote", "add", "another", layout.Remote);
                layout.Git("config", "branch.trunk.remote", "another");
                break;
            case "default":
                layout.RemoteGit("update-ref", "refs/heads/other", layout.Head());
                layout.RemoteGit("symbolic-ref", "HEAD", "refs/heads/other");
                break;
            case "branch": layout.Git("switch", "--quiet", "-c", "review"); break;
            default: layout.Git("commit", "--quiet", "--allow-empty", "-m", "Changed checkpoint"); break;
        }

        layout.AssertOnlyLocalObjectsMayChange(() => Assert.Throws<InvalidOperationException>(() =>
            MigrationFetchPreparation.Read(preflight, remote)));
    }

    [Fact]
    public void OnlyDefaultTipObjectsArriveDespiteWildcardFetchAndTagConfiguration()
    {
        using var layout = new FetchLayout();
        var original = layout.Head();
        var tip = layout.AdvanceRemote("refs/heads/trunk", original);
        var unrelated = layout.AdvanceRemote("refs/heads/unrelated", original);
        layout.RemoteGit("tag", "-a", "-m", "Unrelated tag", "extra", unrelated);
        var tag = layout.RemoteGit("rev-parse", "refs/tags/extra").Trim();
        // A configured mirror must not redirect fetch updates into local branches.
        layout.Git("config", "remote.shared.fetch", "+refs/heads/*:refs/heads/*");
        layout.Git("config", "remote.shared.tagOpt", "--tags");
        var fetchHead = Path.Combine(layout.Repository, ".git", "FETCH_HEAD");
        File.WriteAllText(fetchHead, "Existing fetch history must remain.\n");
        var preflight = layout.Snapshot();
        var remote = MigrationRemotePreflight.Read(preflight);

        layout.AssertOnlyLocalObjectsMayChange(() =>
        {
            Assert.Equal(MigrationAncestry.Behind, MigrationFetchPreparation.Read(preflight, remote).Ancestry);
            Assert.Equal("commit", layout.Git("cat-file", "-t", tip).Trim());
            Assert.NotEqual(0, GitProcess.Run(layout.Repository, "cat-file", "-e", unrelated).ExitCode);
            Assert.NotEqual(0, GitProcess.Run(layout.Repository, "cat-file", "-e", tag).ExitCode);
            Assert.Equal(original, layout.Git("rev-parse", "refs/remotes/shared/trunk").Trim());
        });
    }

    [Fact]
    public void ReplacementRefsCannotDisguiseDivergentHistory()
    {
        using var layout = new FetchLayout();
        var tip = layout.AdvanceRemote("refs/heads/trunk", layout.Head());
        layout.Git("commit", "--quiet", "--allow-empty", "-m", "Local divergent work");
        Assert.Equal(0, GitProcess.FetchRemoteCommit(layout.Repository, "shared", tip).ExitCode);
        var tree = layout.Git("rev-parse", "HEAD^{tree}").Trim();
        var substitute = layout.Git("commit-tree", tree, "-p", layout.Head(), "-m", "Forged ancestry").Trim();
        layout.Git("replace", tip, substitute);
        Assert.Equal(0, GitProcess.Run(layout.Repository, "merge-base", "--is-ancestor", layout.Head(), tip).ExitCode);
        var preflight = layout.Snapshot();
        var remote = MigrationRemotePreflight.Read(preflight);

        layout.AssertOnlyLocalObjectsMayChange(() =>
        {
            var error = Assert.Throws<InvalidOperationException>(() => MigrationFetchPreparation.Read(preflight, remote));
            Assert.Contains("history diverged", error.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void FailedFetchLeavesCheckoutAndMetadataUntouched()
    {
        using var layout = new FetchLayout();
        var tip = layout.AdvanceRemote("refs/heads/trunk", layout.Head());
        // An already-local object can bypass transfer, so it cannot prove a
        // missing remote fails. Require a remote-only commit before the fetch.
        Assert.NotEqual(0, GitProcess.Run(layout.Repository, "--no-replace-objects", "cat-file", "-e", tip).ExitCode);
        layout.Git("remote", "add", "missing", Path.Combine(layout.Root, "absent.git"));

        layout.AssertOnlyLocalObjectsMayChange(() =>
            Assert.NotEqual(0, GitProcess.FetchRemoteCommit(layout.Repository, "missing", tip).ExitCode));
    }

    [Fact]
    public void FetchDeadlineIsBoundedWithoutAdvancingAnything()
    {
        using var layout = new FetchLayout();

        layout.AssertOnlyLocalObjectsMayChange(() =>
        {
            var error = Assert.Throws<InvalidOperationException>(() =>
                GitProcess.FetchRemoteCommit(layout.Repository, "shared", layout.Head(), TimeSpan.FromTicks(1)));
            Assert.Contains("timed out", error.Message, StringComparison.Ordinal);
        });
    }

    private sealed class FetchLayout : IDisposable
    {
        public FetchLayout()
        {
            Root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(),
                "aec-migration-fetch-tests", Guid.NewGuid().ToString("N"));
            Repository = Path.Combine(Root, "data");
            Remote = Path.Combine(Root, "shared remote.git");
            Directory.CreateDirectory(Repository);
            Git("init", "--quiet", "--template=", "--initial-branch=trunk");
            File.WriteAllText(Path.Combine(Repository, "instructions.md"), "Canonical fixture.\n");
            Git("add", "--", "instructions.md");
            Git("commit", "--quiet", "-m", "Fetch preparation fixture");
            Git("init", "--bare", "--quiet", "--template=", "--initial-branch=trunk", Remote);
            Git("remote", "add", "shared", Remote);
            Git("push", "--quiet", "--set-upstream", "shared", "trunk");
            var home = Path.Combine(Root, "untouched-runtime");
            Directory.CreateDirectory(home);
            File.WriteAllText(Path.Combine(home, "AGENTS.md"), "Runtime must remain untouched.\n");
        }

        public string Root { get; }
        public string Repository { get; }
        public string Remote { get; }
        public string Git(params string[] args) => GitProcess.RunRequired(Repository, "Fetch fixture Git failed", args).Output;
        public string RemoteGit(params string[] args) => GitProcess.RunRequired(Remote, "Bare fetch fixture Git failed", args).Output;
        public string Head() => Git("--no-replace-objects", "rev-parse", "HEAD").Trim();
        public MigrationPreflight Snapshot() => new(Repository, Head(),
            MigrationPreflight.LegacyFormat, MigrationPreflight.SectionedFormat);

        public string AdvanceRemote(string branch, string parent)
        {
            var tree = RemoteGit("rev-parse", parent + "^{tree}").Trim();
            var commit = RemoteGit("commit-tree", tree, "-p", parent, "-m", "Remote revision: " + branch).Trim();
            RemoteGit("update-ref", branch, commit);
            return commit;
        }

        public void WriteDraftAndStageIt()
        {
            var path = Path.Combine(Repository, "environment", "shared", "instructions.md");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "Staged user draft.\n");
            Git("add", "--", "environment/shared/instructions.md");
            File.WriteAllText(path, "Staged plus unstaged user draft.\n");
        }

        public void AssertOnlyLocalObjectsMayChange(Action action)
        {
            var objects = Path.Combine(Repository, ".git", "objects");
            bool Protected(string path) => path != objects && !path.StartsWith(objects + Path.DirectorySeparatorChar, StringComparison.Ordinal);
            var files = Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
                .Where(Protected).ToDictionary(path => path, File.ReadAllBytes);
            var directories = Directory.GetDirectories(Root, "*", SearchOption.AllDirectories)
                .Where(Protected).Order(StringComparer.Ordinal).ToArray();

            action();

            Assert.Equal(files.Keys.Order(StringComparer.Ordinal), Directory.EnumerateFiles(Root,
                "*", SearchOption.AllDirectories).Where(Protected).Order(StringComparer.Ordinal));
            Assert.Equal(directories, Directory.GetDirectories(Root, "*", SearchOption.AllDirectories)
                .Where(Protected).Order(StringComparer.Ordinal));
            foreach (var (path, bytes) in files) Assert.Equal(bytes, File.ReadAllBytes(path));
        }

        public void Dispose() => TestDirectoryCleanup.Delete(Root);
    }
}
