namespace Aec.Tests;

internal static class TestApplication
{
    public static CommandResult Run(params string[] arguments)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = AecApplication.Run(arguments, output, error);

        return new CommandResult(exitCode, output.ToString(), error.ToString());
    }
}

internal sealed record CommandResult(int ExitCode, string Output, string Error);
