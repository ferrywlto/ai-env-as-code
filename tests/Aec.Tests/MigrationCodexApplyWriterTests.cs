using System.Text;

namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class MigrationCodexApplyWriterTests
{
    [Theory]
    [InlineData(true, true)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void AppliesOnlyRequiredCodexFiles(bool instructionsChange, bool configChanges)
    {
        using var layout = new MigrationPortableCapturePreviewTests.Layout("codex", "copilot");
        if (instructionsChange) RestoreLegacyCodex(layout);
        if (configChanges) File.WriteAllText(layout.ConfigRuntime, "personality = \"pragmatic\"\nmodel = \"keep-me\"\n");
        var preview = MigrationApplyPreview.Read(layout.Context(), "codex");
        var config = Assert.IsType<PortableCodexConfigPreview>(preview.Apply.CodexConfig);
        Assert.Equal(instructionsChange, preview.Apply.WouldReplace);
        Assert.Equal(configChanges, config.WouldReplace);
        var before = Snapshot(layout.Files.Root);
        var afterConfigCalled = false;
        var afterWriteCalled = false;

        var result = MigrationCodexApplyWriter.Apply(preview,
            () => afterConfigCalled = true, () => afterWriteCalled = true);

        Assert.Equal(instructionsChange || configChanges ? "applied" : "unchanged", result);
        Assert.Equal(configChanges, afterConfigCalled);
        Assert.Equal(instructionsChange, afterWriteCalled);
        Assert.Equal(preview.Apply.DesiredRuntime, File.ReadAllBytes(layout.Runtime("codex")));
        Assert.Equal(config.DesiredRuntime, File.ReadAllBytes(layout.ConfigRuntime));
        if (configChanges) Assert.Contains("model = \"keep-me\"", File.ReadAllText(layout.ConfigRuntime), StringComparison.Ordinal);
        var allowed = new List<string>();
        if (instructionsChange) allowed.Add(layout.Runtime("codex"));
        if (configChanges) allowed.Add(layout.ConfigRuntime);
        AssertOnlyFilesChanged(layout.Files.Root, before, allowed);
    }

    [Fact]
    public void InsertsMissingConfigWithPrivateUnixPermissions()
    {
        using var layout = new MigrationPortableCapturePreviewTests.Layout("codex", "copilot");
        File.Delete(layout.ConfigRuntime);
        var preview = MigrationApplyPreview.Read(layout.Context(), "codex");
        var config = Assert.IsType<PortableCodexConfigPreview>(preview.Apply.CodexConfig);
        Assert.True(config.Inserted);
        var before = Snapshot(layout.Files.Root);

        Assert.Equal("applied", MigrationCodexApplyWriter.Apply(preview));

        Assert.Equal(config.DesiredRuntime, File.ReadAllBytes(layout.ConfigRuntime));
        if (!OperatingSystem.IsWindows())
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(layout.ConfigRuntime));
        AssertOnlyFilesChanged(layout.Files.Root, before, [layout.ConfigRuntime]);
    }

    [Theory]
    [InlineData("config")]
    [InlineData("branch")]
    [InlineData("head")]
    [InlineData("source")]
    public void StaleReviewIsRejectedWithoutChanges(string mutation)
    {
        using var layout = new MigrationPortableCapturePreviewTests.Layout("codex", "copilot");
        RestoreLegacyCodex(layout);
        var preview = MigrationApplyPreview.Read(layout.Context(), "codex");
        switch (mutation)
        {
            case "config": File.AppendAllText(layout.ConfigRuntime, "model = \"edited\"\n"); break;
            case "branch": layout.Files.Git("switch", "-c", "stale-review"); break;
            case "head": layout.Files.Git("commit", "--allow-empty", "-m", "Later checkpoint"); break;
            case "source": layout.Files.Write("environment/shared/instructions.md", "Unreviewed source.\n"); break;
        }

        layout.Files.AssertNoChanges(() =>
            Assert.Throws<InvalidOperationException>(() => MigrationCodexApplyWriter.Apply(preview)));
    }

    [Fact]
    public void RejectsModifiedReviewedConfigBuffer()
    {
        using var layout = new MigrationPortableCapturePreviewTests.Layout("codex", "copilot");
        File.WriteAllText(layout.ConfigRuntime, "personality = \"pragmatic\"\n");
        var preview = MigrationApplyPreview.Read(layout.Context(), "codex");
        preview.Apply.CodexConfig!.DesiredRuntime[0] ^= 1;

        layout.Files.AssertNoChanges(() =>
            Assert.Throws<InvalidOperationException>(() => MigrationCodexApplyWriter.Apply(preview)));
    }

    [Fact]
    public void RejectsCopilotPreviewWithoutChanges()
    {
        using var layout = new MigrationPortableCapturePreviewTests.Layout("codex", "copilot");
        var preview = MigrationApplyPreview.Read(layout.Context(), "copilot");

        layout.Files.AssertNoChanges(() =>
            Assert.Throws<ArgumentException>(() => MigrationCodexApplyWriter.Apply(preview)));
    }

    [Fact]
    public void RejectsPreviewMissingCodexConfigWithoutChanges()
    {
        using var layout = new MigrationPortableCapturePreviewTests.Layout("codex", "copilot");
        var preview = MigrationApplyPreview.Read(layout.Context(), "codex");
        preview = preview with { Apply = preview.Apply with { CodexConfig = null } };

        layout.Files.AssertNoChanges(() =>
            Assert.Throws<ArgumentException>(() => MigrationCodexApplyWriter.Apply(preview)));
    }

    [Theory]
    [InlineData("branch")]
    [InlineData("source")]
    public void DriftAfterConfigStopsInstructionsWithoutRollback(string mutation)
    {
        using var layout = new MigrationPortableCapturePreviewTests.Layout("codex", "copilot");
        RestoreLegacyCodex(layout);
        File.WriteAllText(layout.ConfigRuntime, "personality = \"pragmatic\"\n");
        var preview = MigrationApplyPreview.Read(layout.Context(), "codex");
        var before = Snapshot(layout.Files.Root);
        (Dictionary<string, byte[]> Files, string[] Directories)? afterExternalEdit = null;
        var instructionsHookCalled = false;

        var error = Assert.Throws<InvalidOperationException>(() => MigrationCodexApplyWriter.Apply(preview, () =>
        {
            if (mutation == "branch")
            {
                layout.Files.Git("branch", "during-apply");
                layout.Files.Git("symbolic-ref", "HEAD", "refs/heads/during-apply");
            }
            else layout.Files.Write("environment/shared/instructions.md", "External edit.\n");
            afterExternalEdit = Snapshot(layout.Files.Root);
        }, () => instructionsHookCalled = true));

        AssertPartialWarning(error);
        Assert.False(instructionsHookCalled);
        Assert.Equal(preview.Apply.CodexConfig!.DesiredRuntime, File.ReadAllBytes(layout.ConfigRuntime));
        Assert.Equal(before.Files[layout.Runtime("codex")], File.ReadAllBytes(layout.Runtime("codex")));
        var afterEdit = afterExternalEdit ?? throw new InvalidOperationException("Config hook was not called.");
        AssertOnlyFilesChanged(layout.Files.Root, afterEdit, []);
        var allowed = new List<string> { layout.ConfigRuntime };
        if (mutation == "source") allowed.Add(Path.Combine(layout.Repository, "environment/shared/instructions.md"));
        else
        {
            allowed.Add(Path.Combine(layout.Repository, ".git", "HEAD"));
            allowed.Add(Path.Combine(layout.Repository, ".git", "logs", "HEAD"));
            allowed.Add(Path.Combine(layout.Repository, ".git", "refs", "heads", "during-apply"));
            allowed.Add(Path.Combine(layout.Repository, ".git", "logs", "refs", "heads", "during-apply"));
        }
        AssertOnlyFilesChanged(layout.Files.Root, before, allowed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GuardReadFailureAfterConfigReportsPartialUpdate(bool unauthorized)
    {
        using var layout = new MigrationPortableCapturePreviewTests.Layout("codex", "copilot");
        RestoreLegacyCodex(layout);
        File.WriteAllText(layout.ConfigRuntime, "personality = \"pragmatic\"\n");
        var preview = MigrationApplyPreview.Read(layout.Context(), "codex");
        var before = Snapshot(layout.Files.Root);

        var error = Assert.Throws<InvalidOperationException>(() => MigrationCodexApplyWriter.Apply(preview,
            () =>
            {
                if (unauthorized) throw new UnauthorizedAccessException("Guard read failed.");
                throw new ArgumentException("Guard read failed.");
            }));

        AssertPartialWarning(error);
        Assert.Equal(preview.Apply.CodexConfig!.DesiredRuntime, File.ReadAllBytes(layout.ConfigRuntime));
        Assert.Equal(before.Files[layout.Runtime("codex")], File.ReadAllBytes(layout.Runtime("codex")));
        AssertOnlyFilesChanged(layout.Files.Root, before, [layout.ConfigRuntime]);
    }

    [Fact]
    public void PostWriteDriftReportsPossibleUpdateWithoutRollback()
    {
        using var layout = new MigrationPortableCapturePreviewTests.Layout("codex", "copilot");
        RestoreLegacyCodex(layout);
        var preview = MigrationApplyPreview.Read(layout.Context(), "codex");
        var runtime = layout.Runtime("codex");
        var externalBytes = Encoding.UTF8.GetBytes("External edit after replacement.\n");
        var before = Snapshot(layout.Files.Root);
        var hookCalled = false;

        var error = Assert.Throws<InvalidOperationException>(() => MigrationCodexApplyWriter.Apply(preview,
            afterWriteForTests: () => { hookCalled = true; File.WriteAllBytes(runtime, externalBytes); }));

        Assert.True(hookCalled);
        AssertPartialWarning(error);
        Assert.Equal(externalBytes, File.ReadAllBytes(runtime));
        AssertOnlyFilesChanged(layout.Files.Root, before, [runtime]);
    }

    private static void AssertPartialWarning(Exception error)
    {
        Assert.Contains("may", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rollback", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static void RestoreLegacyCodex(MigrationPortableCapturePreviewTests.Layout layout) =>
        File.WriteAllBytes(layout.Runtime("codex"),
            File.ReadAllBytes(Path.Combine(layout.Repository, AecApplication.SourceRelativePath)));

    private static (Dictionary<string, byte[]> Files, string[] Directories) Snapshot(string root) =>
        (Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal),
         Directory.GetDirectories(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray());

    private static void AssertOnlyFilesChanged(string root,
        (Dictionary<string, byte[]> Files, string[] Directories) before, IReadOnlyList<string> allowed)
    {
        var after = Snapshot(root);
        Assert.Equal(before.Directories, after.Directories);
        var paths = before.Files.Keys.Union(after.Files.Keys, StringComparer.Ordinal);
        foreach (var path in paths)
        {
            if (allowed.Contains(path, StringComparer.Ordinal)) continue;
            Assert.True(before.Files.ContainsKey(path), $"Unexpected new file: {path}");
            Assert.True(after.Files.ContainsKey(path), $"Unexpected removed file: {path}");
            Assert.Equal(before.Files[path], after.Files[path]);
        }
    }
}
