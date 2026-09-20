namespace Aec;

internal static class CopilotInitCommand
{
    public static int Run(string repository, string copilotHome, TextWriter output)
    {
        ChatGptInitCommand.ValidateRepository(repository);
        AecApplication.EnsureNoLinksInExistingPath(copilotHome, "Copilot home path");
        AecApplication.EnsureRealDirectory(copilotHome, "Copilot home");
        ApplyCommand.EnsureRuntimeOutsideRepository(repository, copilotHome);

        var runtimePath = Path.Combine(copilotHome, "copilot-instructions.md");
        var canonicalPath = Path.Combine(repository, AecApplication.CopilotSourceRelativePath);
        var providerDirectory = Path.GetDirectoryName(canonicalPath)
            ?? throw new InvalidOperationException("Canonical Copilot instructions have no provider directory.");

        PreflightProviderDirectory(providerDirectory);
        var runtime = AecApplication.ReadOptionalTextFile(runtimePath, "Copilot runtime instructions");
        var canonical = AecApplication.ReadOptionalTextFile(
            canonicalPath,
            "Canonical Copilot instructions");

        byte[] desired;
        if (canonical is null)
        {
            // The first provider initialization captures the local file before
            // adding guidance, mirroring Codex's initial AEC block lifecycle.
            desired = CopilotInstructionBlock.Merge(runtime ?? [], repository);
        }
        else
        {
            var binding = CopilotInstructionBlock.ReadRepositoryBinding(canonical)
                ?? throw new InvalidDataException(
                    "Canonical Copilot instructions do not contain a supported managed AEC block.");
            if (!AecInstructionBlock.RepositoryPathsEqual(binding, repository))
            {
                throw new InvalidOperationException(
                    "Canonical Copilot instructions are bound to a different data repository. " +
                    $"Recorded repository: {binding}. " +
                    $"Selected repository: {repository}.");
            }

            desired = canonical;
        }

        // Preflight every mutable target before the skill installation can make a
        // visible change. An existing custom skill is rejected rather than replaced.
        var skillChanged = AecSkillInstaller.InstallCopilot(copilotHome);
        var sourceChanged = canonical is null;
        if (sourceChanged)
        {
            Directory.CreateDirectory(providerDirectory);
            AecApplication.EnsureRealDirectory(providerDirectory, "Copilot provider directory");
            AtomicFile.WriteNew(canonicalPath, desired, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        var runtimeChanged = runtime is null || !runtime.AsSpan().SequenceEqual(desired);
        if (runtimeChanged)
        {
            AtomicFile.ReplaceIfUnchanged(
                runtimePath,
                runtime,
                desired,
                "Copilot runtime instructions",
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        VerifyResult(canonicalPath, runtimePath, desired);
        output.WriteLine(sourceChanged || runtimeChanged || skillChanged ? "initialized" : "unchanged");
        return 0;
    }

    private static void PreflightProviderDirectory(string path)
    {
        AecApplication.EnsureNoLinksInExistingPath(path, "Copilot provider path");
        if (Directory.Exists(path))
        {
            AecApplication.EnsureRealDirectory(path, "Copilot provider directory");
            return;
        }

        if (File.Exists(path))
        {
            throw new InvalidOperationException($"Copilot provider path must be a directory: {path}");
        }
    }

    private static void VerifyResult(string canonicalPath, string runtimePath, byte[] expected)
    {
        var canonical = AecApplication.ReadRequiredTextFile(
            canonicalPath,
            "Canonical Copilot instructions");
        var runtime = AecApplication.ReadRequiredTextFile(
            runtimePath,
            "Copilot runtime instructions");
        if (!canonical.AsSpan().SequenceEqual(expected) || !runtime.AsSpan().SequenceEqual(expected))
        {
            throw new IOException("Copilot initialization verification failed.");
        }
    }
}
