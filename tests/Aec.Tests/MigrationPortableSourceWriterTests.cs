using Layout = Aec.Tests.MigrationPortableCapturePreviewTests.Layout;

namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
[Trait("Category", "LegacyMigration")]
public sealed class MigrationPortableSourceWriterTests
{
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
