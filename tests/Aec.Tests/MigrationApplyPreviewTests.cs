using System.Text;

namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class MigrationApplyPreviewTests
{
    [Theory]
    [InlineData("codex", false)]
    [InlineData("copilot", false)]
    [InlineData("codex", true)]
    [InlineData("copilot", true)]
    public void SelectedHarnessPreviewsLegacyTransitionOrPortableNoop(string provider, bool portable)
    {
        using var layout = new MigrationPortableCapturePreviewTests.Layout("codex", "copilot");
        if (!portable) RestoreLegacyRuntime(layout, provider);
        var context = layout.Context();
        var runtime = File.ReadAllBytes(layout.Runtime(provider));
        var target = File.ReadAllBytes(Path.Combine(layout.Repository, layout.Target(provider)));

        layout.Files.AssertNoChanges(() =>
        {
            var preview = MigrationApplyPreview.Read(context, provider);
            Assert.Equal(provider, preview.Harness.Provider);
            Assert.Equal(layout.Runtime(provider), preview.Apply.RuntimePath);
            Assert.Equal(context.Preflight.Commit, preview.Apply.Snapshot.Commit);
            Assert.Equal(runtime, preview.Apply.Snapshot.Runtime);
            Assert.Equal(target, preview.Apply.Snapshot.Target);
            Assert.Equal(!portable, preview.Apply.WouldReplace);
            Assert.Equal(MigrationLegacyCapturePreview.ReadBranch(layout.Repository), preview.Branch);
            Assert.Contains(preview.RequiredSources, item => item.RelativePath == "environment/shared/instructions.md");
            Assert.Contains(preview.RequiredSources, item => item.RelativePath == $"environment/providers/{provider}/overlay.md");
            if (provider == "copilot") Assert.Null(preview.Apply.CodexConfig);
            else Assert.NotNull(preview.Apply.CodexConfig);
        });
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("copilot")]
    public void CleanCommittedPreviewPreservesUnrefreshedIndex(string provider)
    {
        using var layout = new MigrationPortableCapturePreviewTests.Layout("codex", "copilot");
        var context = layout.Context();
        // Recreate the same committed index with an empty stat cache. The
        // preview must accept clean content without populating that cache.
        layout.Files.Git("read-tree", layout.Files.Head());

        layout.Files.AssertNoChanges(() =>
        {
            var preview = MigrationApplyPreview.Read(context, provider);
            Assert.False(preview.Apply.WouldReplace);
            Assert.Equal(context.Preflight.Commit, preview.Apply.Snapshot.Commit);
        });
    }

    [Theory]
    [InlineData("personality = \"friendly\"\nmodel = \"keep-me\"\n", false, false)]
    [InlineData("personality = \"pragmatic\"\nmodel = \"keep-me\"\n", true, false)]
    [InlineData("model = \"keep-me\"\n", true, true)]
    [InlineData(null, true, true)]
    public void CodexConfigPreviewPreservesUnmanagedValues(string? runtime, bool replace, bool inserted)
    {
        using var layout = new MigrationPortableCapturePreviewTests.Layout("codex", "copilot");
        if (runtime is null) File.Delete(layout.ConfigRuntime);
        else File.WriteAllText(layout.ConfigRuntime, runtime);
        var context = layout.Context();

        layout.Files.AssertNoChanges(() =>
        {
            var config = Assert.IsType<PortableCodexConfigPreview>(MigrationApplyPreview.Read(context, "codex").Apply.CodexConfig);
            Assert.Equal(replace, config.WouldReplace);
            Assert.Equal(inserted, config.Inserted);
            Assert.Equal(runtime is null ? null : Encoding.UTF8.GetBytes(runtime), config.Runtime);
            Assert.Equal("personality = \"friendly\"\n" + (runtime?.Contains("model", StringComparison.Ordinal) == true ? "model = \"keep-me\"\n" : ""),
                Encoding.UTF8.GetString(config.DesiredRuntime));
        });
    }

    [Theory]
    [InlineData("canonical")]
    [InlineData("runtime")]
    public void InvalidCodexConfigStopsWithoutChanges(string location)
    {
        using var layout = new MigrationPortableCapturePreviewTests.Layout("codex", "copilot");
        var context = layout.Context();
        if (location == "canonical")
        {
            layout.Files.Write(AecApplication.ConfigSourceRelativePath, "personality = \"unknown\"\n");
            layout.Commit(AecApplication.ConfigSourceRelativePath);
            // Context's preflight would itself reject this invalid config.
            // Pin the new HEAD so the assertion exercises Read, not setup.
            context = context with { Preflight = context.Preflight with { Commit = layout.Files.Head() } };
        }
        else File.WriteAllText(layout.ConfigRuntime, "personality = \"friendly\"\npersonality = \"none\"\n");
        layout.Files.AssertNoChanges(() => Assert.Throws<InvalidDataException>(() => MigrationApplyPreview.Read(context, "codex")));
    }

    [Fact]
    public void UnsupportedProviderIsRejectedWithoutChanges()
    {
        using var layout = new MigrationPortableCapturePreviewTests.Layout("codex", "copilot");
        var context = layout.Context();
        layout.Files.AssertNoChanges(() => Assert.Throws<ArgumentException>(() => MigrationApplyPreview.Read(context, "other")));
    }

    [Fact]
    public void ProviderAbsentFromConfirmedHarnessesIsRejected()
    {
        using var layout = new MigrationPortableCapturePreviewTests.Layout("codex", "copilot");
        File.Delete(layout.Runtime("copilot"));
        var context = layout.Context();
        layout.Files.AssertNoChanges(() => Assert.Throws<InvalidOperationException>(() => MigrationApplyPreview.Read(context, "copilot")));
    }

    [Theory]
    [InlineData("codex", "repository")]
    [InlineData("copilot", "repository")]
    [InlineData("codex", "platform")]
    [InlineData("copilot", "platform")]
    public void ChangedRuntimeBindingStops(string provider, string mismatch)
    {
        using var layout = new MigrationPortableCapturePreviewTests.Layout("codex", "copilot");
        var context = layout.Context();
        var repository = mismatch == "repository" ? Path.Combine(layout.Files.Root, "other-data") : layout.Repository;
        var otherPlatform = layout.Platform == "windows-x64" ? "macos-arm64" : "windows-x64";
        var platform = mismatch == "platform" ? otherPlatform : layout.Platform;
        var bytes = Encoding.UTF8.GetBytes("Instructions.\n");
        File.WriteAllBytes(layout.Runtime(provider), provider == "codex"
            ? AecInstructionBlock.MergeForPortableTarget(bytes, repository, platform, false)
            : CopilotInstructionBlock.MergeForPortableTarget(bytes, repository, platform));
        layout.Files.AssertNoChanges(() => Assert.Throws<InvalidOperationException>(() => MigrationApplyPreview.Read(context, provider)));
    }

    [Theory]
    [InlineData("head")]
    [InlineData("dirty-source")]
    [InlineData("staged-source")]
    [InlineData("dirty-target")]
    [InlineData("staged-target")]
    [InlineData("missing-target")]
    [InlineData("stale-target")]
    public void UnsafeRepositoryStateStops(string kind)
    {
        using var layout = new MigrationPortableCapturePreviewTests.Layout("codex", "copilot");
        var context = layout.Context();
        var source = "environment/shared/instructions.md";
        var target = layout.Target("codex");
        switch (kind)
        {
            case "head":
                layout.Files.Git("commit", "--allow-empty", "-m", "Later checkpoint");
                break;
            case "dirty-source":
            case "staged-source":
                layout.Files.Write(source, "Changed source.\n");
                if (kind == "staged-source") layout.Files.Git("add", "--", source);
                break;
            case "dirty-target":
            case "staged-target":
                layout.Files.Write(target, "Changed target.\n");
                if (kind == "staged-target") layout.Files.Git("add", "--", target);
                break;
            case "missing-target":
                layout.Files.Git("rm", "--", target);
                layout.Files.Git("commit", "--quiet", "-m", "Remove target");
                context = layout.Context();
                break;
            case "stale-target":
                layout.Files.Write(source, "Committed new source.\n");
                layout.Commit(source);
                context = layout.Context();
                break;
        }

        layout.Files.AssertNoChanges(() =>
        {
            if (kind == "missing-target")
                Assert.Throws<FileNotFoundException>(() => MigrationApplyPreview.Read(context, "codex"));
            else if (kind == "stale-target")
                Assert.Throws<InvalidDataException>(() => MigrationApplyPreview.Read(context, "codex"));
            else
                Assert.Throws<InvalidOperationException>(() => MigrationApplyPreview.Read(context, "codex"));
        });
    }

    [Theory]
    [InlineData("source")]
    [InlineData("target")]
    public void LinkedRepositoryFileIsRejected(string kind)
    {
        using var layout = new MigrationPortableCapturePreviewTests.Layout("codex", "copilot");
        var context = layout.Context();
        var relative = kind == "source" ? "environment/shared/instructions.md" : layout.Target("codex");
        var path = Path.Combine(layout.Repository, relative);
        var backing = Path.Combine(layout.Files.Root, "linked-file-content");
        File.Move(path, backing);
        File.CreateSymbolicLink(path, backing);
        layout.Files.AssertNoChanges(() => Assert.Throws<InvalidOperationException>(() => MigrationApplyPreview.Read(context, "codex")));
    }

    [Theory]
    [InlineData("target")]
    [InlineData("config")]
    public void ReplacementCommitCannotAuthorizeMigrationApply(string artifact)
    {
        using var layout = new MigrationPortableCapturePreviewTests.Layout("codex", "copilot");
        var validCommit = layout.Files.Head();
        var relative = artifact == "target" ? layout.Target("codex") : AecApplication.ConfigSourceRelativePath;
        var path = Path.Combine(layout.Repository, relative);
        var validBytes = File.ReadAllBytes(path);
        layout.Files.Write(relative, artifact == "target" ? "Not the rendered target.\n" : "personality = \"pragmatic\"\n");
        layout.Commit(relative);
        var actualCommit = layout.Files.Head();
        var context = layout.Context();

        // Present the old valid tree through a replacement commit, while HEAD
        // still names the actual commit containing the different artifact.
        var validTree = layout.Files.Git("rev-parse", validCommit + "^{tree}").Trim();
        var replacement = layout.Files.Git("commit-tree", validTree, "-p", validCommit,
            "-m", "Valid replacement view").Trim();
        layout.Files.Git("replace", actualCommit, replacement);
        layout.Files.Git("read-tree", replacement);
        File.WriteAllBytes(path, validBytes);

        var entries = ReadDiagnosticIndex(layout, "--stage", "-z");
        var flags = ReadDiagnosticIndex(layout, "-v", "-z");
        var debug = ReadDiagnosticIndex(layout, "--debug");
        var files = Directory.EnumerateFiles(layout.Files.Root, "*", SearchOption.AllDirectories)
            .ToDictionary(file => file, File.ReadAllBytes, StringComparer.Ordinal);

        layout.Files.AssertNoChanges(() =>
        {
            // Existing staged-provenance checks may reject first. Assert the
            // refusal contract, not an assumed ordering of integrity guards.
            AssertReplacementPreviewRefuses(context, artifact);
            AssertDiagnosticFilesUnchanged(layout, files, entries, flags, debug);
        });
    }

    private static void AssertReplacementPreviewRefuses(MigrationPreparation context, string artifact)
    {
        var traceDirectory = Environment.GetEnvironmentVariable("AEC_GIT_TRACE_DIR");
        var previousTrace = Environment.GetEnvironmentVariable("GIT_TRACE2_EVENT");
        try
        {
            if (traceDirectory is not null)
            {
                // The diagnostic runner supplies a directory outside the fixture.
                // Trace only production Read, not setup or later index inspection.
                Directory.CreateDirectory(traceDirectory);
                var tracePath = Path.GetFullPath(Path.Combine(traceDirectory,
                    $"{artifact}-{Guid.NewGuid():N}.json"));
                Environment.SetEnvironmentVariable("GIT_TRACE2_EVENT", tracePath);
            }
            Assert.Throws<InvalidOperationException>(() => MigrationApplyPreview.Read(context, "codex"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("GIT_TRACE2_EVENT", previousTrace);
        }
    }

    private static string ReadDiagnosticIndex(MigrationPortableCapturePreviewTests.Layout layout,
        params string[] options) => GitProcess.RunRequired(layout.Repository,
        "Could not read diagnostic index state", ["--no-optional-locks", "ls-files", .. options]).Output;

    private static void AssertDiagnosticFilesUnchanged(MigrationPortableCapturePreviewTests.Layout layout,
        Dictionary<string, byte[]> files, string entries, string flags, string debug)
    {
        var changes = new List<string>();
        var indexChanged = false;
        var indexPath = Path.Combine(layout.Repository, ".git", "index");
        foreach (var (path, before) in files)
        {
            var after = File.ReadAllBytes(path);
            if (before.AsSpan().SequenceEqual(after)) continue;
            var offset = 0;
            var commonLength = Math.Min(before.Length, after.Length);
            while (offset < commonLength && before[offset] == after[offset]) offset++;
            changes.Add($"Changed {Path.GetRelativePath(layout.Files.Root, path)}: " +
                $"length {before.Length} -> {after.Length}; first different byte {offset}.");
            if (path == indexPath) indexChanged = true;
        }

        if (indexChanged)
        {
            // These diagnostic reads must not refresh the very cache being
            // examined. Raw byte equality remains the required contract.
            var currentEntries = ReadDiagnosticIndex(layout, "--stage", "-z");
            var currentFlags = ReadDiagnosticIndex(layout, "-v", "-z");
            var currentDebug = ReadDiagnosticIndex(layout, "--debug");
            changes.Add($"Index entries equal: {entries == currentEntries}; flags equal: {flags == currentFlags}.");
            changes.Add($"Index debug before:\n{debug}\nIndex debug after:\n{currentDebug}");
        }
        Assert.True(changes.Count == 0, string.Join(Environment.NewLine, changes));
    }

    private static void RestoreLegacyRuntime(MigrationPortableCapturePreviewTests.Layout layout, string provider)
    {
        var relative = provider == "codex" ? AecApplication.SourceRelativePath : AecApplication.CopilotSourceRelativePath;
        File.WriteAllBytes(layout.Runtime(provider), File.ReadAllBytes(Path.Combine(layout.Repository, relative)));
    }
}
