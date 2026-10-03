using System.Text;

namespace Aec;

// This validates a portable repository target only. Its presence says nothing
// about whether the selected harness is installed on this machine.
internal static class CommittedTargetValidator
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static byte[] Read(
        string repository,
        string platform,
        string provider)
    {
        if (platform is not ("macos-arm64" or "windows-x64" or "linux-arm64"))
        {
            throw new ArgumentException($"Unsupported target platform: {platform}");
        }

        var fileName = provider switch
        {
            "codex" => "AGENTS.md",
            "copilot" => "copilot-instructions.md",
            _ => throw new ArgumentException($"Unsupported target provider: {provider}")
        };

        AecApplication.EnsureNoLinksInExistingPath(repository, "Repository path");
        AecApplication.EnsureRealDirectory(repository, "Repository");
        ApplyCommand.ValidateRepositoryRoot(repository);
        var commit = ApplyCommand.ResolveHeadCommit(repository);

        var shared = ReadSource(repository, commit, "environment/shared/instructions.md",
            "Authored shared instructions");
        var policy = ReadSource(repository, commit,
            $"environment/platforms/{platform}/policy.md", "Authored platform policy");
        var overlay = ReadSource(repository, commit,
            $"environment/providers/{provider}/overlay.md", "Authored provider overlay");

        var targetRelativePath = $"environment/targets/{platform}/{provider}/{fileName}";
        var target = ReadCommitted(repository, commit, targetRelativePath, "Committed target");
        var expected = StrictUtf8.GetBytes(InstructionSections.Render(shared, policy, overlay));
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

        return target;
    }

    private static string ReadSource(
        string repository,
        string commit,
        string relativePath,
        string label)
    {
        return StrictUtf8.GetString(ReadCommitted(repository, commit, relativePath, label));
    }

    private static byte[] ReadCommitted(
        string repository,
        string commit,
        string relativePath,
        string label)
    {
        var directory = Path.GetDirectoryName(Path.Combine(repository, relativePath))
            ?? throw new InvalidOperationException($"{label} has no parent directory.");
        AecApplication.EnsureNoLinksInExistingPath(directory, $"{label} path");

        // This shared Git reader checks the HEAD blob, staged and unstaged diffs,
        // and raw working bytes so Git filters cannot hide an uncommitted edit.
        return ApplyCommand.ReadCommittedFile(repository, commit, relativePath, label);
    }
}
