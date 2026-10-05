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
    [InlineData("codex", false, "version=7")]
    [InlineData("codex", true, "version=8")]
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

        public void CommitSource()
        {
            GitProcess.RunRequired(Repository, "Could not stage shared source", "add", "--",
                "environment/shared/instructions.md");
            GitProcess.RunRequired(Repository, "Could not commit shared source",
                "-c", "user.name=AEC Test", "-c", "user.email=aec-test@example.invalid",
                "-c", "commit.gpgsign=false", "commit", "-m", "Changed shared source");
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
