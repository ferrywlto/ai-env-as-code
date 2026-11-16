using System.Text;

namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class CommittedTargetValidatorTests
{
    [Theory]
    [InlineData("codex")]
    [InlineData("copilot")]
    public void ReadsAnExactCommittedTargetWithoutChangingTheRepository(string provider)
    {
        using var layout = new TargetLayout(provider);
        var head = layout.Head();
        var expected = File.ReadAllBytes(layout.TargetPath);

        var actual = CommittedTargetValidator.Read(layout.Repository, layout.Platform, provider);

        Assert.Equal(expected, actual);
        Assert.Equal(head, layout.Head());
        Assert.Empty(GitProcess.RunRequired(layout.Repository, "Could not inspect status",
            "status", "--porcelain").Output);
    }

    [Fact]
    public void RejectsAMissingTarget()
    {
        using var layout = new TargetLayout("codex");
        File.Delete(layout.TargetPath);

        var error = Assert.Throws<FileNotFoundException>(() =>
            CommittedTargetValidator.Read(layout.Repository, layout.Platform, "codex"));

        Assert.Contains("Committed target does not exist", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAnUntrackedTarget()
    {
        using var layout = new TargetLayout("codex", commitTarget: false);

        var error = Assert.Throws<InvalidOperationException>(() =>
            CommittedTargetValidator.Read(layout.Repository, layout.Platform, "codex"));

        Assert.Contains("not a single committed Git file", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsATargetStaleAgainstCommittedSources()
    {
        using var layout = new TargetLayout("codex");
        File.WriteAllText(layout.SharedSource, "Updated shared approval.\n");
        layout.Commit("environment/shared/instructions.md");

        var error = Assert.Throws<InvalidDataException>(() =>
            CommittedTargetValidator.Read(layout.Repository, layout.Platform, "codex"));

        Assert.Contains("does not match committed authored sources", error.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, "unstaged changes")]
    [InlineData(true, "staged changes")]
    public void RejectsAnUncommittedTargetEdit(bool staged, string expectedReason)
    {
        using var layout = new TargetLayout("codex");
        File.AppendAllText(layout.TargetPath, "Personal target edit.\n");
        if (staged)
        {
            GitProcess.RunRequired(layout.Repository, "Could not stage target edit",
                "add", "--", layout.TargetRelativePath);
        }

        var error = Assert.Throws<InvalidOperationException>(() =>
            CommittedTargetValidator.Read(layout.Repository, layout.Platform, "codex"));

        Assert.Contains($"Committed target has {expectedReason}", error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAnUncommittedAuthoredSource()
    {
        using var layout = new TargetLayout("codex");
        File.AppendAllText(layout.SharedSource, "Unreviewed change.\n");

        var error = Assert.Throws<InvalidOperationException>(() =>
            CommittedTargetValidator.Read(layout.Repository, layout.Platform, "codex"));

        Assert.Contains("Authored shared instructions has unstaged changes", error.Message,
            StringComparison.Ordinal);
    }

    private sealed class TargetLayout : IDisposable
    {
        private readonly string root;

        public TargetLayout(string provider, bool commitTarget = true)
        {
            root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(),
                "aec-committed-target-tests", Guid.NewGuid().ToString("N"));
            Repository = Path.Combine(root, "data");
            Platform = RenderCommand.CurrentPlatform();
            SharedSource = Path.Combine(Repository, "environment", "shared", "instructions.md");
            var policy = Path.Combine(Repository, "environment", "platforms", Platform,
                "policy.md");
            var overlay = Path.Combine(Repository, "environment", "providers", provider,
                "overlay.md");
            TargetRelativePath = $"environment/targets/{Platform}/{provider}/" +
                (provider == "codex" ? "AGENTS.md" : "copilot-instructions.md");
            TargetPath = Path.Combine(Repository, TargetRelativePath);

            foreach (var path in new[] { SharedSource, policy, overlay, TargetPath })
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            }

            var utf8 = new UTF8Encoding(false);
            File.WriteAllText(SharedSource, "Shared approval.\n", utf8);
            File.WriteAllText(policy, "Local paths.\n", utf8);
            File.WriteAllText(overlay, "Provider mechanics.\n", utf8);
            File.WriteAllText(TargetPath, InstructionSections.Render(
                "Shared approval.\n", "Local paths.\n", "Provider mechanics.\n"), utf8);

            GitProcess.RunRequired(Repository, "Could not initialize Git", "init", "--quiet");
            if (commitTarget)
            {
                Commit("environment");
            }
            else
            {
                Commit("environment/shared", "environment/platforms", "environment/providers");
            }
        }

        public string Repository { get; }
        public string Platform { get; }
        public string SharedSource { get; }
        public string TargetRelativePath { get; }
        public string TargetPath { get; }

        public string Head() =>
            GitProcess.RunRequired(Repository, "Could not read HEAD", "rev-parse", "HEAD")
                .Output.Trim();

        public void Commit(params string[] paths)
        {
            GitProcess.RunRequired(Repository, "Could not stage fixture files",
                ["add", "--", .. paths]);
            GitProcess.RunRequired(Repository, "Could not commit fixture files",
                "-c", "user.name=AEC Test", "-c", "user.email=aec-test@example.invalid",
                "-c", "commit.gpgsign=false", "commit", "-m", "Fixture baseline");
        }

        public void Dispose() => TestDirectoryCleanup.Delete(root);
    }
}
