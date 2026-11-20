using Layout = Aec.Tests.MigrationPortableCapturePreviewTests.Layout;

namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class MigrationCommandTests
{
    private const string SharedDraftPath = "environment/shared/instructions.md";

    [Fact]
    public void PublicMigrationCompletesWithUnusedGlobalLfsFilter()
    {
        using var layout = new Layout();
        var sentinel = Path.Combine(layout.Files.Root, "filter-ran");
        var globalConfig = Path.Combine(layout.Files.Root, "global-gitconfig");
        var config = $"[user]\n\tname = AEC Fixture\n\temail = fixture@example.invalid\n[filter \"lfs\"]\n\tclean = touch '{sentinel}'\n\tsmudge = touch '{sentinel}'\n\tprocess = touch '{sentinel}'\n\trequired = true\n";
        File.WriteAllText(globalConfig, config);
        var previousGlobal = Environment.GetEnvironmentVariable("GIT_CONFIG_GLOBAL");
        Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", globalConfig);
        try
        {
            using var output = new StringWriter();
            using var error = new StringWriter();
            using var input = new EditingReader(layout, output, true);

            var exitCode = AecApplication.Run(["migrate", "2.0", "--repo", layout.Repository], input, output, error);

            Assert.True(exitCode == 0, error.ToString());
            Assert.False(File.Exists(sentinel));
            Assert.Equal(config, File.ReadAllText(globalConfig));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GIT_CONFIG_GLOBAL", previousGlobal);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PublicCommandCompletesOnlyAfterDone(bool confirm)
    {
        using var layout = new Layout();
        using var output = new StringWriter();
        using var error = new StringWriter();
        using var input = new EditingReader(layout, output, confirm);
        var original = layout.Files.Head();

        var exitCode = AecApplication.Run(["migrate", "2.0", "--repo", layout.Repository],
            input, output, error);

        var marker = Path.Combine(layout.Repository, ".aec-version");
        Assert.Equal(confirm ? 0 : 1, exitCode);
        Assert.Equal(confirm, File.Exists(marker));
        Assert.Equal(confirm, output.ToString().Contains("Migration completed", StringComparison.Ordinal));
        if (confirm)
            Assert.Equal("2.0", File.ReadAllText(marker).Trim());
        else
            Assert.Equal(original, layout.Files.Head());
    }

    [Fact]
    public void PublicRetryPreservesAnExistingScaffoldDraftAndCompletes()
    {
        using var layout = new Layout();
        const string draft = "Reviewed shared instructions.\n";
        SeedScaffoldDraft(layout, draft);
        string[] args = ["migrate", "2.0", "--repo", layout.Repository];
        using var retryInput = new StringReader("Preserve\nDONE\n");
        using var retryOutput = new StringWriter();
        using var retryError = new StringWriter();

        var retryExit = AecApplication.Run(args, retryInput, retryOutput, retryError);

        Assert.True(retryExit == 0, retryError.ToString());
        Assert.Contains("type exactly DONE", retryOutput.ToString(), StringComparison.Ordinal);
        Assert.Contains("Migration completed at ", retryOutput.ToString(), StringComparison.Ordinal);
        Assert.Equal(draft, File.ReadAllText(Path.Combine(layout.Repository, SharedDraftPath)));
        Assert.Equal("2.0\n", File.ReadAllText(Path.Combine(layout.Repository, ".aec-version")));
        Assert.Equal(".aec-version", layout.Files.Git("diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD").Trim());
        Assert.Empty(layout.Files.Git("status", "--porcelain"));
    }

    [Fact]
    public void DoneRejectsAnInvalidUntrackedDraftWithoutChangingTheScaffold()
    {
        using var layout = new Layout();
        SeedScaffoldDraft(layout, "<!-- AEC:SOURCE:SHARED:BEGIN -->\n");
        var scaffold = new MigrationScaffoldResult(layout.Context(), []);
        using var input = new StringReader("DONE\n");
        using var output = new StringWriter();

        layout.Files.AssertNoChanges(() =>
        {
            var error = Assert.Throws<InvalidOperationException>(() =>
                MigrationPostDoneCoordinator.WaitAndComplete(scaffold, input, output));
            Assert.Contains("during sources", error.Message, StringComparison.Ordinal);
            var cause = Assert.IsType<InvalidDataException>(error.InnerException);
            Assert.Contains("AEC section marker", cause.Message, StringComparison.Ordinal);
        });
        Assert.Contains("type exactly DONE", output.ToString(), StringComparison.Ordinal);
    }

    private static void SeedScaffoldDraft(Layout layout, string draft)
    {
        // Model an existing, uncommitted scaffold from an interrupted migration.
        layout.Files.Write(SharedDraftPath, draft);
        layout.Files.Write($"environment/platforms/{layout.Platform}/policy.md", string.Empty);
        layout.Files.Write("environment/providers/codex/overlay.md", string.Empty);
        layout.Files.Write("environment/providers/copilot/overlay.md", string.Empty);
    }

    internal static void FreshInvocationRefusesUncommittedMarkerThenKeepsPriorCommitsOnRecovery()
    {
        // The fixture already has the committed targets and applied runtimes
        // that exist immediately before a migration publishes its marker.
        using var layout = new Layout("codex", "copilot");
        var dataHead = layout.Files.Head();
        var runtime = layout.Files.RuntimeBytes();
        var exclude = Path.Combine(layout.Repository, ".git", "info", "exclude");
        File.WriteAllText(exclude, "/.aec-version\n");
        var marker = Path.Combine(layout.Repository, ".aec-version");
        File.WriteAllText(marker, "2.0\n");
        string[] args = ["migrate", "2.0", "--repo", layout.Repository];
        Assert.Empty(layout.Files.Git("ls-tree", "HEAD", "--", ".aec-version"));
        using var blockedInput = new StringReader("DONE\n");
        using var blockedError = new StringWriter();

        layout.Files.AssertNoChanges(() =>
            Assert.Equal(1, AecApplication.Run(args, blockedInput, TextWriter.Null, blockedError)));
        Assert.Contains("Repository format marker", blockedError.ToString(), StringComparison.Ordinal);
        Assert.Equal("DONE", blockedInput.ReadLine());

        File.Delete(marker);
        File.Delete(exclude);
        using var retryInput = new StringReader("DONE\n");
        using var retryError = new StringWriter();
        var retryExit = AecApplication.Run(args, retryInput, TextWriter.Null, retryError);

        Assert.True(retryExit == 0, retryError.ToString());
        Assert.Equal(dataHead, layout.Files.Git("rev-parse", "HEAD^").Trim());
        Assert.Equal("2.0\n", File.ReadAllText(marker));
        Assert.Equal(".aec-version", layout.Files.Git(
            "diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD").Trim());
        Assert.Empty(layout.Files.Git("status", "--porcelain"));
        layout.Files.AssertRuntime(runtime);
    }

    private sealed class EditingReader(Layout layout, StringWriter output, bool confirm) : StringReader("")
    {
        public override string? ReadLine()
        {
            var messages = output.ToString();
            Assert.StartsWith("Close affected harness sessions", messages, StringComparison.Ordinal);
            Assert.Contains("type exactly DONE", messages, StringComparison.Ordinal);
            Assert.Equal(1, messages.Split("Close affected harness sessions", StringSplitOptions.None).Length - 1);
            var sections = new[]
            {
                "environment/shared/instructions.md",
                $"environment/platforms/{layout.Platform}/policy.md",
                "environment/providers/codex/overlay.md",
                "environment/providers/copilot/overlay.md"
            };
            foreach (var section in sections)
            {
                Assert.Equal("", File.ReadAllText(Path.Combine(layout.Repository, section)));
                if (confirm) layout.Files.Write(section, "Reviewed section.\n");
            }
            return confirm ? "DONE" : null;
        }
    }
}
