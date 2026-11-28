using System.Text;

namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class CopilotInitTests
{
    [Fact]
    public void CapturesRuntimeInstructionsAndInstallsTheCopilotSkill()
    {
        using var layout = new CopilotLayout();
        var expected = CopilotInstructionBlock.Merge(
            File.ReadAllBytes(layout.RuntimeInstructions),
            layout.Repository);

        var result = TestApplication.Run(
            "copilot", "init",
            "--home",
            layout.CopilotHome,
            "--repo",
            layout.Repository);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"initialized{Environment.NewLine}", result.Output);
        Assert.Empty(result.Error);
        Assert.Equal(expected, File.ReadAllBytes(layout.CanonicalInstructions));
        Assert.Equal(expected, File.ReadAllBytes(layout.RuntimeInstructions));
        Assert.Contains(
            "aec copilot status --repo ABSOLUTE_PATH",
            File.ReadAllText(layout.Skill),
            StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(layout.CopilotHome, "config.json")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImplicitCopilotHomeRejectsConflictingCodexBinding(bool forcePathChange)
    {
        using var layout = new CopilotLayout(initializeGit: false);
        var pulled = Path.Combine(layout.Root, "selected checkout");
        Directory.CreateDirectory(pulled);
        Directory.CreateDirectory(layout.CodexHome);
        File.WriteAllBytes(Path.Combine(layout.CodexHome, "AGENTS.md"),
            AecInstructionBlock.Merge("Codex runtime.\n"u8.ToArray(), layout.Repository));
        var runtimeBefore = File.ReadAllBytes(layout.RuntimeInstructions);
        var previousCodexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        var previousCopilotHome = Environment.GetEnvironmentVariable("COPILOT_HOME");

        try
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", layout.CodexHome);
            Environment.SetEnvironmentVariable("COPILOT_HOME", layout.CopilotHome);
            var arguments = new List<string>
            {
                "copilot", "init", "--repo", pulled
            };
            if (forcePathChange)
            {
                arguments.Add("--force-path-change");
            }

            var result = TestApplication.Run([.. arguments]);

            Assert.Equal(1, result.ExitCode);
            Assert.Contains(layout.Repository, result.Error, StringComparison.Ordinal);
            Assert.Contains(pulled, result.Error, StringComparison.Ordinal);
            Assert.Equal(runtimeBefore, File.ReadAllBytes(layout.RuntimeInstructions));
            Assert.False(File.Exists(Path.Combine(pulled,
                AecApplication.CopilotSourceRelativePath)));
            Assert.False(File.Exists(layout.Skill));
            Assert.False(Directory.Exists(Path.Combine(pulled, ".git")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", previousCodexHome);
            Environment.SetEnvironmentVariable("COPILOT_HOME", previousCopilotHome);
        }
    }

    [Fact]
    public void RepeatInitializationIsByteAndTimestampStable()
    {
        using var layout = new CopilotLayout();
        Assert.Equal(0, Run(layout.Repository, layout.CopilotHome).ExitCode);
        var timestamp = new DateTime(2020, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(layout.CanonicalInstructions, timestamp);
        File.SetLastWriteTimeUtc(layout.RuntimeInstructions, timestamp);
        File.SetLastWriteTimeUtc(layout.Skill, timestamp);

        var result = Run(layout.Repository, layout.CopilotHome);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"unchanged{Environment.NewLine}", result.Output);
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(layout.CanonicalInstructions));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(layout.RuntimeInstructions));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(layout.Skill));
    }

    [Fact]
    public void CanonicalInstructionsApplyOnARepeatInitialization()
    {
        using var layout = new CopilotLayout();
        Assert.Equal(0, Run(layout.Repository, layout.CopilotHome).ExitCode);
        var expected = File.ReadAllBytes(layout.CanonicalInstructions);
        File.WriteAllText(layout.RuntimeInstructions, "Local drift.\n");

        var result = Run(layout.Repository, layout.CopilotHome);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"initialized{Environment.NewLine}", result.Output);
        Assert.Equal(expected, File.ReadAllBytes(layout.RuntimeInstructions));
    }

    [Fact]
    public void RejectsACustomCopilotSkillBeforeWritingInstructions()
    {
        using var layout = new CopilotLayout();
        Directory.CreateDirectory(Path.GetDirectoryName(layout.Skill)!);
        File.WriteAllText(layout.Skill, "# Personal skill\n");
        var runtimeBefore = File.ReadAllBytes(layout.RuntimeInstructions);

        var result = Run(layout.Repository, layout.CopilotHome);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("conflicts with the bundled version", result.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(layout.CanonicalInstructions));
        Assert.Equal(runtimeBefore, File.ReadAllBytes(layout.RuntimeInstructions));
    }

    [Theory]
    [InlineData("Copilot-1.4.0-alpha.1.md")]
    [InlineData("Copilot-1.4.0-alpha.4.md")]
    [InlineData("Copilot-1.4.0-alpha.5.md")]
    [InlineData("Copilot-2.0.0-alpha.4-pre-migration.md")]
    public void UpgradesAnExactOfficialCopilotSkillDuringInit(string fixtureName)
    {
        using var layout = new CopilotLayout();
        Directory.CreateDirectory(Path.GetDirectoryName(layout.Skill)!);
        var predecessor = Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Skills",
            fixtureName);
        File.Copy(predecessor, layout.Skill);

        var result = Run(layout.Repository, layout.CopilotHome);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"initialized{Environment.NewLine}", result.Output);
        Assert.Contains(
            "aec copilot apply --repo ABSOLUTE_PATH",
            File.ReadAllText(layout.Skill),
            StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAProviderPathBoundToAnotherRepository()
    {
        using var layout = new CopilotLayout(createRuntime: false);
        Directory.CreateDirectory(layout.CopilotHome);
        var runtimeBefore = layout.SeedCanonicalInstructions();
        File.WriteAllBytes(layout.RuntimeInstructions, runtimeBefore);
        var movedRepository = Path.Combine(layout.Root, "moved-repository");
        Directory.Move(layout.Repository, movedRepository);
        var movedCanonical = Path.Combine(movedRepository, AecApplication.CopilotSourceRelativePath);

        var result = Run(movedRepository, layout.CopilotHome);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("different data repository", result.Error, StringComparison.Ordinal);
        Assert.Equal(runtimeBefore, File.ReadAllBytes(layout.RuntimeInstructions));
        Assert.Equal(runtimeBefore, File.ReadAllBytes(movedCanonical));
    }

    [Fact]
    public void ConfirmedCloneAttachmentChangesOnlyLocalRuntimeBinding()
    {
        using var layout = new CopilotLayout(initializeGit: false, createRuntime: false);
        layout.SeedCommittedInstructions();

        var pulled = layout.Clone("pulled-repository");
        var secondHome = Path.Combine(layout.Root, "second-copilot-home");

        Directory.CreateDirectory(secondHome);
        var canonicalPath = Path.Combine(pulled, AecApplication.CopilotSourceRelativePath);
        var canonicalBefore = File.ReadAllBytes(canonicalPath);
        var headBefore = TestGit.Run(pulled, "rev-parse", "HEAD").Output.Trim();

        var withoutConfirmation = Run(pulled, secondHome);
        Assert.Equal(1, withoutConfirmation.ExitCode);
        Assert.Contains("--force-path-change", withoutConfirmation.Error,
            StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(secondHome, "copilot-instructions.md")));
        Assert.Equal(canonicalBefore, File.ReadAllBytes(canonicalPath));

        var confirmed = Run(pulled, secondHome, forcePathChange: true);
        Assert.Equal(0, confirmed.ExitCode);
        var runtimePath = Path.Combine(secondHome, "copilot-instructions.md");
        var runtime = File.ReadAllBytes(runtimePath);
        Assert.Equal(pulled, CopilotInstructionBlock.ReadRepositoryBinding(runtime));
        Assert.Equal(CopilotInstructionBlock.RebindRepository(canonicalBefore, pulled), runtime);
        Assert.Equal(canonicalBefore, File.ReadAllBytes(canonicalPath));
        Assert.Equal(headBefore, TestGit.Run(pulled, "rev-parse", "HEAD").Output.Trim());
        Assert.Equal(string.Empty, TestGit.Run(pulled, "status", "--porcelain").Output);
        Assert.Equal($"unchanged{Environment.NewLine}", Run(pulled, secondHome).Output);
    }

    [Fact]
    public void ForcePathChangeRequiresExistingCopilotCanonicalFile()
    {
        using var layout = new CopilotLayout();

        var result = Run(layout.Repository, layout.CopilotHome, forcePathChange: true);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("requires existing canonical Copilot instructions", result.Error,
            StringComparison.Ordinal);
        Assert.False(File.Exists(layout.CanonicalInstructions));
    }

    [Fact]
    public void ConfirmedCloneAttachmentRejectsUncommittedCanonicalChanges()
    {
        using var layout = new CopilotLayout(initializeGit: false, createRuntime: false);
        layout.SeedCommittedInstructions();
        var pulled = layout.Clone("dirty-pulled-repository");
        var secondHome = Path.Combine(layout.Root, "second-copilot-home");

        Directory.CreateDirectory(secondHome);
        File.AppendAllText(Path.Combine(pulled, AecApplication.CopilotSourceRelativePath),
            "Local uncommitted edit.\n");

        var result = Run(pulled, secondHome, forcePathChange: true);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("unstaged changes", result.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(secondHome, "copilot-instructions.md")));
        Assert.False(File.Exists(Path.Combine(secondHome, "skills", "aec", "SKILL.md")));
    }

    [Fact]
    public void EnrolledTargetAttachesCloneWithoutChangingSharedHistory()
    {
        using var layout = new CopilotLayout(initializeGit: false, createRuntime: false);
        var platform = RenderCommand.CurrentPlatform();
        var target = layout.SeedEnrolledTarget(platform);
        var originalRuntime = CopilotInstructionBlock.MergeForPortableTarget(target, layout.Repository, platform);

        var pulled = layout.Clone("portable-clone");
        var secondHome = Path.Combine(layout.Root, "second-copilot-home");

        Directory.CreateDirectory(secondHome);
        var canonicalPath = Path.Combine(pulled, AecApplication.CopilotSourceRelativePath);
        var canonicalBefore = File.ReadAllBytes(canonicalPath);
        var headBefore = TestGit.Run(pulled, "rev-parse", "HEAD").Output.Trim();

        var unconfirmed = Run(pulled, secondHome);
        Assert.Equal(1, unconfirmed.ExitCode);
        Assert.Contains("--force-path-change", unconfirmed.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(secondHome, "copilot-instructions.md")));

        File.WriteAllBytes(Path.Combine(secondHome, "copilot-instructions.md"), originalRuntime);
        var wrongRuntimeBinding = Run(pulled, secondHome);
        Assert.Equal(1, wrongRuntimeBinding.ExitCode);
        Assert.Contains("--force-path-change", wrongRuntimeBinding.Error,
            StringComparison.Ordinal);
        File.Delete(Path.Combine(secondHome, "copilot-instructions.md"));

        var confirmed = Run(pulled, secondHome, forcePathChange: true);
        Assert.Equal(0, confirmed.ExitCode);
        var runtimePath = Path.Combine(secondHome, "copilot-instructions.md");
        var runtime = File.ReadAllBytes(runtimePath);
        var binding = CopilotInstructionBlock.ReadManagedBinding(runtime)!;
        Assert.Equal(pulled, binding.Repository);
        Assert.Equal(platform, binding.Platform);
        Assert.Equal(CopilotInstructionBlock.MergeForPortableTarget(target, pulled, platform),
            runtime);
        Assert.Equal(canonicalBefore, File.ReadAllBytes(canonicalPath));
        Assert.Equal(headBefore, TestGit.Run(pulled, "rev-parse", "HEAD").Output.Trim());
        Assert.Empty(TestGit.Run(pulled, "status", "--porcelain").Output);

        File.AppendAllText(runtimePath, "Local drift.\n");
        var drift = File.ReadAllBytes(runtimePath);
        Assert.Equal($"unchanged{Environment.NewLine}", Run(pulled, secondHome).Output);
        Assert.Equal(drift, File.ReadAllBytes(runtimePath));

        var targetPath = Path.Combine(pulled, "environment", "targets", platform,
            "copilot", "copilot-instructions.md");
        File.AppendAllText(targetPath, "Uncommitted target edit.\n");
        var dirtyTarget = Run(pulled, secondHome);
        Assert.Equal(1, dirtyTarget.ExitCode);
        Assert.Contains("unstaged changes", dirtyTarget.Error, StringComparison.Ordinal);
        Assert.Equal(drift, File.ReadAllBytes(runtimePath));
        Assert.Equal(canonicalBefore, File.ReadAllBytes(canonicalPath));
        Assert.Equal(headBefore, TestGit.Run(pulled, "rev-parse", "HEAD").Output.Trim());

        File.WriteAllBytes(targetPath, target);
        var otherPlatform = platform == "windows-x64" ? "linux-arm64" : "windows-x64";
        var wrongPlatformRuntime = CopilotInstructionBlock.MergeForPortableTarget(
            target, pulled, otherPlatform);
        File.WriteAllBytes(runtimePath, wrongPlatformRuntime);
        var wrongPlatformResult = Run(pulled, secondHome);
        Assert.Equal(1, wrongPlatformResult.ExitCode);
        Assert.Contains("different AEC platform", wrongPlatformResult.Error,
            StringComparison.Ordinal);
        Assert.Equal(wrongPlatformRuntime, File.ReadAllBytes(runtimePath));
    }

    [Fact]
    public void EnrolledTargetRejectsDirtyCanonicalBeforeInstallingSkill()
    {
        using var layout = new CopilotLayout(initializeGit: false, createRuntime: false);
        layout.SeedEnrolledTarget(RenderCommand.CurrentPlatform());
        var pulled = layout.Clone("dirty-portable-clone");
        var secondHome = Path.Combine(layout.Root, "second-copilot-home");

        Directory.CreateDirectory(secondHome);
        File.AppendAllText(Path.Combine(pulled, AecApplication.CopilotSourceRelativePath),
            "Uncommitted canonical edit.\n");

        var result = Run(pulled, secondHome, forcePathChange: true);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("unstaged changes", result.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(secondHome, "copilot-instructions.md")));
        Assert.False(File.Exists(Path.Combine(secondHome, "skills", "aec", "SKILL.md")));
    }

    private static CommandResult Run(
        string repository,
        string copilotHome,
        bool forcePathChange = false)
    {
        var arguments = new List<string>
        {
            "copilot", "init", "--repo", repository, "--home", copilotHome
        };
        if (forcePathChange)
        {
            arguments.Add("--force-path-change");
        }

        return TestApplication.Run([.. arguments]);
    }

    private sealed class CopilotLayout : IDisposable
    {
        private static byte[] InitialInstructions => "Existing Copilot instruction.\n"u8.ToArray();

        public CopilotLayout(bool initializeGit = true, bool createRuntime = true)
        {
            Root = Path.Combine(
                TestTemporaryDirectory.Root,
                "aec-copilot-init-tests",
                Guid.NewGuid().ToString("N"));
            Repository = Path.Combine(Root, "repository");
            CodexHome = Path.Combine(Root, "codex-home");
            CopilotHome = Path.Combine(Root, "copilot-home");
            RuntimeInstructions = Path.Combine(CopilotHome, "copilot-instructions.md");
            CanonicalInstructions = Path.Combine(
                Repository,
                AecApplication.CopilotSourceRelativePath);
            Skill = Path.Combine(CopilotHome, "skills", "aec", "SKILL.md");

            Directory.CreateDirectory(Root);
            if (initializeGit)
            {
                // Ordinary Copilot init accepts an unborn worktree with the provider directories.
                Directory.CreateDirectory(Path.Combine(Repository, "environment", "providers", "codex"));
                var initialization = TestGit.Run(
                    Repository, "init", "--quiet", "--template=", "--initial-branch=main");
                Assert.Equal(0, initialization.ExitCode);
            }

            if (createRuntime)
            {
                Directory.CreateDirectory(CopilotHome);
                File.WriteAllBytes(RuntimeInstructions, InitialInstructions);
            }
        }

        public string Root { get; }

        public string Repository { get; }

        public string CodexHome { get; }

        public string CopilotHome { get; }

        public string RuntimeInstructions { get; }

        public string CanonicalInstructions { get; }

        public string Skill { get; }

        public byte[] SeedCanonicalInstructions()
        {
            var canonical = CopilotInstructionBlock.Merge(InitialInstructions, Repository);
            Directory.CreateDirectory(Path.GetDirectoryName(CanonicalInstructions)!);
            File.WriteAllBytes(CanonicalInstructions, canonical);
            return canonical;
        }

        public void SeedCommittedInstructions()
        {
            TestGit.SeedInitializedCodexRepository(Repository, codexHome: null,
                "Codex runtime.\n", "personality = \"none\"\n");
            SeedCanonicalInstructions();
            Commit("Enroll Copilot instructions", AecApplication.CopilotSourceRelativePath);
        }

        public byte[] SeedEnrolledTarget(string platform)
        {
            SeedCommittedInstructions();
            const string sharedPath = "environment/shared/instructions.md";
            var policyPath = $"environment/platforms/{platform}/policy.md";
            const string overlayPath = "environment/providers/copilot/overlay.md";
            const string shared = "Shared approval.\n";
            const string policy = "Local paths.\n";
            const string overlay = "Copilot mechanics.\n";
            Write(sharedPath, Encoding.UTF8.GetBytes(shared));
            Write(policyPath, Encoding.UTF8.GetBytes(policy));
            Write(overlayPath, Encoding.UTF8.GetBytes(overlay));
            Commit("Author Copilot portable sources", sharedPath, policyPath, overlayPath);

            var targetPath = $"environment/targets/{platform}/copilot/copilot-instructions.md";
            var target = Encoding.UTF8.GetBytes(InstructionSections.Render(shared, policy, overlay));
            Write(targetPath, target);
            Commit("Enroll Copilot target", targetPath);
            return target;

            void Write(string relativePath, byte[] bytes)
            {
                var path = Path.Combine(Repository, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, bytes);
            }
        }

        public string Clone(string directoryName)
        {
            var checkout = Path.Combine(Root, directoryName);
            _ = GitProcess.RunRequired(Root, "Could not clone the Copilot fixture",
                "clone", "--quiet", "--template=", "--config", "core.autocrlf=false", Repository, checkout);
            return checkout;
        }

        private void Commit(string message, params string[] paths)
        {
            _ = GitProcess.RunRequired(Repository, "Could not stage the Copilot fixture", ["add", "--", .. paths]);
            _ = GitProcess.RunRequired(Repository, "Could not commit the Copilot fixture", "commit", "--quiet", "-m", message);
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
