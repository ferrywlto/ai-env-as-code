using System.Diagnostics;

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
        var expected = AecInstructionBlock.Merge(original);

        var result = Run(layout.Target, layout.CodexHome);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected, File.ReadAllBytes(layout.Runtime));
        Assert.Equal(expected, File.ReadAllBytes(layout.Source));
        Assert.Equal(0, expected.AsSpan().IndexOf("<!-- AEC:BEGIN version=1 -->"u8));
        Assert.True(expected.AsSpan().EndsWith(original));
    }

    [Fact]
    public void ReplacesAnOlderManagedBlockInPlace()
    {
        using var layout = new InitLayout();
        var original = """
            prefix
            <!-- AEC:BEGIN version=0 -->
            obsolete
            <!-- AEC:END -->
            suffix
            """u8.ToArray();
        File.WriteAllBytes(layout.Runtime, original);
        var expected = AecInstructionBlock.Merge(original);

        var result = Run(layout.Target, layout.CodexHome);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected, File.ReadAllBytes(layout.Runtime));
        Assert.Equal(expected, File.ReadAllBytes(layout.Source));
        Assert.StartsWith("prefix\n", File.ReadAllText(layout.Runtime), StringComparison.Ordinal);
        Assert.EndsWith("suffix", File.ReadAllText(layout.Runtime), StringComparison.Ordinal);
        Assert.DoesNotContain("obsolete", File.ReadAllText(layout.Runtime), StringComparison.Ordinal);
    }

    [Fact]
    public void CurrentManagedBlockIsCopiedWithoutRewritingRuntime()
    {
        using var layout = new InitLayout();
        var current = """
            <!-- AEC:BEGIN version=1 -->
            current custom body
            <!-- AEC:END -->
            Other instructions.
            """u8.ToArray();
        File.WriteAllBytes(layout.Runtime, current);
        var timestamp = new DateTime(2020, 1, 2, 3, 4, 6, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(layout.Runtime, timestamp);

        var result = Run(layout.Target, layout.CodexHome);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(current, File.ReadAllBytes(layout.Runtime));
        Assert.Equal(current, File.ReadAllBytes(layout.Source));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(layout.Runtime));
    }

    [Fact]
    public void MissingRuntimeFileIsCreatedWithTheManagedBlock()
    {
        using var layout = new InitLayout();
        File.Delete(layout.Runtime);

        var result = Run(layout.Target, layout.CodexHome);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(AecInstructionBlock.Merge([]), File.ReadAllBytes(layout.Runtime));
        Assert.Equal(File.ReadAllBytes(layout.Runtime), File.ReadAllBytes(layout.Source));
    }

    [Fact]
    public void NewerManagedBlockFailsBeforeRepositoryCreation()
    {
        using var layout = new InitLayout();
        var runtime = """
            <!-- AEC:BEGIN version=2 -->
            future
            <!-- AEC:END -->
            """u8.ToArray();
        File.WriteAllBytes(layout.Runtime, runtime);

        var result = Run(layout.Target, layout.CodexHome);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("newer unsupported", result.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(layout.Target));
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
        Assert.Equal("preserve me\n", File.ReadAllText(referent));
    }

    [Fact]
    public void RuntimeReplacementStopsWhenTheFileChangedAfterItWasRead()
    {
        using var layout = new InitLayout();
        var original = "original\n"u8.ToArray();
        var concurrent = "concurrent edit\n"u8.ToArray();
        var replacement = AecInstructionBlock.Merge(original);
        File.WriteAllBytes(layout.Runtime, concurrent);

        var exception = Assert.Throws<IOException>(() =>
            InitCommand.ReplaceFileIfUnchanged(layout.Runtime, original, replacement));

        Assert.Contains("changed during initialization", exception.Message, StringComparison.Ordinal);
        Assert.Equal(concurrent, File.ReadAllBytes(layout.Runtime));
        Assert.DoesNotContain(
            Directory.GetFiles(layout.CodexHome),
            path => Path.GetFileName(path).StartsWith(".AGENTS.md.aec-init-", StringComparison.Ordinal));
    }

    private static CommandResult Run(string target, string codexHome)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = AecApplication.Run(
            ["init", target, "--codex-home", codexHome],
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
        Assert.Contains("<!-- AEC:BEGIN version=1 -->", File.ReadAllText(source), StringComparison.Ordinal);
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
    public void InitializesTheCurrentDirectoryWhenTheOperandIsOmitted()
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

            Assert.Equal(0, exitCode);
            Assert.Equal($"initialized{Environment.NewLine}", output.ToString());
            Assert.Empty(error.ToString());
            Assert.True(File.Exists(System.IO.Path.Combine(
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
    public void ResolvesRelativeDirectoriesFromTheCurrentDirectory()
    {
        using var directory = new ProcessStateDirectory();
        var previous = Environment.CurrentDirectory;

        try
        {
            Environment.CurrentDirectory = directory.Path;
            var output = new StringWriter();
            var error = new StringWriter();

            var exitCode = AecApplication.Run(
                ["init", "child", "--codex-home", directory.CodexHome],
                output,
                error);

            Assert.Equal(0, exitCode);
            Assert.Equal($"initialized{Environment.NewLine}", output.ToString());
            Assert.Empty(error.ToString());
            Assert.True(Directory.Exists(System.IO.Path.Combine(directory.Path, "child", ".git")));
        }
        finally
        {
            Environment.CurrentDirectory = previous;
        }
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
                ["init", "--codex-home", directory.CodexHome, target],
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

            var exitCode = AecApplication.Run(["init", target], output, error);

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
    public void RejectsUnknownOptionsAndMultipleDirectoryOperands()
    {
        using var directory = new ProcessStateDirectory();

        foreach (var arguments in new[]
                 {
                     new[] { "init", "--unknown" },
                     new[] { "init", "first", "second", "--codex-home", directory.CodexHome }
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
