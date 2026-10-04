namespace Aec;

// Read-only v2 instruction comparison. Public status remains on the legacy
// source until apply can use the same portable target in the 2.0 cutover.
internal static class SharedTargetStatus
{
    public static string Inspect(string repository, string provider, string providerHome)
    {
        if (!Path.IsPathFullyQualified(repository))
        {
            throw new ArgumentException("Repository must be an absolute path.");
        }

        var fileName = provider switch
        {
            "codex" => "AGENTS.md",
            "copilot" => "copilot-instructions.md",
            _ => throw new ArgumentException($"Unsupported status provider: {provider}")
        };

        if (!Path.IsPathFullyQualified(providerHome))
        {
            throw new ArgumentException("Provider home must be an absolute path.");
        }

        repository = Path.GetFullPath(repository);
        providerHome = Path.GetFullPath(providerHome);
        AecApplication.EnsureNoLinksInExistingPath(providerHome, "Provider home path");
        AecApplication.EnsureRealDirectory(providerHome, "Provider home");
        ApplyCommand.EnsureRuntimeOutsideRepository(
            repository, providerHome, fileName, "Provider runtime target");

        // Repository presence is not local enrollment. The explicitly selected
        // local runtime must exist and, when present, bind to this repository.
        var platform = RenderCommand.CurrentPlatform();
        var target = CommittedTargetValidator.Read(repository, platform, provider);
        var runtime = AecApplication.ReadOptionalTextFile(
            Path.Combine(providerHome, fileName), "Provider runtime instructions");
        if (runtime is null)
        {
            return "missing";
        }

        var codexBinding = provider == "codex"
            ? AecInstructionBlock.ReadRepositoryBinding(runtime)
            : null;
        var binding = provider == "codex"
            ? codexBinding?.Repository
            : CopilotInstructionBlock.ReadRepositoryBinding(runtime);
        var copilotVersion = provider == "copilot"
            ? CopilotInstructionBlock.ReadManagedVersion(runtime)
            : null;
        if (binding is null ||
            !AecInstructionBlock.RepositoryPathsEqual(binding, repository))
        {
            throw new InvalidOperationException(
                "Provider runtime instructions are not bound to the selected AEC repository. " +
                "Run ordinary provider init first.");
        }

        // The provider's generated local block is intentionally outside the
        // portable target. Use the same block generator that a future apply
        // will use, then compare the complete runtime file byte-for-byte.
        var expected = provider switch
        {
            "codex" when codexBinding?.Version is 7 or 8 =>
                AecInstructionBlock.MergeForPortableTarget(
                    target, repository, platform, codexBinding.Version == 8),
            "codex" when codexBinding?.Version is 4 or 6 =>
                AecInstructionBlock.MergeForChatGptProvider(target, repository),
            "codex" => AecInstructionBlock.Merge(target, repository),
            "copilot" when copilotVersion == 2 =>
                CopilotInstructionBlock.MergeForPortableTarget(target, repository, platform),
            _ => CopilotInstructionBlock.Merge(target, repository)
        };
        return runtime.AsSpan().SequenceEqual(expected) ? "in_sync" : "different";
    }
}
