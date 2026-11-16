namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class PortableCopilotBackupCommandTests
{
    [Fact]
    public void OneSectionDriftCommitsSourceAndAffectedTargetWithoutChangingRuntime()
    {
        using var layout = NewLayout();
        File.WriteAllText(layout.RuntimePath, File.ReadAllText(layout.RuntimePath)
            .Replace("Shared approval.", "Shared revision.", StringComparison.Ordinal));
        var runtime = File.ReadAllBytes(layout.RuntimePath);
        var oldHead = layout.Head();

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"committed {layout.Head()}{Environment.NewLine}", result.Output);
        Assert.Empty(result.Error);
        Assert.Equal(oldHead, GitProcess.RunRequired(layout.Repository,
            "Could not inspect backup parent", "rev-parse", "HEAD^").Output.Trim());
        Assert.Equal("Backup portable instructions", GitProcess.RunRequired(layout.Repository,
            "Could not inspect backup subject", "log", "-1", "--format=%s")
            .Output.Trim());
        Assert.Equal(new[]
        {
            "environment/shared/instructions.md",
            Path.GetRelativePath(layout.Repository, layout.TargetPath).Replace('\\', '/')
        }.OrderBy(path => path, StringComparer.Ordinal), CommittedPaths(layout));
        Assert.Equal("Shared revision.\n", File.ReadAllText(layout.SharedSource));
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void UnchangedPortableRuntimeDoesNotCommit()
    {
        using var layout = NewLayout();
        var head = layout.Head();

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"unchanged{Environment.NewLine}", result.Output);
        Assert.Empty(result.Error);
        Assert.Equal(head, layout.Head());
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void MissingEnrollmentDoesNotFallBackToLegacyCanonicalFile()
    {
        using var layout = NewLayout();
        var legacy = Path.Combine(layout.Repository, AecApplication.CopilotSourceRelativePath);
        File.WriteAllBytes(legacy, CopilotInstructionBlock.Merge(
            "Legacy source.\n"u8.ToArray(), layout.Repository));
        GitProcess.RunRequired(layout.Repository, "Could not stage legacy source",
            "add", "--", AecApplication.CopilotSourceRelativePath);
        GitProcess.RunRequired(layout.Repository, "Could not remove enrolled target",
            "rm", "--", Path.GetRelativePath(layout.Repository, layout.TargetPath));
        GitProcess.RunRequired(layout.Repository, "Could not commit fixture change",
            "-c", "user.name=AEC Test", "-c", "user.email=aec-test@example.invalid",
            "-c", "commit.gpgsign=false", "commit", "-m", "Remove enrollment");
        var head = layout.Head();
        var runtime = File.ReadAllBytes(layout.RuntimePath);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("Committed target", result.Error, StringComparison.Ordinal);
        Assert.Equal(head, layout.Head());
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void MultipleEditedSectionsStopWithoutCommit()
    {
        using var layout = NewLayout();
        File.WriteAllText(layout.RuntimePath, File.ReadAllText(layout.RuntimePath)
            .Replace("Shared approval.", "Shared revision.", StringComparison.Ordinal)
            .Replace("Local paths.", "Local revision.", StringComparison.Ordinal));
        var head = layout.Head();

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("multiple authored instruction sections", result.Error,
            StringComparison.Ordinal);
        Assert.Equal(head, layout.Head());
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void StaleLastAppliedTargetStopsBeforeCapture()
    {
        using var layout = NewLayout();
        File.WriteAllText(layout.SharedSource, "Shared latest.\n");
        File.WriteAllText(layout.TargetPath, File.ReadAllText(layout.TargetPath)
            .Replace("Shared approval.", "Shared latest.", StringComparison.Ordinal));
        GitProcess.RunRequired(layout.Repository, "Could not stage newer target",
            "add", "--", "environment/shared/instructions.md",
            Path.GetRelativePath(layout.Repository, layout.TargetPath));
        GitProcess.RunRequired(layout.Repository, "Could not commit newer target",
            "-c", "user.name=AEC Test", "-c", "user.email=aec-test@example.invalid",
            "-c", "commit.gpgsign=false", "commit", "-m", "Newer target");
        var head = layout.Head();
        var runtime = File.ReadAllBytes(layout.RuntimePath);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("older target", result.Error, StringComparison.Ordinal);
        Assert.Equal(head, layout.Head());
        Assert.Equal(runtime, File.ReadAllBytes(layout.RuntimePath));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void RuntimeBoundToAnotherRepositoryStopsBeforeCapture()
    {
        using var layout = NewLayout();
        var otherRepository = Path.Combine(layout.Root, "other-data");
        var target = File.ReadAllBytes(layout.TargetPath);
        File.WriteAllBytes(layout.RuntimePath,
            CopilotInstructionBlock.MergeForPortableTarget(
                target, otherRepository, RenderCommand.CurrentPlatform()));
        var head = layout.Head();

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("not bound to the selected AEC repository", result.Error,
            StringComparison.Ordinal);
        Assert.Equal(head, layout.Head());
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    [Fact]
    public void ExistingStagedWorkStopsBeforeCanonicalWrite()
    {
        using var layout = NewLayout();
        File.WriteAllText(layout.RuntimePath, File.ReadAllText(layout.RuntimePath)
            .Replace("Shared approval.", "Shared revision.", StringComparison.Ordinal));
        File.WriteAllText(Path.Combine(layout.Repository, "note.txt"), "Keep staged.\n");
        GitProcess.RunRequired(layout.Repository, "Could not stage fixture note",
            "add", "--", "note.txt");
        var head = layout.Head();
        var source = File.ReadAllBytes(layout.SharedSource);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("already has staged changes", result.Error,
            StringComparison.Ordinal);
        Assert.Equal(head, layout.Head());
        Assert.Equal(source, File.ReadAllBytes(layout.SharedSource));
        Assert.Equal("note.txt\n", GitProcess.RunRequired(layout.Repository,
            "Could not inspect staged paths", "diff", "--cached", "--name-only").Output);
    }

    [Fact]
    public void DetachedHeadStopsBeforeCanonicalWrite()
    {
        using var layout = NewLayout();
        File.WriteAllText(layout.RuntimePath, File.ReadAllText(layout.RuntimePath)
            .Replace("Shared approval.", "Shared revision.", StringComparison.Ordinal));
        GitProcess.RunRequired(layout.Repository, "Could not detach fixture HEAD",
            "switch", "--detach", "--quiet");
        var head = layout.Head();
        var source = File.ReadAllBytes(layout.SharedSource);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("detached HEAD", result.Error, StringComparison.Ordinal);
        Assert.Equal(head, layout.Head());
        Assert.Equal(source, File.ReadAllBytes(layout.SharedSource));
        Assert.Equal(string.Empty, layout.GitStatus());
    }

    private static SharedTargetStatusTests.StatusLayout NewLayout()
    {
        var layout = new SharedTargetStatusTests.StatusLayout("copilot");
        layout.WriteRuntime(portableBlock: true);
        GitProcess.RunRequired(layout.Repository, "Could not set fixture Git author",
            "config", "--local", "user.name", "AEC Test");
        GitProcess.RunRequired(layout.Repository, "Could not set fixture Git email",
            "config", "--local", "user.email", "aec-test@example.invalid");
        GitProcess.RunRequired(layout.Repository, "Could not disable fixture signing",
            "config", "--local", "commit.gpgsign", "false");
        return layout;
    }

    private static CommandResult Run(SharedTargetStatusTests.StatusLayout layout) =>
        TestApplication.Run("backup", "--repo", layout.Repository, "--provider=copilot",
            "--copilot-home", layout.ProviderHome);

    private static string[] CommittedPaths(SharedTargetStatusTests.StatusLayout layout) =>
        GitProcess.RunRequired(layout.Repository, "Could not inspect backup paths",
            "diff-tree", "--no-commit-id", "--name-only", "-r", "HEAD")
            .Output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .OrderBy(path => path, StringComparer.Ordinal).ToArray();
}
