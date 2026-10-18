namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class PortableEnvironmentStatusTests
{
    [Theory]
    [InlineData("codex")]
    [InlineData("copilot")]
    public void MatchingManagedRuntimeIsInSyncWithoutWrites(string provider)
    {
        using var layout = new SharedTargetStatusTests.StatusLayout(provider);
        layout.WriteRuntime(portableBlock: true);
        var configPath = Path.Combine(layout.ProviderHome, "config.toml");
        if (provider == "codex")
        {
            File.WriteAllText(configPath, "personality = \"none\"\nmodel = \"keep-me\"\n");
        }

        var runtime = File.ReadAllBytes(layout.RuntimePath);
        var config = provider == "codex" ? File.ReadAllBytes(configPath) : null;
        var head = layout.Head();

        Assert.Equal("in_sync", PortableEnvironmentStatus.Inspect(
            layout.Repository, provider, layout.ProviderHome));
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
        if (config is not null)
        {
            Assert.Equal(config, File.ReadAllBytes(configPath));
        }

        Assert.Equal(head, layout.Head());
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("copilot")]
    public void InstructionDriftIsDifferent(string provider)
    {
        using var layout = new SharedTargetStatusTests.StatusLayout(provider);
        layout.WriteRuntime(portableBlock: true);
        if (provider == "codex")
        {
            File.WriteAllText(Path.Combine(layout.ProviderHome, "config.toml"),
                "personality = \"none\"\n");
        }

        File.AppendAllText(layout.RuntimePath, "Unreviewed instruction edit.\n");
        var edited = File.ReadAllBytes(layout.RuntimePath);

        Assert.Equal("different", PortableEnvironmentStatus.Inspect(
            layout.Repository, provider, layout.ProviderHome));
        Assert.Equal(edited, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("model = \"keep-me\"\n")]
    [InlineData("personality = \"friendly\"\nmodel = \"keep-me\"\n")]
    public void MissingOrDifferentCodexPersonalityIsDifferent(string? runtimeConfig)
    {
        using var layout = new SharedTargetStatusTests.StatusLayout("codex");
        layout.WriteRuntime(portableBlock: true);
        var configPath = Path.Combine(layout.ProviderHome, "config.toml");
        if (runtimeConfig is not null)
        {
            File.WriteAllText(configPath, runtimeConfig);
        }

        var runtime = File.ReadAllBytes(layout.RuntimePath);
        Assert.Equal("different", PortableEnvironmentStatus.Inspect(
            layout.Repository, "codex", layout.ProviderHome));
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(runtimeConfig is null, !File.Exists(configPath));
        if (runtimeConfig is not null)
        {
            Assert.Equal(runtimeConfig, File.ReadAllText(configPath));
        }

        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void UnmanagedCodexConfigValuesDoNotCountAsDrift()
    {
        using var layout = new SharedTargetStatusTests.StatusLayout("codex");
        layout.WriteRuntime(portableBlock: true);
        var configPath = Path.Combine(layout.ProviderHome, "config.toml");
        var config = "personality = \"none\"\nmodel = \"different-machine-model\"\n";
        File.WriteAllText(configPath, config);

        Assert.Equal("in_sync", PortableEnvironmentStatus.Inspect(
            layout.Repository, "codex", layout.ProviderHome));
        Assert.Equal(config, File.ReadAllText(configPath));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void InvalidCodexRuntimeConfigStopsWithoutWrites()
    {
        using var layout = new SharedTargetStatusTests.StatusLayout("codex");
        layout.WriteRuntime(portableBlock: true);
        var configPath = Path.Combine(layout.ProviderHome, "config.toml");
        var invalid = "personality = \"none\"\npersonality = \"friendly\"\n";
        File.WriteAllText(configPath, invalid);

        Assert.Throws<InvalidDataException>(() => PortableEnvironmentStatus.Inspect(
            layout.Repository, "codex", layout.ProviderHome));
        Assert.Equal(invalid, File.ReadAllText(configPath));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void DirtyCanonicalCodexConfigStopsWithoutWrites()
    {
        using var layout = new SharedTargetStatusTests.StatusLayout("codex");
        layout.WriteRuntime(portableBlock: true);
        File.WriteAllText(Path.Combine(layout.ProviderHome, "config.toml"),
            "personality = \"none\"\n");
        File.WriteAllText(layout.CanonicalConfigPath, "personality = \"friendly\"\n");

        Assert.Throws<InvalidOperationException>(() => PortableEnvironmentStatus.Inspect(
            layout.Repository, "codex", layout.ProviderHome));
        Assert.Equal("personality = \"friendly\"\n",
            File.ReadAllText(layout.CanonicalConfigPath));
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("copilot")]
    public void MissingRuntimeInstructionsAreMissing(string provider)
    {
        using var layout = new SharedTargetStatusTests.StatusLayout(provider);

        Assert.Equal("missing", PortableEnvironmentStatus.Inspect(
            layout.Repository, provider, layout.ProviderHome));
        Assert.False(File.Exists(layout.RuntimePath));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void CopilotDoesNotInspectUnmanagedConfig()
    {
        using var layout = new SharedTargetStatusTests.StatusLayout("copilot");
        layout.WriteRuntime(portableBlock: true);
        var configPath = Path.Combine(layout.ProviderHome, "config.toml");
        File.WriteAllText(configPath, "not a TOML file");

        Assert.Equal("in_sync", PortableEnvironmentStatus.Inspect(
            layout.Repository, "copilot", layout.ProviderHome));
        Assert.Equal("not a TOML file", File.ReadAllText(configPath));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void WrongRepositoryBindingStopsWithoutWrites()
    {
        using var layout = new SharedTargetStatusTests.StatusLayout("codex");
        layout.WriteRuntime(boundRepository: Path.Combine(layout.Root, "other-data"),
            portableBlock: true);
        var runtime = File.ReadAllBytes(layout.RuntimePath);

        Assert.Throws<InvalidOperationException>(() => PortableEnvironmentStatus.Inspect(
            layout.Repository, "codex", layout.ProviderHome));
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(string.Empty, layout.GitStatus());
    }
}
