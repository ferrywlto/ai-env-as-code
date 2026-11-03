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
