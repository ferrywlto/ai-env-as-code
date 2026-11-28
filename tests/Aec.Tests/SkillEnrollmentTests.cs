namespace Aec.Tests;

public sealed class SkillEnrollmentTests
{
    [Theory]
    [InlineData("codex", "AGENTS.md", "config.toml")]
    [InlineData("copilot", "copilot-instructions.md", "config.json")]
    public void InstallAndUninstallTouchOnlyProviderSkillFiles(
        string provider, string instructionsName, string configName)
    {
        var home = Path.Combine(TestTemporaryDirectory.Root, "skill-enrollment", Guid.NewGuid().ToString("N"));
        var instructions = Path.Combine(home, instructionsName);
        var config = Path.Combine(home, configName);
        var otherSkill = Path.Combine(home, "skills", "other", "SKILL.md");
        var skillRoot = Path.Combine(home, "skills", "aec");
        var skill = Path.Combine(skillRoot, "SKILL.md");
        var metadata = Path.Combine(skillRoot, "agents", "openai.yaml");
        byte[] instructionsBytes = [0xEF, 0xBB, 0xBF, .. "# Personal instructions\r\n"u8.ToArray()];
        byte[] configBytes = [0, 1, 2, 255];
        byte[] otherSkillBytes = "# Independent skill\n"u8.ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(otherSkill)!);
        File.WriteAllBytes(instructions, instructionsBytes);
        File.WriteAllBytes(config, configBytes);
        File.WriteAllBytes(otherSkill, otherSkillBytes);

        try
        {
            var install = TestApplication.Run(provider, "install", "--home", home);

            Assert.True(install.ExitCode == 0, install.Error);
            Assert.Contains("name: aec", File.ReadAllText(skill), StringComparison.Ordinal);
            Assert.Equal(provider == "codex", File.Exists(metadata));
            Assert.Equal(instructionsBytes, File.ReadAllBytes(instructions));
            Assert.Equal(configBytes, File.ReadAllBytes(config));
            Assert.Equal(otherSkillBytes, File.ReadAllBytes(otherSkill));

            var personalNote = Path.Combine(skillRoot, "notes.md");
            File.WriteAllText(personalNote, "Keep these local notes.");
            var uninstall = TestApplication.Run(provider, "uninstall", "--home", home);

            Assert.True(uninstall.ExitCode == 0, uninstall.Error);
            Assert.False(File.Exists(skill));
            Assert.False(File.Exists(metadata));
            Assert.Equal(instructionsBytes, File.ReadAllBytes(instructions));
            Assert.Equal(configBytes, File.ReadAllBytes(config));
            Assert.Equal(otherSkillBytes, File.ReadAllBytes(otherSkill));
            Assert.Equal("Keep these local notes.", File.ReadAllText(personalNote));
        }
        finally
        {
            TestDirectoryCleanup.Delete(home);
        }
    }

    [Theory]
    [InlineData("codex")]
    [InlineData("copilot")]
    public void CustomizedSkillRemainsIntactWhenUninstallationIsRefused(string provider)
    {
        var home = Path.Combine(TestTemporaryDirectory.Root, "skill-enrollment", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(home);

        try
        {
            var install = TestApplication.Run(provider, "install", "--home", home);
            Assert.True(install.ExitCode == 0, install.Error);
            var skill = Path.Combine(home, "skills", "aec", "SKILL.md");
            var metadata = Path.Combine(home, "skills", "aec", "agents", "openai.yaml");
            File.AppendAllText(skill, "\n# Local customization\n");
            var customSkill = File.ReadAllBytes(skill);
            var codexMetadata = provider == "codex" ? File.ReadAllBytes(metadata) : null;

            var uninstall = TestApplication.Run(provider, "uninstall", "--home", home);

            Assert.Equal(1, uninstall.ExitCode);
            Assert.Empty(uninstall.Output);
            Assert.Contains("exact supported official bundle", uninstall.Error, StringComparison.Ordinal);
            Assert.Equal(customSkill, File.ReadAllBytes(skill));
            if (codexMetadata is not null)
            {
                Assert.Equal(codexMetadata, File.ReadAllBytes(metadata));
            }
        }
        finally
        {
            TestDirectoryCleanup.Delete(home);
        }
    }
}
