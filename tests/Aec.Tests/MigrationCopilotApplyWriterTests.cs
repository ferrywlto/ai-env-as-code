using System.Text;

namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class MigrationCopilotApplyWriterTests
{
    [Fact]
    public void AppliesLegacyRuntimeAndChangesOnlySelectedFile()
    {
        using var layout = new MigrationPortableCapturePreviewTests.Layout("codex", "copilot");
        RestoreLegacyCopilot(layout);
        var preview = MigrationApplyPreview.Read(layout.Context(), "copilot");
        Assert.True(preview.Apply.WouldReplace);
        var runtime = layout.Runtime("copilot");
        UnixFileMode? mode = OperatingSystem.IsWindows() ? null : File.GetUnixFileMode(runtime);
        var before = Snapshot(layout.Files.Root);

        Assert.Equal("applied", MigrationCopilotApplyWriter.Apply(preview));

        Assert.Equal(preview.Apply.DesiredRuntime, File.ReadAllBytes(runtime));
        AssertOnlyRuntimeChanged(layout.Files.Root, before, runtime);
        if (!OperatingSystem.IsWindows()) Assert.Equal(mode, File.GetUnixFileMode(runtime));
    }

    [Fact]
    public void PortableRuntimeIsUnchangedAndDoesNotInvokeWriteHook()
    {
        using var layout = new MigrationPortableCapturePreviewTests.Layout("codex", "copilot");
        var preview = MigrationApplyPreview.Read(layout.Context(), "copilot");
        Assert.False(preview.Apply.WouldReplace);
        var hookCalled = false;

        layout.Files.AssertNoChanges(() =>
            Assert.Equal("unchanged", MigrationCopilotApplyWriter.Apply(preview, () => hookCalled = true)));
        Assert.False(hookCalled);
    }

    [Theory]
    [InlineData("head")]
    [InlineData("branch")]
    [InlineData("source")]
    [InlineData("target")]
    [InlineData("runtime")]
    [InlineData("marker")]
    public void StaleReviewIsRejectedWithoutChangingFixture(string mutation)
    {
        using var layout = new MigrationPortableCapturePreviewTests.Layout("codex", "copilot");
        RestoreLegacyCopilot(layout);
        var preview = MigrationApplyPreview.Read(layout.Context(), "copilot");
        switch (mutation)
        {
            case "head":
                layout.Files.Git("commit", "--allow-empty", "-m", "Later checkpoint");
                break;
            case "branch":
                layout.Files.Git("switch", "-c", "another-branch");
                break;
            case "source":
                layout.Files.Write("environment/shared/instructions.md", "Unreviewed source.\n");
                break;
            case "target":
                layout.Files.Write(layout.Target("copilot"), "Unreviewed target.\n");
                break;
            case "runtime":
                File.AppendAllText(layout.Runtime("copilot"), "Unreviewed runtime edit.\n");
                break;
            case "marker":
                layout.Files.Write(MigrationPreflight.FormatRelativePath, "2.0\n");
                break;
        }

        layout.Files.AssertNoChanges(() =>
            Assert.Throws<InvalidOperationException>(() => MigrationCopilotApplyWriter.Apply(preview)));
    }

    [Fact]
    public void RejectsCodexPreviewWithoutChanges()
    {
        using var layout = new MigrationPortableCapturePreviewTests.Layout("codex", "copilot");
        var preview = MigrationApplyPreview.Read(layout.Context(), "codex");

        layout.Files.AssertNoChanges(() =>
            Assert.Throws<InvalidOperationException>(() => MigrationCopilotApplyWriter.Apply(preview)));
    }

    [Theory]
    [InlineData("desired")]
    [InlineData("source")]
    public void RejectsCallerModifiedReviewBuffers(string buffer)
    {
        using var layout = new MigrationPortableCapturePreviewTests.Layout("codex", "copilot");
        RestoreLegacyCopilot(layout);
        var preview = MigrationApplyPreview.Read(layout.Context(), "copilot");
        if (buffer == "desired") preview.Apply.DesiredRuntime[0] ^= 1;
        else preview.RequiredSources[0].WorkingContent[0] ^= 1;

        layout.Files.AssertNoChanges(() =>
            Assert.Throws<InvalidOperationException>(() => MigrationCopilotApplyWriter.Apply(preview)));
    }

    [Fact]
    public void PostWriteDriftReportsPossibleUpdateWithoutRollback()
    {
        using var layout = new MigrationPortableCapturePreviewTests.Layout("codex", "copilot");
        RestoreLegacyCopilot(layout);
        var preview = MigrationApplyPreview.Read(layout.Context(), "copilot");
        var runtime = layout.Runtime("copilot");
        var externalBytes = Encoding.UTF8.GetBytes("External edit after replacement.\n");
        var before = Snapshot(layout.Files.Root);
        var hookCalled = false;

        var error = Assert.Throws<InvalidOperationException>(() =>
            MigrationCopilotApplyWriter.Apply(preview, () =>
            {
                hookCalled = true;
                File.WriteAllBytes(runtime, externalBytes);
            }));

        Assert.True(hookCalled);
        Assert.Contains("may", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rollback", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(externalBytes, File.ReadAllBytes(runtime));
        AssertOnlyRuntimeChanged(layout.Files.Root, before, runtime);
    }

    private static void RestoreLegacyCopilot(MigrationPortableCapturePreviewTests.Layout layout) =>
        File.WriteAllBytes(layout.Runtime("copilot"),
            File.ReadAllBytes(Path.Combine(layout.Repository, AecApplication.CopilotSourceRelativePath)));

    private static (Dictionary<string, byte[]> Files, string[] Directories) Snapshot(string root) =>
        (Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal),
         Directory.GetDirectories(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray());

    private static void AssertOnlyRuntimeChanged(string root,
        (Dictionary<string, byte[]> Files, string[] Directories) before, string runtime)
    {
        var after = Snapshot(root);
        Assert.Equal(before.Files.Keys.Order(StringComparer.Ordinal), after.Files.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(before.Directories, after.Directories);
        foreach (var (path, bytes) in before.Files)
        {
            if (path == runtime) Assert.False(bytes.AsSpan().SequenceEqual(after.Files[path]));
            else Assert.Equal(bytes, after.Files[path]);
        }
    }
}
