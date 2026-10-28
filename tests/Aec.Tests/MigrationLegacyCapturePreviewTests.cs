using System.Text;

namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class MigrationLegacyCapturePreviewTests
{
    [Theory]
    [InlineData("codex", "unchanged")]
    [InlineData("copilot", "unchanged")]
    [InlineData("codex", "runtime-only")]
    [InlineData("copilot", "runtime-only")]
    [InlineData("codex", "pulled-match")]
    [InlineData("copilot", "pulled-match")]
    [InlineData("codex", "pulled-stale")]
    [InlineData("copilot", "pulled-stale")]
    [InlineData("codex", "both-differ")]
    [InlineData("copilot", "both-differ")]
    public void InstructionPreviewUsesExactThreeWayBytesWithoutWrites(string provider, string scenario)
    {
        using var layout = new MigrationRepositoryFixture(includeCopilotCanonical: true);
        var original = layout.Head();
        var relative = Source(provider);
        var canonicalPath = Path.Combine(layout.Repository, relative);
        var runtimePath = Runtime(layout, provider);
        if (scenario.StartsWith("pulled", StringComparison.Ordinal) || scenario == "both-differ")
        {
            File.AppendAllText(canonicalPath, "Retrieved authored change.\n");
            Commit(layout, relative);
        }
        if (scenario == "pulled-match") File.WriteAllBytes(runtimePath, File.ReadAllBytes(canonicalPath));
        if (scenario is "runtime-only" or "both-differ") File.AppendAllText(runtimePath, "Runtime authored change.\n");
        var context = Context(layout, original);

        layout.AssertNoChanges(() =>
        {
            if (scenario is "pulled-stale" or "both-differ")
            {
                var error = Assert.Throws<InvalidOperationException>(() => MigrationLegacyCapturePreview.Read(context));
                Assert.Contains("Reconcile", error.Message, StringComparison.Ordinal);
                return;
            }
            var preview = MigrationLegacyCapturePreview.Read(context);
            var file = Assert.Single(preview.Files, file => file.RelativePath == relative);
            Assert.Equal(scenario == "runtime-only", file.HasChanges);
            Assert.Equal(scenario == "runtime-only", preview.HasChanges);
            Assert.Equal(File.ReadAllBytes(runtimePath), file.ProposedCanonical);
            Assert.Equal(3, preview.Files.Count);
        });
    }

    [Theory]
    [InlineData("runtime-only")]
    [InlineData("format-only")]
    [InlineData("pulled-match")]
    [InlineData("pulled-stale")]
    [InlineData("both-differ")]
    [InlineData("unmanaged-only")]
    public void ConfigPreviewComparesManagedValueAndKeepsRetrievedFormatting(string scenario)
    {
        using var layout = new MigrationRepositoryFixture(includeCopilotCanonical: true);
        var original = layout.Head();
        var canonicalPath = Path.Combine(layout.Repository, AecApplication.ConfigSourceRelativePath);
        var runtimePath = Path.Combine(Path.GetDirectoryName(Runtime(layout, "codex"))!, "config.toml");
        if (scenario is not ("runtime-only" or "unmanaged-only"))
        {
            var value = scenario == "format-only" ? "friendly" : "pragmatic";
            File.WriteAllText(canonicalPath, $"# Retrieved comment\npersonality = '{value}' # Keep spacing\n");
            Commit(layout, AecApplication.ConfigSourceRelativePath);
        }
        var runtimeValue = scenario is "pulled-stale" or "unmanaged-only" ? "friendly"
            : scenario == "both-differ" ? "none" : "pragmatic";
        File.WriteAllText(runtimePath, $"personality = \"{runtimeValue}\"\nmodel = \"machine-only-model\"\n");
        var context = Context(layout, original);

        layout.AssertNoChanges(() =>
        {
            if (scenario is "pulled-stale" or "both-differ")
            {
                Assert.Throws<InvalidOperationException>(() => MigrationLegacyCapturePreview.Read(context));
                return;
            }
            var preview = MigrationLegacyCapturePreview.Read(context);
            var file = Assert.Single(preview.Files, file => file.RelativePath == AecApplication.ConfigSourceRelativePath);
            Assert.Equal(scenario is "runtime-only" or "format-only", file.HasChanges);
            var proposed = Encoding.UTF8.GetString(file.ProposedCanonical);
            Assert.DoesNotContain("machine-only-model", proposed, StringComparison.Ordinal);
            if (scenario == "format-only") Assert.Equal("# Retrieved comment\npersonality = \"pragmatic\" # Keep spacing\n", proposed);
            if (!file.HasChanges) Assert.Equal(File.ReadAllBytes(canonicalPath), file.ProposedCanonical);
        });
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("copilot")]
    public void PortableRuntimeIsNeverProposedForLegacyWholeFileCapture(string provider)
    {
        using var layout = new MigrationRepositoryFixture(includeCopilotCanonical: true);
        var body = Encoding.UTF8.GetBytes("Portable generated runtime.\n");
        var portable = provider == "codex"
            ? AecInstructionBlock.MergeForPortableTarget(body, layout.Repository, RenderCommand.CurrentPlatform(), false)
            : CopilotInstructionBlock.MergeForPortableTarget(body, layout.Repository, RenderCommand.CurrentPlatform());
        File.WriteAllBytes(Runtime(layout, provider), portable);
        var context = Context(layout, layout.Head());

        layout.AssertNoChanges(() =>
        {
            var error = Assert.Throws<InvalidOperationException>(() => MigrationLegacyCapturePreview.Read(context));
            Assert.Contains("portable sections", error.Message, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("missing-file")]
    [InlineData("missing-value")]
    [InlineData("invalid-value")]
    public void InvalidRuntimePersonalityStopsWithoutAnyProposalOrDefault(string kind)
    {
        using var layout = new MigrationRepositoryFixture(includeCopilotCanonical: true);
        var path = Path.Combine(Path.GetDirectoryName(Runtime(layout, "codex"))!, "config.toml");
        if (kind == "missing-file") File.Delete(path);
        else File.WriteAllText(path, kind == "missing-value" ? "model = 'unmanaged'\n" : "personality = 'invalid'\n");
        File.AppendAllText(Runtime(layout, "codex"), "Pending runtime instruction edit.\n");
        var context = Context(layout, layout.Head());

        layout.AssertNoChanges(() =>
        {
            if (kind == "invalid-value") Assert.Throws<InvalidDataException>(() => MigrationLegacyCapturePreview.Read(context));
            else Assert.Throws<MissingRuntimePersonalityException>(() => MigrationLegacyCapturePreview.Read(context));
        });
    }

    [Fact]
    public void UnselectedProviderNeedsNoCanonicalFilesOrRuntime()
    {
        using var layout = new MigrationRepositoryFixture();
        File.Delete(layout.CopilotRuntime);
        var context = Context(layout, layout.Head());

        layout.AssertNoChanges(() =>
        {
            var preview = MigrationLegacyCapturePreview.Read(context);
            Assert.Equal(2, preview.Files.Count);
            Assert.All(preview.Files, file => Assert.Equal("codex", file.Provider));
            Assert.False(preview.HasChanges);
            Assert.False(File.Exists(Path.Combine(layout.Repository, AecApplication.CopilotSourceRelativePath)));
        });
    }

    [Theory]
    [InlineData("head")]
    [InlineData("binding")]
    [InlineData("original-id")]
    [InlineData("original-ancestry")]
    public void StaleOrForgedContextStopsWithoutWrites(string kind)
    {
        using var layout = new MigrationRepositoryFixture(includeCopilotCanonical: true);
        var context = Context(layout, layout.Head());
        if (kind == "head") layout.Git("commit", "--allow-empty", "-m", "Changed checkpoint");
        if (kind == "binding") File.WriteAllBytes(Runtime(layout, "codex"),
            AecInstructionBlock.Merge(Encoding.UTF8.GetBytes("Wrong checkout.\n"), Path.Combine(layout.Root, "other-data")));
        if (kind == "original-id") context = context with { OriginalCommit = "HEAD~1" };
        if (kind == "original-ancestry")
        {
            var tree = layout.Git("rev-parse", "HEAD^{tree}").Trim();
            var unrelated = layout.Git("commit-tree", tree, "-m", "Unrelated root").Trim();
            context = context with { OriginalCommit = unrelated };
        }

        layout.AssertNoChanges(() =>
        {
            var error = Record.Exception(() => MigrationLegacyCapturePreview.Read(context));
            if (kind == "original-id") Assert.IsType<InvalidDataException>(error);
            else Assert.IsType<InvalidOperationException>(error);
            var message = kind switch
            {
                "head" => "context changed",
                "binding" => "different AEC data repository",
                "original-id" => "full Git object ID",
                _ => "not an ancestor"
            };
            Assert.Contains(message, error!.Message, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InvalidHistoricalSourceCannotBeHiddenByAUsableCurrentFile(bool oversized)
    {
        using var layout = new MigrationRepositoryFixture(includeCopilotCanonical: true);
        var relative = AecApplication.CopilotSourceRelativePath;
        var path = Path.Combine(layout.Repository, relative);
        var valid = File.ReadAllBytes(path);
        if (oversized) File.WriteAllText(path, new string('x', AecApplication.MaximumTextBytes + 1));
        else
        {
            File.Delete(path);
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "invalid.txt"), "Not a regular section file.\n");
        }
        Commit(layout, relative);
        var invalidHistory = layout.Head();
        if (!oversized)
        {
            File.Delete(Path.Combine(path, "invalid.txt"));
            Directory.Delete(path);
        }
        File.WriteAllBytes(path, valid);
        Commit(layout, relative);
        var context = Context(layout, invalidHistory);

        layout.AssertNoChanges(() => Assert.Throws<InvalidDataException>(() => MigrationLegacyCapturePreview.Read(context)));
    }

    private static string Source(string provider) => provider == "codex"
        ? AecApplication.SourceRelativePath : AecApplication.CopilotSourceRelativePath;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RuntimeReadGuardsRejectDirectoriesWithoutChangingAnyFiles(bool config)
    {
        using var layout = new MigrationRepositoryFixture(includeCopilotCanonical: true);
        var context = Context(layout, layout.Head());
        var path = config ? Path.Combine(Path.GetDirectoryName(Runtime(layout, "codex"))!, "config.toml")
            : Runtime(layout, "codex");
        File.Delete(path);
        Directory.CreateDirectory(path);

        layout.AssertNoChanges(() =>
        {
            var error = Assert.Throws<InvalidOperationException>(() => MigrationLegacyCapturePreview.Read(context));
            Assert.Contains("regular file", error.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void PreparedPullRetainsOriginalCommitForTheReadOnlyCaptureDecision()
    {
        using var layout = new MigrationRepositoryFixture(includeCopilotCanonical: true);
        var original = layout.Head();
        layout.AdvanceRemote();
        File.AppendAllText(Runtime(layout, "codex"), "Local runtime edit.\n");
        using var input = new StringReader("");
        var context = Assert.IsType<MigrationPreparation>(MigrationPreparation.Run(layout.Repository,
            "2.0", input, TextWriter.Null));
        Assert.Equal(original, context.OriginalCommit);
        Assert.NotEqual(original, context.Preflight.Commit);

        layout.AssertNoChanges(() =>
        {
            var preview = MigrationLegacyCapturePreview.Read(context);
            Assert.True(preview.HasChanges);
            Assert.Equal(AecApplication.SourceRelativePath, Assert.Single(preview.Files, file => file.HasChanges).RelativePath);
        });
    }

    private static string Runtime(MigrationRepositoryFixture layout, string provider) => provider == "codex"
        ? Path.Combine(layout.Root, "codex-home", "AGENTS.md") : layout.CopilotRuntime;

    private static void Commit(MigrationRepositoryFixture layout, string relative)
    {
        layout.Git("add", "--", relative);
        layout.Git("commit", "--quiet", "-m", "Capture preview fixture revision");
    }

    private static MigrationPreparation Context(MigrationRepositoryFixture layout, string original)
    {
        var preflight = MigrationPreflight.Read(layout.Repository, "2.0");
        var harnesses = MigrationHarnessSelection.Read(preflight);
        return new MigrationPreparation(preflight, original, harnesses,
            MigrationDirtySections.Read(preflight, harnesses), MigrationSectionChoice.Unchanged);
    }
}
