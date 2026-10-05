namespace Aec;

// A pulled target is repository data, not evidence that this harness is active
// locally. Read the selected runtime and its exact AEC binding before planning.
internal sealed record PortableRuntimeSnapshot(
    string Repository,
    string Platform,
    string Provider,
    string RuntimePath,
    byte[] Target,
    byte[]? Runtime,
    int? BlockVersion)
{
    public static PortableRuntimeSnapshot Read(
        string repository,
        string provider,
        string providerHome)
    {
        if (!Path.IsPathFullyQualified(repository) ||
            !Path.IsPathFullyQualified(providerHome))
        {
            throw new ArgumentException("Repository and provider home must be absolute paths.");
        }

        var fileName = provider switch
        {
            "codex" => "AGENTS.md",
            "copilot" => "copilot-instructions.md",
            _ => throw new ArgumentException($"Unsupported provider: {provider}")
        };

        repository = Path.GetFullPath(repository);
        providerHome = Path.GetFullPath(providerHome);
        AecApplication.EnsureNoLinksInExistingPath(providerHome, "Provider home path");
        AecApplication.EnsureRealDirectory(providerHome, "Provider home");
        ApplyCommand.EnsureRuntimeOutsideRepository(
            repository, providerHome, fileName, "Provider runtime target");

        var platform = RenderCommand.CurrentPlatform();
        var target = CommittedTargetValidator.Read(repository, platform, provider);
        var runtimePath = Path.Combine(providerHome, fileName);
        var runtime = AecApplication.ReadOptionalTextFile(
            runtimePath, "Provider runtime instructions");
        if (runtime is null)
        {
            return new PortableRuntimeSnapshot(
                repository, platform, provider, runtimePath, target, null, null);
        }

        var codexBinding = provider == "codex"
            ? AecInstructionBlock.ReadRepositoryBinding(runtime)
            : null;
        var copilotBinding = provider == "copilot"
            ? CopilotInstructionBlock.ReadManagedBinding(runtime)
            : null;
        var recordedRepository = codexBinding?.Repository ?? copilotBinding?.Repository;
        if (recordedRepository is null ||
            !AecInstructionBlock.RepositoryPathsEqual(recordedRepository, repository))
        {
            throw new InvalidOperationException(
                "Provider runtime instructions are not bound to the selected AEC repository. " +
                "Run ordinary provider init first.");
        }

        var recordedPlatform = codexBinding?.Platform ?? copilotBinding?.Platform;
        if (recordedPlatform is not null &&
            !string.Equals(recordedPlatform, platform, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Provider runtime instructions are bound to a different AEC platform. " +
                "Run ordinary provider init first.");
        }

        return new PortableRuntimeSnapshot(
            repository,
            platform,
            provider,
            runtimePath,
            target,
            runtime,
            codexBinding?.Version ?? copilotBinding?.Version);
    }
}
