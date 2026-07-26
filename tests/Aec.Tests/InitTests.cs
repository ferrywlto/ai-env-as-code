using System.Diagnostics;
using System.Text;

namespace Aec.Tests;

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
    public void RejectsSecondInitializationWithoutChangingTheRepository()
    {
        using var layout = new InitLayout();
        var first = Run(layout.Target, layout.CodexHome);
        Assert.Equal(0, first.ExitCode);
        var source = Path.Combine(layout.Target, "environment", "providers", "codex", "AGENTS.md");
        var gitHead = Path.Combine(layout.Target, ".git", "HEAD");
        var headBefore = File.ReadAllBytes(gitHead);
        var sourceBefore = File.ReadAllBytes(source);
        var runtimeBefore = File.ReadAllBytes(layout.Runtime);

        var second = Run(layout.Target, layout.CodexHome);

        Assert.Equal(1, second.ExitCode);
        Assert.Empty(second.Output);
        Assert.Contains("not empty", second.Error, StringComparison.Ordinal);
        Assert.Equal(sourceBefore, File.ReadAllBytes(source));
        Assert.Equal(runtimeBefore, File.ReadAllBytes(layout.Runtime));
        Assert.Equal(headBefore, File.ReadAllBytes(gitHead));
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
    public void MissingRuntimeFileIsCreatedWithTheManagedBlock()
    {
        using var layout = new InitLayout();
        File.Delete(layout.Runtime);

        var result = Run(layout.Target, layout.CodexHome);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(
            AecInstructionBlock.Merge([], layout.Target),
            File.ReadAllBytes(layout.Runtime));
        Assert.Equal(File.ReadAllBytes(layout.Runtime), File.ReadAllBytes(layout.Source));
    }

    [Fact]
    public void NewerManagedBlockFailsBeforeRepositoryCreation()
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
        Assert.False(Directory.Exists(layout.Target));
        Assert.False(Directory.Exists(Path.Combine(layout.CodexHome, "skills", "aec")));
        Assert.Equal(runtime, File.ReadAllBytes(layout.Runtime));
    }

    [Fact]
    public void OversizedMergedContentFailsBeforeRepositoryCreation()
    {
        using var layout = new InitLayout();
        var runtime = Enumerable.Repeat((byte)'a', AecApplication.MaximumTextBytes).ToArray();
        File.WriteAllBytes(layout.Runtime, runtime);

        var result = Run(layout.Target, layout.CodexHome);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Merged instructions exceed 1 MiB", result.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(layout.Target));
        Assert.False(Directory.Exists(Path.Combine(layout.CodexHome, "skills", "aec")));
        Assert.Equal(runtime, File.ReadAllBytes(layout.Runtime));
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

    private static CommandResult Run(string target, string codexHome)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = AecApplication.Run(
            ["init", "--repo", target, "--codex-home", codexHome],
            output,
            error);
        return new CommandResult(exitCode, output.ToString(), error.ToString());
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

        var head = RunGit(layout.Target, "rev-parse", "--verify", "HEAD");
        Assert.NotEqual(0, head.ExitCode);

        var statusOutput = new StringWriter();
        var statusError = new StringWriter();
        var statusExitCode = AecApplication.Run(
            ["status", "--repo", layout.Target, "--codex-home", layout.CodexHome],
            statusOutput,
            statusError);
        Assert.Equal(0, statusExitCode);
        Assert.Equal($"in_sync{Environment.NewLine}", statusOutput.ToString());
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
            Assert.False(File.Exists(System.IO.Path.Combine(target, ".git", "index")));
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
