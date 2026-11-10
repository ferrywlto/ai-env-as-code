using System.Text;
using Layout = Aec.Tests.MigrationPortableCapturePreviewTests.Layout;

namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class MigrationPortableSourceWriterTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoopPreservesEveryFileDirectoryAndGitByte(bool portable)
    {
        using var layout = portable ? new Layout("codex", "copilot") : new Layout();
        var reviewed = Preview(layout);
        layout.Files.AssertNoChanges(() => Assert.Empty(MigrationPortableSourceWriter.Write(reviewed)));
    }

    [Theory]
    [InlineData("shared")]
    [InlineData("platform")]
    public void IdenticalSameSourceProposalsAreWrittenExactlyOnce(string section)
    {
        using var layout = new Layout("codex", "copilot");
        var original = section == "shared" ? "Shared approval." : "Local paths.";
        var relative = section == "shared" ? "environment/shared/instructions.md"
            : $"environment/platforms/{layout.Platform}/policy.md";
        layout.EditRuntime("codex", original, "Agreed revision.");
        layout.EditRuntime("copilot", original, "Agreed revision.");
        var reviewed = Preview(layout);

        layout.Files.AssertOnlyChanges([relative], () =>
        {
            Assert.Equal(relative, Assert.Single(MigrationPortableSourceWriter.Write(reviewed)));
            Assert.Equal("Agreed revision.\n", File.ReadAllText(Path.Combine(layout.Repository, relative)));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SeparateProviderAndConfigChangesShareOneBaselineWithoutRendering(bool codexInstructions)
    {
        using var layout = new Layout("codex", "copilot");
        layout.Files.Write(AecApplication.ConfigSourceRelativePath, "# Saved comment\npersonality = 'friendly'\n");
        layout.Commit(AecApplication.ConfigSourceRelativePath);
        File.WriteAllText(layout.ConfigRuntime, "personality = 'pragmatic'\nmodel = 'private-machine-model'\n");
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(layout.Runtime("copilot"))!, "config.json"), "{\"unmanaged\":true}\n");
        if (codexInstructions) layout.EditRuntime("codex", "Provider mechanics.", "Codex revision.");
        layout.EditRuntime("copilot", "Provider mechanics.", "Copilot revision.");
        var reviewed = Preview(layout);
        var expected = new List<string> { AecApplication.ConfigSourceRelativePath, "environment/providers/copilot/overlay.md" };
        if (codexInstructions) expected.Add("environment/providers/codex/overlay.md");

        layout.Files.AssertOnlyChanges(expected, () =>
        {
            Assert.Equal(expected.Order(StringComparer.Ordinal), MigrationPortableSourceWriter.Write(reviewed));
            Assert.Equal("# Saved comment\npersonality = \"pragmatic\"\n", File.ReadAllText(Path.Combine(layout.Repository,
                AecApplication.ConfigSourceRelativePath)));
            Assert.Equal("Copilot revision.\n", File.ReadAllText(Path.Combine(layout.Repository, "environment/providers/copilot/overlay.md")));
            if (codexInstructions) Assert.Equal("Codex revision.\n", File.ReadAllText(Path.Combine(layout.Repository, "environment/providers/codex/overlay.md")));
        });
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("copilot")]
    public void MixedFormatsPreservePendingLegacyRuntimeEdits(string portableProvider)
    {
        using var layout = new Layout(portableProvider);
        var legacyProvider = portableProvider == "codex" ? "copilot" : "codex";
        File.AppendAllText(layout.Runtime(legacyProvider), "Keep pending legacy edits.\n");
        layout.EditRuntime(portableProvider, "Provider mechanics.", "Portable revision.");
        var reviewed = Preview(layout);
        var relative = $"environment/providers/{portableProvider}/overlay.md";

        layout.Files.AssertOnlyChanges([relative], () =>
            Assert.Equal(relative, Assert.Single(MigrationPortableSourceWriter.Write(reviewed))));
        Assert.False(File.Exists(Path.Combine(layout.Repository, layout.Target(legacyProvider))));
    }

    internal static void StaleInputsStopBeforeAnyReplacement(string kind)
    {
        using var layout = new Layout("codex", "copilot");
        layout.EditRuntime("codex", "Provider mechanics.", "Reviewed revision.");
        var reviewed = Preview(layout);
        switch (kind)
        {
            case "head": layout.Files.Git("commit", "--allow-empty", "-m", "Later checkpoint"); break;
            case "branch": layout.Files.Git("switch", "-c", "other-branch"); break;
            case "runtime": layout.EditRuntime("codex", "Reviewed revision.", "Later revision."); break;
            case "other-runtime": layout.EditRuntime("copilot", "Provider mechanics.", "Other revision."); break;
            case "config": File.WriteAllText(layout.ConfigRuntime, "personality = 'pragmatic'\n"); break;
            case "marker": layout.Files.Write(MigrationPreflight.FormatRelativePath, "2.0\n"); break;
            default: layout.Files.Write("environment/shared/instructions.md", "Keep this authored draft.\n"); break;
        }

        layout.Files.AssertNoChanges(() => Assert.Throws<InvalidOperationException>(() => MigrationPortableSourceWriter.Write(reviewed)));
    }

    [Fact]
    public void ConflictingSharedRuntimeProposalsStopBeforeAnyReplacement()
    {
        using var layout = new Layout("codex", "copilot");
        layout.EditRuntime("codex", "Shared approval.", "Agreed revision.");
        layout.EditRuntime("copilot", "Shared approval.", "Agreed revision.");
        var reviewed = Preview(layout);
        layout.EditRuntime("copilot", "Agreed revision.", "Conflicting revision.");

        layout.Files.AssertNoChanges(() =>
        {
            var error = Assert.Throws<InvalidOperationException>(() => MigrationPortableSourceWriter.Write(reviewed));
            Assert.Contains("proposals conflict", error.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ExistingStagedAndUnstagedDraftRemainUntouched()
    {
        using var layout = new Layout("codex");
        layout.EditRuntime("codex", "Provider mechanics.", "Reviewed revision.");
        var reviewed = Preview(layout);
        const string draft = "environment/providers/copilot/overlay.md";
        layout.Files.Write(draft, "Staged draft.\n");
        layout.Files.Git("add", "--", draft);
        layout.Files.Write(draft, "Staged plus unstaged draft.\n");

        layout.Files.AssertNoChanges(() =>
        {
            var error = Assert.Throws<InvalidOperationException>(() => MigrationPortableSourceWriter.Write(reviewed));
            Assert.Contains("empty staged diff", error.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void CallerSuppliedProposalFieldsCannotRedirectCombinedWrites()
    {
        using var layout = new Layout("codex", "copilot");
        layout.EditRuntime("codex", "Provider mechanics.", "Codex revision.");
        layout.EditRuntime("copilot", "Provider mechanics.", "Copilot revision.");
        var reviewed = Preview(layout);
        reviewed = reviewed with
        {
            Codex = reviewed.Codex! with { Instructions = new PortableBackupAttributionPreview("shared", "../escape.md", "Untrusted proposal.\n"),
                ProposedCanonicalConfig = Encoding.UTF8.GetBytes("personality = 'none'\n") },
            Copilot = reviewed.Copilot! with { Instructions = new PortableBackupAttributionPreview(null, null, null) }
        };
        string[] expected = ["environment/providers/codex/overlay.md", "environment/providers/copilot/overlay.md"];

        layout.Files.AssertOnlyChanges(expected, () => Assert.Equal(expected, MigrationPortableSourceWriter.Write(reviewed)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailureReportsCompletedAndPossiblyChangedAttemptWithoutRollback(bool earlierWrite)
    {
        using var layout = new Layout("codex", "copilot");
        if (earlierWrite) layout.EditRuntime("codex", "Provider mechanics.", "First revision.");
        layout.EditRuntime("copilot", "Provider mechanics.", "Blocked revision.");
        var reviewed = Preview(layout);
        const string blocked = "environment/providers/copilot/overlay.md";
        const string first = "environment/providers/codex/overlay.md";
        string[] changed = earlierWrite ? [first] : [];

        layout.Files.AssertOnlyChanges(changed, () => MigrationLegacyCaptureWriterTests.BlockReplacement(
            Path.Combine(layout.Repository, blocked), () =>
            {
                var error = Assert.Throws<MigrationPortableSourceWriteException>(() => MigrationPortableSourceWriter.Write(reviewed));
                Assert.Equal(blocked, error.AttemptedPath);
                Assert.Equal(changed, error.WrittenPaths);
                Assert.NotNull(error.InnerException);
                Assert.Contains("No rollback, rendering, staging, commit or runtime apply", error.Message, StringComparison.Ordinal);
            }));
    }

    private static MigrationPortableCapturePreview Preview(Layout layout) => MigrationPortableCapturePreview.Read(layout.Context());
}
