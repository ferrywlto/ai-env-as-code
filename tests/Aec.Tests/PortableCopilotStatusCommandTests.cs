namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class PortableCopilotStatusCommandTests
{
    [Fact]
    public void ReportsInSyncForCommittedRenderedTargetWithoutWrites()
    {
        using var layout = new SharedTargetStatusTests.StatusLayout("copilot");
        layout.WriteRuntime(portableBlock: true);
        var runtime = File.ReadAllBytes(layout.RuntimePath);
        var head = layout.Head();

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(StatusOutput("in_sync"), result.Output);
        Assert.Empty(result.Error);
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(head, layout.Head());
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void ReportsRuntimeDriftWithoutWrites()
    {
        using var layout = new SharedTargetStatusTests.StatusLayout("copilot");
        layout.WriteRuntime(portableBlock: true);
        File.AppendAllText(layout.RuntimePath, "Unreviewed runtime edit.\n");
        var runtime = File.ReadAllBytes(layout.RuntimePath);
        var head = layout.Head();

        var result = Run(layout);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(StatusOutput("different"), result.Output);
        Assert.Empty(result.Error);
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(head, layout.Head());
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void ReportsMissingRuntimeInstructions()
    {
        using var layout = new SharedTargetStatusTests.StatusLayout("copilot");

        var result = Run(layout);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(StatusOutput("missing"), result.Output);
        Assert.Empty(result.Error);
        Assert.False(File.Exists(layout.RuntimePath));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void MissingEnrollmentDoesNotFallBackToLegacySource()
    {
        using var layout = new SharedTargetStatusTests.StatusLayout("copilot");
        layout.WriteRuntime(portableBlock: true);
        var runtime = File.ReadAllBytes(layout.RuntimePath);
        var legacyPath = Path.Combine(layout.Repository,
            AecApplication.CopilotSourceRelativePath);
        File.WriteAllText(legacyPath, "Legacy source must not be inspected.\n");
        GitProcess.RunRequired(layout.Repository, "Could not stage legacy source fixture",
            "add", "--", AecApplication.CopilotSourceRelativePath);
        GitProcess.RunRequired(layout.Repository, "Could not remove target fixture",
            "rm", "--", Path.GetRelativePath(layout.Repository, layout.TargetPath));
        GitProcess.RunRequired(layout.Repository, "Could not commit missing target fixture",
            "-c", "user.name=AEC Test", "-c", "user.email=aec-test@example.invalid",
            "-c", "commit.gpgsign=false", "commit", "-m", "Remove target fixture");

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("Committed target", result.Error, StringComparison.Ordinal);
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void UsesCopilotHomeEnvironmentWhenTheFlagIsAbsent()
    {
        using var layout = new SharedTargetStatusTests.StatusLayout("copilot");
        layout.WriteRuntime(portableBlock: true);
        var previous = Environment.GetEnvironmentVariable("COPILOT_HOME");

        try
        {
            Environment.SetEnvironmentVariable("COPILOT_HOME", layout.ProviderHome);
            var result = TestApplication.Run("status", "--repo", layout.Repository,
                "--provider=copilot");

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(StatusOutput("in_sync"), result.Output);
        }
        finally
        {
            Environment.SetEnvironmentVariable("COPILOT_HOME", previous);
        }
    }

    [Fact]
    public void RejectsCodexHomeForCopilotStatus()
    {
        using var layout = new SharedTargetStatusTests.StatusLayout("copilot");

        var result = TestApplication.Run("status", "--repo", layout.Repository,
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

        var result = TestApplication.Run("status", "--repo", layout.Repository,
            "--copilot-home", layout.ProviderHome);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("--copilot-home requires --provider=copilot",
            result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void WrongRepositoryBindingStopsWithoutOutputOrWrites()
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
    public void DirtyRenderedTargetStopsWithoutOutputOrWrites()
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

    private static CommandResult Run(SharedTargetStatusTests.StatusLayout layout) =>
        TestApplication.Run("status", "--repo", layout.Repository,
            "--provider=copilot", "--copilot-home", layout.ProviderHome);

    private static string StatusOutput(string status) =>
        $"copilot/copilot-instructions.md {status}{Environment.NewLine}";
}
