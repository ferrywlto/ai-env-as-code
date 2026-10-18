using System.Text;

namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class PortableBackupTargetWriterTests
{
    [Theory]
    [InlineData("shared")]
    [InlineData("platform")]
    [InlineData("provider")]
    public void RendersEveryAffectedCommittedEnrollmentWithoutUsingInstalledHarnesses(
        string section)
    {
        using var layout = new Layout();
        layout.EditRuntime(section);
        var runtime = File.ReadAllBytes(layout.RuntimePath);
        var head = layout.Head();
        var before = layout.Targets.ToDictionary(path => path, File.ReadAllBytes);
        var (reviewed, attribution) = layout.WriteReviewedSource();

        var rendered = PortableBackupTargetWriter.Render(reviewed, attribution);

        var expected = layout.Targets.Where(path => section switch
        {
            "shared" => true,
            "platform" => layout.Relative(path).Contains(
                $"/{layout.LocalPlatform}/", StringComparison.Ordinal),
            "provider" => layout.Relative(path).Contains("/codex/", StringComparison.Ordinal),
            _ => false
        }).OrderBy(path => layout.Relative(path), StringComparer.Ordinal).ToArray();
        Assert.Equal(expected.Select(layout.Relative), rendered);
        foreach (var path in layout.Targets)
        {
            Assert.Equal(expected.Contains(path),
                !File.ReadAllBytes(path).AsSpan().SequenceEqual(before[path]));
            var relative = layout.Relative(path).Split('/');
            var parts = InstructionSections.Parse(File.ReadAllText(path), true, true);
            Assert.Equal(section == "shared" ? "Shared revision.\n" : "Shared baseline.\n",
                parts.Shared);
            Assert.Equal(section == "platform" && relative[2] == layout.LocalPlatform
                    ? $"Revised policy {relative[2]}.\n" : $"Policy {relative[2]}.\n",
                parts.Platform);
            Assert.Equal(section == "provider" && relative[3] == "codex"
                    ? "Revised overlay codex.\n" : $"Overlay {relative[3]}.\n",
                parts.Provider);
        }

        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(head, layout.Head());
        Assert.False(Directory.Exists(layout.CopilotHome));
    }

    [Fact]
    public void CopilotProviderEditRendersOnlyCopilotTargetsAcrossPlatforms()
    {
        using var layout = new Layout(localProvider: "copilot");
        layout.EditRuntime("provider");
        var beforeCodex = layout.Targets
            .Where(path => layout.Relative(path).Contains("/codex/", StringComparison.Ordinal))
            .ToDictionary(path => path, File.ReadAllBytes);
        var (reviewed, attribution) = layout.WriteReviewedSource();

        var rendered = PortableBackupTargetWriter.Render(reviewed, attribution);

        Assert.Equal(3, rendered.Count);
        Assert.All(rendered, path => Assert.Contains("/copilot/", path, StringComparison.Ordinal));
        Assert.All(rendered, path => Assert.Equal("Revised overlay copilot.\n",
            InstructionSections.Parse(
                File.ReadAllText(Path.Combine(layout.Repository, path)), true, true).Provider));
        foreach (var (path, bytes) in beforeCodex)
        {
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }

        Assert.False(Directory.Exists(layout.CodexHome));
    }

    [Fact]
    public void DoesNotCreateAnUnenrolledTarget()
    {
        using var layout = new Layout(enrollRemoteCopilot: false);
        layout.EditRuntime("shared");
        var (reviewed, attribution) = layout.WriteReviewedSource();

        var rendered = PortableBackupTargetWriter.Render(reviewed, attribution);

        Assert.Equal(5, rendered.Count);
        Assert.False(File.Exists(layout.Target(layout.OtherPlatform, "copilot")));
    }

    [Fact]
    public void DirtyAffectedTargetStopsBeforeAnyTargetWrite()
    {
        using var layout = new Layout();
        layout.EditRuntime("shared");
        var (reviewed, attribution) = layout.WriteReviewedSource();
        var untouched = layout.Targets.Where(path =>
            path != layout.Target(layout.OtherPlatform, "copilot"))
            .ToDictionary(path => path, File.ReadAllBytes);
        File.WriteAllText(layout.Target(layout.OtherPlatform, "copilot"),
            "Concurrent target edit.\n");

        Assert.Throws<InvalidOperationException>(() =>
            PortableBackupTargetWriter.Render(reviewed, attribution));

        foreach (var (path, bytes) in untouched)
        {
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }
    }

    [Fact]
    public void RuntimeChangedAfterSourceWriteStopsBeforeTargetWrite()
    {
        using var layout = new Layout();
        layout.EditRuntime("shared");
        var (reviewed, attribution) = layout.WriteReviewedSource();
        var before = layout.Targets.ToDictionary(path => path, File.ReadAllBytes);
        File.AppendAllText(layout.RuntimePath, "Later runtime edit.\n");

        Assert.Throws<InvalidOperationException>(() =>
            PortableBackupTargetWriter.Render(reviewed, attribution));

        foreach (var (path, bytes) in before)
        {
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }
    }

    [Fact]
    public void SourceChangedAfterSourceWriteStopsBeforeTargetWrite()
    {
        using var layout = new Layout();
        layout.EditRuntime("shared");
        var (reviewed, attribution) = layout.WriteReviewedSource();
        var before = layout.Targets.ToDictionary(path => path, File.ReadAllBytes);
        File.WriteAllText(layout.SharedSource, "Later source edit.\n");

        Assert.Throws<InvalidOperationException>(() =>
            PortableBackupTargetWriter.Render(reviewed, attribution));

        foreach (var (path, bytes) in before)
        {
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }
    }

    [Fact]
    public void HeadChangedAfterSourceWriteStopsBeforeTargetWrite()
    {
        using var layout = new Layout();
        layout.EditRuntime("shared");
        var (reviewed, attribution) = layout.WriteReviewedSource();
        var before = layout.Targets.ToDictionary(path => path, File.ReadAllBytes);
        layout.CommitEmpty();

        Assert.Throws<InvalidOperationException>(() =>
            PortableBackupTargetWriter.Render(reviewed, attribution));

        foreach (var (path, bytes) in before)
        {
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }
    }

    [Fact]
    public void StagedSourceStopsBeforeTargetWrite()
    {
        using var layout = new Layout();
        layout.EditRuntime("shared");
        var (reviewed, attribution) = layout.WriteReviewedSource();
        var before = layout.Targets.ToDictionary(path => path, File.ReadAllBytes);
        GitProcess.RunRequired(layout.Repository, "Could not stage changed fixture source",
            "add", "--", "environment/shared/instructions.md");

        Assert.Throws<InvalidOperationException>(() =>
            PortableBackupTargetWriter.Render(reviewed, attribution));

        foreach (var (path, bytes) in before)
        {
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }
    }

    [Fact]
    public void InconsistentCommittedTargetStopsBeforeAnyTargetWrite()
    {
        using var layout = new Layout();
        layout.CorruptAndCommitRemoteTarget();
        layout.EditRuntime("shared");
        var (reviewed, attribution) = layout.WriteReviewedSource();
        var before = layout.Targets.ToDictionary(path => path, File.ReadAllBytes);

        Assert.Throws<InvalidDataException>(() =>
            PortableBackupTargetWriter.Render(reviewed, attribution));

        foreach (var (path, bytes) in before)
        {
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }
    }

    private sealed class Layout : IDisposable
    {
        private readonly string root;

        public Layout(bool enrollRemoteCopilot = true, string localProvider = "codex")
        {
            root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(),
                "aec-backup-target-tests", Guid.NewGuid().ToString("N"));
            Repository = Path.Combine(root, "data");
            CodexHome = Path.Combine(root, "codex-home");
            CopilotHome = Path.Combine(root, "copilot-home");
            LocalProvider = localProvider;
            ProviderHome = localProvider == "codex" ? CodexHome : CopilotHome;
            RuntimePath = Path.Combine(ProviderHome,
                localProvider == "codex" ? "AGENTS.md" : "copilot-instructions.md");
            LocalPlatform = RenderCommand.CurrentPlatform();
            OtherPlatform = LocalPlatform == "macos-arm64" ? "windows-x64" : "macos-arm64";
            SharedSource = Path.Combine(Repository, "environment", "shared", "instructions.md");

            Write(SharedSource, "Shared baseline.\n");
            foreach (var platform in Platforms)
            {
                Write(Path.Combine(Repository, "environment", "platforms", platform,
                    "policy.md"), $"Policy {platform}.\n");
            }

            foreach (var provider in new[] { "codex", "copilot" })
            {
                Write(Path.Combine(Repository, "environment", "providers", provider,
                    "overlay.md"), $"Overlay {provider}.\n");
            }

            Targets = [];
            foreach (var platform in Platforms)
            {
                foreach (var provider in new[] { "codex", "copilot" })
                {
                    if (!enrollRemoteCopilot && platform == OtherPlatform && provider == "copilot")
                    {
                        continue;
                    }

                    var path = Target(platform, provider);
                    Write(path, InstructionSections.Render(
                        "Shared baseline.\n", $"Policy {platform}.\n", $"Overlay {provider}.\n"));
                    Targets.Add(path);
                }
            }

            GitProcess.RunRequired(Repository, "Could not initialize fixture Git", "init", "--quiet");
            GitProcess.RunRequired(Repository, "Could not stage fixture", "add", "--", "environment");
            GitProcess.RunRequired(Repository, "Could not commit fixture",
                "-c", "user.name=AEC Test", "-c", "user.email=aec-test@example.invalid",
                "-c", "commit.gpgsign=false", "commit", "-m", "Fixture baseline");

            var localTarget = File.ReadAllBytes(Target(LocalPlatform, localProvider));
            Directory.CreateDirectory(ProviderHome);
            var runtime = localProvider == "codex"
                ? AecInstructionBlock.MergeForPortableTarget(
                    localTarget, Repository, LocalPlatform, includeChatGptGuidance: false)
                : CopilotInstructionBlock.MergeForPortableTarget(
                    localTarget, Repository, LocalPlatform);
            File.WriteAllBytes(RuntimePath, runtime);
        }

        public string Repository { get; }
        public string CodexHome { get; }
        public string CopilotHome { get; }
        public string LocalProvider { get; }
        public string ProviderHome { get; }
        public string RuntimePath { get; }
        public string LocalPlatform { get; }
        public string OtherPlatform { get; }
        public string SharedSource { get; }
        public List<string> Targets { get; }

        private static readonly string[] Platforms =
            ["macos-arm64", "windows-x64", "linux-arm64"];

        public string Target(string platform, string provider) => Path.Combine(
            Repository, "environment", "targets", platform, provider,
            provider == "codex" ? "AGENTS.md" : "copilot-instructions.md");

        public string Relative(string path) =>
            Path.GetRelativePath(Repository, path).Replace('\\', '/');

        public void EditRuntime(string section)
        {
            var original = section switch
            {
                "shared" => "Shared baseline.",
                "platform" => $"Policy {LocalPlatform}.",
                "provider" => $"Overlay {LocalProvider}.",
                _ => throw new ArgumentOutOfRangeException(nameof(section))
            };
            File.WriteAllText(RuntimePath, File.ReadAllText(RuntimePath)
                .Replace(original, original.Replace("baseline", "revision", StringComparison.Ordinal)
                    .Replace("Policy", "Revised policy", StringComparison.Ordinal)
                    .Replace("Overlay", "Revised overlay", StringComparison.Ordinal),
                    StringComparison.Ordinal));
        }

        public (PortableRuntimeSnapshot, PortableBackupAttributionPreview) WriteReviewedSource()
        {
            var reviewed = PortableRuntimeSnapshot.Read(Repository, LocalProvider, ProviderHome);
            var attribution = PortableBackupAttributionPreview.Create(
                Repository, LocalProvider, ProviderHome);
            PortableBackupSourceWriter.Write(reviewed, attribution);
            return (reviewed, attribution);
        }

        public void CorruptAndCommitRemoteTarget()
        {
            var path = Target(OtherPlatform, "copilot");
            File.WriteAllText(path, "Committed target does not match its sources.\n");
            GitProcess.RunRequired(Repository, "Could not stage corrupt fixture target",
                "add", "--", Relative(path));
            GitProcess.RunRequired(Repository, "Could not commit corrupt fixture target",
                "-c", "user.name=AEC Test", "-c", "user.email=aec-test@example.invalid",
                "-c", "commit.gpgsign=false", "commit", "-m", "Corrupt remote target");
        }

        public string Head() => GitProcess.RunRequired(Repository, "Could not read HEAD",
            "rev-parse", "HEAD").Output.Trim();

        public void CommitEmpty() => GitProcess.RunRequired(Repository,
            "Could not advance fixture HEAD",
            "-c", "user.name=AEC Test", "-c", "user.email=aec-test@example.invalid",
            "-c", "commit.gpgsign=false", "commit", "--allow-empty", "-m", "Advance HEAD");

        public void Dispose() => TestDirectoryCleanup.Delete(root);

        private static void Write(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }
    }
}
