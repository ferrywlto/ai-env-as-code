using System.Text;

namespace Aec.Tests;

internal static class TestGit
{
    public static GitResult Run(string workingDirectory, params string[] arguments)
    {
        // Reuse the production launcher so test setup cannot drift from its Git isolation rules.
        return GitProcess.Run(workingDirectory, arguments);
    }

    public static void SeedInitializedCodexRepository(
        string repository, string? codexHome, string instructions, string config)
    {
        var baseline = Encoding.UTF8.GetBytes(instructions);
        SeedCodexBaseline(repository, baseline, config);

        var source = Path.Combine(repository, AecApplication.SourceRelativePath);
        // Later initialization and enrollment validate this two-commit history.
        var initialized = AecInstructionBlock.Merge(baseline, repository);
        File.WriteAllBytes(source, initialized);
        _ = GitProcess.RunRequired(repository, "Could not seed initialized Codex fixture",
            "commit", "--quiet", "-m", "Initialize AEC instructions", "--", AecApplication.SourceRelativePath);
        if (codexHome is not null)
        {
            Directory.CreateDirectory(codexHome);
            File.WriteAllBytes(Path.Combine(codexHome, "AGENTS.md"), initialized);
            File.WriteAllText(Path.Combine(codexHome, "config.toml"), config);
        }
    }

    public static void SeedCodexBaseline(string repository, byte[] instructions, string? config)
    {
        var source = Path.Combine(repository, AecApplication.SourceRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        Git("init", "--quiet", "--template=", "--initial-branch=main");
        Git("config", "--local", "core.autocrlf", "false");
        File.WriteAllBytes(source, instructions);

        if (config is null)
        {
            Git("add", "--", AecApplication.SourceRelativePath);
        }
        else
        {
            File.WriteAllText(Path.Combine(repository, AecApplication.ConfigSourceRelativePath), config);
            Git("add", "--", AecApplication.SourceRelativePath, AecApplication.ConfigSourceRelativePath);
        }

        Git("commit", "--quiet", "-m", config is null
            ? BackupCommand.CommitMessage
            : BackupCommand.OrdinaryCommitMessage);

        void Git(params string[] arguments) =>
            _ = GitProcess.RunRequired(repository, "Could not seed Codex baseline fixture", arguments);
    }
}
