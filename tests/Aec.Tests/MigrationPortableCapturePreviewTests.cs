using System.Text;

namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
[Trait("Category", "LegacyMigration")]
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

    [Fact]
    public void ConflictingSharedProposalsAreRejectedWithoutWriting()
    {
        using var layout = new Layout("codex", "copilot");
        layout.EditRuntime("codex", "Shared approval.", "Shared revision.");
        layout.EditRuntime("copilot", "Shared approval.", "Other revision.");
        var context = layout.Context();
        layout.Files.AssertNoChanges(() =>
        {
            var error = Assert.Throws<InvalidOperationException>(() => MigrationPortableCapturePreview.Read(context));
            Assert.Contains("proposals conflict", error.Message, StringComparison.Ordinal);
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
    [InlineData("head", "Migration context changed")]
    [InlineData("binding", "bound to a different AEC data repository")]
    [InlineData("missing-target", "Committed target does not exist")]
    [InlineData("old-digest", "last applied from an older target")]
    [InlineData("dirty-draft", "Authored shared instructions has unstaged changes")]
    [InlineData("control-block", "supported initialized AEC block")]
    [InlineData("missing-personality", "runtime config does not declare root")]
    public void UnclearOrStaleInputsStopWithoutChangingTheEntireFixture(string kind, string expectedDiagnostic)
    {
        using var layout = new Layout("codex");
        // Stale review cases pin the context before the change. The remaining
        // cases review the updated checkout and let preview validate its inputs.
        var context = kind is "head" or "binding" or "control-block" ? layout.Context() : null;
        switch (kind)
        {
            case "head": layout.Files.Git("commit", "--allow-empty", "-m", "Later checkpoint"); break;
            case "binding": File.WriteAllBytes(layout.Runtime("codex"), AecInstructionBlock.MergeForPortableTarget(
                Encoding.UTF8.GetBytes("Other runtime.\n"), Path.Combine(layout.Files.Root, "other-data"), layout.Platform, false)); break;
            case "missing-target":
                layout.Files.Git("rm", "--", layout.Target("codex"));
                layout.Files.Git("commit", "--quiet", "-m", "Missing target checkpoint");
                break;
            case "old-digest":
                var original = layout.Files.Head();
                layout.Files.Write("environment/shared/instructions.md", "New committed shared instructions.\n");
                layout.WriteTarget("codex", "New committed shared instructions.\n");
                layout.Commit("environment/shared/instructions.md", layout.Target("codex"));
                context = layout.Context(original);
                break;
            case "dirty-draft": layout.Files.Write("environment/shared/instructions.md", "Preserve this draft.\n"); break;
            case "control-block": layout.EditRuntime("codex", "## AI Environment as Code", "## Edited control block"); break;
            case "missing-personality": File.WriteAllText(layout.ConfigRuntime, "model = 'unmanaged'\n"); break;
            default: throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }

        context ??= layout.Context();
        if (kind == "dirty-draft") Assert.Equal(MigrationSectionChoice.Preserve, context.SectionChoice);

        layout.Files.AssertNoChanges(() =>
        {
            var error = Record.Exception(() => MigrationPortableCapturePreview.Read(context));
            if (kind == "control-block") Assert.IsType<InvalidDataException>(error);
            else if (kind == "missing-personality") Assert.IsType<MissingRuntimePersonalityException>(error);
            else if (kind == "missing-target") Assert.IsType<FileNotFoundException>(error);
            else Assert.IsType<InvalidOperationException>(error);
            Assert.Contains(expectedDiagnostic, error!.Message, StringComparison.Ordinal);
        });
    }

    internal sealed class Layout : IDisposable
    {
        public MigrationRepositoryFixture Files { get; }
        public string Repository => Files.Repository;
        public string Platform { get; } = RenderCommand.CurrentPlatform();
        public string ConfigRuntime => Path.Combine(Files.Root, "codex-home", "config.toml");

        public Layout(params string[] portableProviders)
            : this(portableProviders, includeRemote: false)
        {
        }

        public static Layout WithRemote(params string[] portableProviders) =>
            new(portableProviders, includeRemote: true);

        private Layout(string[] portableProviders, bool includeRemote)
        {
            Files = new MigrationRepositoryFixture(includeCopilotCanonical: true, includeRemote: includeRemote);
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
