using System.Text;
using Layout = Aec.Tests.MigrationPortableCapturePreviewTests.Layout;

namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class MigrationTargetCommitterTests
{
    private const string CommitSubject = "Render migration instruction target";

    [Theory]
    [InlineData("codex")]
    [InlineData("copilot")]
    public void CreatesAndCommitsOnlyTheReviewedTarget(string provider)
    {
        using var layout = new Layout("codex", "copilot");
        RemoveAndCommitTarget(layout, layout.Target(provider));
        var reviewed = Preview(layout, provider);
        var before = CaptureOutsideLocalGit(layout);
        var remote = RemoteTip(layout);

        var commit = MigrationTargetCommitter.WriteAndCommit(reviewed);

        AssertSuccessfulTargetCommit(layout, reviewed, commit, before, remote);
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("copilot")]
    public void RegeneratesAndCommitsOnlyTheReviewedTarget(string provider)
    {
        using var layout = new Layout("codex", "copilot");
        var originalHead = layout.Files.Head();
        CommitSourceEdit(layout, provider, "Reviewed revision.\n");
        var reviewed = Preview(layout, provider, originalHead);
        Assert.Equal(MigrationTargetAction.Regenerate, reviewed.Action);
        var before = CaptureOutsideLocalGit(layout);
        var remote = RemoteTip(layout);

        var commit = MigrationTargetCommitter.WriteAndCommit(reviewed);

        AssertSuccessfulTargetCommit(layout, reviewed, commit, before, remote);
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("copilot")]
    public void UnchangedTargetIsMutationFreeNoOp(string provider)
    {
        using var layout = new Layout("codex", "copilot");
        var reviewed = Preview(layout, provider);
        var before = CaptureEntireFixture(layout);
        var head = layout.Files.Head();
        var remote = RemoteTip(layout);

        Assert.Null(MigrationTargetCommitter.WriteAndCommit(reviewed));

        Assert.Equal(head, layout.Files.Head());
        Assert.Equal(remote, RemoteTip(layout));
        AssertFixtureSnapshot(layout, before);
    }

    [Fact]
    public void CommittedEmptySourcesCreateNonemptyRenderedTarget()
    {
        using var layout = new Layout("codex", "copilot");
        var target = layout.Target("codex");
        RemoveAndCommitTarget(layout, target);
        var sources = new[]
        {
            "environment/shared/instructions.md",
            $"environment/platforms/{layout.Platform}/policy.md",
            "environment/providers/codex/overlay.md"
        };
        foreach (var source in sources) layout.Files.Write(source, string.Empty);
        layout.Commit(sources);
        var reviewed = Preview(layout, "codex");
        Assert.All(reviewed.Sources, source => Assert.Empty(source.WorkingContent));
        Assert.NotEmpty(reviewed.DesiredContent);
        var before = CaptureOutsideLocalGit(layout);
        var remote = RemoteTip(layout);

        var commit = MigrationTargetCommitter.WriteAndCommit(reviewed);

        AssertSuccessfulTargetCommit(layout, reviewed, commit, before, remote);
        var targetBytes = File.ReadAllBytes(Path.Combine(layout.Repository, target));
        Assert.Contains("AEC:SOURCE:SHARED:BEGIN", Encoding.UTF8.GetString(targetBytes), StringComparison.Ordinal);
    }

    [Fact]
    public void NewTargetCommitsAsRegularFileAndUsesPrivateWorkingPermissions()
    {
        using var layout = new Layout("codex", "copilot");
        var target = layout.Target("codex");
        RemoveAndCommitTarget(layout, target);
        var reviewed = Preview(layout, "codex");
        var before = CaptureOutsideLocalGit(layout);
        var remote = RemoteTip(layout);

        var commit = MigrationTargetCommitter.WriteAndCommit(reviewed);

        AssertSuccessfulTargetCommit(layout, reviewed, commit, before, remote);
        Assert.StartsWith("100644 blob ", layout.Files.Git("ls-tree", commit!, "--", target), StringComparison.Ordinal);
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(Path.Combine(layout.Repository, target)));
    }

    [Theory]
    [InlineData("100644")]
    [InlineData("100755")]
    public void RegenerationPreservesCommittedGitModeAndRawWorkingPermissions(string gitMode)
    {
        if (gitMode == "100755" && OperatingSystem.IsWindows()) return;
        using var layout = new Layout("codex", "copilot");
        var originalHead = layout.Files.Head();
        var target = layout.Target("codex");
        var targetPath = Path.Combine(layout.Repository, target);
        if (gitMode == "100755" && !OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(targetPath, File.GetUnixFileMode(targetPath) | UnixFileMode.UserExecute);
            layout.Files.Git("update-index", "--chmod=+x", "--", target);
            layout.Files.Git("commit", "--quiet", "-m", "Executable target baseline");
        }
        else if (!OperatingSystem.IsWindows())
        {
            // Git ignores group/other read bits. No mode-only commit is needed.
            File.SetUnixFileMode(targetPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
        }
        var rawMode = OperatingSystem.IsWindows() ? (UnixFileMode?)null : File.GetUnixFileMode(targetPath);
        CommitSourceEdit(layout, "codex", "Reviewed revision.\n");
        var reviewed = Preview(layout, "codex", originalHead);
        Assert.Equal(gitMode, reviewed.CommittedMode);
        var before = CaptureOutsideLocalGit(layout);
        var remote = RemoteTip(layout);

        var commit = MigrationTargetCommitter.WriteAndCommit(reviewed);

        AssertSuccessfulTargetCommit(layout, reviewed, commit, before, remote);
        Assert.StartsWith($"{gitMode} blob ", layout.Files.Git("ls-tree", commit!, "--", target), StringComparison.Ordinal);
        if (!OperatingSystem.IsWindows() && rawMode is { } expectedMode)
            Assert.Equal(expectedMode, File.GetUnixFileMode(targetPath));
    }

    [Fact]
    public void SigningFailureRetainsExactlyTheSelectedTargetStagedWithoutCommit()
    {
        using var layout = new Layout("codex", "copilot");
        var target = layout.Target("codex");
        RemoveAndCommitTarget(layout, target);
        var reviewed = Preview(layout, "codex");
        var parent = layout.Files.Head();
        layout.Files.Git("config", "gpg.format", "openpgp");
        layout.Files.Git("config", "gpg.program", Path.Combine(layout.Files.Root, "missing-signing-program"));
        layout.Files.Git("config", "commit.gpgSign", "true");
        var before = CaptureOutsideLocalGit(layout);
        var rawMode = OperatingSystem.IsWindows() ? (UnixFileMode?)null :
            UnixFileMode.UserRead | UnixFileMode.UserWrite;

        var error = Assert.Throws<InvalidOperationException>(() =>
            MigrationTargetCommitter.WriteAndCommit(reviewed));

        Assert.Contains("staged changes or a new commit may remain", error.Message, StringComparison.Ordinal);
        Assert.NotNull(error.InnerException);
        Assert.Equal(parent, layout.Files.Head());
        Assert.Equal([target], StagedPaths(layout));
        Assert.Equal(reviewed.DesiredContent, GitProcess.RunRequiredBytes(layout.Repository,
            "Read retained staged target", "show", $":{target}"));
        Assert.Equal(reviewed.DesiredContent, File.ReadAllBytes(Path.Combine(layout.Repository, target)));
        AssertOutsideLocalGitSnapshot(layout, before, target);
        if (!OperatingSystem.IsWindows() && rawMode is { } expectedMode)
            Assert.Equal(expectedMode, File.GetUnixFileMode(Path.Combine(layout.Repository, target)));
    }

    [Fact]
    public void GitNewlineTransformationIsRefusedAndExactTargetRemainsStaged()
    {
        using var layout = new Layout("codex", "copilot");
        var target = layout.Target("codex");
        RemoveAndCommitTarget(layout, target);
        var shared = "environment/shared/instructions.md";
        File.WriteAllBytes(Path.Combine(layout.Repository, shared), Encoding.UTF8.GetBytes("Line one.\r\nLine two.\r\n"));
        layout.Commit(shared);
        var reviewed = Preview(layout, "codex");
        Assert.Contains("\r\n", Encoding.UTF8.GetString(reviewed.DesiredContent), StringComparison.Ordinal);
        File.WriteAllText(Path.Combine(layout.Repository, ".git", "info", "attributes"), $"{target} text eol=lf\n");
        var parent = layout.Files.Head();
        var before = CaptureOutsideLocalGit(layout);

        var error = Assert.Throws<InvalidOperationException>(() =>
            MigrationTargetCommitter.WriteAndCommit(reviewed));

        Assert.Contains("staged changes or a new commit may remain", error.Message, StringComparison.Ordinal);
        Assert.Contains("differs from review", error.InnerException!.Message, StringComparison.Ordinal);
        Assert.Equal(parent, layout.Files.Head());
        Assert.Equal([target], StagedPaths(layout));
        Assert.Equal(reviewed.DesiredContent, File.ReadAllBytes(Path.Combine(layout.Repository, target)));
        var normalized = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(reviewed.DesiredContent)
            .Replace("\r\n", "\n", StringComparison.Ordinal));
        Assert.Equal(normalized, GitProcess.RunRequiredBytes(layout.Repository,
            "Read retained transformed target", "--no-replace-objects", "show", $":{target}"));
        AssertOutsideLocalGitSnapshot(layout, before, target);
    }

    [Fact]
    public void ExistingStagedUnrelatedFileIsRefusedBeforeTargetWrite()
    {
        using var layout = new Layout("codex", "copilot");
        var target = layout.Target("codex");
        RemoveAndCommitTarget(layout, target);
        var reviewed = Preview(layout, "codex");
        layout.Files.Write("notes.md", "Unrelated staged content.\n");
        layout.Files.Git("add", "--", "notes.md");
        var before = CaptureEntireFixture(layout);
        var head = layout.Files.Head();

        Assert.Throws<InvalidOperationException>(() => MigrationTargetCommitter.WriteAndCommit(reviewed));

        Assert.Equal(head, layout.Files.Head());
        AssertFixtureSnapshot(layout, before);
        Assert.Equal(["notes.md"], StagedPaths(layout));
    }

    [Theory]
    [InlineData("head")]
    [InlineData("desired")]
    [InlineData("selected-path")]
    [InlineData("source-snapshot")]
    [InlineData("source-membership")]
    public void StaleOrTamperedReviewIsRejectedBeforeWriting(string mutation)
    {
        using var layout = new Layout("codex", "copilot");
        var target = layout.Target("codex");
        RemoveAndCommitTarget(layout, target);
        var reviewed = Preview(layout, "codex");
        switch (mutation)
        {
            case "head": layout.Files.Git("commit", "--allow-empty", "-m", "Later checkpoint"); break;
            case "desired": reviewed = reviewed with { DesiredContent = Encoding.UTF8.GetBytes("Forged proposal.\n") }; break;
            case "selected-path": reviewed = reviewed with { TargetRelativePath = layout.Target("copilot") }; break;
            case "source-snapshot": reviewed = reviewed with
            {
                Sources = reviewed.Sources.Select((source, index) => index == 0
                    ? source with { WorkingContent = Encoding.UTF8.GetBytes("Forged source.\n") }
                    : source).ToArray()
            }; break;
            default: reviewed = reviewed with { Sources = reviewed.Sources.Take(2).ToArray() }; break;
        }
        var before = CaptureEntireFixture(layout);
        var head = layout.Files.Head();

        Assert.Throws<InvalidOperationException>(() => MigrationTargetCommitter.WriteAndCommit(reviewed));

        Assert.Equal(head, layout.Files.Head());
        AssertFixtureSnapshot(layout, before);
        Assert.Empty(StagedPaths(layout));
    }

    [Fact]
    public void WrongReviewedOriginalCommitIsInvalidDataBeforeWriting()
    {
        using var layout = new Layout("codex", "copilot");
        var target = layout.Target("codex");
        RemoveAndCommitTarget(layout, target);
        var preview = Preview(layout, "codex");
        var reviewed = preview with
        {
            Preparation = preview.Preparation with { OriginalCommit = "HEAD~1" }
        };
        var before = CaptureEntireFixture(layout);
        var head = layout.Files.Head();

        Assert.Throws<InvalidDataException>(() => MigrationTargetCommitter.WriteAndCommit(reviewed));

        Assert.Equal(head, layout.Files.Head());
        AssertFixtureSnapshot(layout, before);
        Assert.Empty(StagedPaths(layout));
    }

    [Theory]
    [InlineData("source-edit")]
    [InlineData("target-edit")]
    [InlineData("target-collision")]
    public void IndependentWorkingTreeMutationIsRefusedWithoutOverwrite(string mutation)
    {
        using var layout = new Layout("codex", "copilot");
        var target = layout.Target("codex");
        if (mutation == "target-collision") RemoveAndCommitTarget(layout, target);
        var reviewed = Preview(layout, "codex");
        var path = mutation == "source-edit" ? "environment/shared/instructions.md" : target;
        var content = mutation == "source-edit" ? "Independent source edit.\n" : "Independent target edit.\n";
        if (mutation == "target-collision") layout.Files.Write(path, content);
        else File.AppendAllText(Path.Combine(layout.Repository, path), content);
        var before = CaptureEntireFixture(layout);
        var head = layout.Files.Head();

        Assert.Throws<InvalidOperationException>(() => MigrationTargetCommitter.WriteAndCommit(reviewed));

        Assert.Equal(head, layout.Files.Head());
        AssertFixtureSnapshot(layout, before);
        Assert.Empty(StagedPaths(layout));
    }

    [Fact]
    public void IgnoredMissingTargetIsRefusedWithoutWriting()
    {
        using var layout = new Layout("codex", "copilot");
        var target = layout.Target("codex");
        RemoveAndCommitTarget(layout, target);
        var reviewed = Preview(layout, "codex");
        File.AppendAllText(Path.Combine(layout.Repository, ".git", "info", "exclude"), target + "\n");
        var before = CaptureEntireFixture(layout);
        var head = layout.Files.Head();

        Assert.Throws<InvalidOperationException>(() => MigrationTargetCommitter.WriteAndCommit(reviewed));

        Assert.Equal(head, layout.Files.Head());
        AssertFixtureSnapshot(layout, before);
        Assert.Empty(StagedPaths(layout));
    }

    private static MigrationTargetPreview Preview(Layout layout, string provider, string? original = null) =>
        MigrationTargetPreview.Read(layout.Context(original), provider);

    private static void RemoveAndCommitTarget(Layout layout, string target)
    {
        layout.Files.Git("rm", "--quiet", "--", target);
        layout.Files.Git("commit", "--quiet", "-m", "Remove generated target");
    }

    private static void CommitSourceEdit(Layout layout, string provider, string content)
    {
        var source = $"environment/providers/{provider}/overlay.md";
        layout.Files.Write(source, content);
        layout.Commit(source);
    }

    private static void AssertSuccessfulTargetCommit(Layout layout, MigrationTargetPreview reviewed,
        string? commit, FixtureSnapshot before, string remote)
    {
        Assert.NotNull(commit);
        Assert.Equal(commit, layout.Files.Head());
        var parent = reviewed.Preparation.Preflight.Commit;
        Assert.Equal($"{commit} {parent}", layout.Files.Git("--no-replace-objects", "rev-list", "--parents", "-n", "1", commit!).Trim());
        Assert.Equal(CommitSubject, layout.Files.Git("log", "-1", "--format=%s").Trim());
        Assert.Equal([reviewed.TargetRelativePath], CommittedPaths(layout, parent, commit!));
        Assert.Equal(reviewed.DesiredContent, GitProcess.RunRequiredBytes(layout.Repository,
            "Read committed target", "--no-replace-objects", "show", $"{commit}:{reviewed.TargetRelativePath}"));
        Assert.Equal(reviewed.DesiredContent, File.ReadAllBytes(Path.Combine(layout.Repository, reviewed.TargetRelativePath)));
        Assert.Empty(StagedPaths(layout));
        Assert.Equal(remote, RemoteTip(layout));
        AssertOutsideLocalGitSnapshot(layout, before, reviewed.TargetRelativePath);
    }

    private static string[] CommittedPaths(Layout layout, string parent, string commit) =>
        layout.Files.Git("diff-tree", "--no-commit-id", "--no-renames", "--name-only", "-r", parent, commit)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal).ToArray();

    private static string[] StagedPaths(Layout layout) =>
        layout.Files.Git("diff", "--cached", "--no-renames", "--name-only")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal).ToArray();

    private static string RemoteTip(Layout layout) => layout.Files.Git("rev-parse", "refs/remotes/shared/main").Trim();

    private static FixtureSnapshot CaptureEntireFixture(Layout layout) => Capture(layout, excludeLocalGit: false);

    private static FixtureSnapshot CaptureOutsideLocalGit(Layout layout) => Capture(layout, excludeLocalGit: true);

    private static FixtureSnapshot Capture(Layout layout, bool excludeLocalGit)
    {
        var localGit = Path.Combine(layout.Repository, ".git");
        var gitPrefix = localGit + Path.DirectorySeparatorChar;
        var files = Directory.EnumerateFiles(layout.Files.Root, "*", SearchOption.AllDirectories)
            .Where(path => !excludeLocalGit || (!string.Equals(path, localGit, StringComparison.Ordinal) &&
                !path.StartsWith(gitPrefix, StringComparison.Ordinal)))
            .ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);
        var directories = Directory.EnumerateDirectories(layout.Files.Root, "*", SearchOption.AllDirectories)
            .Where(path => !excludeLocalGit || (!string.Equals(path, localGit, StringComparison.Ordinal) &&
                !path.StartsWith(gitPrefix, StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal).ToArray();
        var modes = new Dictionary<string, UnixFileMode>(StringComparer.Ordinal);
        if (!OperatingSystem.IsWindows())
        {
            foreach (var path in files.Keys) modes.Add(path, File.GetUnixFileMode(path));
        }
        return new FixtureSnapshot(files, directories, modes);
    }

    private static void AssertOutsideLocalGitSnapshot(Layout layout, FixtureSnapshot before, string target)
    {
        var after = CaptureOutsideLocalGit(layout);
        var targetPath = Path.Combine(layout.Repository, target);
        Assert.Contains(targetPath, after.Files.Keys);
        Assert.Equal(before.Files.Keys.Append(targetPath).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal),
            after.Files.Keys.Order(StringComparer.Ordinal));
        foreach (var (path, bytes) in before.Files)
        {
            if (path != targetPath) Assert.Equal(bytes, after.Files[path]);
        }
        Assert.Equal(before.Directories, after.Directories.Intersect(before.Directories, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray());
        foreach (var directory in after.Directories.Except(before.Directories, StringComparer.Ordinal))
            Assert.True(IsTargetParent(directory, targetPath), $"Unexpected directory created: {directory}");
        foreach (var (path, mode) in before.Modes)
        {
            if (path != targetPath) Assert.Equal(mode, after.Modes[path]);
        }
    }

    private static void AssertFixtureSnapshot(Layout layout, FixtureSnapshot before)
    {
        var after = CaptureEntireFixture(layout);
        Assert.Equal(before.Files.Keys.Order(StringComparer.Ordinal), after.Files.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(before.Directories, after.Directories);
        foreach (var (path, bytes) in before.Files) Assert.Equal(bytes, after.Files[path]);
        foreach (var (path, mode) in before.Modes) Assert.Equal(mode, after.Modes[path]);
    }

    private static bool IsTargetParent(string directory, string targetPath)
    {
        var targetDirectory = Path.GetDirectoryName(targetPath);
        while (targetDirectory is not null)
        {
            if (string.Equals(directory, targetDirectory, StringComparison.Ordinal)) return true;
            targetDirectory = Path.GetDirectoryName(targetDirectory);
        }
        return false;
    }

    private sealed record FixtureSnapshot(Dictionary<string, byte[]> Files, string[] Directories,
        Dictionary<string, UnixFileMode> Modes);
}
