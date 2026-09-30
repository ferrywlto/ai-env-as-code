using System.Runtime.InteropServices;
using System.Text;

namespace Aec;

// Render changes only canonical repository files. Runtime deployment remains
// the separate, commit-gated apply direction.
internal static class RenderCommand
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static int Run(string repository, TextWriter output)
    {
        AecApplication.EnsureNoLinksInExistingPath(repository, "Repository path");
        AecApplication.EnsureRealDirectory(repository, "Repository");
        ApplyCommand.ValidateRepositoryRoot(repository);
        var commit = ApplyCommand.ResolveHeadCommit(repository);

        var platform = CurrentPlatform();
        var targetRoot = Path.Combine(repository, "environment", "targets", platform);
        AecApplication.EnsureNoLinksInExistingPath(targetRoot, "Target path");
        AecApplication.EnsureRealDirectory(targetRoot, "Enrolled platform directory");

        var shared = ReadSource(repository, "environment/shared/instructions.md");
        var platformPolicy = ReadSource(repository, $"environment/platforms/{platform}/policy.md");
        var changes = new List<TargetChange>();

        foreach (var providerDirectory in Directory.EnumerateDirectories(targetRoot)
                     .OrderBy(path => path, StringComparer.Ordinal))
        {
            AecApplication.EnsureNoLinksInExistingPath(providerDirectory, "Target path");
            AecApplication.EnsureRealDirectory(providerDirectory, "Enrolled provider directory");
            var provider = Path.GetFileName(providerDirectory);
            var fileName = provider switch
            {
                "codex" => "AGENTS.md",
                "copilot" => "copilot-instructions.md",
                _ => throw new InvalidDataException($"Unsupported enrolled provider: {provider}")
            };

            var overlay = ReadSource(repository, $"environment/providers/{provider}/overlay.md");
            var relativePath = $"environment/targets/{platform}/{provider}/{fileName}";
            var targetPath = Path.Combine(repository, relativePath);
            var desired = StrictUtf8.GetBytes(InstructionSections.Render(shared, platformPolicy, overlay));
            if (desired.Length > AecApplication.MaximumTextBytes)
            {
                throw new InvalidDataException($"Rendered target exceeds 1 MiB: {relativePath}");
            }

            var current = AecApplication.ReadOptionalTextFile(targetPath, "Rendered target");
            if (current is not null && current.AsSpan().SequenceEqual(desired))
            {
                continue;
            }

            if (current is not null)
            {
                // A committed target must be clean before regeneration. Its working
                // bytes may contain a user's edit that render must never overwrite.
                _ = ApplyCommand.ReadCommittedFile(
                    repository, commit, relativePath, "Rendered target");
            }
            else if (GitProcess.RunRequired(repository, "Git could not inspect target history",
                         "ls-tree", "--name-only", commit, "--", relativePath).Output.Length != 0)
            {
                throw new InvalidOperationException(
                    $"Rendered target is missing but committed at HEAD: {relativePath}");
            }

            changes.Add(new TargetChange(relativePath, targetPath, current, desired));
        }

        if (changes.Count == 0)
        {
            output.WriteLine("unchanged");
            return 0;
        }

        foreach (var change in changes)
        {
            AtomicFile.ReplaceIfUnchanged(
                change.Path,
                change.Current,
                change.Desired,
                "Rendered target",
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
            output.WriteLine($"rendered {change.RelativePath}");
        }

        return 0;
    }

    private static string ReadSource(string repository, string relativePath)
    {
        var path = Path.Combine(repository, relativePath);
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException($"Instruction source has no parent: {path}");
        AecApplication.EnsureNoLinksInExistingPath(directory, "Instruction source path");
        var bytes = AecApplication.ReadRequiredTextFile(path, "Instruction source");
        return StrictUtf8.GetString(bytes);
    }

    private static string CurrentPlatform() =>
        (RuntimeInformation.IsOSPlatform(OSPlatform.OSX),
            RuntimeInformation.IsOSPlatform(OSPlatform.Windows),
            RuntimeInformation.ProcessArchitecture) switch
        {
            (true, false, Architecture.Arm64) => "macos-arm64",
            (false, true, Architecture.X64) => "windows-x64",
            (false, false, Architecture.Arm64) when OperatingSystem.IsLinux() => "linux-arm64",
            _ => throw new PlatformNotSupportedException(
                "Shared-instruction rendering is not available on this platform.")
        };

    private sealed record TargetChange(
        string RelativePath,
        string Path,
        byte[]? Current,
        byte[] Desired);
}
