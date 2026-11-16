using Layout = Aec.Tests.MigrationPortableCapturePreviewTests.Layout;

namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class MigrationCaptureCoordinatorTests
{
    [Fact]
    public void LegacyCaptureAdvancesPinnedCommitAndPreservesAuthoredDraft()
    {
        using var layout = new Layout();
        var original = layout.Files.Head();
        layout.Files.Git("commit", "--allow-empty", "-m", "Retrieved checkpoint");
        File.AppendAllText(layout.Runtime("codex"), "Legacy runtime revision.\n");
        const string draft = "environment/shared/instructions.md";
        layout.Files.Write(draft, "Preserve this draft.\n");
        var prepared = layout.Context(original);

        var captured = AssertCapture(layout, prepared, AecApplication.SourceRelativePath);

        Assert.Equal(MigrationSectionChoice.Preserve, captured.SectionChoice);
        Assert.Equal(new[] { draft }, captured.DirtySections.Paths);
        Assert.Equal("Preserve this draft.\n", File.ReadAllText(Path.Combine(layout.Repository, draft)));
        Assert.Contains("Legacy runtime revision.", layout.Files.Git("show", $"HEAD:{AecApplication.SourceRelativePath}"));
    }

    [Fact]
    public void PortableCaptureCommitsBothProvidersAgainstThePreparedParent()
    {
        using var layout = new Layout("codex", "copilot");
        var original = layout.Files.Git("rev-parse", "HEAD^").Trim();
        layout.EditRuntime("codex", "Provider mechanics.", "Codex revision.");
        layout.EditRuntime("copilot", "Provider mechanics.", "Copilot revision.");

        AssertCapture(layout, layout.Context(original),
            "environment/providers/codex/overlay.md", "environment/providers/copilot/overlay.md",
            layout.Target("codex"), layout.Target("copilot"));

        Assert.Equal("Codex revision.\n", layout.Files.Git("show", "HEAD:environment/providers/codex/overlay.md"));
        Assert.Equal("Copilot revision.\n", layout.Files.Git("show", "HEAD:environment/providers/copilot/overlay.md"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoopRetainsThePreparedContextAndAllFixtureBytes(bool portable)
    {
        using var layout = portable ? new Layout("codex", "copilot") : new Layout();
        var prepared = layout.Context();
        layout.Files.AssertNoChanges(() => Assert.Same(prepared, MigrationCaptureCoordinator.Capture(prepared)));
    }

    [Theory]
    [InlineData("codex", "copilot")]
    [InlineData("copilot", "codex")]
    public void MixedFormatsIdentifyBothProvidersAndLeaveTheFixtureUntouched(string portable, string legacy)
    {
        using var layout = new Layout(portable);
        layout.EditRuntime(portable, "Provider mechanics.", "Pending portable revision.");
        File.AppendAllText(layout.Runtime(legacy), "Pending legacy revision.\n");
        var prepared = layout.Context();

        layout.Files.AssertNoChanges(() =>
        {
            var error = Assert.Throws<InvalidOperationException>(() => MigrationCaptureCoordinator.Capture(prepared));
            Assert.Contains($"{portable} (portable)", error.Message);
            Assert.Contains($"{legacy} (legacy)", error.Message);
        });
    }

    [Fact]
    public void StalePreparationCannotCaptureAgainstAnOutsideCommit()
    {
        using var layout = new Layout();
        File.AppendAllText(layout.Runtime("codex"), "Pending legacy revision.\n");
        var prepared = layout.Context();
        layout.Files.Git("commit", "--allow-empty", "-m", "Outside commit");

        layout.Files.AssertNoChanges(() =>
            Assert.Throws<InvalidOperationException>(() => MigrationCaptureCoordinator.Capture(prepared)));
    }

    private static MigrationPreparation AssertCapture(Layout layout, MigrationPreparation prepared,
        params string[] expectedPaths)
    {
        var runtime = layout.Files.RuntimeBytes();
        var remote = layout.Files.Git("rev-parse", "refs/remotes/shared/main");

        var captured = MigrationCaptureCoordinator.Capture(prepared);

        var commit = layout.Files.Head();
        Assert.Equal(prepared with { Preflight = prepared.Preflight with { Commit = commit } }, captured);
        Assert.Equal(prepared.Preflight.Commit, layout.Files.Git("rev-parse", "HEAD^").Trim());
        Assert.Equal(expectedPaths.Order(StringComparer.Ordinal),
            layout.Files.Git("diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal));
        Assert.Equal(remote, layout.Files.Git("rev-parse", "refs/remotes/shared/main"));
        Assert.Empty(layout.Files.Git("diff", "--cached", "--name-only"));
        Assert.False(File.Exists(Path.Combine(layout.Repository, MigrationPreflight.FormatRelativePath)));
        layout.Files.AssertRuntime(runtime);
        return captured;
    }
}
