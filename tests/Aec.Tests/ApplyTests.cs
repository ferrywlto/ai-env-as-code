using System.Text;

namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class ApplyTests
{
    private const string SourceRelativePath = "environment/providers/codex/AGENTS.md";
    private const string ConfigSourceRelativePath = "environment/providers/codex/config.toml";

    [Fact]
    public void AppliesExactCommittedBytesWithoutChangingGit()
    {
        var desired = new byte[] { 0xEF, 0xBB, 0xBF }.Concat("# desired\r\n"u8.ToArray()).ToArray();
        using var layout = new ApplyLayout(desired, "runtime\n"u8.ToArray());
        var headBefore = Git(layout, "rev-parse", "HEAD").Output.Trim();
        var sourceBefore = File.ReadAllBytes(layout.Source);

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"applied{Environment.NewLine}", result.Output);
        Assert.Empty(result.Error);
        Assert.Equal(desired, File.ReadAllBytes(layout.Runtime));
        Assert.Equal(sourceBefore, File.ReadAllBytes(layout.Source));
        Assert.Equal(headBefore, Git(layout, "rev-parse", "HEAD").Output.Trim());
        Assert.Equal(string.Empty, Git(layout, "status", "--porcelain").Output);
        AssertNoTemporaryFiles(layout);
    }

    [Fact]
    public void MissingRuntimeIsCreated()
    {
        using var layout = new ApplyLayout("desired\n"u8.ToArray(), runtime: null);

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"applied{Environment.NewLine}", result.Output);
        Assert.Equal("desired\n"u8.ToArray(), File.ReadAllBytes(layout.Runtime));
        AssertNoTemporaryFiles(layout);
    }

    [Fact]
    public void AppliesManagedPersonalityWithoutChangingOtherRuntimeConfigBytes()
    {
        var runtimeConfig = Encoding.UTF8.GetBytes(
            "# café\nmodel = \"gpt-test\"\npersonality = 'none' # retain this comment\n" +
            "[projects.\"/tmp/example\"]\npersonality = \"pragmatic\"\n");
        using var layout = new ApplyLayout(
            "same\n"u8.ToArray(),
            "same\n"u8.ToArray(),
            canonicalConfig: "\"personality\" = \"friendly\"\n"u8.ToArray(),
            runtimeConfig: runtimeConfig);
        var headBefore = Git(layout, "rev-parse", "HEAD").Output.Trim();

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"applied{Environment.NewLine}", result.Output);
        Assert.Empty(result.Error);
        Assert.Equal(
            "# café\nmodel = \"gpt-test\"\npersonality = \"friendly\" # retain this comment\n" +
            "[projects.\"/tmp/example\"]\npersonality = \"pragmatic\"\n",
            File.ReadAllText(layout.RuntimeConfig));
        Assert.Equal(headBefore, Git(layout, "rev-parse", "HEAD").Output.Trim());
        Assert.Equal(string.Empty, Git(layout, "status", "--porcelain").Output);
        AssertNoTemporaryFiles(layout);
    }

    [Fact]
    public void MissingManagedPersonalityIsAddedAtTopWithWarning()
    {
        var runtimeConfig = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat("model = \"gpt-test\"\r\n[projects.\"/tmp/example\"]\r\ntrust_level = \"trusted\"\r\n"u8.ToArray())
            .ToArray();
        using var layout = new ApplyLayout(
            "same\n"u8.ToArray(),
            "same\n"u8.ToArray(),
            canonicalConfig: "personality = \"pragmatic\"\n"u8.ToArray(),
            runtimeConfig: runtimeConfig);

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"applied{Environment.NewLine}", result.Output);
        Assert.Contains("warning:", result.Error, StringComparison.Ordinal);
        Assert.Contains("personality", result.Error, StringComparison.Ordinal);
        Assert.Equal(
            new byte[] { 0xEF, 0xBB, 0xBF }
                .Concat("personality = \"pragmatic\"\r\nmodel = \"gpt-test\"\r\n"u8.ToArray())
                .Concat("[projects.\"/tmp/example\"]\r\ntrust_level = \"trusted\"\r\n"u8.ToArray())
                .ToArray(),
            File.ReadAllBytes(layout.RuntimeConfig));
        AssertNoTemporaryFiles(layout);
    }

    [Fact]
    public void MissingRuntimeConfigIsCreatedWithWarning()
    {
        using var layout = new ApplyLayout(
            "same\n"u8.ToArray(),
            "same\n"u8.ToArray(),
            canonicalConfig: "personality = \"friendly\"\n"u8.ToArray(),
            createRuntimeConfig: false);

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"applied{Environment.NewLine}", result.Output);
        Assert.Contains("warning:", result.Error, StringComparison.Ordinal);
        Assert.Contains("personality", result.Error, StringComparison.Ordinal);
        Assert.Equal("personality = \"friendly\"\n"u8.ToArray(), File.ReadAllBytes(layout.RuntimeConfig));
        if (!OperatingSystem.IsWindows())
        {
            Assert.Equal(
                UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(layout.RuntimeConfig));
        }
        AssertNoTemporaryFiles(layout);
    }

    [Fact]
    public void ExistingRuntimeConfigPermissionsArePreserved()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var layout = new ApplyLayout(
            "same\n"u8.ToArray(),
            "same\n"u8.ToArray(),
            canonicalConfig: "personality = \"friendly\"\n"u8.ToArray());
        // GroupWrite is commonly masked by umask, so this verifies the explicit
        // post-creation mode restoration rather than only the creation request.
        var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupWrite;
        File.SetUnixFileMode(layout.RuntimeConfig, mode);

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"applied{Environment.NewLine}", result.Output);
        Assert.Equal(mode, File.GetUnixFileMode(layout.RuntimeConfig));
    }

    [Fact]
    public void OversizedPlannedRuntimeConfigIsRejectedBeforeEitherRuntimeWrite()
    {
        var prefix = "personality = \"none\"\n#"u8.ToArray();
        var runtimeConfig = new byte[AecApplication.MaximumTextBytes];
        prefix.CopyTo(runtimeConfig, 0);
        Array.Fill(runtimeConfig, (byte)'a', prefix.Length, runtimeConfig.Length - prefix.Length);
        using var layout = new ApplyLayout(
            "desired\n"u8.ToArray(),
            "runtime\n"u8.ToArray(),
            canonicalConfig: "personality = \"pragmatic\"\n"u8.ToArray(),
            runtimeConfig: runtimeConfig);
        var agentsBefore = File.ReadAllBytes(layout.Runtime);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("would exceed 1 MiB", result.Error, StringComparison.Ordinal);
        Assert.Equal(agentsBefore, File.ReadAllBytes(layout.Runtime));
        Assert.Equal(runtimeConfig, File.ReadAllBytes(layout.RuntimeConfig));
        AssertNoTemporaryFiles(layout);
    }

    [Fact]
    public void CanonicalLoneCarriageReturnIsRejectedBeforeEitherRuntimeWrite()
    {
        using var layout = new ApplyLayout(
            "desired\n"u8.ToArray(),
            "runtime\n"u8.ToArray(),
            canonicalConfig: "personality = \"friendly\"\r"u8.ToArray());
        var agentsBefore = File.ReadAllBytes(layout.Runtime);
        var configBefore = File.ReadAllBytes(layout.RuntimeConfig);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("lone carriage return", result.Error, StringComparison.Ordinal);
        Assert.Equal(agentsBefore, File.ReadAllBytes(layout.Runtime));
        Assert.Equal(configBefore, File.ReadAllBytes(layout.RuntimeConfig));
        AssertNoTemporaryFiles(layout);
    }

    [Fact]
    public void PersonalityTableConflictIsRejectedBeforeEitherRuntimeWrite()
    {
        using var layout = new ApplyLayout(
            "desired\n"u8.ToArray(),
            "runtime\n"u8.ToArray(),
            canonicalConfig: "personality = \"friendly\"\n"u8.ToArray());
        File.WriteAllText(layout.RuntimeConfig, "[personality]\nvalue = \"nested\"\n");
        var agentsBefore = File.ReadAllBytes(layout.Runtime);
        var configBefore = File.ReadAllBytes(layout.RuntimeConfig);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.DoesNotContain("warning:", result.Error, StringComparison.Ordinal);
        Assert.Contains("conflicts with personality", result.Error, StringComparison.Ordinal);
        Assert.Equal(agentsBefore, File.ReadAllBytes(layout.Runtime));
        Assert.Equal(configBefore, File.ReadAllBytes(layout.RuntimeConfig));
        AssertNoTemporaryFiles(layout);
    }

    [Fact]
    public void EquivalentManagedPersonalityDoesNotRewriteRuntimeConfig()
    {
        var runtimeConfig = "\"personality\" = \"fr\\u0069endly\" # keep encoding\n"u8.ToArray();
        using var layout = new ApplyLayout(
            "same\n"u8.ToArray(),
            "same\n"u8.ToArray(),
            canonicalConfig: "personality = \"friendly\"\n"u8.ToArray(),
            runtimeConfig: runtimeConfig);
        var timestamp = new DateTime(2020, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(layout.RuntimeConfig, timestamp);

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"unchanged{Environment.NewLine}", result.Output);
        Assert.Empty(result.Error);
        Assert.Equal(runtimeConfig, File.ReadAllBytes(layout.RuntimeConfig));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(layout.RuntimeConfig));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirtyCanonicalConfigIsRejectedBeforeEitherRuntimeWrite(bool stageChange)
    {
        using var layout = new ApplyLayout("desired\n"u8.ToArray(), "runtime\n"u8.ToArray());
        File.WriteAllText(layout.ConfigSource, "personality = \"friendly\"\n");
        if (stageChange)
        {
            Assert.Equal(0, Git(layout, "add", "--", ConfigSourceRelativePath).ExitCode);
        }

        var agentsBefore = File.ReadAllBytes(layout.Runtime);
        var configBefore = File.ReadAllBytes(layout.RuntimeConfig);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            stageChange ? "staged changes" : "unstaged changes",
            result.Error,
            StringComparison.Ordinal);
        Assert.Equal(agentsBefore, File.ReadAllBytes(layout.Runtime));
        Assert.Equal(configBefore, File.ReadAllBytes(layout.RuntimeConfig));
    }

    [Fact]
    public void InvalidRuntimeConfigIsRejectedBeforeAgentsWrite()
    {
        using var layout = new ApplyLayout("desired\n"u8.ToArray(), "runtime\n"u8.ToArray());
        File.WriteAllText(
            layout.RuntimeConfig,
            "personality = \"none\"\npersonality = \"friendly\"\n");
        var agentsBefore = File.ReadAllBytes(layout.Runtime);
        var configBefore = File.ReadAllBytes(layout.RuntimeConfig);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("more than once", result.Error, StringComparison.Ordinal);
        Assert.Equal(agentsBefore, File.ReadAllBytes(layout.Runtime));
        Assert.Equal(configBefore, File.ReadAllBytes(layout.RuntimeConfig));
    }

    [Fact]
    public void GitFiltersThatHideDifferentRawConfigBytesAreRejected()
    {
        using var layout = new ApplyLayout("desired\n"u8.ToArray(), "runtime\n"u8.ToArray());
        File.WriteAllText(
            Path.Combine(layout.Repository, ".gitattributes"),
            $"{ConfigSourceRelativePath} text eol=lf\n");
        File.WriteAllBytes(layout.ConfigSource, "personality = \"friendly\"\r\n"u8.ToArray());
        Assert.Equal(
            0,
            Git(layout, "add", "--", ".gitattributes", ConfigSourceRelativePath).ExitCode);
        Assert.Equal(
            0,
            Git(layout, "commit", "--quiet", "--message", "Normalize config").ExitCode);
        Assert.Equal(
            string.Empty,
            Git(layout, "status", "--porcelain", "--", ConfigSourceRelativePath).Output);
        var agentsBefore = File.ReadAllBytes(layout.Runtime);
        var configBefore = File.ReadAllBytes(layout.RuntimeConfig);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("do not exactly match", result.Error, StringComparison.Ordinal);
        Assert.Equal(agentsBefore, File.ReadAllBytes(layout.Runtime));
        Assert.Equal(configBefore, File.ReadAllBytes(layout.RuntimeConfig));
    }

    [Fact]
    public void EqualRuntimeIsUnchangedWithoutRewritingIt()
    {
        var desired = "same\n"u8.ToArray();
        using var layout = new ApplyLayout(desired, desired);
        var timestamp = new DateTime(2020, 2, 3, 4, 5, 6, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(layout.Runtime, timestamp);
        File.SetLastWriteTimeUtc(layout.RuntimeConfig, timestamp);
        var headBefore = Git(layout, "rev-parse", "HEAD").Output.Trim();

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"unchanged{Environment.NewLine}", result.Output);
        Assert.Empty(result.Error);
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(layout.Runtime));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(layout.RuntimeConfig));
        Assert.Equal(headBefore, Git(layout, "rev-parse", "HEAD").Output.Trim());
        Assert.Equal(string.Empty, Git(layout, "status", "--porcelain").Output);
    }

    [Fact]
    public void DifferentRecordedRepositoryPathStopsAndDirectsTheUserToInit()
    {
        var recordedRepository = Path.Combine(
            TestTemporaryDirectory.Root,
            "recorded aec repository");
        var desired = AecInstructionBlock.Merge("desired\n"u8.ToArray(), recordedRepository);
        using var layout = new ApplyLayout(desired, "preserve runtime\n"u8.ToArray());
        File.Delete(layout.ConfigSource);
        var statusBefore = Git(layout, "status", "--porcelain").Output;
        var headBefore = Git(layout, "rev-parse", "HEAD").Output.Trim();
        var sourceBefore = File.ReadAllBytes(layout.Source);
        var runtimeBefore = File.ReadAllBytes(layout.Runtime);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(recordedRepository, result.Error, StringComparison.Ordinal);
        Assert.Contains(layout.Repository, result.Error, StringComparison.Ordinal);
        Assert.Contains("aec codex init", result.Error, StringComparison.Ordinal);
        Assert.Equal(sourceBefore, File.ReadAllBytes(layout.Source));
        Assert.Equal(runtimeBefore, File.ReadAllBytes(layout.Runtime));
        Assert.Equal(headBefore, Git(layout, "rev-parse", "HEAD").Output.Trim());
        Assert.Equal(statusBefore, Git(layout, "status", "--porcelain").Output);
    }

    [Fact]
    public void InitializationApplyRejectsRuntimeChangedSinceItsBackup()
    {
        using var layout = new ApplyLayout(
            "desired\n"u8.ToArray(),
            "concurrent runtime\n"u8.ToArray());
        var output = new StringWriter();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ApplyCommand.RunForInitialization(
                layout.Repository,
                layout.CodexHome,
                output,
                "captured runtime\n"u8.ToArray(),
                CodexPersonality.None,
                Git(layout, "rev-parse", "HEAD").Output.Trim(),
                "desired\n"u8.ToArray(),
                File.ReadAllBytes(layout.ConfigSource)));

        Assert.Contains("changed after", exception.Message, StringComparison.Ordinal);
        Assert.Empty(output.ToString());
        Assert.Equal("concurrent runtime\n", File.ReadAllText(layout.Runtime));
    }

    [Fact]
    public void InitializationApplyAcceptsRuntimeAlreadyAtCommittedSource()
    {
        var desired = "desired\n"u8.ToArray();
        using var layout = new ApplyLayout(desired, desired);
        var output = new StringWriter();

        var exitCode = ApplyCommand.RunForInitialization(
            layout.Repository,
            layout.CodexHome,
            output,
            "captured runtime\n"u8.ToArray(),
            CodexPersonality.None,
            Git(layout, "rev-parse", "HEAD").Output.Trim(),
            desired,
            File.ReadAllBytes(layout.ConfigSource));

        Assert.Equal(0, exitCode);
        Assert.Equal($"unchanged{Environment.NewLine}", output.ToString());
        Assert.Equal(desired, File.ReadAllBytes(layout.Runtime));
    }

    [Fact]
    public void InitializationApplyCompletesConfigWhenAgentsAreAlreadyApplied()
    {
        var desired = "desired\n"u8.ToArray();
        using var layout = new ApplyLayout(desired, desired, createRuntimeConfig: false);
        var output = new StringWriter();

        var exitCode = ApplyCommand.RunForInitialization(
            layout.Repository,
            layout.CodexHome,
            output,
            "captured runtime\n"u8.ToArray(),
            expectedRuntimePersonality: null,
            Git(layout, "rev-parse", "HEAD").Output.Trim(),
            desired,
            File.ReadAllBytes(layout.ConfigSource));

        Assert.Equal(0, exitCode);
        Assert.Equal($"applied{Environment.NewLine}", output.ToString());
        Assert.Equal(desired, File.ReadAllBytes(layout.Runtime));
        Assert.Equal("personality = \"none\"\n", File.ReadAllText(layout.RuntimeConfig));
    }

    [Fact]
    public void AttachmentApplyRejectsPersonalityChangedAfterPreflight()
    {
        var desired = "desired\n"u8.ToArray();
        using var layout = new ApplyLayout(
            desired,
            "captured runtime\n"u8.ToArray(),
            canonicalConfig: "personality = \"none\"\n"u8.ToArray(),
            runtimeConfig: "personality = \"pragmatic\"\n"u8.ToArray());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ApplyCommand.RunForAttachment(
                layout.Repository,
                layout.CodexHome,
                TextWriter.Null,
                TextWriter.Null,
                "captured runtime\n"u8.ToArray(),
                CodexPersonality.Friendly,
                Git(layout, "rev-parse", "HEAD").Output.Trim(),
                desired,
                File.ReadAllBytes(layout.ConfigSource)));

        Assert.Contains("personality changed", exception.Message, StringComparison.Ordinal);
        Assert.Equal("captured runtime\n", File.ReadAllText(layout.Runtime));
        Assert.Equal("personality = \"pragmatic\"\n", File.ReadAllText(layout.RuntimeConfig));
    }

    [Fact]
    public void InitializationApplyCreatesMissingRuntimeConfigFromItsCheckpoint()
    {
        var desired = "desired\n"u8.ToArray();
        using var layout = new ApplyLayout(
            desired,
            "captured runtime\n"u8.ToArray(),
            createRuntimeConfig: false);

        var exitCode = ApplyCommand.RunForInitialization(
            layout.Repository,
            layout.CodexHome,
            TextWriter.Null,
            "captured runtime\n"u8.ToArray(),
            expectedRuntimePersonality: null,
            Git(layout, "rev-parse", "HEAD").Output.Trim(),
            desired,
            File.ReadAllBytes(layout.ConfigSource));

        Assert.Equal(0, exitCode);
        Assert.Equal(desired, File.ReadAllBytes(layout.Runtime));
        Assert.Equal("personality = \"none\"\n", File.ReadAllText(layout.RuntimeConfig));
    }

    [Fact]
    public void InitializationApplyRejectsAnUnexpectedHead()
    {
        using var layout = new ApplyLayout(
            "desired\n"u8.ToArray(),
            "captured runtime\n"u8.ToArray());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ApplyCommand.RunForInitialization(
                layout.Repository,
                layout.CodexHome,
                TextWriter.Null,
                "captured runtime\n"u8.ToArray(),
                CodexPersonality.None,
                new string('0', 40),
                "desired\n"u8.ToArray(),
                File.ReadAllBytes(layout.ConfigSource)));

        Assert.Contains("HEAD changed", exception.Message, StringComparison.Ordinal);
        Assert.Equal("captured runtime\n", File.ReadAllText(layout.Runtime));
    }

    [Fact]
    public void InitializationApplyRejectsUnexpectedCommittedContent()
    {
        using var layout = new ApplyLayout(
            "desired\n"u8.ToArray(),
            "captured runtime\n"u8.ToArray());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ApplyCommand.RunForInitialization(
                layout.Repository,
                layout.CodexHome,
                TextWriter.Null,
                "captured runtime\n"u8.ToArray(),
                CodexPersonality.None,
                Git(layout, "rev-parse", "HEAD").Output.Trim(),
                "other desired\n"u8.ToArray(),
                File.ReadAllBytes(layout.ConfigSource)));

        Assert.Contains("expected initialization content", exception.Message, StringComparison.Ordinal);
        Assert.Equal("captured runtime\n", File.ReadAllText(layout.Runtime));
    }

    [Fact]
    public void UnrelatedRepositoryChangesArePreserved()
    {
        using var layout = new ApplyLayout("desired\n"u8.ToArray(), "runtime\n"u8.ToArray());
        var tracked = Path.Combine(layout.Repository, "tracked.txt");
        File.WriteAllText(tracked, "committed\n");
        Assert.Equal(0, Git(layout, "add", "--", "tracked.txt").ExitCode);
        Assert.Equal(0, Git(layout, "commit", "--quiet", "--message", "Add test file").ExitCode);
        File.WriteAllText(tracked, "unstaged\n");
        File.WriteAllText(Path.Combine(layout.Repository, "staged.txt"), "staged\n");
        Assert.Equal(0, Git(layout, "add", "--", "staged.txt").ExitCode);
        File.WriteAllText(Path.Combine(layout.Repository, "untracked.txt"), "untracked\n");
        var statusBefore = Git(layout, "status", "--porcelain=v1").Output;
        var headBefore = Git(layout, "rev-parse", "HEAD").Output.Trim();

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("desired\n"u8.ToArray(), File.ReadAllBytes(layout.Runtime));
        Assert.Equal(statusBefore, Git(layout, "status", "--porcelain=v1").Output);
        Assert.Equal(headBefore, Git(layout, "rev-parse", "HEAD").Output.Trim());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirtyCanonicalSourceIsRejectedBeforeRuntimeWrite(bool stageChange)
    {
        using var layout = new ApplyLayout("desired\n"u8.ToArray(), "runtime\n"u8.ToArray());
        File.WriteAllText(layout.Source, "pending\n");
        if (stageChange)
        {
            Assert.Equal(0, Git(layout, "add", "--", SourceRelativePath).ExitCode);
        }

        var runtimeBefore = File.ReadAllBytes(layout.Runtime);
        var headBefore = Git(layout, "rev-parse", "HEAD").Output.Trim();

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            stageChange ? "staged changes" : "unstaged changes",
            result.Error,
            StringComparison.Ordinal);
        Assert.Equal(runtimeBefore, File.ReadAllBytes(layout.Runtime));
        Assert.Equal(headBefore, Git(layout, "rev-parse", "HEAD").Output.Trim());
    }

    [Fact]
    public void StagedSourceIsRejectedWhenWorkingBytesWereReverted()
    {
        var desired = "desired\n"u8.ToArray();
        using var layout = new ApplyLayout(desired, "runtime\n"u8.ToArray());
        File.WriteAllText(layout.Source, "staged\n");
        Assert.Equal(0, Git(layout, "add", "--", SourceRelativePath).ExitCode);
        File.WriteAllBytes(layout.Source, desired);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("staged changes", result.Error, StringComparison.Ordinal);
        Assert.Equal("runtime\n"u8.ToArray(), File.ReadAllBytes(layout.Runtime));
    }

    [Fact]
    public void RepositoryWithoutACommitIsRejected()
    {
        using var layout = new ApplyLayout(
            "desired\n"u8.ToArray(),
            "runtime\n"u8.ToArray(),
            commitSource: false);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("committed Git HEAD", result.Error, StringComparison.Ordinal);
        Assert.Equal("runtime\n"u8.ToArray(), File.ReadAllBytes(layout.Runtime));
    }

    [Fact]
    public void SourceAbsentFromHeadIsRejected()
    {
        using var layout = new ApplyLayout(
            "desired\n"u8.ToArray(),
            "runtime\n"u8.ToArray(),
            commitSource: false);
        File.WriteAllText(Path.Combine(layout.Repository, "tracked.txt"), "tracked\n");
        Assert.Equal(0, Git(layout, "add", "--", "tracked.txt").ExitCode);
        Assert.Equal(0, Git(layout, "commit", "--quiet", "--message", "Unrelated commit").ExitCode);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("not a single committed Git file", result.Error, StringComparison.Ordinal);
        Assert.Equal("runtime\n"u8.ToArray(), File.ReadAllBytes(layout.Runtime));
    }

    [Fact]
    public void GitFiltersThatHideDifferentRawBytesAreRejected()
    {
        using var layout = new ApplyLayout("first\n"u8.ToArray(), "runtime\n"u8.ToArray());
        File.WriteAllText(
            Path.Combine(layout.Repository, ".gitattributes"),
            $"{SourceRelativePath} text eol=lf\n");
        File.WriteAllBytes(layout.Source, "second\r\n"u8.ToArray());
        Assert.Equal(0, Git(layout, "add", "--", ".gitattributes", SourceRelativePath).ExitCode);
        Assert.Equal(0, Git(layout, "commit", "--quiet", "--message", "Normalize source").ExitCode);
        Assert.Equal(string.Empty, Git(layout, "status", "--porcelain", "--", SourceRelativePath).Output);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("do not exactly match", result.Error, StringComparison.Ordinal);
        Assert.Equal("runtime\n"u8.ToArray(), File.ReadAllBytes(layout.Runtime));
    }

    [Fact]
    public void RuntimeDirectoryIsRejected()
    {
        using var layout = new ApplyLayout("desired\n"u8.ToArray(), runtime: null);
        Directory.CreateDirectory(layout.Runtime);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("Runtime target must be a regular file", result.Error, StringComparison.Ordinal);
        Assert.True(Directory.Exists(layout.Runtime));
    }

    [Fact]
    public void OversizedRuntimeIsRejected()
    {
        using var layout = new ApplyLayout("desired\n"u8.ToArray(), runtime: null);
        File.WriteAllBytes(layout.Runtime, new byte[AecApplication.MaximumTextBytes + 1]);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("exceeds 1 MiB", result.Error, StringComparison.Ordinal);
        Assert.Equal(AecApplication.MaximumTextBytes + 1, new FileInfo(layout.Runtime).Length);
    }

    [Fact]
    public void SymbolicLinkRuntimeIsRejectedWithoutChangingItsReferent()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var layout = new ApplyLayout("desired\n"u8.ToArray(), runtime: null);
        var referent = Path.Combine(layout.Root, "referent.md");
        File.WriteAllText(referent, "external\n");
        File.CreateSymbolicLink(layout.Runtime, referent);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains("must not be a symbolic link", result.Error, StringComparison.Ordinal);
        Assert.Equal("external\n", File.ReadAllText(referent));
    }

    [Fact]
    public void NestedDirectoryIsRejectedAsRepositoryRoot()
    {
        using var layout = new ApplyLayout("desired\n"u8.ToArray(), "runtime\n"u8.ToArray());
        var nested = Path.Combine(layout.Repository, "nested");
        Directory.CreateDirectory(nested);
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = AecApplication.Run(
            ["codex", "apply", "--repo", nested, "--home", layout.CodexHome],
            output,
            error);

        Assert.Equal(1, exitCode);
        Assert.Contains("repository root", error.ToString(), StringComparison.Ordinal);
        Assert.Equal("runtime\n"u8.ToArray(), File.ReadAllBytes(layout.Runtime));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RuntimeInsideRepositoryIsRejected(bool throughLinkedAncestor)
    {
        if (throughLinkedAncestor && OperatingSystem.IsWindows())
        {
            return;
        }

        var root = Path.Combine(TestTemporaryDirectory.Root, Guid.NewGuid().ToString("N"));
        var repository = Path.Combine(root, "data repository");
        var alias = Path.Combine(root, "root alias");
        Directory.CreateDirectory(repository);
        try
        {
            if (throughLinkedAncestor)
            {
                Directory.CreateSymbolicLink(alias, root);
            }

            var home = throughLinkedAncestor ? Path.Combine(alias, "data repository") : repository;
            var result = TestApplication.Run("codex", "apply", "--repo", repository, "--home", home);

            Assert.Equal(1, result.ExitCode);
            Assert.Empty(result.Output);
            Assert.Contains(throughLinkedAncestor ? "must not contain a symbolic link" : "outside the data repository",
                result.Error, StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(repository, "AGENTS.md")));
        }
        finally
        {
            if (Directory.Exists(alias))
            {
                Directory.Delete(alias);
            }

            TestDirectoryCleanup.Delete(root);
        }
    }

    [Fact]
    public void DetachedHeadCanBeApplied()
    {
        using var layout = new ApplyLayout("desired\n"u8.ToArray(), "runtime\n"u8.ToArray());
        Assert.Equal(0, Git(layout, "checkout", "--detach", "--quiet").ExitCode);

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal("desired\n"u8.ToArray(), File.ReadAllBytes(layout.Runtime));
    }

    [Fact]
    public void UsesCodexHomeFromEnvironmentWhenFlagIsAbsent()
    {
        using var layout = new SharedTargetStatusTests.StatusLayout("codex");
        var desired = layout.WriteRuntime(portableBlock: true);
        File.AppendAllText(layout.RuntimePath, "Unreviewed instruction edit.\n");
        File.WriteAllText(Path.Combine(layout.ProviderHome, "config.toml"),
            "personality = \"none\"\n");
        var previous = Environment.GetEnvironmentVariable("CODEX_HOME");

        try
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", layout.ProviderHome);
            var output = new StringWriter();
            var error = new StringWriter();

            var exitCode = AecApplication.Run(
                ["codex", "apply", "--repo", layout.Repository],
                output,
                error);

            Assert.Equal(0, exitCode);
            Assert.Equal($"applied{Environment.NewLine}", output.ToString());
            Assert.Equal(desired, File.ReadAllBytes(layout.RuntimePath));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", previous);
        }
    }

    private static CommandResult Run(ApplyLayout layout)
    {
        // Exercise the legacy writer through its live attachment entry point
        // using the fixture state captured before each test's changes.
        var output = new StringWriter();
        var error = new StringWriter();
        try
        {
            AecApplication.EnsureNoLinksInExistingPath(layout.Repository, "Repository path");
            AecApplication.EnsureNoLinksInExistingPath(layout.CodexHome, "Codex home path");
            AecApplication.EnsureRealDirectory(layout.Repository, "Repository");
            AecApplication.EnsureSourceDirectories(layout.Repository);
            AecApplication.EnsureRealDirectory(layout.CodexHome, "Codex home");
            var commit = GitRepository.ResolveHeadCommit(layout.Repository);
            var expectedPersonality = layout.InitialRuntimeConfig is null
                ? null
                : CodexPersonalityConfig.ReadRuntime(layout.InitialRuntimeConfig, layout.RuntimeConfig);
            var exitCode = ApplyCommand.RunForAttachment(
                layout.Repository, layout.CodexHome, output, error,
                layout.InitialRuntime, expectedPersonality, commit,
                layout.InitialSource, layout.InitialConfig);
            return new CommandResult(exitCode, output.ToString(), error.ToString());
        }
        catch (Exception exception)
        {
            error.WriteLine($"error: {exception.Message}");
            return new CommandResult(1, output.ToString(), error.ToString());
        }
    }

    private static void AssertNoTemporaryFiles(ApplyLayout layout)
    {
        Assert.DoesNotContain(
            Directory.GetFiles(layout.CodexHome),
            path => Path.GetFileName(path).Contains(".aec-", StringComparison.Ordinal));
    }

    private static GitResult Git(ApplyLayout layout, params string[] arguments)
    {
        return TestGit.Run(layout.Repository, arguments);
    }

    private sealed class ApplyLayout : IDisposable
    {
        public ApplyLayout(
            byte[] desired,
            byte[]? runtime,
            bool commitSource = true,
            byte[]? canonicalConfig = null,
            byte[]? runtimeConfig = null,
            bool createRuntimeConfig = true)
        {
            Root = Path.Combine(TestTemporaryDirectory.Root, "aec-apply-tests", Guid.NewGuid().ToString("N"));
            Repository = Path.Combine(Root, "data repository");
            CodexHome = Path.Combine(Root, "codex home");
            Source = Path.Combine(Repository, SourceRelativePath);
            ConfigSource = Path.Combine(Repository, ConfigSourceRelativePath);
            Runtime = Path.Combine(CodexHome, "AGENTS.md");
            RuntimeConfig = Path.Combine(CodexHome, "config.toml");
            InitialSource = desired;
            InitialConfig = canonicalConfig ?? "personality = \"none\"\n"u8.ToArray();
            InitialRuntime = runtime;
            InitialRuntimeConfig = createRuntimeConfig
                ? runtimeConfig ?? "personality = \"none\"\n"u8.ToArray()
                : null;

            Directory.CreateDirectory(Path.GetDirectoryName(Source)!);
            Directory.CreateDirectory(CodexHome);
            RequireGit(Repository, "init", "--quiet", "--template=", "--initial-branch=main");
            // The collection supplies identity and disables signing. The empty template has no hooks.
            var gitDirectory = Path.Combine(Repository, ".git");
            File.WriteAllText(Path.Combine(gitDirectory, "aec-empty-attributes"), string.Empty);
            File.AppendAllText(Path.Combine(gitDirectory, "config"),
                "\n[core]\n\tautocrlf = false\n\tattributesFile = .git/aec-empty-attributes\n\thooksPath = .git/hooks\n");

            File.WriteAllBytes(Source, desired);
            File.WriteAllBytes(
                ConfigSource,
                canonicalConfig ?? "personality = \"none\"\n"u8.ToArray());
            if (commitSource)
            {
                RequireGit(
                    Repository,
                    "add",
                    "--",
                    SourceRelativePath,
                    ConfigSourceRelativePath);
                RequireGit(Repository, "commit", "--quiet", "--message", "Test canonical source");
            }

            if (runtime is not null)
            {
                File.WriteAllBytes(Runtime, runtime);
            }

            if (createRuntimeConfig)
            {
                File.WriteAllBytes(RuntimeConfig, runtimeConfig ?? "personality = \"none\"\n"u8.ToArray());
            }
        }

        public byte[] InitialSource { get; }
        public byte[] InitialConfig { get; }
        public byte[]? InitialRuntime { get; }
        public byte[]? InitialRuntimeConfig { get; }

        public string Root { get; }

        public string Repository { get; }

        public string CodexHome { get; }

        public string Source { get; }

        public string ConfigSource { get; }

        public string Runtime { get; }

        public string RuntimeConfig { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }

        private static void RequireGit(string workingDirectory, params string[] arguments)
        {
            var result = TestGit.Run(workingDirectory, arguments);
            if (result.ExitCode != 0)
            {
                throw new InvalidOperationException(result.Error);
            }
        }
    }
}
