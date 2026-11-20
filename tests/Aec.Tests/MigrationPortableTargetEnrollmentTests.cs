namespace Aec.Tests;

[Trait("Category", "Unit")]
public sealed class MigrationPortableTargetEnrollmentTests
{
    [Theory]
    [InlineData("environment/targets/unknown/codex/AGENTS.md", "platform")]
    [InlineData("environment/targets/macos-arm64/unknown/AGENTS.md", "provider")]
    [InlineData("environment/targets/macos-arm64/codex/wrong.md", "file name")]
    [InlineData("environment/targets/macos-arm64/wrong.md", "path structure")]
    public void InvalidCommittedTargetPathsAreRejected(string path, string problem)
    {
        var error = Assert.Throws<InvalidDataException>(() => MigrationPortableTargetWriter.ReadEnrollment(path));
        Assert.Equal($"Unsupported committed target {problem}: {path}", error.Message);
    }
}
