namespace Aec;

internal sealed record MigrationHarness(string Provider, string Home, string RuntimePath);

// Selection proves local enrollment, not capture readiness. Remote freshness,
// dirty sources, config values and backup routing are later preparation steps.
internal static class MigrationHarnessSelection
{
    public static IReadOnlyList<MigrationHarness> Read(MigrationPreflight preflight)
    {
        var selected = new List<MigrationHarness>();
        foreach (var provider in new[] { AecIdentifiers.Codex, AecIdentifiers.Copilot })
        {
            var home = provider == AecIdentifiers.Codex
                ? AecApplication.ResolveCodexHome(null)
                : AecApplication.ResolveCopilotHome(null);
            AecApplication.EnsureNoLinksInExistingPath(home, $"{provider} home path");
            if (!Directory.Exists(home))
            {
                continue;
            }

            AecApplication.EnsureRealDirectory(home, $"{provider} home");
            var fileName = provider == AecIdentifiers.Codex ? "AGENTS.md" : "copilot-instructions.md";
            ApplyCommand.EnsureRuntimeOutsideRepository(
                preflight.Repository, home, fileName, $"{provider} runtime target");
            var runtimePath = Path.Combine(home, fileName);
            var runtime = AecApplication.ReadOptionalTextFile(runtimePath,
                $"{provider} runtime instructions");
            if (runtime is null)
            {
                continue;
            }

            var codex = provider == AecIdentifiers.Codex ? AecInstructionBlock.ReadRepositoryBinding(runtime) : null;
            var copilot = provider == AecIdentifiers.Copilot ? CopilotInstructionBlock.ReadManagedBinding(runtime) : null;
            var repository = codex?.Repository ?? copilot?.Repository;
            if (repository is null)
            {
                throw new InvalidOperationException(
                    $"Existing {provider} runtime instructions have no supported AEC binding. " +
                    "Run ordinary provider init before migration.");
            }
            if (!AecInstructionBlock.RepositoryPathsEqual(repository, preflight.Repository))
            {
                throw new InvalidOperationException(
                    $"Default {provider} runtime is bound to a different AEC data repository. " +
                    $"Recorded repository: {repository}. Selected repository: {preflight.Repository}. " +
                    "Resolve local harness bindings to one checkout before migration.");
            }

            var platform = codex?.Platform ?? copilot?.Platform;
            if (platform is not null &&
                !string.Equals(platform, RenderCommand.CurrentPlatform(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Default {provider} runtime is bound to a different AEC platform. " +
                    "Run ordinary provider init before migration.");
            }

            // An interrupted migration may already have a portable runtime block.
            // Keep that enrolled harness; do not infer capture format or deploy here.
            selected.Add(new MigrationHarness(provider, home, runtimePath));
        }

        if (!string.Equals(preflight.Commit, ApplyCommand.ResolveHeadCommit(preflight.Repository),
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Repository HEAD changed during migration harness selection.");
        }

        // An empty result is not permission to initialize providers from repo folders.
        return selected.ToArray();
    }
}
