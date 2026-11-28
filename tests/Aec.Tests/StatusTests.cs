using System.Text;

namespace Aec.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessStateTestGroup : ICollectionFixture<GitTestEnvironment>
{
    public const string Name = "Process state";
}

public sealed class GitTestEnvironment : IDisposable
{
    private readonly string? previousGlobalConfig;
    private readonly string root;

    public GitTestEnvironment()
    {
        root = Path.Combine(TestTemporaryDirectory.Root, "aec-git-test-config", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var config = Path.Combine(root, "config");
        File.WriteAllText(
            config,
            """
            [user]
                name = AEC Tests
                email = aec-tests@example.invalid
            [commit]
                gpgSign = false
            """);

        previousGlobalConfig = Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL");
        Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", config);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", previousGlobalConfig);
        Directory.Delete(root, recursive: true);
    }
}

[Collection(ProcessStateTestGroup.Name)]
public sealed class StatusTests
{
    private const string DefaultCanonicalConfig = "personality = \"none\"\n";
    private const string DefaultRuntimeConfig =
        "model = \"local-model\"\n" +
        "personality = \"none\"\n" +
        "\n" +
        "[features]\n" +
        "example = true\n";

    [Fact]
    public void ReportsInSyncForEqualFiles()
    {
        using var layout = new TemporaryLayout("same\n", "same\n");
        var previous = Environment.GetEnvironmentVariable("CODEX_HOME");
        CommandResult result;

        try
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", layout.CodexHome);
            result = TestApplication.Run("codex", "status", "--repo", layout.Repository);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", previous);
        }

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(StatusOutput("in_sync", "in_sync"), result.Output);
        Assert.Empty(result.Error);
    }

    [Fact]
    public void ComparesExactBytesWithoutNormalizingLineEndings()
    {
        using var layout = new TemporaryLayout("same\n", "same\r\n");
        var paths = new[] { layout.Source, layout.Target, layout.CanonicalConfig, layout.RuntimeConfig };
        var original = paths.ToDictionary(path => path, File.ReadAllBytes);
        var files = Directory.GetFiles(layout.Root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();

        var result = Run(layout);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(StatusOutput("different", "in_sync"), result.Output);
        Assert.Empty(result.Error);
        Assert.Equal(files, Directory.GetFiles(layout.Root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
        foreach (var (path, bytes) in original)
        {
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }
    }

    [Fact]
    public void ReportsMissingWhenTheRuntimeTargetIsAbsent()
    {
        using var layout = new TemporaryLayout("desired\n", null);

        var result = Run(layout);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(StatusOutput("missing", "in_sync"), result.Output);
        Assert.Empty(result.Error);
    }

    [Fact]
    public void FailsWhenTheCanonicalSourceIsAbsent()
    {
        using var layout = new TemporaryLayout(null, null);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("Authored shared instructions does not exist", result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitRootsOverrideTheWorkingDirectoryAndHomeEnvironment()
    {
        using var layout = new TemporaryLayout("same\n", "same\n");
        var unrelatedDirectory = Path.Combine(layout.Root, "unrelated");
        Directory.CreateDirectory(unrelatedDirectory);
        var previousDirectory = Environment.CurrentDirectory;
        var previousHome = Environment.GetEnvironmentVariable("CODEX_HOME");

        try
        {
            Environment.CurrentDirectory = unrelatedDirectory;
            Environment.SetEnvironmentVariable("CODEX_HOME", Path.Combine(layout.Root, "missing-home"));
            var result = Run(layout);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(StatusOutput("in_sync", "in_sync"), result.Output);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", previousHome);
            Environment.CurrentDirectory = previousDirectory;
        }
    }

    [Fact]
    public void RejectsSourceFilesOverOneMiB()
    {
        using var layout = new TemporaryLayout(null, null);
        File.WriteAllBytes(layout.Source, new byte[(1024 * 1024) + 1]);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("exceeds 1 MiB", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsSymbolicLinkSources()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var layout = new TemporaryLayout(null, null);
        var externalSource = Path.Combine(layout.Root, "external-agents.md");
        File.WriteAllText(externalSource, "content\n");
        File.CreateSymbolicLink(layout.Source, externalSource);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("must not be a symbolic link", result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("missing-home", "Provider home does not exist")]
    [InlineData("repository-link", "must not contain a symbolic link")]
    [InlineData("home-link", "must not contain a symbolic link")]
    [InlineData("home-inside-repository", "outside the data repository")]
    public void RejectsUnsafePathsWithoutChangingFiles(string boundary, string expectedError)
    {
        var usesLink = boundary is "repository-link" or "home-link";
        if (usesLink && OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Path.Combine(TestTemporaryDirectory.Root, Guid.NewGuid().ToString("N"));
        var repository = Path.Combine(root, "data");
        var codexHome = boundary == "home-inside-repository" ? repository : Path.Combine(root, "codex-home");
        var alias = Path.Combine(root, "path-alias");
        Directory.CreateDirectory(repository);
        try
        {
            if (boundary != "missing-home")
            {
                Directory.CreateDirectory(codexHome);
            }

            var sentinel = Path.Combine(repository, "AGENTS.md");
            File.WriteAllText(sentinel, "Preserve instructions.\n");
            if (boundary == "repository-link")
            {
                Directory.CreateSymbolicLink(alias, repository);
                repository = alias;
            }
            else if (boundary == "home-link")
            {
                Directory.CreateSymbolicLink(alias, root);
                codexHome = Path.Combine(alias, "codex-home");
            }

            var result = Run(repository, codexHome);

            Assert.Equal(1, result.ExitCode);
            Assert.Empty(result.Output);
            Assert.Contains(expectedError, result.Error, StringComparison.Ordinal);
            Assert.Equal("Preserve instructions.\n", File.ReadAllText(sentinel));
        }
        finally
        {
            if (Directory.Exists(alias))
            {
                Directory.Delete(alias);
            }

            TestDirectoryCleanup.Delete(root);
        }
    }

    [Fact]
    public void FailsWhenCanonicalConfigIsAbsent()
    {
        using var layout = new TemporaryLayout("same\n", "same\n", null, DefaultRuntimeConfig);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("Canonical config does not exist", result.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("aec init --repo", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void FailsWhenCanonicalConfigContainsUnmanagedSettings()
    {
        using var layout = new TemporaryLayout(
            "same\n",
            "same\n",
            "model = \"local-model\"\npersonality = \"none\"\n",
            DefaultRuntimeConfig);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("Canonical config contains an unmanaged setting", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsCanonicalConfigFilesOverOneMiB()
    {
        using var layout = new TemporaryLayout("same\n", "same\n");
        File.WriteAllBytes(layout.CanonicalConfig, new byte[(1024 * 1024) + 1]);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("Canonical config exceeds 1 MiB", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsSymbolicLinkCanonicalConfig()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var layout = new TemporaryLayout("same\n", "same\n");
        var externalConfig = Path.Combine(layout.Root, "external-config.toml");
        File.WriteAllText(externalConfig, DefaultCanonicalConfig);
        File.Delete(layout.CanonicalConfig);
        File.CreateSymbolicLink(layout.CanonicalConfig, externalConfig);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("Canonical config must not be a symbolic link", result.Error, StringComparison.Ordinal);
    }

    private static string StatusOutput(string agentsStatus, string configStatus)
    {
        return
            $"codex/AGENTS.md   {agentsStatus}{Environment.NewLine}" +
            $"codex/config.toml {configStatus}{Environment.NewLine}";
    }

    private static CommandResult Run(TemporaryLayout layout)
    {
        return Run(layout.Repository, layout.CodexHome);
    }

    private static CommandResult Run(string repository, string codexHome)
    {
        return TestApplication.Run(
            "codex", "status",
            "--repo",
            repository,
            "--home",
            codexHome);
    }

    private sealed class TemporaryLayout : IDisposable
    {
        public TemporaryLayout(
            string? source,
            string? target,
            string? canonicalConfig = DefaultCanonicalConfig,
            string? runtimeConfig = DefaultRuntimeConfig)
        {
            Root = Path.Combine(TestTemporaryDirectory.Root, "aec-tests", Guid.NewGuid().ToString("N"));
            Repository = Path.Combine(Root, "data");
            CodexHome = Path.Combine(Root, "codex-home");
            Source = Path.Combine(Repository, "environment", "shared", "instructions.md");
            Target = Path.Combine(CodexHome, "AGENTS.md");
            CanonicalConfig = Path.Combine(
                Repository,
                "environment",
                "providers",
                "codex",
                "config.toml");
            RuntimeConfig = Path.Combine(CodexHome, "config.toml");

            Directory.CreateDirectory(Path.GetDirectoryName(Source)!);
            Directory.CreateDirectory(CodexHome);
            var platform = RenderCommand.CurrentPlatform();
            var policy = Path.Combine(Repository, "environment", "platforms", platform,
                "policy.md");
            var overlay = Path.Combine(Repository, "environment", "providers", "codex",
                "overlay.md");
            var renderedTarget = Path.Combine(Repository, "environment", "targets", platform,
                "codex", "AGENTS.md");
            Directory.CreateDirectory(Path.GetDirectoryName(policy)!);
            Directory.CreateDirectory(Path.GetDirectoryName(overlay)!);
            Directory.CreateDirectory(Path.GetDirectoryName(renderedTarget)!);
            File.WriteAllText(policy, string.Empty);
            File.WriteAllText(overlay, string.Empty);

            if (source is not null)
            {
                File.WriteAllText(Source, source, new UTF8Encoding(false));
                File.WriteAllText(renderedTarget,
                    InstructionSections.Render(source, string.Empty, string.Empty),
                    new UTF8Encoding(false));
            }

            if (target is not null)
            {
                var renderedRuntime = InstructionSections.Render(
                    target, string.Empty, string.Empty);
                File.WriteAllBytes(Target, AecInstructionBlock.MergeForPortableTarget(
                    Encoding.UTF8.GetBytes(renderedRuntime), Repository, platform,
                    includeChatGptGuidance: false));
            }

            if (canonicalConfig is not null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CanonicalConfig)!);
                File.WriteAllText(CanonicalConfig, canonicalConfig, new UTF8Encoding(false));
            }

            if (runtimeConfig is not null)
            {
                File.WriteAllText(RuntimeConfig, runtimeConfig, new UTF8Encoding(false));
            }

            GitProcess.RunRequired(Repository, "Could not initialize Git",
                "init", "--quiet", "--template=", "--initial-branch=main");
            GitProcess.RunRequired(Repository, "Could not stage status fixture", "add", "--",
                "environment");
            GitProcess.RunRequired(Repository, "Could not commit status fixture",
                "commit", "-m", "Fixture baseline");
        }

        public string Root { get; }

        public string Repository { get; }

        public string CodexHome { get; }

        public string Source { get; }

        public string Target { get; }

        public string CanonicalConfig { get; }

        public string RuntimeConfig { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }
    }
}
