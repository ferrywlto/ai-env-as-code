namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class GitRoundTripTests
{
    [Theory]
    [InlineData("codex")]
    [InlineData("copilot")]
    public void ExplicitGitTransportUpdatesAnotherCheckoutWithoutSharingRuntimePaths(string provider)
    {
        using var layout = new RoundTripLayout(provider);
        var firstCommit = Head(layout.CheckoutA);
        var canonical = layout.CanonicalPaths.ToDictionary(path => path,
            path => File.ReadAllBytes(Path.Combine(layout.CheckoutA, path)));
        var configA = File.ReadAllBytes(Path.Combine(layout.HomeA, layout.ConfigFileName));

        // Git transport is an explicit caller action. A local bare repository
        // exercises real push/clone/fetch without any hosted service or network.
        Git(layout.Root, "init", "--bare", "--quiet", "--template=",
            "--initial-branch=main", layout.Remote);
        Git(layout.CheckoutA, "remote", "add", "shared", layout.Remote);
        Git(layout.CheckoutA, "push", "--quiet", "--set-upstream", "shared", "main");
        Git(layout.Root, "clone", "--quiet", "--template=", "--origin", "shared",
            "--config", "core.autocrlf=false", layout.Remote, layout.CheckoutB);
        Assert.Equal(firstCommit, Head(layout.CheckoutB));
        Assert.False(Directory.Exists(layout.HomeB));
        Assert.Equal(1, layout.Run(layout.CheckoutB, layout.HomeB, "status").ExitCode);
        Assert.False(Directory.Exists(layout.HomeB));

        // The harness home is a separate local prerequisite, not something
        // cloning a portable target is allowed to invent.
        Directory.CreateDirectory(layout.HomeB);
        var configPathB = Path.Combine(layout.HomeB, layout.ConfigFileName);
        var configBefore = layout.InitialConfigB;
        File.WriteAllText(configPathB, configBefore);
        var unconfirmed = layout.Run(layout.CheckoutB, layout.HomeB, "init");
        Assert.Equal(1, unconfirmed.ExitCode);
        Assert.Contains("--force-path-change", unconfirmed.Error, StringComparison.Ordinal);
        Assert.Equal(configBefore, File.ReadAllText(configPathB));
        Assert.False(File.Exists(Path.Combine(layout.HomeB, layout.RuntimeFileName)));
        Assert.False(Directory.Exists(Path.Combine(layout.HomeB, "skills")));
        Assert.Equal(firstCommit, Head(layout.CheckoutB));

        Success(layout.Run(layout.CheckoutB, layout.HomeB, "init", "--force-path-change"));
        Assert.Equal("unchanged", Success(layout.Run(layout.CheckoutB, layout.HomeB, "apply"))
            .Output.Trim());
        var configB = File.ReadAllBytes(configPathB);
        Assert.Equal(layout.ExpectedConfigB, File.ReadAllText(configPathB));
        Assert.Equal(firstCommit, Head(layout.CheckoutB));
        AssertCanonicalBytes(layout.CheckoutB, canonical);
        layout.AssertLocalRuntime(layout.CheckoutB, layout.HomeB);

        File.WriteAllText(layout.SharedSource, "Shared revision from machine A.\n");
        Success(TestApplication.Run("render", "--repo", layout.CheckoutA));
        Commit(layout.CheckoutA, "Review shared revision", "environment/shared/instructions.md",
            layout.TargetRelativePath);
        var publishedCommit = Head(layout.CheckoutA);
        Assert.NotEqual(firstCommit, publishedCommit);
        Git(layout.CheckoutA, "push", "--quiet", "shared", "main");
        Assert.Equal(publishedCommit,
            Git(layout.Remote, "rev-parse", "refs/heads/main").Trim());
        Success(layout.Run(layout.CheckoutA, layout.HomeA, "apply"));

        // Status/apply deliberately see only B's old committed target. Even
        // with a newer remote tip, they must not silently fetch or advance HEAD.
        layout.AssertLocalRuntime(layout.CheckoutB, layout.HomeB);
        Assert.Equal("unchanged", Success(layout.Run(layout.CheckoutB, layout.HomeB, "apply"))
            .Output.Trim());
        Assert.Equal(firstCommit, Head(layout.CheckoutB));
        Assert.Equal(firstCommit,
            Git(layout.CheckoutB, "rev-parse", "refs/remotes/shared/main").Trim());

        var advertised = Git(layout.CheckoutB, "ls-remote", "--symref", "shared", "HEAD");
        Assert.Contains(publishedCommit, advertised, StringComparison.Ordinal);
        Git(layout.CheckoutB, "fetch", "--quiet", "shared");
        Assert.Equal(firstCommit, Head(layout.CheckoutB));
        Git(layout.CheckoutB, "merge", "--ff-only", "--no-edit", "shared/main");
        Assert.Equal(publishedCommit, Head(layout.CheckoutB));
        Assert.Equal(2, layout.Run(layout.CheckoutB, layout.HomeB, "status").ExitCode);
        Success(layout.Run(layout.CheckoutB, layout.HomeB, "apply"));

        foreach (var (checkout, home) in new[]
        {
            (layout.CheckoutA, layout.HomeA), (layout.CheckoutB, layout.HomeB)
        })
        {
            layout.AssertLocalRuntime(checkout, home);
            Assert.Equal(publishedCommit, Head(checkout));
            AssertCanonicalBytes(checkout, canonical);
            Assert.Empty(Git(checkout, "status", "--porcelain"));
            Assert.False(Directory.Exists(Path.Combine(checkout, "environment", "targets",
                layout.Platform, layout.OtherProvider)));
            Assert.Equal(0, TestGit.Run(checkout, "merge-base", "--is-ancestor", firstCommit,
                publishedCommit).ExitCode);
        }
        Assert.Equal(File.ReadAllBytes(layout.TargetPath(layout.CheckoutA)),
            File.ReadAllBytes(layout.TargetPath(layout.CheckoutB)));
        var sections = InstructionSections.Parse(File.ReadAllText(layout.TargetPath(layout.CheckoutB)),
            true, true);
        Assert.Equal("Shared revision from machine A.\n", sections.Shared);
        Assert.Equal(layout.ProviderInstructions, sections.Provider);
        Assert.Equal(configA, File.ReadAllBytes(Path.Combine(layout.HomeA, layout.ConfigFileName)));
        Assert.Equal(configB, File.ReadAllBytes(configPathB));
        Assert.False(Directory.Exists(layout.OtherHomeB));
    }

    private static CommandResult Success(CommandResult result)
    {
        Assert.True(result.ExitCode == 0, result.Error);
        return result;
    }

    private static void AssertCanonicalBytes(string checkout, Dictionary<string, byte[]> canonical)
    {
        foreach (var (path, content) in canonical)
        {
            Assert.Equal(content, File.ReadAllBytes(Path.Combine(checkout, path)));
        }
    }

    private static void Commit(string checkout, string message, params string[] paths)
    {
        Git(checkout, ["add", "--", .. paths]);
        Git(checkout, "commit", "--quiet", "--message", message);
    }
    private static string Head(string checkout) => Git(checkout, "rev-parse", "HEAD").Trim();
    private static string Git(string directory, params string[] arguments) => GitProcess.RunRequired(
        directory, "Round-trip fixture Git operation failed", arguments).Output;

    private sealed class RoundTripLayout : IDisposable
    {
        public RoundTripLayout(string provider)
        {
            Provider = provider;
            Root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(),
                "aec-roundtrip-tests", Guid.NewGuid().ToString("N"));
            CheckoutA = Path.Combine(Root, "machine-a", "data");
            CheckoutB = Path.Combine(Root, "machine-b", "different data path");
            HomeA = Path.Combine(Root, "machine-a", provider + "-home");
            HomeB = Path.Combine(Root, "machine-b", provider + "-home");
            Remote = Path.Combine(Root, "shared-data.git");
            Directory.CreateDirectory(HomeA);
            Directory.CreateDirectory(Path.GetDirectoryName(CheckoutB)!);
            // The current data layout starts with a Codex baseline. Copilot's
            // case retains that history without enrolling a Codex target.
            var codexHome = Path.Combine(Root, "machine-a", "codex-home");
            Directory.CreateDirectory(codexHome);
            File.WriteAllText(Path.Combine(codexHome, "AGENTS.md"),
                "Shared approval.\nLocal paths.\nCodex mechanics remain.\n");
            File.WriteAllText(Path.Combine(codexHome, "config.toml"),
                "model = \"machine-a\"\npersonality = \"friendly\"\n");
            Success(TestApplication.Run("init", "--repo", CheckoutA, "--codex-home", codexHome));
            if (Provider == "copilot")
            {
                File.WriteAllText(Path.Combine(HomeA, RuntimeFileName),
                    "Shared approval.\nLocal paths.\n" + ProviderInstructions);
                File.WriteAllText(Path.Combine(HomeA, ConfigFileName),
                    "{\"machine\":\"a\",\"localSetting\":true}\n");
                Success(Run(CheckoutA, HomeA, "init"));
                Commit(CheckoutA, "Record legacy Copilot baseline",
                    AecApplication.CopilotSourceRelativePath);
            }
            var sourcePaths = new[] { "environment/shared/instructions.md",
                $"environment/platforms/{Platform}/policy.md", $"environment/providers/{provider}/overlay.md" };
            var content = new[] { "Shared approval.\n", "Local paths.\n", ProviderInstructions };
            for (var index = 0; index < sourcePaths.Length; index++)
            {
                var path = Path.Combine(CheckoutA, sourcePaths[index]);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, content[index]);
            }
            Commit(CheckoutA, "Review portable sources", sourcePaths);
            Success(Run(CheckoutA, HomeA, "init", "--enroll-shared"));
            Commit(CheckoutA, "Enroll reviewed provider target", TargetRelativePath);
            Success(Run(CheckoutA, HomeA, "apply"));
            AssertLocalRuntime(CheckoutA, HomeA);
        }

        public string Root { get; }
        public string CheckoutA { get; }
        public string CheckoutB { get; }
        public string HomeA { get; }
        public string HomeB { get; }
        public string Remote { get; }
        public string Provider { get; }
        public string OtherProvider => Provider == "codex" ? "copilot" : "codex";
        public string OtherHomeB => Path.Combine(Root, "machine-b", OtherProvider + "-home");
        public string RuntimeFileName => Provider == "codex" ? "AGENTS.md" : "copilot-instructions.md";
        public string ConfigFileName => Provider == "codex" ? "config.toml" : "config.json";
        public string InitialConfigB => Provider == "codex"
            ? "model = \"machine-b\"\npersonality = 'none'\n[local]\nkeep_setting = true\n"
            : "{\"machine\":\"b\",\"localSetting\":true}\n";
        public string ExpectedConfigB => Provider == "codex"
            ? InitialConfigB.Replace("'none'", "\"friendly\"", StringComparison.Ordinal)
            : InitialConfigB;
        public string ProviderInstructions => Provider == "codex"
            ? "Codex mechanics remain.\n" : "Copilot mechanics remain.\n";
        public string[] CanonicalPaths => Provider == "codex"
            ? [AecApplication.SourceRelativePath, AecApplication.ConfigSourceRelativePath]
            : [AecApplication.SourceRelativePath, AecApplication.ConfigSourceRelativePath,
                AecApplication.CopilotSourceRelativePath];
        public string Platform { get; } = RenderCommand.CurrentPlatform();
        public string SharedSource => Path.Combine(CheckoutA, "environment", "shared", "instructions.md");
        public string TargetRelativePath => $"environment/targets/{Platform}/{Provider}/{RuntimeFileName}";
        public string TargetPath(string checkout) => Path.Combine(checkout, TargetRelativePath);

        public CommandResult Run(string checkout, string home, string command,
            params string[] options) => Provider == "codex"
            ? TestApplication.Run([command, "--repo", checkout, "--codex-home", home, .. options])
            : TestApplication.Run([command, "--repo", checkout, "--provider=copilot",
                "--copilot-home", home, .. options]);

        public void AssertLocalRuntime(string checkout, string home)
        {
            Success(Run(checkout, home, "status"));
            var runtime = File.ReadAllBytes(Path.Combine(home, RuntimeFileName));
            if (Provider == "codex")
            {
                Assert.Equal(checkout, AecInstructionBlock.ReadRepositoryBinding(runtime)!.Repository);
                Assert.Equal(File.ReadAllBytes(TargetPath(checkout)),
                    AecInstructionBlock.Remove(runtime).Content);
            }
            else
            {
                var binding = CopilotInstructionBlock.ReadManagedBinding(runtime)!;
                Assert.Equal(checkout, binding.Repository);
                Assert.Equal(Platform, binding.Platform);
                Assert.EndsWith(File.ReadAllText(TargetPath(checkout)),
                    File.ReadAllText(Path.Combine(home, RuntimeFileName)), StringComparison.Ordinal);
            }
        }

        public void Dispose() => TestDirectoryCleanup.Delete(Root);
    }
}
