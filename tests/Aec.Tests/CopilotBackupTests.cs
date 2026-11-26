namespace Aec.Tests;

public sealed class CopilotBackupArgumentTests
{
    [Fact]
    public void RejectsCodexHomeForCopilotBackup()
    {
        var repository = Path.GetFullPath("repository");
        var home = Path.GetFullPath("copilot-home");

        var result = TestApplication.Run(
            "backup",
            "--repo",
            repository,
            "--provider=copilot",
            "--codex-home",
            home);

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
        var repository = Path.GetFullPath("repository");
        var home = Path.GetFullPath("copilot-home");

        var result = TestApplication.Run(
            "backup",
            "--repo",
            repository,
            "--copilot-home",
            home);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "--copilot-home requires --provider=copilot",
            result.Error,
            StringComparison.Ordinal);
    }
}
