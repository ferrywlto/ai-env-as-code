using System.Text;

namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class LegacyMigrationTests
{
    [Fact]
    public void ReviewedLegacySplitEnrollsBothProvidersAndRerunsWithoutDataLoss()
    {
        using var layout = new LegacyLayout();
        var legacyHead = layout.Head();
        var preserved = LegacyLayout.PreservedPaths.ToDictionary(path => path,
            path => File.ReadAllBytes(Path.Combine(layout.Repository, path)));
        var runtimeBefore = layout.RuntimePaths.ToDictionary(path => path, File.ReadAllBytes);

        // These identify the released 1.x block formats, not CLI release numbers.
        Assert.Equal(6, AecInstructionBlock.ReadRepositoryBinding(
            preserved[AecApplication.SourceRelativePath])!.Version);
        Assert.Equal(1, CopilotInstructionBlock.ReadManagedBinding(
            preserved[AecApplication.CopilotSourceRelativePath])!.Version);
        Assert.False(Directory.Exists(Path.Combine(layout.Repository, "environment", "targets")));

        layout.WriteReviewedSplit();
        layout.Commit("Review legacy instruction split", layout.SourcePaths);
        var sourceCommit = layout.Head();
        foreach (var provider in LegacyLayout.Providers)
        {
            Assert.Equal(1, layout.Run(provider, "status").ExitCode);
        }

        Success(layout.Run("codex", "init", "--enroll-shared"));
        Assert.Equal("unchanged", Success(layout.Run("codex", "init", "--enroll-shared"))
            .Output.Trim());
        Assert.Equal(sourceCommit, layout.Head());
        Assert.Empty(layout.Git("diff", "--cached", "--name-only"));
        Assert.False(File.Exists(layout.TargetPath("copilot")));

        // Enrollment permits only its own uncommitted target. Review and commit
        // one provider before enrolling the next, rather than mixing checkpoints.
        var prematureCopilot = layout.Run("copilot", "init", "--enroll-shared");
        Assert.Equal(1, prematureCopilot.ExitCode);
        Assert.Contains("changes outside the selected enrollment target",
            prematureCopilot.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(layout.TargetPath("copilot")));
        layout.Commit("Enroll reviewed Codex target", layout.TargetRelativePath("codex"));

        Success(layout.Run("copilot", "init", "--enroll-shared"));
        Assert.Empty(layout.Git("diff", "--cached", "--name-only"));
        layout.Commit("Enroll reviewed Copilot target", layout.TargetRelativePath("copilot"));
        var migrationHead = layout.Head();
        foreach (var (path, bytes) in runtimeBefore)
        {
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }

        foreach (var provider in LegacyLayout.Providers)
        {
            // The explicit fixture split accounts for every non-AEC byte in
            // each original instruction body, including shared text only once.
            var target = File.ReadAllText(layout.TargetPath(provider));
            var sections = InstructionSections.Parse(target, true, true);
            Assert.Equal(LegacyLayout.OriginalInstructions(provider),
                sections.Shared + sections.Platform + sections.Provider);
            Assert.Equal(2, layout.Run(provider, "status").ExitCode);
            Assert.Equal("applied", Success(layout.Run(provider, "apply")).Output.Trim());
            Success(layout.Run(provider, "status"));
            Assert.EndsWith(target, File.ReadAllText(layout.RuntimePath(provider)),
                StringComparison.Ordinal);
        }

        var codexBinding = AecInstructionBlock.ReadRepositoryBinding(
            File.ReadAllBytes(layout.RuntimePath("codex")))!;
        Assert.True(AecInstructionBlock.IncludesChatGptGuidance(codexBinding.Version));
        Assert.Contains(Path.Combine(layout.Repository, "environment", "providers", "chatgpt")
            .Replace('\\', '/'), File.ReadAllText(layout.RuntimePath("codex"))
            .Replace('\\', '/'), StringComparison.Ordinal);
        var runtimeAfter = layout.RuntimePaths.ToDictionary(path => path, File.ReadAllBytes);

        foreach (var provider in LegacyLayout.Providers)
        {
            Assert.Equal("unchanged", Success(layout.Run(provider, "init", "--enroll-shared"))
                .Output.Trim());
            Success(layout.Run(provider, "init"));
            Assert.Equal("unchanged", Success(layout.Run(provider, "apply")).Output.Trim());
            Assert.Equal("unchanged", Success(layout.Run(provider, "backup")).Output.Trim());
            Success(layout.Run(provider, "status"));
        }
        Assert.Equal("unchanged", Success(TestApplication.Run("render", "--repo",
            layout.Repository)).Output.Trim());

        foreach (var (path, bytes) in preserved)
        {
            Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(layout.Repository, path)));
        }
        foreach (var (path, bytes) in runtimeAfter)
        {
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }
        Assert.Equal(runtimeBefore[layout.CodexConfig], File.ReadAllBytes(layout.CodexConfig));
        Assert.Equal(runtimeBefore[layout.CopilotConfig], File.ReadAllBytes(layout.CopilotConfig));
        Assert.Equal(migrationHead, layout.Head());
        Assert.Equal(0, TestGit.Run(layout.Repository, "merge-base", "--is-ancestor",
            legacyHead, migrationHead).ExitCode);
        Assert.Empty(layout.Git("status", "--porcelain"));
        Assert.Equal([layout.Platform], Directory.GetDirectories(Path.Combine(layout.Repository,
            "environment", "targets")).Select(Path.GetFileName));
        Assert.Equal(LegacyLayout.Providers, Directory.GetDirectories(Path.Combine(layout.Repository,
            "environment", "targets", layout.Platform)).Select(Path.GetFileName)
            .Order(StringComparer.Ordinal));
    }

    private static CommandResult Success(CommandResult result)
    {
        Assert.True(result.ExitCode == 0, result.Error);
        return result;
    }

    private sealed class LegacyLayout : IDisposable
    {
        internal static readonly string[] Providers = ["codex", "copilot"];
        private const string Shared = "## Collaboration\nAsk before changing instructions.\n\n";
        private const string Policy = "## Local paths\nUse only the approved workspace.\n\n";
        private const string CodexOverlay = "## Codex\nUse the selected local Codex models.\n";
        private const string CopilotOverlay = "## Copilot\nUse the local Copilot CLI tools.\n";
        private readonly string root;

        public LegacyLayout()
        {
            root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(),
                "aec-legacy-migration-tests", Guid.NewGuid().ToString("N"));
            Repository = Path.Combine(root, "data");
            foreach (var provider in Providers)
            {
                Directory.CreateDirectory(Home(provider));
                File.WriteAllText(RuntimePath(provider), OriginalInstructions(provider));
            }
            File.WriteAllText(CodexConfig,
                "model = \"keep-local-model\"\npersonality = 'friendly'\n" +
                "[features]\nlocal_setting = true\n");
            File.WriteAllText(CopilotConfig, "{\"localSetting\":true}\n");

            // Ordinary unenrolled init still writes the historical 1.x layout.
            // Assert that shape above so this test cannot silently start in v2.
            Success(Run("codex", "init"));
            Success(TestApplication.Run("init", "--repo", Repository, "--provider=chatgpt"));
            foreach (var fileName in ChatGptInitCommand.ScaffoldFileNames)
            {
                File.WriteAllBytes(Path.Combine(Repository, "environment", "providers", "chatgpt",
                    fileName), Encoding.UTF8.GetBytes($"# Manual {fileName}\r\nKeep this backup.\r\n"));
            }
            Success(Run("copilot", "init"));
            Commit("Record 1.x providers and manual backups", PreservedPaths);
            Success(Run("codex", "init"));
            Assert.Equal("personality = \"friendly\"\n", File.ReadAllText(Path.Combine(
                Repository, AecApplication.ConfigSourceRelativePath)));
            Assert.Empty(Git("status", "--porcelain"));
        }

        public string Repository { get; }
        public string Platform { get; } = RenderCommand.CurrentPlatform();
        public string CodexConfig => Path.Combine(Home("codex"), "config.toml");
        public string CopilotConfig => Path.Combine(Home("copilot"), "config.json");
        public string[] RuntimePaths => [RuntimePath("codex"), RuntimePath("copilot"),
            CodexConfig, CopilotConfig];
        public static string[] PreservedPaths => [AecApplication.SourceRelativePath,
            AecApplication.CopilotSourceRelativePath, AecApplication.ConfigSourceRelativePath,
            .. ChatGptInitCommand.ScaffoldFileNames.Select(name =>
                $"environment/providers/chatgpt/{name}")];
        public string[] SourcePaths => ["environment/shared/instructions.md",
            $"environment/platforms/{Platform}/policy.md",
            "environment/providers/codex/overlay.md", "environment/providers/copilot/overlay.md"];

        public static string OriginalInstructions(string provider) => Shared + Policy +
            (provider == "codex" ? CodexOverlay : CopilotOverlay);
        public string RuntimePath(string provider) => Path.Combine(Home(provider),
            provider == "codex" ? "AGENTS.md" : "copilot-instructions.md");
        public string TargetRelativePath(string provider) =>
            $"environment/targets/{Platform}/{provider}/" +
            (provider == "codex" ? "AGENTS.md" : "copilot-instructions.md");
        public string TargetPath(string provider) => Path.Combine(Repository,
            TargetRelativePath(provider));
        private string Home(string provider) => Path.Combine(root, provider + "-home");

        public CommandResult Run(string provider, string command, params string[] options) =>
            provider == "codex"
                ? TestApplication.Run([command, "--repo", Repository,
                    "--codex-home", Home(provider), .. options])
                : TestApplication.Run([command, "--repo", Repository, "--provider=copilot",
                    "--copilot-home", Home(provider), .. options]);

        public void WriteReviewedSplit()
        {
            var content = new[] { Shared, Policy, CodexOverlay, CopilotOverlay };
            for (var index = 0; index < SourcePaths.Length; index++)
            {
                var path = Path.Combine(Repository, SourcePaths[index]);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, content[index]);
            }
        }

        public void Commit(string message, params string[] paths)
        {
            Git(["add", "--", .. paths]);
            Git("commit", "--quiet", "--message", message);
        }
        public string Head() => Git("rev-parse", "HEAD").Trim();
        public string Git(params string[] arguments) => GitProcess.RunRequired(
            Repository, "Migration fixture Git operation failed", arguments).Output;
        public void Dispose() => TestDirectoryCleanup.Delete(root);
    }
}
