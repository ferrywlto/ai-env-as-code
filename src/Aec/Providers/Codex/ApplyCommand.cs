namespace Aec;

internal static class ApplyCommand
{
    internal static int RunForInitialization(
        string repository,
        string codexHome,
        TextWriter output,
        byte[] expectedRuntime,
        CodexPersonality? expectedRuntimePersonality,
        string expectedCommit,
        byte[] expectedSource,
        byte[] expectedConfig)
    {
        ArgumentNullException.ThrowIfNull(expectedRuntime);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedCommit);
        ArgumentNullException.ThrowIfNull(expectedSource);
        ArgumentNullException.ThrowIfNull(expectedConfig);
        return Run(
            repository,
            codexHome,
            output,
            TextWriter.Null,
            new ApplyExpectation(expectedRuntime, expectedRuntimePersonality,
                expectedCommit, expectedSource, expectedConfig, null, IsInitialization: true));
    }

    internal static int RunForAttachment(
        string repository,
        string codexHome,
        TextWriter output,
        TextWriter warning,
        byte[]? expectedRuntime,
        CodexPersonality? expectedRuntimePersonality,
        string expectedCommit,
        byte[] expectedSource,
        byte[] expectedConfig,
        byte[]? expectedLocalRuntimeSource = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedCommit);
        ArgumentNullException.ThrowIfNull(expectedSource);
        ArgumentNullException.ThrowIfNull(expectedConfig);
        return Run(
            repository,
            codexHome,
            output,
            warning,
            new ApplyExpectation(expectedRuntime, expectedRuntimePersonality,
                expectedCommit, expectedSource, expectedConfig, expectedLocalRuntimeSource,
                IsInitialization: false));
    }

    private static int Run(
        string repository,
        string codexHome,
        TextWriter output,
        TextWriter warning,
        ApplyExpectation expected)
    {
        RuntimePathGuard.EnsureRuntimeOutsideRepository(repository, codexHome);
        GitRepository.ValidateRepositoryRoot(repository);

        var commit = GitRepository.ResolveHeadCommit(repository);
        if (!string.Equals(commit, expected.Commit, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(expected.IsInitialization
                ? "Repository HEAD changed after the initialization commit."
                : "Repository HEAD changed before attachment apply.");
        }

        var source = CodexCanonicalFiles.ReadCommittedSource(repository, commit);
        if (!source.AsSpan().SequenceEqual(expected.Source))
        {
            throw new InvalidOperationException(expected.IsInitialization
                ? "Committed source does not match the expected initialization content."
                : "Committed source changed before attachment apply.");
        }

        var binding = AecInstructionBlock.ReadRepositoryBinding(source);
        var desiredRuntimeSource = source;
        if (binding is not null &&
            !AecInstructionBlock.RepositoryPathsEqual(binding.Repository, repository))
        {
            // Only confirmed init attachment may substitute a machine-local
            // binding. The committed bytes remain pinned and are never edited.
            var localSource = expected.LocalRuntimeSource;
            if (localSource is null ||
                !AecInstructionBlock.RebindRepository(source, repository)
                    .AsSpan().SequenceEqual(localSource))
            {
                throw new InvalidOperationException(
                    "Committed AEC instructions are bound to a different data repository. " +
                    $"Recorded repository: {binding.Repository}. " +
                    $"Selected repository: {repository}. " +
                    "Run `aec init` with the selected --repo path before applying it.");
            }

            desiredRuntimeSource = localSource;
        }

        var canonicalConfig = CodexCanonicalFiles.ReadCommittedConfig(repository, commit);
        if (!canonicalConfig.AsSpan().SequenceEqual(expected.Config))
        {
            throw new InvalidOperationException(expected.IsInitialization
                ? "Committed config does not match the expected initialization content."
                : "Committed config changed before attachment apply.");
        }

        var desiredPersonality = CodexPersonalityConfig.ReadCanonical(
            canonicalConfig,
            Path.Combine(repository, AecApplication.ConfigSourceRelativePath));

        var runtimePath = Path.Combine(codexHome, "AGENTS.md");
        var runtime = AecApplication.ReadOptionalTextFile(runtimePath, "Runtime target");
        var runtimeConfigPath = Path.Combine(codexHome, "config.toml");
        var runtimeConfig = AecApplication.ReadOptionalTextFile(runtimeConfigPath, "Runtime config");
        var runtimePersonality = runtimeConfig is null
            ? null
            : CodexPersonalityConfig.ReadRuntime(runtimeConfig, runtimeConfigPath);
        // Build and validate the config edit before either runtime file is
        // changed, so malformed TOML cannot cause a partial apply.
        var configUpdate = CodexPersonalityConfig.PlanRuntimeUpdate(
            runtimeConfig,
            runtimeConfigPath,
            desiredPersonality);

        // Revalidate provenance after observing runtime so a concurrent checkout or canonical edit stops apply.
        var currentCommit = GitRepository.ResolveHeadCommit(repository);
        if (!string.Equals(commit, currentCommit, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Repository HEAD changed during apply.");
        }

        var refreshedSource = CodexCanonicalFiles.ReadCommittedSource(repository, commit);
        if (!refreshedSource.AsSpan().SequenceEqual(source))
        {
            throw new InvalidOperationException("Canonical source changed during apply.");
        }

        var refreshedConfig = CodexCanonicalFiles.ReadCommittedConfig(repository, commit);
        if (!refreshedConfig.AsSpan().SequenceEqual(canonicalConfig))
        {
            throw new InvalidOperationException("Canonical config changed during apply.");
        }

        var agentsChanged = runtime is null || !runtime.AsSpan().SequenceEqual(desiredRuntimeSource);
        if (!agentsChanged && !configUpdate.Changed)
        {
            output.WriteLine("unchanged");
            return 0;
        }

        // Both routes permit the reviewed runtime or an already-applied target.
        if (!MatchesOptionalBytes(runtime, expected.Runtime) &&
            (runtime is null || !runtime.AsSpan().SequenceEqual(desiredRuntimeSource)))
        {
            throw new InvalidOperationException(expected.IsInitialization
                ? "Runtime target changed after the initialization backup; committed source was not applied."
                : "Runtime target changed during AEC attachment; the committed environment was not applied.");
        }

        if (runtimePersonality != expected.RuntimePersonality &&
            runtimePersonality != desiredPersonality)
        {
            throw new InvalidOperationException(expected.IsInitialization
                ? "Runtime personality changed after the initialization backup; the committed environment was not applied."
                : "Runtime personality changed during AEC attachment; the committed environment was not applied.");
        }

        if (configUpdate.Changed)
        {
            if (configUpdate.Inserted)
            {
                warning.WriteLine(
                    "warning: runtime config does not declare root `personality`; " +
                    "the committed value will be added.");
            }

            // Each target has its own compare-before-replace guard. A later
            // interruption is safe to resume with another status/apply cycle.
            AtomicFile.ReplaceIfUnchanged(
                runtimeConfigPath,
                runtimeConfig,
                configUpdate.Content,
                "Runtime config",
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        if (agentsChanged)
        {
            AtomicFile.ReplaceIfUnchanged(
                runtimePath,
                expected.IsInitialization ? expected.Runtime : runtime,
                desiredRuntimeSource);
        }

        output.WriteLine("applied");
        return 0;
    }

    private static bool MatchesOptionalBytes(byte[]? left, byte[]? right)
    {
        return left is null
            ? right is null
            : right is not null && left.AsSpan().SequenceEqual(right);
    }

    private sealed record ApplyExpectation(
        byte[]? Runtime,
        CodexPersonality? RuntimePersonality,
        string Commit,
        byte[] Source,
        byte[] Config,
        byte[]? LocalRuntimeSource,
        bool IsInitialization);
}
