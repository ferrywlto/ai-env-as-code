namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class MigrationPreparationTests
{
    private const string Shared = "environment/shared/instructions.md";

    [Fact]
    public void AlreadyCancelledDoesNotInspectRepositoryOrReadInput()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        using var input = new RefuseInput();

        Assert.Null(MigrationPreparation.Run("not-an-absolute-repository", "2.0",
            input, TextWriter.Null, cancellation.Token));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-choice\n")]
    public void EndOfInputCancelsBeforeLookingUpTheRemote(string answers)
    {
        using var layout = new MigrationRepositoryFixture();
        layout.Write(Shared, "Dirty draft.\n");
        BreakRemote(layout);
        using var input = new StringReader(answers);
        using var output = new StringWriter();

        layout.AssertNoChanges(() => Assert.Null(MigrationPreparation.Run(layout.Repository,
            "2.0", input, output)));
        Assert.Contains("Preserve or Overwrite", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CancellationWhileAnsweringStopsBeforeRemoteLookup()
    {
        using var layout = new MigrationRepositoryFixture();
        layout.Write(Shared, "Dirty draft.\n");
        BreakRemote(layout);
        using var cancellation = new CancellationTokenSource();
        using var input = new CallbackInput(cancellation.Cancel, "Preserve");

        layout.AssertNoChanges(() => Assert.Null(MigrationPreparation.Run(layout.Repository,
            "2.0", input, TextWriter.Null, cancellation.Token)));
    }

    [Fact]
    public void CleanBehindCheckoutReturnsRefreshedContextWithoutReadingInput()
    {
        using var layout = new MigrationRepositoryFixture();
        var original = layout.Head();
        layout.AdvanceRemote();
        var expected = layout.Git("ls-remote", "shared", "refs/heads/main").Split('\t')[0];
        var runtime = layout.RuntimeBytes();
        using var input = new RefuseInput();

        var result = Assert.IsType<MigrationPreparation>(MigrationPreparation.Run(layout.Repository,
            "2.0", input, TextWriter.Null));

        Assert.NotEqual(original, result.Preflight.Commit);
        Assert.Equal(original, result.OriginalCommit);
        Assert.Equal(expected, result.Preflight.Commit);
        Assert.Equal(expected, layout.Head());
        Assert.Collection(result.Harnesses,
            harness => Assert.Equal("codex", harness.Provider),
            harness => Assert.Equal("copilot", harness.Provider));
        Assert.Equal(MigrationSectionChoice.Unchanged, result.SectionChoice);
        Assert.Empty(result.DirtySections.Paths);
        Assert.False(File.Exists(Path.Combine(layout.Repository, MigrationPreflight.FormatRelativePath)));
        Assert.False(Directory.Exists(Path.Combine(layout.Repository, "environment", "targets")));
        layout.AssertRuntime(runtime);
    }

    [Theory]
    [InlineData(false, "Preserve")]
    [InlineData(false, "Overwrite")]
    [InlineData(true, "Preserve")]
    [InlineData(true, "Overwrite")]
    public void EqualOrAheadReturnsChoiceWithoutDiscardingDrafts(bool ahead, string answer)
    {
        using var layout = new MigrationRepositoryFixture();
        if (ahead) layout.Git("commit", "--allow-empty", "-m", "Local revision");
        layout.Write(Shared, "Keep staged and unstaged draft.\n");
        layout.Git("add", "--", Shared);
        layout.Write(Shared, "Keep later edits too.\n");
        using var input = new StringReader(answer + "\nDONE\n");

        layout.AssertNoChanges(() =>
        {
            var result = Assert.IsType<MigrationPreparation>(MigrationPreparation.Run(layout.Repository,
                "2.0", input, TextWriter.Null));
            Assert.Equal(answer, result.SectionChoice.ToString());
            Assert.Equal(Shared, Assert.Single(result.DirtySections.Paths));
            Assert.Equal(layout.Head(), result.Preflight.Commit);
        });
        Assert.Equal("DONE", input.ReadLine());
    }

    [Theory]
    [InlineData("Preserve")]
    [InlineData("Overwrite")]
    public void NeitherChoicePermitsAdvancingADirtyBehindCheckout(string answer)
    {
        using var layout = new MigrationRepositoryFixture();
        layout.AdvanceRemote();
        layout.Write(Shared, "User draft must not be discarded.\n");
        var head = layout.Head();
        var index = File.ReadAllBytes(Path.Combine(layout.Repository, ".git", "index"));
        var runtime = layout.RuntimeBytes();
        using var input = new StringReader(answer);

        var error = Assert.Throws<InvalidOperationException>(() => MigrationPreparation.Run(
            layout.Repository, "2.0", input, TextWriter.Null));

        Assert.Contains("clean checkout", error.Message, StringComparison.Ordinal);
        Assert.Equal(head, layout.Head());
        Assert.Equal(index, File.ReadAllBytes(Path.Combine(layout.Repository, ".git", "index")));
        Assert.Equal("User draft must not be discarded.\n", File.ReadAllText(Path.Combine(layout.Repository, Shared)));
        Assert.Empty(layout.Git("stash", "list"));
        layout.AssertRuntime(runtime);
    }

    [Theory]
    [InlineData("unrelated-dirt")]
    [InlineData("no-harness")]
    public void LocalRefusalPrecedesPromptAndRemoteLookup(string cause)
    {
        using var layout = new MigrationRepositoryFixture();
        BreakRemote(layout);
        if (cause == "unrelated-dirt") layout.Write("unrelated.md", "Unrelated changes.\n");
        else foreach (var path in layout.RuntimeBytes().Keys.Where(path =>
            Path.GetFileName(path) is "AGENTS.md" or "copilot-instructions.md")) File.Delete(path);
        using var input = new RefuseInput();

        layout.AssertNoChanges(() =>
        {
            var error = Assert.Throws<InvalidOperationException>(() => MigrationPreparation.Run(
                layout.Repository, "2.0", input, TextWriter.Null));
            Assert.Contains(cause == "unrelated-dirt" ? "outside the selected" : "locally enrolled",
                error.Message, StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ChangedHeadDuringPromptStopsBeforeRemoteLookup()
    {
        using var layout = new MigrationRepositoryFixture();
        layout.Write(Shared, "Draft.\n");
        BreakRemote(layout);
        using var input = new CallbackInput(() => layout.Git("commit", "--allow-empty", "-m", "User checkpoint"), "Preserve");

        var error = Assert.Throws<InvalidOperationException>(() => MigrationPreparation.Run(
            layout.Repository, "2.0", input, TextWriter.Null));

        Assert.Contains("context changed", error.Message, StringComparison.Ordinal);
        Assert.Equal("Draft.\n", File.ReadAllText(Path.Combine(layout.Repository, Shared)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SamePathEditsOrStagingDuringPromptInvalidateTheChoice(bool stageOnly)
    {
        using var layout = new MigrationRepositoryFixture();
        layout.Write(Shared, "Reviewed draft.\n");
        BreakRemote(layout);
        using var input = new CallbackInput(() =>
        {
            if (stageOnly) layout.Git("add", "--", Shared);
            else layout.Write(Shared, "New user edits.\n");
        }, "Overwrite");

        var error = Assert.Throws<InvalidOperationException>(() => MigrationPreparation.Run(
            layout.Repository, "2.0", input, TextWriter.Null));

        Assert.Contains("context changed", error.Message, StringComparison.Ordinal);
        Assert.Equal(stageOnly ? "Reviewed draft.\n" : "New user edits.\n",
            File.ReadAllText(Path.Combine(layout.Repository, Shared)));
    }

    [Fact]
    public void PulledCompletionIsReportedAsRetainedHistoryNotCancellation()
    {
        using var layout = new MigrationRepositoryFixture();
        layout.AdvanceRemote(writer => File.WriteAllText(Path.Combine(writer,
            MigrationPreflight.FormatRelativePath), "2.0\n"));
        var expected = layout.Git("ls-remote", "shared", "refs/heads/main").Split('\t')[0];
        var runtime = layout.RuntimeBytes();
        using var input = new RefuseInput();

        var error = Assert.Throws<InvalidOperationException>(() => MigrationPreparation.Run(
            layout.Repository, "2.0", input, TextWriter.Null));

        Assert.Contains("retrieved history is retained", error.Message, StringComparison.Ordinal);
        Assert.Equal(expected, layout.Head());
        layout.AssertRuntime(runtime);
    }

    private static void BreakRemote(MigrationRepositoryFixture layout) =>
        layout.Git("remote", "set-url", "shared", Path.Combine(layout.Root, "absent-remote.git"));

    [Fact]
    public void InvalidPulledSectionReportsRetainedHistoryAfterAdvancement()
    {
        using var layout = new MigrationRepositoryFixture();
        layout.AdvanceRemote(writer =>
        {
            // A directory exercises the invalid-file boundary without requiring
            // platform-specific permission to create a symbolic link.
            var directory = Path.Combine(writer, Shared);
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "not-markdown.txt"), "Invalid section shape.\n");
        });
        var expected = layout.Git("ls-remote", "shared", "refs/heads/main").Split('\t')[0];
        var runtime = layout.RuntimeBytes();
        using var input = new RefuseInput();

        var error = Assert.Throws<InvalidOperationException>(() => MigrationPreparation.Run(
            layout.Repository, "2.0", input, TextWriter.Null));

        Assert.Contains("final section validation failed; retrieved history is retained", error.Message, StringComparison.Ordinal);
        Assert.NotNull(error.InnerException);
        Assert.Equal(expected, layout.Head());
        layout.AssertRuntime(runtime);
    }

    private sealed class RefuseInput : TextReader
    {
        public override string? ReadLine() => throw new InvalidOperationException("Unexpected input read.");
    }

    private sealed class CallbackInput(Action action, string answer) : TextReader
    {
        public override string? ReadLine()
        {
            action();
            return answer;
        }
    }
}
