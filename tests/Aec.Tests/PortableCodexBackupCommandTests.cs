using System.Text;

namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class PortableCodexBackupCommandTests
{
    [Fact]
    public void UnchangedPortableEnvironmentDoesNotCommit()
    {
        using var layout = NewLayout();
        var head = layout.Head();
        var runtime = File.ReadAllBytes(layout.RuntimePath);
        var configPath = RuntimeConfigPath(layout);
        var config = File.ReadAllBytes(configPath);

        var result = RunWithCodexHomeEnvironment(layout, explicitHome: false);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"unchanged{Environment.NewLine}", result.Output);
        Assert.Empty(result.Error);
        Assert.Equal(head, layout.Head());
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(config, File.ReadAllBytes(configPath));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void ConfigOnlyDriftCommitsOnlyCanonicalConfig()
    {
        const string config = "personality = 'friendly' # runtime\n[other]\nkey = 1\n";
        using var layout = NewLayout(config);
        var configPath = RuntimeConfigPath(layout);
        var runtime = File.ReadAllBytes(layout.RuntimePath);
        var target = File.ReadAllBytes(layout.TargetPath);
        var head = layout.Head();

        var result = RunWithCodexHomeEnvironment(layout, explicitHome: true);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"committed {layout.Head()}{Environment.NewLine}", result.Output);
        Assert.Empty(result.Error);
        Assert.Equal(head, Parent(layout));
        Assert.Equal([AecApplication.ConfigSourceRelativePath], CommittedPaths(layout));
        Assert.Equal("Backup portable Codex environment", GitProcess.RunRequired(
            layout.Repository, "Could not inspect backup subject", "log", "-1", "--format=%s")
            .Output.Trim());
        Assert.Equal("personality = \"friendly\"\n",
            File.ReadAllText(layout.CanonicalConfigPath));
        Assert.Equal(target, File.ReadAllBytes(layout.TargetPath));
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(Encoding.UTF8.GetBytes(config), File.ReadAllBytes(configPath));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    [Trait("Category", "PortableReview")]
    public void InstructionAndPersonalityDriftProduceOneCommit()
    {
        using var layout = NewLayout();
        File.WriteAllText(layout.RuntimePath, File.ReadAllText(layout.RuntimePath)
            .Replace("Shared approval.", "Shared revision.", StringComparison.Ordinal));
        var configPath = RuntimeConfigPath(layout);
        File.WriteAllText(configPath, "personality = \"pragmatic\"\n");
        var runtime = File.ReadAllBytes(layout.RuntimePath);
        var runtimeConfig = File.ReadAllBytes(configPath);
        var head = layout.Head();

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"committed {layout.Head()}{Environment.NewLine}", result.Output);
        Assert.Empty(result.Error);
        Assert.Equal(head, Parent(layout));
        Assert.Equal(new[]
        {
            AecApplication.ConfigSourceRelativePath,
            "environment/shared/instructions.md",
            Path.GetRelativePath(layout.Repository, layout.TargetPath).Replace('\\', '/')
        }.OrderBy(path => path, StringComparer.Ordinal), CommittedPaths(layout));
        Assert.Equal("Shared revision.\n", File.ReadAllText(layout.SharedSource));
        Assert.Equal(InstructionSections.Render("Shared revision.\n", "Local paths.\n", "Provider mechanics.\n"),
            File.ReadAllText(layout.TargetPath));
        Assert.Equal("personality = \"pragmatic\"\n",
            File.ReadAllText(layout.CanonicalConfigPath));
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(runtimeConfig, File.ReadAllBytes(configPath));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MissingManagedPersonalityWarnsAndStops(bool configExists)
    {
        const string unmanagedConfig = "model = \"keep\"\n";
        using var layout = NewLayout(configExists ? unmanagedConfig : null);
        var configPath = RuntimeConfigPath(layout);
        var head = layout.Head();
        var canonical = File.ReadAllBytes(layout.CanonicalConfigPath);
        var runtime = File.ReadAllBytes(layout.RuntimePath);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("warning: runtime config does not declare root `personality`",
            result.Error, StringComparison.Ordinal);
        Assert.Equal(head, layout.Head());
        Assert.Equal(canonical, File.ReadAllBytes(layout.CanonicalConfigPath));
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
        if (configExists)
        {
            Assert.Equal(unmanagedConfig, File.ReadAllText(configPath));
        }
        else
        {
            Assert.False(File.Exists(configPath));
        }

        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void MissingEnrollmentDoesNotFallBackToLegacyCanonicalSource()
    {
        using var layout = NewLayout();
        var legacy = Path.Combine(layout.Repository, AecApplication.SourceRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
        File.WriteAllText(legacy, "Legacy source.\n");
        GitProcess.RunRequired(layout.Repository, "Could not stage legacy source",
            "add", "--", AecApplication.SourceRelativePath);
        GitProcess.RunRequired(layout.Repository, "Could not remove enrolled target",
            "rm", "--", Path.GetRelativePath(layout.Repository, layout.TargetPath));
        GitProcess.RunRequired(layout.Repository, "Could not commit fixture change",
            "-c", "user.name=AEC Test", "-c", "user.email=aec-test@example.invalid",
            "-c", "commit.gpgsign=false", "commit", "-m", "Remove enrollment");
        var head = layout.Head();
        var runtime = File.ReadAllBytes(layout.RuntimePath);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("Committed target", result.Error, StringComparison.Ordinal);
        Assert.Equal(head, layout.Head());
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal("Legacy source.\n", File.ReadAllText(legacy));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    [Trait("Category", "PortableReview")]
    public void StaleLastAppliedTargetStopsBeforeConfigCapture()
    {
        using var layout = NewLayout();
        File.WriteAllText(RuntimeConfigPath(layout), "personality = \"friendly\"\n");
        File.WriteAllText(layout.SharedSource, "Shared latest.\n");
        File.WriteAllText(layout.TargetPath, File.ReadAllText(layout.TargetPath)
            .Replace("Shared approval.", "Shared latest.", StringComparison.Ordinal));
        GitProcess.RunRequired(layout.Repository, "Could not stage newer target",
            "add", "--", "environment/shared/instructions.md",
            Path.GetRelativePath(layout.Repository, layout.TargetPath));
        GitProcess.RunRequired(layout.Repository, "Could not commit newer target",
            "-c", "user.name=AEC Test", "-c", "user.email=aec-test@example.invalid",
            "-c", "commit.gpgsign=false", "commit", "-m", "Newer target");
        var head = layout.Head();
        var runtime = File.ReadAllBytes(layout.RuntimePath);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("older target", result.Error, StringComparison.Ordinal);
        Assert.Equal(head, layout.Head());
        Assert.Equal("personality = \"none\"\n",
            File.ReadAllText(layout.CanonicalConfigPath));
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void MultipleInstructionSectionsStopBeforeConfigCapture()
    {
        using var layout = NewLayout();
        File.WriteAllText(layout.RuntimePath, File.ReadAllText(layout.RuntimePath)
            .Replace("Shared approval.", "Shared revision.", StringComparison.Ordinal)
            .Replace("Local paths.", "Local revision.", StringComparison.Ordinal));
        File.WriteAllText(RuntimeConfigPath(layout), "personality = \"friendly\"\n");
        var head = layout.Head();
        var runtime = File.ReadAllBytes(layout.RuntimePath);
        var config = File.ReadAllBytes(RuntimeConfigPath(layout));

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("multiple authored instruction sections", result.Error,
            StringComparison.Ordinal);
        Assert.Equal(head, layout.Head());
        Assert.Equal("personality = \"none\"\n",
            File.ReadAllText(layout.CanonicalConfigPath));
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(config, File.ReadAllBytes(RuntimeConfigPath(layout)));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DetachedHeadStopsBeforeCanonicalWrite(bool configDrift)
    {
        using var layout = NewLayout();
        if (configDrift)
        {
            File.WriteAllText(RuntimeConfigPath(layout), "personality = \"friendly\"\n");
        }
        GitProcess.RunRequired(layout.Repository, "Could not detach fixture HEAD",
            "checkout", "--detach", "--quiet");
        var head = layout.Head();

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("detached HEAD", result.Error, StringComparison.Ordinal);
        Assert.Equal(head, layout.Head());
        Assert.Equal("personality = \"none\"\n",
            File.ReadAllText(layout.CanonicalConfigPath));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Theory]
    [InlineData("repository")]
    [InlineData("codex-home")]
    [InlineData("inside-repository")]
    public void UnsafeRuntimeOrRepositoryPathStopsBeforeWriting(string boundary)
    {
        var insideRepository = boundary == "inside-repository";
        if (!insideRepository && OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Path.Combine(TestTemporaryDirectory.Root, Guid.NewGuid().ToString("N"));
        var repository = Path.Combine(root, "data");
        var codexHome = insideRepository ? repository : Path.Combine(root, "provider-home");
        var alias = Path.Combine(root, "path-alias");
        Directory.CreateDirectory(repository);
        Directory.CreateDirectory(codexHome);
        try
        {
            var runtime = Path.Combine(codexHome, "AGENTS.md");
            const string instructions = "Preserve instructions.\n";
            File.WriteAllText(runtime, instructions);
            if (boundary == "repository")
            {
                Directory.CreateSymbolicLink(alias, repository);
                repository = alias;
            }
            else if (!insideRepository)
            {
                Directory.CreateSymbolicLink(alias, root);
                codexHome = Path.Combine(alias, "provider-home");
            }

            var result = TestApplication.Run("backup", "--repo", repository, "--codex-home", codexHome);

            Assert.Equal(1, result.ExitCode);
            Assert.Empty(result.Output);
            Assert.Contains(insideRepository ? "outside the data repository" : "must not contain a symbolic link",
                result.Error, StringComparison.Ordinal);
            Assert.Equal(instructions, File.ReadAllText(runtime));
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
    public void NestedDirectoryIsRejectedAsTheRepositoryArgument()
    {
        using var layout = NewLayout();
        var nested = Path.Combine(layout.Repository, "nested");
        Directory.CreateDirectory(nested);
        var head = layout.Head();

        var result = TestApplication.Run("backup", "--repo", nested,
            "--codex-home", layout.ProviderHome);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("repository root", result.Error, StringComparison.Ordinal);
        Assert.Equal(head, layout.Head());
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    private static SharedTargetStatusTests.StatusLayout NewLayout(string? runtimeConfig = "personality = \"none\"\n")
    {
        var layout = new SharedTargetStatusTests.StatusLayout("codex");
        layout.WriteRuntime(portableBlock: true);
        if (runtimeConfig is not null)
        {
            File.WriteAllText(RuntimeConfigPath(layout), runtimeConfig);
        }

        return layout;
    }

    private static CommandResult RunWithCodexHomeEnvironment(
        SharedTargetStatusTests.StatusLayout layout, bool explicitHome)
    {
        var previous = Environment.GetEnvironmentVariable("CODEX_HOME");
        try
        {
            Environment.SetEnvironmentVariable("CODEX_HOME",
                explicitHome ? Path.Combine(layout.Root, "missing-home") : layout.ProviderHome);
            return explicitHome ? Run(layout) : TestApplication.Run("backup", "--repo", layout.Repository);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", previous);
        }
    }

    private static CommandResult Run(SharedTargetStatusTests.StatusLayout layout) =>
        TestApplication.Run("backup", "--repo", layout.Repository,
            "--codex-home", layout.ProviderHome);

    private static string RuntimeConfigPath(SharedTargetStatusTests.StatusLayout layout) =>
        Path.Combine(layout.ProviderHome, "config.toml");

    private static string Parent(SharedTargetStatusTests.StatusLayout layout) =>
        GitProcess.RunRequired(layout.Repository, "Could not inspect backup parent",
            "rev-parse", "HEAD^").Output.Trim();

    private static string[] CommittedPaths(SharedTargetStatusTests.StatusLayout layout) =>
        GitProcess.RunRequired(layout.Repository, "Could not inspect backup paths",
            "diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD")
            .Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();
}
