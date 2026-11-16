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

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"unchanged{Environment.NewLine}", result.Output);
        Assert.Empty(result.Error);
        Assert.Equal(head, layout.Head());
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void ConfigOnlyDriftCommitsOnlyCanonicalConfig()
    {
        using var layout = NewLayout();
        var configPath = RuntimeConfigPath(layout);
        File.WriteAllText(configPath, "personality = \"friendly\"\nmodel = \"keep\"\n");
        var runtime = File.ReadAllBytes(layout.RuntimePath);
        var runtimeConfig = File.ReadAllBytes(configPath);
        var head = layout.Head();

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.StartsWith("committed ", result.Output, StringComparison.Ordinal);
        Assert.Empty(result.Error);
        Assert.Equal(head, Parent(layout));
        Assert.Equal([AecApplication.ConfigSourceRelativePath], CommittedPaths(layout));
        Assert.Equal("Backup portable Codex environment", GitProcess.RunRequired(
            layout.Repository, "Could not inspect backup subject", "log", "-1", "--format=%s")
            .Output.Trim());
        Assert.Equal("personality = \"friendly\"\n",
            File.ReadAllText(layout.CanonicalConfigPath));
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(runtimeConfig, File.ReadAllBytes(configPath));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
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
        Assert.StartsWith("committed ", result.Output, StringComparison.Ordinal);
        Assert.Empty(result.Error);
        Assert.Equal(head, Parent(layout));
        Assert.Equal(new[]
        {
            AecApplication.ConfigSourceRelativePath,
            "environment/shared/instructions.md",
            Path.GetRelativePath(layout.Repository, layout.TargetPath).Replace('\\', '/')
        }.OrderBy(path => path, StringComparer.Ordinal), CommittedPaths(layout));
        Assert.Equal("Shared revision.\n", File.ReadAllText(layout.SharedSource));
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
        using var layout = NewLayout();
        var configPath = RuntimeConfigPath(layout);
        if (configExists)
        {
            File.WriteAllText(configPath, "model = \"keep\"\n");
        }
        else
        {
            File.Delete(configPath);
        }
        var head = layout.Head();
        var canonical = File.ReadAllBytes(layout.CanonicalConfigPath);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("warning: runtime config does not declare root `personality`",
            result.Error, StringComparison.Ordinal);
        Assert.Equal(head, layout.Head());
        Assert.Equal(canonical, File.ReadAllBytes(layout.CanonicalConfigPath));
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

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("multiple authored instruction sections", result.Error,
            StringComparison.Ordinal);
        Assert.Equal(head, layout.Head());
        Assert.Equal("personality = \"none\"\n",
            File.ReadAllText(layout.CanonicalConfigPath));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void ExistingStagedWorkStopsBeforeAnyCanonicalWrite()
    {
        using var layout = NewLayout();
        File.WriteAllText(RuntimeConfigPath(layout), "personality = \"friendly\"\n");
        var note = Path.Combine(layout.Repository, "note.txt");
        File.WriteAllText(note, "Keep staged.\n");
        GitProcess.RunRequired(layout.Repository, "Could not stage fixture note",
            "add", "--", "note.txt");
        var head = layout.Head();

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("already has staged changes", result.Error,
            StringComparison.Ordinal);
        Assert.Equal(head, layout.Head());
        Assert.Equal("personality = \"none\"\n",
            File.ReadAllText(layout.CanonicalConfigPath));
        Assert.Equal("note.txt\n", GitProcess.RunRequired(layout.Repository,
            "Could not inspect staged paths", "diff", "--cached", "--name-only").Output);
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

    [Fact]
    public void ExplicitCodexHomeOverridesTheEnvironment()
    {
        using var layout = NewLayout();
        var previous = Environment.GetEnvironmentVariable("CODEX_HOME");
        try
        {
            Environment.SetEnvironmentVariable("CODEX_HOME",
                Path.Combine(layout.Root, "missing-home"));

            var result = Run(layout);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal($"unchanged{Environment.NewLine}", result.Output);
            Assert.Empty(result.Error);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", previous);
        }
    }

    [Fact]
    public void UsesCodexHomeFromEnvironmentWhenFlagIsAbsent()
    {
        using var layout = NewLayout();
        var previous = Environment.GetEnvironmentVariable("CODEX_HOME");
        try
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", layout.ProviderHome);

            var result = TestApplication.Run("backup", "--repo", layout.Repository);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal($"unchanged{Environment.NewLine}", result.Output);
            Assert.Empty(result.Error);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", previous);
        }
    }

    [Theory]
    [InlineData("repository")]
    [InlineData("codex-home")]
    public void SymbolicLinkAncestorStopsBeforeWriting(string linkedTarget)
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var layout = NewLayout();
        var alias = Path.Combine(layout.Root, $"{linkedTarget}-alias");
        var repository = layout.Repository;
        var codexHome = layout.ProviderHome;
        if (linkedTarget == "repository")
        {
            Directory.CreateSymbolicLink(alias, layout.Repository);
            repository = alias;
        }
        else
        {
            Directory.CreateSymbolicLink(alias, layout.Root);
            codexHome = Path.Combine(alias, "provider-home");
        }
        var head = layout.Head();
        try
        {
            var result = TestApplication.Run("backup", "--repo", repository,
                "--codex-home", codexHome);

            Assert.Equal(1, result.ExitCode);
            Assert.Empty(result.Output);
            Assert.Contains("must not contain a symbolic link", result.Error,
                StringComparison.Ordinal);
            Assert.Equal(head, layout.Head());
            Assert.Equal(string.Empty, layout.GitStatus());
        }
        finally
        {
            Directory.Delete(alias);
        }
    }

    [Fact]
    public void CodexHomeInsideTheDataRepositoryIsRejected()
    {
        using var layout = NewLayout();
        var head = layout.Head();

        var result = TestApplication.Run("backup", "--repo", layout.Repository,
            "--codex-home", layout.Repository);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("outside the data repository", result.Error,
            StringComparison.Ordinal);
        Assert.Equal(head, layout.Head());
        Assert.Equal(string.Empty, layout.GitStatus());
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

    private static SharedTargetStatusTests.StatusLayout NewLayout()
    {
        var layout = new SharedTargetStatusTests.StatusLayout("codex");
        layout.WriteRuntime(portableBlock: true);
        File.WriteAllText(RuntimeConfigPath(layout), "personality = \"none\"\n");
        GitProcess.RunRequired(layout.Repository, "Could not set fixture author",
            "config", "--local", "user.name", "AEC Test");
        GitProcess.RunRequired(layout.Repository, "Could not set fixture email",
            "config", "--local", "user.email", "aec-test@example.invalid");
        GitProcess.RunRequired(layout.Repository, "Could not disable fixture signing",
            "config", "--local", "commit.gpgsign", "false");
        return layout;
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
