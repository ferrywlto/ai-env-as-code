using System.Text;

namespace Aec;

// Enrollment is deliberately separate from the legacy init lifecycle. It only
// creates a portable repository target after the user has committed the split.
internal static class SharedEnrollmentCommand
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static int Run(
        string repository,
        string provider,
        string providerHome,
        TextWriter output)
    {
        var platform = RenderCommand.CurrentPlatform();
        var fileName = provider switch
        {
            "codex" => "AGENTS.md",
            "copilot" => "copilot-instructions.md",
            _ => throw new ArgumentException($"Unsupported enrollment provider: {provider}")
        };
        var targetRelativePath = $"environment/targets/{platform}/{provider}/{fileName}";
        var targetPath = Path.Combine(repository, targetRelativePath);
        var targetDirectory = Path.GetDirectoryName(targetPath)
            ?? throw new InvalidOperationException("Enrollment target has no parent directory.");

        AecApplication.EnsureNoLinksInExistingPath(repository, "Repository path");
        AecApplication.EnsureRealDirectory(repository, "Repository");
        AecApplication.EnsureNoLinksInExistingPath(providerHome, "Provider home path");
        AecApplication.EnsureRealDirectory(providerHome, "Provider home");
        ApplyCommand.EnsureRuntimeOutsideRepository(
            repository, providerHome, fileName, "Provider runtime target");
        AecApplication.EnsureNoLinksInExistingPath(targetDirectory, "Enrollment target path");
        ValidateProviderRuntime(repository, provider, Path.Combine(providerHome, fileName));

        var sourcePaths = new[]
        {
            "environment/shared/instructions.md",
            $"environment/platforms/{platform}/policy.md",
            $"environment/providers/{provider}/overlay.md"
        };
        EnsureSourcesExist(repository, sourcePaths);

        var commit = ApplyCommand.ResolveHeadCommit(repository);
        var shared = ReadCommittedSource(repository, commit, sourcePaths[0]);
        var policy = ReadCommittedSource(repository, commit, sourcePaths[1]);
        var overlay = ReadCommittedSource(repository, commit, sourcePaths[2]);
        var desired = StrictUtf8.GetBytes(InstructionSections.Render(shared, policy, overlay));
        if (desired.Length > AecApplication.MaximumTextBytes)
        {
            throw new InvalidDataException($"Enrolled target exceeds 1 MiB: {targetRelativePath}");
        }

        // Validate the established AEC repository and allow only this target to
        // be uncommitted, so a repeat before the review commit is idempotent.
        var validatedCommit = InitCommand.ValidateCompletedRepositoryForEnrollment(
            repository, targetRelativePath);
        if (!string.Equals(commit, validatedCommit, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Repository HEAD changed during enrollment preflight.");
        }

        if (provider == "copilot")
        {
            var canonical = ApplyCommand.ReadCommittedFile(
                repository,
                commit,
                AecApplication.CopilotSourceRelativePath,
                "Canonical Copilot instructions");
            var binding = CopilotInstructionBlock.ReadRepositoryBinding(canonical)
                ?? throw new InvalidDataException(
                    "Canonical Copilot instructions do not contain a supported managed AEC block.");
            if (!AecInstructionBlock.RepositoryPathsEqual(binding, repository))
            {
                throw new InvalidOperationException(
                    "Canonical Copilot instructions are bound to a different data repository.");
            }
        }

        var current = AecApplication.ReadOptionalTextFile(targetPath, "Enrolled target");
        if (current is not null)
        {
            if (!current.AsSpan().SequenceEqual(desired))
            {
                throw new InvalidOperationException(
                    $"Enrolled target differs: {targetRelativePath}. Review it and use `aec render` " +
                    "for later source changes; init will not overwrite it.");
            }

            output.WriteLine("unchanged");
            return 0;
        }

        // Recheck the inputs before creating the first target: a source or HEAD
        // change during preflight must not produce an unreviewed composition.
        if (!string.Equals(
                commit,
                ApplyCommand.ResolveHeadCommit(repository),
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Repository HEAD changed during enrollment.");
        }

        foreach (var relativePath in sourcePaths)
        {
            _ = ApplyCommand.ReadCommittedFile(
                repository, commit, relativePath, "Authored instruction source");
        }

        Directory.CreateDirectory(targetDirectory);
        AecApplication.EnsureNoLinksInExistingPath(targetDirectory, "Enrollment target path");
        AecApplication.EnsureRealDirectory(targetDirectory, "Enrollment target directory");
        AtomicFile.WriteNew(targetPath, desired, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        if (!AecApplication.ReadRequiredTextFile(targetPath, "Enrolled target")
                .AsSpan().SequenceEqual(desired))
        {
            throw new IOException("Enrolled target verification failed after writing.");
        }

        output.WriteLine($"enrolled {targetRelativePath}");
        return 0;
    }

    private static void ValidateProviderRuntime(string repository, string provider, string runtimePath)
    {
        var runtime = AecApplication.ReadRequiredTextFile(runtimePath, "Provider runtime instructions");
        var recordedRepository = provider == "codex"
            ? AecInstructionBlock.ReadRepositoryBinding(runtime)?.Repository
            : CopilotInstructionBlock.ReadRepositoryBinding(runtime);
        if (recordedRepository is null ||
            !AecInstructionBlock.RepositoryPathsEqual(recordedRepository, repository))
        {
            throw new InvalidOperationException(
                "Provider runtime instructions are not bound to the selected AEC repository. " +
                "Run ordinary provider init first.");
        }
    }

    private static void EnsureSourcesExist(string repository, string[] relativePaths)
    {
        var missing = new List<string>();
        foreach (var relativePath in relativePaths)
        {
            var path = Path.Combine(repository, relativePath);
            var directory = Path.GetDirectoryName(path)
                ?? throw new InvalidOperationException("Authored source has no parent directory.");
            AecApplication.EnsureNoLinksInExistingPath(directory, "Authored source path");
            if (AecApplication.ReadOptionalTextFile(path, "Authored source") is null)
            {
                missing.Add(relativePath);
            }
        }

        if (missing.Count != 0)
        {
            throw new InvalidOperationException(
                "Create, review, and commit these authored sources before enrollment: " +
                string.Join(", ", missing) + ". Shared means every enrolled harness; platform means " +
                "local paths or OS rules; provider means one harness's mechanics. " +
                "AEC will not split existing instructions automatically.");
        }
    }

    private static string ReadCommittedSource(string repository, string commit, string relativePath)
    {
        var bytes = ApplyCommand.ReadCommittedFile(
            repository, commit, relativePath, "Authored instruction source");
        return StrictUtf8.GetString(bytes);
    }
}
