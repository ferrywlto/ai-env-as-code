using System.Diagnostics;

namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class FifoProbeTests
{
    [Fact]
    public async Task ZeroLengthFifoDoesNotBlockStatus()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        // Portable status must pass committed-target validation before it can
        // reach the runtime FIFO. An unenrolled fixture would fail too early.
        using var layout = new SharedTargetStatusTests.StatusLayout("codex");
        layout.WriteRuntime(portableBlock: true);
        var runtimeConfig = Path.Combine(layout.ProviderHome, "config.toml");
        File.WriteAllText(runtimeConfig, "personality = \"none\"\n");
        var arguments = new[] { "status", "--repo", layout.Repository,
            "--codex-home", layout.ProviderHome };
        var baseline = TestApplication.Run(arguments);
        Assert.True(baseline.ExitCode == 0, baseline.Error);
        var head = layout.Head();
        var config = File.ReadAllBytes(runtimeConfig);
        File.Delete(layout.RuntimePath);

        try
        {
            var fifo = await RunProbeProcess("mkfifo", [layout.RuntimePath], layout.Root);
            Assert.True(fifo.ExitCode == 0, fifo.Error);

            // Run the public CLI in a killable child: a FIFO reader and a
            // recovery writer can both block, so never open the write end here.
            var result = await RunProbeProcess(
                Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet",
                [typeof(AecApplication).Assembly.Location, .. arguments], layout.Root);
            Assert.Equal(1, result.ExitCode);
            Assert.Empty(result.Output);
            // This diagnostic is produced only after the runtime bytes are read.
            // Empty FIFO content is unbound; an earlier Git error cannot pass.
            Assert.Equal(
                "error: Provider runtime instructions are not bound to the selected AEC repository. " +
                $"Run ordinary provider init first.{Environment.NewLine}", result.Error);
            Assert.Equal(head, layout.Head());
            Assert.Empty(layout.GitStatus());
            Assert.Equal(config, File.ReadAllBytes(runtimeConfig));
        }
        finally
        {
            File.Delete(layout.RuntimePath);
        }
    }

    private static async Task<CommandResult> RunProbeProcess(
        string executable, string[] arguments, string workingDirectory)
    {
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start);
        Assert.NotNull(process);
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        var completion = Task.WhenAll(process.WaitForExitAsync(), output, error);
        try
        {
            // Allow CLI startup and Git preflight, but bound a stuck FIFO read.
            await completion.WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (TimeoutException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            // Bound cleanup too; do not replace one hang with an unbounded reap.
            await completion.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Fail($"{executable} did not finish within the FIFO probe's 10-second bound.");
        }

        return new CommandResult(process.ExitCode, await output, await error);
    }
}
