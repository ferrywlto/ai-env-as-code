namespace Aec.Tests;

[Trait("Category", "Unit")]
public sealed class SharedEnrollmentDecisionTests
{
    [Theory]
    [InlineData("chatgpt", "--enroll-shared", null, "Unknown argument: --enroll-shared")]
    [InlineData("codex", "--force-path-change", "--enroll-shared", "cannot be combined")]
    [InlineData("codex", "--enroll-shared", "--enroll-shared", "only once")]
    public void RejectsUnsupportedEnrollmentArguments(string provider, string first, string? second, string message)
    {
        string[] arguments = [provider, "init", "--repo", "unused-relative-path", first];
        var result = TestApplication.Run(second is null ? arguments : [.. arguments, second]);

        Assert.Equal(1, result.ExitCode);
        Assert.Contains(message, result.Error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(AecIdentifiers.Codex)]
    [InlineData(AecIdentifiers.Copilot)]
    public void EnrollmentRequiresTheSelectedCheckoutBinding(string provider)
    {
        var selected = Path.GetFullPath("selected-checkout");
        var other = Path.GetFullPath("different-checkout");
        var instructions = "Personal instructions.\n"u8.ToArray();
        var runtime = provider == AecIdentifiers.Codex
            ? AecInstructionBlock.Merge(instructions, other)
            : CopilotInstructionBlock.Merge(instructions, other);

        SharedEnrollmentCommand.ValidateRuntimeBinding(other, provider, runtime);
        var error = Assert.Throws<InvalidOperationException>(() =>
            SharedEnrollmentCommand.ValidateRuntimeBinding(selected, provider, runtime));
        Assert.Contains("not bound to the selected AEC repository", error.Message, StringComparison.Ordinal);
    }
}
