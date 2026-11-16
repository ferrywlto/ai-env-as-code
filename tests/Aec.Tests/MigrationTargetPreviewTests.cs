using System.Text;
using Layout = Aec.Tests.MigrationPortableCapturePreviewTests.Layout;

namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class MigrationTargetPreviewTests
{
    [Theory]
    [InlineData("codex")]
    [InlineData("copilot")]
    public void ExistingCommittedTargetMatchingCompositionIsUnchanged(string provider)
    {
        using var layout = new Layout("codex", "copilot");
        var context = layout.Context();

        layout.Files.AssertNoChanges(() =>
        {
            var preview = MigrationTargetPreview.Read(context, provider);
            Assert.Equal(provider, preview.Harness.Provider);
            Assert.Equal(layout.Platform, preview.Platform);
            Assert.Equal(layout.Target(provider), preview.TargetRelativePath);
            Assert.Equal(MigrationTargetAction.Unchanged, preview.Action);
            Assert.Equal(preview.CommittedContent, preview.DesiredContent);
            Assert.Equal("100644", preview.CommittedMode);
            Assert.Equal(preview.CommittedContent, preview.WorkingContent);
        });
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("copilot")]
    public void MissingTargetWithoutCommittedBaselineIsCreate(string provider)
    {
        using var layout = new Layout("codex", "copilot");
        var target = layout.Target(provider);
        layout.Files.Git("rm", "--quiet", "--", target);
        layout.Files.Git("commit", "--quiet", "-m", "Remove generated target");
        var context = layout.Context();

        layout.Files.AssertNoChanges(() =>
        {
            var preview = MigrationTargetPreview.Read(context, provider);
            Assert.Equal(MigrationTargetAction.Create, preview.Action);
            Assert.Null(preview.CommittedContent);
            Assert.Null(preview.CommittedMode);
            Assert.Null(preview.WorkingContent);
            Assert.Equal(target, preview.TargetRelativePath);
            Assert.NotEmpty(preview.DesiredContent);
        });
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("copilot")]
    public void ChangedCommittedCompositionAgainstExactOldTargetIsRegenerate(string provider)
    {
        using var layout = new Layout("codex", "copilot");
        var original = layout.Files.Head();
        layout.Files.Write($"environment/providers/{provider}/overlay.md", "Revised provider instructions.\n");
        layout.Commit($"environment/providers/{provider}/overlay.md");
        var context = layout.Context(original);
        var targetPath = Path.Combine(layout.Repository, layout.Target(provider));
        var committedTarget = File.ReadAllBytes(targetPath);

        layout.Files.AssertNoChanges(() =>
        {
            var preview = MigrationTargetPreview.Read(context, provider);
            Assert.Equal(MigrationTargetAction.Regenerate, preview.Action);
            Assert.Equal(committedTarget, preview.CommittedContent);
            Assert.Equal(committedTarget, preview.WorkingContent);
            Assert.NotEqual(preview.CommittedContent, preview.DesiredContent);
            Assert.Contains("Revised provider instructions.", Encoding.UTF8.GetString(preview.DesiredContent), StringComparison.Ordinal);
            Assert.Equal("100644", preview.CommittedMode);
        });
    }

    [Fact]
    public void ReturnedInventoryContainsOnlySelectedProvidersThreeSources()
    {
        using var layout = new Layout("codex", "copilot");
        var context = layout.Context();
        var expected = new[]
        {
            "environment/shared/instructions.md",
            $"environment/platforms/{layout.Platform}/policy.md",
            "environment/providers/copilot/overlay.md"
        };

        layout.Files.AssertNoChanges(() =>
        {
            var preview = MigrationTargetPreview.Read(context, "copilot");
            Assert.Equal(context.Preflight.Commit, preview.Preparation.Preflight.Commit);
            Assert.Equal("refs/heads/main", preview.Branch);
            Assert.Equal(layout.Runtime("copilot"), preview.Harness.RuntimePath);
            Assert.Equal(expected.Order(StringComparer.Ordinal),
                preview.Sources.Select(source => source.RelativePath).Order(StringComparer.Ordinal));
            Assert.DoesNotContain(preview.Sources, source => source.RelativePath.Contains("/codex/", StringComparison.Ordinal));
            foreach (var source in preview.Sources)
            {
                var bytes = File.ReadAllBytes(Path.Combine(layout.Repository, source.RelativePath));
                Assert.Equal(bytes, source.CommittedContent);
                Assert.Equal(bytes, source.WorkingContent);
            }
        });
    }

    [Fact]
    public void CommittedEmptySectionsStillProduceAValidCreateProposal()
    {
        using var layout = new Layout("codex", "copilot");
        var target = layout.Target("codex");
        layout.Files.Git("rm", "--quiet", "--", target);
        layout.Files.Write("environment/shared/instructions.md", "");
        layout.Files.Write($"environment/platforms/{layout.Platform}/policy.md", "");
        layout.Files.Write("environment/providers/codex/overlay.md", "");
        layout.Commit("environment/shared/instructions.md",
            $"environment/platforms/{layout.Platform}/policy.md", "environment/providers/codex/overlay.md");
        var context = layout.Context();

        layout.Files.AssertNoChanges(() =>
        {
            var preview = MigrationTargetPreview.Read(context, "codex");
            Assert.Equal(MigrationTargetAction.Create, preview.Action);
            Assert.All(preview.Sources, source => Assert.Empty(source.WorkingContent));
            Assert.NotEmpty(preview.DesiredContent);
        });
    }

    [Theory]
    [InlineData("dirty")]
    [InlineData("staged")]
    [InlineData("hidden")]
    public void SourceMustBeCommittedAndByteClean(string state)
    {
        using var layout = new Layout("codex", "copilot");
        var context = layout.Context();
        var source = "environment/shared/instructions.md";
        layout.Files.Write(source, "Uncommitted shared instructions.\n");
        if (state == "staged") layout.Files.Git("add", "--", source);
        if (state != "hidden") context = layout.Context();
        if (state == "hidden")
        {
            layout.Files.Git("update-index", "--assume-unchanged", "--", source);
            context = layout.Context();
            Assert.Empty(context.DirtySections.Paths);
        }

        layout.Files.AssertNoChanges(() =>
            Assert.Throws<InvalidOperationException>(() => MigrationTargetPreview.Read(context, "codex")));
    }

    [Theory]
    [InlineData("untracked")]
    [InlineData("staged")]
    [InlineData("deleted")]
    [InlineData("modified")]
    [InlineData("hidden")]
    [InlineData("link")]
    public void ExistingTargetMustBeAByteCleanTrackedRegularFile(string state)
    {
        using var layout = new Layout("codex", "copilot");
        var context = layout.Context();
        var target = layout.Target("codex");
        var path = Path.Combine(layout.Repository, target);
        switch (state)
        {
            case "untracked":
                layout.Files.Git("rm", "--quiet", "--", target);
                layout.Files.Git("commit", "--quiet", "-m", "Remove generated target");
                context = layout.Context();
                layout.Files.Write(target, "Untracked target.\n");
                break;
            case "staged":
                File.AppendAllText(path, "Staged target change.\n");
                layout.Files.Git("add", "--", target);
                break;
            case "deleted":
                File.Delete(path);
                break;
            case "modified":
                File.AppendAllText(path, "Unstaged target change.\n");
                break;
            case "hidden":
                layout.Files.Git("update-index", "--assume-unchanged", "--", target);
                File.AppendAllText(path, "Hidden target change.\n");
                break;
            default:
                File.Delete(path);
                File.CreateSymbolicLink(path, layout.Runtime("codex"));
                break;
        }

        layout.Files.AssertNoChanges(() =>
            Assert.Throws<InvalidOperationException>(() => MigrationTargetPreview.Read(context, "codex")));
    }

    [Fact]
    public void MissingIgnoredTargetIsNotTreatedAsCreatable()
    {
        using var layout = new Layout("codex", "copilot");
        var target = layout.Target("codex");
        layout.Files.Git("rm", "--quiet", "--", target);
        layout.Files.Git("commit", "--quiet", "-m", "Remove generated target");
        var context = layout.Context();
        File.AppendAllText(Path.Combine(layout.Repository, ".git", "info", "exclude"), target + "\n");

        layout.Files.AssertNoChanges(() =>
            Assert.Throws<InvalidOperationException>(() => MigrationTargetPreview.Read(context, "codex")));
    }

    [Theory]
    [InlineData("other-provider")]
    [InlineData("wrong-binding")]
    [InlineData("stale-head")]
    [InlineData("oversized")]
    [InlineData("combined-oversized")]
    [InlineData("unsupported-provider")]
    public void UnselectedOrInvalidCompositionInputsAreRejected(string state)
    {
        using var layout = new Layout("codex", "copilot");
        if (state == "other-provider") File.Delete(layout.Runtime("copilot"));
        var context = layout.Context();
        switch (state)
        {
            case "other-provider":
                layout.Files.AssertNoChanges(() =>
                    Assert.Throws<InvalidOperationException>(() => MigrationTargetPreview.Read(context, "copilot")));
                return;
            case "unsupported-provider":
                layout.Files.AssertNoChanges(() =>
                    Assert.Throws<ArgumentException>(() => MigrationTargetPreview.Read(context, "unknown")));
                return;
            case "wrong-binding":
                File.WriteAllBytes(layout.Runtime("codex"), AecInstructionBlock.MergeForPortableTarget(
                    Encoding.UTF8.GetBytes("Other runtime.\n"), Path.Combine(layout.Files.Root, "other-data"),
                    layout.Platform, false));
                break;
            case "stale-head":
                layout.Files.Git("commit", "--allow-empty", "-m", "Fresh checkpoint");
                break;
            case "oversized":
                var largeSource = new string('x', AecApplication.MaximumTextBytes + 1);
                layout.Files.Write("environment/shared/instructions.md", largeSource);
                layout.Commit("environment/shared/instructions.md");
                // Refresh HEAD without asking Context to read the unsafe source.
                context = context with { Preflight = MigrationPreflight.Read(layout.Repository, "2.0") };
                break;
            case "combined-oversized":
                string[] sources = ["environment/shared/instructions.md",
                    $"environment/platforms/{layout.Platform}/policy.md", "environment/providers/codex/overlay.md"];
                foreach (var source in sources) layout.Files.Write(source, new string('x', 400_000));
                layout.Commit(sources);
                context = layout.Context(context.OriginalCommit);
                break;
        }

        layout.Files.AssertNoChanges(() =>
        {
            if (state is "oversized" or "combined-oversized")
                Assert.Throws<InvalidDataException>(() => MigrationTargetPreview.Read(context, "codex"));
            else
                Assert.Throws<InvalidOperationException>(() => MigrationTargetPreview.Read(context, "codex"));
        });
    }

    [Theory]
    [InlineData("100644")]
    [InlineData("100755")]
    public void TargetModeIsReportedWithoutChangingIt(string mode)
    {
        if (mode == "100755" && OperatingSystem.IsWindows()) return;

        using var layout = new Layout("codex", "copilot");
        var target = layout.Target("codex");
        if (mode == "100755" && !OperatingSystem.IsWindows())
        {
            var path = Path.Combine(layout.Repository, target);
            File.SetUnixFileMode(path, File.GetUnixFileMode(path) | UnixFileMode.UserExecute);
            layout.Commit(target);
        }
        var context = layout.Context();
        var targetPath = Path.Combine(layout.Repository, target);
        var workingMode = OperatingSystem.IsWindows() ? (UnixFileMode?)null : File.GetUnixFileMode(targetPath);

        layout.Files.AssertNoChanges(() =>
        {
            var preview = MigrationTargetPreview.Read(context, "codex");
            Assert.Equal(mode, preview.CommittedMode);
            if (!OperatingSystem.IsWindows() && workingMode is { } expectedMode)
                Assert.Equal(expectedMode, File.GetUnixFileMode(targetPath));
        });
    }
}
