using System.Text;

namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class MigrationPortableCapturePreviewTests
{
    [Fact]
    public void LegacyRuntimesReturnNoPortableProposalsAndCreateNothing()
    {
        using var layout = new Layout();
        var context = layout.Context();
        layout.Files.AssertNoChanges(() =>
        {
            var preview = MigrationPortableCapturePreview.Read(context);
            Assert.Null(preview.Codex);
            Assert.Null(preview.Copilot);
            Assert.False(preview.HasChanges);
        });
        Assert.False(Directory.Exists(Path.Combine(layout.Repository, "environment", "targets")));
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("copilot")]
    public void MixedFormatsPreviewOnlyThePortableRuntime(string portableProvider)
    {
        using var layout = new Layout(portableProvider);
        var context = layout.Context();
        layout.Files.AssertNoChanges(() =>
        {
            var preview = MigrationPortableCapturePreview.Read(context);
            Assert.Equal(portableProvider == "codex", preview.Codex is not null);
            Assert.Equal(portableProvider == "copilot", preview.Copilot is not null);
            Assert.False(preview.HasChanges);
            Assert.Equal(context.Preflight.Commit, preview.Codex?.Snapshot.Commit ?? preview.Copilot!.Snapshot.Commit);
        });
    }

    [Fact]
    public void BothPortableRuntimesRemainDistinctFromAbsentProvidersOnNoop()
    {
        using var layout = new Layout("codex", "copilot");
        var context = layout.Context();
        layout.Files.AssertNoChanges(() =>
        {
            var preview = MigrationPortableCapturePreview.Read(context);
            Assert.NotNull(preview.Codex);
            Assert.NotNull(preview.Copilot);
            Assert.False(preview.HasChanges);
            Assert.Equal(MigrationLegacyCapturePreview.ReadBranch(layout.Repository), preview.Branch);
        });
    }

    [Fact]
    public void PulledTargetCannotSelectAnAbsentLocalHarness()
    {
        using var layout = new Layout("codex", "copilot");
        File.Delete(layout.Runtime("copilot"));
        var context = layout.Context();
        layout.Files.AssertNoChanges(() =>
        {
            var preview = MigrationPortableCapturePreview.Read(context);
            Assert.NotNull(preview.Codex);
            Assert.Null(preview.Copilot);
            Assert.False(preview.HasChanges);
        });
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("copilot")]
    public void OneRuntimeSectionProducesAnAuthoredProposalNotALegacyFileCopy(string provider)
    {
        using var layout = new Layout(provider);
        layout.EditRuntime(provider, "Provider mechanics.", "Provider revision.");
        var context = layout.Context();
        layout.Files.AssertNoChanges(() =>
        {
            var preview = MigrationPortableCapturePreview.Read(context);
            var instructions = preview.Codex?.Instructions ?? preview.Copilot!.Instructions;
            Assert.True(preview.HasChanges);
            Assert.Equal("provider", instructions.Section);
            Assert.Equal($"environment/providers/{provider}/overlay.md", instructions.SourceRelativePath);
            Assert.Equal("Provider revision.\n", instructions.ProposedContent);
        });
    }

    [Fact]
    public void ManagedConfigOnlyDriftNeverCapturesUnrelatedRuntimeSettings()
    {
        using var layout = new Layout("codex");
        File.WriteAllText(layout.ConfigRuntime, "personality = 'pragmatic'\nmodel = 'machine-only'\n");
        var context = layout.Context();
        layout.Files.AssertNoChanges(() =>
        {
            var preview = MigrationPortableCapturePreview.Read(context);
            Assert.True(preview.HasChanges);
            Assert.Null(preview.Codex!.Instructions.Section);
            Assert.True(preview.Codex.ConfigWouldChange);
            Assert.Equal("personality = \"pragmatic\"\n", Encoding.UTF8.GetString(preview.Codex.ProposedCanonicalConfig));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SameSourceProposalsMustAgreeExactly(bool conflict)
    {
        using var layout = new Layout("codex", "copilot");
        layout.EditRuntime("codex", "Shared approval.", "Shared revision.");
        layout.EditRuntime("copilot", "Shared approval.", conflict ? "Other revision." : "Shared revision.");
        var context = layout.Context();
        layout.Files.AssertNoChanges(() =>
        {
            if (conflict)
            {
                var error = Assert.Throws<InvalidOperationException>(() => MigrationPortableCapturePreview.Read(context));
                Assert.Contains("proposals conflict", error.Message, StringComparison.Ordinal);
                return;
            }
            var preview = MigrationPortableCapturePreview.Read(context);
            Assert.True(preview.HasChanges);
            Assert.Equal("environment/shared/instructions.md", preview.Codex!.Instructions.SourceRelativePath);
            Assert.Equal(preview.Codex.Instructions, preview.Copilot!.Instructions);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RetrievedPersonalityRequiresMatchingRuntimeOrStops(bool runtimeMatches)
    {
        using var layout = new Layout("codex");
        var original = layout.Files.Head();
        layout.Files.Write(AecApplication.ConfigSourceRelativePath, "# Retrieved comment\npersonality = 'pragmatic'\n");
        layout.Commit(AecApplication.ConfigSourceRelativePath);
        if (runtimeMatches) File.WriteAllText(layout.ConfigRuntime, "personality = 'pragmatic'\nmodel = 'local'\n");
        var context = layout.Context(original);
        layout.Files.AssertNoChanges(() =>
        {
            if (!runtimeMatches)
            {
                var error = Assert.Throws<InvalidOperationException>(() => MigrationPortableCapturePreview.Read(context));
                Assert.Contains("Retrieved canonical personality differs", error.Message, StringComparison.Ordinal);
                return;
            }
            var preview = MigrationPortableCapturePreview.Read(context);
            Assert.False(preview.HasChanges);
            Assert.Equal(File.ReadAllBytes(Path.Combine(layout.Repository, AecApplication.ConfigSourceRelativePath)),
                preview.Codex!.ProposedCanonicalConfig);
        });
    }

    [Theory]
    [InlineData("head")]
    [InlineData("binding")]
    [InlineData("original-id")]
    [InlineData("original-ancestry")]
    [InlineData("missing-target")]
    [InlineData("old-digest")]
    [InlineData("dirty-draft")]
    [InlineData("multiple-sections")]
    [InlineData("control-block")]
    [InlineData("missing-personality")]
    public void UnclearOrStaleInputsStopWithoutChangingTheEntireFixture(string kind)
    {
        using var layout = new Layout("codex", "copilot");
        var context = layout.Context();
        switch (kind)
        {
            case "head": layout.Files.Git("commit", "--allow-empty", "-m", "Later checkpoint"); break;
            case "binding": File.WriteAllBytes(layout.Runtime("codex"), AecInstructionBlock.MergeForPortableTarget(
                Encoding.UTF8.GetBytes("Other runtime.\n"), Path.Combine(layout.Files.Root, "other-data"), layout.Platform, false)); break;
            case "original-id": context = context with { OriginalCommit = "HEAD~1" }; break;
            case "original-ancestry":
                var tree = layout.Files.Git("rev-parse", "HEAD^{tree}").Trim();
                context = context with { OriginalCommit = layout.Files.Git("commit-tree", tree, "-m", "Unrelated root").Trim() };
                break;
            case "missing-target":
                layout.Files.Git("rm", "--", layout.Target("codex"));
                layout.Files.Git("commit", "--quiet", "-m", "Missing target checkpoint");
                context = layout.Context();
                break;
            case "old-digest":
                layout.Files.Write("environment/shared/instructions.md", "New committed shared instructions.\n");
                layout.WriteTarget("codex", "New committed shared instructions.\n");
                layout.WriteTarget("copilot", "New committed shared instructions.\n");
                layout.Commit("environment/shared/instructions.md", layout.Target("codex"), layout.Target("copilot"));
                context = layout.Context(context.OriginalCommit);
                break;
            case "dirty-draft":
                layout.Files.Write("environment/shared/instructions.md", "Preserve this draft.\n");
                context = layout.Context();
                Assert.Equal(MigrationSectionChoice.Preserve, context.SectionChoice);
                break;
            case "multiple-sections":
                layout.EditRuntime("codex", "Shared approval.", "Shared revision.");
                layout.EditRuntime("codex", "Local paths.", "Local revision.");
                break;
            case "control-block": layout.EditRuntime("codex", "## AI Environment as Code", "## Edited control block"); break;
            default: File.WriteAllText(layout.ConfigRuntime, "model = 'unmanaged'\n"); break;
        }

        layout.Files.AssertNoChanges(() =>
        {
            var error = Record.Exception(() => MigrationPortableCapturePreview.Read(context));
            if (kind is "original-id" or "multiple-sections" or "control-block") Assert.IsType<InvalidDataException>(error);
            else if (kind == "missing-personality") Assert.IsType<MissingRuntimePersonalityException>(error);
            else if (kind == "missing-target") Assert.IsType<FileNotFoundException>(error);
            else Assert.IsType<InvalidOperationException>(error);
        });
    }

    internal sealed class Layout : IDisposable
    {
        public MigrationRepositoryFixture Files { get; } = new(includeCopilotCanonical: true);
        public string Repository => Files.Repository;
        public string Platform { get; } = RenderCommand.CurrentPlatform();
        public string ConfigRuntime => Path.Combine(Files.Root, "codex-home", "config.toml");

        public Layout(params string[] portableProviders)
        {
            if (portableProviders.Length == 0) return;
            var paths = new List<string> { "environment/shared/instructions.md", $"environment/platforms/{Platform}/policy.md" };
            Files.Write(paths[0], "Shared approval.\n");
            Files.Write(paths[1], "Local paths.\n");
            foreach (var provider in portableProviders)
            {
                var overlay = $"environment/providers/{provider}/overlay.md";
                Files.Write(overlay, "Provider mechanics.\n");
                WriteTarget(provider);
                paths.AddRange([overlay, Target(provider)]);
            }
            Commit([.. paths]);
            foreach (var provider in portableProviders)
            {
                var target = File.ReadAllBytes(Path.Combine(Repository, Target(provider)));
                File.WriteAllBytes(Runtime(provider), provider == "codex"
                    ? AecInstructionBlock.MergeForPortableTarget(target, Repository, Platform, false)
                    : CopilotInstructionBlock.MergeForPortableTarget(target, Repository, Platform));
            }
        }

        public string Runtime(string provider) => provider == "codex"
            ? Path.Combine(Files.Root, "codex-home", "AGENTS.md") : Files.CopilotRuntime;
        public string Target(string provider) => $"environment/targets/{Platform}/{provider}/" +
            (provider == "codex" ? "AGENTS.md" : "copilot-instructions.md");

        public void WriteTarget(string provider, string shared = "Shared approval.\n") =>
            Files.Write(Target(provider), InstructionSections.Render(shared, "Local paths.\n", "Provider mechanics.\n"));

        public void EditRuntime(string provider, string original, string replacement)
        {
            var path = Runtime(provider);
            var text = File.ReadAllText(path);
            Assert.Contains(original, text, StringComparison.Ordinal);
            File.WriteAllText(path, text.Replace(original, replacement, StringComparison.Ordinal));
        }

        public void Commit(params string[] paths)
        {
            Files.Git(["add", "--", .. paths]);
            Files.Git("commit", "--quiet", "-m", "Interrupted migration checkpoint");
        }

        public MigrationPreparation Context(string? original = null)
        {
            var preflight = MigrationPreflight.Read(Repository, "2.0");
            var harnesses = MigrationHarnessSelection.Read(preflight);
            var dirty = MigrationDirtySections.Read(preflight, harnesses);
            return new MigrationPreparation(preflight, original ?? preflight.Commit, harnesses, dirty,
                dirty.Paths.Count == 0 ? MigrationSectionChoice.Unchanged : MigrationSectionChoice.Preserve);
        }

        public void Dispose() => Files.Dispose();
    }
}
