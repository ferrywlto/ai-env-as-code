namespace Aec.Tests;

public sealed class AecIdentifiersTests
{
    [Fact]
    public void ProviderAndPlatformValuesRemainStable()
    {
        Assert.Equal("codex", AecIdentifiers.Codex);
        Assert.Equal("copilot", AecIdentifiers.Copilot);
        Assert.Equal("chatgpt", AecIdentifiers.ChatGpt);
        Assert.Equal("macos-arm64", AecIdentifiers.MacOsArm64);
        Assert.Equal("windows-x64", AecIdentifiers.WindowsX64);
        Assert.Equal("linux-arm64", AecIdentifiers.LinuxArm64);
    }
}
