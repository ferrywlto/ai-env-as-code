namespace Aec;

internal static class CopilotApplyCommand
{
    public static int Run(string repository, string copilotHome, TextWriter output)
    {
        AecApplication.EnsureNoLinksInExistingPath(repository, "Repository path");
        AecApplication.EnsureNoLinksInExistingPath(copilotHome, "Copilot home path");
        ApplyCommand.EnsureRuntimeOutsideRepository(
            repository,
            copilotHome,
            "copilot-instructions.md",
            "Copilot runtime target");
        AecApplication.EnsureRealDirectory(repository, "Repository");
        AecApplication.EnsureSourceDirectories(repository);
        AecApplication.EnsureRealDirectory(copilotHome, "Copilot home");
        ApplyCommand.ValidateRepositoryRoot(repository);

        var canonicalPath = Path.Combine(repository, AecApplication.CopilotSourceRelativePath);
        var providerDirectory = Path.GetDirectoryName(canonicalPath)
            ?? throw new InvalidOperationException(
                "Canonical Copilot instructions have no provider directory.");
        AecApplication.EnsureNoLinksInExistingPath(providerDirectory, "Copilot provider path");
        AecApplication.EnsureRealDirectory(providerDirectory, "Copilot provider directory");

        var commit = ApplyCommand.ResolveHeadCommit(repository);
        var canonical = ApplyCommand.ReadCommittedFile(
            repository,
            commit,
            AecApplication.CopilotSourceRelativePath,
            "Canonical Copilot instructions");
        ValidateBinding(repository, canonical);

        var runtimePath = Path.Combine(copilotHome, "copilot-instructions.md");
        var runtime = AecApplication.ReadOptionalTextFile(
            runtimePath,
            "Copilot runtime instructions");

        // Recheck the selected commit and its exact bytes after observing runtime,
        // so a concurrent checkout or canonical edit cannot redirect the apply.
        var refreshedCommit = ApplyCommand.ResolveHeadCommit(repository);
        if (!string.Equals(commit, refreshedCommit, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Repository HEAD changed during Copilot apply.");
        }

        var refreshedCanonical = ApplyCommand.ReadCommittedFile(
            repository,
            commit,
            AecApplication.CopilotSourceRelativePath,
            "Canonical Copilot instructions");
        if (!refreshedCanonical.AsSpan().SequenceEqual(canonical))
        {
            throw new InvalidOperationException(
                "Canonical Copilot instructions changed during apply.");
        }

        if (runtime is not null && runtime.AsSpan().SequenceEqual(canonical))
        {
            output.WriteLine("unchanged");
            return 0;
        }

        AtomicFile.ReplaceIfUnchanged(
            runtimePath,
            runtime,
            canonical,
            "Copilot runtime instructions",
            UnixFileMode.UserRead | UnixFileMode.UserWrite);
        output.WriteLine("applied");
        return 0;
    }

    private static void ValidateBinding(string repository, byte[] canonical)
    {
        var binding = CopilotInstructionBlock.ReadRepositoryBinding(canonical)
            ?? throw new InvalidDataException(
                "Canonical Copilot instructions do not contain a supported managed AEC block.");
        if (!AecInstructionBlock.RepositoryPathsEqual(binding, repository))
        {
            throw new InvalidOperationException(
                "Committed Copilot instructions are bound to a different data repository. " +
                $"Recorded repository: {binding}. Selected repository: {repository}. " +
                "Run `aec init --provider=copilot` with the selected --repo path before applying it.");
        }
    }
}
