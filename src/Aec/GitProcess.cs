using System.Diagnostics;

namespace Aec;

internal static class GitProcess
{
    private static readonly AsyncLocal<bool> DisableMigrationFilters = new();
    internal static bool MigrationFiltersSuppressed => DisableMigrationFilters.Value;

    internal static IDisposable SuppressMigrationFilters()
    {
        var previous = DisableMigrationFilters.Value;
        DisableMigrationFilters.Value = true;
        return new FilterScope(previous);
    }

    private sealed class FilterScope(bool previous) : IDisposable
    {
        public void Dispose() => DisableMigrationFilters.Value = previous;
    }
    private static readonly string[] GitLocationVariables =
    [
        "GIT_DIR",
        "GIT_WORK_TREE",
        "GIT_COMMON_DIR",
        "GIT_OBJECT_DIRECTORY",
        "GIT_INDEX_FILE",
        "GIT_ALTERNATE_OBJECT_DIRECTORIES",
        "GIT_TEMPLATE_DIR",
        "GIT_CONFIG_PARAMETERS"
    ];

    public static GitResult Run(string? workingDirectory, params string[] arguments)
    {
        var startInfo = CreateStartInfo(workingDirectory, arguments);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Git could not be started.");
        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();
        process.WaitForExit();

        return new GitResult(
            process.ExitCode,
            standardOutput.GetAwaiter().GetResult(),
            standardError.GetAwaiter().GetResult());
    }

    internal static GitResult RunRemoteCommand(
        string repository, string[] arguments, string operation, TimeSpan limit)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(limit, TimeSpan.Zero);
        var elapsed = Stopwatch.StartNew();
        var startInfo = CreateStartInfo(repository, arguments);
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.RedirectStandardInput = true;
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Git could not be started.");
        // The remote query must not consume input intended for the migration's
        // later review prompt. Custom SSH/helper waits are bounded by the deadline.
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        var completion = Task.WhenAll(process.WaitForExitAsync(), output, error);
        try
        {
            var remaining = limit - elapsed.Elapsed;
            if (remaining <= TimeSpan.Zero) throw new TimeoutException();
            completion.WaitAsync(remaining).GetAwaiter().GetResult();
        }
        catch (TimeoutException exception)
        {
            // Request termination only for this query's tree, then confirm Git's
            // exit with a bounded cleanup wait rather than waiting indefinitely.
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { /* The process exited during cleanup. */ }
            if (!process.WaitForExit(1000))
            {
                throw new InvalidOperationException($"{operation} timed out and Git termination could not be confirmed.", exception);
            }
            throw new InvalidOperationException($"{operation} timed out; migration preparation stopped.", exception);
        }

        return new GitResult(process.ExitCode, output.GetAwaiter().GetResult(), error.GetAwaiter().GetResult());
    }

    public static byte[] RunRequiredBytes(
        string? workingDirectory,
        string failureMessage,
        params string[] arguments)
    {
        var startInfo = CreateStartInfo(workingDirectory, arguments);
        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Git could not be started.");
        using var output = new MemoryStream();

        // Binary stdout keeps committed instruction BOM and line-ending bytes unchanged.
        var copyOutput = process.StandardOutput.BaseStream.CopyToAsync(output);
        var readError = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        copyOutput.GetAwaiter().GetResult();
        var error = readError.GetAwaiter().GetResult();

        if (process.ExitCode != 0)
        {
            throw CreateFailure(failureMessage, process.ExitCode, error);
        }

        return output.ToArray();
    }

    public static GitResult RunRequired(
        string? workingDirectory,
        string failureMessage,
        params string[] arguments) =>
        RequireSuccess(Run(workingDirectory, arguments), failureMessage);

    internal static GitResult RequireSuccess(GitResult result, string failureMessage)
    {
        if (result.ExitCode == 0)
        {
            return result;
        }

        var details = string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error;
        throw CreateFailure(failureMessage, result.ExitCode, details);
    }

    private static InvalidOperationException CreateFailure(string message, int exitCode, string details)
    {
        details = details.ReplaceLineEndings(" ").Trim();
        var suffix = details.Length == 0 ? string.Empty : $": {details}";
        return new InvalidOperationException($"{message} with exit code {exitCode}{suffix}");
    }

    private static void ClearInjectedGitConfiguration(ProcessStartInfo startInfo)
    {
        if (startInfo.Environment.TryGetValue("GIT_CONFIG_COUNT", out var value) &&
            int.TryParse(value, out var count) &&
            count >= 0 &&
            count <= 1024)
        {
            for (var index = 0; index < count; index++)
            {
                startInfo.Environment.Remove($"GIT_CONFIG_KEY_{index}");
                startInfo.Environment.Remove($"GIT_CONFIG_VALUE_{index}");
            }
        }

        startInfo.Environment.Remove("GIT_CONFIG_COUNT");
    }

    private static ProcessStartInfo CreateStartInfo(
        string? workingDirectory,
        IReadOnlyCollection<string> arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        if (workingDirectory is not null)
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        foreach (var variable in GitLocationVariables)
        {
            startInfo.Environment.Remove(variable);
        }

        ClearInjectedGitConfiguration(startInfo);

        if (DisableMigrationFilters.Value && workingDirectory is not null)
        {
            // Inspect the same effective configuration Git will use, without
            // recursively applying overrides to the inspection itself.
            DisableMigrationFilters.Value = false;
            GitResult configured;
            try
            {
                configured = Run(workingDirectory, "config", "--null", "--name-only", "--get-regexp",
                    @"^filter\..*\.(clean|smudge|process|required)$");
            }
            finally { DisableMigrationFilters.Value = true; }
            if (configured.ExitCode is not (0 or 1))
                throw new InvalidOperationException("Could not inspect Git filter configuration for migration.");
            if (configured.Output.Contains('\uFFFD'))
                throw new InvalidOperationException("Git filter configuration contains an undecodable name; migration cannot suppress it safely.");
            var drivers = configured.Output.Split('\0', StringSplitOptions.RemoveEmptyEntries)
                .Select(key => key[7..key.LastIndexOf('.')])
                .Distinct(StringComparer.Ordinal);
            foreach (var driver in drivers)
            {
                foreach (var key in new[] { "clean", "smudge", "process" })
                {
                    startInfo.ArgumentList.Add("-c");
                    startInfo.ArgumentList.Add($"filter.{driver}.{key}=");
                }
                // A selected driver fails closed rather than silently checking
                // out a pointer or staging unconverted content.
                startInfo.ArgumentList.Add("-c");
                startInfo.ArgumentList.Add($"filter.{driver}.required=true");
            }
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }
}

internal sealed record GitResult(int ExitCode, string Output, string Error);
