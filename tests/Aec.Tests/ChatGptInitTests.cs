namespace Aec.Tests;

[Collection(ProcessStateCollection.Name)]
public sealed class ChatGptInitTests
{
    [Fact]
    public void InitializesChatGptProviderWithoutTouchingRuntime()
    {
        using var layout = new ChatGptLayout();
        var skillsDirectory = Path.Combine(layout.CodexHome, "skills");
        Directory.Delete(skillsDirectory, recursive: true);
        var runtimeBefore = File.ReadAllBytes(layout.Runtime);

        var result = Run(layout.Repository);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"initialized{Environment.NewLine}", result.Output);
        Assert.Empty(result.Error);
        Assert.Empty(File.ReadAllBytes(layout.CustomInstructions));
        Assert.Empty(File.ReadAllBytes(layout.ProjectBaseline));
        Assert.Empty(File.ReadAllBytes(layout.GptBaseline));
        Assert.Contains(
            "<!-- AEC:BEGIN version=4 -->",
            File.ReadAllText(layout.CanonicalAgents),
            StringComparison.Ordinal);
        Assert.Contains(
            $"Manual ChatGPT instruction backups live under " +
            $"`{Path.GetDirectoryName(layout.CustomInstructions)}{Path.DirectorySeparatorChar}`.",
            File.ReadAllText(layout.CanonicalAgents),
            StringComparison.Ordinal);
        Assert.Equal(runtimeBefore, File.ReadAllBytes(layout.Runtime));
        Assert.False(Directory.Exists(skillsDirectory));
    }

    [Fact]
    public void AcceptsProviderBeforeRepo()
    {
        using var layout = new ChatGptLayout();

        var result = RunArguments(
            "init",
            "--provider=chatgpt",
            "--repo",
            layout.Repository);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"initialized{Environment.NewLine}", result.Output);
        Assert.True(File.Exists(layout.CustomInstructions));
    }

    [Fact]
    public void ProviderRequiresRepoInsteadOfDefaultingToCurrentDirectory()
    {
        using var layout = new ChatGptLayout();
        var previousDirectory = Environment.CurrentDirectory;

        try
        {
            Environment.CurrentDirectory = layout.Repository;

            var result = RunArguments("init", "--provider=chatgpt");

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("init requires --repo", result.Error, StringComparison.Ordinal);
        }
        finally
        {
            Environment.CurrentDirectory = previousDirectory;
        }
    }

    [Fact]
    public void ProviderModeRejectsCodexHomeWithoutTouchingRuntimeOrRepository()
    {
        using var layout = new ChatGptLayout();
        var runtimeBefore = File.ReadAllBytes(layout.Runtime);
        var canonicalBefore = File.ReadAllBytes(layout.CanonicalAgents);

        var result = Run(layout.Repository, "--codex-home", layout.CodexHome);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(
            "--codex-home is not valid with --provider=chatgpt",
            result.Error,
            StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.GetDirectoryName(layout.CustomInstructions)!));
        Assert.Equal(runtimeBefore, File.ReadAllBytes(layout.Runtime));
        Assert.Equal(canonicalBefore, File.ReadAllBytes(layout.CanonicalAgents));
    }

    [Fact]
    public void ProviderModeIgnoresCodexHomeEnvironment()
    {
        using var layout = new ChatGptLayout();
        var previous = Environment.GetEnvironmentVariable("CODEX_HOME");

        try
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", "relative-path-that-must-not-be-read");

            var result = Run(layout.Repository);

            Assert.Equal(0, result.ExitCode);
            Assert.True(File.Exists(layout.CustomInstructions));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", previous);
        }
    }

    [Fact]
    public void ProviderModeDirectsMovedRepositoriesToOrdinaryInitBeforeMutation()
    {
        using var layout = new ChatGptLayout();
        var movedRepository = Path.Combine(layout.Root, "moved repository");
        var canonicalBefore = File.ReadAllBytes(layout.CanonicalAgents);
        var runtimeBefore = File.ReadAllBytes(layout.Runtime);
        Directory.Move(layout.Repository, movedRepository);

        var result = Run(movedRepository);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(layout.Repository, result.Error, StringComparison.Ordinal);
        Assert.Contains(movedRepository, result.Error, StringComparison.Ordinal);
        Assert.Contains("ordinary `aec init`", result.Error, StringComparison.Ordinal);
        Assert.Equal(
            canonicalBefore,
            File.ReadAllBytes(Path.Combine(movedRepository, AecApplication.SourceRelativePath)));
        Assert.Equal(runtimeBefore, File.ReadAllBytes(layout.Runtime));
        Assert.False(Directory.Exists(Path.Combine(
            movedRepository,
            "environment",
            "providers",
            "chatgpt")));
    }

    [Theory]
    [InlineData("--provider=other", "Unsupported provider")]
    [InlineData("--provider", "Unknown argument")]
    public void RejectsUnsupportedProviderSyntax(string providerArgument, string expectedError)
    {
        using var layout = new ChatGptLayout();

        var result = RunArguments(
            "init",
            "--repo",
            layout.Repository,
            providerArgument);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(expectedError, result.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.GetDirectoryName(layout.CustomInstructions)!));
    }

    [Fact]
    public void RejectsDuplicateProvider()
    {
        using var layout = new ChatGptLayout();

        var result = Run(layout.Repository, "--provider=chatgpt");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(
            "--provider may be specified only once",
            result.Error,
            StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.GetDirectoryName(layout.CustomInstructions)!));
    }

    [Fact]
    public void PreservesExistingBackupsAndDoesNotRewriteAnInitializedProvider()
    {
        using var layout = new ChatGptLayout();
        Directory.CreateDirectory(Path.GetDirectoryName(layout.CustomInstructions)!);
        File.WriteAllText(layout.CustomInstructions, "My copied custom instructions.\n");

        var first = Run(layout.Repository);

        Assert.Equal(0, first.ExitCode);
        Assert.Equal("My copied custom instructions.\n", File.ReadAllText(layout.CustomInstructions));
        var timestamp = new DateTime(2020, 4, 5, 6, 7, 8, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(layout.CustomInstructions, timestamp);
        File.SetLastWriteTimeUtc(layout.ProjectBaseline, timestamp);
        File.SetLastWriteTimeUtc(layout.GptBaseline, timestamp);
        File.SetLastWriteTimeUtc(layout.CanonicalAgents, timestamp);
        var canonicalBefore = File.ReadAllBytes(layout.CanonicalAgents);

        var second = Run(layout.Repository);

        Assert.Equal(0, second.ExitCode);
        Assert.Equal($"unchanged{Environment.NewLine}", second.Output);
        Assert.Empty(second.Error);
        Assert.Equal("My copied custom instructions.\n", File.ReadAllText(layout.CustomInstructions));
        Assert.Equal(canonicalBefore, File.ReadAllBytes(layout.CanonicalAgents));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(layout.CustomInstructions));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(layout.ProjectBaseline));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(layout.GptBaseline));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(layout.CanonicalAgents));
    }

    [Fact]
    public void UpgradesVersionTwoCanonicalBlockAndPreservesSurroundingBytes()
    {
        using var layout = new ChatGptLayout();
        var canonical = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(
            "Prefix\r\n<!-- AEC:BEGIN version=2 -->\r\nOld body\r\n<!-- AEC:END -->\r\nSuffix\r\n"u8.ToArray())
            .ToArray();
        File.WriteAllBytes(layout.CanonicalAgents, canonical);
        var expected = AecInstructionBlock.MergeForChatGptProvider(
            canonical,
            layout.Repository);
        var runtimeBefore = File.ReadAllBytes(layout.Runtime);

        var result = Run(layout.Repository);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected, File.ReadAllBytes(layout.CanonicalAgents));
        Assert.True(expected.AsSpan().StartsWith(
            new byte[] { 0xEF, 0xBB, 0xBF }.Concat("Prefix\r\n"u8.ToArray()).ToArray()));
        Assert.True(expected.AsSpan().EndsWith("Suffix\r\n"u8));
        Assert.Equal(runtimeBefore, File.ReadAllBytes(layout.Runtime));
    }

    [Fact]
    public void CanonicalMergeErrorsDoNotClaimTheRuntimeWasRead()
    {
        using var layout = new ChatGptLayout();
        File.WriteAllText(
            layout.CanonicalAgents,
            "<!-- AEC:BEGIN version=1 -->\nmissing end\n");

        var result = Run(layout.Repository);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Instructions contain", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("Runtime instructions", result.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.GetDirectoryName(layout.CustomInstructions)!));
    }

    [Fact]
    public void FutureCanonicalBlockFailsBeforeScaffolding()
    {
        using var layout = new ChatGptLayout();
        var future = """
            <!-- AEC:BEGIN version=5 -->
            Future body
            <!-- AEC:END -->
            """u8.ToArray();
        File.WriteAllBytes(layout.CanonicalAgents, future);
        var runtimeBefore = File.ReadAllBytes(layout.Runtime);

        var result = Run(layout.Repository);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("newer unsupported", result.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.GetDirectoryName(layout.CustomInstructions)!));
        Assert.Equal(future, File.ReadAllBytes(layout.CanonicalAgents));
        Assert.Equal(runtimeBefore, File.ReadAllBytes(layout.Runtime));
    }

    [Fact]
    public void UnsafeScaffoldTargetFailsBeforeAnyWrite()
    {
        using var layout = new ChatGptLayout();
        var providerDirectory = Path.GetDirectoryName(layout.CustomInstructions)!;
        Directory.CreateDirectory(layout.GptBaseline);
        var canonicalBefore = File.ReadAllBytes(layout.CanonicalAgents);

        var result = Run(layout.Repository);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("regular file", result.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(layout.CustomInstructions));
        Assert.False(File.Exists(layout.ProjectBaseline));
        Assert.True(Directory.Exists(layout.GptBaseline));
        Assert.Equal(canonicalBefore, File.ReadAllBytes(layout.CanonicalAgents));
        Assert.Single(Directory.GetFileSystemEntries(providerDirectory));
    }

    [Fact]
    public void SymbolicLinkProviderDirectoryDoesNotRedirectScaffolding()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var layout = new ChatGptLayout();
        var providerDirectory = Path.GetDirectoryName(layout.CustomInstructions)!;
        var referent = Path.Combine(layout.Root, "external-provider");
        Directory.CreateDirectory(referent);
        Directory.CreateSymbolicLink(providerDirectory, referent);

        try
        {
            var result = Run(layout.Repository);

            Assert.Equal(1, result.ExitCode);
            Assert.Contains("symbolic link", result.Error, StringComparison.Ordinal);
            Assert.Empty(Directory.GetFileSystemEntries(referent));
        }
        finally
        {
            Directory.Delete(providerDirectory);
        }
    }

    [Fact]
    public void SymbolicLinkScaffoldFailsWithoutChangingItsReferent()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var layout = new ChatGptLayout();
        Directory.CreateDirectory(Path.GetDirectoryName(layout.CustomInstructions)!);
        var referent = Path.Combine(layout.Root, "external.md");
        File.WriteAllText(referent, "External content.\n");
        File.CreateSymbolicLink(layout.CustomInstructions, referent);

        var result = Run(layout.Repository);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("symbolic link", result.Error, StringComparison.Ordinal);
        Assert.Equal("External content.\n", File.ReadAllText(referent));
        Assert.False(File.Exists(layout.ProjectBaseline));
        Assert.False(File.Exists(layout.GptBaseline));
    }

    [Fact]
    public void MissingCanonicalSourceFailsBeforeScaffolding()
    {
        using var layout = new ChatGptLayout();
        File.Delete(layout.CanonicalAgents);

        var result = Run(layout.Repository);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Canonical source does not exist", result.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.GetDirectoryName(layout.CustomInstructions)!));
    }

    [Fact]
    public void NestedRepositoryDirectoryIsRejected()
    {
        using var layout = new ChatGptLayout();
        var nested = Path.Combine(layout.Repository, "nested");
        var nestedCanonical = Path.Combine(
            nested,
            "environment",
            "providers",
            "codex",
            "AGENTS.md");
        Directory.CreateDirectory(Path.GetDirectoryName(nestedCanonical)!);
        File.Copy(layout.CanonicalAgents, nestedCanonical);

        var result = Run(nested);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Git repository root", result.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(
            nested,
            "environment",
            "providers",
            "chatgpt")));
    }

    [Fact]
    public void NonWorkTreeRepositoryIsRejected()
    {
        using var layout = new ChatGptLayout();
        var bare = Path.Combine(layout.Root, "bare.git");
        Assert.Equal(0, GitProcess.Run(null, "init", "--bare", "--quiet", bare).ExitCode);
        var canonical = Path.Combine(
            bare,
            "environment",
            "providers",
            "codex",
            "AGENTS.md");
        Directory.CreateDirectory(Path.GetDirectoryName(canonical)!);
        File.Copy(layout.CanonicalAgents, canonical);

        var result = Run(bare);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Git work tree", result.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.Combine(
            bare,
            "environment",
            "providers",
            "chatgpt")));
    }

    [Fact]
    public void PreservesUnrelatedChangesAndGitState()
    {
        using var layout = new ChatGptLayout();
        ConfigureIdentity(layout.Repository);
        var tracked = Path.Combine(layout.Repository, "tracked.txt");
        File.WriteAllText(tracked, "committed\n");
        Assert.Equal(
            0,
            GitProcess.Run(
                layout.Repository,
                "add",
                "--",
                AecApplication.SourceRelativePath,
                "tracked.txt").ExitCode);
        Assert.Equal(
            0,
            GitProcess.Run(
                layout.Repository,
                "commit",
                "--quiet",
                "--message",
                "Initial test state").ExitCode);

        File.WriteAllText(tracked, "unstaged\n");
        var staged = Path.Combine(layout.Repository, "staged.txt");
        File.WriteAllText(staged, "staged\n");
        Assert.Equal(0, GitProcess.Run(layout.Repository, "add", "--", "staged.txt").ExitCode);
        var untracked = Path.Combine(layout.Repository, "untracked.txt");
        File.WriteAllText(untracked, "untracked\n");
        var headBefore = GitProcess.Run(layout.Repository, "rev-parse", "HEAD").Output.Trim();
        var indexBefore = GitProcess.Run(layout.Repository, "diff", "--cached", "--binary").Output;

        var result = Run(layout.Repository);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(headBefore, GitProcess.Run(layout.Repository, "rev-parse", "HEAD").Output.Trim());
        Assert.Equal(indexBefore, GitProcess.Run(layout.Repository, "diff", "--cached", "--binary").Output);
        Assert.Equal("unstaged\n", File.ReadAllText(tracked));
        Assert.Equal("staged\n", File.ReadAllText(staged));
        Assert.Equal("untracked\n", File.ReadAllText(untracked));
    }

    [Fact]
    public void HelpListsTheExactProviderForm()
    {
        var result = RunArguments("help");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        Assert.Contains(
            "aec init --repo ABSOLUTE_PATH [--codex-home ABSOLUTE_PATH] [--force-path-change]",
            result.Output,
            StringComparison.Ordinal);
        Assert.Contains(
            "aec init --repo ABSOLUTE_PATH --provider=chatgpt",
            result.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void NoCommandSuggestsTheDocumentedHelpCommand()
    {
        var result = RunArguments();

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("Use `aec help` for usage.", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateOnlyWriteDoesNotOverwriteAConcurrentBackup()
    {
        using var layout = new ChatGptLayout();
        Directory.CreateDirectory(Path.GetDirectoryName(layout.CustomInstructions)!);
        File.WriteAllText(layout.CustomInstructions, "Manual backup.\n");

        Assert.Throws<IOException>(() => AtomicFile.WriteNew(layout.CustomInstructions, []));
        Assert.Equal("Manual backup.\n", File.ReadAllText(layout.CustomInstructions));
    }

    [Fact]
    public void CanonicalReplacementDiagnosticsDoNotClaimTheRuntimeWasRead()
    {
        using var layout = new ChatGptLayout();
        var staleSnapshot = File.ReadAllBytes(layout.CanonicalAgents);
        File.WriteAllText(layout.CanonicalAgents, "Concurrent canonical edit.\n");

        var exception = Assert.Throws<IOException>(() =>
            AtomicFile.ReplaceIfUnchanged(
                layout.CanonicalAgents,
                staleSnapshot,
                "Replacement.\n"u8.ToArray(),
                "Canonical source"));

        Assert.Contains("Canonical source changed", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Runtime target", exception.Message, StringComparison.Ordinal);
        Assert.Equal("Concurrent canonical edit.\n", File.ReadAllText(layout.CanonicalAgents));
    }

    private static void ConfigureIdentity(string repository)
    {
        Assert.Equal(0, GitProcess.Run(repository, "config", "user.name", "AEC Tests").ExitCode);
        Assert.Equal(
            0,
            GitProcess.Run(repository, "config", "user.email", "aec-tests@example.invalid").ExitCode);
        Assert.Equal(0, GitProcess.Run(repository, "config", "commit.gpgSign", "false").ExitCode);
    }

    private static CommandResult Run(string repository, params string[] extraArguments)
    {
        var arguments = new[]
        {
            "init",
            "--repo",
            repository,
            "--provider=chatgpt"
        }.Concat(extraArguments).ToArray();
        return RunArguments(arguments);
    }

    private static CommandResult RunArguments(params string[] arguments)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = AecApplication.Run(arguments, output, error);
        return new CommandResult(exitCode, output.ToString(), error.ToString());
    }

    private sealed record CommandResult(int ExitCode, string Output, string Error);

    private sealed class ChatGptLayout : IDisposable
    {
        public ChatGptLayout()
        {
            Root = Path.Combine(
                OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(),
                "aec-chatgpt-init-tests",
                Guid.NewGuid().ToString("N"));
            Repository = Path.Combine(Root, "repository");
            CodexHome = Path.Combine(Root, "codex-home");
            Runtime = Path.Combine(CodexHome, "AGENTS.md");
            CanonicalAgents = Path.Combine(
                Repository,
                "environment",
                "providers",
                "codex",
                "AGENTS.md");
            CustomInstructions = Path.Combine(
                Repository,
                "environment",
                "providers",
                "chatgpt",
                "custom-instructions.md");
            ProjectBaseline = Path.Combine(
                Repository,
                "environment",
                "providers",
                "chatgpt",
                "project-baseline.md");
            GptBaseline = Path.Combine(
                Repository,
                "environment",
                "providers",
                "chatgpt",
                "gpt-baseline.md");

            Directory.CreateDirectory(CodexHome);
            File.WriteAllText(Runtime, "Existing runtime instruction.\n");

            var output = new StringWriter();
            var error = new StringWriter();
            var exitCode = AecApplication.Run(
                ["init", "--repo", Repository, "--codex-home", CodexHome],
                output,
                error);
            if (exitCode != 0)
            {
                throw new InvalidOperationException(error.ToString());
            }
        }

        public string Root { get; }

        public string Repository { get; }

        public string CodexHome { get; }

        public string Runtime { get; }

        public string CanonicalAgents { get; }

        public string CustomInstructions { get; }

        public string ProjectBaseline { get; }

        public string GptBaseline { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }
}
