using System.Text;

namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class SharedTargetStatusTests
{
    [Theory]
    [InlineData("codex")]
    [InlineData("copilot")]
    public void ApplyPreviewIsUnchangedForExactV2Runtime(string provider)
    {
        using var layout = new StatusLayout(provider);
        layout.WriteRuntime(portableBlock: true);
        var runtime = File.ReadAllBytes(layout.RuntimePath);
        var head = layout.Head();

        var preview = PortableApplyPreview.Create(
            layout.Repository, provider, layout.ProviderHome);

        Assert.False(preview.WouldReplace);
        Assert.Equal(provider == "codex", preview.CodexConfig is not null);
        Assert.Equal(layout.RuntimePath, preview.RuntimePath);
        Assert.Equal(runtime, preview.DesiredRuntime);
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(head, layout.Head());
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Theory]
    [InlineData("codex", "Local paths.", "Local revision.", "platform", "platform")]
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
    [InlineData("codex", "Shared approval.", "shared")]
    [InlineData("copilot", "Provider mechanics.", "provider")]
    public void InternalBackupWriterChangesOnlyTheAttributedSource(
        string provider,
        string original,
        string section)
    {
        using var layout = new StatusLayout(provider);
        layout.WriteRuntime(portableBlock: true);
        File.WriteAllText(layout.RuntimePath,
            File.ReadAllText(layout.RuntimePath).Replace(
                original, "Reviewed revision.", StringComparison.Ordinal));
        var reviewed = ReviewFixture(layout);
        var attribution = PortableBackupAttributionPreview.Create(reviewed);

        var writtenPath = PortableBackupSourceWriter.Write(reviewed, attribution);

        Assert.Equal(section, attribution.Section);
        Assert.Equal(attribution.SourceRelativePath, writtenPath);
        Assert.Equal("Reviewed revision.\n",
            File.ReadAllText(Path.Combine(layout.Repository, writtenPath)));
        Assert.Equal(reviewed.Runtime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(reviewed.Target, File.ReadAllBytes(layout.TargetPath));
        Assert.Equal(reviewed.Commit, layout.Head());
        var change = Assert.Single(layout.GitStatus().Split('\n', StringSplitOptions.RemoveEmptyEntries));
        Assert.Contains(writtenPath, change, StringComparison.Ordinal);
    }

    [Fact]
    public void InternalBackupWriterStopsWhenRuntimeChangesAfterReview()
    {
        using var layout = new StatusLayout("codex");
        layout.WriteRuntime(portableBlock: true);
        File.WriteAllText(layout.RuntimePath, File.ReadAllText(layout.RuntimePath)
            .Replace("Shared approval.", "First revision.", StringComparison.Ordinal));
        var reviewed = ReviewFixture(layout);
        var attribution = PortableBackupAttributionPreview.Create(reviewed);
        File.WriteAllText(layout.RuntimePath, File.ReadAllText(layout.RuntimePath)
            .Replace("First revision.", "Later revision.", StringComparison.Ordinal));
        var source = File.ReadAllBytes(layout.SharedSource);

        var error = Assert.Throws<InvalidOperationException>(() =>
            PortableBackupSourceWriter.Write(reviewed, attribution));
        Assert.Contains("Portable backup inputs changed after review", error.Message, StringComparison.Ordinal);

        Assert.Equal(source, File.ReadAllBytes(layout.SharedSource));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void InternalBackupWriterStopsWhenHeadChangesAfterReview()
    {
        using var layout = new StatusLayout("copilot");
        layout.WriteRuntime(portableBlock: true);
        File.WriteAllText(layout.RuntimePath, File.ReadAllText(layout.RuntimePath)
            .Replace("Shared approval.", "Reviewed revision.", StringComparison.Ordinal));
        var reviewed = ReviewFixture(layout);
        var attribution = PortableBackupAttributionPreview.Create(reviewed);
        layout.CommitEmpty();
        var source = File.ReadAllBytes(layout.SharedSource);

        var error = Assert.Throws<InvalidOperationException>(() =>
            PortableBackupSourceWriter.Write(reviewed, attribution));
        Assert.Contains("Portable backup inputs changed after review", error.Message, StringComparison.Ordinal);

        Assert.Equal(source, File.ReadAllBytes(layout.SharedSource));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void InternalBackupWriterStopsWhenSourceChangesAfterReview()
    {
        using var layout = new StatusLayout("codex");
        layout.WriteRuntime(portableBlock: true);
        File.WriteAllText(layout.RuntimePath, File.ReadAllText(layout.RuntimePath)
            .Replace("Shared approval.", "Reviewed revision.", StringComparison.Ordinal));
        var reviewed = ReviewFixture(layout);
        var attribution = PortableBackupAttributionPreview.Create(reviewed);
        File.WriteAllText(layout.SharedSource, "Concurrent source edit.\n");
        var source = File.ReadAllBytes(layout.SharedSource);

        var error = Assert.Throws<InvalidOperationException>(() =>
            PortableBackupSourceWriter.Write(reviewed, attribution));
        Assert.Contains("Authored shared instructions has unstaged changes", error.Message, StringComparison.Ordinal);

        Assert.Equal(source, File.ReadAllBytes(layout.SharedSource));
        Assert.Contains("environment/shared/instructions.md", layout.GitStatus(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void InternalBackupWriterStopsWhenTargetChangesAfterReview()
    {
        using var layout = new StatusLayout("copilot");
        layout.WriteRuntime(portableBlock: true);
        File.WriteAllText(layout.RuntimePath, File.ReadAllText(layout.RuntimePath)
            .Replace("Shared approval.", "Reviewed revision.", StringComparison.Ordinal));
        var reviewed = ReviewFixture(layout);
        var attribution = PortableBackupAttributionPreview.Create(reviewed);
        File.AppendAllText(layout.TargetPath, "Concurrent target edit.\n");
        var source = File.ReadAllBytes(layout.SharedSource);

        var error = Assert.Throws<InvalidOperationException>(() =>
            PortableBackupSourceWriter.Write(reviewed, attribution));
        Assert.Contains("Committed target has unstaged changes", error.Message, StringComparison.Ordinal);

        Assert.Equal(source, File.ReadAllBytes(layout.SharedSource));
        Assert.Contains("environment/targets/", layout.GitStatus(),
            StringComparison.Ordinal);
    }

    [Fact]
    public void CodexApplyPreviewPlansOnlyManagedConfigValueWithoutWriting()
    {
        using var layout = new StatusLayout("codex");
        layout.WriteRuntime(portableBlock: true);
        var configPath = Path.Combine(layout.ProviderHome, "config.toml");
        var runtimeConfig = "personality = \"friendly\"\nmodel = \"keep-me\"\n"u8.ToArray();
        File.WriteAllBytes(configPath, runtimeConfig);
        var head = layout.Head();

        var preview = PortableApplyPreview.Create(
            layout.Repository, "codex", layout.ProviderHome);

        var config = Assert.IsType<PortableCodexConfigPreview>(preview.CodexConfig);
        Assert.Equal(configPath, config.RuntimePath);
        Assert.Equal(runtimeConfig, config.Runtime);
        Assert.True(config.WouldReplace);
        Assert.False(config.Inserted);
        Assert.Equal("personality = \"none\"\nmodel = \"keep-me\"\n",
            Encoding.UTF8.GetString(config.DesiredRuntime));
        Assert.Equal(runtimeConfig, File.ReadAllBytes(configPath));
        Assert.Equal(head, layout.Head());
        Assert.Equal(string.Empty, layout.GitStatus());
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
    public void CodexCombinedWriterUpdatesConfigBeforeInstructionsAndIsIdempotent()
    {
        using var layout = new StatusLayout("codex");
        layout.WriteRuntime();
        var originalInstructions = File.ReadAllBytes(layout.RuntimePath);
        var configPath = Path.Combine(layout.ProviderHome, "config.toml");
        File.WriteAllText(configPath,
            "personality = \"friendly\"\nmodel = \"keep-me\"\n");
        var reviewed = ReviewCodexApplyFixture(layout);

        var result = PortableCodexWriter.Apply(reviewed, () =>
        {
            Assert.Equal("personality = \"none\"\nmodel = \"keep-me\"\n",
                File.ReadAllText(configPath));
            Assert.Equal(originalInstructions, File.ReadAllBytes(layout.RuntimePath));
        });
        var after = ReviewCodexApplyFixture(layout);

        Assert.Equal("applied", result);
        Assert.Equal("unchanged", PortableCodexWriter.Apply(after));
        Assert.False(after.WouldReplace);
        Assert.False(after.CodexConfig!.WouldReplace);
        Assert.Equal(reviewed.DesiredRuntime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(reviewed.Snapshot.Commit, layout.Head());
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
        var reviewed = ReviewCodexApplyFixture(layout);
        var instructions = reviewed.Snapshot.Runtime;
        if (changeCanonical)
        {
            File.AppendAllText(layout.CanonicalConfigPath, "# new canonical edit\n");
        }
        else
        {
            File.AppendAllText(configPath, "# new runtime edit\n");
        }
        var changedConfig = File.ReadAllBytes(configPath);

        var error = Assert.Throws<InvalidOperationException>(() => PortableCodexWriter.Apply(reviewed));

        Assert.Contains(changeCanonical ? "Canonical config has unstaged changes" : "changed after preflight",
            error.Message, StringComparison.Ordinal);
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
        var reviewed = ReviewCodexApplyFixture(layout);

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
        var reviewed = ReviewCodexApplyFixture(layout);
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
    [Trait("Category", "PortableReview")]
    public void CodexCombinedWriterReportsPartialUpdateWhenHeadChangesAfterConfig()
    {
        using var layout = new StatusLayout("codex");
        layout.WriteRuntime();
        var instructions = File.ReadAllBytes(layout.RuntimePath);
        var configPath = Path.Combine(layout.ProviderHome, "config.toml");
        File.WriteAllText(configPath, "personality = \"friendly\"\n");
        var reviewed = ReviewCodexApplyFixture(layout);

        var error = Assert.Throws<InvalidOperationException>(() =>
            PortableCodexWriter.Apply(reviewed, layout.CommitEmpty));

        Assert.Contains("after the config step", error.Message,
            StringComparison.Ordinal);
        Assert.Equal("personality = \"none\"\n", File.ReadAllText(configPath));
        Assert.Equal(instructions, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Theory]
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
        var preview = PortableApplyPreview.PlanInstructions(ReviewFixture(layout));

        var applied = PortableInstructionWriter.Apply(preview.Snapshot);
        var afterApply = ReviewFixture(layout);
        var repeated = PortableInstructionWriter.Apply(afterApply);

        Assert.True(preview.WouldReplace);
        Assert.Equal("applied", applied);
        Assert.Equal("unchanged", repeated);
        Assert.Equal(preview.DesiredRuntime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal("in_sync", PortableEnvironmentStatus.ClassifyInstructions(afterApply));
        Assert.Equal(config, File.ReadAllBytes(configPath));
        Assert.Equal(preview.Snapshot.Commit, layout.Head());
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void InternalWriterMayReplaceRuntimeOnlyDriftAfterReview()
    {
        using var layout = new StatusLayout("codex");
        var desired = layout.WriteRuntime(portableBlock: true);
        File.AppendAllText(layout.RuntimePath, "Runtime-only edit, not committed.\n");
        var reviewed = ReviewFixture(layout);

        var result = PortableInstructionWriter.Apply(reviewed);

        Assert.Equal("applied", result);
        Assert.Equal(desired, File.ReadAllBytes(layout.RuntimePath));
    }

    [Fact]
    public void InternalWriterStopsWhenRuntimeChangesAfterPreflight()
    {
        using var layout = new StatusLayout("codex");
        layout.WriteRuntime();
        var reviewed = ReviewFixture(layout);
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
        var reviewed = ReviewFixture(layout);
        var originalRuntime = reviewed.Runtime;
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
        var reviewed = ReviewFixture(layout);
        var originalRuntime = reviewed.Runtime;
        File.AppendAllText(layout.TargetPath, "Uncommitted target edit.\n");
        var repositoryStatus = layout.GitStatus();

        var error = Assert.Throws<InvalidOperationException>(() =>
            PortableInstructionWriter.Apply(reviewed));

        Assert.Contains("Committed target has unstaged changes", error.Message,
            StringComparison.Ordinal);
        Assert.Equal(originalRuntime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(repositoryStatus, layout.GitStatus());
    }

    [Fact]
    public void MissingProviderHomeFailsBeforeReadingGit()
    {
        var repositoryPath = Path.Combine(TestTemporaryDirectory.Root, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(repositoryPath);
        try
        {
            var missingHome = Path.Combine(repositoryPath, "missing-home");
            var error = Assert.Throws<DirectoryNotFoundException>(() =>
                PortableEnvironmentStatus.Inspect(repositoryPath, "codex", missingHome));
            Assert.Contains(missingHome, error.Message, StringComparison.Ordinal);
        }
        finally
        {
            TestDirectoryCleanup.Delete(repositoryPath);
        }
    }

    private static PortableRuntimeSnapshot ReviewFixture(StatusLayout layout)
    {
        var platform = RenderCommand.CurrentPlatform();
        var runtime = File.ReadAllBytes(layout.RuntimePath);
        var version = PortableRuntimeSnapshot.ValidateRuntimeBinding(layout.Repository, platform, layout.Provider, runtime);
        return new PortableRuntimeSnapshot(layout.Repository, platform, layout.Provider,
            layout.RuntimePath, layout.Head(), File.ReadAllBytes(layout.TargetPath), runtime, version);
    }

    private static PortableApplyPreview ReviewCodexApplyFixture(StatusLayout layout)
    {
        var preview = PortableApplyPreview.PlanInstructions(ReviewFixture(layout));
        var canonical = File.ReadAllBytes(layout.CanonicalConfigPath);
        var runtimePath = Path.Combine(layout.ProviderHome, "config.toml");
        var runtime = File.Exists(runtimePath) ? File.ReadAllBytes(runtimePath) : null;
        var personality = CodexPersonalityConfig.ReadCanonical(canonical, layout.CanonicalConfigPath);
        var update = CodexPersonalityConfig.PlanRuntimeUpdate(runtime, runtimePath, personality);
        return preview with
        {
            CodexConfig = new PortableCodexConfigPreview(
                runtimePath, canonical, runtime, update.Content, update.Changed, update.Inserted)
        };
    }

    internal sealed class StatusLayout : IDisposable
    {
        public StatusLayout(string provider)
        {
            var temporaryRoot = TestTemporaryDirectory.Root;
            Root = Path.Combine(temporaryRoot,
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

        public string Provider { get; }
        public string Root { get; }
        public string Repository { get; }
        public string ProviderHome { get; }
        public string SharedSource { get; }
        public string TargetPath { get; }
        public string RuntimePath { get; }
        public string CanonicalConfigPath { get; }

        public void CommitEmpty()
        {
            GitProcess.RunRequired(Repository, "Could not commit empty fixture change",
                "-c", "user.name=AEC Test", "-c", "user.email=aec-test@example.invalid",
                "-c", "commit.gpgsign=false", "commit", "--allow-empty", "-m",
                "Unrelated committed change");
        }

        public byte[] WriteRuntime(
            string? boundRepository = null,
            bool codexChatGptBlock = false,
            bool portableBlock = false,
            string? platformOverride = null)
        {
            var runtime = CreateRuntime(boundRepository, codexChatGptBlock, portableBlock, platformOverride);
            File.WriteAllBytes(RuntimePath, runtime);
            return runtime;
        }

        public byte[] CreateRuntime(
            string? boundRepository = null,
            bool codexChatGptBlock = false,
            bool portableBlock = false,
            string? platformOverride = null)
        {
            var repository = boundRepository ?? Repository;
            var target = File.ReadAllBytes(TargetPath);
            var platform = platformOverride ?? RenderCommand.CurrentPlatform();
            return Provider switch
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
