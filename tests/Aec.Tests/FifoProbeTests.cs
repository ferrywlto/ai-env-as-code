using System.Diagnostics;

namespace Aec.Tests;

public sealed class FifoProbeTests
{
    [Fact]
    public async Task ZeroLengthFifoDoesNotBlockStatus()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        var temporaryDirectory = OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath();
        var root = Path.Combine(temporaryDirectory, "aec-fifo-test", Guid.NewGuid().ToString("N"));
        var repository = Path.Combine(root, "data");
        var codexHome = Path.Combine(root, "codex-home");
        var source = Path.Combine(repository, "environment", "providers", "codex", "AGENTS.md");
        var target = Path.Combine(codexHome, "AGENTS.md");
        var canonicalConfig = Path.Combine(
            repository,
            "environment",
            "providers",
            "codex",
            "config.toml");
        var runtimeConfig = Path.Combine(codexHome, "config.toml");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        Directory.CreateDirectory(codexHome);
        File.WriteAllText(source, "desired\n");
        File.WriteAllText(canonicalConfig, "personality = \"none\"\n");
        File.WriteAllText(runtimeConfig, "personality = \"none\"\n");

        try
        {
            using var process = Process.Start(new ProcessStartInfo("mkfifo", target)
            {
                UseShellExecute = false
            });
            Assert.NotNull(process);
            process.WaitForExit();
            Assert.Equal(0, process.ExitCode);

            var status = Task.Run(() =>
            {
                var output = new StringWriter();
                var error = new StringWriter();
                var exitCode = AecApplication.Run(
                    ["status", "--repo", repository, "--codex-home", codexHome],
                    output,
                    error);
                return (exitCode, Output: output.ToString(), Error: error.ToString());
            });

            if (await Task.WhenAny(status, Task.Delay(TimeSpan.FromSeconds(2))) != status)
            {
                await using (var writer = new FileStream(target, FileMode.Open, FileAccess.Write))
                {
                }

                if (await Task.WhenAny(status, Task.Delay(TimeSpan.FromSeconds(2))) != status)
                {
                    Assert.Fail("The blocked FIFO read did not recover after its writer closed.");
                }

                Assert.Fail("status blocked while reading a zero-length FIFO.");
            }

            var result = await status;
            Assert.Equal(2, result.exitCode);
            Assert.Equal(
                $"codex/AGENTS.md   different{Environment.NewLine}" +
                $"codex/config.toml in_sync{Environment.NewLine}",
                result.Output);
            Assert.Empty(result.Error);
        }
        finally
        {
            File.Delete(target);
            Directory.Delete(root, true);
        }
    }
}
