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
        var (reviewed, attribution) = layout.SeedReviewedSource(section);
        var runtime = File.ReadAllBytes(layout.RuntimePath);
        var before = layout.Targets.ToDictionary(path => path, File.ReadAllBytes);

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
            var bytes = File.ReadAllBytes(path);
            Assert.Equal(expected.Contains(path), !bytes.AsSpan().SequenceEqual(before[path]));
            var relative = layout.Relative(path).Split('/');
            var parts = InstructionSections.Parse(Encoding.UTF8.GetString(bytes), true, true);
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
        Assert.Equal(reviewed.Commit, layout.Head());
        Assert.False(Directory.Exists(layout.CopilotHome));
    }

    [Fact]
    public void CopilotProviderEditRendersOnlyCopilotTargetsAcrossPlatforms()
    {
        using var layout = new Layout(localProvider: "copilot");
        var (reviewed, attribution) = layout.SeedReviewedSource("provider");
        var beforeCodex = layout.Targets
            .Where(path => layout.Relative(path).Contains("/codex/", StringComparison.Ordinal))
            .ToDictionary(path => path, File.ReadAllBytes);

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
        var (reviewed, attribution) = layout.SeedReviewedSource("shared");

        var rendered = PortableBackupTargetWriter.Render(reviewed, attribution);

        Assert.Equal(5, rendered.Count);
        Assert.False(File.Exists(layout.Target(layout.OtherPlatform, "copilot")));
    }

    [Fact]
    public void DirtyAffectedTargetStopsBeforeAnyTargetWrite()
    {
        using var layout = new Layout();
        var (reviewed, attribution) = layout.SeedReviewedSource("shared");
        var untouched = layout.Targets.Where(path =>
            path != layout.Target(layout.OtherPlatform, "copilot"))
            .ToDictionary(path => path, File.ReadAllBytes);
        File.WriteAllText(layout.Target(layout.OtherPlatform, "copilot"),
            "Concurrent target edit.\n");

        var error = Assert.Throws<InvalidOperationException>(() =>
            PortableBackupTargetWriter.Render(reviewed, attribution));
        Assert.Contains("Rendered target has unstaged changes", error.Message, StringComparison.Ordinal);

        foreach (var (path, bytes) in untouched)
        {
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }
    }

    [Fact]
    public void RuntimeChangedAfterSourceWriteStopsBeforeTargetWrite()
    {
        using var layout = new Layout(localEnrollmentOnly: true);
        var (reviewed, attribution) = layout.SeedReviewedSource("shared");
        var before = layout.Targets.ToDictionary(path => path, File.ReadAllBytes);
        File.AppendAllText(layout.RuntimePath, "Later runtime edit.\n");

        var error = Assert.Throws<InvalidOperationException>(() =>
            PortableBackupTargetWriter.Render(reviewed, attribution));
        Assert.Contains("Backup inputs changed while rendering targets", error.Message, StringComparison.Ordinal);

        foreach (var (path, bytes) in before)
        {
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }
    }

    [Fact]
    public void SourceChangedAfterSourceWriteStopsBeforeTargetWrite()
    {
        using var layout = new Layout(localEnrollmentOnly: true);
        var (reviewed, attribution) = layout.SeedReviewedSource("shared");
        var before = layout.Targets.ToDictionary(path => path, File.ReadAllBytes);
        File.WriteAllText(layout.SharedSource, "Later source edit.\n");

        var error = Assert.Throws<InvalidOperationException>(() =>
            PortableBackupTargetWriter.Render(reviewed, attribution));
        Assert.Contains("Authored source differs from the reviewed backup edit", error.Message, StringComparison.Ordinal);

        foreach (var (path, bytes) in before)
        {
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }
    }

    [Fact]
    public void HeadChangedAfterSourceWriteStopsBeforeTargetWrite()
    {
        using var layout = new Layout(localEnrollmentOnly: true);
        var (reviewed, attribution) = layout.SeedReviewedSource("shared");
        var before = layout.Targets.ToDictionary(path => path, File.ReadAllBytes);
        layout.CommitEmpty();

        var error = Assert.Throws<InvalidOperationException>(() =>
            PortableBackupTargetWriter.Render(reviewed, attribution));
        Assert.Contains("Backup inputs changed while rendering targets", error.Message, StringComparison.Ordinal);

        foreach (var (path, bytes) in before)
        {
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }
    }

    [Fact]
    public void StagedSourceStopsBeforeTargetWrite()
    {
        using var layout = new Layout(localEnrollmentOnly: true);
        var (reviewed, attribution) = layout.SeedReviewedSource("shared");
        var before = layout.Targets.ToDictionary(path => path, File.ReadAllBytes);
        GitProcess.RunRequired(layout.Repository, "Could not stage changed fixture source",
            "add", "--", "environment/shared/instructions.md");

        var error = Assert.Throws<InvalidOperationException>(() =>
            PortableBackupTargetWriter.Render(reviewed, attribution));
        Assert.Contains("An authored source or affected target has staged changes", error.Message, StringComparison.Ordinal);

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
        var (reviewed, attribution) = layout.SeedReviewedSource("shared");
        var before = layout.Targets.ToDictionary(path => path, File.ReadAllBytes);

        var error = Assert.Throws<InvalidDataException>(() =>
            PortableBackupTargetWriter.Render(reviewed, attribution));
        Assert.Contains("Committed target does not match its authored sources", error.Message, StringComparison.Ordinal);

        foreach (var (path, bytes) in before)
        {
            Assert.Equal(bytes, File.ReadAllBytes(path));
        }
    }

    internal sealed class Layout : IDisposable
    {
        private readonly string root;

        public Layout(
            bool enrollRemoteCopilot = true, string localProvider = "codex", bool localEnrollmentOnly = false)
        {
            root = Path.Combine(TestTemporaryDirectory.Root,
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

            var platforms = localEnrollmentOnly ? [LocalPlatform] : Platforms;
            string[] providers = localEnrollmentOnly ? [localProvider] : ["codex", "copilot"];

            Write(SharedSource, "Shared baseline.\n");
            foreach (var platform in platforms)
            {
                Write(Path.Combine(Repository, "environment", "platforms", platform,
                    "policy.md"), $"Policy {platform}.\n");
            }

            foreach (var provider in providers)
            {
                Write(Path.Combine(Repository, "environment", "providers", provider,
                    "overlay.md"), $"Overlay {provider}.\n");
            }

            Targets = [];
            foreach (var platform in platforms)
            {
                foreach (var provider in providers)
                {
                    if (!enrollRemoteCopilot && platform == OtherPlatform && provider == "copilot")
                    {
                        continue;
                    }

                    var path = Target(platform, provider);
                    Write(path, BaselineTarget(platform, provider));
                    Targets.Add(path);
                }
            }

            GitProcess.RunRequired(Repository, "Could not initialize fixture Git",
                "init", "--quiet", "--template=", "--initial-branch=main");
            GitProcess.RunRequired(Repository, "Could not stage fixture", "add", "--", "environment");
            GitProcess.RunRequired(Repository, "Could not commit fixture", "commit", "-m", "Fixture baseline");
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

        public (PortableRuntimeSnapshot, PortableBackupAttributionPreview) SeedReviewedSource(string section)
        {
            var target = Encoding.UTF8.GetBytes(BaselineTarget(LocalPlatform, LocalProvider));
            var baseline = LocalProvider == "codex"
                ? AecInstructionBlock.MergeForPortableTarget(
                    target, Repository, LocalPlatform, includeChatGptGuidance: false)
                : CopilotInstructionBlock.MergeForPortableTarget(target, Repository, LocalPlatform);
            var (original, replacement) = section switch
            {
                "shared" => ("Shared baseline.", "Shared revision."),
                "platform" => ($"Policy {LocalPlatform}.", $"Revised policy {LocalPlatform}."),
                "provider" => ($"Overlay {LocalProvider}.", $"Revised overlay {LocalProvider}."),
                _ => throw new ArgumentOutOfRangeException(nameof(section))
            };
            var runtime = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(baseline)
                .Replace(original, replacement, StringComparison.Ordinal));
            var version = PortableRuntimeSnapshot.ValidateRuntimeBinding(
                Repository, LocalPlatform, LocalProvider, runtime);
            var reviewed = new PortableRuntimeSnapshot(
                Repository, LocalPlatform, LocalProvider, RuntimePath, Head(), target, runtime, version);
            var attribution = PortableBackupAttributionPreview.Create(reviewed);
            Assert.Equal(section, attribution.Section);
            Write(Path.Combine(Repository, attribution.SourceRelativePath!), attribution.ProposedContent!);
            Directory.CreateDirectory(ProviderHome);
            File.WriteAllBytes(RuntimePath, runtime);
            return (reviewed, attribution);
        }

        public IReadOnlyList<string> SeedRenderedTargets(PortableBackupAttributionPreview attribution)
        {
            var paths = new List<string>();
            foreach (var path in Targets.Select(Relative).Order(StringComparer.Ordinal))
            {
                var parts = path.Split('/');
                var platform = parts[2];
                var provider = parts[3];
                var original = attribution.Section switch
                {
                    "shared" => "Shared baseline.\n",
                    "platform" when platform == LocalPlatform => $"Policy {platform}.\n",
                    "provider" when provider == LocalProvider => $"Overlay {provider}.\n",
                    _ => null
                };
                if (original is null)
                {
                    continue;
                }

                var rendered = BaselineTarget(platform, provider)
                    .Replace(original, attribution.ProposedContent!, StringComparison.Ordinal);
                Write(Path.Combine(Repository, path), rendered);
                paths.Add(path);
            }

            return paths;
        }

        private static string BaselineTarget(string platform, string provider) =>
            InstructionSections.Render("Shared baseline.\n", $"Policy {platform}.\n", $"Overlay {provider}.\n");

        public void CorruptAndCommitRemoteTarget()
        {
            var path = Target(OtherPlatform, "copilot");
            File.WriteAllText(path, "Committed target does not match its sources.\n");
            GitProcess.RunRequired(Repository, "Could not stage corrupt fixture target",
                "add", "--", Relative(path));
            GitProcess.RunRequired(Repository, "Could not commit corrupt fixture target",
                "commit", "-m", "Corrupt remote target");
        }

        public string Head() => GitProcess.RunRequired(Repository, "Could not read HEAD",
            "rev-parse", "HEAD").Output.Trim();

        public void CommitEmpty() => GitProcess.RunRequired(Repository,
            "Could not advance fixture HEAD", "commit", "--allow-empty", "-m", "Advance HEAD");

        public void Dispose() => TestDirectoryCleanup.Delete(root);

        private static void Write(string path, string content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }
    }
}
