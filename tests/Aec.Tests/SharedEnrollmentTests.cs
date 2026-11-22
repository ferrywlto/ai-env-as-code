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

    [Theory]
    [InlineData("codex")]
    [InlineData("copilot")]
    public void ConfirmedCloneCanEnrollWithoutRewritingCanonicalRepositoryPath(string provider)
    {
        using var layout = new EnrollmentLayout();
        if (provider == "copilot")
        {
            layout.InitializeCopilot();
        }

        layout.CommitAuthoredSources(provider);
        var pulled = Path.Combine(Path.GetDirectoryName(layout.Repository)!, "pulled-data");
        var secondHome = Path.Combine(Path.GetDirectoryName(layout.Repository)!,
            $"second-{provider}-home");
        CloneWithoutConversion(layout.Repository, pulled);
        Directory.CreateDirectory(secondHome);
        var canonicalRelativePath = provider == "codex"
            ? AecApplication.SourceRelativePath
            : AecApplication.CopilotSourceRelativePath;
        var canonicalPath = Path.Combine(pulled, canonicalRelativePath);
        var canonicalBefore = File.ReadAllBytes(canonicalPath);
        var headBefore = TestGit.Run(pulled, "rev-parse", "HEAD").Output.Trim();
        var attached = provider == "codex"
            ? TestApplication.Run("init", "--repo", pulled, "--codex-home", secondHome,
                "--force-path-change")
            : TestApplication.Run("init", "--repo", pulled, "--provider=copilot",
                "--copilot-home", secondHome, "--force-path-change");
        Assert.True(attached.ExitCode == 0, attached.Error);
        var runtimePath = Path.Combine(secondHome, provider == "codex"
            ? "AGENTS.md" : "copilot-instructions.md");
        var runtimeBefore = File.ReadAllBytes(runtimePath);
        CommandResult EnrollPulled() => provider == "codex"
            ? TestApplication.Run("init", "--repo", pulled, "--codex-home", secondHome,
                "--enroll-shared")
            : TestApplication.Run("init", "--repo", pulled, "--provider=copilot",
                "--copilot-home", secondHome, "--enroll-shared");

        var enrollment = EnrollPulled();

        Assert.True(enrollment.ExitCode == 0, enrollment.Error);
        Assert.Equal($"enrolled {layout.TargetRelativePath(provider)}{Environment.NewLine}",
            enrollment.Output);
        Assert.True(File.Exists(Path.Combine(pulled, layout.TargetRelativePath(provider))));
        Assert.Equal(canonicalBefore, File.ReadAllBytes(canonicalPath));
        Assert.Equal(runtimeBefore, File.ReadAllBytes(runtimePath));
        Assert.Equal(headBefore, TestGit.Run(pulled, "rev-parse", "HEAD").Output.Trim());

        var targetPath = Path.Combine(pulled, layout.TargetRelativePath(provider));
        var targetBefore = File.ReadAllBytes(targetPath);
        File.AppendAllText(canonicalPath, "Uncommitted canonical edit.\n");
        var dirtyEnrollment = EnrollPulled();
        Assert.Equal(1, dirtyEnrollment.ExitCode);
        Assert.Contains("changes outside the selected enrollment target",
            dirtyEnrollment.Error, StringComparison.Ordinal);
        Assert.Equal(targetBefore, File.ReadAllBytes(targetPath));
        Assert.Equal(runtimeBefore, File.ReadAllBytes(runtimePath));
    }

    [Fact]
    public void EnrollmentRejectsARealRuntimeBoundToAnotherCheckout()
    {
        using var layout = new EnrollmentLayout(seedHistory: false);
        var otherCheckout = Path.Combine(Path.GetDirectoryName(layout.Repository)!, "other-data");
        layout.WriteCodexRuntime(otherCheckout);

        var enrollment = layout.Enroll("codex");

        Assert.Equal(1, enrollment.ExitCode);
        Assert.Contains("not bound to the selected AEC repository", enrollment.Error,
            StringComparison.Ordinal);
        Assert.False(File.Exists(layout.TargetPath("codex")));
    }

    [Fact]
    public void MissingSourcesExplainTheSplitWithoutCreatingTargets()
    {
        using var layout = new EnrollmentLayout(seedHistory: false);
        layout.WriteCodexRuntime(layout.Repository);

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
        using var layout = new EnrollmentLayout(seedHistory: false);

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

    private static void CloneWithoutConversion(string source, string destination)
    {
        // Clone does not inherit source-local config. Disable conversion before
        // checkout so a clean Windows clone also matches committed bytes exactly.
        var clone = TestGit.Run(Path.GetDirectoryName(source)!, "clone", "--quiet",
            "--config", "core.autocrlf=false", source, destination);
        Assert.True(clone.ExitCode == 0, clone.Error);
    }

    private sealed class EnrollmentLayout : IDisposable
    {
        private readonly string root;
        private readonly string platform = RenderCommand.CurrentPlatform();

        public EnrollmentLayout(bool seedHistory = true)
        {
            root = Path.Combine(TestTemporaryDirectory.Root,
                "aec-enrollment-tests", Guid.NewGuid().ToString("N"));
            Repository = Path.Combine(root, "data");
            CodexHome = Path.Combine(root, "codex-home");
            CopilotHome = Path.Combine(root, "copilot-home");
            CodexRuntime = Path.Combine(CodexHome, "AGENTS.md");
            CopilotRuntime = Path.Combine(CopilotHome, "copilot-instructions.md");
            SharedSource = Path.Combine(Repository, "environment", "shared", "instructions.md");

            Directory.CreateDirectory(CodexHome);
            Directory.CreateDirectory(Repository);
            if (seedHistory)
                TestGit.SeedInitializedCodexRepository(Repository, CodexHome,
                    "Personal Codex instructions.\n", "personality = \"none\"\n");
        }

        public string Repository { get; }
        public string CodexHome { get; }
        public string CopilotHome { get; }
        public string CodexRuntime { get; }
        public string CopilotRuntime { get; }
        public string SharedSource { get; }

        public void WriteCodexRuntime(string repository) =>
            File.WriteAllBytes(CodexRuntime, AecInstructionBlock.Merge("Personal Codex instructions.\n"u8.ToArray(), repository));

        public void InitializeCopilot()
        {
            Directory.CreateDirectory(CopilotHome);
            var source = Path.Combine(Repository, AecApplication.CopilotSourceRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(source)!);
            var content = CopilotInstructionBlock.Merge("Personal Copilot instructions.\n"u8.ToArray(), Repository);
            File.WriteAllBytes(CopilotRuntime, content);
            File.WriteAllBytes(source, content);
            GitProcess.RunRequired(Repository, "Could not stage Copilot baseline", "add", "--", AecApplication.CopilotSourceRelativePath);
            GitProcess.RunRequired(Repository, "Could not commit Copilot baseline", "commit", "--quiet", "-m", "Fixture legacy Copilot enrollment");
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
