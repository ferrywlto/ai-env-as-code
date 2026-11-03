using System.Text;
using Layout = Aec.Tests.MigrationPortableCapturePreviewTests.Layout;

namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class MigrationPostDoneCoordinatorTests
{
    private const string Shared = "environment/shared/instructions.md";
    private const string Marker = MigrationCompletionPreview.MarkerRelativePath;
    private static readonly string[] MarkerPaths = [Marker];

    [Theory]
    [InlineData("both", 3)]
    [InlineData("codex", 2)]
    public void CommitsSourcesTargetsThenMarkerAndAppliesEnrolledProviders(string selected, int count)
    {
        using var layout = selected == "both" ? new Layout("codex", "copilot") : new Layout(selected);
        if (selected != "both") File.Delete(layout.Runtime("copilot"));
        var original = layout.Files.Head();
        layout.Files.Write(Shared, "Changed approval.\n");
        var preparation = layout.Context(original);
        var before = Capture(layout, excludeGit: true);
        var remote = layout.Files.Git("ls-remote", "shared", "refs/heads/main");

        var result = MigrationPostDoneCoordinator.Complete(preparation);

        Assert.Equal(count, result.DataCommits.Count);
        var parent = original;
        foreach (var commit in result.DataCommits.Append(result.Commit))
        {
            Assert.Equal($"{commit} {parent}", layout.Files.Git("--no-replace-objects", "rev-list", "--parents", "-n", "1", commit).Trim());
            parent = commit;
        }
        Assert.Equal(Shared, Assert.Single(ChangedPaths(layout, original, result.DataCommits[0])));
        for (var index = 0; index < result.Providers.Count; index++)
        {
            var provider = result.Providers[index].Provider;
            Assert.Equal(layout.Target(provider), Assert.Single(ChangedPaths(layout, result.DataCommits[index], result.DataCommits[index + 1])));
            var harness = preparation.Harnesses.Single(item => item.Provider == provider);
            Assert.True(PortableEnvironmentStatus.Inspect(layout.Repository, provider, harness.Home).InSync);
            Assert.Equal("applied", result.Providers[index].Result);
        }
        Assert.Equal(MarkerPaths, ChangedPaths(layout, result.DataCommits[^1], result.Commit));
        Assert.Equal(preparation.Harnesses.Select(item => item.Provider), result.Providers.Select(item => item.Provider));
        Assert.Equal(result.Commit, layout.Files.Head());
        Assert.Empty(layout.Files.Git("status", "--porcelain"));
        Assert.Equal(remote, layout.Files.Git("ls-remote", "shared", "refs/heads/main"));
        var allowed = preparation.Harnesses.SelectMany(harness => new[]
        {
            harness.RuntimePath, Path.Combine(layout.Repository, layout.Target(harness.Provider))
        }).Append(Path.Combine(layout.Repository, Marker)).ToHashSet(StringComparer.Ordinal);
        var after = Capture(layout, excludeGit: true);
        Assert.Equal(before.Files.Keys.Append(Path.Combine(layout.Repository, Marker)).Order(StringComparer.Ordinal), after.Files.Keys.Order(StringComparer.Ordinal));
        foreach (var (path, bytes) in before.Files)
            if (!allowed.Contains(path)) Assert.Equal(bytes, after.Files[path]);
        Assert.Equal(before.Directories, after.Directories);
    }

    [Fact]
    public void FirstLegacyMigrationCreatesSourcesAndTargetsBeforeCompleting()
    {
        using var layout = new Layout();
        var original = layout.Files.Head();
        var canonical = new[] { AecApplication.SourceRelativePath, AecApplication.ConfigSourceRelativePath,
            AecApplication.CopilotSourceRelativePath }.ToDictionary(path => path,
                path => File.ReadAllBytes(Path.Combine(layout.Repository, path)), StringComparer.Ordinal);
        var runtimeConfig = File.ReadAllBytes(layout.ConfigRuntime);
        var remote = layout.Files.Git("ls-remote", "shared", "refs/heads/main");
        Assert.False(Directory.Exists(Path.Combine(layout.Repository, "environment", "targets")));
        var scaffold = MigrationScaffoldCoordinator.CaptureAndScaffold(layout.Context(original));
        Assert.Equal(4, scaffold.WrittenPaths.Count);
        var sections = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [Shared] = "Shared approval.\n",
            [$"environment/platforms/{layout.Platform}/policy.md"] = "Local paths.\n",
            ["environment/providers/codex/overlay.md"] = "Codex mechanics.\n",
            ["environment/providers/copilot/overlay.md"] = "Copilot mechanics.\n"
        };
        // These untracked files model the user's filled scaffold at DONE;
        // neither a section commit nor a portable target exists yet.
        foreach (var (path, content) in sections) layout.Files.Write(path, content);
        var preparation = scaffold.Preparation;
        using var input = new StringReader("done\nDONE\n");
        using var output = new StringWriter();
        var result = Assert.IsType<MigrationPostDoneResult>(
            MigrationPostDoneCoordinator.WaitAndComplete(scaffold, input, output));
        Assert.Contains("Not confirmed", output.ToString());

        Assert.Equal(3, result.DataCommits.Count);
        Assert.Equal(sections.Keys.Order(StringComparer.Ordinal), ChangedPaths(layout, original, result.DataCommits[0]));
        var parent = original;
        foreach (var commit in result.DataCommits.Append(result.Commit))
        {
            Assert.Equal($"{commit} {parent}", layout.Files.Git("--no-replace-objects", "rev-list", "--parents", "-n", "1", commit).Trim());
            parent = commit;
        }
        foreach (var harness in preparation.Harnesses)
        {
            Assert.True(PortableEnvironmentStatus.Inspect(layout.Repository, harness.Provider, harness.Home).InSync);
            Assert.True(File.Exists(Path.Combine(layout.Repository, layout.Target(harness.Provider))));
        }
        Assert.All(result.Providers, item => Assert.Equal("applied", item.Result));
        Assert.Equal(MarkerPaths, ChangedPaths(layout, result.DataCommits[^1], result.Commit));
        foreach (var (path, bytes) in canonical)
            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(layout.Repository, path)));
        Assert.Equal(runtimeConfig, File.ReadAllBytes(layout.ConfigRuntime));
        Assert.Equal(Encoding.UTF8.GetBytes("2.0\n"), File.ReadAllBytes(Path.Combine(layout.Repository, Marker)));
        Assert.Equal(remote, layout.Files.Git("ls-remote", "shared", "refs/heads/main"));
        Assert.Empty(layout.Files.Git("status", "--porcelain"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EofOrCancellationRetainsScaffoldWithoutCompleting(bool cancel)
    {
        using var layout = new Layout();
        var scaffold = MigrationScaffoldCoordinator.CaptureAndScaffold(layout.Context());
        using var cancellation = new CancellationTokenSource();
        if (cancel) cancellation.Cancel();
        using var input = new StringReader(cancel ? "DONE\n" : "done\n");
        using var output = new StringWriter();

        layout.Files.AssertNoChanges(() => Assert.Null(
            MigrationPostDoneCoordinator.WaitAndComplete(scaffold, input, output, cancellation.Token)));
        Assert.NotEmpty(scaffold.WrittenPaths);
    }

    [Fact]
    public void NoOpDataPhasesOnlyCommitMarker()
    {
        using var layout = new Layout("codex", "copilot");
        var original = layout.Files.Head();
        var result = MigrationPostDoneCoordinator.Complete(layout.Context(original));
        Assert.Empty(result.DataCommits);
        Assert.All(result.Providers, item => Assert.Equal("unchanged", item.Result));
        Assert.Equal($"{result.Commit} {original}", layout.Files.Git("rev-list", "--parents", "-n", "1", result.Commit).Trim());
        Assert.Equal(MarkerPaths, ChangedPaths(layout, original, result.Commit));
        Assert.Equal(result.Commit, layout.Files.Head());
    }

    [Theory]
    [InlineData("sources", 1)]
    [InlineData("target:codex", 2)]
    [InlineData("target:copilot", 3)]
    [InlineData("apply", 3)]
    [InlineData("status", 3)]
    public void ComponentFailureStopsWithoutMarkerOrLaterWrites(string stop, int expectedCommits)
    {
        using var layout = new Layout("codex", "copilot");
        var original = layout.Files.Head();
        layout.Files.Write(Shared, "Changed approval.\n");
        var preparation = layout.Context(original);
        Snapshot? atFailure = null;
        string? head = null;
        var seen = new List<string>();
        var error = Assert.Throws<InvalidOperationException>(() => MigrationPostDoneCoordinator.Complete(preparation, phase =>
        {
            seen.Add(phase);
            if (phase != stop) return;
            head = layout.Files.Head();
            atFailure = Capture(layout);
            throw new InvalidOperationException("Injected component failure");
        }));
        Assert.NotNull(atFailure);
        AssertSnapshot(layout, atFailure);
        Assert.Equal(head, layout.Files.Head());
        Assert.Equal(expectedCommits.ToString(System.Globalization.CultureInfo.InvariantCulture),
            layout.Files.Git("rev-list", "--count", $"{original}..HEAD").Trim());
        Assert.False(File.Exists(Path.Combine(layout.Repository, Marker)));
        Assert.Equal(stop, seen[^1]);
        Assert.Contains("may remain", error.Message, StringComparison.Ordinal);
        Assert.Contains("No automatic rollback", error.Message, StringComparison.Ordinal);
        Assert.Contains(head!, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void UnexpectedHeadAfterNoOpIsNotAdopted()
    {
        using var layout = new Layout("codex", "copilot");
        var original = layout.Files.Head();
        var preparation = layout.Context(original);
        Snapshot? afterMutation = null;
        var error = Assert.Throws<InvalidOperationException>(() => MigrationPostDoneCoordinator.Complete(preparation, phase =>
        {
            Assert.Equal("sources", phase);
            layout.Files.Git("commit", "--allow-empty", "-m", "Unreviewed checkpoint");
            afterMutation = Capture(layout);
        }));
        Assert.NotNull(afterMutation);
        AssertSnapshot(layout, afterMutation);
        Assert.Contains(original, error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(layout.Repository, Marker)));
    }

    [Fact]
    public void InitialUnrelatedDirtStopsWithoutChanges()
    {
        using var layout = new Layout("codex", "copilot");
        var preparation = layout.Context();
        layout.Files.Write("notes.md", "Unrelated edit.\n");
        layout.Files.AssertNoChanges(() =>
            Assert.Throws<InvalidOperationException>(() => MigrationPostDoneCoordinator.Complete(preparation)));
    }

    [Fact]
    public void PartialApplyFailureRetainsFirstProviderAndStopsCompletion()
    {
        using var layout = new Layout("codex", "copilot");
        layout.Files.Write(Shared, "Changed approval.\n");
        var preparation = layout.Context();
        Snapshot? afterMutation = null;
        var error = Assert.Throws<InvalidOperationException>(() => MigrationPostDoneCoordinator.Complete(preparation,
            afterProviderForTests: provider =>
            {
                Assert.Equal("codex", provider);
                File.AppendAllText(layout.Runtime("copilot"), "External edit.\n");
                afterMutation = Capture(layout);
            }));
        Assert.NotNull(afterMutation);
        AssertSnapshot(layout, afterMutation);
        Assert.Contains("Changed approval.", File.ReadAllText(layout.Runtime("codex")), StringComparison.Ordinal);
        Assert.DoesNotContain("Changed approval.", File.ReadAllText(layout.Runtime("copilot")), StringComparison.Ordinal);
        Assert.Contains("during apply", error.Message, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(layout.Repository, Marker)));
    }

    [Fact]
    public void CompletionSigningFailureRetainsStagedMarkerAfterVerifiedApply()
    {
        using var layout = new Layout("codex", "copilot");
        layout.Files.Write(Shared, "Changed approval.\n");
        var preparation = layout.Context();
        string? dataHead = null;
        var error = Assert.Throws<InvalidOperationException>(() => MigrationPostDoneCoordinator.Complete(preparation, phase =>
        {
            if (phase != "status") return;
            dataHead = layout.Files.Head();
            layout.Files.Git("config", "gpg.format", "openpgp");
            layout.Files.Git("config", "gpg.program", Path.Combine(layout.Files.Root, "missing-signing-program"));
            layout.Files.Git("config", "commit.gpgSign", "true");
        }));
        Assert.NotNull(dataHead);
        Assert.Equal(dataHead, layout.Files.Head());
        Assert.Equal(MarkerPaths, layout.Files.Git("diff", "--cached", "--name-only").Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.Equal(Encoding.UTF8.GetBytes("2.0\n"), File.ReadAllBytes(Path.Combine(layout.Repository, Marker)));
        Assert.Empty(layout.Files.Git("ls-tree", dataHead, "--", Marker));
        Assert.Contains("during completion", error.Message, StringComparison.Ordinal);
    }

    private static string[] ChangedPaths(Layout layout, string parent, string commit) =>
        layout.Files.Git("diff-tree", "--no-commit-id", "--no-renames", "--name-only", "-r", parent, commit)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal).ToArray();

    private static Snapshot Capture(Layout layout, bool excludeGit = false)
    {
        var git = Path.Combine(layout.Repository, ".git");
        bool Include(string path) => !excludeGit ||
            (path != git && !path.StartsWith(git + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        return new Snapshot(Directory.EnumerateFiles(layout.Files.Root, "*", SearchOption.AllDirectories).Where(Include)
            .ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal),
            Directory.GetDirectories(layout.Files.Root, "*", SearchOption.AllDirectories).Where(Include)
                .Order(StringComparer.Ordinal).ToArray());
    }

    private static void AssertSnapshot(Layout layout, Snapshot expected)
    {
        var actual = Capture(layout);
        Assert.Equal(expected.Files.Keys.Order(StringComparer.Ordinal), actual.Files.Keys.Order(StringComparer.Ordinal));
        foreach (var (path, bytes) in expected.Files) Assert.Equal(bytes, actual.Files[path]);
        Assert.Equal(expected.Directories, actual.Directories);
    }

    private sealed record Snapshot(Dictionary<string, byte[]> Files, string[] Directories);
}
