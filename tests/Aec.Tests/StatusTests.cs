using System.Text;

namespace Aec.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ProcessStateCollection
{
    public const string Name = "Process state";
}

[Collection(ProcessStateCollection.Name)]
public sealed class StatusTests
{
    [Fact]
    public void ReportsInSyncForEqualFiles()
    {
        using var layout = new TemporaryLayout("same\n", "same\n");

        var result = Run(layout);

        Assert.Equal(0, result.ExitCode);
        Assert.Equal($"in_sync{Environment.NewLine}", result.Output);
        Assert.Empty(result.Error);
    }

    [Fact]
    public void ReportsDifferentForDifferentFiles()
    {
        using var layout = new TemporaryLayout("desired\n", "current\n");

        var result = Run(layout);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal($"different{Environment.NewLine}", result.Output);
        Assert.Empty(result.Error);
    }

    [Fact]
    public void ComparesExactBytesWithoutNormalizingLineEndings()
    {
        using var layout = new TemporaryLayout("same\n", "same\r\n");

        var result = Run(layout);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal($"different{Environment.NewLine}", result.Output);
    }

    [Fact]
    public void ReportsMissingWhenTheRuntimeTargetIsAbsent()
    {
        using var layout = new TemporaryLayout("desired\n", null);

        var result = Run(layout);

        Assert.Equal(2, result.ExitCode);
        Assert.Equal($"missing{Environment.NewLine}", result.Output);
        Assert.Empty(result.Error);
    }

    [Fact]
    public void FailsWhenTheCanonicalSourceIsAbsent()
    {
        using var layout = new TemporaryLayout(null, null);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("Canonical source does not exist", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsRelativeRepositoryPaths()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = AecApplication.Run(
            ["status", "--repo", "relative/path", "--codex-home", Path.GetTempPath()],
            output,
            error);

        Assert.Equal(1, exitCode);
        Assert.Empty(output.ToString());
        Assert.Contains("--repo must be an absolute path", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void ExplicitCodexHomeOverridesTheEnvironment()
    {
        using var layout = new TemporaryLayout("same\n", "same\n");
        using var other = new TemporaryLayout("unused\n", "different\n");
        var previous = Environment.GetEnvironmentVariable("CODEX_HOME");

        try
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", other.CodexHome);
            var result = Run(layout);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal($"in_sync{Environment.NewLine}", result.Output);
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", previous);
        }
    }

    [Fact]
    public void UsesCodexHomeFromTheEnvironmentWhenTheFlagIsAbsent()
    {
        using var layout = new TemporaryLayout("same\n", "same\n");
        var previous = Environment.GetEnvironmentVariable("CODEX_HOME");

        try
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", layout.CodexHome);
            var output = new StringWriter();
            var error = new StringWriter();

            var exitCode = AecApplication.Run(
                ["status", "--repo", layout.Repository],
                output,
                error);

            Assert.Equal(0, exitCode);
            Assert.Equal($"in_sync{Environment.NewLine}", output.ToString());
            Assert.Empty(error.ToString());
        }
        finally
        {
            Environment.SetEnvironmentVariable("CODEX_HOME", previous);
        }
    }

    [Fact]
    public void AbsoluteRootsAreIndependentOfTheWorkingDirectory()
    {
        using var layout = new TemporaryLayout("same\n", "same\n");
        var unrelatedDirectory = Path.Combine(layout.Root, "unrelated");
        Directory.CreateDirectory(unrelatedDirectory);
        var previous = Environment.CurrentDirectory;

        try
        {
            Environment.CurrentDirectory = unrelatedDirectory;
            var result = Run(layout);

            Assert.Equal(0, result.ExitCode);
            Assert.Equal($"in_sync{Environment.NewLine}", result.Output);
        }
        finally
        {
            Environment.CurrentDirectory = previous;
        }
    }

    [Fact]
    public void MissingCodexHomeIsAnError()
    {
        using var layout = new TemporaryLayout("desired\n", null);
        Directory.Delete(layout.CodexHome);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("Codex home does not exist", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsSourceFilesOverOneMiB()
    {
        using var layout = new TemporaryLayout(null, null);
        File.WriteAllBytes(layout.Source, new byte[(1024 * 1024) + 1]);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("exceeds 1 MiB", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsDuplicateRepositoryArguments()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = AecApplication.Run(
            ["status", "--repo", Path.GetTempPath(), "--repo", Path.GetTempPath()],
            output,
            error);

        Assert.Equal(1, exitCode);
        Assert.Empty(output.ToString());
        Assert.Contains("--repo may be specified only once", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsAnEmptyExplicitCodexHome()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = AecApplication.Run(
            ["status", "--repo", Path.GetTempPath(), "--codex-home", ""],
            output,
            error);

        Assert.Equal(1, exitCode);
        Assert.Empty(output.ToString());
        Assert.Contains("--codex-home requires a non-empty path", error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsSymbolicLinkSources()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }

        using var layout = new TemporaryLayout(null, null);
        var externalSource = Path.Combine(layout.Root, "external-agents.md");
        File.WriteAllText(externalSource, "content\n");
        File.CreateSymbolicLink(layout.Source, externalSource);

        var result = Run(layout);

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains("must not be a symbolic link", result.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void StatusDoesNotChangeSourceOrTargetContent()
    {
        using var layout = new TemporaryLayout("desired\n", "current\n");
        var sourceBefore = File.ReadAllBytes(layout.Source);
        var targetBefore = File.ReadAllBytes(layout.Target);
        var filesBefore = Directory.GetFiles(layout.Root, "*", SearchOption.AllDirectories);

        _ = Run(layout);

        Assert.Equal(sourceBefore, File.ReadAllBytes(layout.Source));
        Assert.Equal(targetBefore, File.ReadAllBytes(layout.Target));
        Assert.Equal(filesBefore, Directory.GetFiles(layout.Root, "*", SearchOption.AllDirectories));
    }

    private static CommandResult Run(TemporaryLayout layout)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        var exitCode = AecApplication.Run(
            ["status", "--repo", layout.Repository, "--codex-home", layout.CodexHome],
            output,
            error);

        return new CommandResult(exitCode, output.ToString(), error.ToString());
    }

    private sealed record CommandResult(int ExitCode, string Output, string Error);

    private sealed class TemporaryLayout : IDisposable
    {
        public TemporaryLayout(string? source, string? target)
        {
            Root = Path.Combine(Path.GetTempPath(), "aec-tests", Guid.NewGuid().ToString("N"));
            Repository = Path.Combine(Root, "data");
            CodexHome = Path.Combine(Root, "codex-home");
            Source = Path.Combine(Repository, "environment", "providers", "codex", "AGENTS.md");
            Target = Path.Combine(CodexHome, "AGENTS.md");

            Directory.CreateDirectory(Path.GetDirectoryName(Source)!);
            Directory.CreateDirectory(CodexHome);

            if (source is not null)
            {
                File.WriteAllText(Source, source, new UTF8Encoding(false));
            }

            if (target is not null)
            {
                File.WriteAllText(Target, target, new UTF8Encoding(false));
            }
        }

        public string Root { get; }

        public string Repository { get; }

        public string CodexHome { get; }

        public string Source { get; }

        public string Target { get; }

        public void Dispose()
        {
            if (Directory.Exists(Root))
            {
                Directory.Delete(Root, true);
            }
        }
    }
}
