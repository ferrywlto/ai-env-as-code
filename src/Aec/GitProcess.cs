using System.Diagnostics;

namespace Aec;

internal static class GitProcess
{
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
            var details = error.ReplaceLineEndings(" ").Trim();
            var suffix = details.Length == 0 ? string.Empty : $": {details}";
            throw new InvalidOperationException(
                $"{failureMessage} with exit code {process.ExitCode}{suffix}");
        }

        return output.ToArray();
    }

    public static GitResult RunRequired(
        string? workingDirectory,
        string failureMessage,
        params string[] arguments)
    {
        var result = Run(workingDirectory, arguments);
        if (result.ExitCode == 0)
        {
            return result;
        }

        var details = string.IsNullOrWhiteSpace(result.Error) ? result.Output : result.Error;
        details = details.ReplaceLineEndings(" ").Trim();
        var suffix = details.Length == 0 ? string.Empty : $": {details}";
        throw new InvalidOperationException(
            $"{failureMessage} with exit code {result.ExitCode}{suffix}");
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

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }
}

internal sealed record GitResult(int ExitCode, string Output, string Error);
