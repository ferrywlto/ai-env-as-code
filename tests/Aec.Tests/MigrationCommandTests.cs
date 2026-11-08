using Layout = Aec.Tests.MigrationPortableCapturePreviewTests.Layout;

namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class MigrationCommandTests
{
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FreshInvocationPreservesDraftsAndCompletesAfterInterruption(bool invalidDraft)
    {
        using var layout = new Layout();
        const string shared = "environment/shared/instructions.md";
        const string validDraft = "Reviewed shared instructions.\n";
        var draft = invalidDraft ? "<!-- AEC:SOURCE:SHARED:BEGIN -->\n" : validDraft;
        layout.Files.Write(shared, draft);
        var original = layout.Files.Head();
        var runtime = layout.Files.RuntimeBytes();
        string[] args = ["migrate", "2.0", "--repo", layout.Repository];
        using var firstInput = new StringReader(invalidDraft ? "Preserve\nDONE\n" : "Preserve\n");
        using var firstOutput = new StringWriter();
        using var firstError = new StringWriter();

        Assert.Equal(1, AecApplication.Run(args, firstInput, firstOutput, firstError));
        Assert.Contains("type exactly DONE", firstOutput.ToString(), StringComparison.Ordinal);
        Assert.Contains(invalidDraft ? "sources" : "stopped before completion",
            firstError.ToString(), StringComparison.Ordinal);
        Assert.Equal(original, layout.Files.Head());
        Assert.Empty(layout.Files.Git("diff", "--cached", "--name-only"));
        Assert.False(File.Exists(Path.Combine(layout.Repository, ".aec-version")));
        Assert.Equal(draft, File.ReadAllText(Path.Combine(layout.Repository, shared)));
        Assert.True(File.Exists(Path.Combine(layout.Repository, "environment/providers/codex/overlay.md")));
        layout.Files.AssertRuntime(runtime);

        if (invalidDraft) layout.Files.Write(shared, validDraft);
        using var retryInput = new StringReader("Preserve\nDONE\n");
        using var retryOutput = new StringWriter();
        using var retryError = new StringWriter();

        var retryExit = AecApplication.Run(args, retryInput, retryOutput, retryError);

        Assert.True(retryExit == 0, retryError.ToString());
        Assert.Contains("Migration completed at ", retryOutput.ToString(), StringComparison.Ordinal);
        Assert.Equal(validDraft, File.ReadAllText(Path.Combine(layout.Repository, shared)));
        Assert.Equal("2.0\n", File.ReadAllText(Path.Combine(layout.Repository, ".aec-version")));
        Assert.Equal(".aec-version", layout.Files.Git("diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD").Trim());
        Assert.Empty(layout.Files.Git("status", "--porcelain"));
    }

    [Fact]
    public void FreshInvocationRefusesUncommittedMarkerThenKeepsPriorCommitsOnRecovery()
    {
        using var layout = new Layout();
        layout.Files.Write("environment/shared/instructions.md", "Reviewed shared instructions.\n");
        var exclude = Path.Combine(layout.Repository, ".git", "info", "exclude");
        File.WriteAllText(exclude, "/.aec-version\n");
        var marker = Path.Combine(layout.Repository, ".aec-version");
        string[] args = ["migrate", "2.0", "--repo", layout.Repository];
        using var firstInput = new StringReader("Preserve\nDONE\n");
        using var firstError = new StringWriter();

        Assert.Equal(1, AecApplication.Run(args, firstInput, TextWriter.Null, firstError));
        Assert.Contains("during completion", firstError.ToString(), StringComparison.Ordinal);
        Assert.Equal("2.0\n", File.ReadAllText(marker));
        Assert.Empty(layout.Files.Git("ls-tree", "HEAD", "--", ".aec-version"));
        var dataHead = layout.Files.Head();
        var runtime = layout.Files.RuntimeBytes();
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
