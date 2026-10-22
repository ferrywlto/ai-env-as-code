namespace Aec;

internal static class CopilotInitCommand
{
    public static int Run(
        string repository,
        string copilotHome,
        bool forcePathChange,
        TextWriter output)
    {
        ChatGptInitCommand.ValidateRepository(repository);
        AecApplication.EnsureNoLinksInExistingPath(copilotHome, "Copilot home path");
        AecApplication.EnsureRealDirectory(copilotHome, "Copilot home");
        ApplyCommand.EnsureRuntimeOutsideRepository(
            repository,
            copilotHome,
            "copilot-instructions.md",
            "Copilot runtime target");

        var platform = RenderCommand.CurrentPlatform();
        var head = GitProcess.Run(repository, "rev-parse", "--verify", "--quiet",
            "HEAD^{commit}");
        if (head.ExitCode is not (0 or 1))
        {
            throw new InvalidOperationException(
                $"Git could not inspect committed HEAD (exit code {head.ExitCode}).");
        }

        if (head.ExitCode == 0)
        {
            var commit = head.Output.Trim();
            if (CommittedTargetValidator.IsEnrolled(repository, commit, platform, "copilot"))
            {
                return AttachEnrolledRepository(
                    repository, copilotHome, forcePathChange, commit, platform, output);
            }
        }

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
        string? expectedCommit = null;
        if (canonical is null)
        {
            if (forcePathChange)
            {
                throw new InvalidOperationException(
                    "--force-path-change requires existing canonical Copilot instructions.");
            }

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
                desired = CopilotInstructionBlock.RebindRepository(canonical, repository);
                if (!forcePathChange &&
                    (runtime is null || !runtime.AsSpan().SequenceEqual(desired)))
                {
                    throw new InvalidOperationException(
                        "Canonical Copilot instructions are bound to a different data repository. " +
                        $"Recorded repository: {binding}. " +
                        $"Selected repository: {repository}. " +
                        "Confirm the local path change, then rerun with --force-path-change.");
                }

                // A pulled checkout retains the original machine's canonical path.
                // Bind this machine's runtime only; never commit a path-only change.
                expectedCommit = ApplyCommand.ResolveHeadCommit(repository);
                var committed = ApplyCommand.ReadCommittedFile(
                    repository,
                    expectedCommit,
                    AecApplication.CopilotSourceRelativePath,
                    "Canonical Copilot instructions");
                if (!canonical.AsSpan().SequenceEqual(committed))
                {
                    throw new InvalidOperationException(
                        "Canonical Copilot instructions changed before local path attachment.");
                }
            }
            else
            {
                desired = canonical;
            }
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

        VerifyResult(canonicalPath, runtimePath, canonical ?? desired, desired);
        if (expectedCommit is not null &&
            !string.Equals(expectedCommit, ApplyCommand.ResolveHeadCommit(repository),
                StringComparison.Ordinal))
        {
            throw new IOException("Repository HEAD changed during Copilot attachment.");
        }
        output.WriteLine(sourceChanged || runtimeChanged || skillChanged ? "initialized" : "unchanged");
        return 0;
    }

    private static int AttachEnrolledRepository(
        string repository,
        string copilotHome,
        bool forcePathChange,
        string commit,
        string platform,
        TextWriter output)
    {
        var canonical = ApplyCommand.ReadCommittedFile(
            repository, commit, AecApplication.CopilotSourceRelativePath,
            "Canonical Copilot instructions");
        var canonicalBinding = CopilotInstructionBlock.ReadManagedBinding(canonical)
            ?? throw new InvalidDataException(
                "Canonical Copilot instructions do not contain a supported managed AEC block.");
        var target = CommittedTargetValidator.Read(repository, platform, "copilot");
        var runtimePath = Path.Combine(copilotHome, "copilot-instructions.md");
        var runtime = AecApplication.ReadOptionalTextFile(
            runtimePath, "Copilot runtime instructions");
        var runtimeBinding = runtime is null
            ? null
            : CopilotInstructionBlock.ReadManagedBinding(runtime);
        var locallyBound = runtimeBinding is not null &&
            AecInstructionBlock.RepositoryPathsEqual(runtimeBinding.Repository, repository);
        if (locallyBound && runtimeBinding!.Platform is not null &&
            !string.Equals(runtimeBinding.Platform, platform, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Copilot runtime instructions are bound to a different AEC platform.");
        }

        var canonicalPathMatches = AecInstructionBlock.RepositoryPathsEqual(
            canonicalBinding.Repository, repository);
        if (!forcePathChange &&
            ((!canonicalPathMatches && !locallyBound) ||
             (runtimeBinding is not null && !locallyBound)))
        {
            throw new InvalidOperationException(
                "Copilot instructions record a different data repository. " +
                "Confirm the local path change, then rerun `aec init` with --force-path-change.");
        }

        // An already-portable local runtime is owned by status/apply. Only a
        // missing or legacy runtime needs an attachment write from this target.
        if (locallyBound && runtimeBinding!.Platform is not null)
        {
            var installed = AecSkillInstaller.InstallCopilot(copilotHome);
            VerifyEnrolledInputs(repository, commit, platform, canonical, target);
            output.WriteLine(installed ? "initialized" : "unchanged");
            return 0;
        }

        var desired = CopilotInstructionBlock.MergeForPortableTarget(
            target, repository, platform);
        var skillChanged = AecSkillInstaller.InstallCopilot(copilotHome);
        VerifyEnrolledInputs(repository, commit, platform, canonical, target);
        var runtimeChanged = runtime is null || !runtime.AsSpan().SequenceEqual(desired);
        if (runtimeChanged)
        {
            AtomicFile.ReplaceIfUnchanged(
                runtimePath, runtime, desired, "Copilot runtime instructions",
                UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        VerifyEnrolledInputs(repository, commit, platform, canonical, target);
        if (!AecApplication.ReadRequiredTextFile(runtimePath, "Copilot runtime instructions")
                .AsSpan().SequenceEqual(desired))
        {
            throw new IOException("Copilot runtime verification failed after enrolled init.");
        }

        output.WriteLine(skillChanged || runtimeChanged ? "initialized" : "unchanged");
        return 0;
    }

    private static void VerifyEnrolledInputs(
        string repository,
        string commit,
        string platform,
        byte[] canonical,
        byte[] target)
    {
        if (!string.Equals(commit, ApplyCommand.ResolveHeadCommit(repository),
                StringComparison.Ordinal) ||
            !ApplyCommand.ReadCommittedFile(
                repository, commit, AecApplication.CopilotSourceRelativePath,
                "Canonical Copilot instructions").AsSpan().SequenceEqual(canonical) ||
            !CommittedTargetValidator.Read(repository, platform, "copilot")
                .AsSpan().SequenceEqual(target))
        {
            throw new InvalidOperationException(
                "Committed Copilot instructions changed during enrolled init.");
        }
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

    private static void VerifyResult(
        string canonicalPath,
        string runtimePath,
        byte[] expectedCanonical,
        byte[] expectedRuntime)
    {
        var canonical = AecApplication.ReadRequiredTextFile(
            canonicalPath,
            "Canonical Copilot instructions");
        var runtime = AecApplication.ReadRequiredTextFile(
            runtimePath,
            "Copilot runtime instructions");
        if (!canonical.AsSpan().SequenceEqual(expectedCanonical) ||
            !runtime.AsSpan().SequenceEqual(expectedRuntime))
        {
            throw new IOException("Copilot initialization verification failed.");
        }
    }
}
