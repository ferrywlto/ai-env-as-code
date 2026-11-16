namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class MigrationApplyCoordinatorTests
{
    private static readonly string[] ProviderOrder = ["codex", "copilot"];
    private static readonly string[] CodexOnly = ["codex"];

    [Theory]
    [InlineData("codex")]
    [InlineData("copilot")]
    public void AppliesSingleEnrolledProvider(string provider)
    {
        using var layout = new MigrationPortableCapturePreviewTests.Layout(provider);
        File.Delete(layout.Runtime(provider == "codex" ? "copilot" : "codex"));
        RestoreLegacy(layout, provider);
        var preview = MigrationApplyPreview.Read(layout.Context(), provider);
        var before = Snapshot(layout.Files.Root);
        var calls = new List<string>();

        var result = MigrationApplyCoordinator.Apply([preview], calls.Add);

        var item = Assert.Single(result);
        Assert.Equal(provider, item.Provider);
        Assert.Equal("applied", item.Result);
        Assert.Equal(new[] { provider }, calls);
        Assert.Equal(preview.Apply.DesiredRuntime, File.ReadAllBytes(layout.Runtime(provider)));
        AssertOnlyChanges(layout.Files.Root, before, layout.Runtime(provider));
    }

    [Fact]
    public void AppliesBothInProviderOrderDespiteReversedSeparateReviews()
    {
        using var layout = new MigrationPortableCapturePreviewTests.Layout("codex", "copilot");
        RestoreLegacy(layout, "codex");
        RestoreLegacy(layout, "copilot");
        File.WriteAllText(layout.ConfigRuntime, "personality = \"pragmatic\"\nmodel = \"keep-me\"\n");
        // Separate reads deliberately produce equivalent, not reference-equal,
        // preparation collections. They still describe the same approved flow.
        var codex = MigrationApplyPreview.Read(layout.Context(), "codex");
        var copilot = MigrationApplyPreview.Read(layout.Context(), "copilot");
        var before = Snapshot(layout.Files.Root);
        var calls = new List<string>();

        var result = MigrationApplyCoordinator.Apply([copilot, codex], calls.Add);

        Assert.Equal(ProviderOrder, result.Select(item => item.Provider));
        Assert.All(result, item => Assert.Equal("applied", item.Result));
        Assert.Equal(ProviderOrder, calls);
        Assert.Equal(codex.Apply.DesiredRuntime, File.ReadAllBytes(layout.Runtime("codex")));
        Assert.Equal(codex.Apply.CodexConfig!.DesiredRuntime, File.ReadAllBytes(layout.ConfigRuntime));
        Assert.Equal(copilot.Apply.DesiredRuntime, File.ReadAllBytes(layout.Runtime("copilot")));
        Assert.Contains("model = \"keep-me\"", File.ReadAllText(layout.ConfigRuntime), StringComparison.Ordinal);
        AssertOnlyChanges(layout.Files.Root, before,
            layout.Runtime("codex"), layout.ConfigRuntime, layout.Runtime("copilot"));
    }

    [Fact]
    public void BothUnchangedStillCheckProviderBoundariesAndPreserveEverything()
    {
        using var layout = new MigrationPortableCapturePreviewTests.Layout("codex", "copilot");
        var context = layout.Context();
        var reviews = new[] { MigrationApplyPreview.Read(context, "copilot"), MigrationApplyPreview.Read(context, "codex") };
        var calls = new List<string>();

        layout.Files.AssertNoChanges(() =>
        {
            var result = MigrationApplyCoordinator.Apply(reviews, calls.Add);
            Assert.Equal(ProviderOrder, result.Select(item => item.Provider));
            Assert.All(result, item => Assert.Equal("unchanged", item.Result));
        });
        Assert.Equal(ProviderOrder, calls);
    }

    [Fact]
    public void EmptyReviewSetIsRejected() =>
        Assert.Throws<ArgumentException>(() => MigrationApplyCoordinator.Apply([]));

    [Theory]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("preparation")]
    [InlineData("unsupported")]
    public void InvalidReviewSetIsRejectedWithoutChanges(string kind)
    {
        using var layout = new MigrationPortableCapturePreviewTests.Layout("codex", "copilot");
        var context = layout.Context();
        var codex = MigrationApplyPreview.Read(context, "codex");
        var copilot = MigrationApplyPreview.Read(context, "copilot");
        MigrationApplyPreview[] reviews = kind switch
        {
            "missing" => [codex],
            "duplicate" => [codex, codex],
            "preparation" => [codex, copilot with { Preparation = context with { OriginalCommit = "different" } }],
            _ => [codex, copilot with { Harness = copilot.Harness with { Provider = "other" } }]
        };

        layout.Files.AssertNoChanges(() =>
            Assert.Throws<ArgumentException>(() => MigrationApplyCoordinator.Apply(reviews)));
    }

    [Fact]
    public void StaleSecondProviderStopsBeforeFirstWrite()
    {
        using var layout = new MigrationPortableCapturePreviewTests.Layout("codex", "copilot");
        RestoreLegacy(layout, "codex");
        RestoreLegacy(layout, "copilot");
        var context = layout.Context();
        var codex = MigrationApplyPreview.Read(context, "codex");
        var copilot = MigrationApplyPreview.Read(context, "copilot");
        File.AppendAllText(layout.Runtime("copilot"), "External change.\n");

        layout.Files.AssertNoChanges(() =>
            Assert.Throws<InvalidOperationException>(() => MigrationApplyCoordinator.Apply([codex, copilot])));
    }

    internal static void DriftAfterFirstProviderStopsSecondAndReportsPartialState(string mutation)
    {
        using var layout = new MigrationPortableCapturePreviewTests.Layout("codex", "copilot");
        RestoreLegacy(layout, "codex");
        RestoreLegacy(layout, "copilot");
        var context = layout.Context();
        var codex = MigrationApplyPreview.Read(context, "codex");
        var copilot = MigrationApplyPreview.Read(context, "copilot");
        var copilotBefore = File.ReadAllText(layout.Runtime("copilot"));
        var before = Snapshot(layout.Files.Root);
        var calls = new List<string>();
        const string externalChange = "External change.\n";
        var mutatedPath = mutation switch
        {
            "source" => Path.Combine(layout.Repository, "environment/shared/instructions.md"),
            "target" => Path.Combine(layout.Repository, layout.Target("copilot")),
            _ => layout.Runtime(mutation)
        };

        var error = Assert.Throws<InvalidOperationException>(() =>
            MigrationApplyCoordinator.Apply([copilot, codex], provider =>
            {
                calls.Add(provider);
                if (mutation == "exception") throw new IOException("Injected boundary failure");
                File.AppendAllText(mutatedPath, externalChange);
            }));

        Assert.Equal(CodexOnly, calls);
        Assert.Contains("Completed providers: codex (applied)", error.Message, StringComparison.Ordinal);
        Assert.Contains("may be updated", error.Message, StringComparison.Ordinal);
        Assert.Contains("No automatic rollback", error.Message, StringComparison.Ordinal);
        Assert.Contains(layout.Runtime("codex"), error.Message, StringComparison.Ordinal);
        Assert.Contains(layout.Runtime("copilot"), error.Message, StringComparison.Ordinal);
        Assert.Equal(copilotBefore + (mutation == "copilot" ? externalChange : ""),
            File.ReadAllText(layout.Runtime("copilot")));
        Assert.Equal(System.Text.Encoding.UTF8.GetString(codex.Apply.DesiredRuntime) +
            (mutation == "codex" ? externalChange : ""), File.ReadAllText(layout.Runtime("codex")));
        var allowed = mutation == "exception"
            ? new[] { layout.Runtime("codex") }
            : new[] { layout.Runtime("codex"), mutatedPath };
        AssertOnlyChanges(layout.Files.Root, before, allowed);
    }

    private static void RestoreLegacy(MigrationPortableCapturePreviewTests.Layout layout, string provider) =>
        File.WriteAllBytes(layout.Runtime(provider), File.ReadAllBytes(Path.Combine(layout.Repository,
            provider == "codex" ? AecApplication.SourceRelativePath : AecApplication.CopilotSourceRelativePath)));

    private static (Dictionary<string, byte[]> Files, string[] Directories) Snapshot(string root) =>
        (Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .ToDictionary(path => path, File.ReadAllBytes, StringComparer.Ordinal),
         Directory.GetDirectories(root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray());

    private static void AssertOnlyChanges(string root,
        (Dictionary<string, byte[]> Files, string[] Directories) before, params string[] allowed)
    {
        var after = Snapshot(root);
        Assert.Equal(before.Directories, after.Directories);
        Assert.Equal(before.Files.Keys.Order(StringComparer.Ordinal), after.Files.Keys.Order(StringComparer.Ordinal));
        foreach (var (path, bytes) in before.Files)
        {
            if (!allowed.Contains(path, StringComparer.Ordinal)) Assert.Equal(bytes, after.Files[path]);
        }
    }
}
