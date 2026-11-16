using System.Text;
using Layout = Aec.Tests.MigrationPortableCapturePreviewTests.Layout;

namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class MigrationSourceCommitterTests
{
    private const string Shared = "environment/shared/instructions.md";
    private const string CommitSubject = "Initialize migration instruction sections";

    [Fact]
    public void CommitsFourNewEmptyRequiredSourcesAndThenIsNoOp()
    {
        using var layout = new Layout();
        var paths = RequiredPaths(layout);
        foreach (var path in paths) layout.Files.Write(path, string.Empty);
        var reviewed = Preview(layout);
        Assert.Equal(paths.Order(StringComparer.Ordinal), reviewed.ChangedPaths.Order(StringComparer.Ordinal));
        var commit = CommitAndAssertSourcesOnly(layout, reviewed, paths);
        foreach (var path in paths)
        {
            Assert.Empty(GitProcess.RunRequiredBytes(layout.Repository,
                "Read committed empty source", "show", $"{commit}:{path}"));
        }
        var clean = Preview(layout);
        layout.Files.AssertNoChanges(() => Assert.Null(MigrationSourceCommitter.Commit(clean)));
    }

    [Fact]
    public void AlreadyCommittedEmptyRequiredSourcesAreNoOp()
    {
        using var layout = new Layout("codex", "copilot");
        var paths = RequiredPaths(layout);
        foreach (var path in paths) layout.Files.Write(path, string.Empty);
        layout.Files.Git(["add", "--", .. paths]);
        layout.Files.Git("commit", "--quiet", "-m", "Commit empty migration sources");
        var reviewed = Preview(layout);
        Assert.Empty(reviewed.ChangedPaths);
        layout.Files.AssertNoChanges(() => Assert.Null(MigrationSourceCommitter.Commit(reviewed)));
    }

    [Theory]
    [InlineData("shared")]
    [InlineData("platform")]
    [InlineData("codex")]
    [InlineData("copilot")]
    public void CommitsReviewedSectionEditsOnly(string section)
    {
        using var layout = new Layout("codex", "copilot");
        var path = section switch
        {
            "shared" => Shared,
            "platform" => $"environment/platforms/{layout.Platform}/policy.md",
            "codex" => "environment/providers/codex/overlay.md",
            _ => "environment/providers/copilot/overlay.md"
        };
        File.AppendAllText(Path.Combine(layout.Repository, path), "Reviewed source edit.\n");
        var reviewed = Preview(layout);
        CommitAndAssertSourcesOnly(layout, reviewed, [path]);
    }

    [Fact]
    public void MixedExistingAndNewSourcesCommitTheirUnion()
    {
        using var layout = new Layout("codex", "copilot");
        const string added = "environment/providers/copilot/overlay.md";
        layout.Files.Git("rm", "--cached", "--", added);
        layout.Files.Git("commit", "--quiet", "-m", "Remove Copilot source baseline");
        File.AppendAllText(Path.Combine(layout.Repository, Shared), "Shared source edit.\n");
        var reviewed = Preview(layout);
        CommitAndAssertSourcesOnly(layout, reviewed, [Shared, added]);
    }

    [Theory]
    [InlineData("head")]
    [InlineData("branch")]
    [InlineData("source")]
    [InlineData("missing-row")]
    [InlineData("path")]
    [InlineData("working-content")]
    [InlineData("changed-paths")]
    public void StaleOrTamperedReviewStopsBeforeStagingOrCommit(string mutation)
    {
        using var layout = new Layout("codex", "copilot");
        File.AppendAllText(Path.Combine(layout.Repository, Shared), "Reviewed edit.\n");
        var reviewed = Preview(layout);
        switch (mutation)
        {
            case "head": layout.Files.Git("commit", "--allow-empty", "-m", "Later checkpoint"); break;
            case "branch": layout.Files.Git("switch", "-c", "other-branch"); break;
            case "source": File.AppendAllText(Path.Combine(layout.Repository, Shared), "Later edit.\n"); break;
            case "missing-row": reviewed = reviewed with { Files = reviewed.Files.Skip(1).ToArray() }; break;
            case "path": reviewed = reviewed with { Files = reviewed.Files.Select((file, index) => index == 0
                ? file with { RelativePath = "../outside.md" } : file).ToArray() }; break;
            case "working-content": reviewed = reviewed with { Files = reviewed.Files.Select((file, index) => index == 0
                ? file with { WorkingContent = Encoding.UTF8.GetBytes("Forged content.\n") } : file).ToArray() }; break;
            default: reviewed = reviewed with { ChangedPaths = [] }; break;
        }
        layout.Files.AssertNoChanges(() => Assert.ThrowsAny<InvalidOperationException>(() => MigrationSourceCommitter.Commit(reviewed)));
    }

    [Fact]
    public void ExistingStagedIndexIsRefusedUnchanged()
    {
        using var layout = new Layout("codex", "copilot");
        File.AppendAllText(Path.Combine(layout.Repository, Shared), "Reviewed edit.\n");
        var reviewed = Preview(layout);
        const string other = "notes.md";
        layout.Files.Write(other, "Actually staged unrelated edit.\n");
        layout.Files.Git("add", "--", other);
        layout.Files.AssertNoChanges(() => Assert.ThrowsAny<InvalidOperationException>(() => MigrationSourceCommitter.Commit(reviewed)));
    }

    [Fact]
    public void HiddenAssumeUnchangedSourceIsRefusedWithoutClearingFlag()
    {
        using var layout = new Layout("codex", "copilot");
        File.AppendAllText(Path.Combine(layout.Repository, Shared), "Reviewed edit.\n");
        var reviewed = Preview(layout);
        layout.Files.Git("update-index", "--assume-unchanged", "--", Shared);
        var head = layout.Files.Head();
        var working = CaptureWorkingFiles(layout);
        var error = Assert.ThrowsAny<InvalidOperationException>(() => MigrationSourceCommitter.Commit(reviewed));
        Assert.Contains("staged changes or a new commit may remain", error.Message, StringComparison.Ordinal);
        Assert.Equal(head, layout.Files.Head());
        Assert.StartsWith("h ", layout.Files.Git("ls-files", "-v", "--", Shared), StringComparison.Ordinal);
        AssertWorkingFilesUnchanged(layout, working);
    }

    [Fact]
    public void GitNewlineTransformationIsRejectedWithStagingRetained()
    {
        using var layout = new Layout("codex", "copilot");
        var path = "environment/providers/codex/overlay.md";
        File.WriteAllBytes(Path.Combine(layout.Repository, path), Encoding.UTF8.GetBytes("Line one.\r\nLine two.\r\n"));
        var reviewed = Preview(layout);
        File.WriteAllText(Path.Combine(layout.Repository, ".git", "info", "attributes"), $"{path} text eol=lf\n");
        var parent = layout.Files.Head();
        var working = CaptureWorkingFiles(layout);
        var error = Assert.Throws<InvalidOperationException>(() => MigrationSourceCommitter.Commit(reviewed));
        Assert.Contains("staged changes or a new commit may remain", error.Message, StringComparison.Ordinal);
        Assert.Equal(parent, layout.Files.Head());
        Assert.Equal([path], StagedPaths(layout));
        Assert.Equal(Encoding.UTF8.GetBytes("Line one.\r\nLine two.\r\n"), File.ReadAllBytes(Path.Combine(layout.Repository, path)));
        AssertWorkingFilesUnchanged(layout, working);
    }

    [Fact]
    public void SigningFailureRetainsExactStagedSources()
    {
        using var layout = new Layout("codex", "copilot");
        File.AppendAllText(Path.Combine(layout.Repository, Shared), "Reviewed edit.\n");
        var reviewed = Preview(layout);
        var paths = reviewed.ChangedPaths.ToArray();
        var parent = layout.Files.Head();
        layout.Files.Git("config", "gpg.format", "openpgp");
        layout.Files.Git("config", "gpg.program", Path.Combine(layout.Files.Root, "missing-signing-program"));
        layout.Files.Git("config", "commit.gpgSign", "true");
        var working = CaptureWorkingFiles(layout);
        var error = Assert.ThrowsAny<InvalidOperationException>(() => MigrationSourceCommitter.Commit(reviewed));
        Assert.Contains("staged changes or a new commit may remain", error.Message, StringComparison.Ordinal);
        Assert.NotNull(error.InnerException);
        Assert.Equal(parent, layout.Files.Head());
        Assert.Equal(paths.Order(StringComparer.Ordinal), StagedPaths(layout).Order(StringComparer.Ordinal));
        foreach (var path in paths)
            Assert.Equal(File.ReadAllBytes(Path.Combine(layout.Repository, path)), GitProcess.RunRequiredBytes(layout.Repository,
                "Read retained staged proposal", "show", $":{path}"));
        AssertWorkingFilesUnchanged(layout, working);
    }

    [Fact]
    public void ExistingExecutableModeIsPreserved()
    {
        if (OperatingSystem.IsWindows()) return;
        using var layout = new Layout("codex", "copilot");
        const string existing = "environment/providers/codex/overlay.md";
        var path = Path.Combine(layout.Repository, existing);
        File.SetUnixFileMode(path, File.GetUnixFileMode(path) | UnixFileMode.UserExecute);
        layout.Files.Git("update-index", "--chmod=+x", "--", existing);
        layout.Files.Git("commit", "--quiet", "-m", "Executable source baseline");
        File.AppendAllText(Path.Combine(layout.Repository, existing), "Reviewed edit.\n");
        var reviewed = Preview(layout);
        var commit = CommitAndAssertSourcesOnly(layout, reviewed, [existing]);
        Assert.StartsWith("100755 blob ", layout.Files.Git("ls-tree", commit, "--", existing), StringComparison.Ordinal);
    }

    [Fact]
    public void NewExecutableModeIsRejectedWithStagingRetained()
    {
        if (OperatingSystem.IsWindows()) return;
        using var layout = new Layout();
        var paths = RequiredPaths(layout);
        foreach (var relative in paths) layout.Files.Write(relative, string.Empty);
        var path = Path.Combine(layout.Repository, Shared);
        File.SetUnixFileMode(path, File.GetUnixFileMode(path) | UnixFileMode.UserExecute);
        var reviewed = Preview(layout);
        var parent = layout.Files.Head();
        var working = CaptureWorkingFiles(layout);

        var error = Assert.Throws<InvalidOperationException>(() => MigrationSourceCommitter.Commit(reviewed));

        Assert.Equal(parent, layout.Files.Head());
        Assert.Contains("100644", error.InnerException!.Message, StringComparison.Ordinal);
        Assert.Equal(paths.Order(StringComparer.Ordinal), StagedPaths(layout));
        AssertWorkingFilesUnchanged(layout, working);
    }

    [Fact]
    public void ExistingTrackedModeChangeIsRefusedBeforePublish()
    {
        if (OperatingSystem.IsWindows()) return;
        using var layout = new Layout("codex", "copilot");
        const string path = "environment/providers/codex/overlay.md";
        File.AppendAllText(Path.Combine(layout.Repository, path), "Reviewed edit.\n");
        var reviewed = Preview(layout);
        File.SetUnixFileMode(Path.Combine(layout.Repository, path), UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var parent = layout.Files.Head();
        var working = CaptureWorkingFiles(layout);

        var error = Assert.Throws<InvalidOperationException>(() => MigrationSourceCommitter.Commit(reviewed));

        Assert.Contains("mode differs", error.InnerException!.Message, StringComparison.Ordinal);
        Assert.Equal(parent, layout.Files.Head());
        Assert.Equal([path], StagedPaths(layout));
        AssertWorkingFilesUnchanged(layout, working);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NonLocalSymbolicHeadAfterReviewIsRefusedEvenForNoOp(bool changed)
    {
        using var layout = new Layout("codex", "copilot");
        if (changed) File.AppendAllText(Path.Combine(layout.Repository, Shared), "Reviewed edit.\n");
        var reviewed = Preview(layout);
        layout.Files.Git("update-ref", "refs/tags/source-test", layout.Files.Head());
        layout.Files.Git("symbolic-ref", "HEAD", "refs/tags/source-test");

        layout.Files.AssertNoChanges(() =>
        {
            var error = Assert.Throws<InvalidOperationException>(() => MigrationSourceCommitter.Commit(reviewed));
            Assert.Contains("local Git branch", error.Message, StringComparison.Ordinal);
        });
    }

    private static MigrationSourceValidationPreview Preview(Layout layout) =>
        MigrationSourceValidationPreview.Read(layout.Context());

    private static string[] RequiredPaths(Layout layout) =>
    [
        Shared,
        $"environment/platforms/{layout.Platform}/policy.md",
        "environment/providers/codex/overlay.md",
        "environment/providers/copilot/overlay.md"
    ];

    private static string[] StagedPaths(Layout layout) => layout.Files.Git("diff", "--cached", "--no-renames", "--name-only")
        .Split('\n', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal).ToArray();

    private static string CommitAndAssertSourcesOnly(Layout layout,
        MigrationSourceValidationPreview reviewed, string[] paths)
    {
        var parent = layout.Files.Head();
        var working = CaptureWorkingFiles(layout);
        var commit = MigrationSourceCommitter.Commit(reviewed);
        Assert.NotNull(commit);
        Assert.Equal(commit, layout.Files.Head());
        Assert.Equal($"{commit} {parent}", layout.Files.Git("--no-replace-objects", "rev-list", "--parents", "-n", "1", commit).Trim());
        Assert.Equal(CommitSubject, layout.Files.Git("log", "-1", "--format=%s").Trim());
        Assert.Equal(paths.Order(StringComparer.Ordinal), layout.Files.Git("diff-tree", "--no-commit-id", "--no-renames", "--name-only", "-r", "HEAD")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal));
        foreach (var path in paths)
        {
            var source = Assert.Single(reviewed.Files, file => file.RelativePath == path);
            Assert.Equal(source.WorkingContent, GitProcess.RunRequiredBytes(layout.Repository,
                "Read committed source", "--no-replace-objects", "show", $"{commit}:{path}"));
            Assert.StartsWith($"{source.CommittedMode ?? "100644"} blob ",
                layout.Files.Git("ls-tree", commit, "--", path), StringComparison.Ordinal);
        }
        Assert.Empty(StagedPaths(layout));
        AssertWorkingFilesUnchanged(layout, working);
        return commit;
    }

    private static WorkingSnapshot CaptureWorkingFiles(Layout layout)
    {
        var gitDirectory = Path.Combine(layout.Repository, ".git");
        var gitPrefix = gitDirectory + Path.DirectorySeparatorChar;
        var files = Directory.EnumerateFiles(layout.Files.Root, "*", SearchOption.AllDirectories)
            .Where(path => !path.StartsWith(gitPrefix, StringComparison.Ordinal))
            .ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);
        var directories = Directory.EnumerateDirectories(layout.Files.Root, "*", SearchOption.AllDirectories)
            .Where(path => path != gitDirectory && !path.StartsWith(gitPrefix, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).ToArray();
        var modes = new Dictionary<string, UnixFileMode>(StringComparer.Ordinal);
        if (!OperatingSystem.IsWindows())
        {
            foreach (var path in files.Keys) modes.Add(path, File.GetUnixFileMode(path));
        }
        var remote = layout.Files.Git("rev-parse", "refs/remotes/shared/main").Trim();
        return new WorkingSnapshot(files, directories, modes, remote);
    }

    private static void AssertWorkingFilesUnchanged(Layout layout, WorkingSnapshot before)
    {
        var after = CaptureWorkingFiles(layout);
        Assert.Equal(before.Files.Keys.Order(StringComparer.Ordinal), after.Files.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(before.Directories, after.Directories);
        foreach (var (path, bytes) in before.Files) Assert.Equal(bytes, after.Files[path]);
        foreach (var (path, mode) in before.Modes) Assert.Equal(mode, after.Modes[path]);
        Assert.Equal(before.Remote, after.Remote);
    }

    private sealed record WorkingSnapshot(Dictionary<string, byte[]> Files, string[] Directories,
        Dictionary<string, UnixFileMode> Modes, string Remote);
}
