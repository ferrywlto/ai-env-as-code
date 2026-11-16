using System.Text;
using Layout = Aec.Tests.MigrationPortableCapturePreviewTests.Layout;

namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class MigrationTargetWriterTests
{
    [Theory]
    [InlineData("codex")]
    [InlineData("copilot")]
    public void CreatesOnlyTheReviewedMissingTarget(string provider)
    {
        using var layout = new Layout("codex", "copilot");
        var target = layout.Target(provider);
        RemoveAndCommitTarget(layout, target);
        var reviewed = Preview(layout, provider);
        Assert.Equal(MigrationTargetAction.Create, reviewed.Action);
        var before = Capture(layout);

        var written = MigrationTargetWriter.Write(reviewed);

        Assert.Equal(target, written);
        Assert.Equal(reviewed.DesiredContent, File.ReadAllBytes(Path.Combine(layout.Repository, target)));
        AssertOnlyTargetChanged(layout, before, target);
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("copilot")]
    public void RegeneratesOnlyTheReviewedTarget(string provider)
    {
        using var layout = new Layout("codex", "copilot");
        var originalHead = layout.Files.Head();
        CommitSourceEdit(layout, provider, "Reviewed revision.\n");
        var reviewed = Preview(layout, provider, originalHead);
        Assert.Equal(MigrationTargetAction.Regenerate, reviewed.Action);
        var before = Capture(layout);

        var written = MigrationTargetWriter.Write(reviewed);

        Assert.Equal(reviewed.TargetRelativePath, written);
        Assert.Equal(reviewed.DesiredContent, File.ReadAllBytes(Path.Combine(layout.Repository, reviewed.TargetRelativePath)));
        AssertOnlyTargetChanged(layout, before, reviewed.TargetRelativePath);
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("copilot")]
    public void UnchangedTargetIsAMutationFreeNoOp(string provider)
    {
        using var layout = new Layout("codex", "copilot");
        var reviewed = Preview(layout, provider);
        Assert.Equal(MigrationTargetAction.Unchanged, reviewed.Action);
        var before = Capture(layout);

        Assert.Null(MigrationTargetWriter.Write(reviewed));

        AssertEntireFixtureUnchanged(layout, before);
    }

    [Fact]
    public void NewTargetHasExactPrivateUnixPermissions()
    {
        if (OperatingSystem.IsWindows()) return;
        using var layout = new Layout("codex", "copilot");
        var target = layout.Target("codex");
        RemoveAndCommitTarget(layout, target);
        var reviewed = Preview(layout, "codex");

        Assert.Equal(target, MigrationTargetWriter.Write(reviewed));

        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite,
            File.GetUnixFileMode(Path.Combine(layout.Repository, target)));
    }

    [Fact]
    public void RegenerationPreservesExactExistingUnixPermissions()
    {
        if (OperatingSystem.IsWindows()) return;
        using var layout = new Layout("codex", "copilot");
        var originalHead = layout.Files.Head();
        var target = layout.Target("codex");
        var targetPath = Path.Combine(layout.Repository, target);
        var expectedMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead;
        File.SetUnixFileMode(targetPath, expectedMode);
        CommitSourceEdit(layout, "codex", "Reviewed revision.\n");
        var reviewed = Preview(layout, "codex", originalHead);

        Assert.Equal(target, MigrationTargetWriter.Write(reviewed));

        Assert.Equal(expectedMode, File.GetUnixFileMode(targetPath));
    }

    internal static void StaleOrTamperedReviewIsRejectedWithoutWriterChanges(string state)
    {
        using var layout = new Layout("codex", "copilot");
        var reviewed = Preview(layout, "codex");
        switch (state)
        {
            case "head":
                layout.Files.Git("commit", "--allow-empty", "-m", "Later checkpoint");
                break;
            case "branch":
                layout.Files.Git("switch", "-c", "other-branch");
                break;
            case "context":
                reviewed = reviewed with
                {
                    Preparation = reviewed.Preparation with { OriginalCommit = "HEAD~1" }
                };
                break;
            case "platform":
                reviewed = reviewed with { Platform = "wrong-platform" };
                break;
            case "harness":
                reviewed = reviewed with { Harness = reviewed.Harness with { Provider = "copilot" } };
                break;
            case "path":
                reviewed = reviewed with { TargetRelativePath = "environment/targets/outside.md" };
                break;
            case "action":
                reviewed = reviewed with { Action = MigrationTargetAction.Regenerate };
                break;
            case "proposal":
                reviewed = reviewed with { DesiredContent = Encoding.UTF8.GetBytes("Forged proposal.\n") };
                break;
            case "baseline":
                reviewed = reviewed with { CommittedContent = Encoding.UTF8.GetBytes("Forged baseline.\n") };
                break;
            case "source-snapshot":
                reviewed = reviewed with { Sources = reviewed.Sources.Select((source, index) => index == 0
                        ? source with { WorkingContent = Encoding.UTF8.GetBytes("Forged source.\n") }
                        : source).ToArray() };
                break;
            case "source-membership":
                reviewed = reviewed with { Sources = reviewed.Sources.Take(2).ToArray() };
                break;
            case "index":
                layout.Files.Write("notes.md", "Staged unrelated content.\n");
                layout.Files.Git("add", "--", "notes.md");
                break;
            case "target-ignored":
                reviewed = PreviewMissingTarget(layout, "codex");
                File.AppendAllText(Path.Combine(layout.Repository, ".git", "info", "exclude"),
                    reviewed.TargetRelativePath + "\n");
                break;
            case "other-overlay":
                layout.Files.Write("environment/providers/copilot/overlay.md", "Other overlay draft.\n");
                break;
            default:
                File.Delete(layout.Runtime("copilot"));
                break;
        }
        var before = Capture(layout);

        if (state == "context")
            Assert.Throws<InvalidDataException>(() => MigrationTargetWriter.Write(reviewed));
        else
            Assert.Throws<InvalidOperationException>(() => MigrationTargetWriter.Write(reviewed));

        AssertEntireFixtureUnchanged(layout, before);
    }

    [Fact]
    public void TargetCollisionAndDeletionAfterReviewAreRejected()
    {
        using (var collision = new Layout("codex", "copilot"))
        {
            var target = collision.Target("codex");
            RemoveAndCommitTarget(collision, target);
            var reviewed = Preview(collision, "codex");
            collision.Files.Write(target, "Unreviewed target collision.\n");
            var before = Capture(collision);

            Assert.Throws<InvalidOperationException>(() => MigrationTargetWriter.Write(reviewed));

            AssertEntireFixtureUnchanged(collision, before);
        }

        using (var deleted = new Layout("codex", "copilot"))
        {
            var reviewed = Preview(deleted, "codex");
            File.Delete(Path.Combine(deleted.Repository, reviewed.TargetRelativePath));
            var before = Capture(deleted);

            Assert.Throws<InvalidOperationException>(() => MigrationTargetWriter.Write(reviewed));

            AssertEntireFixtureUnchanged(deleted, before);
        }
    }

    [Fact]
    public void ReadOnlyTargetDirectoryFailureReportsAttemptAndRetainsState()
    {
        if (OperatingSystem.IsWindows()) return;
        using var layout = new Layout("codex", "copilot");
        var target = layout.Target("codex");
        RemoveAndCommitTarget(layout, target);
        var reviewed = Preview(layout, "codex");
        var targetDirectory = Path.GetDirectoryName(Path.Combine(layout.Repository, target))!;
        Directory.CreateDirectory(targetDirectory);
        var originalMode = File.GetUnixFileMode(targetDirectory);
        var before = Capture(layout);
        try
        {
            File.SetUnixFileMode(targetDirectory, UnixFileMode.UserRead | UnixFileMode.UserExecute);

            var error = Assert.Throws<MigrationTargetWriteException>(() => MigrationTargetWriter.Write(reviewed));

            Assert.Equal(target, error.AttemptedPath);
            Assert.Empty(error.CreatedDirectories);
            Assert.Contains("Inspect", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.False(File.Exists(Path.Combine(layout.Repository, target)));
            AssertEntireFixtureUnchanged(layout, before);
        }
        finally
        {
            File.SetUnixFileMode(targetDirectory, originalMode);
        }
    }

    [Theory]
    [InlineData("runtime")]
    [InlineData("other-source")]
    public void CurrentInputGuardAllowsOnlyTheReviewedWrittenTargetDifference(string drift)
    {
        using var layout = new Layout("codex", "copilot");
        var originalHead = layout.Files.Head();
        CommitSourceEdit(layout, "codex", "Reviewed revision.\n");
        var reviewed = Preview(layout, "codex", originalHead);
        var index = File.ReadAllBytes(Path.Combine(layout.Repository, ".git", "index"));
        var protectedFiles = MigrationSourceValidationPreview.ReadProtectedFiles(reviewed.Preparation);
        var targetPath = Path.Combine(layout.Repository, reviewed.TargetRelativePath);

        Assert.Equal(reviewed.TargetRelativePath, MigrationTargetWriter.Write(reviewed));

        var mode = OperatingSystem.IsWindows() ? (UnixFileMode?)null : File.GetUnixFileMode(targetPath);
        MigrationTargetWriter.VerifyCurrentInputs(reviewed, index, protectedFiles, written: true, expectedMode: mode);
        Assert.Throws<InvalidOperationException>(() =>
            MigrationDirtySections.Read(reviewed.Preparation.Preflight, reviewed.Preparation.Harnesses));
        var changedPath = drift == "runtime" ? layout.Runtime("copilot")
            : Path.Combine(layout.Repository, "environment/providers/copilot/overlay.md");
        File.AppendAllText(changedPath, "Other harness drift.\n");
        Assert.Throws<InvalidOperationException>(() => MigrationTargetWriter.VerifyCurrentInputs(
            reviewed, index, protectedFiles, written: true, expectedMode: mode));
    }

    private static MigrationTargetPreview Preview(Layout layout, string provider, string? original = null) =>
        MigrationTargetPreview.Read(layout.Context(original), provider);

    private static MigrationTargetPreview PreviewMissingTarget(Layout layout, string provider)
    {
        var target = layout.Target(provider);
        RemoveAndCommitTarget(layout, target);
        return Preview(layout, provider);
    }

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

    private static FixtureSnapshot Capture(Layout layout)
    {
        var files = Directory.EnumerateFiles(layout.Files.Root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);
        var directories = Directory.EnumerateDirectories(layout.Files.Root, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal).ToArray();
        var modes = new Dictionary<string, UnixFileMode>(StringComparer.Ordinal);
        if (!OperatingSystem.IsWindows())
        {
            foreach (var path in files.Keys) modes.Add(path, File.GetUnixFileMode(path));
        }
        return new FixtureSnapshot(files, directories, modes);
    }

    private static void AssertOnlyTargetChanged(Layout layout, FixtureSnapshot before, string target)
    {
        var targetPath = Path.Combine(layout.Repository, target);
        var after = Capture(layout);
        Assert.Contains(targetPath, after.Files.Keys);
        Assert.Equal(before.Files.Keys.Append(targetPath).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal),
            after.Files.Keys.Order(StringComparer.Ordinal));
        foreach (var (path, bytes) in before.Files)
        {
            if (path == targetPath) continue;
            Assert.Equal(bytes, after.Files[path]);
        }
        Assert.Equal(before.Directories, after.Directories.Intersect(before.Directories, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal).ToArray());
        foreach (var directory in after.Directories.Except(before.Directories, StringComparer.Ordinal))
            Assert.True(IsTargetParent(directory, targetPath), $"Unexpected directory created: {directory}");
        foreach (var (path, mode) in before.Modes)
        {
            if (path == targetPath) continue;
            Assert.Equal(mode, after.Modes[path]);
        }
    }

    private static void AssertEntireFixtureUnchanged(Layout layout, FixtureSnapshot before)
    {
        var after = Capture(layout);
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
