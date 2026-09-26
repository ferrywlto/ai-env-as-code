namespace Aec;

internal static class CopilotBackupCommand
{
    internal const string CommitMessage = "Backup Copilot instructions";

    public static int Run(string repository, string copilotHome, TextWriter output)
    {
        AecApplication.EnsureNoLinksInExistingPath(repository, "Repository path");
        AecApplication.EnsureNoLinksInExistingPath(copilotHome, "Copilot home path");
        ApplyCommand.EnsureRuntimeOutsideRepository(repository, copilotHome);
        AecApplication.EnsureRealDirectory(repository, "Repository");
        AecApplication.EnsureSourceDirectories(repository);
        AecApplication.EnsureRealDirectory(copilotHome, "Copilot home");

        var canonicalPath = Path.Combine(repository, AecApplication.CopilotSourceRelativePath);
        var providerDirectory = Path.GetDirectoryName(canonicalPath)
            ?? throw new InvalidOperationException(
                "Canonical Copilot instructions have no provider directory.");
        AecApplication.EnsureNoLinksInExistingPath(providerDirectory, "Copilot provider path");
        AecApplication.EnsureRealDirectory(providerDirectory, "Copilot provider directory");

        var runtimePath = Path.Combine(copilotHome, "copilot-instructions.md");
        return BackupCommand.RunSingleFile(
            repository,
            AecApplication.CopilotSourceRelativePath,
            runtimePath,
            "Canonical Copilot instructions",
            "Copilot runtime instructions",
            CommitMessage,
            (canonical, runtime) => ValidateBindings(repository, canonical, runtime),
            output);
    }

    private static void ValidateBindings(string repository, byte[] canonical, byte[] runtime)
    {
        ValidateBinding(repository, canonical, "Canonical Copilot instructions");
        ValidateBinding(repository, runtime, "Copilot runtime instructions");
    }

    private static void ValidateBinding(string repository, byte[] content, string label)
    {
        var binding = CopilotInstructionBlock.ReadRepositoryBinding(content)
            ?? throw new InvalidDataException(
                $"{label} do not contain a supported managed AEC block.");
        if (!AecInstructionBlock.RepositoryPathsEqual(binding, repository))
        {
            throw new InvalidOperationException(
                $"{label} are bound to a different data repository. " +
                $"Recorded repository: {binding}. Selected repository: {repository}.");
        }
    }
}
