namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class MigrationRemotePreflightTests
{
    [Fact]
    public void UsesConfiguredRemoteAndAdvertisedDefaultRatherThanOriginOrMain()
    {
        using var layout = new RemoteLayout();
        layout.Git("remote", "add", "unrelated", Path.Combine(layout.Root, "never-contacted.git"));

        layout.AssertNoChanges(() =>
        {
            var result = MigrationRemotePreflight.Read(layout.Preflight);

            Assert.Equal("shared", result.Remote);
            Assert.Equal("refs/heads/trunk", result.BranchRef);
            Assert.Equal(layout.Head(), result.AdvertisedCommit);
        });
    }

    [Fact]
    public void AdvertisedTipDoesNotFetchOrTrustStaleLocalTrackingRefs()
    {
        using var layout = new RemoteLayout();
        var original = layout.Head();
        var tree = layout.RemoteGit("rev-parse", "HEAD^{tree}").Trim();
        var newer = layout.RemoteGit("commit-tree", tree, "-p", original, "-m", "Remote-only revision").Trim();
        layout.RemoteGit("update-ref", "refs/heads/trunk", newer);

        layout.AssertNoChanges(() =>
        {
            var result = MigrationRemotePreflight.Read(layout.Preflight);

            Assert.Equal(newer, result.AdvertisedCommit);
            Assert.Equal(original, layout.Git("rev-parse", "refs/remotes/shared/trunk").Trim());
            Assert.NotEqual(0, GitProcess.Run(layout.Repository, "cat-file", "-e", newer + "^{commit}").ExitCode);
            Assert.False(File.Exists(Path.Combine(layout.Repository, ".git", "FETCH_HEAD")));
        });
    }

    [Theory]
    [InlineData("remote", false)]
    [InlineData("merge", false)]
    [InlineData("url", false)]
    [InlineData("remote", true)]
    [InlineData("merge", true)]
    [InlineData("url", true)]
    public void MissingOrAmbiguousConfigurationStops(string setting, bool duplicate)
    {
        using var layout = new RemoteLayout();
        var key = setting == "url" ? "remote.shared.url" : $"branch.trunk.{setting}";
        var value = setting switch { "url" => layout.Remote, "remote" => "shared", _ => "refs/heads/trunk" };
        if (duplicate) layout.Git("config", "--add", key, value);
        else layout.Git("config", "--unset-all", key);

        layout.AssertNoChanges(() =>
        {
            var error = Assert.Throws<InvalidOperationException>(() => MigrationRemotePreflight.Read(layout.Preflight));
            Assert.Contains("exactly one nonempty configured value", error.Message, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData(".")]
    [InlineData("-invalid")]
    [InlineData("not-configured")]
    public void InvalidOrUndefinedRemoteCannotBeGuessedAsARepositoryPath(string remote)
    {
        using var layout = new RemoteLayout();
        layout.Git("config", "branch.trunk.remote", remote);

        layout.AssertNoChanges(() => Assert.Throws<InvalidOperationException>(() =>
            MigrationRemotePreflight.Read(layout.Preflight)));
    }

    [Fact]
    public void NonDefaultBranchWithItsOwnUpstreamStopsWithoutSwitching()
    {
        using var layout = new RemoteLayout();
        layout.Git("switch", "--quiet", "-c", "feature");
        layout.Git("config", "branch.feature.remote", "shared");
        layout.Git("config", "branch.feature.merge", "refs/heads/feature");

        layout.AssertNoChanges(() =>
        {
            var error = Assert.Throws<InvalidOperationException>(() => MigrationRemotePreflight.Read(layout.Preflight));
            Assert.Contains("remote default-branch checkout", error.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void LocalAliasOfTheDefaultBranchAlsoRequiresTheDefaultCheckout()
    {
        using var layout = new RemoteLayout();
        layout.Git("switch", "--quiet", "-c", "review");
        layout.Git("config", "branch.review.remote", "shared");
        layout.Git("config", "branch.review.merge", "refs/heads/trunk");

        layout.AssertNoChanges(() =>
        {
            var error = Assert.Throws<InvalidOperationException>(() => MigrationRemotePreflight.Read(layout.Preflight));
            Assert.Contains("Current branch and configured upstream branch differ", error.Message, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DetachedHeadOrChangedCommitStopsBeforeRemoteQuery(bool changedCommit)
    {
        using var layout = new RemoteLayout();
        if (changedCommit) layout.Git("commit", "--quiet", "--allow-empty", "-m", "Changed local checkpoint");
        else layout.Git("switch", "--quiet", "--detach");

        layout.AssertNoChanges(() =>
        {
            var error = Assert.Throws<InvalidOperationException>(() => MigrationRemotePreflight.Read(layout.Preflight));
            Assert.Contains(changedCommit ? "HEAD or branch changed" : "detached HEAD", error.Message, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnresolvedOrDetachedRemoteHeadStops(bool detached)
    {
        using var layout = new RemoteLayout();
        if (detached) layout.RemoteGit("update-ref", "--no-deref", "HEAD", layout.Head());
        else layout.RemoteGit("symbolic-ref", "HEAD", "refs/heads/missing");

        layout.AssertNoChanges(() => Assert.Throws<InvalidDataException>(() =>
            MigrationRemotePreflight.Read(layout.Preflight)));
    }

    [Fact]
    public void EmptyRemoteDoesNotInventADefaultTip()
    {
        using var layout = new RemoteLayout();
        var empty = Path.Combine(layout.Root, "empty.git");
        layout.Git("init", "--bare", "--quiet", "--template=", "--initial-branch=trunk", empty);
        layout.Git("config", "remote.shared.url", empty);

        layout.AssertNoChanges(() => Assert.Throws<InvalidDataException>(() =>
            MigrationRemotePreflight.Read(layout.Preflight)));
    }

    [Fact]
    public void UnreachableRemoteStopsWithoutEchoingTransportSecrets()
    {
        using var layout = new RemoteLayout();
        layout.Git("config", "remote.shared.url", Path.Combine(layout.Root, "private-token-value", "absent.git"));

        layout.AssertNoChanges(() =>
        {
            var error = Assert.Throws<InvalidOperationException>(() => MigrationRemotePreflight.Read(layout.Preflight));
            Assert.Contains("could not advertise HEAD", error.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("private-token-value", error.Message, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData(40)]
    [InlineData(64)]
    public void ParsesSha1AndSha256Advertisements(int length)
    {
        var oid = new string('a', length);
        var result = MigrationRemotePreflight.ParseAdvertisement($"ref: refs/heads/trunk\tHEAD\n{oid}\tHEAD\n");

        Assert.Equal("refs/heads/trunk", result.BranchRef);
        Assert.Equal(oid, result.Commit);
    }

    [Fact]
    public void ExpiredQueryDeadlineStopsWithoutFetchingOrLeavingTheFixtureDirty()
    {
        using var layout = new RemoteLayout();

        layout.AssertNoChanges(() =>
        {
            // Count startup against the deadline so this deterministic tiny budget
            // tests cleanup using a local bare remote, not a hanging network server.
            var error = Assert.Throws<InvalidOperationException>(() =>
                GitProcess.ReadRemoteHead(layout.Repository, "shared", TimeSpan.FromTicks(1)));
            Assert.Contains("timed out", error.Message, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("missing-tip")]
    [InlineData("missing-ref")]
    [InlineData("duplicate-ref")]
    [InlineData("duplicate-tip")]
    [InlineData("bad-id")]
    [InlineData("zero-id")]
    [InlineData("short-id")]
    [InlineData("non-branch")]
    [InlineData("blank-branch")]
    [InlineData("bad-fields")]
    [InlineData("extra-ref")]
    public void InvalidOrAmbiguousAdvertisementsStop(string kind)
    {
        var header = "ref: refs/heads/trunk\tHEAD\n";
        var tip = new string('a', 40) + "\tHEAD\n";
        var advertisement = kind switch
        {
            "empty" => "",
            "missing-tip" => header,
            "missing-ref" => tip,
            "duplicate-ref" => header + header + tip,
            "duplicate-tip" => header + tip + tip,
            "bad-id" => header + new string('z', 40) + "\tHEAD\n",
            "zero-id" => header + new string('0', 40) + "\tHEAD\n",
            "short-id" => header + "abc\tHEAD\n",
            "non-branch" => "ref: refs/tags/release\tHEAD\n" + tip,
            "blank-branch" => "ref: refs/heads/\tHEAD\n" + tip,
            "bad-fields" => header + tip + "unexpected record\n",
            _ => header + tip + new string('b', 40) + "\trefs/heads/other\n"
        };

        Assert.Throws<InvalidDataException>(() => MigrationRemotePreflight.ParseAdvertisement(advertisement));
    }

    private sealed class RemoteLayout : IDisposable
    {
        public RemoteLayout()
        {
            Root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(),
                "aec-migration-remote-tests", Guid.NewGuid().ToString("N"));
            Repository = Path.Combine(Root, "data");
            Remote = Path.Combine(Root, "shared remote.git");
            Directory.CreateDirectory(Repository);
            Git("init", "--quiet", "--template=", "--initial-branch=trunk");
            File.WriteAllText(Path.Combine(Repository, "instructions.md"), "Canonical fixture.\n");
            Git("add", "--", "instructions.md");
            Git("commit", "--quiet", "-m", "Remote preflight fixture");
            Git("init", "--bare", "--quiet", "--template=", "--initial-branch=trunk", Remote);
            Git("remote", "add", "shared", Remote);
            Git("push", "--quiet", "--set-upstream", "shared", "trunk");
            // Isolate transport selection from the separately tested format/history
            // validation. All setup and remote operations stay inside this fixture.
            Preflight = new MigrationPreflight(Repository, Head(),
                MigrationPreflight.LegacyFormat, MigrationPreflight.SectionedFormat);
        }

        public string Root { get; }
        public string Repository { get; }
        public string Remote { get; }
        public MigrationPreflight Preflight { get; }
        public string Git(params string[] args) => GitProcess.RunRequired(Repository, "Remote fixture Git failed", args).Output;
        public string RemoteGit(params string[] args) => GitProcess.RunRequired(Remote, "Bare fixture Git failed", args).Output;
        public string Head() => Git("rev-parse", "HEAD").Trim();

        public void AssertNoChanges(Action inspect)
        {
            // Include both repositories' metadata and raw index bytes: ls-remote
            // must not fetch objects, create FETCH_HEAD, or alter tracking refs.
            var files = Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
                .ToDictionary(path => path, File.ReadAllBytes);
            var directories = Directory.GetDirectories(Root, "*", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal).ToArray();

            inspect();

            Assert.Equal(files.Keys.Order(StringComparer.Ordinal),
                Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
            Assert.Equal(directories, Directory.GetDirectories(Root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
            foreach (var (path, bytes) in files) Assert.Equal(bytes, File.ReadAllBytes(path));
        }

        public void Dispose() => TestDirectoryCleanup.Delete(Root);
    }
}
