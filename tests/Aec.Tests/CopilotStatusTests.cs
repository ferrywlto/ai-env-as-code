namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class CopilotStatusTests
{
    [Fact]
    public void ReportsInSyncForEqualInstructionBytes()
    {
        using var layout = new CopilotStatusLayout("same\n", "same\n");

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(StatusOutput("in_sync"), result.Output);
        Assert.Empty(result.Error);
    }

    [Fact]
    public void ReportsDifferentWithoutChangingEitherFile()
    {
        using var layout = new CopilotStatusLayout("canonical\n", "runtime\n");
        var canonicalBefore = File.ReadAllBytes(layout.CanonicalInstructions);
        var runtimeBefore = File.ReadAllBytes(layout.RuntimeInstructions);
        var timestamp = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(layout.CanonicalInstructions, timestamp);
        File.SetLastWriteTimeUtc(layout.RuntimeInstructions, timestamp);

        var result = Run(layout);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(StatusOutput("different"), result.Output);
        Assert.Empty(result.Error);
        Assert.Equal(canonicalBefore, File.ReadAllBytes(layout.CanonicalInstructions));
        Assert.Equal(runtimeBefore, File.ReadAllBytes(layout.RuntimeInstructions));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(layout.CanonicalInstructions));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(layout.RuntimeInstructions));
    }

    [Fact]
    public void ReportsMissingWhenRuntimeInstructionsAreAbsent()
    {
        using var layout = new CopilotStatusLayout("canonical\n", null);

        var result = Run(layout);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(StatusOutput("missing"), result.Output);
        Assert.Empty(result.Error);
    }

    [Fact]
    public void FailsWhenCanonicalInstructionsAreAbsent()
    {
        using var layout = new CopilotStatusLayout(null, "runtime\n");

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "Canonical Copilot instructions does not exist",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public void UsesCopilotHomeEnvironmentWhenTheFlagIsAbsent()
    {
        using var layout = new CopilotStatusLayout("same\n", "same\n");
        var previous = Environment.GetEnvironmentVariable("COPILOT_HOME");

        try
        {
            Environment.SetEnvironmentVariable("COPILOT_HOME", layout.CopilotHome);

            var result = TestApplication.Run(
                "status",
                "--repo",
                layout.Repository,
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
        using var layout = new CopilotStatusLayout("same\n", "same\n");

        var result = TestApplication.Run(
            "status",
            "--repo",
            layout.Repository,
            "--provider=copilot",
            "--codex-home",
            layout.CopilotHome);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "--codex-home is not valid with --provider=copilot",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsCopilotHomeWithoutTheProvider()
    {
        using var layout = new CopilotStatusLayout("same\n", "same\n");

        var result = TestApplication.Run(
            "status",
            "--repo",
            layout.Repository,
            "--copilot-home",
            layout.CopilotHome);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "--copilot-home requires --provider=copilot",
            result.Error,
            StringComparison.Ordinal);
    }

    private static CommandResult Run(CopilotStatusLayout layout) =>
        TestApplication.Run(
            "status",
            "--repo",
            layout.Repository,
            "--provider=copilot",
            "--copilot-home",
            layout.CopilotHome);

    private static string StatusOutput(string status) =>
        $"copilot/copilot-instructions.md {status}{Environment.NewLine}";

    private sealed class CopilotStatusLayout : IDisposable
    {
        public CopilotStatusLayout(string? canonical, string? runtime)
        {
            Root = Path.Combine(
                OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(),
                "aec-copilot-status-tests",
                Guid.NewGuid().ToString("N"));
            Repository = Path.Combine(Root, "repository");
            CopilotHome = Path.Combine(Root, "copilot-home");
            CanonicalInstructions = Path.Combine(
                Repository,
                AecApplication.CopilotSourceRelativePath);
            RuntimeInstructions = Path.Combine(CopilotHome, "copilot-instructions.md");

            Directory.CreateDirectory(Path.Combine(Repository, "environment", "providers", "codex"));
            Directory.CreateDirectory(Path.GetDirectoryName(CanonicalInstructions)!);
            Directory.CreateDirectory(CopilotHome);
            if (canonical is not null)
            {
                File.WriteAllText(CanonicalInstructions, canonical);
            }

            if (runtime is not null)
            {
                File.WriteAllText(RuntimeInstructions, runtime);
            }
        }

        public string Root { get; }

        public string Repository { get; }

        public string CopilotHome { get; }

        public string CanonicalInstructions { get; }

        public string RuntimeInstructions { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
