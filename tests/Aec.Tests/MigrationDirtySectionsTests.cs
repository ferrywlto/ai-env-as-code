namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class MigrationDirtySectionsTests
{
    private const string Shared = "environment/shared/instructions.md";

    [Fact]
    public void CleanSectionsDoNotPromptOrReadInput()
    {
        using var layout = new DirtyLayout();
        using var input = new StringReader("Overwrite");
        using var output = new StringWriter();

        layout.AssertNoChanges(() =>
        {
            var preview = layout.Read();
            Assert.Empty(preview.Paths);
            Assert.Equal(MigrationSectionChoice.Unchanged, preview.Prompt(input, output));
            Assert.Equal("Overwrite", input.ReadToEnd());
            Assert.Equal("", output.ToString());
        });
    }

    [Theory]
    [InlineData("shared")]
    [InlineData("platform")]
    [InlineData("codex")]
    [InlineData("copilot")]
    public void ExactSelectedSectionPathsAreEligible(string section)
    {
        using var layout = new DirtyLayout();
        var path = section switch
        {
            "shared" => Shared,
            "platform" => $"environment/platforms/{RenderCommand.CurrentPlatform()}/policy.md",
            _ => $"environment/providers/{section}/overlay.md"
        };
        layout.Write(path);

        layout.AssertNoChanges(() => Assert.Equal(path, Assert.Single(layout.Read("codex", "copilot").Paths)));
    }

    [Theory]
    [InlineData("untracked")]
    [InlineData("staged-add")]
    [InlineData("modified")]
    [InlineData("staged-modified")]
    [InlineData("staged-and-unstaged")]
    [InlineData("deleted")]
    [InlineData("staged-deleted")]
    public void OrdinaryDirtyStatesRemainUnchangedUntilLaterPreparation(string state)
    {
        using var layout = new DirtyLayout();
        layout.Write(Shared, "Original instructions.\n");
        if (state is not ("untracked" or "staged-add")) layout.Commit(Shared);
        layout.Write(Shared, "User edits.\n");
        if (state is "staged-add" or "staged-modified" or "staged-and-unstaged") layout.Git("add", "--", Shared);
        if (state == "staged-and-unstaged") layout.Write(Shared, "More user edits.\n");
        if (state is "deleted" or "staged-deleted") File.Delete(layout.PathFor(Shared));
        if (state == "staged-deleted") layout.Git("add", "--", Shared);

        layout.AssertNoChanges(() => Assert.Equal(Shared, Assert.Single(layout.Read().Paths)));
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("notes with spaces\nand a newline.md")]
    [InlineData("environment/shared/instructions.md.extra")]
    [InlineData("environment/shared/other.md")]
    [InlineData("environment/providers/copilot/overlay.md")]
    [InlineData("environment/providers/codex/AGENTS.md")]
    [InlineData("environment/providers/codex/config.toml")]
    [InlineData(".aec-version")]
    public void OtherDirtyPathsStopRatherThanOfferOverwrite(string path)
    {
        using var layout = new DirtyLayout();
        // Newlines are valid Unix Git pathnames but not Windows filenames.
        if (OperatingSystem.IsWindows()) path = path.Replace('\n', '_');
        layout.Write(Shared);
        layout.Write(path);

        layout.AssertNoChanges(() =>
        {
            var error = Assert.Throws<InvalidOperationException>(() => layout.Read("codex"));
            Assert.Contains("outside the selected migration sections", error.Message, StringComparison.Ordinal);
            Assert.Contains(path, error.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void OtherPlatformAndRenderedTargetsAreNotMigrationOwnedSources()
    {
        using var layout = new DirtyLayout();
        var platform = RenderCommand.CurrentPlatform() == "windows-x64" ? "linux-arm64" : "windows-x64";
        layout.Write($"environment/platforms/{platform}/policy.md");
        layout.AssertNoChanges(() => Assert.Throws<InvalidOperationException>(() => layout.Read()));
        File.Delete(layout.PathFor($"environment/platforms/{platform}/policy.md"));
        layout.Write($"environment/targets/{RenderCommand.CurrentPlatform()}/codex/AGENTS.md");
        layout.AssertNoChanges(() => Assert.Throws<InvalidOperationException>(() => layout.Read()));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RenamesIntoOrOutOfAnAllowedSectionStop(bool intoSection)
    {
        using var layout = new DirtyLayout();
        var from = intoSection ? "notes.md" : Shared;
        var to = intoSection ? Shared : "notes.md";
        layout.Write(from);
        layout.Commit(from);
        Directory.CreateDirectory(Path.GetDirectoryName(layout.PathFor(to))!);
        layout.Git("mv", "--", from, to);

        layout.AssertNoChanges(() =>
        {
            var error = Assert.Throws<InvalidOperationException>(() => layout.Read());
            Assert.Contains("Unsafe Git status", error.Message, StringComparison.Ordinal);
        });
    }

    [Theory]
    [InlineData("120000", false)]
    [InlineData("120000", true)]
    [InlineData("160000", false)]
    [InlineData("160000", true)]
    public void IndexedOrCommittedLinkKindsStopEvenWithoutAWorkingLink(string mode, bool committed)
    {
        using var layout = new DirtyLayout();
        layout.Write(Shared);
        var objectId = mode == "120000" ? layout.Git("hash-object", "-w", "--", Shared).Trim()
            : layout.Git("rev-parse", "HEAD").Trim();
        layout.Git("update-index", "--add", "--cacheinfo", mode, objectId, Shared);
        if (committed) layout.CommitIndex();

        layout.AssertNoChanges(() =>
        {
            var error = Assert.Throws<InvalidOperationException>(() => layout.Read());
            Assert.Contains("regular Git files", error.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ReplacementRefsCannotHideCommittedLinkKinds()
    {
        using var layout = new DirtyLayout();
        layout.Write(Shared);
        var blob = layout.Git("hash-object", "-w", "--", Shared).Trim();
        layout.Git("update-index", "--add", "--cacheinfo", "120000", blob, Shared);
        layout.CommitIndex();
        var original = layout.Git("rev-parse", "HEAD").Trim();
        // Build a replacement with regular bytes without moving the pinned HEAD.
        layout.Git("add", "--", Shared);
        var tree = layout.Git("write-tree").Trim();
        var replacement = layout.Git("commit-tree", tree, "-m", "Substitute regular tree").Trim();
        layout.Git("replace", original, replacement);

        layout.AssertNoChanges(() =>
        {
            var error = Assert.Throws<InvalidOperationException>(() => layout.Read());
            Assert.Contains("regular Git files", error.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void AddAddMergeConflictIsNotAnOrdinaryAddedSection()
    {
        using var layout = new DirtyLayout();
        layout.Git("branch", "other");
        layout.Write(Shared, "Main instructions.\n");
        layout.Commit(Shared);
        layout.Git("switch", "other");
        layout.Write(Shared, "Other instructions.\n");
        layout.Commit(Shared);
        layout.Git("switch", "main");
        layout.PinHead();
        var merge = GitProcess.Run(layout.Repository, "merge", "--no-commit", "other");
        Assert.Equal(1, merge.ExitCode);

        layout.AssertNoChanges(() =>
        {
            var error = Assert.Throws<InvalidOperationException>(() => layout.Read());
            Assert.Contains("Unsafe Git status", error.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void IgnoredSectionFilesCannotBypassTheDirtyPrompt()
    {
        using var layout = new DirtyLayout();
        layout.Write(".gitignore", Shared + "\n");
        layout.Commit(".gitignore");
        layout.Write(Shared);

        layout.AssertNoChanges(() =>
        {
            var error = Assert.Throws<InvalidOperationException>(() => layout.Read());
            Assert.Contains("ignored by Git", error.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void DirectoryInPlaceOfAnAllowedFileStops()
    {
        using var layout = new DirtyLayout();
        Directory.CreateDirectory(layout.PathFor(Shared));

        layout.AssertNoChanges(() => Assert.Throws<InvalidOperationException>(() => layout.Read()));
    }

    [Fact]
    public void ChangedHeadAndAbsentHarnessesStop()
    {
        using var layout = new DirtyLayout();
        layout.AssertNoChanges(() => Assert.Throws<InvalidOperationException>(() => layout.Read([])));
        layout.Git("commit", "--allow-empty", "-m", "Changed HEAD");
        layout.AssertNoChanges(() => Assert.Throws<InvalidOperationException>(() => layout.Read()));
    }

    [Theory]
    [InlineData("Preserve", false)]
    [InlineData("Overwrite", true)]
    public void ExplicitChoicesOnlyReturnADecisionWithoutMutatingFiles(string answer, bool overwrite)
    {
        using var layout = new DirtyLayout();
        layout.Write(Shared, "User draft.\n");
        using var input = new StringReader(answer + "\n");
        using var output = new StringWriter();

        var expected = overwrite ? MigrationSectionChoice.Overwrite : MigrationSectionChoice.Preserve;
        layout.AssertNoChanges(() => Assert.Equal(expected, layout.Read().Prompt(input, output)));
        Assert.Contains(Shared, output.ToString(), StringComparison.Ordinal);
        Assert.Contains("Overwrite proposes resetting dirty section files to empty Markdown", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("Discarded uncommitted section edits may not be recoverable from Git history.", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("does not change any files", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void InvalidAndBlankChoicesHaveNoDefault()
    {
        using var input = new StringReader("\npreserve\nyes\nOverwrite\n");
        using var output = new StringWriter();
        var preview = new MigrationDirtySections([Shared]);

        Assert.Equal(MigrationSectionChoice.Overwrite, preview.Prompt(input, output));
        Assert.Equal(3, output.ToString().Split("No choice recorded.").Length - 1);
    }

    [Fact]
    public void EofAndCancellationNeverChooseOverwriteOrPreserve()
    {
        var preview = new MigrationDirtySections([Shared]);
        using var input = new StringReader("");
        using var output = new StringWriter();
        Assert.Equal(MigrationSectionChoice.Cancelled, preview.Prompt(input, output));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var unread = new StringReader("Overwrite\n");
        Assert.Equal(MigrationSectionChoice.Cancelled, preview.Prompt(unread, output, cancellation.Token));
        Assert.Equal("Overwrite\n", unread.ReadToEnd());
    }

    [Fact]
    public void CancellationDuringAReadDiscardsTheEnteredChoice()
    {
        using var cancellation = new CancellationTokenSource();
        using var input = new CancellingReader(cancellation);
        using var output = new StringWriter();

        Assert.Equal(MigrationSectionChoice.Cancelled,
            new MigrationDirtySections([Shared]).Prompt(input, output, cancellation.Token));
    }

    private sealed class CancellingReader(CancellationTokenSource cancellation) : StringReader("Overwrite\n")
    {
        public override string? ReadLine()
        {
            cancellation.Cancel();
            return base.ReadLine();
        }
    }

    private sealed class DirtyLayout : IDisposable
    {
        private readonly string root = Path.Combine(
            OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(),
            "aec-migration-dirty-tests", Guid.NewGuid().ToString("N"));
        public string Repository { get; }
        private MigrationPreflight preflight;

        public DirtyLayout()
        {
            Repository = Path.Combine(root, "data");
            Directory.CreateDirectory(Repository);
            Git("init", "--quiet", "--template=", "--initial-branch=main");
            Git("commit", "--allow-empty", "-m", "Dirty section fixture checkpoint");
            preflight = Snapshot();
        }

        public string PathFor(string path) => Path.Combine(Repository, path);
        public string Git(params string[] arguments) =>
            GitProcess.RunRequired(Repository, "Dirty section fixture Git failed", arguments).Output;
        private MigrationPreflight Snapshot() => new(Repository, Git("rev-parse", "HEAD").Trim(),
            MigrationPreflight.LegacyFormat, MigrationPreflight.SectionedFormat);

        public void Write(string path, string content = "User instructions.\n")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathFor(path))!);
            File.WriteAllText(PathFor(path), content);
        }

        public void Commit(string path)
        {
            Git("add", "--", path);
            CommitIndex();
        }

        public void CommitIndex()
        {
            Git("commit", "-m", "Dirty section baseline");
            preflight = Snapshot();
        }

        public void PinHead() => preflight = Snapshot();

        public MigrationDirtySections Read(params string[] providers)
        {
            // Binding selection has its own tests; never resolve real homes here.
            return MigrationDirtySections.Read(preflight, providers.Select(provider =>
                new MigrationHarness(provider, Path.Combine(root, provider),
                    Path.Combine(root, provider, "unused-runtime"))).ToArray());
        }

        public MigrationDirtySections Read() => Read("codex");

        public void AssertNoChanges(Action inspect)
        {
            var head = Git("rev-parse", "HEAD");
            var status = Git("--no-optional-locks", "status", "--porcelain=v1", "-z", "--untracked-files=all");
            var indexPath = PathFor(".git/index");
            var index = File.Exists(indexPath) ? File.ReadAllBytes(indexPath) : null;
            var metadata = PathFor(".git") + Path.DirectorySeparatorChar;
            var files = Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(path => !path.StartsWith(metadata, StringComparison.Ordinal))
                .ToDictionary(path => path, File.ReadAllBytes);
            var directories = Directory.GetDirectories(root, "*", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal).ToArray();

            inspect();

            Assert.Equal(head, Git("rev-parse", "HEAD"));
            Assert.Equal(status, Git("--no-optional-locks", "status", "--porcelain=v1", "-z", "--untracked-files=all"));
            Assert.Equal(index, File.Exists(indexPath) ? File.ReadAllBytes(indexPath) : null);
            Assert.Equal(directories, Directory.GetDirectories(root, "*", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal));
            Assert.Equal(files.Keys.Order(StringComparer.Ordinal), Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Where(path => !path.StartsWith(metadata, StringComparison.Ordinal)).Order(StringComparer.Ordinal));
            foreach (var (path, bytes) in files) Assert.Equal(bytes, File.ReadAllBytes(path));
        }

        public void Dispose() => TestDirectoryCleanup.Delete(root);
    }
}
