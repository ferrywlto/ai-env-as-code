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

        var result = Run(layout.Repository, layout.CopilotHome);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"initialized{Environment.NewLine}", result.Output);
        Assert.Empty(result.Error);
        Assert.Equal(expected, File.ReadAllBytes(layout.CanonicalInstructions));
        Assert.Equal(expected, File.ReadAllBytes(layout.RuntimeInstructions));
        Assert.Contains(
            "aec status --repo ABSOLUTE_PATH --provider=copilot",
            File.ReadAllText(layout.Skill),
            StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(layout.CopilotHome, "config.json")));
    }

    [Fact]
    public void AcceptsProviderBeforeRepository()
    {
        using var layout = new CopilotLayout();

        var result = TestApplication.Run(
            "init",
            "--provider=copilot",
            "--copilot-home",
            layout.CopilotHome,
            "--repo",
            layout.Repository);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(layout.CanonicalInstructions));
    }

    [Fact]
    public void RequiresAnExplicitRepository()
    {
        using var layout = new CopilotLayout();
        var previousDirectory = Environment.CurrentDirectory;

        try
        {
            Environment.CurrentDirectory = layout.Repository;

            var result = TestApplication.Run(
                "init",
                "--provider=copilot",
                "--copilot-home",
                layout.CopilotHome);

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("init requires --repo", result.Error, StringComparison.Ordinal);
            Assert.False(File.Exists(layout.CanonicalInstructions));
        }
        finally
        {
            Environment.CurrentDirectory = previousDirectory;
        }
    }

    [Fact]
    public void ExplicitCopilotHomeTakesPrecedenceOverEnvironment()
    {
        using var layout = new CopilotLayout();
        var previous = Environment.GetEnvironmentVariable("COPILOT_HOME");

        try
        {
            Environment.SetEnvironmentVariable("COPILOT_HOME", "not-an-absolute-path");

            var result = Run(layout.Repository, layout.CopilotHome);

            Assert.Equal(0, result.ExitCode);
            Assert.True(File.Exists(layout.RuntimeInstructions));
        }
        finally
        {
            Environment.SetEnvironmentVariable("COPILOT_HOME", previous);
        }
    }

    [Fact]
    public void UsesCopilotHomeEnvironmentWhenNoOptionWasSupplied()
    {
        using var layout = new CopilotLayout();
        var previous = Environment.GetEnvironmentVariable("COPILOT_HOME");
        var previousCodexHome = Environment.GetEnvironmentVariable("CODEX_HOME");

        try
        {
            Environment.SetEnvironmentVariable("COPILOT_HOME", layout.CopilotHome);
            Environment.SetEnvironmentVariable("CODEX_HOME", layout.CodexHome);

            var result = TestApplication.Run(
                "init",
                "--repo",
                layout.Repository,
                "--provider=copilot");

            Assert.Equal(0, result.ExitCode);
            Assert.True(File.Exists(layout.CanonicalInstructions));
        }
        finally
        {
            Environment.SetEnvironmentVariable("COPILOT_HOME", previous);
            Environment.SetEnvironmentVariable("CODEX_HOME", previousCodexHome);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImplicitCopilotHomeRejectsConflictingCodexBinding(bool forcePathChange)
    {
        using var layout = new CopilotLayout();
        var pulled = Path.Combine(layout.Root, "second checkout");
        Assert.Equal(0, TestGit.Run(layout.Root, "clone", "--quiet", layout.Repository,
            pulled).ExitCode);
        var runtimeBefore = File.ReadAllBytes(layout.RuntimeInstructions);
        var originalHead = TestGit.Run(pulled, "rev-parse", "HEAD").Output.Trim();
        var previousCodexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        var previousCopilotHome = Environment.GetEnvironmentVariable("COPILOT_HOME");

        try
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", layout.CodexHome);
            Environment.SetEnvironmentVariable("COPILOT_HOME", layout.CopilotHome);
            var arguments = new List<string>
            {
                "init", "--repo", pulled, "--provider=copilot"
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
            Assert.Equal(originalHead,
                TestGit.Run(pulled, "rev-parse", "HEAD").Output.Trim());
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
            "aec apply --repo ABSOLUTE_PATH --provider=copilot",
            File.ReadAllText(layout.Skill),
            StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAProviderPathBoundToAnotherRepository()
    {
        using var layout = new CopilotLayout();
        Assert.Equal(0, Run(layout.Repository, layout.CopilotHome).ExitCode);
        var movedRepository = Path.Combine(layout.Root, "moved-repository");
        Directory.Move(layout.Repository, movedRepository);
        var movedCanonical = Path.Combine(movedRepository, AecApplication.CopilotSourceRelativePath);
        var runtimeBefore = File.ReadAllBytes(layout.RuntimeInstructions);

        var result = Run(movedRepository, layout.CopilotHome);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("different data repository", result.Error, StringComparison.Ordinal);
        Assert.Equal(runtimeBefore, File.ReadAllBytes(layout.RuntimeInstructions));
        Assert.True(File.Exists(movedCanonical));
    }

    [Fact]
    public void ConfirmedCloneAttachmentChangesOnlyLocalRuntimeBinding()
    {
        using var layout = new CopilotLayout();
        Assert.Equal(0, Run(layout.Repository, layout.CopilotHome).ExitCode);
        Assert.Equal(0, TestGit.Run(layout.Repository, "add", "--",
            AecApplication.CopilotSourceRelativePath).ExitCode);
        Assert.Equal(0, TestGit.Run(layout.Repository, "commit", "--quiet", "--message",
            "Enroll Copilot instructions").ExitCode);

        var pulled = Path.Combine(layout.Root, "pulled-repository");
        var secondHome = Path.Combine(layout.Root, "second-copilot-home");
        Assert.Equal(0, TestGit.Run(layout.Root, "clone", "--quiet", layout.Repository,
            pulled).ExitCode);
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
        Assert.Equal(pulled, CopilotInstructionBlock.ReadRepositoryBinding(
            File.ReadAllBytes(runtimePath)));
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
        using var layout = new CopilotLayout();
        Assert.Equal(0, Run(layout.Repository, layout.CopilotHome).ExitCode);
        Assert.Equal(0, TestGit.Run(layout.Repository, "add", "--",
            AecApplication.CopilotSourceRelativePath).ExitCode);
        Assert.Equal(0, TestGit.Run(layout.Repository, "commit", "--quiet", "--message",
            "Enroll Copilot instructions").ExitCode);
        var pulled = Path.Combine(layout.Root, "dirty-pulled-repository");
        var secondHome = Path.Combine(layout.Root, "second-copilot-home");
        Assert.Equal(0, TestGit.Run(layout.Root, "clone", "--quiet", layout.Repository,
            pulled).ExitCode);
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
        using var layout = new CopilotLayout();
        CommitPortableTarget(layout);
        var platform = RenderCommand.CurrentPlatform();
        var originalAttachment = Run(layout.Repository, layout.CopilotHome);
        Assert.Equal(0, originalAttachment.ExitCode);
        Assert.Equal(0, TestApplication.Run("status", "--repo", layout.Repository,
            "--provider=copilot", "--copilot-home", layout.CopilotHome).ExitCode);

        var pulled = Path.Combine(layout.Root, "portable-clone");
        var secondHome = Path.Combine(layout.Root, "second-copilot-home");
        Assert.Equal(0, TestGit.Run(layout.Root, "clone", "--quiet", layout.Repository,
            pulled).ExitCode);
        Directory.CreateDirectory(secondHome);
        var canonicalPath = Path.Combine(pulled, AecApplication.CopilotSourceRelativePath);
        var canonicalBefore = File.ReadAllBytes(canonicalPath);
        var headBefore = TestGit.Run(pulled, "rev-parse", "HEAD").Output.Trim();

        var unconfirmed = Run(pulled, secondHome);
        Assert.Equal(1, unconfirmed.ExitCode);
        Assert.Contains("--force-path-change", unconfirmed.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(secondHome, "copilot-instructions.md")));

        File.Copy(layout.RuntimeInstructions,
            Path.Combine(secondHome, "copilot-instructions.md"));
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
        Assert.Equal(0, TestApplication.Run("status", "--repo", pulled,
            "--provider=copilot", "--copilot-home", secondHome).ExitCode);
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

        File.WriteAllBytes(targetPath, File.ReadAllBytes(Path.Combine(
            layout.Repository, "environment", "targets", platform, "copilot",
            "copilot-instructions.md")));
        var otherPlatform = platform == "windows-x64" ? "linux-arm64" : "windows-x64";
        var wrongPlatformRuntime = CopilotInstructionBlock.MergeForPortableTarget(
            File.ReadAllBytes(targetPath), pulled, otherPlatform);
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
        using var layout = new CopilotLayout();
        CommitPortableTarget(layout);
        var pulled = Path.Combine(layout.Root, "dirty-portable-clone");
        var secondHome = Path.Combine(layout.Root, "second-copilot-home");
        Assert.Equal(0, TestGit.Run(layout.Root, "clone", "--quiet", layout.Repository,
            pulled).ExitCode);
        Directory.CreateDirectory(secondHome);
        File.AppendAllText(Path.Combine(pulled, AecApplication.CopilotSourceRelativePath),
            "Uncommitted canonical edit.\n");

        var result = Run(pulled, secondHome, forcePathChange: true);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("unstaged changes", result.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(secondHome, "copilot-instructions.md")));
        Assert.False(File.Exists(Path.Combine(secondHome, "skills", "aec", "SKILL.md")));
    }

    [Fact]
    public void RejectsCodexHomeAndCopilotHomeTogether()
    {
        using var layout = new CopilotLayout();

        var result = TestApplication.Run(
            "init",
            "--repo",
            layout.Repository,
            "--provider=copilot",
            "--copilot-home",
            layout.CopilotHome,
            "--codex-home",
            layout.CodexHome);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("--codex-home is not valid", result.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(layout.CanonicalInstructions));
    }

    private static CommandResult Run(
        string repository,
        string copilotHome,
        bool forcePathChange = false)
    {
        var arguments = new List<string>
        {
            "init", "--repo", repository, "--provider=copilot", "--copilot-home", copilotHome
        };
        if (forcePathChange)
        {
            arguments.Add("--force-path-change");
        }

        return TestApplication.Run([.. arguments]);
    }

    private static void CommitPortableTarget(CopilotLayout layout)
    {
        Assert.Equal(0, Run(layout.Repository, layout.CopilotHome).ExitCode);
        var platform = RenderCommand.CurrentPlatform();
        var shared = Path.Combine(layout.Repository, "environment", "shared",
            "instructions.md");
        var policy = Path.Combine(layout.Repository, "environment", "platforms",
            platform, "policy.md");
        var overlay = Path.Combine(layout.Repository, "environment", "providers",
            "copilot", "overlay.md");
        foreach (var path in new[] { shared, policy, overlay })
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        }

        File.WriteAllText(shared, "Shared approval.\n");
        File.WriteAllText(policy, "Local paths.\n");
        File.WriteAllText(overlay, "Copilot mechanics.\n");
        Assert.Equal(0, TestGit.Run(layout.Repository, "add", "--", "environment").ExitCode);
        Assert.Equal(0, TestGit.Run(layout.Repository, "commit", "--quiet", "--message",
            "Author Copilot portable sources").ExitCode);
        Assert.Equal(0, TestApplication.Run("init", "--repo", layout.Repository,
            "--provider=copilot", "--copilot-home", layout.CopilotHome,
            "--enroll-shared").ExitCode);
        Assert.Equal(0, TestGit.Run(layout.Repository, "add", "--", "environment/targets")
            .ExitCode);
        Assert.Equal(0, TestGit.Run(layout.Repository, "commit", "--quiet", "--message",
            "Enroll Copilot target").ExitCode);
    }

    private sealed class CopilotLayout : IDisposable
    {
        public CopilotLayout()
        {
            Root = Path.Combine(
                OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(),
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

            Directory.CreateDirectory(CodexHome);
            File.WriteAllText(Path.Combine(CodexHome, "AGENTS.md"), "Codex runtime.\n");
            var initialization = TestApplication.Run(
                "init",
                "--repo",
                Repository,
                "--codex-home",
                CodexHome);
            if (initialization.ExitCode != 0)
            {
                throw new InvalidOperationException(initialization.Error);
            }

            Directory.CreateDirectory(CopilotHome);
            File.WriteAllText(RuntimeInstructions, "Existing Copilot instruction.\n");
        }

        public string Root { get; }

        public string Repository { get; }

        public string CodexHome { get; }

        public string CopilotHome { get; }

        public string RuntimeInstructions { get; }

        public string CanonicalInstructions { get; }

        public string Skill { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
