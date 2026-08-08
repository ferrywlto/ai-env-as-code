using System.Diagnostics;
using System.Text;

namespace Aec.Tests;

[Collection(ProcessStateCollection.Name)]
public sealed class InitTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InitializesMissingOrExistingEmptyDirectory(bool preCreateTarget)
    {
        using var layout = new InitLayout();
        if (preCreateTarget)
        {
            Directory.CreateDirectory(layout.Target);
        }

        var result = Run(layout.Target, layout.CodexHome);

        AssertInitialized(layout, result);
        Assert.Contains(
            $"The AEC data repository selected by `--repo` is `{layout.Target}`.",
            File.ReadAllText(layout.Source),
            StringComparison.Ordinal);
    }

    [Fact]
    public void OrdinaryInitializationInstallsTheAecSkill()
    {
        using var layout = new InitLayout();

        var result = Run(layout.Target, layout.CodexHome);

        Assert.Equal(0, result.ExitCode);
        var skillRoot = Path.Combine(layout.CodexHome, "skills", "aec");
        var skill = File.ReadAllText(Path.Combine(skillRoot, "SKILL.md"));
        Assert.Contains("name: aec", skill, StringComparison.Ordinal);
        Assert.Contains("runtime → repository", skill, StringComparison.Ordinal);
        Assert.Contains("committed repository → runtime", skill, StringComparison.Ordinal);
        Assert.Contains("aec init --repo ABSOLUTE_PATH", skill, StringComparison.Ordinal);
        Assert.DoesNotContain("aec init ABSOLUTE_DIRECTORY", skill, StringComparison.Ordinal);
        var openAi = File.ReadAllText(Path.Combine(skillRoot, "agents", "openai.yaml"));
        Assert.Contains("display_name: \"AI Environment as Code\"", openAi, StringComparison.Ordinal);
    }

    [Fact]
    public void ExistingExactSkillIsNotRewrittenForAnotherRepository()
    {
        using var layout = new InitLayout();
        Assert.Equal(0, Run(layout.Target, layout.CodexHome).ExitCode);
        var skillRoot = Path.Combine(layout.CodexHome, "skills", "aec");
        var skillPath = Path.Combine(skillRoot, "SKILL.md");
        var openAiPath = Path.Combine(skillRoot, "agents", "openai.yaml");
        var timestamp = new DateTime(2020, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(skillPath, timestamp);
        File.SetLastWriteTimeUtc(openAiPath, timestamp);
        var skillBefore = File.ReadAllBytes(skillPath);
        var openAiBefore = File.ReadAllBytes(openAiPath);

        var result = Run(Path.Combine(layout.Root, "second repository"), layout.CodexHome);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(skillBefore, File.ReadAllBytes(skillPath));
        Assert.Equal(openAiBefore, File.ReadAllBytes(openAiPath));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(skillPath));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(openAiPath));
    }

    [Fact]
    public void CompletedRepositoryAtTheSamePathAttachesToAMissingRuntime()
    {
        using var layout = new InitLayout();
        Assert.Equal(0, Run(layout.Target, layout.CodexHome).ExitCode);
        var headBefore = RunGit(layout.Target, "rev-parse", "HEAD").Output.Trim();
        var sourceBefore = File.ReadAllBytes(layout.Source);
        File.Delete(layout.Runtime);
        Directory.Delete(Path.Combine(layout.CodexHome, "skills"), recursive: true);

        var result = Run(layout.Target, layout.CodexHome);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"initialized{Environment.NewLine}", result.Output);
        Assert.Empty(result.Error);
        Assert.Equal(sourceBefore, File.ReadAllBytes(layout.Source));
        Assert.Equal(sourceBefore, File.ReadAllBytes(layout.Runtime));
        Assert.True(File.Exists(Path.Combine(layout.CodexHome, "skills", "aec", "SKILL.md")));
        Assert.Equal(headBefore, RunGit(layout.Target, "rev-parse", "HEAD").Output.Trim());
        Assert.Empty(RunGit(layout.Target, "status", "--porcelain").Output);
    }

    [Fact]
    public void MovedCompletedRepositoryRequiresPathChangeConfirmationWithoutMutation()
    {
        using var layout = new InitLayout();
        Assert.Equal(0, Run(layout.Target, layout.CodexHome).ExitCode);
        var oldRepository = layout.Target;
        var movedRepository = Path.Combine(layout.Root, "pulled data repository");
        Directory.Move(oldRepository, movedRepository);
        var movedSource = Path.Combine(movedRepository, AecApplication.SourceRelativePath);
        var headBefore = RunGit(movedRepository, "rev-parse", "HEAD").Output.Trim();
        var sourceBefore = File.ReadAllBytes(movedSource);
        File.Delete(layout.Runtime);
        Directory.Delete(Path.Combine(layout.CodexHome, "skills"), recursive: true);

        var result = Run(movedRepository, layout.CodexHome);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(oldRepository, result.Error, StringComparison.Ordinal);
        Assert.Contains(movedRepository, result.Error, StringComparison.Ordinal);
        Assert.Contains("--force-path-change", result.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(layout.Runtime));
        Assert.False(Directory.Exists(Path.Combine(layout.CodexHome, "skills")));
        Assert.Equal(sourceBefore, File.ReadAllBytes(movedSource));
        Assert.Equal(headBefore, RunGit(movedRepository, "rev-parse", "HEAD").Output.Trim());
        Assert.Empty(RunGit(movedRepository, "status", "--porcelain").Output);
    }

    [Theory]
    [InlineData(false, 3, 3)]
    [InlineData(true, 4, 4)]
    public void ConfirmedPathChangeRebindsCommitsAndAppliesIdempotently(
        bool includeChatGptProvider,
        int expectedBlockVersion,
        int expectedCommitCount)
    {
        using var layout = new InitLayout();
        Assert.Equal(0, Run(layout.Target, layout.CodexHome).ExitCode);
        if (includeChatGptProvider)
        {
            var current = File.ReadAllBytes(layout.Source);
            File.WriteAllBytes(
                layout.Source,
                AecInstructionBlock.MergeForChatGptProvider(current, layout.Target));
            Assert.Equal(
                0,
                RunGit(layout.Target, "add", "--", AecApplication.SourceRelativePath).ExitCode);
            Assert.Equal(
                0,
                RunGit(layout.Target, "commit", "--quiet", "--message", "Enable ChatGPT provider").ExitCode);
        }

        var movedRepository = Path.Combine(layout.Root, "pulled data repository");
        Directory.Move(layout.Target, movedRepository);
        var movedSource = Path.Combine(movedRepository, AecApplication.SourceRelativePath);
        File.Delete(layout.Runtime);
        Directory.Delete(Path.Combine(layout.CodexHome, "skills"), recursive: true);

        var result = Run(movedRepository, layout.CodexHome, forcePathChange: true);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"initialized{Environment.NewLine}", result.Output);
        Assert.Empty(result.Error);
        var source = File.ReadAllBytes(movedSource);
        var binding = AecInstructionBlock.ReadRepositoryBinding(source);
        Assert.NotNull(binding);
        Assert.Equal(expectedBlockVersion, binding.Version);
        Assert.Equal(movedRepository, binding.Repository);
        Assert.Contains("Existing instruction.", File.ReadAllText(movedSource), StringComparison.Ordinal);
        Assert.Equal(
            1,
            File.ReadAllText(movedSource)
                .Split("<!-- AEC:BEGIN", StringSplitOptions.None).Length - 1);
        Assert.Equal(source, File.ReadAllBytes(layout.Runtime));
        Assert.Equal(
            "Rebind AEC repository path",
            RunGit(movedRepository, "log", "-1", "--format=%s").Output.Trim());
        Assert.Equal(
            expectedCommitCount.ToString(),
            RunGit(movedRepository, "rev-list", "--count", "HEAD").Output.Trim());
        Assert.Equal(
            AecApplication.SourceRelativePath,
            RunGit(
                movedRepository,
                "diff-tree",
                "--no-commit-id",
                "--name-only",
                "-r",
                "HEAD").Output.Trim());
        Assert.Empty(RunGit(movedRepository, "status", "--porcelain").Output);

        var head = RunGit(movedRepository, "rev-parse", "HEAD").Output.Trim();
        var repeated = Run(movedRepository, layout.CodexHome, forcePathChange: true);

        Assert.Equal(0, repeated.ExitCode);
        Assert.Equal(head, RunGit(movedRepository, "rev-parse", "HEAD").Output.Trim());
        Assert.Equal(source, File.ReadAllBytes(layout.Runtime));
    }

    [Fact]
    public void ForcePathChangeIsRejectedForFreshInitialization()
    {
        using var layout = new InitLayout();
        var runtimeBefore = File.ReadAllBytes(layout.Runtime);

        var result = Run(layout.Target, layout.CodexHome, forcePathChange: true);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("existing initialized repository", result.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(layout.Target));
        Assert.Equal(runtimeBefore, File.ReadAllBytes(layout.Runtime));
        Assert.False(Directory.Exists(Path.Combine(layout.CodexHome, "skills")));
    }

    [Fact]
    public void ForcePathChangeIsRejectedForABaselineOnlyPartialInitialization()
    {
        using var layout = new InitLayout();
        var baseline = CreateBaselineOnlyInitialization(layout);
        var sourceBefore = File.ReadAllBytes(layout.Source);
        var runtimeBefore = File.ReadAllBytes(layout.Runtime);

        var result = Run(layout.Target, layout.CodexHome, forcePathChange: true);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("partial baseline", result.Error, StringComparison.Ordinal);
        Assert.Equal(baseline, RunGit(layout.Target, "rev-parse", "HEAD").Output.Trim());
        Assert.Equal(sourceBefore, File.ReadAllBytes(layout.Source));
        Assert.Equal(runtimeBefore, File.ReadAllBytes(layout.Runtime));
        Assert.False(Directory.Exists(Path.Combine(layout.CodexHome, "skills")));
    }

    [Fact]
    public void PartialExactSkillInstallationIsCompletedWithoutRewritingExistingFile()
    {
        using var layout = new InitLayout();
        Assert.Equal(0, Run(layout.Target, layout.CodexHome).ExitCode);
        var skillRoot = Path.Combine(layout.CodexHome, "skills", "aec");
        var skillPath = Path.Combine(skillRoot, "SKILL.md");
        var openAiPath = Path.Combine(skillRoot, "agents", "openai.yaml");
        var expectedOpenAi = File.ReadAllBytes(openAiPath);
        File.Delete(openAiPath);
        var timestamp = new DateTime(2020, 3, 4, 5, 6, 8, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(skillPath, timestamp);

        var result = Run(Path.Combine(layout.Root, "second repository"), layout.CodexHome);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expectedOpenAi, File.ReadAllBytes(openAiPath));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(skillPath));
    }

    [Theory]
    [InlineData("SKILL.md")]
    [InlineData("agents/openai.yaml")]
    public void ConflictingSkillFileFailsBeforeRepositoryOrRuntimeMutation(string relativePath)
    {
        using var layout = new InitLayout();
        var skillRoot = Path.Combine(layout.CodexHome, "skills", "aec");
        var conflictingPath = Path.Combine(
            skillRoot,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(conflictingPath)!);
        File.WriteAllText(conflictingPath, "preserve conflicting skill\n");
        var runtimeBefore = File.ReadAllBytes(layout.Runtime);

        var result = Run(layout.Target, layout.CodexHome);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("conflicts with the bundled version", result.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(layout.Target));
        Assert.Equal(runtimeBefore, File.ReadAllBytes(layout.Runtime));
        Assert.Equal("preserve conflicting skill\n", File.ReadAllText(conflictingPath));
        var otherPath = relativePath == "SKILL.md"
            ? Path.Combine(skillRoot, "agents", "openai.yaml")
            : Path.Combine(skillRoot, "SKILL.md");
        Assert.False(File.Exists(otherPath));
    }

    [Fact]
    public void SkillInstallationPreservesUnmanagedEntries()
    {
        using var layout = new InitLayout();
        var skillsRoot = Path.Combine(layout.CodexHome, "skills");
        var unrelatedSkill = Path.Combine(skillsRoot, "other", "SKILL.md");
        var unmanagedAecFile = Path.Combine(skillsRoot, "aec", "notes.md");
        Directory.CreateDirectory(Path.GetDirectoryName(unrelatedSkill)!);
        Directory.CreateDirectory(Path.GetDirectoryName(unmanagedAecFile)!);
        File.WriteAllText(unrelatedSkill, "other skill\n");
        File.WriteAllText(unmanagedAecFile, "user note\n");

        var result = Run(layout.Target, layout.CodexHome);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("other skill\n", File.ReadAllText(unrelatedSkill));
        Assert.Equal("user note\n", File.ReadAllText(unmanagedAecFile));
        Assert.True(File.Exists(Path.Combine(skillsRoot, "aec", "SKILL.md")));
        Assert.True(File.Exists(Path.Combine(skillsRoot, "aec", "agents", "openai.yaml")));
    }

    [Fact]
    public void LinkedSkillDirectoryFailsWithoutWritingItsReferent()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var layout = new InitLayout();
        var skillsRoot = Path.Combine(layout.CodexHome, "skills");
        var referent = Path.Combine(layout.Root, "skill referent");
        Directory.CreateDirectory(skillsRoot);
        Directory.CreateDirectory(referent);
        Directory.CreateSymbolicLink(Path.Combine(skillsRoot, "aec"), referent);
        var runtimeBefore = File.ReadAllBytes(layout.Runtime);

        var result = Run(layout.Target, layout.CodexHome);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("symbolic link", result.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(layout.Target));
        Assert.Equal(runtimeBefore, File.ReadAllBytes(layout.Runtime));
        Assert.Empty(Directory.GetFileSystemEntries(referent));
    }

    [Fact]
    public void LinkedCodexHomeAncestorFailsWithoutInstallingTheSkill()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var layout = new InitLayout();
        var referent = Path.Combine(layout.Root, "codex referent");
        var realCodexHome = Path.Combine(referent, "home");
        var link = Path.Combine(layout.Root, "codex link");
        var linkedCodexHome = Path.Combine(link, "home");
        Directory.CreateDirectory(realCodexHome);
        var runtime = Path.Combine(realCodexHome, "AGENTS.md");
        File.WriteAllText(runtime, "preserve runtime\n");
        Directory.CreateSymbolicLink(link, referent);

        var result = Run(layout.Target, linkedCodexHome);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("symbolic link", result.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(layout.Target));
        Assert.False(Directory.Exists(Path.Combine(realCodexHome, "skills")));
        Assert.Equal("preserve runtime\n", File.ReadAllText(runtime));
    }

    [Theory]
    [InlineData("skills")]
    [InlineData("skills/aec")]
    [InlineData("skills/aec/agents")]
    public void FileAtRequiredSkillDirectoryFailsBeforeMutation(string relativePath)
    {
        using var layout = new InitLayout();
        var occupiedPath = Path.Combine(
            layout.CodexHome,
            relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(occupiedPath)!);
        File.WriteAllText(occupiedPath, "preserve occupying file\n");
        var runtimeBefore = File.ReadAllBytes(layout.Runtime);

        var result = Run(layout.Target, layout.CodexHome);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("not a directory", result.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(layout.Target));
        Assert.Equal(runtimeBefore, File.ReadAllBytes(layout.Runtime));
        Assert.Equal("preserve occupying file\n", File.ReadAllText(occupiedPath));
    }

    [Fact]
    public void DirectoryAtManagedSkillFileFailsBeforeMutation()
    {
        using var layout = new InitLayout();
        var skillPath = Path.Combine(layout.CodexHome, "skills", "aec", "SKILL.md");
        Directory.CreateDirectory(skillPath);
        var runtimeBefore = File.ReadAllBytes(layout.Runtime);

        var result = Run(layout.Target, layout.CodexHome);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("must be a regular file", result.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(layout.Target));
        Assert.Equal(runtimeBefore, File.ReadAllBytes(layout.Runtime));
        Assert.True(Directory.Exists(skillPath));
    }

    [Fact]
    public void OrdinaryInitializationDoesNotOptIntoTheChatGptProvider()
    {
        using var layout = new InitLayout();

        var result = Run(layout.Target, layout.CodexHome);

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(
            "<!-- AEC:BEGIN version=3 -->",
            File.ReadAllText(layout.Source),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Manual ChatGPT instruction backups",
            File.ReadAllText(layout.Source),
            StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(
            layout.Target,
            "environment",
            "providers",
            "chatgpt")));
    }

    [Theory]
    [InlineData("ordinary.txt")]
    [InlineData(".keep")]
    public void RejectsNonEmptyDirectoryWithoutChangingIt(string entryName)
    {
        using var layout = new InitLayout();
        Directory.CreateDirectory(layout.Target);
        var existingPath = Path.Combine(layout.Target, entryName);
        File.WriteAllText(existingPath, "preserve me");

        var runtimeBefore = File.ReadAllBytes(layout.Runtime);
        var result = Run(layout.Target, layout.CodexHome);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("not empty", result.Error, StringComparison.Ordinal);
        Assert.Equal("preserve me", File.ReadAllText(existingPath));
        Assert.Single(Directory.GetFileSystemEntries(layout.Target));
        Assert.Equal(runtimeBefore, File.ReadAllBytes(layout.Runtime));
        Assert.False(Directory.Exists(Path.Combine(layout.CodexHome, "skills", "aec")));
    }

    [Fact]
    public void SecondInitializationAppliesTheCompletedRepositoryWithoutAnotherCommit()
    {
        using var layout = new InitLayout();
        var first = Run(layout.Target, layout.CodexHome);
        Assert.Equal(0, first.ExitCode);
        var source = Path.Combine(layout.Target, "environment", "providers", "codex", "AGENTS.md");
        var gitHead = Path.Combine(layout.Target, ".git", "HEAD");
        var headBefore = File.ReadAllBytes(gitHead);
        var commitBefore = RunGit(layout.Target, "rev-parse", "HEAD").Output.Trim();
        var sourceBefore = File.ReadAllBytes(source);
        File.WriteAllText(layout.Runtime, "divergent live instruction\n");

        var second = Run(layout.Target, layout.CodexHome);

        Assert.Equal(0, second.ExitCode);
        Assert.Equal($"initialized{Environment.NewLine}", second.Output);
        Assert.Empty(second.Error);
        Assert.Equal(sourceBefore, File.ReadAllBytes(source));
        Assert.Equal(sourceBefore, File.ReadAllBytes(layout.Runtime));
        Assert.Equal(headBefore, File.ReadAllBytes(gitHead));
        Assert.Equal(commitBefore, RunGit(layout.Target, "rev-parse", "HEAD").Output.Trim());
        Assert.Empty(RunGit(layout.Target, "status", "--porcelain").Output);
    }

    [Fact]
    public void ResumesARecognizableBaselineOnlyInitialization()
    {
        using var layout = new InitLayout();
        var baseline = CreateBaselineOnlyInitialization(layout);

        var result = Run(layout.Target, layout.CodexHome);

        AssertInitialized(layout, result);
        Assert.Equal(
            baseline,
            RunGit(layout.Target, "rev-parse", "HEAD~1").Output.Trim());
    }

    [Fact]
    public void RejectsBaselineOnlyInitializationWhenRuntimeHasChanged()
    {
        using var layout = new InitLayout();
        var baseline = CreateBaselineOnlyInitialization(layout);
        File.WriteAllText(layout.Runtime, "newer runtime instruction\n");
        var sourceBefore = File.ReadAllBytes(layout.Source);

        var result = Run(layout.Target, layout.CodexHome);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("baseline", result.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("newer runtime instruction\n", File.ReadAllText(layout.Runtime));
        Assert.Equal(sourceBefore, File.ReadAllBytes(layout.Source));
        Assert.Equal(baseline, RunGit(layout.Target, "rev-parse", "HEAD").Output.Trim());
    }

    [Theory]
    [InlineData("wrong-subject")]
    [InlineData("wrong-branch")]
    [InlineData("extra-entry")]
    [InlineData("dirty-canonical")]
    public void RejectsNearMissBaselineOnlyInitialization(string variation)
    {
        using var layout = new InitLayout();
        CreateBaselineOnlyInitialization(layout);

        switch (variation)
        {
            case "wrong-subject":
                Assert.Equal(
                    0,
                    RunGit(
                        layout.Target,
                        "commit",
                        "--amend",
                        "--quiet",
                        "--message",
                        "Different subject").ExitCode);
                break;
            case "wrong-branch":
                Assert.Equal(0, RunGit(layout.Target, "branch", "--move", "other").ExitCode);
                break;
            case "extra-entry":
                File.WriteAllText(Path.Combine(layout.Target, "extra.txt"), "preserve\n");
                break;
            case "dirty-canonical":
                File.WriteAllText(layout.Source, "unrecognized pending content\n");
                break;
            default:
                throw new InvalidOperationException($"Unknown test variation: {variation}");
        }

        var headBefore = RunGit(layout.Target, "rev-parse", "HEAD").Output.Trim();
        var statusBefore = RunGit(layout.Target, "status", "--porcelain=v1").Output;
        var runtimeBefore = File.ReadAllBytes(layout.Runtime);

        var result = Run(layout.Target, layout.CodexHome);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("not a resumable baseline-only", result.Error, StringComparison.Ordinal);
        Assert.Equal(headBefore, RunGit(layout.Target, "rev-parse", "HEAD").Output.Trim());
        Assert.Equal(statusBefore, RunGit(layout.Target, "status", "--porcelain=v1").Output);
        Assert.Equal(runtimeBefore, File.ReadAllBytes(layout.Runtime));
        Assert.False(Directory.Exists(Path.Combine(layout.CodexHome, "skills", "aec")));
    }

    [Fact]
    public void RejectsAParentedCommitHiddenByShallowHistory()
    {
        using var layout = new InitLayout();
        CreateBaselineOnlyInitialization(layout);
        Assert.Equal(
            0,
            RunGit(
                layout.Target,
                "commit",
                "--allow-empty",
                "--quiet",
                "--message",
                "Backup Codex AGENTS.md").ExitCode);
        var head = RunGit(layout.Target, "rev-parse", "HEAD").Output.Trim();
        File.WriteAllText(Path.Combine(layout.Target, ".git", "shallow"), $"{head}\n");

        var result = Run(layout.Target, layout.CodexHome);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("root commit", result.Error, StringComparison.Ordinal);
        Assert.Equal(head, RunGit(layout.Target, "rev-parse", "HEAD").Output.Trim());
        Assert.False(Directory.Exists(Path.Combine(layout.CodexHome, "skills", "aec")));
    }

    [Fact]
    public void RejectsGitMetadataLinkedOutsideTheSelectedRepository()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var layout = new InitLayout();
        CreateBaselineOnlyInitialization(layout);
        var objects = Path.Combine(layout.Target, ".git", "objects");
        var externalObjects = Path.Combine(layout.Root, "external objects");
        Directory.Move(objects, externalObjects);
        Directory.CreateSymbolicLink(objects, externalObjects);
        var runtimeBefore = File.ReadAllBytes(layout.Runtime);

        var result = Run(layout.Target, layout.CodexHome);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Git metadata must not contain links", result.Error, StringComparison.Ordinal);
        Assert.Equal(runtimeBefore, File.ReadAllBytes(layout.Runtime));
        Assert.NotNull(new DirectoryInfo(objects).LinkTarget);
        Assert.False(Directory.Exists(Path.Combine(layout.CodexHome, "skills", "aec")));
    }

    [Fact]
    public void FailedInstructionCommitLeavesRuntimeAtTheCommittedBaselineAndCanResume()
    {
        using var layout = new InitLayout();
        var baseline = CreateBaselineOnlyInitialization(layout);
        Assert.Equal(
            0,
            RunGit(layout.Target, "config", "--local", "commit.gpgSign", "true").ExitCode);
        Assert.Equal(
            0,
            RunGit(layout.Target, "config", "--local", "gpg.format", "openpgp").ExitCode);
        Assert.Equal(
            0,
            RunGit(
                layout.Target,
                "config",
                "--local",
                "gpg.program",
                Path.Combine(layout.Root, "missing-gpg")).ExitCode);
        var runtimeBefore = File.ReadAllBytes(layout.Runtime);

        var failed = Run(layout.Target, layout.CodexHome);

        Assert.Equal(1, failed.ExitCode);
        Assert.Contains("commit", failed.Error, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(baseline, RunGit(layout.Target, "rev-parse", "HEAD").Output.Trim());
        Assert.Equal(runtimeBefore, File.ReadAllBytes(layout.Runtime));

        Assert.Equal(
            0,
            RunGit(layout.Target, "config", "--local", "commit.gpgSign", "false").ExitCode);
        var resumed = Run(layout.Target, layout.CodexHome);

        AssertInitialized(layout, resumed);
    }

    [Fact]
    public void InitializationCommitRejectsUnexpectedCanonicalContent()
    {
        using var layout = new InitLayout();
        var baseline = CreateBaselineOnlyInitialization(layout);
        var runtime = File.ReadAllBytes(layout.Runtime);
        var expected = AecInstructionBlock.Merge(runtime, layout.Target);
        File.WriteAllText(layout.Source, "concurrent canonical edit\n");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            BackupCommand.CommitCanonicalSource(
                layout.Target,
                "Initialize AEC instructions",
                baseline,
                expected,
                allowEmpty: true));

        Assert.Contains("changed before", exception.Message, StringComparison.Ordinal);
        Assert.Equal(baseline, RunGit(layout.Target, "rev-parse", "HEAD").Output.Trim());
        Assert.Equal(runtime, File.ReadAllBytes(layout.Runtime));
    }

    [Fact]
    public void RejectsAFileTargetWithoutChangingIt()
    {
        using var layout = new InitLayout();
        File.WriteAllText(layout.Target, "preserve me");

        var result = Run(layout.Target, layout.CodexHome);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("not a directory", result.Error, StringComparison.Ordinal);
        Assert.Equal("preserve me", File.ReadAllText(layout.Target));
    }

    [Fact]
    public void RejectsASymbolicLinkTargetWithoutChangingItsReferent()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var layout = new InitLayout();
        var referent = Path.Combine(layout.Root, "referent");
        Directory.CreateDirectory(referent);
        Directory.CreateSymbolicLink(layout.Target, referent);

        var result = Run(layout.Target, layout.CodexHome);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("symbolic link", result.Error, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFileSystemEntries(referent));
        Assert.NotNull(new DirectoryInfo(layout.Target).LinkTarget);
    }

    [Fact]
    public void RejectsASymbolicLinkAncestorWithoutChangingItsReferent()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var layout = new InitLayout();
        var referent = Path.Combine(layout.Root, "referent");
        var link = Path.Combine(layout.Root, "link");
        var target = Path.Combine(link, "data");
        Directory.CreateDirectory(referent);
        Directory.CreateSymbolicLink(link, referent);

        var result = Run(target, layout.CodexHome);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("symbolic link", result.Error, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFileSystemEntries(referent));
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void PrependsTheManagedBlockAndPreservesExistingRuntimeBytes()
    {
        using var layout = new InitLayout();
        var original = "# Personal instructions\r\n\r\n  Preserve spacing.  \r\n"u8.ToArray();
        File.WriteAllBytes(layout.Runtime, original);
        var expected = AecInstructionBlock.Merge(original, layout.Target);

        var result = Run(layout.Target, layout.CodexHome);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected, File.ReadAllBytes(layout.Runtime));
        Assert.Equal(expected, File.ReadAllBytes(layout.Source));
        Assert.Equal(0, expected.AsSpan().IndexOf("<!-- AEC:BEGIN version=3 -->"u8));
        Assert.True(expected.AsSpan().EndsWith(original));
        var baselineObject = RunGit(
            layout.Target,
            "rev-parse",
            $"HEAD~1:{AecApplication.SourceRelativePath}").Output.Trim();
        Assert.Equal(
            original,
            GitProcess.RunRequiredBytes(
                layout.Target,
                "Git could not read test baseline",
                "cat-file",
                "blob",
                baselineObject));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ReplacesAnOlderManagedBlockInPlace(int version)
    {
        using var layout = new InitLayout();
        var original = Encoding.UTF8.GetBytes(
            $"prefix\n<!-- AEC:BEGIN version={version} -->\nobsolete\n<!-- AEC:END -->\nsuffix");
        File.WriteAllBytes(layout.Runtime, original);
        var expected = AecInstructionBlock.Merge(original, layout.Target);

        var result = Run(layout.Target, layout.CodexHome);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected, File.ReadAllBytes(layout.Runtime));
        Assert.Equal(expected, File.ReadAllBytes(layout.Source));
        Assert.StartsWith("prefix\n", File.ReadAllText(layout.Runtime), StringComparison.Ordinal);
        Assert.EndsWith("suffix", File.ReadAllText(layout.Runtime), StringComparison.Ordinal);
        Assert.DoesNotContain("obsolete", File.ReadAllText(layout.Runtime), StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentManagedBlockWithStaleContentIsReconciled()
    {
        using var layout = new InitLayout();
        var current = """
            <!-- AEC:BEGIN version=3 -->
            current custom body
            <!-- AEC:END -->
            Other instructions.
            """u8.ToArray();
        File.WriteAllBytes(layout.Runtime, current);
        var expected = AecInstructionBlock.Merge(current, layout.Target);

        var result = Run(layout.Target, layout.CodexHome);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected, File.ReadAllBytes(layout.Runtime));
        Assert.Equal(expected, File.ReadAllBytes(layout.Source));
        Assert.DoesNotContain("current custom body", File.ReadAllText(layout.Runtime));
        Assert.EndsWith("Other instructions.", File.ReadAllText(layout.Runtime), StringComparison.Ordinal);
    }

    [Fact]
    public void MissingRuntimeFileIsAnErrorBeforeRepositoryOrSkillMutation()
    {
        using var layout = new InitLayout();
        File.Delete(layout.Runtime);

        var result = Run(layout.Target, layout.CodexHome);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("Runtime target does not exist", result.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(layout.Runtime));
        Assert.False(Directory.Exists(layout.Target));
        Assert.False(Directory.Exists(Path.Combine(layout.CodexHome, "skills", "aec")));
    }

    [Fact]
    public void NewerManagedBlockFailureLeavesTheExactRuntimeBaselineCommitted()
    {
        using var layout = new InitLayout();
        var runtime = """
            <!-- AEC:BEGIN version=4 -->
            future
            <!-- AEC:END -->
            """u8.ToArray();
        File.WriteAllBytes(layout.Runtime, runtime);

        var result = Run(layout.Target, layout.CodexHome);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("newer unsupported", result.Error, StringComparison.Ordinal);
        AssertBaselineOnly(layout, runtime);
        Assert.Equal(runtime, File.ReadAllBytes(layout.Runtime));
    }

    [Fact]
    public void OversizedMergeFailureLeavesTheExactRuntimeBaselineCommitted()
    {
        using var layout = new InitLayout();
        var runtime = Enumerable.Repeat((byte)'a', AecApplication.MaximumTextBytes).ToArray();
        File.WriteAllBytes(layout.Runtime, runtime);

        var result = Run(layout.Target, layout.CodexHome);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Merged instructions exceed 1 MiB", result.Error, StringComparison.Ordinal);
        AssertBaselineOnly(layout, runtime);
        Assert.Equal(runtime, File.ReadAllBytes(layout.Runtime));
    }

    [Fact]
    public void AlreadyManagedRuntimeStillCreatesTheExplicitSecondCommit()
    {
        using var layout = new InitLayout();
        var managed = AecInstructionBlock.Merge([], layout.Target);
        File.WriteAllBytes(layout.Runtime, managed);

        var result = Run(layout.Target, layout.CodexHome);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("2", RunGit(layout.Target, "rev-list", "--count", "HEAD").Output.Trim());
        Assert.Equal(
            $"Backup Codex AGENTS.md{Environment.NewLine}" +
            $"Initialize AEC instructions{Environment.NewLine}",
            RunGit(layout.Target, "log", "--reverse", "--format=%s").Output);
        Assert.Empty(
            RunGit(
                layout.Target,
                "diff-tree",
                "--no-commit-id",
                "--name-only",
                "-r",
                "HEAD").Output);
        Assert.Equal(managed, File.ReadAllBytes(layout.Runtime));
        Assert.Equal(managed, File.ReadAllBytes(layout.Source));
    }

    [Fact]
    public void SymbolicLinkRuntimeFailsBeforeRepositoryCreation()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var layout = new InitLayout();
        var referent = Path.Combine(layout.Root, "runtime referent.md");
        File.WriteAllText(referent, "preserve me\n");
        File.Delete(layout.Runtime);
        File.CreateSymbolicLink(layout.Runtime, referent);

        var result = Run(layout.Target, layout.CodexHome);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Runtime target must not be a symbolic link", result.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(layout.Target));
        Assert.False(Directory.Exists(Path.Combine(layout.CodexHome, "skills", "aec")));
        Assert.Equal("preserve me\n", File.ReadAllText(referent));
    }

    [Fact]
    public void RuntimeReplacementStopsWhenTheFileChangedAfterItWasRead()
    {
        using var layout = new InitLayout();
        var original = "original\n"u8.ToArray();
        var concurrent = "concurrent edit\n"u8.ToArray();
        var replacement = AecInstructionBlock.Merge(original, layout.Target);
        File.WriteAllBytes(layout.Runtime, concurrent);

        var exception = Assert.Throws<IOException>(() =>
            AtomicFile.ReplaceIfUnchanged(layout.Runtime, original, replacement));

        Assert.Contains("changed during the operation", exception.Message, StringComparison.Ordinal);
        Assert.Equal(concurrent, File.ReadAllBytes(layout.Runtime));
        Assert.DoesNotContain(
            Directory.GetFiles(layout.CodexHome),
            path => Path.GetFileName(path).StartsWith(".AGENTS.md.aec-", StringComparison.Ordinal));
    }

    [Fact]
    public void MissingRuntimeSnapshotDoesNotOverwriteAConcurrentEmptyFile()
    {
        using var layout = new InitLayout();
        File.WriteAllBytes(layout.Runtime, []);

        var exception = Assert.Throws<IOException>(() =>
            AtomicFile.ReplaceIfUnchanged(
                layout.Runtime,
                expectedCurrent: null,
                content: "replacement\n"u8.ToArray()));

        Assert.Contains("changed during the operation", exception.Message, StringComparison.Ordinal);
        Assert.Empty(File.ReadAllBytes(layout.Runtime));
    }

    private static CommandResult Run(
        string target,
        string codexHome,
        bool forcePathChange = false)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var arguments = new List<string>
        {
            "init",
            "--repo",
            target,
            "--codex-home",
            codexHome
        };
        if (forcePathChange)
        {
            arguments.Add("--force-path-change");
        }

        var exitCode = AecApplication.Run(
            [.. arguments],
            output,
            error);
        return new CommandResult(exitCode, output.ToString(), error.ToString());
    }

    private static string CreateBaselineOnlyInitialization(InitLayout layout)
    {
        Directory.CreateDirectory(layout.Target);
        Assert.Equal(
            0,
            RunGit(
                layout.Target,
                "init",
                "--quiet",
                "--template=",
                "--initial-branch=main").ExitCode);
        Assert.Equal(
            0,
            RunGit(layout.Target, "config", "--local", "core.autocrlf", "false").ExitCode);
        Directory.CreateDirectory(Path.GetDirectoryName(layout.Source)!);
        File.WriteAllBytes(layout.Source, File.ReadAllBytes(layout.Runtime));

        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = AecApplication.Run(
            ["backup", "--repo", layout.Target, "--codex-home", layout.CodexHome],
            output,
            error);

        Assert.Equal(0, exitCode);
        Assert.Empty(error.ToString());
        return RunGit(layout.Target, "rev-parse", "HEAD").Output.Trim();
    }

    private static void AssertBaselineOnly(InitLayout layout, byte[] expected)
    {
        Assert.True(Directory.Exists(layout.Target));
        Assert.Equal("1", RunGit(layout.Target, "rev-list", "--count", "HEAD").Output.Trim());
        Assert.Equal(
            "Backup Codex AGENTS.md",
            RunGit(layout.Target, "log", "-1", "--format=%s").Output.Trim());
        Assert.Equal(
            expected,
            Encoding.UTF8.GetBytes(RunGit(
                layout.Target,
                "show",
                $"HEAD:{AecApplication.SourceRelativePath}").Output));
        Assert.Empty(RunGit(layout.Target, "status", "--porcelain").Output);
    }

    private static void AssertInitialized(InitLayout layout, CommandResult result)
    {
        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"initialized{Environment.NewLine}", result.Output);
        Assert.Empty(result.Error);

        var source = Path.Combine(layout.Target, "environment", "providers", "codex", "AGENTS.md");
        Assert.True(File.Exists(source));
        Assert.Equal(File.ReadAllBytes(layout.Runtime), File.ReadAllBytes(source));
        Assert.Contains("<!-- AEC:BEGIN version=3 -->", File.ReadAllText(source), StringComparison.Ordinal);
        Assert.Contains("Existing instruction.", File.ReadAllText(source), StringComparison.Ordinal);
        Assert.True(Directory.Exists(Path.Combine(layout.Target, ".git")));

        var prefix = RunGit(layout.Target, "rev-parse", "--show-prefix");
        Assert.Equal(0, prefix.ExitCode);
        Assert.Equal(string.Empty, prefix.Output.Trim());

        var branch = RunGit(layout.Target, "symbolic-ref", "--short", "HEAD");
        Assert.Equal(0, branch.ExitCode);
        Assert.Equal("main", branch.Output.Trim());

        var staged = RunGit(layout.Target, "diff", "--cached", "--name-only");
        Assert.Equal(0, staged.ExitCode);
        Assert.Equal(string.Empty, staged.Output.Trim());
        Assert.Equal(
            "false",
            RunGit(layout.Target, "config", "--local", "--get", "core.autocrlf").Output.Trim());

        var head = RunGit(layout.Target, "rev-parse", "--verify", "HEAD");
        Assert.Equal(0, head.ExitCode);
        Assert.Equal("2", RunGit(layout.Target, "rev-list", "--count", "HEAD").Output.Trim());
        Assert.Equal(
            $"Backup Codex AGENTS.md{Environment.NewLine}" +
            $"Initialize AEC instructions{Environment.NewLine}",
            RunGit(layout.Target, "log", "--reverse", "--format=%s").Output);
        Assert.Equal(
            "Existing instruction.\n",
            RunGit(
                layout.Target,
                "show",
                $"HEAD~1:{AecApplication.SourceRelativePath}").Output);
        Assert.Equal(
            File.ReadAllText(layout.Source),
            RunGit(
                layout.Target,
                "show",
                $"HEAD:{AecApplication.SourceRelativePath}").Output);
        Assert.Equal(
            AecApplication.SourceRelativePath,
            RunGit(
                layout.Target,
                "diff-tree",
                "--no-commit-id",
                "--name-only",
                "-r",
                "HEAD").Output.Trim());

        // Config initialization is wired in a later v0.11 checkpoint. Seed both
        // sides here so this lifecycle assertion continues to exercise status.
        File.WriteAllText(
            Path.Combine(layout.Target, AecApplication.ConfigSourceRelativePath),
            "personality = \"none\"\n");
        File.WriteAllText(
            Path.Combine(layout.CodexHome, "config.toml"),
            "personality = \"none\"\n");
        var statusOutput = new StringWriter();
        var statusError = new StringWriter();
        var statusExitCode = AecApplication.Run(
            ["status", "--repo", layout.Target, "--codex-home", layout.CodexHome],
            statusOutput,
            statusError);
        Assert.Equal(0, statusExitCode);
        Assert.Equal(
            $"codex/AGENTS.md   in_sync{Environment.NewLine}" +
            $"codex/config.toml in_sync{Environment.NewLine}",
            statusOutput.ToString());
        Assert.Empty(statusError.ToString());
    }

    private static GitResult RunGit(string workingDirectory, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        foreach (var variable in new[]
                 {
                     "GIT_DIR",
                     "GIT_WORK_TREE",
                     "GIT_COMMON_DIR",
                     "GIT_OBJECT_DIRECTORY",
                     "GIT_INDEX_FILE",
                     "GIT_ALTERNATE_OBJECT_DIRECTORIES"
                 })
        {
            startInfo.Environment.Remove(variable);
        }

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return new GitResult(
            process.ExitCode,
            output.GetAwaiter().GetResult(),
            error.GetAwaiter().GetResult());
    }

    private sealed record CommandResult(int ExitCode, string Output, string Error);

    private sealed record GitResult(int ExitCode, string Output, string Error);

    private sealed class InitLayout : IDisposable
    {
        public InitLayout()
        {
            Root = Path.Combine(RealTemporaryDirectory(), "aec-init-tests", Guid.NewGuid().ToString("N"));
            Target = Path.Combine(Root, "data repository");
            CodexHome = Path.Combine(Root, "codex home");
            Runtime = Path.Combine(CodexHome, "AGENTS.md");
            Source = Path.Combine(Target, "environment", "providers", "codex", "AGENTS.md");
            Directory.CreateDirectory(Root);
            Directory.CreateDirectory(CodexHome);
            File.WriteAllText(Runtime, "Existing instruction.\n");
        }

        public string Root { get; }

        public string Target { get; }

        public string CodexHome { get; }

        public string Runtime { get; }

        public string Source { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }

        private static string RealTemporaryDirectory()
        {
            return OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath();
        }
    }
}

[Collection(ProcessStateCollection.Name)]
public sealed class InitProcessStateTests
{
    [Fact]
    public void InitRequiresRepoFlagInsteadOfDefaultingToTheCurrentDirectory()
    {
        using var directory = new ProcessStateDirectory();
        var previous = Environment.CurrentDirectory;

        try
        {
            Environment.CurrentDirectory = directory.Path;
            var output = new StringWriter();
            var error = new StringWriter();

            var exitCode = AecApplication.Run(
                ["init", "--codex-home", directory.CodexHome],
                output,
                error);

            Assert.Equal(1, exitCode);
            Assert.Empty(output.ToString());
            Assert.Contains("init requires --repo", error.ToString(), StringComparison.Ordinal);
            Assert.False(File.Exists(System.IO.Path.Combine(
                directory.Path,
                "environment",
                "providers",
                "codex",
                "AGENTS.md")));
        }
        finally
        {
            Environment.CurrentDirectory = previous;
        }
    }

    [Fact]
    public void InitializesTheAbsoluteRepoFlag()
    {
        using var directory = new ProcessStateDirectory();
        var target = System.IO.Path.Combine(directory.Path, "child");
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = AecApplication.Run(
            ["init", "--repo", target, "--codex-home", directory.CodexHome],
            output,
            error);

        Assert.Equal(0, exitCode);
        Assert.Equal($"initialized{Environment.NewLine}", output.ToString());
        Assert.Empty(error.ToString());
        Assert.True(Directory.Exists(System.IO.Path.Combine(target, ".git")));
    }

    [Fact]
    public void RejectsRelativeRepoFlag()
    {
        using var directory = new ProcessStateDirectory();
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = AecApplication.Run(
            ["init", "--repo", "relative", "--codex-home", directory.CodexHome],
            output,
            error);

        Assert.Equal(1, exitCode);
        Assert.Empty(output.ToString());
        Assert.Contains("--repo must be an absolute path", error.ToString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(System.IO.Path.Combine(directory.Path, "relative")));
    }

    [Theory]
    [InlineData(null, "--repo requires a value")]
    [InlineData("", "--repo requires a non-empty path")]
    public void RejectsMissingOrEmptyRepoValue(string? value, string expectedError)
    {
        using var directory = new ProcessStateDirectory();
        var arguments = value is null
            ? new[] { "init", "--repo" }
            : new[] { "init", "--repo", value };
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = AecApplication.Run(arguments, output, error);

        Assert.Equal(1, exitCode);
        Assert.Empty(output.ToString());
        Assert.Contains(expectedError, error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsDuplicateRepoFlag()
    {
        using var directory = new ProcessStateDirectory();
        var first = System.IO.Path.Combine(directory.Path, "first");
        var second = System.IO.Path.Combine(directory.Path, "second");
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = AecApplication.Run(
            ["init", "--repo", first, "--repo", second],
            output,
            error);

        Assert.Equal(1, exitCode);
        Assert.Contains("--repo may be specified only once", error.ToString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(first));
        Assert.False(Directory.Exists(second));
    }

    [Theory]
    [InlineData("apply", "Unknown argument")]
    [InlineData("provider", "not valid with --provider=chatgpt")]
    [InlineData("duplicate", "may be specified only once")]
    public void ForcePathChangeIsAcceptedOnlyOnceByOrdinaryInit(
        string variation,
        string expectedError)
    {
        using var directory = new ProcessStateDirectory();
        var repository = System.IO.Path.Combine(directory.Path, "target");
        var arguments = variation switch
        {
            "apply" => new[]
            {
                "apply", "--repo", repository, "--force-path-change"
            },
            "provider" => new[]
            {
                "init", "--repo", repository, "--provider=chatgpt", "--force-path-change"
            },
            _ => new[]
            {
                "init", "--repo", repository,
                "--force-path-change", "--force-path-change"
            }
        };
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = AecApplication.Run(arguments, output, error);

        Assert.Equal(1, exitCode);
        Assert.Empty(output.ToString());
        Assert.Contains(expectedError, error.ToString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(repository));
    }

    [Fact]
    public void GitEnvironmentCannotRedirectInitialization()
    {
        using var directory = new ProcessStateDirectory();
        var target = System.IO.Path.Combine(directory.Path, "target");
        var hostileRoot = System.IO.Path.Combine(directory.Path, "hostile");
        var hostileGitDirectory = System.IO.Path.Combine(hostileRoot, "redirected.git");
        var template = System.IO.Path.Combine(hostileRoot, "template");
        Directory.CreateDirectory(template);
        File.WriteAllText(System.IO.Path.Combine(template, "template-marker"), "must not be copied");
        var variables = new Dictionary<string, string?>
        {
            ["GIT_DIR"] = Environment.GetEnvironmentVariable("GIT_DIR"),
            ["GIT_WORK_TREE"] = Environment.GetEnvironmentVariable("GIT_WORK_TREE"),
            ["GIT_COMMON_DIR"] = Environment.GetEnvironmentVariable("GIT_COMMON_DIR"),
            ["GIT_TEMPLATE_DIR"] = Environment.GetEnvironmentVariable("GIT_TEMPLATE_DIR"),
            ["GIT_CONFIG_COUNT"] = Environment.GetEnvironmentVariable("GIT_CONFIG_COUNT"),
            ["GIT_CONFIG_KEY_0"] = Environment.GetEnvironmentVariable("GIT_CONFIG_KEY_0"),
            ["GIT_CONFIG_VALUE_0"] = Environment.GetEnvironmentVariable("GIT_CONFIG_VALUE_0")
        };

        try
        {
            Environment.SetEnvironmentVariable("GIT_DIR", hostileGitDirectory);
            Environment.SetEnvironmentVariable("GIT_WORK_TREE", hostileRoot);
            Environment.SetEnvironmentVariable("GIT_COMMON_DIR", hostileGitDirectory);
            Environment.SetEnvironmentVariable("GIT_TEMPLATE_DIR", template);
            Environment.SetEnvironmentVariable("GIT_CONFIG_COUNT", "1");
            Environment.SetEnvironmentVariable("GIT_CONFIG_KEY_0", "init.templateDir");
            Environment.SetEnvironmentVariable("GIT_CONFIG_VALUE_0", template);
            var output = new StringWriter();
            var error = new StringWriter();

            var exitCode = AecApplication.Run(
                ["init", "--codex-home", directory.CodexHome, "--repo", target],
                output,
                error);

            Assert.Equal(0, exitCode);
            Assert.Equal($"initialized{Environment.NewLine}", output.ToString());
            Assert.Empty(error.ToString());
            Assert.True(Directory.Exists(System.IO.Path.Combine(target, ".git")));
            Assert.False(Directory.Exists(hostileGitDirectory));
            Assert.False(File.Exists(System.IO.Path.Combine(target, ".git", "template-marker")));
            Assert.True(File.Exists(System.IO.Path.Combine(target, ".git", "index")));
            Assert.Equal(
                "2",
                GitProcess.Run(target, "rev-list", "--count", "HEAD").Output.Trim());
            Assert.Equal(
                "must not be copied",
                File.ReadAllText(System.IO.Path.Combine(template, "template-marker")));
        }
        finally
        {
            foreach (var variable in variables)
            {
                Environment.SetEnvironmentVariable(variable.Key, variable.Value);
            }
        }
    }

    [Fact]
    public void RepositoryLocalIdentityDoesNotSatisfyFreshInitializationPreflight()
    {
        using var directory = new ProcessStateDirectory();
        var target = System.IO.Path.Combine(directory.Path, "target");
        var emptyGlobalConfig = System.IO.Path.Combine(directory.Path, "empty-global-config");
        File.WriteAllText(
            emptyGlobalConfig,
            """
            [user]
                useConfigOnly = true
            """);
        Assert.Equal(
            0,
            GitProcess.Run(
                directory.Path,
                "init",
                "--quiet",
                "--template=",
                "--initial-branch=main").ExitCode);
        Assert.Equal(
            0,
            GitProcess.Run(directory.Path, "config", "--local", "user.name", "Local Only").ExitCode);
        Assert.Equal(
            0,
            GitProcess.Run(
                directory.Path,
                "config",
                "--local",
                "user.email",
                "local@example.invalid").ExitCode);
        var previousDirectory = Environment.CurrentDirectory;
        var variables = new[]
        {
            "GIT_CONFIG_GLOBAL",
            "GIT_CONFIG_NOSYSTEM",
            "GIT_AUTHOR_NAME",
            "GIT_AUTHOR_EMAIL",
            "GIT_COMMITTER_NAME",
            "GIT_COMMITTER_EMAIL"
        }.ToDictionary(
            name => name,
            Environment.GetEnvironmentVariable,
            StringComparer.Ordinal);

        try
        {
            Environment.CurrentDirectory = directory.Path;
            Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", emptyGlobalConfig);
            Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", "1");
            foreach (var name in variables.Keys.Where(name =>
                         name.StartsWith("GIT_AUTHOR_", StringComparison.Ordinal) ||
                         name.StartsWith("GIT_COMMITTER_", StringComparison.Ordinal)))
            {
                Environment.SetEnvironmentVariable(name, null);
            }

            var output = new StringWriter();
            var error = new StringWriter();

            var exitCode = AecApplication.Run(
                ["init", "--repo", target, "--codex-home", directory.CodexHome],
                output,
                error);

            Assert.Equal(1, exitCode);
            Assert.Empty(output.ToString());
            Assert.Contains("identity is not configured", error.ToString(), StringComparison.Ordinal);
            Assert.False(Directory.Exists(target));
            Assert.False(Directory.Exists(
                System.IO.Path.Combine(directory.CodexHome, "skills", "aec")));
        }
        finally
        {
            foreach (var variable in variables)
            {
                Environment.SetEnvironmentVariable(variable.Key, variable.Value);
            }

            Environment.CurrentDirectory = previousDirectory;
        }
    }

    [Fact]
    public void ConditionalIdentityForTheRequestedGitDirectoryIsAccepted()
    {
        using var directory = new ProcessStateDirectory();
        var target = System.IO.Path.Combine(directory.Path, "conditional target");
        var includedConfig = System.IO.Path.Combine(directory.Path, "conditional-identity");
        var globalConfig = System.IO.Path.Combine(directory.Path, "conditional-global");
        File.WriteAllText(
            includedConfig,
            """
            [user]
                name = Conditional Identity
                email = conditional@example.invalid
            """);
        var gitDirectoryPattern = System.IO.Path
            .Combine(target, ".git")
            .Replace('\\', '/');
        var includedConfigPath = includedConfig.Replace('\\', '/');
        File.WriteAllText(
            globalConfig,
            $"""
            [user]
                useConfigOnly = true
            [commit]
                gpgSign = false
            [includeIf "gitdir:{gitDirectoryPattern}"]
                path = "{includedConfigPath}"
            """);
        var variables = new[]
        {
            "GIT_CONFIG_GLOBAL",
            "GIT_CONFIG_NOSYSTEM",
            "GIT_AUTHOR_NAME",
            "GIT_AUTHOR_EMAIL",
            "GIT_COMMITTER_NAME",
            "GIT_COMMITTER_EMAIL"
        }.ToDictionary(
            name => name,
            Environment.GetEnvironmentVariable,
            StringComparer.Ordinal);

        try
        {
            Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", globalConfig);
            Environment.SetEnvironmentVariable("GIT_CONFIG_NOSYSTEM", "1");
            foreach (var name in variables.Keys.Where(name =>
                         name.StartsWith("GIT_AUTHOR_", StringComparison.Ordinal) ||
                         name.StartsWith("GIT_COMMITTER_", StringComparison.Ordinal)))
            {
                Environment.SetEnvironmentVariable(name, null);
            }

            var output = new StringWriter();
            var error = new StringWriter();
            var exitCode = AecApplication.Run(
                ["init", "--repo", target, "--codex-home", directory.CodexHome],
                output,
                error);

            Assert.Equal(0, exitCode);
            Assert.Equal($"initialized{Environment.NewLine}", output.ToString());
            Assert.Empty(error.ToString());
            Assert.Equal(
                "Conditional Identity",
                GitProcess.Run(target, "log", "-1", "--format=%an").Output.Trim());
        }
        finally
        {
            foreach (var variable in variables)
            {
                Environment.SetEnvironmentVariable(variable.Key, variable.Value);
            }
        }
    }

    [Fact]
    public void UsesCodexHomeFromTheEnvironmentWhenTheFlagIsAbsent()
    {
        using var directory = new ProcessStateDirectory();
        var target = System.IO.Path.Combine(directory.Path, "target");
        var previous = Environment.GetEnvironmentVariable("CODEX_HOME");

        try
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", directory.CodexHome);
            var output = new StringWriter();
            var error = new StringWriter();

            var exitCode = AecApplication.Run(
                ["init", "--repo", target],
                output,
                error);

            Assert.Equal(0, exitCode);
            Assert.Equal(File.ReadAllBytes(System.IO.Path.Combine(directory.CodexHome, "AGENTS.md")),
                File.ReadAllBytes(System.IO.Path.Combine(
                    target,
                    "environment",
                    "providers",
                    "codex",
                    "AGENTS.md")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", previous);
        }
    }

    [Fact]
    public void RejectsDuplicateCodexHomeArgumentsWithoutCreatingTheTarget()
    {
        using var directory = new ProcessStateDirectory();
        var target = System.IO.Path.Combine(directory.Path, "target");
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = AecApplication.Run(
            [
                "init",
                "--repo",
                target,
                "--codex-home",
                directory.CodexHome,
                "--codex-home",
                directory.CodexHome
            ],
            output,
            error);

        Assert.Equal(1, exitCode);
        Assert.Contains("may be specified only once", error.ToString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void RejectsUnknownOptionsAndPositionalOperands()
    {
        using var directory = new ProcessStateDirectory();

        foreach (var arguments in new[]
                 {
                     new[] { "init", "--unknown" },
                     new[] { "init", "positional", "--codex-home", directory.CodexHome }
                 })
        {
            var output = new StringWriter();
            var error = new StringWriter();

            var exitCode = AecApplication.Run(arguments, output, error);

            Assert.Equal(1, exitCode);
            Assert.Empty(output.ToString());
        }
    }

    private sealed class ProcessStateDirectory : IDisposable
    {
        public ProcessStateDirectory()
        {
            var temporaryDirectory = OperatingSystem.IsMacOS()
                ? "/private/tmp"
                : System.IO.Path.GetTempPath();
            Root = System.IO.Path.Combine(
                temporaryDirectory,
                "aec-init-process-tests",
                Guid.NewGuid().ToString("N"));
            Path = System.IO.Path.Combine(Root, "working directory");
            CodexHome = System.IO.Path.Combine(Root, "codex home");
            Directory.CreateDirectory(Path);
            Directory.CreateDirectory(CodexHome);
            File.WriteAllText(System.IO.Path.Combine(CodexHome, "AGENTS.md"), "Existing instruction.\n");
        }

        public string Path { get; }

        public string CodexHome { get; }

        private string Root { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }
    }
}
