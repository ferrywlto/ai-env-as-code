using System.Text;

namespace Aec.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessStateCollection : ICollectionFixture<GitTestEnvironment>
{
    public const string Name = "Process state";
}

public sealed class GitTestEnvironment : IDisposable
{
    private readonly string? previousGlobalConfig;
    private readonly string root;

    public GitTestEnvironment()
    {
        root = Path.Combine(Path.GetTempPath(), "aec-git-test-config", Guid.NewGuid().ToString("N"));
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

[Collection(ProcessStateCollection.Name)]
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

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(StatusOutput("in_sync", "in_sync"), result.Output);
        Assert.Empty(result.Error);
    }

    [Fact]
    public void ReportsDifferentForDifferentFiles()
    {
        using var layout = new TemporaryLayout("desired\n", "current\n");

        var result = Run(layout);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(StatusOutput("different", "in_sync"), result.Output);
        Assert.Empty(result.Error);
    }

    [Fact]
    public void ComparesExactBytesWithoutNormalizingLineEndings()
    {
        using var layout = new TemporaryLayout("same\n", "same\r\n");

        var result = Run(layout);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(StatusOutput("different", "in_sync"), result.Output);
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
        Assert.Contains("Canonical source does not exist", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsRelativeRepositoryPaths()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = AecApplication.Run(
            ["status", "--repo", "relative/path", "--codex-home", Path.GetTempPath()],
            output,
            error);

        Assert.Equal(1, exitCode);
        Assert.Empty(output.ToString());
        Assert.Contains("--repo must be an absolute path", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitCodexHomeOverridesTheEnvironment()
    {
        using var layout = new TemporaryLayout("same\n", "same\n");
        using var other = new TemporaryLayout("unused\n", "different\n");
        var previous = Environment.GetEnvironmentVariable("CODEX_HOME");

        try
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", other.CodexHome);
            var result = Run(layout);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(StatusOutput("in_sync", "in_sync"), result.Output);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", previous);
        }
    }

    [Fact]
    public void UsesCodexHomeFromTheEnvironmentWhenTheFlagIsAbsent()
    {
        using var layout = new TemporaryLayout("same\n", "same\n");
        var previous = Environment.GetEnvironmentVariable("CODEX_HOME");

        try
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", layout.CodexHome);
            var output = new StringWriter();
            var error = new StringWriter();

            var exitCode = AecApplication.Run(
                ["status", "--repo", layout.Repository],
                output,
                error);

            Assert.Equal(0, exitCode);
            Assert.Equal(StatusOutput("in_sync", "in_sync"), output.ToString());
            Assert.Empty(error.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", previous);
        }
    }

    [Fact]
    public void AbsoluteRootsAreIndependentOfTheWorkingDirectory()
    {
        using var layout = new TemporaryLayout("same\n", "same\n");
        var unrelatedDirectory = Path.Combine(layout.Root, "unrelated");
        Directory.CreateDirectory(unrelatedDirectory);
        var previous = Environment.CurrentDirectory;

        try
        {
            Environment.CurrentDirectory = unrelatedDirectory;
            var result = Run(layout);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal(StatusOutput("in_sync", "in_sync"), result.Output);
        }
        finally
        {
            Environment.CurrentDirectory = previous;
        }
    }

    [Fact]
    public void MissingCodexHomeIsAnError()
    {
        using var layout = new TemporaryLayout("desired\n", null);
        Directory.Delete(layout.CodexHome, recursive: true);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("Codex home does not exist", result.Error, StringComparison.Ordinal);
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
    public void RejectsDuplicateRepositoryArguments()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = AecApplication.Run(
            ["status", "--repo", Path.GetTempPath(), "--repo", Path.GetTempPath()],
            output,
            error);

        Assert.Equal(1, exitCode);
        Assert.Empty(output.ToString());
        Assert.Contains("--repo may be specified only once", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAnEmptyExplicitCodexHome()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = AecApplication.Run(
            ["status", "--repo", Path.GetTempPath(), "--codex-home", ""],
            output,
            error);

        Assert.Equal(1, exitCode);
        Assert.Empty(output.ToString());
        Assert.Contains("--codex-home requires a non-empty path", error.ToString(), StringComparison.Ordinal);
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
    [InlineData("none")]
    [InlineData("friendly")]
    [InlineData("pragmatic")]
    public void AcceptsOfficiallySupportedPersonalityValues(string personality)
    {
        using var layout = new TemporaryLayout(
            "same\n",
            "same\n",
            $"personality = \"{personality}\"\n",
            $"personality = \"{personality}\"\n");

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(StatusOutput("in_sync", "in_sync"), result.Output);
        Assert.Empty(result.Error);
    }

    [Fact]
    public void ComparesOnlyTheManagedRuntimeValue()
    {
        using var layout = new TemporaryLayout(
            "same\n",
            "same\n",
            DefaultCanonicalConfig,
            "# Machine-owned settings remain outside AEC ownership.\r\n" +
            "model = \"another-model\"\r\n" +
            "personality    =    \"none\" # same managed value; comments may contain \"\"\"\r\n" +
            "[features]\r\n" +
            "example = false\r\n");

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(StatusOutput("in_sync", "in_sync"), result.Output);
        Assert.Empty(result.Error);
    }

    [Fact]
    public void ReportsDifferentForAnotherSupportedRuntimeValue()
    {
        using var layout = new TemporaryLayout(
            "same\n",
            "same\n",
            DefaultCanonicalConfig,
            "personality = \"friendly\"\n");

        var result = Run(layout);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(StatusOutput("in_sync", "different"), result.Output);
        Assert.Empty(result.Error);
    }

    [Fact]
    public void ReportsMissingWhenRuntimeConfigIsAbsent()
    {
        using var layout = new TemporaryLayout(
            "same\n",
            "same\n",
            DefaultCanonicalConfig,
            null);

        var result = Run(layout);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(StatusOutput("in_sync", "missing"), result.Output);
        Assert.Empty(result.Error);
    }

    [Fact]
    public void ReportsMissingWhenManagedRuntimeValueIsAbsent()
    {
        using var layout = new TemporaryLayout(
            "same\n",
            "same\n",
            DefaultCanonicalConfig,
            "model = \"local-model\"\n[features]\nexample = true\n");

        var result = Run(layout);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(StatusOutput("in_sync", "missing"), result.Output);
        Assert.Empty(result.Error);
    }

    [Fact]
    public void IgnoresASettingWithTheSameNameInsideATable()
    {
        using var layout = new TemporaryLayout(
            "same\n",
            "same\n",
            DefaultCanonicalConfig,
            "[features]\npersonality = \"none\"\n");

        var result = Run(layout);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal(StatusOutput("in_sync", "missing"), result.Output);
        Assert.Empty(result.Error);
    }

    [Fact]
    public void FailsWhenCanonicalConfigIsAbsent()
    {
        using var layout = new TemporaryLayout("same\n", "same\n", null, DefaultRuntimeConfig);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("Canonical config does not exist", result.Error, StringComparison.Ordinal);
        Assert.Contains("supported root `personality` value", result.Error, StringComparison.Ordinal);
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
    public void FailsWhenCanonicalConfigDoesNotDeclarePersonality()
    {
        using var layout = new TemporaryLayout("same\n", "same\n", "# no managed value\n", DefaultRuntimeConfig);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("does not declare personality", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void FailsWhenCanonicalConfigDeclaresPersonalityTwice()
    {
        using var layout = new TemporaryLayout(
            "same\n",
            "same\n",
            "personality = \"none\"\npersonality = \"friendly\"\n",
            DefaultRuntimeConfig);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("Canonical config declares personality more than once", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void FailsWhenCanonicalConfigUsesAnUnsupportedPersonality()
    {
        using var layout = new TemporaryLayout(
            "same\n",
            "same\n",
            "personality = \"playful\"\n",
            DefaultRuntimeConfig);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("Canonical config has unsupported personality", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void FailsWhenRuntimeConfigDeclaresPersonalityTwice()
    {
        using var layout = new TemporaryLayout(
            "same\n",
            "same\n",
            DefaultCanonicalConfig,
            "personality = \"none\"\npersonality = \"friendly\"\n");

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("Runtime config declares personality more than once", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void FailsWhenRuntimeConfigUsesAnUnsupportedPersonality()
    {
        using var layout = new TemporaryLayout(
            "same\n",
            "same\n",
            DefaultCanonicalConfig,
            "personality = \"playful\"\n");

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("Runtime config has unsupported personality", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void AcceptsAQuotedManagedKey()
    {
        using var layout = new TemporaryLayout(
            "same\n",
            "same\n",
            DefaultCanonicalConfig,
            "\"personality\" = \"none\"\n");

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(StatusOutput("in_sync", "in_sync"), result.Output);
        Assert.Empty(result.Error);
    }

    [Fact]
    public void IgnoresManagedTextInsideAnUnrelatedMultilineString()
    {
        using var layout = new TemporaryLayout(
            "same\n",
            "same\n",
            DefaultCanonicalConfig,
            "instructions = \"\"\"\npersonality = \"friendly\"\n\"\"\"\n" +
            "personality = \"none\"\n");

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(StatusOutput("in_sync", "in_sync"), result.Output);
        Assert.Empty(result.Error);
    }

    [Fact]
    public void ReadsPersonalityAfterAnUnrelatedNestedMultilineArray()
    {
        using var layout = new TemporaryLayout(
            "same\n",
            "same\n",
            DefaultCanonicalConfig,
            "matrix = [\n  [1, 2],\n  [3, 4],\n]\n" +
            "personality = \"none\"\n");

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(StatusOutput("in_sync", "in_sync"), result.Output);
        Assert.Empty(result.Error);
    }

    [Fact]
    public void AcceptsAnEscapedManagedKey()
    {
        using var layout = new TemporaryLayout(
            "same\n",
            "same\n",
            DefaultCanonicalConfig,
            "\"personal\\u0069ty\" = \"none\"\n");

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(StatusOutput("in_sync", "in_sync"), result.Output);
        Assert.Empty(result.Error);
    }

    [Fact]
    public void ComparesTheDecodedManagedValue()
    {
        using var layout = new TemporaryLayout(
            "same\n",
            "same\n",
            "personality = \"friendly\"\n",
            "personality = \"frien\\u0064ly\"\n");

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(StatusOutput("in_sync", "in_sync"), result.Output);
        Assert.Empty(result.Error);
    }

    [Fact]
    public void FailsClosedForAMultilineManagedValue()
    {
        using var layout = new TemporaryLayout(
            "same\n",
            "same\n",
            DefaultCanonicalConfig,
            "personality = \"\"\"none\"\"\"\n");

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("ambiguous personality declaration", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void IgnoresAnUnrelatedLiteralKeyContainingABackslash()
    {
        using var layout = new TemporaryLayout(
            "same\n",
            "same\n",
            DefaultCanonicalConfig,
            "'machine\\path' = \"local\"\npersonality = \"none\"\n");

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(StatusOutput("in_sync", "in_sync"), result.Output);
        Assert.Empty(result.Error);
    }

    [Fact]
    public void RejectsNonTomlWhitespaceInCanonicalConfig()
    {
        using var layout = new TemporaryLayout(
            "same\n",
            "same\n",
            "personality\u00A0=\u00A0\"none\"\n",
            DefaultRuntimeConfig);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("ambiguous root key", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsForbiddenControlCharactersInCanonicalComments()
    {
        using var layout = new TemporaryLayout(
            "same\n",
            "same\n",
            "# invalid\0comment\npersonality = \"none\"\n",
            DefaultRuntimeConfig);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("forbidden control character", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void FailsWhenCanonicalConfigIsNotValidUtf8()
    {
        using var layout = new TemporaryLayout("same\n", "same\n");
        File.WriteAllBytes(layout.CanonicalConfig, [0xFF]);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("Canonical config is not valid UTF-8", result.Error, StringComparison.Ordinal);
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

    [Fact]
    public void StatusDoesNotChangeSourceOrTargetContent()
    {
        using var layout = new TemporaryLayout("desired\n", "current\n");
        var sourceBefore = File.ReadAllBytes(layout.Source);
        var targetBefore = File.ReadAllBytes(layout.Target);
        var canonicalConfigBefore = File.ReadAllBytes(layout.CanonicalConfig);
        var runtimeConfigBefore = File.ReadAllBytes(layout.RuntimeConfig);
        var filesBefore = Directory.GetFiles(layout.Root, "*", SearchOption.AllDirectories);

        _ = Run(layout);

        Assert.Equal(sourceBefore, File.ReadAllBytes(layout.Source));
        Assert.Equal(targetBefore, File.ReadAllBytes(layout.Target));
        Assert.Equal(canonicalConfigBefore, File.ReadAllBytes(layout.CanonicalConfig));
        Assert.Equal(runtimeConfigBefore, File.ReadAllBytes(layout.RuntimeConfig));
        Assert.Equal(filesBefore, Directory.GetFiles(layout.Root, "*", SearchOption.AllDirectories));
    }

    private static string StatusOutput(string agentsStatus, string configStatus)
    {
        return
            $"codex/AGENTS.md   {agentsStatus}{Environment.NewLine}" +
            $"codex/config.toml {configStatus}{Environment.NewLine}";
    }

    private static CommandResult Run(TemporaryLayout layout)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = AecApplication.Run(
            ["status", "--repo", layout.Repository, "--codex-home", layout.CodexHome],
            output,
            error);

        return new CommandResult(exitCode, output.ToString(), error.ToString());
    }

    private sealed record CommandResult(int ExitCode, string Output, string Error);

    private sealed class TemporaryLayout : IDisposable
    {
        public TemporaryLayout(
            string? source,
            string? target,
            string? canonicalConfig = DefaultCanonicalConfig,
            string? runtimeConfig = DefaultRuntimeConfig)
        {
            Root = Path.Combine(Path.GetTempPath(), "aec-tests", Guid.NewGuid().ToString("N"));
            Repository = Path.Combine(Root, "data");
            CodexHome = Path.Combine(Root, "codex-home");
            Source = Path.Combine(Repository, "environment", "providers", "codex", "AGENTS.md");
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

            if (source is not null)
            {
                File.WriteAllText(Source, source, new UTF8Encoding(false));
            }

            if (target is not null)
            {
                File.WriteAllText(Target, target, new UTF8Encoding(false));
            }

            if (canonicalConfig is not null)
            {
                File.WriteAllText(CanonicalConfig, canonicalConfig, new UTF8Encoding(false));
            }

            if (runtimeConfig is not null)
            {
                File.WriteAllText(RuntimeConfig, runtimeConfig, new UTF8Encoding(false));
            }
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
