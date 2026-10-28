namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class MigrationPreflightTests
{
    [Theory]
    [InlineData("")]
    [InlineData("two")]
    [InlineData("2")]
    [InlineData("v2.0")]
    [InlineData("2.0.0")]
    [InlineData("2.0.0-alpha.4")]
    [InlineData("02.0")]
    [InlineData("2.0 ")]
    [InlineData("2.-1")]
    public void RejectsNoncanonicalFormatVersions(string value)
    {
        var error = Assert.Throws<ArgumentException>(() =>
            MigrationPreflight.ParseFormatVersion(value, "Migration target"));

        Assert.Contains("canonical major.minor", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("2.0.0.0")]
    [InlineData("2.0.9.0")]
    [InlineData("3.0.0.0")]
    public void KnownMigrationDoesNotDependOnBinaryPatchVersion(string binary)
    {
        MigrationPreflight.ValidateVersions(MigrationPreflight.LegacyFormat,
            MigrationPreflight.SectionedFormat, Version.Parse(binary));
    }

    [Theory]
    [InlineData("2.0", "2.0", "2.0", "already 2.0")]
    [InlineData("2.0", "1.0", "2.0", "downgrade")]
    [InlineData("1.0", "2.0", "1.9", "This binary cannot")]
    [InlineData("1.0", "3.0", "9.0", "is not supported")]
    [InlineData("1.0", "1.5", "2.0", "is not supported")]
    [InlineData("1.5", "2.0", "2.0", "is not supported")]
    public void RejectsInvalidVersionTransitions(
        string current, string target, string binary, string expected)
    {
        var error = Assert.Throws<InvalidOperationException>(() =>
            MigrationPreflight.ValidateVersions(Version.Parse(current),
                Version.Parse(target), Version.Parse(binary)));

        Assert.Contains(expected, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadsLegacyHistoryDespiteDraftSectionsAndNonMainBranchWithoutWrites()
    {
        using var layout = new MigrationLayout();
        layout.Git("switch", "-c", "migration-review");
        var target = Path.Combine(layout.Repository, "environment", "targets",
            RenderCommand.CurrentPlatform(), "codex", "AGENTS.md");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, "Partially prepared target.\n");
        layout.Commit("environment/targets");
        var shared = Path.Combine(layout.Repository, "environment", "shared", "instructions.md");
        Directory.CreateDirectory(Path.GetDirectoryName(shared)!);
        File.WriteAllText(shared, "User edits must remain.\n");

        layout.AssertNoChanges(() =>
        {
            var result = MigrationPreflight.Read(layout.Repository, "2.0");

            Assert.Equal(layout.Repository, result.Repository);
            Assert.Equal(layout.Head(), result.Commit);
            Assert.Equal(MigrationPreflight.LegacyFormat, result.CurrentFormat);
            Assert.Equal(MigrationPreflight.SectionedFormat, result.TargetFormat);
        });
    }

    [Theory]
    [InlineData("2.0", "already 2.0")]
    [InlineData("1.0", "downgrade")]
    public void CommittedCompletionMarkerControlsCurrentVersion(string target, string expected)
    {
        using var layout = new MigrationLayout();
        layout.WriteMarker("2.0\n", commit: true);

        layout.AssertNoChanges(() =>
        {
            var error = Assert.Throws<InvalidOperationException>(() =>
                MigrationPreflight.Read(layout.Repository, target));
            Assert.Contains(expected, error.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void InitializedRuntimeCanBeSelectedAfterFormatPreflightWithoutTargets()
    {
        using var layout = new MigrationLayout();
        var root = Path.GetDirectoryName(layout.Repository)!;
        var previousCodex = Environment.GetEnvironmentVariable("CODEX_HOME");
        var previousCopilot = Environment.GetEnvironmentVariable("COPILOT_HOME");
        try
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", Path.Combine(root, "codex-home"));
            Environment.SetEnvironmentVariable("COPILOT_HOME", Path.Combine(root, "absent-copilot"));

            layout.AssertNoChanges(() =>
            {
                var preflight = MigrationPreflight.Read(layout.Repository, "2.0");
                var selected = Assert.Single(MigrationHarnessSelection.Read(preflight));

                Assert.Equal("codex", selected.Provider);
                Assert.Equal(Path.Combine(root, "codex-home", "AGENTS.md"), selected.RuntimePath);
                Assert.False(Directory.Exists(Path.Combine(layout.Repository, "environment", "targets")));
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", previousCodex);
            Environment.SetEnvironmentVariable("COPILOT_HOME", previousCopilot);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void OriginalHistoryCannotHideMissingOrInvalidCurrentLegacyFiles(bool config, bool delete)
    {
        using var layout = new MigrationLayout();
        var relativePath = config
            ? AecApplication.ConfigSourceRelativePath
            : AecApplication.SourceRelativePath;
        var path = Path.Combine(layout.Repository, relativePath);
        if (delete)
        {
            File.Delete(path);
        }
        else
        {
            File.WriteAllText(path, config ? "personality = \"unsupported\"\n" : "Missing AEC block.\n");
        }
        layout.Commit(relativePath);

        layout.AssertNoChanges(() =>
        {
            var error = Record.Exception(() => MigrationPreflight.Read(layout.Repository, "2.0"));

            if (delete)
            {
                Assert.IsType<FileNotFoundException>(error);
            }
            else
            {
                Assert.IsType<InvalidDataException>(error);
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UncommittedMarkerCannotAssertCompletion(bool staged)
    {
        using var layout = new MigrationLayout();
        layout.WriteMarker("2.0\n");
        if (staged)
        {
            layout.Git("add", "--", MigrationPreflight.FormatRelativePath);
        }

        layout.AssertNoChanges(() =>
        {
            var error = Assert.Throws<InvalidOperationException>(() =>
                MigrationPreflight.Read(layout.Repository, "2.0"));
            Assert.Contains("not a single committed Git file", error.Message, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData(false, "unstaged changes")]
    [InlineData(true, "staged changes")]
    public void EditedMarkerDoesNotFallBackToLegacy(bool staged, string expected)
    {
        using var layout = new MigrationLayout();
        layout.WriteMarker("2.0\n", commit: true);
        layout.WriteMarker("1.0\n");
        if (staged)
        {
            layout.Git("add", "--", MigrationPreflight.FormatRelativePath);
        }

        layout.AssertNoChanges(() =>
        {
            var error = Assert.Throws<InvalidOperationException>(() =>
                MigrationPreflight.Read(layout.Repository, "2.0"));
            Assert.Contains(expected, error.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void DeletedMarkerDoesNotFallBackToLegacy()
    {
        using var layout = new MigrationLayout();
        layout.WriteMarker("2.0\n", commit: true);
        File.Delete(layout.MarkerPath);

        layout.AssertNoChanges(() => Assert.Throws<FileNotFoundException>(() =>
            MigrationPreflight.Read(layout.Repository, "2.0")));
    }

    [Fact]
    public void MalformedCommittedMarkerDoesNotFallBackToLegacy()
    {
        using var layout = new MigrationLayout();
        layout.WriteMarker("2.0.0-alpha.4\n", commit: true);

        layout.AssertNoChanges(() =>
        {
            var error = Assert.Throws<ArgumentException>(() =>
                MigrationPreflight.Read(layout.Repository, "2.0"));
            Assert.Contains("Repository format marker", error.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void CommittedLegacyMarkerCannotClaimAnUnissuedCompletionState()
    {
        using var layout = new MigrationLayout();
        layout.WriteMarker("1.0\n", commit: true);

        layout.AssertNoChanges(() =>
        {
            var error = Assert.Throws<InvalidDataException>(() =>
                MigrationPreflight.Read(layout.Repository, "2.0"));
            Assert.Contains("supported completed data format", error.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void MarkerCannotReplaceProofOfInitializationHistory()
    {
        using var layout = new MigrationLayout(initialize: false);
        // Matching commit subjects and paths are not enough: the baseline tree
        // and reconstructed initialization content must also be valid.
        File.WriteAllText(layout.MarkerPath, "2.0\n");
        layout.Commit(MigrationPreflight.FormatRelativePath, "Backup Codex environment");
        layout.Git("commit", "--allow-empty", "-m", "Initialize AEC instructions");

        layout.AssertNoChanges(() =>
        {
            var error = Assert.Throws<InvalidOperationException>(() =>
                MigrationPreflight.Read(layout.Repository, "2.0"));
            Assert.Contains("initialization baseline", error.Message, StringComparison.Ordinal);
        });
    }

    private sealed class MigrationLayout : IDisposable
    {
        private readonly string root;

        public MigrationLayout(bool initialize = true)
        {
            root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(),
                "aec-migration-preflight-tests", Guid.NewGuid().ToString("N"));
            Repository = Path.Combine(root, "data");
            if (!initialize)
            {
                Directory.CreateDirectory(Repository);
                Git("init", "--quiet", "--template=", "--initial-branch=main");
                return;
            }

            var home = Path.Combine(root, "codex-home");
            Directory.CreateDirectory(home);
            File.WriteAllText(Path.Combine(home, "AGENTS.md"), "Personal instructions.\n");
            File.WriteAllText(Path.Combine(home, "config.toml"), "personality = \"friendly\"\n");
            var init = TestApplication.Run("init", "--repo", Repository, "--codex-home", home);
            Assert.True(init.ExitCode == 0, init.Error);
        }

        public string Repository { get; }
        public string MarkerPath => Path.Combine(Repository, MigrationPreflight.FormatRelativePath);
        public string Head() => Git("rev-parse", "HEAD").Trim();

        public void WriteMarker(string content, bool commit = false)
        {
            File.WriteAllText(MarkerPath, content);
            if (commit)
            {
                Commit(MigrationPreflight.FormatRelativePath);
            }
        }

        public void Commit(string path, string message = "Migration fixture checkpoint")
        {
            Git("add", "--", path);
            Git("commit", "-m", message);
        }

        public string Git(params string[] args) =>
            GitProcess.RunRequired(Repository, "Migration fixture Git operation failed", args).Output;

        public void AssertNoChanges(Action inspect)
        {
            var head = Head();
            var status = Git("status", "--porcelain");
            var metadata = Path.Combine(Repository, ".git") + Path.DirectorySeparatorChar;
            var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(path => !path.StartsWith(metadata, StringComparison.Ordinal))
                .ToDictionary(path => path, File.ReadAllBytes);

            inspect();

            Assert.Equal(head, Head());
            Assert.Equal(status, Git("status", "--porcelain"));
            Assert.Equal(files.Keys.Order(StringComparer.Ordinal), Directory.EnumerateFiles(root,
                "*", SearchOption.AllDirectories).Where(path =>
                !path.StartsWith(metadata, StringComparison.Ordinal)).Order(StringComparer.Ordinal));
            foreach (var (path, content) in files)
            {
                Assert.Equal(content, File.ReadAllBytes(path));
            }
        }

        public void Dispose() => TestDirectoryCleanup.Delete(root);
    }
}
