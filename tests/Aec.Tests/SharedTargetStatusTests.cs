using System.Text;

namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class SharedTargetStatusTests
{
    [Theory]
    [InlineData("codex")]
    [InlineData("copilot")]
    public void ExactCommittedTargetAndBoundRuntimeAreInSyncWithoutWrites(string provider)
    {
        using var layout = new StatusLayout(provider);
        layout.WriteRuntime(portableBlock: true);
        var head = layout.Head();
        var runtime = File.ReadAllBytes(layout.RuntimePath);

        var result = SharedTargetStatus.Inspect(
            layout.Repository, provider, layout.ProviderHome);

        Assert.Equal("in_sync", result);
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(head, layout.Head());
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("copilot")]
    public void BoundRuntimeDriftIsDifferent(string provider)
    {
        using var layout = new StatusLayout(provider);
        layout.WriteRuntime(portableBlock: true);
        File.AppendAllText(layout.RuntimePath, "Unreviewed runtime edit.");
        var runtime = File.ReadAllBytes(layout.RuntimePath);

        var result = SharedTargetStatus.Inspect(
            layout.Repository, provider, layout.ProviderHome);

        Assert.Equal("different", result);
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void CodexChatGptManagedBlockIsPreservedInExpectedRuntime()
    {
        using var layout = new StatusLayout("codex");
        layout.WriteRuntime(codexChatGptBlock: true, portableBlock: true);

        var result = SharedTargetStatus.Inspect(
            layout.Repository, "codex", layout.ProviderHome);

        Assert.Equal("in_sync", result);
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Theory]
    [InlineData("codex", false)]
    [InlineData("codex", true)]
    [InlineData("copilot", false)]
    public void LegacyManagedBlockIsReadableButNeedsV2Replacement(
        string provider,
        bool includeChatGptGuidance)
    {
        using var layout = new StatusLayout(provider);
        layout.WriteRuntime(codexChatGptBlock: includeChatGptGuidance);

        var result = SharedTargetStatus.Inspect(
            layout.Repository, provider, layout.ProviderHome);

        Assert.Equal("different", result);
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Theory]
    [InlineData("codex", false)]
    [InlineData("codex", true)]
    [InlineData("copilot", false)]
    public void PortableManagedBlockAndCommittedTargetAreInSync(
        string provider,
        bool includeChatGptGuidance)
    {
        using var layout = new StatusLayout(provider);
        layout.WriteRuntime(codexChatGptBlock: includeChatGptGuidance,
            portableBlock: true);

        var result = SharedTargetStatus.Inspect(
            layout.Repository, provider, layout.ProviderHome);

        Assert.Equal("in_sync", result);
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Theory]
    [InlineData("codex", false)]
    [InlineData("codex", true)]
    [InlineData("copilot", false)]
    public void ApplyPreviewIsUnchangedForExactV2Runtime(
        string provider,
        bool includeChatGptGuidance)
    {
        using var layout = new StatusLayout(provider);
        layout.WriteRuntime(codexChatGptBlock: includeChatGptGuidance,
            portableBlock: true);
        var runtime = File.ReadAllBytes(layout.RuntimePath);
        var head = layout.Head();

        var preview = PortableApplyPreview.Create(
            layout.Repository, provider, layout.ProviderHome);

        Assert.False(preview.WouldReplace);
        Assert.Equal(layout.RuntimePath, preview.RuntimePath);
        Assert.Equal(runtime, preview.DesiredRuntime);
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(head, layout.Head());
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Theory]
    [InlineData("codex", false, "version=9")]
    [InlineData("codex", true, "version=10")]
    [InlineData("copilot", false, "version=2")]
    public void ApplyPreviewWouldUpgradeOnlyTheLocalBlock(
        string provider,
        bool includeChatGptGuidance,
        string expectedVersion)
    {
        using var layout = new StatusLayout(provider);
        layout.WriteRuntime(codexChatGptBlock: includeChatGptGuidance);
        var runtime = File.ReadAllBytes(layout.RuntimePath);

        var preview = PortableApplyPreview.Create(
            layout.Repository, provider, layout.ProviderHome);
        var desired = Encoding.UTF8.GetString(preview.DesiredRuntime);

        Assert.True(preview.WouldReplace);
        Assert.Equal("different", SharedTargetStatus.Inspect(
            layout.Repository, provider, layout.ProviderHome));
        Assert.Contains(expectedVersion, desired, StringComparison.Ordinal);
        Assert.Contains("<!-- AEC:SOURCE:SHARED:BEGIN -->", desired,
            StringComparison.Ordinal);
        Assert.Equal(includeChatGptGuidance,
            desired.Contains("Manual ChatGPT instruction backups live under",
                StringComparison.Ordinal));
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("copilot")]
    public void BackupAttributionPreviewReportsNoChangeWithoutWriting(string provider)
    {
        using var layout = new StatusLayout(provider);
        layout.WriteRuntime(portableBlock: true);
        var original = File.ReadAllBytes(layout.RuntimePath);
        var head = layout.Head();

        var preview = PortableBackupAttributionPreview.Create(
            layout.Repository, provider, layout.ProviderHome);

        Assert.Null(preview.Section);
        Assert.Null(preview.SourceRelativePath);
        Assert.Null(preview.ProposedContent);
        Assert.Equal(original, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(head, layout.Head());
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Theory]
    [InlineData("codex", "Shared approval.", "Shared revision.", "shared",
        "environment/shared/instructions.md")]
    [InlineData("codex", "Local paths.", "Local revision.", "platform", "platform")]
    [InlineData("codex", "Provider mechanics.", "Provider revision.", "provider",
        "environment/providers/codex/overlay.md")]
    [InlineData("copilot", "Shared approval.", "Shared revision.", "shared",
        "environment/shared/instructions.md")]
    [InlineData("copilot", "Local paths.", "Local revision.", "platform", "platform")]
    [InlineData("copilot", "Provider mechanics.", "Provider revision.", "provider",
        "environment/providers/copilot/overlay.md")]
    public void BackupAttributionPreviewIdentifiesOneAuthoredSection(
        string provider,
        string original,
        string replacement,
        string section,
        string expectedPath)
    {
        using var layout = new StatusLayout(provider);
        layout.WriteRuntime(portableBlock: true);
        var runtime = File.ReadAllText(layout.RuntimePath);
        File.WriteAllText(layout.RuntimePath,
            runtime.Replace(original, replacement, StringComparison.Ordinal));
        var edited = File.ReadAllBytes(layout.RuntimePath);
        var head = layout.Head();

        var preview = PortableBackupAttributionPreview.Create(
            layout.Repository, provider, layout.ProviderHome);

        Assert.Equal(section, preview.Section);
        Assert.Equal(expectedPath == "platform"
            ? $"environment/platforms/{RenderCommand.CurrentPlatform()}/policy.md"
            : expectedPath, preview.SourceRelativePath);
        Assert.Equal(replacement + "\n", preview.ProposedContent);
        Assert.Equal(edited, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(head, layout.Head());
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("copilot")]
    public void BackupAttributionPreviewStopsOnMultipleAuthoredSections(string provider)
    {
        using var layout = new StatusLayout(provider);
        layout.WriteRuntime(portableBlock: true);
        var runtime = File.ReadAllText(layout.RuntimePath)
            .Replace("Shared approval.", "Shared revision.", StringComparison.Ordinal)
            .Replace("Local paths.", "Local revision.", StringComparison.Ordinal);
        File.WriteAllText(layout.RuntimePath, runtime);

        var error = Assert.Throws<InvalidDataException>(() =>
            PortableBackupAttributionPreview.Create(
                layout.Repository, provider, layout.ProviderHome));

        Assert.Contains("multiple", error.Message, StringComparison.Ordinal);
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void ApplyPreviewReportsRuntimeEditsWithoutWriting()
    {
        using var layout = new StatusLayout("codex");
        layout.WriteRuntime(portableBlock: true);
        File.AppendAllText(layout.RuntimePath, "Unreviewed runtime edit.");
        var runtime = File.ReadAllBytes(layout.RuntimePath);

        var preview = PortableApplyPreview.Create(
            layout.Repository, "codex", layout.ProviderHome);

        Assert.True(preview.WouldReplace);
        Assert.DoesNotContain("Unreviewed runtime edit.",
            Encoding.UTF8.GetString(preview.DesiredRuntime), StringComparison.Ordinal);
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("copilot")]
    public void ApplyPreviewStopsWhenRuntimeIsMissingOrBoundElsewhere(string provider)
    {
        using var layout = new StatusLayout(provider);
        var missing = Assert.Throws<InvalidOperationException>(() =>
            PortableApplyPreview.Create(layout.Repository, provider, layout.ProviderHome));
        Assert.Contains("Run ordinary provider init first", missing.Message,
            StringComparison.Ordinal);

        layout.WriteRuntime(Path.Combine(layout.Root, "other-data"), portableBlock: true);
        var wrongBinding = Assert.Throws<InvalidOperationException>(() =>
            PortableApplyPreview.Create(layout.Repository, provider, layout.ProviderHome));
        Assert.Contains("not bound to the selected AEC repository", wrongBinding.Message,
            StringComparison.Ordinal);
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("copilot")]
    public void ApplyPreviewStopsForAnotherPlatformBinding(string provider)
    {
        using var layout = new StatusLayout(provider);
        var otherPlatform = RenderCommand.CurrentPlatform() == "macos-arm64"
            ? "windows-x64"
            : "macos-arm64";
        layout.WriteRuntime(portableBlock: true, platformOverride: otherPlatform);

        var error = Assert.Throws<InvalidOperationException>(() =>
            PortableApplyPreview.Create(layout.Repository, provider, layout.ProviderHome));

        Assert.Contains("different AEC platform", error.Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() =>
            SharedTargetStatus.Inspect(layout.Repository, provider, layout.ProviderHome));
    }

    [Fact]
    public void ApplyPreviewRejectsUncommittedTargetBeforeComparingRuntime()
    {
        using var layout = new StatusLayout("codex");
        layout.WriteRuntime(portableBlock: true);
        File.AppendAllText(layout.TargetPath, "Uncommitted target edit.");
        var repositoryStatus = layout.GitStatus();

        var error = Assert.Throws<InvalidOperationException>(() =>
            PortableApplyPreview.Create(layout.Repository, "codex", layout.ProviderHome));

        Assert.Contains("Committed target has unstaged changes", error.Message,
            StringComparison.Ordinal);
        Assert.Equal(repositoryStatus, layout.GitStatus());
    }

    [Theory]
    [InlineData("none", false)]
    [InlineData("friendly", true)]
    public void CodexApplyPreviewPlansOnlyManagedConfigValueWithoutWriting(
        string runtimePersonality,
        bool wouldReplace)
    {
        using var layout = new StatusLayout("codex");
        layout.WriteRuntime(portableBlock: true);
        var configPath = Path.Combine(layout.ProviderHome, "config.toml");
        var runtimeConfig = Encoding.UTF8.GetBytes(
            $"personality = \"{runtimePersonality}\"\nmodel = \"keep-me\"\n");
        File.WriteAllBytes(configPath, runtimeConfig);
        var head = layout.Head();

        var preview = PortableApplyPreview.Create(
            layout.Repository, "codex", layout.ProviderHome);

        var config = Assert.IsType<PortableCodexConfigPreview>(preview.CodexConfig);
        Assert.Equal(configPath, config.RuntimePath);
        Assert.Equal(runtimeConfig, config.Runtime);
        Assert.Equal(wouldReplace, config.WouldReplace);
        Assert.False(config.Inserted);
        Assert.Equal("personality = \"none\"\nmodel = \"keep-me\"\n",
            Encoding.UTF8.GetString(config.DesiredRuntime));
        Assert.Equal(runtimeConfig, File.ReadAllBytes(configPath));
        Assert.Equal(head, layout.Head());
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Theory]
    [InlineData(null, "personality = \"none\"\n")]
    [InlineData("model = \"keep-me\"\n", "personality = \"none\"\nmodel = \"keep-me\"\n")]
    public void CodexApplyPreviewPlansMissingRuntimePersonalityWithoutWriting(
        string? runtimeText,
        string expected)
    {
        using var layout = new StatusLayout("codex");
        layout.WriteRuntime(portableBlock: true);
        var configPath = Path.Combine(layout.ProviderHome, "config.toml");
        if (runtimeText is not null)
        {
            File.WriteAllText(configPath, runtimeText);
        }

        var preview = PortableApplyPreview.Create(
            layout.Repository, "codex", layout.ProviderHome);

        var config = Assert.IsType<PortableCodexConfigPreview>(preview.CodexConfig);
        Assert.True(config.WouldReplace);
        Assert.True(config.Inserted);
        Assert.Equal(expected, Encoding.UTF8.GetString(config.DesiredRuntime));
        Assert.Equal(runtimeText is null, config.Runtime is null);
        Assert.Equal(runtimeText is not null, File.Exists(configPath));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void CodexApplyPreviewStopsForInvalidRuntimeConfig()
    {
        using var layout = new StatusLayout("codex");
        layout.WriteRuntime(portableBlock: true);
        var configPath = Path.Combine(layout.ProviderHome, "config.toml");
        var runtimeConfig = "personality = \"friendly\"\npersonality = \"none\"\n";
        File.WriteAllText(configPath, runtimeConfig);

        var error = Assert.Throws<InvalidDataException>(() =>
            PortableApplyPreview.Create(layout.Repository, "codex", layout.ProviderHome));

        Assert.Contains("more than once", error.Message, StringComparison.Ordinal);
        Assert.Equal(runtimeConfig, File.ReadAllText(configPath));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CodexApplyPreviewStopsForUncommittedCanonicalConfig(bool staged)
    {
        using var layout = new StatusLayout("codex");
        layout.WriteRuntime(portableBlock: true);
        File.WriteAllText(layout.CanonicalConfigPath, "personality = \"friendly\"\n");
        if (staged)
        {
            GitProcess.RunRequired(layout.Repository, "Could not stage fixture config",
                "add", "--", AecApplication.ConfigSourceRelativePath);
        }

        var error = Assert.Throws<InvalidOperationException>(() =>
            PortableApplyPreview.Create(layout.Repository, "codex", layout.ProviderHome));

        Assert.Contains(staged ? "staged changes" : "unstaged changes",
            error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CodexApplyPreviewStopsForMissingCanonicalConfig()
    {
        using var layout = new StatusLayout("codex");
        layout.WriteRuntime(portableBlock: true);
        File.Delete(layout.CanonicalConfigPath);

        var error = Assert.Throws<FileNotFoundException>(() =>
            PortableApplyPreview.Create(layout.Repository, "codex", layout.ProviderHome));

        Assert.Contains("Canonical config does not exist", error.Message,
            StringComparison.Ordinal);
        Assert.True(File.Exists(layout.RuntimePath));
    }

    [Fact]
    public void CodexApplyPreviewStopsForInvalidCommittedCanonicalConfig()
    {
        using var layout = new StatusLayout("codex");
        layout.WriteRuntime(portableBlock: true);
        File.WriteAllText(layout.CanonicalConfigPath, "personality = \"unknown\"\n");
        GitProcess.RunRequired(layout.Repository, "Could not stage fixture config",
            "add", "--", AecApplication.ConfigSourceRelativePath);
        GitProcess.RunRequired(layout.Repository, "Could not commit fixture config",
            "-c", "user.name=AEC Test", "-c", "user.email=aec-test@example.invalid",
            "-c", "commit.gpgsign=false", "commit", "-m", "Invalid canonical config");

        var error = Assert.Throws<InvalidDataException>(() =>
            PortableApplyPreview.Create(layout.Repository, "codex", layout.ProviderHome));

        Assert.Contains("personality", error.Message, StringComparison.Ordinal);
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void CopilotApplyPreviewDoesNotRequireCodexConfig()
    {
        using var layout = new StatusLayout("copilot");
        layout.WriteRuntime(portableBlock: true);

        var preview = PortableApplyPreview.Create(
            layout.Repository, "copilot", layout.ProviderHome);

        Assert.Null(preview.CodexConfig);
        Assert.False(preview.WouldReplace);
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void CodexCombinedWriterUpdatesConfigBeforeInstructionsAndIsIdempotent()
    {
        using var layout = new StatusLayout("codex");
        layout.WriteRuntime();
        var originalInstructions = File.ReadAllBytes(layout.RuntimePath);
        var configPath = Path.Combine(layout.ProviderHome, "config.toml");
        File.WriteAllText(configPath,
            "personality = \"friendly\"\nmodel = \"keep-me\"\n");
        var head = layout.Head();
        var reviewed = PortableApplyPreview.Create(
            layout.Repository, "codex", layout.ProviderHome);

        var result = PortableCodexWriter.Apply(reviewed, () =>
        {
            Assert.Equal("personality = \"none\"\nmodel = \"keep-me\"\n",
                File.ReadAllText(configPath));
            Assert.Equal(originalInstructions, File.ReadAllBytes(layout.RuntimePath));
        });
        var after = PortableApplyPreview.Create(
            layout.Repository, "codex", layout.ProviderHome);

        Assert.Equal("applied", result);
        Assert.Equal("unchanged", PortableCodexWriter.Apply(after));
        Assert.False(after.WouldReplace);
        Assert.False(after.CodexConfig!.WouldReplace);
        Assert.Equal(reviewed.DesiredRuntime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(head, layout.Head());
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void CodexCombinedWriterCreatesMissingRuntimeConfigOnly()
    {
        using var layout = new StatusLayout("codex");
        layout.WriteRuntime(portableBlock: true);
        var instructions = File.ReadAllBytes(layout.RuntimePath);
        var configPath = Path.Combine(layout.ProviderHome, "config.toml");
        var reviewed = PortableApplyPreview.Create(
            layout.Repository, "codex", layout.ProviderHome);

        var result = PortableCodexWriter.Apply(reviewed);

        Assert.Equal("applied", result);
        Assert.Equal("personality = \"none\"\n", File.ReadAllText(configPath));
        Assert.Equal(instructions, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(string.Empty, layout.GitStatus());
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(configPath));
        }
    }

    [Fact]
    public void CodexCombinedWriterChangesInstructionsWithoutRewritingMatchingConfig()
    {
        using var layout = new StatusLayout("codex");
        layout.WriteRuntime();
        var configPath = Path.Combine(layout.ProviderHome, "config.toml");
        var config = "personality = \"none\"\nmodel = \"keep-me\"\n"u8.ToArray();
        File.WriteAllBytes(configPath, config);
        var reviewed = PortableApplyPreview.Create(
            layout.Repository, "codex", layout.ProviderHome);

        var result = PortableCodexWriter.Apply(reviewed);

        Assert.Equal("applied", result);
        Assert.Equal(config, File.ReadAllBytes(configPath));
        Assert.Equal(reviewed.DesiredRuntime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CodexCombinedWriterStopsBeforeEitherWriteWhenReviewedInputChanges(
        bool changeCanonical)
    {
        using var layout = new StatusLayout("codex");
        layout.WriteRuntime();
        var configPath = Path.Combine(layout.ProviderHome, "config.toml");
        File.WriteAllText(configPath, "personality = \"friendly\"\n");
        var reviewed = PortableApplyPreview.Create(
            layout.Repository, "codex", layout.ProviderHome);
        var instructions = File.ReadAllBytes(layout.RuntimePath);
        if (changeCanonical)
        {
            File.AppendAllText(layout.CanonicalConfigPath, "# new canonical edit\n");
        }
        else
        {
            File.AppendAllText(configPath, "# new runtime edit\n");
        }
        var changedConfig = File.ReadAllBytes(configPath);

        Assert.Throws<InvalidOperationException>(() => PortableCodexWriter.Apply(reviewed));

        Assert.Equal(changedConfig, File.ReadAllBytes(configPath));
        Assert.Equal(instructions, File.ReadAllBytes(layout.RuntimePath));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CodexCombinedWriterReportsPartialUpdateWhenInputChangesAfterConfig(
        bool changeConfig)
    {
        using var layout = new StatusLayout("codex");
        layout.WriteRuntime();
        var configPath = Path.Combine(layout.ProviderHome, "config.toml");
        File.WriteAllText(configPath, "personality = \"friendly\"\n");
        var reviewed = PortableApplyPreview.Create(
            layout.Repository, "codex", layout.ProviderHome);

        var error = Assert.Throws<InvalidOperationException>(() =>
            PortableCodexWriter.Apply(reviewed, () =>
                File.AppendAllText(
                    changeConfig ? configPath : layout.RuntimePath,
                    "Concurrent edit.\n")));

        Assert.Contains("after the config step", error.Message,
            StringComparison.Ordinal);
        Assert.Contains("no automatic rollback", error.Message,
            StringComparison.Ordinal);
        Assert.StartsWith("personality = \"none\"\n", File.ReadAllText(configPath),
            StringComparison.Ordinal);
        Assert.Equal(changeConfig,
            File.ReadAllText(configPath).Contains("Concurrent edit.", StringComparison.Ordinal));
        Assert.Equal(!changeConfig,
            File.ReadAllText(layout.RuntimePath).Contains("Concurrent edit.",
                StringComparison.Ordinal));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void CodexCombinedWriterStopsBeforeWritingWhenHeadChangesAfterReview()
    {
        using var layout = new StatusLayout("codex");
        layout.WriteRuntime();
        var reviewed = PortableApplyPreview.Create(
            layout.Repository, "codex", layout.ProviderHome);
        var instructions = File.ReadAllBytes(layout.RuntimePath);
        layout.CommitEmpty();

        var error = Assert.Throws<InvalidOperationException>(() =>
            PortableCodexWriter.Apply(reviewed));

        Assert.Contains("changed after preflight", error.Message,
            StringComparison.Ordinal);
        Assert.Equal(instructions, File.ReadAllBytes(layout.RuntimePath));
        Assert.False(File.Exists(Path.Combine(layout.ProviderHome, "config.toml")));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void CodexCombinedWriterReportsPartialUpdateWhenHeadChangesAfterConfig()
    {
        using var layout = new StatusLayout("codex");
        layout.WriteRuntime();
        var instructions = File.ReadAllBytes(layout.RuntimePath);
        var configPath = Path.Combine(layout.ProviderHome, "config.toml");
        File.WriteAllText(configPath, "personality = \"friendly\"\n");
        var reviewed = PortableApplyPreview.Create(
            layout.Repository, "codex", layout.ProviderHome);

        var error = Assert.Throws<InvalidOperationException>(() =>
            PortableCodexWriter.Apply(reviewed, layout.CommitEmpty));

        Assert.Contains("after the config step", error.Message,
            StringComparison.Ordinal);
        Assert.Equal("personality = \"none\"\n", File.ReadAllText(configPath));
        Assert.Equal(instructions, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void CodexCombinedWriterRejectsCopilotPreview()
    {
        using var layout = new StatusLayout("copilot");
        layout.WriteRuntime(portableBlock: true);
        var reviewed = PortableApplyPreview.Create(
            layout.Repository, "copilot", layout.ProviderHome);

        Assert.Throws<ArgumentException>(() => PortableCodexWriter.Apply(reviewed));

        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Theory]
    [InlineData("codex", false)]
    [InlineData("codex", true)]
    [InlineData("copilot", false)]
    public void InternalWriterAppliesReviewedDriftAndIsIdempotent(
        string provider,
        bool includeChatGptGuidance)
    {
        using var layout = new StatusLayout(provider);
        layout.WriteRuntime(codexChatGptBlock: includeChatGptGuidance);
        var configPath = Path.Combine(layout.ProviderHome, "config.toml");
        var config = "personality = \"friendly\"\n# keep this setting\n"u8.ToArray();
        File.WriteAllBytes(configPath, config);
        var head = layout.Head();
        var preview = PortableApplyPreview.Create(
            layout.Repository, provider, layout.ProviderHome);
        var reviewed = PortableRuntimeSnapshot.Read(
            layout.Repository, provider, layout.ProviderHome);

        var applied = PortableInstructionWriter.Apply(reviewed);
        var afterApply = PortableRuntimeSnapshot.Read(
            layout.Repository, provider, layout.ProviderHome);
        var repeated = PortableInstructionWriter.Apply(afterApply);

        Assert.True(preview.WouldReplace);
        Assert.Equal("applied", applied);
        Assert.Equal("unchanged", repeated);
        Assert.Equal(preview.DesiredRuntime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal("in_sync", SharedTargetStatus.Inspect(
            layout.Repository, provider, layout.ProviderHome));
        Assert.Equal(config, File.ReadAllBytes(configPath));
        Assert.Equal(head, layout.Head());
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void InternalWriterMayReplaceRuntimeOnlyDriftAfterReview()
    {
        using var layout = new StatusLayout("codex");
        layout.WriteRuntime(portableBlock: true);
        File.AppendAllText(layout.RuntimePath, "Runtime-only edit, not committed.\n");
        var reviewed = PortableRuntimeSnapshot.Read(
            layout.Repository, "codex", layout.ProviderHome);

        var result = PortableInstructionWriter.Apply(reviewed);

        Assert.Equal("applied", result);
        Assert.DoesNotContain("Runtime-only edit", File.ReadAllText(layout.RuntimePath),
            StringComparison.Ordinal);
        Assert.Equal("in_sync", SharedTargetStatus.Inspect(
            layout.Repository, "codex", layout.ProviderHome));
    }

    [Fact]
    public void InternalWriterStopsWhenRuntimeChangesAfterPreflight()
    {
        using var layout = new StatusLayout("codex");
        layout.WriteRuntime();
        var reviewed = PortableRuntimeSnapshot.Read(
            layout.Repository, "codex", layout.ProviderHome);
        File.AppendAllText(layout.RuntimePath, "New edit after preflight.\n");
        var changedRuntime = File.ReadAllBytes(layout.RuntimePath);

        var error = Assert.Throws<InvalidOperationException>(() =>
            PortableInstructionWriter.Apply(reviewed));

        Assert.Contains("changed after preflight", error.Message, StringComparison.Ordinal);
        Assert.Equal(changedRuntime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void InternalWriterStopsWhenHeadChangesAfterPreflight()
    {
        using var layout = new StatusLayout("copilot");
        layout.WriteRuntime();
        var reviewed = PortableRuntimeSnapshot.Read(
            layout.Repository, "copilot", layout.ProviderHome);
        var originalRuntime = File.ReadAllBytes(layout.RuntimePath);
        layout.CommitEmpty();

        var error = Assert.Throws<InvalidOperationException>(() =>
            PortableInstructionWriter.Apply(reviewed));

        Assert.Contains("changed after preflight", error.Message, StringComparison.Ordinal);
        Assert.Equal(originalRuntime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void InternalWriterStopsWhenTargetChangesAfterPreflight()
    {
        using var layout = new StatusLayout("codex");
        layout.WriteRuntime();
        var reviewed = PortableRuntimeSnapshot.Read(
            layout.Repository, "codex", layout.ProviderHome);
        var originalRuntime = File.ReadAllBytes(layout.RuntimePath);
        File.AppendAllText(layout.TargetPath, "Uncommitted target edit.\n");
        var repositoryStatus = layout.GitStatus();

        var error = Assert.Throws<InvalidOperationException>(() =>
            PortableInstructionWriter.Apply(reviewed));

        Assert.Contains("Committed target has unstaged changes", error.Message,
            StringComparison.Ordinal);
        Assert.Equal(originalRuntime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(repositoryStatus, layout.GitStatus());
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("copilot")]
    public void MissingRuntimeIsReportedWithoutAssumingLocalEnrollment(string provider)
    {
        using var layout = new StatusLayout(provider);

        var result = SharedTargetStatus.Inspect(
            layout.Repository, provider, layout.ProviderHome);

        Assert.Equal("missing", result);
        Assert.False(File.Exists(layout.RuntimePath));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("copilot")]
    public void RuntimeBoundToAnotherRepositoryIsRejected(string provider)
    {
        using var layout = new StatusLayout(provider);
        layout.WriteRuntime(Path.Combine(layout.Root, "other-data"));

        var error = Assert.Throws<InvalidOperationException>(() =>
            SharedTargetStatus.Inspect(layout.Repository, provider, layout.ProviderHome));

        Assert.Contains("not bound to the selected AEC repository", error.Message,
            StringComparison.Ordinal);
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("copilot")]
    public void RuntimeWithoutAManagedBindingIsRejected(string provider)
    {
        using var layout = new StatusLayout(provider);
        File.WriteAllText(layout.RuntimePath, "Local text without an AEC block.");

        var error = Assert.Throws<InvalidOperationException>(() =>
            SharedTargetStatus.Inspect(layout.Repository, provider, layout.ProviderHome));

        Assert.Contains("not bound to the selected AEC repository", error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void UncommittedTargetStopsBeforeReportingRuntimeDrift()
    {
        using var layout = new StatusLayout("codex");
        layout.WriteRuntime();
        File.AppendAllText(layout.TargetPath, "Uncommitted target edit.");
        var repositoryStatus = layout.GitStatus();

        var error = Assert.Throws<InvalidOperationException>(() =>
            SharedTargetStatus.Inspect(layout.Repository, "codex", layout.ProviderHome));

        Assert.Contains("Committed target has unstaged changes", error.Message,
            StringComparison.Ordinal);
        Assert.Equal(repositoryStatus, layout.GitStatus());
    }

    [Fact]
    public void StaleCommittedTargetStopsBeforeReportingRuntimeDrift()
    {
        using var layout = new StatusLayout("codex");
        layout.WriteRuntime();
        File.WriteAllText(layout.SharedSource, "Changed shared approval.\n");
        layout.CommitSource();

        var error = Assert.Throws<InvalidDataException>(() =>
            SharedTargetStatus.Inspect(layout.Repository, "codex", layout.ProviderHome));

        Assert.Contains("does not match committed authored sources", error.Message,
            StringComparison.Ordinal);
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void MissingProviderHomeDoesNotTreatPulledTargetAsInstallation()
    {
        using var layout = new StatusLayout("codex");
        Directory.Delete(layout.ProviderHome);

        Assert.Throws<DirectoryNotFoundException>(() =>
            SharedTargetStatus.Inspect(layout.Repository, "codex", layout.ProviderHome));
    }

    private sealed class StatusLayout : IDisposable
    {
        public StatusLayout(string provider)
        {
            Root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(),
                "aec-shared-status-tests", Guid.NewGuid().ToString("N"));
            Repository = Path.Combine(Root, "data");
            ProviderHome = Path.Combine(Root, "provider-home");
            var platform = RenderCommand.CurrentPlatform();
            var fileName = provider == "codex" ? "AGENTS.md" : "copilot-instructions.md";
            SharedSource = Path.Combine(Repository, "environment", "shared", "instructions.md");
            var policy = Path.Combine(Repository, "environment", "platforms", platform,
                "policy.md");
            var overlay = Path.Combine(Repository, "environment", "providers", provider,
                "overlay.md");
            TargetPath = Path.Combine(Repository, "environment", "targets", platform,
                provider, fileName);
            RuntimePath = Path.Combine(ProviderHome, fileName);
            CanonicalConfigPath = Path.Combine(Repository,
                AecApplication.ConfigSourceRelativePath);

            foreach (var path in new[] { SharedSource, policy, overlay, TargetPath, RuntimePath })
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            }

            var utf8 = new UTF8Encoding(false);
            File.WriteAllText(SharedSource, "Shared approval.\n", utf8);
            File.WriteAllText(policy, "Local paths.\n", utf8);
            File.WriteAllText(overlay, "Provider mechanics.\n", utf8);
            File.WriteAllText(TargetPath, InstructionSections.Render(
                "Shared approval.\n", "Local paths.\n", "Provider mechanics.\n"), utf8);
            if (provider == "codex")
            {
                File.WriteAllText(CanonicalConfigPath, "personality = \"none\"\n", utf8);
            }

            GitProcess.RunRequired(Repository, "Could not initialize Git", "init", "--quiet");
            GitProcess.RunRequired(Repository, "Could not stage fixture files", "add", "--",
                "environment");
            GitProcess.RunRequired(Repository, "Could not commit fixture files",
                "-c", "user.name=AEC Test", "-c", "user.email=aec-test@example.invalid",
                "-c", "commit.gpgsign=false", "commit", "-m", "Fixture baseline");
            Provider = provider;
        }

        private string Provider { get; }
        public string Root { get; }
        public string Repository { get; }
        public string ProviderHome { get; }
        public string SharedSource { get; }
        public string TargetPath { get; }
        public string RuntimePath { get; }
        public string CanonicalConfigPath { get; }

        public void CommitSource()
        {
            GitProcess.RunRequired(Repository, "Could not stage shared source", "add", "--",
                "environment/shared/instructions.md");
            GitProcess.RunRequired(Repository, "Could not commit shared source",
                "-c", "user.name=AEC Test", "-c", "user.email=aec-test@example.invalid",
                "-c", "commit.gpgsign=false", "commit", "-m", "Changed shared source");
        }

        public void CommitEmpty()
        {
            GitProcess.RunRequired(Repository, "Could not commit empty fixture change",
                "-c", "user.name=AEC Test", "-c", "user.email=aec-test@example.invalid",
                "-c", "commit.gpgsign=false", "commit", "--allow-empty", "-m",
                "Unrelated committed change");
        }

        public void WriteRuntime(
            string? boundRepository = null,
            bool codexChatGptBlock = false,
            bool portableBlock = false,
            string? platformOverride = null)
        {
            var repository = boundRepository ?? Repository;
            var target = File.ReadAllBytes(TargetPath);
            var platform = platformOverride ?? RenderCommand.CurrentPlatform();
            var runtime = Provider switch
            {
                "codex" when portableBlock => AecInstructionBlock.MergeForPortableTarget(
                    target, repository, platform, codexChatGptBlock),
                "codex" when codexChatGptBlock =>
                    AecInstructionBlock.MergeForChatGptProvider(target, repository),
                "codex" => AecInstructionBlock.Merge(target, repository),
                "copilot" when portableBlock => CopilotInstructionBlock.MergeForPortableTarget(
                    target, repository, platform),
                _ => CopilotInstructionBlock.Merge(target, repository)
            };
            File.WriteAllBytes(RuntimePath, runtime);
        }

        public string Head() =>
            GitProcess.RunRequired(Repository, "Could not read HEAD", "rev-parse", "HEAD")
                .Output.Trim();

        public string GitStatus() =>
            GitProcess.RunRequired(Repository, "Could not inspect status", "status", "--porcelain")
                .Output;

        public void Dispose() => TestDirectoryCleanup.Delete(Root);
    }
}
