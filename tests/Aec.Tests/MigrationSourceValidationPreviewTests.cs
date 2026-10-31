using System.Text;
using Layout = Aec.Tests.MigrationPortableCapturePreviewTests.Layout;

namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class MigrationSourceValidationPreviewTests
{
    private const string Shared = "environment/shared/instructions.md";
    private const int Limit = 1024 * 1024;

    [Fact]
    public void CleanSourcesReturnExactCommittedAndWorkingSnapshots()
    {
        using var layout = new Layout("codex", "copilot");
        var context = layout.Context();
        layout.Files.AssertNoChanges(() =>
        {
            var preview = MigrationSourceValidationPreview.Read(context);
            Assert.Equal(context.Preflight.Commit, preview.Preparation.Preflight.Commit);
            Assert.Equal("refs/heads/main", preview.Branch);
            Assert.Equal(4, preview.Files.Count);
            Assert.Empty(preview.ChangedPaths);
            foreach (var file in preview.Files)
            {
                var expected = File.ReadAllBytes(Path.Combine(layout.Repository, file.RelativePath));
                Assert.Equal(expected, file.CommittedContent);
                Assert.Equal(expected, file.WorkingContent);
                Assert.Equal("100644", file.CommittedMode);
            }
        });
    }

    [Theory]
    [InlineData("shared")]
    [InlineData("platform")]
    [InlineData("codex")]
    [InlineData("copilot")]
    public void CurrentRequiredEditsAreAcceptedRegardlessOfOldChoiceOrDirtyPaths(string section)
    {
        using var layout = new Layout("codex", "copilot");
        var context = layout.Context() with
        {
            SectionChoice = MigrationSectionChoice.Overwrite,
            DirtySections = new MigrationDirtySections(["old/stale/path.md"])
        };
        var path = section switch
        {
            "shared" => Shared,
            "platform" => $"environment/platforms/{layout.Platform}/policy.md",
            "codex" => "environment/providers/codex/overlay.md",
            _ => "environment/providers/copilot/overlay.md"
        };
        var bytes = Encoding.UTF8.GetBytes($"current {section} revision.\r\n");
        File.WriteAllBytes(Path.Combine(layout.Repository, path), bytes);
        layout.Files.AssertNoChanges(() =>
        {
            var preview = MigrationSourceValidationPreview.Read(context);
            var file = Assert.Single(preview.Files, item => item.RelativePath == path);
            Assert.Equal(bytes, file.WorkingContent);
            Assert.Equal([path], preview.ChangedPaths);
        });
    }

    [Fact]
    public void RequiredScopeTracksLocalHarnessAndCurrentPlatformOnly()
    {
        using var layout = new Layout("codex");
        File.Delete(layout.Runtime("copilot"));
        var otherPlatform = layout.Platform == "windows-x64" ? "linux-arm64" : "windows-x64";
        layout.Files.Write($"environment/platforms/{otherPlatform}/policy.md", "unselected\n");
        layout.Commit($"environment/platforms/{otherPlatform}/policy.md");
        var context = layout.Context();
        layout.Files.AssertNoChanges(() =>
        {
            var preview = MigrationSourceValidationPreview.Read(context);
            Assert.Equal(3, preview.Files.Count);
            Assert.Contains(preview.Files, file => file.RelativePath == $"environment/platforms/{layout.Platform}/policy.md");
            Assert.DoesNotContain(preview.Files, file => file.RelativePath.Contains(otherPlatform, StringComparison.Ordinal));
            Assert.DoesNotContain(preview.Files, file => file.RelativePath == "environment/providers/copilot/overlay.md");
        });
    }

    [Fact]
    public void EmptyAndWhitespaceAreValidAndNewZeroByteFilesHaveAbsentBaseline()
    {
        using var layout = new Layout();
        var paths = RequiredPaths(layout);
        foreach (var path in paths)
        {
            var full = Path.Combine(layout.Repository, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, []);
        }
        var context = layout.Context() with { SectionChoice = MigrationSectionChoice.Overwrite };
        layout.Files.AssertNoChanges(() =>
        {
            var preview = MigrationSourceValidationPreview.Read(context);
            Assert.Equal(paths.Order(StringComparer.Ordinal), preview.Files.Select(file => file.RelativePath));
            Assert.Equal(paths.Order(StringComparer.Ordinal), preview.ChangedPaths);
            Assert.All(preview.Files, file =>
            {
                Assert.Null(file.CommittedContent);
                Assert.Null(file.CommittedMode);
                Assert.Empty(file.WorkingContent);
            });
        });
    }

    [Fact]
    public void WhitespaceAndCrLfBytesArePreservedExactly()
    {
        using var layout = new Layout("codex", "copilot");
        var path = Shared;
        var bytes = Encoding.UTF8.GetBytes(" \t\r\n\r\n");
        File.WriteAllBytes(Path.Combine(layout.Repository, path), bytes);
        var context = layout.Context();
        layout.Files.AssertNoChanges(() =>
        {
            var preview = MigrationSourceValidationPreview.Read(context);
            Assert.Equal(bytes, Assert.Single(preview.Files, f => f.RelativePath == path).WorkingContent);
        });
    }

    [Fact]
    public void HiddenAssumeUnchangedEditIsIncludedByExactWorkingBytes()
    {
        using var layout = new Layout("codex", "copilot");
        var context = layout.Context();
        layout.Files.Git("update-index", "--assume-unchanged", "--", Shared);
        var bytes = Encoding.UTF8.GetBytes("hidden but valid edit\r\n");
        File.WriteAllBytes(Path.Combine(layout.Repository, Shared), bytes);
        layout.Files.AssertNoChanges(() =>
        {
            var preview = MigrationSourceValidationPreview.Read(context);
            var file = Assert.Single(preview.Files, item => item.RelativePath == Shared);
            Assert.Equal(bytes, file.WorkingContent);
            Assert.Equal([Shared], preview.ChangedPaths);
        });
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("deleted")]
    [InlineData("staged")]
    [InlineData("unrelated")]
    [InlineData("invalid-utf8")]
    [InlineData("reserved-marker")]
    [InlineData("oversize")]
    [InlineData("combined-oversize")]
    [InlineData("ignored-new")]
    [InlineData("stale-head")]
    [InlineData("invalid-original")]
    public void UnsafeOrStaleInputsAreRefusedWithoutChangingFixture(string scenario)
    {
        using var layout = scenario is "missing" or "ignored-new" ? new Layout() : new Layout("codex", "copilot");
        var context = layout.Context();
        switch (scenario)
        {
            case "missing":
                foreach (var path in RequiredPaths(layout).Where(path => path != Shared))
                    layout.Files.Write(path, "present required source\n");
                context = layout.Context();
                break;
            case "deleted": File.Delete(Path.Combine(layout.Repository, Shared)); break;
            case "staged":
                File.WriteAllText(Path.Combine(layout.Repository, Shared), "staged\n");
                layout.Files.Git("add", "--", Shared);
                break;
            case "unrelated": layout.Files.Write("stray.txt", "unrelated\n"); break;
            case "invalid-utf8": File.WriteAllBytes(Path.Combine(layout.Repository, Shared), [0xff, 0xfe]); break;
            case "reserved-marker": File.WriteAllText(Path.Combine(layout.Repository, Shared), "<!-- AEC:SOURCE:SHARED:BEGIN -->\n"); break;
            case "oversize": File.WriteAllBytes(Path.Combine(layout.Repository, Shared), new byte[Limit + 1]); break;
            case "combined-oversize":
                foreach (var path in RequiredPaths(layout))
                    File.WriteAllBytes(Path.Combine(layout.Repository, path), new byte[Limit / 2]);
                break;
            case "ignored-new":
                foreach (var path in RequiredPaths(layout)) layout.Files.Write(path, "untracked required source\n");
                layout.Files.Write(".git/info/exclude", Shared + "\n");
                break;
            case "stale-head": layout.Files.Git("commit", "--allow-empty", "-m", "later HEAD"); break;
            case "invalid-original": context = context with { OriginalCommit = "HEAD~1" }; break;
        }
        layout.Files.AssertNoChanges(() =>
        {
            if (scenario == "invalid-utf8")
            {
                Assert.Throws<DecoderFallbackException>(() => MigrationSourceValidationPreview.Read(context));
            }
            else if (scenario is "oversize" or "combined-oversize" or "reserved-marker" or "invalid-original")
            {
                Assert.ThrowsAny<InvalidDataException>(() => MigrationSourceValidationPreview.Read(context));
            }
            else
            {
                Assert.ThrowsAny<InvalidOperationException>(() => MigrationSourceValidationPreview.Read(context));
            }
        });
    }

    [Theory]
    [InlineData("symlink")]
    [InlineData("legacy-enrollment")]
    public void LinksAndEnrollmentChangesAreDetected(string scenario)
    {
        using var layout = new Layout("codex", "copilot");
        var context = layout.Context();
        switch (scenario)
        {
            case "symlink":
                File.Delete(Path.Combine(layout.Repository, Shared));
                File.CreateSymbolicLink(Path.Combine(layout.Repository, Shared), layout.Runtime("codex"));
                break;
            default:
                File.Delete(layout.Runtime("copilot"));
                break;
        }
        layout.Files.AssertNoChanges(() =>
            Assert.ThrowsAny<InvalidOperationException>(() => MigrationSourceValidationPreview.Read(context)));
    }

    [Theory]
    [InlineData("source")]
    [InlineData("index")]
    [InlineData("runtime")]
    [InlineData("config")]
    [InlineData("legacy-source")]
    [InlineData("head")]
    [InlineData("branch")]
    [InlineData("marker")]
    [InlineData("unrelated")]
    [InlineData("enrollment")]
    public void FinalStateGuardRejectsDeterministicInputDrift(string drift)
    {
        using var layout = new Layout("codex", "copilot");
        var reviewed = MigrationSourceValidationPreview.Read(layout.Context());
        var repository = layout.Repository;
        var index = MigrationLegacyCaptureWriter.ReadIndex(Path.Combine(repository, ".git", "index"));
        var protectedFiles = MigrationSourceValidationPreview.ReadProtectedFiles(reviewed.Preparation);

        switch (drift)
        {
            case "source":
                File.WriteAllText(Path.Combine(repository, Shared), "changed after preview\n");
                break;
            case "index": layout.Files.Git("update-index", "--assume-unchanged", "--", Shared); break;
            case "runtime": layout.EditRuntime("codex", "Provider mechanics.", "Guard drift."); break;
            case "config": File.AppendAllText(layout.ConfigRuntime, "# harmless drift\n"); break;
            case "legacy-source":
                layout.Files.Write(AecApplication.ConfigSourceRelativePath, "changed canonical input\n");
                break;
            case "head": layout.Files.Git("commit", "--allow-empty", "-m", "guard drift"); break;
            case "branch":
                layout.Files.Git("branch", "side");
                layout.Files.Git("symbolic-ref", "HEAD", "refs/heads/side");
                break;
            case "marker": layout.Files.Write(MigrationPreflight.FormatRelativePath, "complete\n"); break;
            case "unrelated": layout.Files.Write("stray.txt", "unrelated drift\n"); break;
            case "enrollment": File.Delete(layout.Runtime("copilot")); break;
        }

        layout.Files.AssertNoChanges(() => Assert.ThrowsAny<InvalidOperationException>(() =>
            MigrationSourceValidationPreview.VerifyFinalState(reviewed.Preparation, reviewed.Branch,
                index, reviewed.Preparation.DirtySections, reviewed.Files, protectedFiles)));
    }

    private static string[] RequiredPaths(Layout layout)
    {
        string[] paths = [Shared, $"environment/platforms/{layout.Platform}/policy.md",
            "environment/providers/codex/overlay.md", "environment/providers/copilot/overlay.md"];
        return paths.Order(StringComparer.Ordinal).ToArray();
    }
}
