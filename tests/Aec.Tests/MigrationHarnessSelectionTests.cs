using System.Text;

namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class MigrationHarnessSelectionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void SelectsOnlyBoundLocalRuntimes(bool codex, bool copilot)
    {
        using var layout = new SelectionLayout();
        if (codex) layout.WriteRuntime("codex");
        if (copilot) layout.WriteRuntime("copilot");

        layout.AssertNoChanges(() =>
        {
            var selected = MigrationHarnessSelection.Read(layout.Preflight);
            var expected = new List<string>();
            if (codex) expected.Add("codex");
            if (copilot) expected.Add("copilot");

            Assert.Equal(expected, selected.Select(item => item.Provider));
            foreach (var item in selected)
            {
                Assert.Equal(layout.Home(item.Provider), item.Home);
                Assert.Equal(layout.RuntimePath(item.Provider), item.RuntimePath);
            }
        });
    }

    [Fact]
    public void RepositoryFoldersAndEmptyHomesCannotEnrollAHarness()
    {
        using var layout = new SelectionLayout();
        foreach (var provider in new[] { "codex", "copilot" })
        {
            Directory.CreateDirectory(layout.Home(provider));
            layout.WriteRepositoryFile($"environment/providers/{provider}/overlay.md", "Draft overlay.\n");
            layout.WriteRepositoryFile($"environment/targets/{RenderCommand.CurrentPlatform()}/{provider}/target.md",
                "Pulled or draft target is not local enrollment.\n");
        }

        layout.AssertNoChanges(() => Assert.Empty(MigrationHarnessSelection.Read(layout.Preflight)));
    }

    [Theory]
    [InlineData("codex", false)]
    [InlineData("copilot", false)]
    [InlineData("codex", true)]
    [InlineData("copilot", true)]
    public void RecognizedPortableBindingsRemainSelectedForIncompleteMigration(string provider, bool wrongPlatform)
    {
        using var layout = new SelectionLayout();
        var platform = RenderCommand.CurrentPlatform();
        if (wrongPlatform) platform = platform == "windows-x64" ? "linux-arm64" : "windows-x64";
        layout.WriteRuntime(provider, portablePlatform: platform);

        layout.AssertNoChanges(() =>
        {
            if (wrongPlatform)
            {
                var error = Assert.Throws<InvalidOperationException>(() =>
                    MigrationHarnessSelection.Read(layout.Preflight));
                Assert.Contains("different AEC platform", error.Message, StringComparison.Ordinal);
            }
            else
            {
                Assert.Equal(provider, Assert.Single(MigrationHarnessSelection.Read(layout.Preflight)).Provider);
            }
        });
    }

    [Theory]
    [InlineData("codex", false)]
    [InlineData("copilot", false)]
    [InlineData("codex", true)]
    [InlineData("copilot", true)]
    public void ExistingUnboundOrMalformedInstructionsStopSelection(string provider, bool malformed)
    {
        using var layout = new SelectionLayout();
        layout.WriteRuntime(provider);
        var content = "Unmanaged personal instructions.\n";
        if (malformed)
        {
            content = provider == "codex" ? "<!-- AEC:BEGIN version=5 -->\n"
                : "<!-- AEC:COPILOT:BEGIN version=1 -->\n";
        }
        File.WriteAllText(layout.RuntimePath(provider), content);

        layout.AssertNoChanges(() =>
        {
            var error = Record.Exception(() => MigrationHarnessSelection.Read(layout.Preflight));
            if (malformed) Assert.IsType<InvalidDataException>(error);
            else Assert.IsType<InvalidOperationException>(error);
        });
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("copilot")]
    public void AnotherCheckoutStopsSelectionEvenWhenTheOtherHarnessMatches(string provider)
    {
        using var layout = new SelectionLayout();
        layout.WriteRuntime(provider == "codex" ? "copilot" : "codex");
        var otherRepository = Path.Combine(layout.Root, "other-data");
        layout.WriteRuntime(provider, repository: otherRepository);

        layout.AssertNoChanges(() =>
        {
            var error = Assert.Throws<InvalidOperationException>(() => MigrationHarnessSelection.Read(layout.Preflight));

            Assert.Contains("different AEC data repository", error.Message, StringComparison.Ordinal);
            Assert.Contains(otherRepository, error.Message, StringComparison.Ordinal);
            Assert.Contains(layout.Repository, error.Message, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("codex", false)]
    [InlineData("copilot", false)]
    [InlineData("codex", true)]
    [InlineData("copilot", true)]
    public void InvalidImplicitHomeOrRuntimeShapeStopsSelection(string provider, bool directoryRuntime)
    {
        using var layout = new SelectionLayout();
        if (directoryRuntime) Directory.CreateDirectory(layout.RuntimePath(provider));
        else File.WriteAllText(layout.Home(provider), "Not a directory.\n");

        layout.AssertNoChanges(() => Assert.Throws<InvalidOperationException>(() =>
            MigrationHarnessSelection.Read(layout.Preflight)));
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("copilot")]
    public void RejectsAHomeInsideTheDataRepository(string provider)
    {
        using var layout = new SelectionLayout();
        var home = Path.Combine(layout.Repository, "runtime");
        Directory.CreateDirectory(home);
        Environment.SetEnvironmentVariable(provider == "codex" ? "CODEX_HOME" : "COPILOT_HOME", home);

        layout.AssertNoChanges(() => Assert.Throws<InvalidOperationException>(() =>
            MigrationHarnessSelection.Read(layout.Preflight)));
    }

    [Theory]
    [InlineData("CODEX_HOME")]
    [InlineData("COPILOT_HOME")]
    public void RejectsRelativeEnvironmentHomeWithoutReadingPersonalDefaults(string variable)
    {
        using var layout = new SelectionLayout();
        Environment.SetEnvironmentVariable(variable, "relative-home");

        layout.AssertNoChanges(() => Assert.Throws<ArgumentException>(() =>
            MigrationHarnessSelection.Read(layout.Preflight)));
    }

    [Fact]
    public void ChangedHeadInvalidatesTheSelectionSnapshot()
    {
        using var layout = new SelectionLayout();
        layout.Git("commit", "--allow-empty", "-m", "New checkpoint");

        layout.AssertNoChanges(() =>
        {
            var error = Assert.Throws<InvalidOperationException>(() => MigrationHarnessSelection.Read(layout.Preflight));
            Assert.Contains("HEAD changed", error.Message, StringComparison.Ordinal);
        });
    }

    private sealed class SelectionLayout : IDisposable
    {
        private readonly string? previousCodexHome = Environment.GetEnvironmentVariable("CODEX_HOME");
        private readonly string? previousCopilotHome = Environment.GetEnvironmentVariable("COPILOT_HOME");

        public SelectionLayout()
        {
            Root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(),
                "aec-migration-selection-tests", Guid.NewGuid().ToString("N"));
            Repository = Path.Combine(Root, "data");
            Directory.CreateDirectory(Repository);
            Git("init", "--quiet", "--template=", "--initial-branch=main");
            Git("commit", "--allow-empty", "-m", "Selection fixture checkpoint");
            // Format/history proof is tested by MigrationPreflightTests. Isolate
            // selection without repeating full init for every filesystem case.
            Preflight = new MigrationPreflight(Repository, Head(),
                MigrationPreflight.LegacyFormat, MigrationPreflight.SectionedFormat);
            Environment.SetEnvironmentVariable("CODEX_HOME", Home("codex"));
            Environment.SetEnvironmentVariable("COPILOT_HOME", Home("copilot"));
        }

        public string Root { get; }
        public string Repository { get; }
        public MigrationPreflight Preflight { get; }
        public string Home(string provider) => Path.Combine(Root, provider + "-home");
        public string RuntimePath(string provider) => Path.Combine(Home(provider),
            provider == "codex" ? "AGENTS.md" : "copilot-instructions.md");
        public string Head() => Git("rev-parse", "HEAD").Trim();
        public string Git(params string[] args) =>
            GitProcess.RunRequired(Repository, "Selection fixture Git failed", args).Output;

        public void WriteRepositoryFile(string relativePath, string content)
        {
            var path = Path.Combine(Repository, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
        }

        public void WriteRuntime(string provider, string? repository = null, string? portablePlatform = null)
        {
            repository ??= Repository;
            Directory.CreateDirectory(Home(provider));
            var body = Encoding.UTF8.GetBytes("Personal instructions.\n");
            byte[] content;
            if (provider == "codex")
            {
                content = portablePlatform is null ? AecInstructionBlock.Merge(body, repository)
                    : AecInstructionBlock.MergeForPortableTarget(body, repository, portablePlatform, false);
            }
            else
            {
                content = portablePlatform is null ? CopilotInstructionBlock.Merge(body, repository)
                    : CopilotInstructionBlock.MergeForPortableTarget(body, repository, portablePlatform);
            }
            File.WriteAllBytes(RuntimePath(provider), content);
        }

        public void AssertNoChanges(Action inspect)
        {
            var head = Head();
            var status = Git("status", "--porcelain");
            var gitDirectory = Path.Combine(Repository, ".git") + Path.DirectorySeparatorChar;
            var files = Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
                .Where(path => !path.StartsWith(gitDirectory, StringComparison.Ordinal))
                .ToDictionary(path => path, File.ReadAllBytes);
            var directories = Directory.GetDirectories(Root, "*", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal).ToArray();

            inspect();

            Assert.Equal(head, Head());
            Assert.Equal(status, Git("status", "--porcelain"));
            Assert.Equal(directories, Directory.GetDirectories(Root, "*", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal));
            Assert.Equal(files.Keys.Order(StringComparer.Ordinal), Directory.EnumerateFiles(Root,
                "*", SearchOption.AllDirectories).Where(path =>
                !path.StartsWith(gitDirectory, StringComparison.Ordinal)).Order(StringComparer.Ordinal));
            foreach (var (path, bytes) in files) Assert.Equal(bytes, File.ReadAllBytes(path));
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", previousCodexHome);
            Environment.SetEnvironmentVariable("COPILOT_HOME", previousCopilotHome);
            TestDirectoryCleanup.Delete(Root);
        }
    }
}
