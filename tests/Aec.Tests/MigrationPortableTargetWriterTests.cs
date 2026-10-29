using Layout = Aec.Tests.MigrationPortableCapturePreviewTests.Layout;

namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class MigrationPortableTargetWriterTests
{
    private static readonly string[] Platforms = ["macos-arm64", "windows-x64", "linux-arm64"];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NoopPreservesTheEntireFixture(bool portable)
    {
        using var layout = portable ? new Layout("codex", "copilot") : new Layout();
        var reviewed = Preview(layout);
        layout.Files.AssertNoChanges(() =>
        {
            var plan = MigrationPortableTargetWriter.Prepare(reviewed);
            Assert.Empty(plan.Write(MigrationPortableSourceWriter.Write(reviewed)));
        });
    }

    [Theory]
    [InlineData("shared")]
    [InlineData("platform")]
    public void AgreeingProposalsUpdateExactlyTheirEnrolledConsumers(string section)
    {
        using var layout = new Layout("codex", "copilot");
        var targets = EnrollOtherPlatforms(layout, "codex", "copilot");
        var original = section == "shared" ? "Shared approval." : "Local paths.";
        layout.EditRuntime("codex", original, "Agreed revision.");
        layout.EditRuntime("copilot", original, "Agreed revision.");
        var (plan, sources) = PrepareSources(layout);
        var expected = targets.Where(path => section == "shared" || path.Contains($"/{layout.Platform}/", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).ToArray();

        layout.Files.AssertOnlyChanges(expected, () => Assert.Equal(expected, plan.Write(sources)));
        foreach (var path in expected)
        {
            var parts = InstructionSections.Parse(File.ReadAllText(Path.Combine(layout.Repository, path)), true, true);
            Assert.Equal("Agreed revision.\n", section == "shared" ? parts.Shared : parts.Platform);
            Assert.Equal("Provider mechanics.\n", parts.Provider);
        }
    }

    [Fact]
    public void SeparateProviderAndConfigProposalsRenderTogetherWithoutChangingAuthoredFiles()
    {
        using var layout = new Layout("codex", "copilot");
        var targets = EnrollOtherPlatforms(layout, "codex", "copilot");
        layout.EditRuntime("codex", "Provider mechanics.", "Codex revision.");
        layout.EditRuntime("copilot", "Provider mechanics.", "Copilot revision.");
        File.WriteAllText(layout.ConfigRuntime, "personality = 'pragmatic'\nmodel = 'private'\n");
        var (plan, sources) = PrepareSources(layout);
        Assert.Contains(AecApplication.ConfigSourceRelativePath, sources);

        layout.Files.AssertOnlyChanges(targets, () => Assert.Equal(targets, plan.Write(sources)));
        foreach (var path in targets)
        {
            var parts = InstructionSections.Parse(File.ReadAllText(Path.Combine(layout.Repository, path)), true, true);
            Assert.Equal(path.Contains("/codex/", StringComparison.Ordinal) ? "Codex revision.\n" : "Copilot revision.\n", parts.Provider);
        }
    }

    [Fact]
    public void CombinedPlatformAndProviderChangesUseTheUnionOfConsumers()
    {
        using var layout = new Layout("codex", "copilot");
        var targets = EnrollOtherPlatforms(layout, "codex", "copilot");
        layout.EditRuntime("codex", "Local paths.", "Platform revision.");
        layout.EditRuntime("copilot", "Provider mechanics.", "Provider revision.");
        var (plan, sources) = PrepareSources(layout);
        var expected = targets.Where(path => path.Contains($"/{layout.Platform}/", StringComparison.Ordinal) ||
            path.Contains("/copilot/", StringComparison.Ordinal)).ToArray();

        layout.Files.AssertOnlyChanges(expected, () => Assert.Equal(expected, plan.Write(sources)));
        var combined = InstructionSections.Parse(File.ReadAllText(Path.Combine(layout.Repository, layout.Target("copilot"))), true, true);
        Assert.Equal("Platform revision.\n", combined.Platform);
        Assert.Equal("Provider revision.\n", combined.Provider);
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("copilot")]
    public void MixedFormatsRenderOnlyExistingPortableEnrollments(string provider)
    {
        using var layout = new Layout(provider);
        var targets = EnrollOtherPlatforms(layout, provider);
        var legacy = provider == "codex" ? "copilot" : "codex";
        File.AppendAllText(layout.Runtime(legacy), "Pending legacy edit.\n");
        layout.EditRuntime(provider, "Shared approval.", "Shared revision.");
        var (plan, sources) = PrepareSources(layout);

        layout.Files.AssertOnlyChanges(targets, () => Assert.Equal(targets, plan.Write(sources)));
        Assert.False(File.Exists(Path.Combine(layout.Repository, layout.Target(legacy))));
    }

    [Fact]
    public void ConfigOnlyCaptureDoesNotRenderTargets()
    {
        using var layout = new Layout("codex", "copilot");
        _ = EnrollOtherPlatforms(layout, "codex", "copilot");
        File.WriteAllText(layout.ConfigRuntime, "personality = 'pragmatic'\n");
        var (plan, sources) = PrepareSources(layout);
        Assert.Equal(AecApplication.ConfigSourceRelativePath, Assert.Single(sources));
        layout.Files.AssertNoChanges(() => Assert.Empty(plan.Write(sources)));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("extra")]
    [InlineData("duplicate")]
    public void InexactCompletedSourcePathsStopWithoutWrites(string kind)
    {
        using var layout = new Layout("codex", "copilot");
        layout.EditRuntime("codex", "Provider mechanics.", "Codex revision.");
        layout.EditRuntime("copilot", "Provider mechanics.", "Copilot revision.");
        var (plan, sources) = PrepareSources(layout);
        string[] reported = kind switch
        {
            "missing" => [sources[0]],
            "extra" => [.. sources, "notes.md"],
            _ => [.. sources, sources[0]]
        };
        layout.Files.AssertNoChanges(() => Assert.Throws<InvalidOperationException>(() => plan.Write(reported)));
    }

    [Fact]
    public void CompletedPathListDoesNotSubstituteForActualSourceWrites()
    {
        using var layout = new Layout("codex", "copilot");
        layout.EditRuntime("codex", "Provider mechanics.", "Codex revision.");
        layout.EditRuntime("copilot", "Provider mechanics.", "Copilot revision.");
        var plan = MigrationPortableTargetWriter.Prepare(Preview(layout));
        layout.Files.Write("environment/providers/codex/overlay.md", "Codex revision.\n");
        string[] reported = ["environment/providers/codex/overlay.md", "environment/providers/copilot/overlay.md"];
        layout.Files.AssertNoChanges(() => Assert.Throws<MigrationPortableTargetWriteException>(() => plan.Write(reported)));
    }

    [Theory]
    [InlineData("head")]
    [InlineData("branch")]
    [InlineData("runtime")]
    [InlineData("other-runtime")]
    [InlineData("runtime-config")]
    [InlineData("marker")]
    [InlineData("source")]
    [InlineData("other-platform-source")]
    [InlineData("canonical-config")]
    [InlineData("target")]
    [InlineData("other-platform-target")]
    [InlineData("staged")]
    [InlineData("raw-index")]
    public void StaleInputsStopBeforeAnyTargetReplacement(string kind)
    {
        using var layout = new Layout("codex", "copilot");
        var targets = EnrollOtherPlatforms(layout, "codex", "copilot");
        layout.EditRuntime("codex", "Shared approval.", "Shared revision.");
        var (plan, sources) = PrepareSources(layout);
        var otherTarget = targets.First(path => !path.Contains($"/{layout.Platform}/", StringComparison.Ordinal));
        var otherPlatform = otherTarget.Split('/')[2];
        switch (kind)
        {
            case "head": layout.Files.Git("commit", "--allow-empty", "-m", "Later checkpoint"); break;
            case "branch": layout.Files.Git("switch", "-c", "other-branch"); break;
            case "runtime": layout.EditRuntime("codex", "Shared revision.", "Later revision."); break;
            case "other-runtime": layout.EditRuntime("copilot", "Provider mechanics.", "Other revision."); break;
            case "runtime-config": File.WriteAllText(layout.ConfigRuntime, "personality = 'pragmatic'\n"); break;
            case "marker": layout.Files.Write(MigrationPreflight.FormatRelativePath, "2.0\n"); break;
            case "source": layout.Files.Write("environment/shared/instructions.md", "Later shared draft.\n"); break;
            case "other-platform-source": layout.Files.Write($"environment/platforms/{otherPlatform}/policy.md", "Other policy draft.\n"); break;
            case "canonical-config": layout.Files.Write(AecApplication.ConfigSourceRelativePath, "personality = 'none'\n"); break;
            case "target": layout.Files.Write(layout.Target("codex"), "Unexpected target draft.\n"); break;
            case "other-platform-target": layout.Files.Write(otherTarget, "Unexpected other target.\n"); break;
            case "staged": layout.Files.Git("add", "--", sources[0]); break;
            default:
                using (var index = new FileStream(Path.Combine(layout.Repository, ".git", "index"), FileMode.Append)) index.WriteByte(0);
                break;
        }

        layout.Files.AssertNoChanges(() =>
        {
            var error = Assert.Throws<MigrationPortableTargetWriteException>(() => plan.Write(sources));
            Assert.Empty(error.WrittenPaths);
            Assert.Null(error.AttemptedPath);
        });
    }

    [Theory]
    [InlineData("dirty-target")]
    [InlineData("dirty-source")]
    [InlineData("missing-target")]
    [InlineData("inconsistent-target")]
    [InlineData("invalid-enrollment")]
    [InlineData("invalid-provider")]
    [InlineData("invalid-filename")]
    [InlineData("invalid-structure")]
    [InlineData("oversize-render")]
    public void OtherPlatformProblemsAreRejectedDuringPreparation(string kind)
    {
        using var layout = new Layout("codex", "copilot");
        var targets = EnrollOtherPlatforms(layout, "codex", "copilot");
        var otherTarget = targets.First(path => !path.Contains($"/{layout.Platform}/", StringComparison.Ordinal));
        var policy = $"environment/platforms/{otherTarget.Split('/')[2]}/policy.md";
        layout.EditRuntime("codex", "Shared approval.", kind == "oversize-render" ? new string('y', 500) : "Shared revision.");
        var reviewed = Preview(layout);
        switch (kind)
        {
            case "dirty-target": layout.Files.Write(otherTarget, "Keep target draft.\n"); break;
            case "dirty-source": layout.Files.Write(policy, "Keep policy draft.\n"); break;
            case "missing-target": File.Delete(Path.Combine(layout.Repository, otherTarget)); break;
            case "inconsistent-target": layout.Files.Write(otherTarget, "Inconsistent committed target.\n"); layout.Commit(otherTarget); break;
            case "invalid-enrollment":
            case "invalid-provider":
            case "invalid-filename":
            case "invalid-structure":
                var invalidTarget = kind switch
                {
                    "invalid-enrollment" => "environment/targets/unknown/codex/AGENTS.md",
                    "invalid-provider" => $"environment/targets/{layout.Platform}/unknown/AGENTS.md",
                    "invalid-filename" => $"environment/targets/{layout.Platform}/codex/wrong.md",
                    _ => $"environment/targets/{layout.Platform}/wrong.md"
                };
                layout.Files.Write(invalidTarget, "Unknown target.\n");
                layout.Commit(invalidTarget);
                break;
            default:
                var large = new string('x', AecApplication.MaximumTextBytes - 300);
                layout.Files.Write(policy, large);
                foreach (var target in targets.Where(path => path.Split('/')[2] == otherTarget.Split('/')[2]))
                    layout.Files.Write(target, InstructionSections.Render("Shared approval.\n", large, "Provider mechanics.\n"));
                layout.Commit([policy, .. targets.Where(path => path.Split('/')[2] == otherTarget.Split('/')[2])]);
                break;
        }
        if (kind is "inconsistent-target" or "invalid-enrollment" or "invalid-provider" or
            "invalid-filename" or "invalid-structure" or "oversize-render")
        {
            reviewed = Preview(layout);
        }

        layout.Files.AssertNoChanges(() =>
        {
            var error = Record.Exception(() => MigrationPortableTargetWriter.Prepare(reviewed));
            Assert.NotNull(error);
            Assert.True(error is InvalidOperationException or InvalidDataException, error.ToString());
        });
    }

    [Fact]
    public void CallerMutationAfterPreparationCannotChangePrivateProposals()
    {
        using var layout = new Layout("codex");
        layout.EditRuntime("codex", "Provider mechanics.", "Reviewed revision.");
        var reviewed = Preview(layout);
        var plan = MigrationPortableTargetWriter.Prepare(reviewed);
        var sources = MigrationPortableSourceWriter.Write(reviewed);
        Array.Fill(reviewed.Codex!.Snapshot.Target, (byte)'x');
        Array.Fill(reviewed.Codex.RuntimeConfig, (byte)'x');

        layout.Files.AssertOnlyChanges([layout.Target("codex")], () => Assert.Equal(layout.Target("codex"), Assert.Single(plan.Write(sources))));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailureReportsCompletedAndPossiblyChangedTargetsWithoutRollback(bool earlierWrite)
    {
        using var layout = new Layout("codex", "copilot");
        if (earlierWrite) layout.EditRuntime("codex", "Provider mechanics.", "First revision.");
        layout.EditRuntime("copilot", "Provider mechanics.", "Blocked revision.");
        var (plan, sources) = PrepareSources(layout);
        var blocked = layout.Target("copilot");
        string[] changed = earlierWrite ? [layout.Target("codex")] : [];

        layout.Files.AssertOnlyChanges(changed, () => MigrationLegacyCaptureWriterTests.BlockReplacement(
            Path.Combine(layout.Repository, blocked), () =>
            {
                var error = Assert.Throws<MigrationPortableTargetWriteException>(() => plan.Write(sources));
                Assert.Equal(blocked, error.AttemptedPath);
                Assert.Equal(changed, error.WrittenPaths);
                Assert.NotNull(error.InnerException);
                Assert.Contains("No rollback, authored-file write, staging, commit or runtime apply", error.Message, StringComparison.Ordinal);
            }));
    }

    private static (MigrationPortableTargetWriter Plan, IReadOnlyList<string> Sources) PrepareSources(Layout layout)
    {
        var reviewed = Preview(layout);
        var plan = MigrationPortableTargetWriter.Prepare(reviewed);
        return (plan, MigrationPortableSourceWriter.Write(reviewed));
    }

    private static MigrationPortableCapturePreview Preview(Layout layout) => MigrationPortableCapturePreview.Read(layout.Context());

    // These are committed enrollments, not simulated operating systems or new
    // local harnesses. Only deterministic target composition is cross-platform.
    internal static string[] EnrollOtherPlatforms(Layout layout, params string[] providers)
    {
        var committed = new List<string>();
        var targets = providers.Select(layout.Target).ToList();
        foreach (var platform in Platforms.Where(value => value != layout.Platform))
        {
            var policy = $"environment/platforms/{platform}/policy.md";
            var content = $"Policy for {platform}.\n";
            layout.Files.Write(policy, content);
            committed.Add(policy);
            foreach (var provider in providers)
            {
                var target = $"environment/targets/{platform}/{provider}/" + (provider == "codex" ? "AGENTS.md" : "copilot-instructions.md");
                layout.Files.Write(target, InstructionSections.Render("Shared approval.\n", content, "Provider mechanics.\n"));
                committed.Add(target);
                targets.Add(target);
            }
        }
        layout.Commit([.. committed]);
        return targets.Order(StringComparer.Ordinal).ToArray();
    }
}
