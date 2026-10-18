namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class PortableCopilotApplyCommandTests
{
    [Fact]
    public void ExactPortableRuntimeIsUnchangedWithoutRewrite()
    {
        using var layout = new SharedTargetStatusTests.StatusLayout("copilot");
        layout.WriteRuntime(portableBlock: true);
        var timestamp = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(layout.RuntimePath, timestamp);
        var before = File.ReadAllBytes(layout.RuntimePath);
        var head = layout.Head();

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"unchanged{Environment.NewLine}", result.Output);
        Assert.Empty(result.Error);
        Assert.Equal(before, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(layout.RuntimePath));
        Assert.Equal(head, layout.Head());
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void AppliesCommittedRenderedTargetWithoutChangingRepositoryOrConfig()
    {
        using var layout = new SharedTargetStatusTests.StatusLayout("copilot");
        layout.WriteRuntime(portableBlock: true);
        File.AppendAllText(layout.RuntimePath, "Unreviewed runtime edit.\n");
        var configPath = Path.Combine(layout.ProviderHome, "config.toml");
        File.WriteAllText(configPath, "not a TOML file");
        var desired = PortableApplyPreview.Create(
            layout.Repository, "copilot", layout.ProviderHome).DesiredRuntime;
        var head = layout.Head();

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"applied{Environment.NewLine}", result.Output);
        Assert.Empty(result.Error);
        Assert.Equal(desired, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal("not a TOML file", File.ReadAllText(configPath));
        Assert.Equal(head, layout.Head());
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void LegacyRuntimeBlockUpgradesToStampedPortableBlock()
    {
        using var layout = new SharedTargetStatusTests.StatusLayout("copilot");
        layout.WriteRuntime();
        var desired = PortableApplyPreview.Create(
            layout.Repository, "copilot", layout.ProviderHome).DesiredRuntime;

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"applied{Environment.NewLine}", result.Output);
        Assert.Equal(desired, File.ReadAllBytes(layout.RuntimePath));
        Assert.Contains("<!-- AEC:COPILOT:BEGIN version=3 -->",
            File.ReadAllText(layout.RuntimePath), StringComparison.Ordinal);
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void MissingRuntimeInstructionsDirectToProviderInit()
    {
        using var layout = new SharedTargetStatusTests.StatusLayout("copilot");

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("Run ordinary provider init first", result.Error,
            StringComparison.Ordinal);
        Assert.False(File.Exists(layout.RuntimePath));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void MissingEnrollmentDoesNotFallBackToLegacySource()
    {
        using var layout = new SharedTargetStatusTests.StatusLayout("copilot");
        layout.WriteRuntime(portableBlock: true);
        var legacyPath = Path.Combine(layout.Repository,
            AecApplication.CopilotSourceRelativePath);
        File.WriteAllText(legacyPath, "Legacy source must not be applied.\n");
        GitProcess.RunRequired(layout.Repository, "Could not stage legacy fixture source",
            "add", "--", AecApplication.CopilotSourceRelativePath);
        GitProcess.RunRequired(layout.Repository, "Could not remove enrolled fixture target",
            "rm", "--", Path.GetRelativePath(layout.Repository, layout.TargetPath));
        Commit(layout, "Remove portable enrollment");
        var runtime = File.ReadAllBytes(layout.RuntimePath);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("Committed target", result.Error, StringComparison.Ordinal);
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void WrongRepositoryBindingStopsBeforeRuntimeWrite()
    {
        using var layout = new SharedTargetStatusTests.StatusLayout("copilot");
        layout.WriteRuntime(boundRepository: Path.Combine(layout.Root, "other-data"),
            portableBlock: true);
        var runtime = File.ReadAllBytes(layout.RuntimePath);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("not bound to the selected AEC repository", result.Error,
            StringComparison.Ordinal);
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void WrongPlatformBindingStopsBeforeRuntimeWrite()
    {
        using var layout = new SharedTargetStatusTests.StatusLayout("copilot");
        var otherPlatform = RenderCommand.CurrentPlatform() == "windows-x64"
            ? "linux-arm64" : "windows-x64";
        layout.WriteRuntime(portableBlock: true, platformOverride: otherPlatform);
        var runtime = File.ReadAllBytes(layout.RuntimePath);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("bound to a different AEC platform", result.Error,
            StringComparison.Ordinal);
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void DirtyRenderedTargetStopsBeforeRuntimeWrite()
    {
        using var layout = new SharedTargetStatusTests.StatusLayout("copilot");
        layout.WriteRuntime(portableBlock: true);
        var runtime = File.ReadAllBytes(layout.RuntimePath);
        File.AppendAllText(layout.TargetPath, "Uncommitted target edit.\n");

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("unstaged changes", result.Error, StringComparison.Ordinal);
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
    }

    [Fact]
    public void DirtyAuthoredSourceStopsBeforeRuntimeWrite()
    {
        using var layout = new SharedTargetStatusTests.StatusLayout("copilot");
        layout.WriteRuntime(portableBlock: true);
        var runtime = File.ReadAllBytes(layout.RuntimePath);
        File.AppendAllText(layout.SharedSource, "Uncommitted shared edit.\n");

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("unstaged changes", result.Error, StringComparison.Ordinal);
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
    }

    [Fact]
    public void CommittedTargetMustMatchCommittedAuthoredSources()
    {
        using var layout = new SharedTargetStatusTests.StatusLayout("copilot");
        layout.WriteRuntime(portableBlock: true);
        var runtime = File.ReadAllBytes(layout.RuntimePath);
        File.AppendAllText(layout.TargetPath, "Wrong committed target.\n");
        GitProcess.RunRequired(layout.Repository, "Could not stage target fixture",
            "add", "--", Path.GetRelativePath(layout.Repository, layout.TargetPath));
        Commit(layout, "Break rendered target");

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("does not match committed authored sources", result.Error,
            StringComparison.Ordinal);
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void UnrelatedRepositoryChangesArePreserved()
    {
        using var layout = new SharedTargetStatusTests.StatusLayout("copilot");
        layout.WriteRuntime(portableBlock: true);
        File.AppendAllText(layout.RuntimePath, "Unreviewed runtime edit.\n");
        File.WriteAllText(Path.Combine(layout.Repository, "unrelated.txt"), "preserve\n");
        var status = layout.GitStatus();
        var head = layout.Head();

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"applied{Environment.NewLine}", result.Output);
        Assert.Equal(status, layout.GitStatus());
        Assert.Equal(head, layout.Head());
    }

    [Fact]
    public void RejectsCodexHomeForCopilotApply()
    {
        using var layout = new SharedTargetStatusTests.StatusLayout("copilot");

        var result = TestApplication.Run("apply", "--repo", layout.Repository,
            "--provider=copilot", "--codex-home", layout.ProviderHome);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("--codex-home is not valid with --provider=copilot",
            result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsCopilotHomeWithoutProvider()
    {
        using var layout = new SharedTargetStatusTests.StatusLayout("copilot");

        var result = TestApplication.Run("apply", "--repo", layout.Repository,
            "--copilot-home", layout.ProviderHome);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("--copilot-home requires --provider=copilot",
            result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void CopilotRuntimeInsideRepositoryIsRejected()
    {
        using var layout = new SharedTargetStatusTests.StatusLayout("copilot");
        layout.WriteRuntime(portableBlock: true);
        var runtime = File.ReadAllBytes(layout.RuntimePath);

        var result = TestApplication.Run("apply", "--repo", layout.Repository,
            "--provider=copilot", "--copilot-home", layout.Repository);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("Provider runtime target must be outside the data repository",
            result.Error, StringComparison.Ordinal);
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
    }

    private static CommandResult Run(SharedTargetStatusTests.StatusLayout layout) =>
        TestApplication.Run("apply", "--repo", layout.Repository,
            "--provider=copilot", "--copilot-home", layout.ProviderHome);

    private static void Commit(SharedTargetStatusTests.StatusLayout layout, string message) =>
        GitProcess.RunRequired(layout.Repository, "Could not commit fixture change",
            "-c", "user.name=AEC Test", "-c", "user.email=aec-test@example.invalid",
            "-c", "commit.gpgsign=false", "commit", "-m", message);
}
