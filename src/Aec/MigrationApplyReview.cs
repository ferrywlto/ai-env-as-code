namespace Aec;

// Compare advisory review data for both migration writers. Successful equality
// is not write permission: each writer still validates fresh mutable state.
internal static class MigrationApplyReview
{
    public static void Verify(MigrationApplyPreview reviewed, MigrationApplyPreview fresh)
    {
        RequireSameValue(reviewed.Branch, fresh.Branch, "branch");
        if (reviewed.Harness != fresh.Harness) throw Changed("local harness");
        var expected = reviewed.Apply.Snapshot;
        var current = fresh.Apply.Snapshot;
        RequireSameValue(expected.Repository, current.Repository, "repository");
        RequireSameValue(expected.Platform, current.Platform, "platform");
        RequireSameValue(expected.Provider, current.Provider, "provider");
        RequireSameValue(expected.RuntimePath, current.RuntimePath, "runtime path");
        RequireSameValue(expected.Commit, current.Commit, "commit");
        if (expected.BlockVersion != current.BlockVersion) throw Changed("runtime AEC block version");
        RequireSameBytes(expected.Target, current.Target, "committed target");
        RequireSameBytes(expected.Runtime, current.Runtime, "runtime instructions");
        RequireSameValue(reviewed.Apply.RuntimePath, fresh.Apply.RuntimePath, "apply destination");
        RequireSameBytes(reviewed.Apply.DesiredRuntime, fresh.Apply.DesiredRuntime, "desired instructions");
        if (reviewed.Apply.WouldReplace != fresh.Apply.WouldReplace) throw Changed("apply action");

        if (reviewed.RequiredSources.Count != fresh.RequiredSources.Count) throw Changed("required sources");
        for (var index = 0; index < fresh.RequiredSources.Count; index++)
        {
            var expectedSource = reviewed.RequiredSources[index];
            var currentSource = fresh.RequiredSources[index];
            RequireSameValue(expectedSource.RelativePath, currentSource.RelativePath, "source path");
            RequireSameValue(expectedSource.CommittedMode, currentSource.CommittedMode, "source Git mode");
            RequireSameBytes(expectedSource.CommittedContent, currentSource.CommittedContent, "committed source");
            RequireSameBytes(expectedSource.WorkingContent, currentSource.WorkingContent, "working source");
        }

        VerifyConfig(reviewed.Apply.CodexConfig, fresh.Apply.CodexConfig);
    }

    private static void VerifyConfig(PortableCodexConfigPreview? expected, PortableCodexConfigPreview? current)
    {
        if (expected is null)
        {
            if (current is not null) throw Changed("config proposal");
            return;
        }
        if (current is null) throw Changed("config proposal");
        RequireSameValue(expected.RuntimePath, current.RuntimePath, "config destination");
        RequireSameBytes(expected.Canonical, current.Canonical, "canonical config");
        RequireSameBytes(expected.Runtime, current.Runtime, "runtime config");
        RequireSameBytes(expected.DesiredRuntime, current.DesiredRuntime, "desired config");
        if (expected.WouldReplace != current.WouldReplace) throw Changed("config action");
        if (expected.Inserted != current.Inserted) throw Changed("config insertion");
    }

    private static void RequireSameValue(string? expected, string? current, string input)
    {
        if (!string.Equals(expected, current, StringComparison.Ordinal)) throw Changed(input);
    }

    private static void RequireSameBytes(byte[]? expected, byte[]? current, string input)
    {
        if (expected is null)
        {
            if (current is not null) throw Changed(input);
            return;
        }
        if (current is null || !expected.AsSpan().SequenceEqual(current)) throw Changed(input);
    }

    private static InvalidOperationException Changed(string input) =>
        new($"Migration apply {input} changed after preview; no runtime file was replaced.");
}
