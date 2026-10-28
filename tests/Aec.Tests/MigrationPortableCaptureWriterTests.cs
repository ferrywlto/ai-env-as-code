using System.Text;
using Layout = Aec.Tests.MigrationPortableCapturePreviewTests.Layout;

namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class MigrationPortableCaptureWriterTests
{
    private static readonly string[] LegacyPaths =
        [AecApplication.SourceRelativePath, AecApplication.CopilotSourceRelativePath];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ValidatedNoopPreservesEntireFixture(bool portable)
    {
        using var layout = portable ? new Layout("codex", "copilot") : new Layout();
        var reviewed = Preview(layout);
        layout.Files.AssertNoChanges(() => Assert.Null(MigrationPortableCaptureWriter.WriteAndCommit(reviewed)));
    }

    [Theory]
    [InlineData("codex", "provider")]
    [InlineData("copilot", "provider")]
    [InlineData("codex", "shared")]
    [InlineData("copilot", "shared")]
    public void OneChangedProviderCommitsExactlyTheSourceAndAllAffectedTargets(string provider, string section)
    {
        using var layout = new Layout("codex", "copilot");
        layout.EditRuntime(provider, section == "shared" ? "Shared approval." : "Provider mechanics.", "Approved revision.");
        var reviewed = Preview(layout);
        var runtime = layout.Files.RuntimeBytes();
        var legacy = LegacyBytes(layout);
        var parent = layout.Files.Head();
        var remote = layout.Files.Git("rev-parse", "refs/remotes/shared/main");

        var commit = MigrationPortableCaptureWriter.WriteAndCommit(reviewed);

        AssertCommit(layout, commit, parent);
        var source = section == "shared" ? "environment/shared/instructions.md" : $"environment/providers/{provider}/overlay.md";
        var targets = section == "shared" ? new[] { layout.Target("codex"), layout.Target("copilot") } : [layout.Target(provider)];
        Assert.Equal(targets.Append(source).Order(StringComparer.Ordinal), CommittedPaths(layout));
        Assert.Equal("Approved revision.\n", File.ReadAllText(Path.Combine(layout.Repository, source)));
        foreach (var target in targets)
            Assert.Contains("Approved revision.", File.ReadAllText(Path.Combine(layout.Repository, target)), StringComparison.Ordinal);
        Assert.Equal(remote, layout.Files.Git("rev-parse", "refs/remotes/shared/main"));
        layout.Files.AssertRuntime(runtime);
        AssertLegacy(layout, legacy);
        // Capturing does not refresh runtime digests or apply targets. A fresh
        // migration preview would correctly stop until runtime is reconciled.
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("copilot")]
    public void MixedFormatHandoffDoesNotCaptureOrOverwriteTheLegacyRuntime(string provider)
    {
        using var layout = new Layout(provider);
        var legacyProvider = provider == "codex" ? "copilot" : "codex";
        File.AppendAllText(layout.Runtime(legacyProvider), "Pending legacy runtime edit.\n");
        layout.EditRuntime(provider, "Provider mechanics.", "Portable revision.");
        var reviewed = Preview(layout);
        var runtime = layout.Files.RuntimeBytes();
        var legacy = LegacyBytes(layout);
        var parent = layout.Files.Head();

        var commit = MigrationPortableCaptureWriter.WriteAndCommit(reviewed);

        AssertCommit(layout, commit, parent);
        Assert.Equal(new[] { $"environment/providers/{provider}/overlay.md", layout.Target(provider) }
            .Order(StringComparer.Ordinal), CommittedPaths(layout));
        layout.Files.AssertRuntime(runtime);
        AssertLegacy(layout, legacy);
        Assert.False(File.Exists(Path.Combine(layout.Repository, layout.Target(legacyProvider))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CodexConfigCaptureRemainsOneCommitAndIgnoresUnmanagedSettings(bool instructionsAlsoChange)
    {
        using var layout = new Layout("codex", "copilot");
        File.WriteAllText(layout.ConfigRuntime, "personality = 'pragmatic'\nmodel = 'machine-only'\n");
        if (instructionsAlsoChange) layout.EditRuntime("codex", "Provider mechanics.", "Codex revision.");
        var reviewed = Preview(layout);
        var runtime = layout.Files.RuntimeBytes();
        var parent = layout.Files.Head();

        var commit = MigrationPortableCaptureWriter.WriteAndCommit(reviewed);

        AssertCommit(layout, commit, parent);
        string[] paths = instructionsAlsoChange
            ? [AecApplication.ConfigSourceRelativePath, "environment/providers/codex/overlay.md", layout.Target("codex")]
            : [AecApplication.ConfigSourceRelativePath];
        Assert.Equal(paths.Order(StringComparer.Ordinal), CommittedPaths(layout));
        Assert.Equal("personality = \"pragmatic\"\n", File.ReadAllText(Path.Combine(layout.Repository,
            AecApplication.ConfigSourceRelativePath)));
        layout.Files.AssertRuntime(runtime);
    }

    [Theory]
    [InlineData("identical-shared")]
    [InlineData("separate-overlays")]
    [InlineData("config-and-copilot")]
    public void BothChangedProvidersStopBeforeAnyWriteEvenWithMatchingProposals(string scenario)
    {
        using var layout = new Layout("codex", "copilot");
        if (scenario == "config-and-copilot") File.WriteAllText(layout.ConfigRuntime, "personality = 'pragmatic'\n");
        else layout.EditRuntime("codex", scenario == "identical-shared" ? "Shared approval." : "Provider mechanics.", "Same revision.");
        layout.EditRuntime("copilot", scenario == "identical-shared" ? "Shared approval." : "Provider mechanics.", "Same revision.");
        var reviewed = Preview(layout);

        layout.Files.AssertNoChanges(() =>
        {
            var error = Assert.Throws<InvalidOperationException>(() => MigrationPortableCaptureWriter.WriteAndCommit(reviewed));
            Assert.Contains("Both portable providers have changes", error.Message, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("head")]
    [InlineData("branch")]
    [InlineData("active-runtime")]
    [InlineData("other-runtime")]
    [InlineData("runtime-config")]
    [InlineData("provider-presence")]
    [InlineData("target-buffer")]
    public void StaleOrAlteredReviewStopsBeforeAnyWrite(string change)
    {
        using var layout = new Layout("codex", "copilot");
        layout.EditRuntime("codex", "Provider mechanics.", "Reviewed revision.");
        var reviewed = Preview(layout);
        switch (change)
        {
            case "head": layout.Files.Git("commit", "--allow-empty", "-m", "New checkpoint"); break;
            case "branch": layout.Files.Git("switch", "-c", "other-branch"); break;
            case "active-runtime": layout.EditRuntime("codex", "Reviewed revision.", "Later revision."); break;
            case "other-runtime": layout.EditRuntime("copilot", "Provider mechanics.", "Later revision."); break;
            case "runtime-config": File.WriteAllText(layout.ConfigRuntime, "personality = 'pragmatic'\n"); break;
            case "provider-presence": reviewed = reviewed with { Copilot = null }; break;
            default: reviewed = reviewed with { Codex = reviewed.Codex! with { Snapshot = reviewed.Codex!.Snapshot with
                { Target = Encoding.UTF8.GetBytes("Unreviewed target.\n") } } }; break;
        }

        layout.Files.AssertNoChanges(() => Assert.Throws<InvalidOperationException>(() =>
            MigrationPortableCaptureWriter.WriteAndCommit(reviewed)));
    }

    [Fact]
    public void ExistingStagedDraftIsPreservedWithoutAddingCapturePaths()
    {
        using var layout = new Layout("codex");
        layout.EditRuntime("codex", "Provider mechanics.", "Reviewed revision.");
        var reviewed = Preview(layout);
        const string draft = "environment/providers/copilot/overlay.md";
        layout.Files.Write(draft, "Staged legacy-provider draft.\n");
        layout.Files.Git("add", "--", draft);
        layout.Files.Write(draft, "Staged plus unstaged draft.\n");

        layout.Files.AssertNoChanges(() =>
        {
            var error = Assert.Throws<InvalidOperationException>(() => MigrationPortableCaptureWriter.WriteAndCommit(reviewed));
            Assert.Contains("empty staged diff", error.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void DispatchUsesFreshDerivedProposalsInsteadOfCallerSuppliedContent()
    {
        using var layout = new Layout("codex");
        layout.EditRuntime("codex", "Provider mechanics.", "Runtime-approved revision.");
        File.WriteAllText(layout.ConfigRuntime, "personality = 'pragmatic'\n");
        var reviewed = Preview(layout);
        reviewed = reviewed with { Codex = reviewed.Codex! with
        {
            Instructions = new PortableBackupAttributionPreview("shared", "environment/shared/instructions.md", "Untrusted proposal.\n"),
            ProposedCanonicalConfig = Encoding.UTF8.GetBytes("personality = 'none'\n")
        } };
        var parent = layout.Files.Head();

        var commit = MigrationPortableCaptureWriter.WriteAndCommit(reviewed);

        AssertCommit(layout, commit, parent);
        Assert.Equal("Shared approval.\n", File.ReadAllText(Path.Combine(layout.Repository, "environment/shared/instructions.md")));
        Assert.Equal("Runtime-approved revision.\n", File.ReadAllText(Path.Combine(layout.Repository, "environment/providers/codex/overlay.md")));
        Assert.Equal("personality = \"pragmatic\"\n", File.ReadAllText(Path.Combine(layout.Repository, AecApplication.ConfigSourceRelativePath)));
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("copilot")]
    public void SigningFailureReportsAndRetainsPartialFilesAndIndexWithoutRollback(string provider)
    {
        using var layout = new Layout(provider);
        layout.EditRuntime(provider, "Provider mechanics.", "Retained revision.");
        var reviewed = Preview(layout);
        var parent = layout.Files.Head();
        var runtime = layout.Files.RuntimeBytes();
        layout.Files.Git("config", "gpg.format", "openpgp");
        layout.Files.Git("config", "gpg.program", Path.Combine(layout.Files.Root, "missing-signing-program"));
        layout.Files.Git("config", "commit.gpgSign", "true");

        var error = Assert.Throws<IOException>(() => MigrationPortableCaptureWriter.WriteAndCommit(reviewed));

        Assert.Contains("new commit may remain", error.Message, StringComparison.Ordinal);
        Assert.Contains("no rollback, push or runtime apply", error.Message, StringComparison.Ordinal);
        Assert.NotNull(error.InnerException);
        Assert.Equal(parent, layout.Files.Head());
        Assert.Equal(new[] { $"environment/providers/{provider}/overlay.md", layout.Target(provider) }.Order(StringComparer.Ordinal),
            layout.Files.Git("diff", "--cached", "--name-only").Split('\n', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal));
        Assert.Equal("Retained revision.\n", File.ReadAllText(Path.Combine(layout.Repository, $"environment/providers/{provider}/overlay.md")));
        layout.Files.AssertRuntime(runtime);
    }

    private static MigrationPortableCapturePreview Preview(Layout layout) => MigrationPortableCapturePreview.Read(layout.Context());

    private static string[] CommittedPaths(Layout layout) => layout.Files.Git("diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD")
        .Split('\n', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal).ToArray();

    private static void AssertCommit(Layout layout, string? commit, string parent)
    {
        Assert.NotNull(commit);
        Assert.Equal(commit, layout.Files.Head());
        Assert.Equal($"{commit} {parent}", layout.Files.Git("rev-list", "--parents", "-n", "1", commit).Trim());
        Assert.Empty(layout.Files.Git("diff", "--cached", "--name-only"));
        Assert.False(File.Exists(Path.Combine(layout.Repository, MigrationPreflight.FormatRelativePath)));
    }

    private static Dictionary<string, byte[]> LegacyBytes(Layout layout) =>
        LegacyPaths.ToDictionary(path => path, path => File.ReadAllBytes(Path.Combine(layout.Repository, path)));

    private static void AssertLegacy(Layout layout, Dictionary<string, byte[]> legacy)
    {
        foreach (var (path, bytes) in legacy) Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(layout.Repository, path)));
    }
}
