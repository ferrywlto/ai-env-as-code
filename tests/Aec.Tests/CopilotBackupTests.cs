namespace Aec.Tests;

public sealed class CopilotBackupArgumentTests
{
    [Fact]
    public void RejectsLegacyCodexHomeForCopilotBackup()
    {
        var repository = Path.GetFullPath("repository");
        var home = Path.GetFullPath("copilot-home");

        var result = TestApplication.Run(
            "copilot", "backup",
            "--repo",
            repository,
            "--codex-home",
            home);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "Unknown argument: --codex-home",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsLegacyCopilotHomeForCodexBackup()
    {
        var repository = Path.GetFullPath("repository");
        var home = Path.GetFullPath("copilot-home");

        var result = TestApplication.Run(
            "codex", "backup",
            "--repo",
            repository,
            "--copilot-home",
            home);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "Unknown argument: --copilot-home",
            result.Error,
            StringComparison.Ordinal);
    }
}
