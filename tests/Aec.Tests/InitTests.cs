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

        var result = Run(layout.Target);

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

        var result = Run(layout.Target);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("not empty", result.Error, StringComparison.Ordinal);
        Assert.Equal("preserve me", File.ReadAllText(existingPath));
        Assert.Single(Directory.GetFileSystemEntries(layout.Target));
    }

    [Fact]
    public void RejectsSecondInitializationWithoutChangingTheRepository()
    {
        using var layout = new InitLayout();
        var first = Run(layout.Target);
        Assert.Equal(0, first.ExitCode);
        var source = Path.Combine(layout.Target, "environment", "providers", "codex", "AGENTS.md");
        var gitHead = Path.Combine(layout.Target, ".git", "HEAD");
        var headBefore = File.ReadAllBytes(gitHead);

        var second = Run(layout.Target);

        Assert.Equal(1, second.ExitCode);
        Assert.Empty(second.Output);
        Assert.Contains("not empty", second.Error, StringComparison.Ordinal);
        Assert.Empty(File.ReadAllBytes(source));
        Assert.Equal(headBefore, File.ReadAllBytes(gitHead));
    }

    [Fact]
    public void RejectsAFileTargetWithoutChangingIt()
    {
        using var layout = new InitLayout();
        File.WriteAllText(layout.Target, "preserve me");

        var result = Run(layout.Target);

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

        var result = Run(layout.Target);

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

        var result = Run(target);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("symbolic link", result.Error, StringComparison.Ordinal);
        Assert.Empty(Directory.GetFileSystemEntries(referent));
        Assert.False(Directory.Exists(target));
    }

    private static CommandResult Run(string target)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = AecApplication.Run(["init", target], output, error);
        return new CommandResult(exitCode, output.ToString(), error.ToString());
    }

    private static void AssertInitialized(InitLayout layout, CommandResult result)
    {
        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"initialized{Environment.NewLine}", result.Output);
        Assert.Empty(result.Error);

        var source = Path.Combine(layout.Target, "environment", "providers", "codex", "AGENTS.md");
        Assert.True(File.Exists(source));
        Assert.Empty(File.ReadAllBytes(source));
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
            Directory.CreateDirectory(Root);
        }

        public string Root { get; }

        public string Target { get; }

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

            var exitCode = AecApplication.Run(["init"], output, error);

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

            var exitCode = AecApplication.Run(["init", "child"], output, error);

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

            var exitCode = AecApplication.Run(["init", target], output, error);

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

    private sealed class ProcessStateDirectory : IDisposable
    {
        public ProcessStateDirectory()
        {
            var temporaryDirectory = OperatingSystem.IsMacOS()
                ? "/private/tmp"
                : System.IO.Path.GetTempPath();
            Path = System.IO.Path.Combine(
                temporaryDirectory,
                "aec-init-process-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
    }
}
