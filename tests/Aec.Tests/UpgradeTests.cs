using System.Security.Cryptography;

namespace Aec.Tests;

[Collection(ProcessStateCollection.Name)]
public sealed class UpgradeTests
{
    [Fact]
    public void HelpListsTheExactSkillUpgradeForm()
    {
        var result = Run("help");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains(
            "aec skill upgrade [--codex-home ABSOLUTE_PATH]",
            result.Output,
            StringComparison.Ordinal);
        Assert.Empty(result.Error);
    }

    [Fact]
    public void VersionReports110()
    {
        var result = Run("version");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"1.1.0{Environment.NewLine}", result.Output);
        Assert.Empty(result.Error);
    }

    [Theory]
    [InlineData("0.9.0", "dc5b81445caa9ea6d039504b67676d05ef2e19d2f98394eda826522056d4a6a8")]
    [InlineData("0.10.0", "728a706eadd9a802a17960a940430466d71b841d612b1b1953c99caf6df2d0ec")]
    [InlineData("0.11.4", "9cddc5727f0e491a1735e7c2d40e4cee865dc3675dfe24c4fb5842c2119b61c0")]
    [InlineData("0.12.0", "8cf1c0d8effbdf19cd44520bd96300b5201ba2a71cef69101f5490077159a3a7")]
    [InlineData("0.13.0", "1bf54d30a4237801df36dd4949d8a21e843dc6c1f98cfb092694c0999b51eacf")]
    [InlineData("1.0.0", "60754cc941dbfaf17042c4eb4093c9706ea0054c526001f096da2fc4a795aec9")]
    public void UpgradesEachExactSupportedOfficialPredecessor(
        string version,
        string expectedHash)
    {
        using var layout = new UpgradeLayout();
        var expectedSkill = File.ReadAllBytes(layout.Skill);
        var expectedMetadata = File.ReadAllBytes(layout.Metadata);
        var predecessor = ReadFixture(version);
        Assert.Equal(expectedHash, Hash(predecessor));
        File.WriteAllBytes(layout.Skill, predecessor);

        var first = Run("skill", "upgrade", "--codex-home", layout.CodexHome);

        Assert.Equal(0, first.ExitCode);
        Assert.Equal($"upgraded{Environment.NewLine}", first.Output);
        Assert.Empty(first.Error);
        Assert.Equal(expectedSkill, File.ReadAllBytes(layout.Skill));
        Assert.Equal(expectedMetadata, File.ReadAllBytes(layout.Metadata));
        AssertNoTemporaryFiles(layout);

        var second = Run("skill", "upgrade", "--codex-home", layout.CodexHome);

        Assert.Equal(0, second.ExitCode);
        Assert.Equal($"unchanged{Environment.NewLine}", second.Output);
        Assert.Empty(second.Error);
        AssertNoTemporaryFiles(layout);
    }

    [Fact]
    public void CurrentBundleIsUnchangedWithoutRewritingFiles()
    {
        using var layout = new UpgradeLayout();
        var timestamp = new DateTime(2020, 1, 2, 3, 4, 6, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(layout.Skill, timestamp);
        File.SetLastWriteTimeUtc(layout.Metadata, timestamp);
        var skillMode = GetUnixMode(layout.Skill);
        var metadataMode = GetUnixMode(layout.Metadata);
        var previous = Environment.GetEnvironmentVariable("CODEX_HOME");

        try
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", layout.CodexHome);
            var result = Run("skill", "upgrade");

            Assert.Equal(0, result.ExitCode);
            Assert.Equal($"unchanged{Environment.NewLine}", result.Output);
            Assert.Empty(result.Error);
            Assert.Equal(timestamp, File.GetLastWriteTimeUtc(layout.Skill));
            Assert.Equal(timestamp, File.GetLastWriteTimeUtc(layout.Metadata));
            Assert.Equal(skillMode, GetUnixMode(layout.Skill));
            Assert.Equal(metadataMode, GetUnixMode(layout.Metadata));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", previous);
        }
    }

    [Fact]
    public void RejectsModifiedMetadataBeforeChangingARecognizedSkill()
    {
        using var layout = new UpgradeLayout();
        File.WriteAllBytes(layout.Skill, ReadFixture("0.9.0"));
        File.AppendAllText(layout.Metadata, "# local customization\n");
        var skillBefore = File.ReadAllBytes(layout.Skill);
        var metadataBefore = File.ReadAllBytes(layout.Metadata);

        var result = Run("skill", "upgrade", "--codex-home", layout.CodexHome);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "Existing AEC skill is not an exact supported official bundle",
            result.Error,
            StringComparison.Ordinal);
        Assert.Equal(skillBefore, File.ReadAllBytes(layout.Skill));
        Assert.Equal(metadataBefore, File.ReadAllBytes(layout.Metadata));
        AssertNoTemporaryFiles(layout);
    }

    [Theory]
    [InlineData("SKILL.md")]
    [InlineData("agents/openai.yaml")]
    public void MissingManagedFileFailsWithoutChangingTheOtherFile(string relativePath)
    {
        using var layout = new UpgradeLayout();
        File.WriteAllBytes(layout.Skill, ReadFixture("0.9.0"));
        var missing = Path.Combine(layout.SkillRoot, relativePath);
        var other = missing == layout.Skill ? layout.Metadata : layout.Skill;
        var otherBefore = File.ReadAllBytes(other);
        File.Delete(missing);

        var result = Run("skill", "upgrade", "--codex-home", layout.CodexHome);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("AEC skill file does not exist", result.Error, StringComparison.Ordinal);
        Assert.Equal(otherBefore, File.ReadAllBytes(other));
        Assert.False(File.Exists(missing));
        AssertNoTemporaryFiles(layout);
    }

    [Fact]
    public void RejectsTheRetiredVersion080Bundle()
    {
        using var layout = new UpgradeLayout();
        var predecessor = ReadFixture("0.8.0");
        Assert.Equal(
            "d57e50eaeb0be3b79d9083bb59de353fd7db84cd957a18001b2d90ff355e4cce",
            Hash(predecessor));
        File.WriteAllBytes(layout.Skill, predecessor);
        var metadataBefore = File.ReadAllBytes(layout.Metadata);

        var result = Run("skill", "upgrade", "--codex-home", layout.CodexHome);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            "Existing AEC skill is not an exact supported official bundle",
            result.Error,
            StringComparison.Ordinal);
        Assert.Equal(predecessor, File.ReadAllBytes(layout.Skill));
        Assert.Equal(metadataBefore, File.ReadAllBytes(layout.Metadata));
    }

    [Fact]
    public void SuccessfulUpgradePreservesUnmanagedCodexFilesAndUnixMode()
    {
        using var layout = new UpgradeLayout();
        File.WriteAllBytes(layout.Skill, ReadFixture("0.9.0"));
        var runtimeAgents = Path.Combine(layout.CodexHome, "AGENTS.md");
        var runtimeConfig = Path.Combine(layout.CodexHome, "config.toml");
        var notes = Path.Combine(layout.SkillRoot, "notes.md");
        var otherSkill = Path.Combine(layout.CodexHome, "skills", "other", "SKILL.md");
        Directory.CreateDirectory(Path.GetDirectoryName(otherSkill)!);
        File.WriteAllText(runtimeAgents, "runtime instructions\n");
        File.WriteAllText(runtimeConfig, "model = \"local\"\n");
        File.WriteAllText(notes, "personal notes\n");
        File.WriteAllText(otherSkill, "other skill\n");
        var sentinels = new[] { runtimeAgents, runtimeConfig, notes, otherSkill }
            .ToDictionary(path => path, File.ReadAllBytes);
        UnixFileMode? expectedMode = null;
        if (!OperatingSystem.IsWindows())
        {
            expectedMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            File.SetUnixFileMode(layout.Skill, expectedMode.Value);
        }

        var result = Run("skill", "upgrade", "--codex-home", layout.CodexHome);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"upgraded{Environment.NewLine}", result.Output);
        foreach (var (path, content) in sentinels)
        {
            Assert.Equal(content, File.ReadAllBytes(path));
        }

        Assert.Equal(expectedMode, GetUnixMode(layout.Skill));
    }

    [Fact]
    public void OversizedMetadataFailsBeforeChangingARecognizedSkill()
    {
        using var layout = new UpgradeLayout();
        var skillBefore = ReadFixture("0.9.0");
        File.WriteAllBytes(layout.Skill, skillBefore);
        File.WriteAllBytes(
            layout.Metadata,
            Enumerable.Repeat((byte)'x', AecApplication.MaximumTextBytes + 1).ToArray());

        var result = Run("skill", "upgrade", "--codex-home", layout.CodexHome);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("exceeds 1 MiB", result.Error, StringComparison.Ordinal);
        Assert.Equal(skillBefore, File.ReadAllBytes(layout.Skill));
        Assert.Equal(AecApplication.MaximumTextBytes + 1, new FileInfo(layout.Metadata).Length);
        AssertNoTemporaryFiles(layout);
    }

    [Fact]
    public void LinkedMetadataFailsBeforeChangingARecognizedSkill()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var layout = new UpgradeLayout();
        var skillBefore = ReadFixture("0.9.0");
        File.WriteAllBytes(layout.Skill, skillBefore);
        var referent = Path.Combine(layout.Root, "external metadata.yaml");
        var referentBytes = File.ReadAllBytes(layout.Metadata);
        File.WriteAllBytes(referent, referentBytes);
        File.Delete(layout.Metadata);
        File.CreateSymbolicLink(layout.Metadata, referent);

        var result = Run("skill", "upgrade", "--codex-home", layout.CodexHome);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("must not be a symbolic link", result.Error, StringComparison.Ordinal);
        Assert.Equal(skillBefore, File.ReadAllBytes(layout.Skill));
        Assert.Equal(referentBytes, File.ReadAllBytes(referent));
    }

    [Fact]
    public void LinkedAgentsDirectoryFailsBeforeChangingARecognizedSkill()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var layout = new UpgradeLayout();
        var skillBefore = ReadFixture("0.9.0");
        File.WriteAllBytes(layout.Skill, skillBefore);
        var agentsDirectory = Path.GetDirectoryName(layout.Metadata)!;
        var referentDirectory = Path.Combine(layout.Root, "external agents");
        Directory.Move(agentsDirectory, referentDirectory);
        Directory.CreateSymbolicLink(agentsDirectory, referentDirectory);
        var metadataBefore = File.ReadAllBytes(layout.Metadata);

        var result = Run("skill", "upgrade", "--codex-home", layout.CodexHome);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("must not contain a symbolic link", result.Error, StringComparison.Ordinal);
        Assert.Equal(skillBefore, File.ReadAllBytes(layout.Skill));
        Assert.Equal(metadataBefore, File.ReadAllBytes(Path.Combine(referentDirectory, "openai.yaml")));
    }

    [Fact]
    public void ExplicitCodexHomeOverridesEnvironmentSelection()
    {
        using var selected = new UpgradeLayout();
        using var poison = new UpgradeLayout();
        var selectedOld = ReadFixture("0.9.0");
        var poisonOld = ReadFixture("0.10.0");
        File.WriteAllBytes(selected.Skill, selectedOld);
        File.WriteAllBytes(poison.Skill, poisonOld);
        var previous = Environment.GetEnvironmentVariable("CODEX_HOME");

        try
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", poison.CodexHome);
            var result = Run("skill", "upgrade", "--codex-home", selected.CodexHome);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal($"upgraded{Environment.NewLine}", result.Output);
            Assert.NotEqual(selectedOld, File.ReadAllBytes(selected.Skill));
            Assert.Equal(poisonOld, File.ReadAllBytes(poison.Skill));
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", previous);
        }
    }

    [Fact]
    public void EmptyCodexHomeFailsWithoutInstallingTheSkill()
    {
        var root = Path.Combine(
            OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(),
            "aec-upgrade-empty-tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var result = Run("skill", "upgrade", "--codex-home", root);

            Assert.Equal(1, result.ExitCode);
            Assert.Empty(result.Output);
            Assert.Contains("Codex skills directory does not exist", result.Error, StringComparison.Ordinal);
            Assert.False(Directory.Exists(Path.Combine(root, "skills")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void RejectsRepositoryArguments()
    {
        using var layout = new UpgradeLayout();

        var result = Run(
            "skill",
            "upgrade",
            "--codex-home",
            layout.CodexHome,
            "--repo",
            layout.Root);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Equal(
            $"error: Unknown argument: --repo{Environment.NewLine}",
            result.Error);
    }

    [Theory]
    [MemberData(nameof(InvalidArguments))]
    public void RejectsInvalidSkillUpgradeArguments(string[] arguments, string message)
    {
        var result = Run(arguments);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Equal($"error: {message}{Environment.NewLine}", result.Error);
    }

    public static TheoryData<string[], string> InvalidArguments => new()
    {
        { ["skill"], "skill requires a subcommand. Use `aec help` for usage." },
        { ["skill", "unknown"], "Unknown skill command: unknown" },
        { ["skill", "upgrade", "--codex-home"], "--codex-home requires a value." },
        { ["skill", "upgrade", "--codex-home", ""], "--codex-home requires a non-empty path." },
        { ["skill", "upgrade", "--codex-home", "relative"], "--codex-home must be an absolute path." },
        {
            ["skill", "upgrade", "--codex-home", Path.GetTempPath(), "--codex-home", Path.GetTempPath()],
            "--codex-home may be specified only once."
        },
        { ["skill", "upgrade", "unexpected"], "Unknown argument: unexpected" },
        { ["skill", "upgrade", "--codex-home=/tmp/codex"], "Unknown argument: --codex-home=/tmp/codex" }
    };

    private static CommandResult Run(params string[] arguments)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = AecApplication.Run(arguments, output, error);
        return new CommandResult(exitCode, output.ToString(), error.ToString());
    }

    private sealed record CommandResult(int ExitCode, string Output, string Error);

    private static byte[] ReadFixture(string version)
    {
        return File.ReadAllBytes(Path.Combine(
            AppContext.BaseDirectory,
            "Fixtures",
            "Skills",
            $"{version}.md"));
    }

    private static string Hash(byte[] content)
    {
        return Convert.ToHexStringLower(SHA256.HashData(content));
    }

    private static UnixFileMode? GetUnixMode(string path)
    {
        return OperatingSystem.IsWindows() ? null : File.GetUnixFileMode(path);
    }

    private static void AssertNoTemporaryFiles(UpgradeLayout layout)
    {
        Assert.DoesNotContain(
            Directory.EnumerateFiles(layout.SkillRoot, "*", SearchOption.AllDirectories),
            path => Path.GetFileName(path).Contains(".aec-", StringComparison.Ordinal));
    }

    private sealed class UpgradeLayout : IDisposable
    {
        public UpgradeLayout()
        {
            Root = Path.Combine(
                OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(),
                "aec-upgrade-tests",
                Guid.NewGuid().ToString("N"));
            CodexHome = Path.Combine(Root, "codex home");
            Skill = Path.Combine(CodexHome, "skills", "aec", "SKILL.md");
            Metadata = Path.Combine(CodexHome, "skills", "aec", "agents", "openai.yaml");

            Directory.CreateDirectory(CodexHome);
            AecSkillInstaller.Install(CodexHome);
        }

        public string Root { get; }

        public string CodexHome { get; }

        public string Skill { get; }

        public string Metadata { get; }

        public string SkillRoot => Path.GetDirectoryName(Skill)!;

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, recursive: true);
            }
        }
    }

}
