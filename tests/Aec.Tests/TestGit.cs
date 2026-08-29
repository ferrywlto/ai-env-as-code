namespace Aec.Tests;

internal static class TestGit
{
    public static GitResult Run(string workingDirectory, params string[] arguments)
    {
        // Reuse the production launcher so test setup cannot drift from its Git isolation rules.
        return GitProcess.Run(workingDirectory, arguments);
    }
}
