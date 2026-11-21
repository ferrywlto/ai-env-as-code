using System.Text;

namespace Aec.Tests;

[Trait("Category", "Unit")]
public sealed class MigrationPortableCapturePlanTests
{
    private const string Runtime = "/runtime/codex/AGENTS.md";
    private const string RuntimeConfig = "/runtime/codex/config.toml";
    private const string CanonicalConfig = "environment/providers/codex/config.toml";
    private const string Source = "environment/shared/instructions.md";
    private const string Target = "environment/targets/macos-arm64/codex/AGENTS.md";

    [Theory]
    [InlineData(Runtime, "Later runtime edit.")]
    [InlineData(RuntimeConfig, "personality = 'pragmatic'")]
    [InlineData(Runtime, null)]
    [InlineData(RuntimeConfig, "")]
    public void ChangedRuntimeInputsAreRejected(string path, string? current)
    {
        var reviewed = new Dictionary<string, byte[]?> { [path] = "Reviewed input."u8.ToArray() };
        var observed = new Dictionary<string, byte[]?> { [path] = Bytes(current) };

        var error = Assert.Throws<InvalidOperationException>(() =>
            MigrationPortableCapturePlan.VerifyRuntimeFiles(reviewed, file => observed[file]));

        Assert.Contains($"Runtime capture input changed: {path}", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MatchingRuntimeBytesWithIndependentBuffersAreAccepted()
    {
        var reviewed = new Dictionary<string, byte[]?>
        {
            [Runtime] = "Reviewed instructions."u8.ToArray(),
            [RuntimeConfig] = null
        };
        var observed = new Dictionary<string, byte[]?>
        {
            [Runtime] = "Reviewed instructions."u8.ToArray(),
            [RuntimeConfig] = null
        };

        MigrationPortableCapturePlan.VerifyRuntimeFiles(reviewed, file => observed[file]);
    }

    [Theory]
    [InlineData(CanonicalConfig, null, false, false)]
    [InlineData(CanonicalConfig, "Later config edit.", false, false)]
    [InlineData(Source, "Proposed source.", false, false)]
    [InlineData(Source, "Original input.", true, false)]
    [InlineData(Target, "Later target edit.", false, false)]
    [InlineData(Target, "Original input.", false, true)]
    public void CanonicalInputsMustMatchTheirOriginalOrCompletedBytes(
        string path, string? current, bool sourceWasWritten, bool targetWasWritten)
    {
        var originals = new Dictionary<string, byte[]?> { [path] = "Original input."u8.ToArray() };
        var proposals = new Dictionary<string, byte[]> { [Source] = "Proposed source."u8.ToArray() };
        var observed = new Dictionary<string, byte[]?> { [path] = Bytes(current) };
        string[] writtenSources = sourceWasWritten ? [Source] : [];
        var writtenTargets = targetWasWritten
            ? new Dictionary<string, byte[]> { [Target] = "Rendered target."u8.ToArray() }
            : null;

        var error = Assert.Throws<InvalidOperationException>(() => MigrationPortableCapturePlan.VerifyCanonicalFiles(
            originals, proposals, writtenSources, writtenTargets, file => observed[file]));

        Assert.Contains($"Portable canonical input changed: {path}", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void MatchingOriginalAndCompletedCanonicalBytesAreAccepted()
    {
        var originals = new Dictionary<string, byte[]?>
        {
            [CanonicalConfig] = "Original config."u8.ToArray(),
            [Source] = "Original source."u8.ToArray(),
            [Target] = "Original target."u8.ToArray()
        };
        var proposals = new Dictionary<string, byte[]> { [Source] = "Proposed source."u8.ToArray() };
        var writtenTargets = new Dictionary<string, byte[]> { [Target] = "Rendered target."u8.ToArray() };
        var observed = new Dictionary<string, byte[]?>
        {
            [CanonicalConfig] = "Original config."u8.ToArray(),
            [Source] = "Proposed source."u8.ToArray(),
            [Target] = "Rendered target."u8.ToArray()
        };

        MigrationPortableCapturePlan.VerifyCanonicalFiles(
            originals, proposals, [Source], writtenTargets, file => observed[file]);
    }

    private static byte[]? Bytes(string? value) => value is null ? null : Encoding.UTF8.GetBytes(value);
}
