using System.Text;
using Layout = Aec.Tests.MigrationPortableCapturePreviewTests.Layout;

namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class MigrationPortableCaptureCommitterTests
{
    private static readonly string[] ConfigOnlyPaths = [AecApplication.ConfigSourceRelativePath];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidatedNoopPreservesEveryFixtureAndGitByte(bool portable)
    {
        using var layout = portable ? new Layout("codex", "copilot") : new Layout();
        var reviewed = Preview(layout);
        layout.Files.AssertNoChanges(() => Assert.Null(MigrationPortableCaptureCommitter.WriteAndCommit(reviewed)));
    }

    [Theory]
    [InlineData("shared")]
    [InlineData("platform")]
    public void MatchingProposalsProduceOneCommitWithExactlyTheirConsumers(string section)
    {
        using var layout = new Layout("codex", "copilot");
        var targets = MigrationPortableTargetWriterTests.EnrollOtherPlatforms(layout, "codex", "copilot");
        var original = section == "shared" ? "Shared approval." : "Local paths.";
        layout.EditRuntime("codex", original, "Agreed revision.");
        layout.EditRuntime("copilot", original, "Agreed revision.");
        var source = section == "shared" ? "environment/shared/instructions.md" : $"environment/platforms/{layout.Platform}/policy.md";
        var expected = targets.Where(path => section == "shared" || path.Contains($"/{layout.Platform}/", StringComparison.Ordinal))
            .Append(source).ToArray();
        AssertCapture(layout, expected);
    }

    [Fact]
    public void SeparateOverlaysAndManagedConfigShareOneCommit()
    {
        using var layout = new Layout("codex", "copilot");
        layout.EditRuntime("codex", "Provider mechanics.", "Codex revision.");
        layout.EditRuntime("copilot", "Provider mechanics.", "Copilot revision.");
        File.WriteAllText(layout.ConfigRuntime, "personality = 'pragmatic'\nmodel = 'private-machine-model'\n");
        AssertCapture(layout, ["environment/providers/codex/overlay.md", "environment/providers/copilot/overlay.md",
            AecApplication.ConfigSourceRelativePath, layout.Target("codex"), layout.Target("copilot")]);
        Assert.Equal("personality = \"pragmatic\"\n", File.ReadAllText(Path.Combine(layout.Repository, AecApplication.ConfigSourceRelativePath)));
    }

    [Fact]
    public void CombinedPlatformAndProviderChangesCommitTheirUnionAcrossPlatforms()
    {
        using var layout = new Layout("codex", "copilot");
        var targets = MigrationPortableTargetWriterTests.EnrollOtherPlatforms(layout, "codex", "copilot");
        layout.EditRuntime("codex", "Local paths.", "Platform revision.");
        layout.EditRuntime("copilot", "Provider mechanics.", "Provider revision.");
        var expected = targets.Where(path => path.Contains($"/{layout.Platform}/", StringComparison.Ordinal) || path.Contains("/copilot/", StringComparison.Ordinal))
            .Concat([$"environment/platforms/{layout.Platform}/policy.md", "environment/providers/copilot/overlay.md"]).ToArray();
        AssertCapture(layout, expected);
        var combined = InstructionSections.Parse(File.ReadAllText(Path.Combine(layout.Repository, layout.Target("copilot"))), true, true);
        Assert.Equal("Platform revision.\n", combined.Platform);
        Assert.Equal("Provider revision.\n", combined.Provider);
    }

    [Fact]
    public void ConfigOnlyCaptureLeavesAllInstructionFilesUntouched()
    {
        using var layout = new Layout("codex", "copilot");
        File.WriteAllText(layout.ConfigRuntime, "personality = 'none'\nmodel = 'private'\n");
        AssertCapture(layout, ConfigOnlyPaths);
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("copilot")]
    public void MixedFormatsPreserveLegacyRuntimeAndUnconsumedAuthoredDraft(string provider)
    {
        using var layout = new Layout(provider);
        var legacy = provider == "codex" ? "copilot" : "codex";
        File.AppendAllText(layout.Runtime(legacy), "Pending legacy edit.\n");
        layout.Files.Write($"environment/providers/{legacy}/overlay.md", "Keep this unconsumed authored draft.\n");
        layout.EditRuntime(provider, "Provider mechanics.", "Portable revision.");
        AssertCapture(layout, [$"environment/providers/{provider}/overlay.md", layout.Target(provider)]);
        Assert.False(File.Exists(Path.Combine(layout.Repository, layout.Target(legacy))));
    }

    [Fact]
    public void ExistingStagedAndUnstagedDraftStopBeforeCapture()
    {
        using var layout = new Layout("codex", "copilot");
        layout.EditRuntime("codex", "Provider mechanics.", "Codex revision.");
        var reviewed = Preview(layout);
        const string draft = "environment/providers/copilot/overlay.md";
        layout.Files.Write(draft, "Staged draft.\n");
        layout.Files.Git("add", "--", draft);
        layout.Files.Write(draft, "Staged plus unstaged draft.\n");
        layout.Files.AssertNoChanges(() => Assert.Throws<InvalidOperationException>(() => MigrationPortableCaptureCommitter.WriteAndCommit(reviewed)));
    }

    [Theory]
    [InlineData("head")]
    [InlineData("branch")]
    [InlineData("runtime")]
    [InlineData("config")]
    [InlineData("target")]
    [InlineData("marker")]
    [InlineData("enrollment")]
    public void StaleInputsStopBeforeFilesStagingOrCommit(string kind)
    {
        using var layout = new Layout("codex", "copilot");
        layout.EditRuntime("codex", "Provider mechanics.", "Codex revision.");
        layout.EditRuntime("copilot", "Provider mechanics.", "Copilot revision.");
        var reviewed = Preview(layout);
        switch (kind)
        {
            case "head": layout.Files.Git("commit", "--allow-empty", "-m", "Later checkpoint"); break;
            case "branch": layout.Files.Git("switch", "-c", "other-branch"); break;
            case "runtime": layout.EditRuntime("copilot", "Copilot revision.", "Later revision."); break;
            case "config": File.WriteAllText(layout.ConfigRuntime, "personality = 'pragmatic'\n"); break;
            case "target": layout.Files.Write(layout.Target("codex"), "Keep target draft.\n"); break;
            case "marker": layout.Files.Write(MigrationPreflight.FormatRelativePath, "2.0\n"); break;
            default: File.Delete(layout.Runtime("copilot")); break;
        }
        layout.Files.AssertNoChanges(() => Assert.Throws<InvalidOperationException>(() => MigrationPortableCaptureCommitter.WriteAndCommit(reviewed)));
    }

    [Fact]
    public void CallerDerivedProposalsCannotRedirectCombinedCapture()
    {
        using var layout = new Layout("codex", "copilot");
        layout.EditRuntime("codex", "Provider mechanics.", "Codex revision.");
        layout.EditRuntime("copilot", "Provider mechanics.", "Copilot revision.");
        var reviewed = Preview(layout);
        reviewed = reviewed with
        {
            Codex = reviewed.Codex! with { Instructions = new PortableBackupAttributionPreview("shared", "../escape.md", "Untrusted content.\n"),
                ProposedCanonicalConfig = Encoding.UTF8.GetBytes("personality = 'none'\n") },
            Copilot = reviewed.Copilot! with { Instructions = new PortableBackupAttributionPreview(null, null, null) }
        };
        AssertCapture(layout, ["environment/providers/codex/overlay.md", "environment/providers/copilot/overlay.md",
            layout.Target("codex"), layout.Target("copilot")], reviewed);
    }

    [Fact]
    public void BaselineGitExecutableModesArePreservedForSourceAndTarget()
    {
        using var layout = new Layout("codex");
        const string source = "environment/providers/codex/overlay.md";
        var target = layout.Target("codex");
        // Git executable bits can be tested on Windows too; do not require
        // Unix permissions or let local file-mode detection rewrite the index.
        layout.Files.Git("config", "core.fileMode", "false");
        layout.Files.Git("update-index", "--chmod=+x", "--", source, target);
        layout.Files.Git("commit", "--quiet", "-m", "Executable fixture checkpoint");
        layout.EditRuntime("codex", "Provider mechanics.", "Mode-preserving revision.");
        AssertCapture(layout, [source, target]);
        Assert.All(new[] { source, target }, path => Assert.StartsWith("100755 blob ", layout.Files.Git("ls-tree", "HEAD", "--", path), StringComparison.Ordinal));
    }

    [Fact]
    public void SigningFailureRetainsExactCombinedStagingWithoutRollback()
    {
        using var layout = new Layout("codex", "copilot");
        layout.EditRuntime("codex", "Provider mechanics.", "Codex revision.");
        layout.EditRuntime("copilot", "Provider mechanics.", "Copilot revision.");
        var reviewed = Preview(layout);
        var parent = layout.Files.Head();
        var runtime = layout.Files.RuntimeBytes();
        layout.Files.Git("config", "gpg.format", "openpgp");
        layout.Files.Git("config", "gpg.program", Path.Combine(layout.Files.Root, "missing-signing-program"));
        layout.Files.Git("config", "commit.gpgSign", "true");

        var error = Assert.Throws<IOException>(() => MigrationPortableCaptureCommitter.WriteAndCommit(reviewed));

        Assert.Contains("new commit may remain", error.Message, StringComparison.Ordinal);
        Assert.Contains("no rollback, push or runtime apply", error.Message, StringComparison.Ordinal);
        Assert.NotNull(error.InnerException);
        Assert.Equal(parent, layout.Files.Head());
        string[] expected = ["environment/providers/codex/overlay.md", "environment/providers/copilot/overlay.md", layout.Target("codex"), layout.Target("copilot")];
        Assert.Equal(expected.Order(StringComparer.Ordinal), StagedPaths(layout));
        foreach (var path in expected) Assert.Equal(File.ReadAllBytes(Path.Combine(layout.Repository, path)),
            GitProcess.RunRequiredBytes(layout.Repository, "Read retained staged proposal", "show", $":{path}"));
        layout.Files.AssertRuntime(runtime);
    }

    [Fact]
    public void GitNewlineTransformationStopsBeforePublishing()
    {
        using var layout = new Layout("codex");
        const string source = "environment/providers/codex/overlay.md";
        layout.EditRuntime("codex", "Provider mechanics.", "Line one.\r\nLine two.");
        var reviewed = Preview(layout);
        var parent = layout.Files.Head();
        var runtime = layout.Files.RuntimeBytes();
        File.WriteAllText(Path.Combine(layout.Repository, ".git", "info", "attributes"), $"{source} text eol=lf\n");

        var error = Assert.Throws<IOException>(() => MigrationPortableCaptureCommitter.WriteAndCommit(reviewed));

        Assert.Contains("Staged portable capture byte length differs from review", error.InnerException!.Message, StringComparison.Ordinal);
        Assert.Equal(parent, layout.Files.Head());
        Assert.Equal(new[] { source, layout.Target("codex") }.Order(StringComparer.Ordinal), StagedPaths(layout));
        Assert.Contains("\r\n", File.ReadAllText(Path.Combine(layout.Repository, source)), StringComparison.Ordinal);
        layout.Files.AssertRuntime(runtime);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TargetFailureRetainsAuthoredAndEarlierTargetChangesWithoutStaging(bool earlierTarget)
    {
        using var layout = new Layout("codex", "copilot");
        if (earlierTarget) layout.EditRuntime("codex", "Provider mechanics.", "First revision.");
        layout.EditRuntime("copilot", "Provider mechanics.", "Blocked revision.");
        var reviewed = Preview(layout);
        var blocked = layout.Target("copilot");
        string[] changed = earlierTarget ? ["environment/providers/codex/overlay.md", "environment/providers/copilot/overlay.md", layout.Target("codex")]
            : ["environment/providers/copilot/overlay.md"];
        layout.Files.AssertOnlyChanges(changed, () => MigrationLegacyCaptureWriterTests.BlockReplacement(Path.Combine(layout.Repository, blocked), () =>
        {
            var error = Assert.Throws<IOException>(() => MigrationPortableCaptureCommitter.WriteAndCommit(reviewed));
            var inner = Assert.IsType<MigrationPortableTargetWriteException>(error.InnerException);
            Assert.Equal(blocked, inner.AttemptedPath);
            Assert.Equal(earlierTarget ? new[] { layout.Target("codex") } : [], inner.WrittenPaths);
        }));
    }

    private static void AssertCapture(Layout layout, string[] changedPaths, MigrationPortableCapturePreview? reviewed = null)
    {
        reviewed ??= Preview(layout);
        var parent = layout.Files.Head();
        var remote = layout.Files.Git("rev-parse", "refs/remotes/shared/main");
        var gitDirectory = Path.Combine(layout.Repository, ".git") + Path.DirectorySeparatorChar;
        var files = Directory.EnumerateFiles(layout.Files.Root, "*", SearchOption.AllDirectories)
            .Where(path => !path.StartsWith(gitDirectory, StringComparison.Ordinal)).ToDictionary(path => path, File.ReadAllBytes);
        var expected = changedPaths.Select(path => Path.Combine(layout.Repository, path)).ToHashSet(StringComparer.Ordinal);
        var modes = changedPaths.ToDictionary(path => path, path => layout.Files.Git("ls-tree", "HEAD", "--", path).Split(' ')[0]);

        var commit = MigrationPortableCaptureCommitter.WriteAndCommit(reviewed);

        Assert.NotNull(commit);
        Assert.Equal(commit, layout.Files.Head());
        Assert.Equal($"{commit} {parent}", layout.Files.Git("--no-replace-objects", "rev-list", "--parents", "-n", "1", commit).Trim());
        Assert.Equal("Backup portable environment before migration", layout.Files.Git("log", "-1", "--format=%s").Trim());
        Assert.Equal(changedPaths.Order(StringComparer.Ordinal), layout.Files.Git("diff-tree", "--no-commit-id", "--no-renames", "--name-only", "-r", "HEAD")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal));
        Assert.Equal(files.Keys.Order(StringComparer.Ordinal), Directory.EnumerateFiles(layout.Files.Root, "*", SearchOption.AllDirectories)
            .Where(path => !path.StartsWith(gitDirectory, StringComparison.Ordinal)).Order(StringComparer.Ordinal));
        foreach (var (path, before) in files)
        {
            var after = File.ReadAllBytes(path);
            if (expected.Contains(path)) Assert.False(before.AsSpan().SequenceEqual(after));
            else Assert.Equal(before, after);
        }
        foreach (var path in changedPaths)
        {
            Assert.Equal(File.ReadAllBytes(Path.Combine(layout.Repository, path)), GitProcess.RunRequiredBytes(layout.Repository,
                "Read committed proposal", "--no-replace-objects", "show", $"{commit}:{path}"));
            Assert.StartsWith($"{modes[path]} blob ", layout.Files.Git("ls-tree", commit, "--", path), StringComparison.Ordinal);
        }
        Assert.Equal(remote, layout.Files.Git("rev-parse", "refs/remotes/shared/main"));
        Assert.Empty(StagedPaths(layout));
        Assert.False(File.Exists(Path.Combine(layout.Repository, MigrationPreflight.FormatRelativePath)));
    }

    private static MigrationPortableCapturePreview Preview(Layout layout) => MigrationPortableCapturePreview.Read(layout.Context());

    private static string[] StagedPaths(Layout layout) => layout.Files.Git("diff", "--cached", "--name-only")
        .Split('\n', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal).ToArray();
}
