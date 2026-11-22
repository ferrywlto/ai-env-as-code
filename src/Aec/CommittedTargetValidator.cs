using System.Text;

namespace Aec;

// This validates a portable repository target only. Its presence says nothing
// about whether the selected harness is installed on this machine.
internal static class CommittedTargetValidator
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static bool IsEnrolled(
        string repository,
        string commit,
        string platform,
        string provider)
    {
        var fileName = TargetFileName(provider);
        var relativePath = $"environment/targets/{platform}/{provider}/{fileName}";
        var entries = GitProcess.RunRequired(
            repository,
            "Git could not inspect the enrolled target",
            "ls-tree", "-z", "--name-only", commit, "--", relativePath).Output;
        return string.Equals(entries, relativePath + '\0', StringComparison.Ordinal);
    }

    public static byte[] Read(
        string repository,
        string platform,
        string provider) => ReadWithCommit(repository, platform, provider).Target;

    internal static (string Commit, byte[] Target) ReadWithCommit(
        string repository,
        string platform,
        string provider)
    {
        if (platform is not (AecIdentifiers.MacOsArm64 or AecIdentifiers.WindowsX64 or AecIdentifiers.LinuxArm64))
        {
            throw new ArgumentException($"Unsupported target platform: {platform}");
        }

        var fileName = TargetFileName(provider);

        AecApplication.EnsureNoLinksInExistingPath(repository, "Repository path");
        AecApplication.EnsureRealDirectory(repository, "Repository");
        ApplyCommand.ValidateRepositoryRoot(repository);
        var commit = ApplyCommand.ResolveHeadCommit(repository);

        var targetRelativePath = $"environment/targets/{platform}/{provider}/{fileName}";
        var files = CommittedFileReader.ReadCommittedFiles(repository, commit,
            ("environment/shared/instructions.md", "Authored shared instructions"),
            ($"environment/platforms/{platform}/policy.md", "Authored platform policy"),
            ($"environment/providers/{provider}/overlay.md", "Authored provider overlay"),
            (targetRelativePath, "Committed target"));
        var target = files[3];
        var expected = StrictUtf8.GetBytes(InstructionSections.Render(
            StrictUtf8.GetString(files[0]), StrictUtf8.GetString(files[1]), StrictUtf8.GetString(files[2])));
        if (expected.Length > AecApplication.MaximumTextBytes)
        {
            throw new InvalidDataException($"Rendered target exceeds 1 MiB: {targetRelativePath}");
        }

        if (!target.AsSpan().SequenceEqual(expected))
        {
            throw new InvalidDataException(
                $"Committed target does not match committed authored sources: {targetRelativePath}. " +
                "Run `aec render`, review, and commit the regenerated target.");
        }

        if (!string.Equals(commit, ApplyCommand.ResolveHeadCommit(repository),
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Repository HEAD changed during target validation.");
        }

        return (commit, target);
    }

    private static string TargetFileName(string provider) => provider switch
    {
        AecIdentifiers.Codex => "AGENTS.md",
        AecIdentifiers.Copilot => "copilot-instructions.md",
        _ => throw new ArgumentException($"Unsupported target provider: {provider}")
    };
}
