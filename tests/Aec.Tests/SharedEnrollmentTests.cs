namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class SharedEnrollmentTests
{
    [Fact]
    public void CodexEnrollmentCreatesOnlyItsTargetWithoutRuntimeOrCommitChanges()
    {
        using var layout = new EnrollmentLayout();
        layout.CommitAuthoredSources("codex");
        var head = layout.Head();
        var runtime = File.ReadAllBytes(layout.CodexRuntime);

        var first = layout.Enroll("codex");
        var second = layout.Enroll("codex");

        Assert.Equal(0, first.ExitCode);
        Assert.Equal($"enrolled {layout.TargetRelativePath("codex")}{Environment.NewLine}", first.Output);
        Assert.Equal(0, second.ExitCode);
        Assert.Equal($"unchanged{Environment.NewLine}", second.Output);
        Assert.Equal(head, layout.Head());
        Assert.Equal(runtime, File.ReadAllBytes(layout.CodexRuntime));
        Assert.False(Directory.Exists(Path.GetDirectoryName(layout.TargetPath("copilot"))));

        var sections = InstructionSections.Parse(File.ReadAllText(layout.TargetPath("codex")), true, true);
        Assert.Equal("Shared approval.\n", sections.Shared);
        Assert.Equal("Local paths.\n", sections.Platform);
        Assert.Equal("Codex mechanics.\n", sections.Provider);
    }

    [Fact]
    public void CopilotEnrollmentCreatesOnlyItsTarget()
    {
        using var layout = new EnrollmentLayout();
        layout.InitializeCopilot();
        layout.CommitAuthoredSources("copilot");
        var head = layout.Head();
        var runtime = File.ReadAllBytes(layout.CopilotRuntime);

        var result = layout.Enroll("copilot");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(head, layout.Head());
        Assert.Equal(runtime, File.ReadAllBytes(layout.CopilotRuntime));
        Assert.False(Directory.Exists(Path.GetDirectoryName(layout.TargetPath("codex"))));
        Assert.Equal(
            "Copilot mechanics.\n",
            InstructionSections.Parse(File.ReadAllText(layout.TargetPath("copilot")), true, true).Provider);
    }

    [Fact]
    public void MissingSourcesExplainTheSplitWithoutCreatingTargets()
    {
        using var layout = new EnrollmentLayout();

        var result = layout.Enroll("codex");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Shared means every enrolled harness", result.Error, StringComparison.Ordinal);
        Assert.Contains("AEC will not split existing instructions automatically", result.Error,
            StringComparison.Ordinal);
        Assert.False(Directory.Exists(Path.GetDirectoryName(layout.TargetPath("codex"))));
    }

    [Fact]
    public void MissingProviderRuntimeStopsWithoutEnrollment()
    {
        using var layout = new EnrollmentLayout();
        layout.CommitAuthoredSources("codex");
        File.Delete(layout.CodexRuntime);

        var result = layout.Enroll("codex");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Provider runtime instructions does not exist", result.Error,
            StringComparison.Ordinal);
        Assert.False(File.Exists(layout.TargetPath("codex")));
    }

    [Fact]
    public void UncommittedSourceStopsBeforeTargetCreation()
    {
        using var layout = new EnrollmentLayout();
        layout.CommitAuthoredSources("codex");
        File.AppendAllText(layout.SharedSource, "Unreviewed edit.\n");

        var result = layout.Enroll("codex");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("unstaged changes", result.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(layout.TargetPath("codex")));
    }

    [Fact]
    public void UnrelatedRepositoryChangeStopsBeforeTargetCreation()
    {
        using var layout = new EnrollmentLayout();
        layout.CommitAuthoredSources("codex");
        File.WriteAllText(Path.Combine(layout.Repository, "other.txt"), "unrelated\n");

        var result = layout.Enroll("codex");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("changes outside the selected enrollment target", result.Error,
            StringComparison.Ordinal);
        Assert.False(File.Exists(layout.TargetPath("codex")));
    }

    [Fact]
    public void ExistingDifferentTargetIsNeverOverwritten()
    {
        using var layout = new EnrollmentLayout();
        layout.CommitAuthoredSources("codex");
        Directory.CreateDirectory(Path.GetDirectoryName(layout.TargetPath("codex"))!);
        File.WriteAllText(layout.TargetPath("codex"), "personal target edit\n");

        var result = layout.Enroll("codex");

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Enrolled target differs", result.Error, StringComparison.Ordinal);
        Assert.Equal("personal target edit\n", File.ReadAllText(layout.TargetPath("codex")));
    }

    [Theory]
    [InlineData("--provider=chatgpt", "--enroll-shared", "only local Codex or Copilot")]
    [InlineData("--force-path-change", "--enroll-shared", "cannot be combined")]
    [InlineData("--enroll-shared", "--enroll-shared", "only once")]
    public void RejectsUnsupportedEnrollmentArguments(string first, string second, string message)
    {
        using var layout = new EnrollmentLayout();

        var result = TestApplication.Run("init", "--repo", layout.Repository, first, second);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(message, result.Error, StringComparison.Ordinal);
    }

    private sealed class EnrollmentLayout : IDisposable
    {
        private readonly string root;
        private readonly string platform = RenderCommand.CurrentPlatform();

        public EnrollmentLayout()
        {
            root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(),
                "aec-enrollment-tests", Guid.NewGuid().ToString("N"));
            Repository = Path.Combine(root, "data");
            CodexHome = Path.Combine(root, "codex-home");
            CopilotHome = Path.Combine(root, "copilot-home");
            CodexRuntime = Path.Combine(CodexHome, "AGENTS.md");
            CopilotRuntime = Path.Combine(CopilotHome, "copilot-instructions.md");
            SharedSource = Path.Combine(Repository, "environment", "shared", "instructions.md");

            Directory.CreateDirectory(CodexHome);
            Directory.CreateDirectory(CopilotHome);
            File.WriteAllText(CodexRuntime, "Personal Codex instructions.\n");
            File.WriteAllText(Path.Combine(CodexHome, "config.toml"), "personality = \"none\"\n");
            File.WriteAllText(CopilotRuntime, "Personal Copilot instructions.\n");

            var init = TestApplication.Run("init", "--repo", Repository, "--codex-home", CodexHome);
            Assert.Equal(0, init.ExitCode);
        }

        public string Repository { get; }
        public string CodexHome { get; }
        public string CopilotHome { get; }
        public string CodexRuntime { get; }
        public string CopilotRuntime { get; }
        public string SharedSource { get; }

        public void InitializeCopilot()
        {
            var init = TestApplication.Run(
                "init", "--repo", Repository, "--provider=copilot", "--copilot-home", CopilotHome);
            Assert.Equal(0, init.ExitCode);
        }

        public void CommitAuthoredSources(string provider)
        {
            var platformSource = Path.Combine(Repository, "environment", "platforms", platform,
                "policy.md");
            var overlaySource = Path.Combine(Repository, "environment", "providers", provider,
                "overlay.md");
            Directory.CreateDirectory(Path.GetDirectoryName(SharedSource)!);
            Directory.CreateDirectory(Path.GetDirectoryName(platformSource)!);
            Directory.CreateDirectory(Path.GetDirectoryName(overlaySource)!);
            File.WriteAllText(SharedSource, "Shared approval.\n");
            File.WriteAllText(platformSource, "Local paths.\n");
            File.WriteAllText(overlaySource, provider == "codex"
                ? "Codex mechanics.\n" : "Copilot mechanics.\n");
            GitProcess.RunRequired(Repository, "Could not stage authored sources", "add", "--", "environment");
            GitProcess.RunRequired(Repository, "Could not commit authored sources", "commit", "-m",
                "Review authored sources");
        }

        public CommandResult Enroll(string provider) => provider == "codex"
            ? TestApplication.Run(
                "init", "--repo", Repository, "--codex-home", CodexHome, "--enroll-shared")
            : TestApplication.Run(
                "init", "--repo", Repository, "--provider=copilot", "--copilot-home", CopilotHome,
                "--enroll-shared");

        public string Head() => GitProcess.RunRequired(
            Repository, "Could not read HEAD", "rev-parse", "HEAD").Output.Trim();

        public string TargetRelativePath(string provider) => provider == "codex"
            ? $"environment/targets/{platform}/codex/AGENTS.md"
            : $"environment/targets/{platform}/copilot/copilot-instructions.md";

        public string TargetPath(string provider) => Path.Combine(Repository, TargetRelativePath(provider));

        public void Dispose()
        {
            TestDirectoryCleanup.Delete(root);
        }
    }
}
