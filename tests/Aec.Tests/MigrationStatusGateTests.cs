using Layout = Aec.Tests.MigrationPortableCapturePreviewTests.Layout;

namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class MigrationStatusGateTests
{
    private static readonly string[] Both = ["codex", "copilot"];

    [Theory]
    [InlineData("codex")]
    [InlineData("copilot")]
    [InlineData("both")]
    public void InSyncEnrolledProvidersPassWithoutWriting(string selected)
    {
        using var layout = selected == "both" ? new Layout("codex", "copilot") : new Layout(selected);
        if (selected != "both") File.Delete(layout.Runtime(selected == "codex" ? "copilot" : "codex"));
        var context = layout.Context();
        layout.Files.AssertNoChanges(() =>
        {
            var result = MigrationStatusGate.Verify(context);
            if (selected == "both") Assert.Equal(Both, result.Select(item => item.Provider));
            else Assert.Equal(selected, Assert.Single(result).Provider);
            Assert.All(result, item => Assert.True(item.Status.InSync));
            Assert.False(File.Exists(Path.Combine(layout.Repository, MigrationPreflight.FormatRelativePath)));
        });
    }

    [Theory]
    [InlineData("instructions")]
    [InlineData("config-different")]
    [InlineData("config-missing")]
    public void DriftStopsWithoutChanges(string kind)
    {
        using var layout = new Layout("codex");
        File.Delete(layout.Runtime("copilot"));
        if (kind == "instructions") layout.EditRuntime("codex", "Provider mechanics.", "External edit.");
        else File.WriteAllText(layout.ConfigRuntime,
            kind == "config-different" ? "personality = 'pragmatic'\n" : "model = 'local'\n");
        var context = layout.Context();
        layout.Files.AssertNoChanges(() =>
        {
            var error = Assert.Throws<InvalidOperationException>(() => MigrationStatusGate.Verify(context));
            Assert.Contains("codex", error.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(kind == "config-missing" ? "missing" : "different", error.Message, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void UnmanagedModelSettingIsAccepted()
    {
        using var layout = new Layout("codex");
        File.Delete(layout.Runtime("copilot"));
        File.WriteAllText(layout.ConfigRuntime, "personality = 'friendly'\nmodel = 'keep-me'\n");
        var context = layout.Context();
        layout.Files.AssertNoChanges(() => Assert.True(Assert.Single(MigrationStatusGate.Verify(context)).Status.InSync));
    }

    [Theory]
    [InlineData("missing-runtime")]
    [InlineData("dirty-source")]
    [InlineData("stale-head")]
    public void StaleOrUncommittedInputsStopWithoutChanges(string kind)
    {
        using var layout = new Layout("codex", "copilot");
        var context = layout.Context();
        if (kind == "missing-runtime") File.Delete(layout.Runtime("codex"));
        else if (kind == "dirty-source") layout.Files.Write("environment/shared/instructions.md", "Uncommitted draft.\n");
        else layout.Files.Git("commit", "--allow-empty", "-m", "Later checkpoint");
        layout.Files.AssertNoChanges(() => Assert.Throws<InvalidOperationException>(() => MigrationStatusGate.Verify(context)));
    }

    [Theory]
    [InlineData("runtime")]
    [InlineData("head")]
    public void MutationDuringInspectionIsRejectedAndRetained(string kind)
    {
        using var layout = new Layout("codex", "copilot");
        var context = layout.Context();
        Dictionary<string, byte[]>? afterMutation = null;
        string[]? directoriesAfterMutation = null;
        Assert.Throws<InvalidOperationException>(() => MigrationStatusGate.Verify(context, provider =>
        {
            if (provider != "codex") return;
            if (kind == "runtime") File.AppendAllText(layout.Runtime("codex"), "External mutation.\n");
            else layout.Files.Git("commit", "--allow-empty", "-m", "Concurrent checkpoint");
            afterMutation = Snapshot(layout.Files.Root);
            directoriesAfterMutation = Directory.GetDirectories(layout.Files.Root, "*", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal).ToArray();
        }));
        Assert.NotNull(afterMutation);
        var after = Snapshot(layout.Files.Root);
        Assert.Equal(afterMutation.Keys.Order(StringComparer.Ordinal), after.Keys.Order(StringComparer.Ordinal));
        foreach (var (path, content) in afterMutation) Assert.Equal(content, after[path]);
        Assert.Equal(directoriesAfterMutation, Directory.GetDirectories(layout.Files.Root, "*", SearchOption.AllDirectories)
            .Order(StringComparer.Ordinal).ToArray());
        Assert.False(File.Exists(Path.Combine(layout.Repository, MigrationPreflight.FormatRelativePath)));
    }

    [Fact]
    public void ReplacementConfigViewCannotProveMigrationInSync()
    {
        using var layout = new Layout("codex", "copilot");
        var validCommit = layout.Files.Head();
        var relative = AecApplication.ConfigSourceRelativePath;
        var path = Path.Combine(layout.Repository, relative);
        var validBytes = File.ReadAllBytes(path);
        layout.Files.Write(relative, "personality = \"pragmatic\"\n");
        layout.Commit(relative);
        var actualCommit = layout.Files.Head();
        var context = layout.Context();
        var validTree = layout.Files.Git("rev-parse", validCommit + "^{tree}").Trim();
        var replacement = layout.Files.Git("commit-tree", validTree, "-p", validCommit,
            "-m", "Substituted config view").Trim();
        layout.Files.Git("replace", actualCommit, replacement);
        layout.Files.Git("read-tree", replacement);
        File.WriteAllBytes(path, validBytes);

        // Earlier provenance guards may reject first; the contract is refusal
        // without changing the substituted view, not a particular guard order.
        layout.Files.AssertNoChanges(() =>
            Assert.ThrowsAny<InvalidOperationException>(() => MigrationStatusGate.Verify(context)));
    }

    private static Dictionary<string, byte[]> Snapshot(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal);
}
