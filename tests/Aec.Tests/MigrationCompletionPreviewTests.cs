using Layout = Aec.Tests.MigrationPortableCapturePreviewTests.Layout;

namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class MigrationCompletionPreviewTests
{
    private static readonly string[] Both = ["codex", "copilot"];

    [Theory]
    [InlineData("both")]
    [InlineData("codex")]
    [InlineData("copilot")]
    public void InSyncProvidersReturnCompletionSnapshotWithoutWriting(string selected)
    {
        using var layout = selected == "both" ? new Layout("codex", "copilot") : new Layout(selected);
        if (selected != "both") File.Delete(layout.Runtime(selected == "codex" ? "copilot" : "codex"));
        var context = layout.Context();
        layout.Files.AssertNoChanges(() =>
        {
            var preview = MigrationCompletionPreview.Read(context);
            Assert.Equal(context.Preflight.Commit, preview.Preparation.Preflight.Commit);
            Assert.Equal("refs/heads/main", preview.Branch);
            if (selected == "both") Assert.Equal(Both, preview.Providers.Select(item => item.Provider));
            else Assert.Equal(selected, Assert.Single(preview.Providers).Provider);
            Assert.All(preview.Providers, item => Assert.True(item.Status.InSync));
            Assert.Equal("2.0\n", preview.MarkerContent);
            Assert.Equal(".aec-version", MigrationCompletionPreview.MarkerRelativePath);
            Assert.Equal("100644", MigrationCompletionPreview.MarkerGitMode);
            Assert.False(File.Exists(Path.Combine(layout.Repository, MigrationCompletionPreview.MarkerRelativePath)));
            Assert.Equal(File.ReadAllBytes(Path.Combine(layout.Repository, ".git", "index")), preview.Index);
            Assert.Equal(selected == "both" ? 4 : 3, preview.Sources.Count);
            Assert.All(preview.Sources, source =>
            {
                var bytes = File.ReadAllBytes(Path.Combine(layout.Repository, source.RelativePath));
                Assert.Equal(bytes, source.WorkingContent);
                Assert.Equal(bytes, source.CommittedContent);
                Assert.Equal("100644", source.CommittedMode);
            });
            var expected = MigrationSourceValidationPreview.ReadProtectedFiles(preview.Preparation);
            foreach (var harness in preview.Preparation.Harnesses)
            {
                var target = Path.Combine(layout.Repository, layout.Target(harness.Provider));
                expected.Add(target, File.ReadAllBytes(target));
            }
            Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), preview.ProtectedFiles.Keys.Order(StringComparer.Ordinal));
            foreach (var (path, bytes) in expected) Assert.Equal(bytes, preview.ProtectedFiles[path]);
        });
    }

    [Theory]
    [InlineData("config-drift")]
    [InlineData("stale-head")]
    [InlineData("marker")]
    public void UnsafeInputsStopWithoutChanges(string scenario)
    {
        using var layout = new Layout("codex", "copilot");
        var context = layout.Context();
        switch (scenario)
        {
            case "config-drift": File.WriteAllText(layout.ConfigRuntime, "personality = 'pragmatic'\n"); break;
            case "stale-head": layout.Files.Git("commit", "--allow-empty", "-m", "Later checkpoint"); break;
            case "marker": layout.Files.Write(MigrationPreflight.FormatRelativePath, "2.0\n"); break;
        }
        layout.Files.AssertNoChanges(() =>
            Assert.ThrowsAny<InvalidOperationException>(() => MigrationCompletionPreview.Read(context)));
    }

    [Theory]
    [InlineData("runtime")]
    [InlineData("head")]
    public void MutationDuringPreviewIsRejectedAndRetained(string scenario)
    {
        using var layout = new Layout("codex", "copilot");
        var context = layout.Context();
        Dictionary<string, byte[]>? afterMutation = null;
        string[]? directoriesAfterMutation = null;
        Assert.ThrowsAny<InvalidOperationException>(() => MigrationCompletionPreview.Read(context, () =>
        {
            if (scenario == "runtime") File.AppendAllText(layout.Runtime("codex"), "External mutation.\n");
            else layout.Files.Git("commit", "--allow-empty", "-m", "Concurrent checkpoint");
            afterMutation = Snapshot(layout.Files.Root);
            directoriesAfterMutation = Directories(layout.Files.Root);
        }));
        Assert.NotNull(afterMutation);
        var after = Snapshot(layout.Files.Root);
        Assert.Equal(afterMutation.Keys.Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
        foreach (var (path, bytes) in afterMutation) Assert.Equal(bytes, after[path]);
        Assert.Equal(directoriesAfterMutation, Directories(layout.Files.Root));
        Assert.False(File.Exists(Path.Combine(layout.Repository, MigrationPreflight.FormatRelativePath)));
    }

    private static Dictionary<string, byte[]> Snapshot(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);

    private static string[] Directories(string root) =>
        Directory.GetDirectories(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();
}
