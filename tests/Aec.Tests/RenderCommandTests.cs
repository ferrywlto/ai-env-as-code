using System.Text;

namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class RenderCommandTests
{
    [Fact]
    public void RendersOnlyEnrolledProvidersWithoutChangingGitOrRuntime()
    {
        using var layout = new RenderLayout(enrollCopilot: true);
        var head = GitProcess.RunRequired(layout.Repository, "Could not read HEAD", "rev-parse", "HEAD").Output;

        var first = TestApplication.Run("render", "--repo", layout.Repository);
        var second = TestApplication.Run("render", "--repo", layout.Repository);

        Assert.Equal(0, first.ExitCode);
        Assert.Contains("rendered environment/targets/", first.Output, StringComparison.Ordinal);
        Assert.Equal(0, second.ExitCode);
        Assert.Equal($"unchanged{Environment.NewLine}", second.Output);
        Assert.Equal(head, GitProcess.RunRequired(layout.Repository, "Could not read HEAD", "rev-parse", "HEAD").Output);
        Assert.Equal("runtime stays untouched\n", File.ReadAllText(layout.Runtime));

        var codex = InstructionSections.Parse(File.ReadAllText(layout.CodexTarget), true, true);
        var copilot = InstructionSections.Parse(File.ReadAllText(layout.CopilotTarget), true, true);
        Assert.Equal("Shared approval.\n", codex.Shared);
        Assert.Equal(codex.Shared, copilot.Shared);
        Assert.Equal(codex.Platform, copilot.Platform);
        Assert.Equal("Codex models.\n", codex.Provider);
        Assert.Equal("Copilot guidance.\n", copilot.Provider);
    }

    [Fact]
    public void DoesNotCreateAnUnusedProviderTarget()
    {
        using var layout = new RenderLayout(enrollCopilot: false);

        var result = TestApplication.Run("render", "--repo", layout.Repository);

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(layout.CodexTarget));
        Assert.False(Directory.Exists(Path.GetDirectoryName(layout.CopilotTarget)));
    }

    [Fact]
    public void DirtyTargetStopsBeforeAnyOtherTargetIsWritten()
    {
        using var layout = new RenderLayout(enrollCopilot: true);
        Assert.Equal(0, TestApplication.Run("render", "--repo", layout.Repository).ExitCode);
        GitProcess.RunRequired(layout.Repository, "Could not commit baseline", "add", "--", "environment");
        GitProcess.RunRequired(layout.Repository, "Could not commit baseline", "commit", "-m", "Render baseline");

        File.WriteAllText(layout.CodexTarget, "personal target edit\n");
        File.WriteAllText(layout.SharedSource, "Updated shared approval.\n");
        var copilotBefore = File.ReadAllBytes(layout.CopilotTarget);

        var result = TestApplication.Run("render", "--repo", layout.Repository);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("Rendered target has unstaged changes", result.Error, StringComparison.Ordinal);
        Assert.Equal(copilotBefore, File.ReadAllBytes(layout.CopilotTarget));
        Assert.Equal("personal target edit\n", File.ReadAllText(layout.CodexTarget));
    }

    [Fact]
    public void RejectsMissingSourceWithoutWritingATarget()
    {
        using var layout = new RenderLayout(enrollCopilot: false);
        File.Delete(layout.SharedSource);

        var result = TestApplication.Run("render", "--repo", layout.Repository);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.False(File.Exists(layout.CodexTarget));
    }

    private sealed class RenderLayout : IDisposable
    {
        private readonly string root;

        public RenderLayout(bool enrollCopilot)
        {
            root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(),
                "aec-render-tests", Guid.NewGuid().ToString("N"));
            Repository = Path.Combine(root, "data");
            var platform = OperatingSystem.IsMacOS() ? "macos-arm64" :
                OperatingSystem.IsWindows() ? "windows-x64" : "linux-arm64";
            SharedSource = Path.Combine(Repository, "environment", "shared", "instructions.md");
            CodexTarget = Path.Combine(Repository, "environment", "targets", platform,
                "codex", "AGENTS.md");
            CopilotTarget = Path.Combine(Repository, "environment", "targets", platform,
                "copilot", "copilot-instructions.md");
            Runtime = Path.Combine(root, "runtime", "AGENTS.md");

            Directory.CreateDirectory(Path.GetDirectoryName(SharedSource)!);
            Directory.CreateDirectory(Path.GetDirectoryName(CodexTarget)!);
            Directory.CreateDirectory(Path.GetDirectoryName(Runtime)!);
            Directory.CreateDirectory(Path.Combine(Repository, "environment", "platforms", platform));
            Directory.CreateDirectory(Path.Combine(Repository, "environment", "providers", "codex"));
            if (enrollCopilot)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(CopilotTarget)!);
                Directory.CreateDirectory(Path.Combine(Repository, "environment", "providers", "copilot"));
            }

            File.WriteAllText(SharedSource, "Shared approval.\n", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(Repository, "environment", "platforms", platform,
                "policy.md"), "Local paths.\n", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(Repository, "environment", "providers", "codex",
                "overlay.md"), "Codex models.\n", new UTF8Encoding(false));
            if (enrollCopilot)
            {
                File.WriteAllText(Path.Combine(Repository, "environment", "providers", "copilot",
                    "overlay.md"), "Copilot guidance.\n", new UTF8Encoding(false));
            }

            File.WriteAllText(Runtime, "runtime stays untouched\n");
            GitProcess.RunRequired(Repository, "Could not initialize Git", "init", "--quiet");
            GitProcess.RunRequired(Repository, "Could not stage sources", "add", "--", "environment");
            GitProcess.RunRequired(Repository, "Could not commit sources", "commit", "-m", "Source baseline");
        }

        public string Repository { get; }
        public string SharedSource { get; }
        public string CodexTarget { get; }
        public string CopilotTarget { get; }
        public string Runtime { get; }

        public void Dispose()
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
