using System.Security.Cryptography;
using System.Text;

namespace Aec.Tests;

public sealed class CopilotInstructionBlockTests
{
    private static readonly string Repository = Path.Combine(
        Path.GetTempPath(),
        "aec-copilot-block-tests",
        "repository");

    [Fact]
    public void PrependsTheManagedBlockWithoutChangingBomOrCrLfBytes()
    {
        var original = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat("# Existing\r\nKeep this.\r\n"u8.ToArray())
            .ToArray();

        var merged = CopilotInstructionBlock.Merge(original, Repository);

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, merged[..3]);
        Assert.Contains(
            "<!-- AEC:COPILOT:BEGIN version=1 -->\r\n",
            System.Text.Encoding.UTF8.GetString(merged),
            StringComparison.Ordinal);
        Assert.True(merged.AsSpan(3).EndsWith("# Existing\r\nKeep this.\r\n"u8));
    }

    [Fact]
    public void RecognizesOnlyTheExactEmittedBlock()
    {
        var managed = CopilotInstructionBlock.Merge("Original.\n"u8.ToArray(), Repository);

        Assert.Equal(Repository, CopilotInstructionBlock.ReadRepositoryBinding(managed));
        Assert.Equal(managed, CopilotInstructionBlock.Merge(managed, Repository));

        var malformed = "<!-- AEC:COPILOT:BEGIN version=1 -->\ninvalid\n<!-- AEC:COPILOT:END -->\n"u8.ToArray();
        Assert.Throws<InvalidDataException>(() => CopilotInstructionBlock.ReadRepositoryBinding(malformed));
    }

    [Fact]
    public void PortableBlockPointsToTheSelectedTargetWithoutChangingTheLegacyGenerator()
    {
        var original = "Authored instructions.\n"u8.ToArray();
        var managed = CopilotInstructionBlock.MergeForPortableTarget(
            original, Repository, "windows-x64");
        var text = Encoding.UTF8.GetString(managed);
        var binding = CopilotInstructionBlock.ReadManagedBinding(managed);

        Assert.Equal(3, binding?.Version);
        Assert.Equal("windows-x64", binding?.Platform);
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(original)),
            binding?.TargetDigest);
        Assert.Equal(Repository, CopilotInstructionBlock.ReadRepositoryBinding(managed));
        Assert.Contains(Path.Combine(Repository, "environment", "targets", "windows-x64",
            "copilot", "copilot-instructions.md"), text, StringComparison.Ordinal);
        Assert.Contains("Edit the authored shared, platform, or Copilot overlay source", text,
            StringComparison.Ordinal);
        Assert.DoesNotContain(Path.Combine(Repository, AecApplication.CopilotSourceRelativePath),
            text, StringComparison.Ordinal);
        Assert.Equal(managed, CopilotInstructionBlock.MergeForPortableTarget(
            managed, Repository, "windows-x64"));
        Assert.Throws<InvalidDataException>(() => CopilotInstructionBlock.Merge(managed, Repository));
        Assert.Contains("version=1", Encoding.UTF8.GetString(
            CopilotInstructionBlock.Merge(original, Repository)), StringComparison.Ordinal);
    }

    [Fact]
    public void StampedPortableBlockPreservesBomAndCrLfTargetBytes()
    {
        var original = new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat("# Existing\r\nKeep this.\r\n"u8.ToArray())
            .ToArray();

        var managed = CopilotInstructionBlock.MergeForPortableTarget(
            original, Repository, "macos-arm64");
        var binding = CopilotInstructionBlock.ReadManagedBinding(managed);

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, managed[..3]);
        Assert.Contains("<!-- AEC:COPILOT:BEGIN version=3 -->\r\n",
            Encoding.UTF8.GetString(managed), StringComparison.Ordinal);
        Assert.True(managed.AsSpan().EndsWith(original.AsSpan(3)));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(original)),
            binding?.TargetDigest);
        Assert.Equal(managed, CopilotInstructionBlock.MergeForPortableTarget(
            managed, Repository, "macos-arm64"));
    }

    [Fact]
    public void EarlierPortableBlockRemainsReadableAndCanUpgradeWithoutChangingTheBody()
    {
        var original = "Authored instructions.\n"u8.ToArray();
        var stamped = CopilotInstructionBlock.MergeForPortableTarget(
            original, Repository, "macos-arm64");
        var digest = CopilotInstructionBlock.ReadManagedBinding(stamped)!.TargetDigest!;
        var earlier = Encoding.UTF8.GetString(stamped)
            .Replace("version=3", "version=2", StringComparison.Ordinal)
            .Replace($"The last-applied target SHA-256 is `{digest}`.\n", string.Empty,
                StringComparison.Ordinal);
        var earlierBytes = Encoding.UTF8.GetBytes(earlier);

        var earlierBinding = CopilotInstructionBlock.ReadManagedBinding(earlierBytes);
        var upgraded = CopilotInstructionBlock.MergeForPortableTarget(
            earlierBytes, Repository, "macos-arm64");

        Assert.Equal(2, earlierBinding?.Version);
        Assert.Null(earlierBinding?.TargetDigest);
        Assert.Equal(stamped, upgraded);
        Assert.Equal(original, upgraded.AsSpan(upgraded.Length - original.Length).ToArray());
    }

    [Theory]
    [InlineData("not-a-sha256")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public void PortableBindingRejectsMalformedTargetDigest(string invalidDigest)
    {
        var managed = CopilotInstructionBlock.MergeForPortableTarget(
            "Authored instructions.\n"u8.ToArray(), Repository, "macos-arm64");
        var digest = CopilotInstructionBlock.ReadManagedBinding(managed)!.TargetDigest!;
        var tampered = Encoding.UTF8.GetString(managed)
            .Replace(digest, invalidDigest, StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() =>
            CopilotInstructionBlock.ReadManagedBinding(Encoding.UTF8.GetBytes(tampered)));
    }

    [Fact]
    public void PortableBindingRejectsTamperingAndUnsupportedPlatform()
    {
        var managed = CopilotInstructionBlock.MergeForPortableTarget(
            [], Repository, "linux-arm64");
        var tampered = System.Text.Encoding.UTF8.GetString(managed).Replace(
            Path.Combine("environment", "targets", "linux-arm64", "copilot",
                "copilot-instructions.md"),
            Path.Combine("environment", "targets", "linux-arm64", "codex",
                "copilot-instructions.md"),
            StringComparison.Ordinal);

        Assert.Throws<InvalidDataException>(() => CopilotInstructionBlock.ReadRepositoryBinding(
            System.Text.Encoding.UTF8.GetBytes(tampered)));
        Assert.Throws<ArgumentException>(() => CopilotInstructionBlock.MergeForPortableTarget(
            [], Repository, "../another-platform"));
    }

    [Fact]
    public void PortableBlockRejectsRuntimeContentBeyondTheManagedTextLimit()
    {
        var content = new byte[AecApplication.MaximumTextBytes];
        Array.Fill(content, (byte)'x');

        Assert.Throws<InvalidDataException>(() =>
            CopilotInstructionBlock.MergeForPortableTarget(
                content, Repository, "macos-arm64"));
    }
}
