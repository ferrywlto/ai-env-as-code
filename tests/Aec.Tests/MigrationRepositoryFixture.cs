using System.Text;

namespace Aec.Tests;

internal sealed class MigrationRepositoryFixture : IDisposable
{
    private readonly string? previousCodex = Environment.GetEnvironmentVariable("CODEX_HOME");
    private readonly string? previousCopilot = Environment.GetEnvironmentVariable("COPILOT_HOME");
    private readonly string writer;

    public MigrationRepositoryFixture(bool includeCopilotCanonical = false)
    {
        Root = Path.Combine(OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath(),
            "aec-migration-forward-tests", Guid.NewGuid().ToString("N"));
        Repository = Path.Combine(Root, "data");
        var codex = Path.Combine(Root, "codex-home");
        var copilot = Path.Combine(Root, "copilot-home");
        Environment.SetEnvironmentVariable("CODEX_HOME", codex);
        Environment.SetEnvironmentVariable("COPILOT_HOME", copilot);
        Directory.CreateDirectory(codex);
        Directory.CreateDirectory(copilot);
        File.WriteAllText(Path.Combine(codex, "AGENTS.md"), "Personal instructions.\n");
        File.WriteAllText(Path.Combine(codex, "config.toml"), "personality = \"friendly\"\n");
        var initialized = TestApplication.Run("init", "--repo", Repository, "--codex-home", codex);
        Assert.True(initialized.ExitCode == 0, initialized.Error);
        // Empty Git templates intentionally omit info; cases below create
        // local attributes/ignore rules there without assuming Git made it.
        Directory.CreateDirectory(Path.Combine(Repository, ".git", "info"));
        Write("notes.md", "Committed fixture notes.\n");
        Git("add", "--", "notes.md");
        Git("commit", "--quiet", "-m", "Fixture notes");
        CopilotRuntime = Path.Combine(copilot, "copilot-instructions.md");
        File.WriteAllBytes(CopilotRuntime, CopilotInstructionBlock.Merge(Encoding.UTF8.GetBytes("Copilot instructions.\n"), Repository));
        if (includeCopilotCanonical)
        {
            var path = Path.Combine(Repository, AecApplication.CopilotSourceRelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, File.ReadAllBytes(CopilotRuntime));
            Git("add", "--", AecApplication.CopilotSourceRelativePath);
            Git("commit", "--quiet", "-m", "Fixture legacy Copilot enrollment");
        }
        var remote = Path.Combine(Root, "shared remote.git");
        Git("init", "--bare", "--quiet", "--template=", "--initial-branch=main", remote);
        Git("remote", "add", "shared", remote);
        Git("push", "--quiet", "--set-upstream", "shared", "main");
        writer = Path.Combine(Root, "remote-writer");
        Git("clone", "--no-local", "--quiet", "--origin", "shared", "--config", "core.autocrlf=false", remote, writer);
    }

    public string Root { get; }
    public string Repository { get; }
    public string CopilotRuntime { get; }
    public string Head() => Git("rev-parse", "HEAD").Trim();
    public string Git(params string[] args) => GitProcess.RunRequired(Repository, "Fast-forward fixture Git failed", args).Output;

    public void AdvanceRemote(Action<string>? edit = null)
    {
        if (edit is not null) edit(writer);
        else File.AppendAllText(Path.Combine(writer, "notes.md"), "Remote revision.\n");
        _ = GitProcess.RunRequired(writer, "Writer staging failed", "add", "--all");
        _ = GitProcess.RunRequired(writer, "Writer commit failed", "commit", "--quiet", "-m", "Remote revision");
        _ = GitProcess.RunRequired(writer, "Writer push failed", "push", "--quiet", "shared");
    }

    public (MigrationPreflight Preflight, MigrationFetchPreparation Fetched, IReadOnlyList<MigrationHarness> Harnesses) Prepare()
    {
        var preflight = MigrationPreflight.Read(Repository, "2.0");
        var harnesses = MigrationHarnessSelection.Read(preflight);
        var remote = MigrationRemotePreflight.Read(preflight);
        return (preflight, MigrationFetchPreparation.Read(preflight, remote), harnesses);
    }

    public void Write(string relative, string content)
    {
        var path = Path.Combine(Repository, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    public Dictionary<string, byte[]> RuntimeBytes() => Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
        .Where(path => path.StartsWith(Path.Combine(Root, "codex-home") + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || path.StartsWith(Path.Combine(Root, "copilot-home") + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        .ToDictionary(path => path, File.ReadAllBytes);

    public void AssertRuntime(Dictionary<string, byte[]> original)
    {
        var current = RuntimeBytes();
        Assert.Equal(original.Keys.Order(StringComparer.Ordinal), current.Keys.Order(StringComparer.Ordinal));
        foreach (var (path, bytes) in original) Assert.Equal(bytes, current[path]);
    }

    public void AssertNoChanges(Action action) => AssertOnlyChanges([], action);

    public void AssertOnlyChanges(IReadOnlyList<string> changedPaths, Action action)
    {
        var files = Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        var directories = Directory.GetDirectories(Root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToArray();
        var changed = changedPaths.Select(path => Path.Combine(Repository, path)).ToHashSet(StringComparer.Ordinal);
        foreach (var path in changed) Assert.Contains(path, files.Keys);
        action();
        Assert.Equal(files.Keys.Order(StringComparer.Ordinal), Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
        Assert.Equal(directories, Directory.GetDirectories(Root, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal));
        foreach (var (path, bytes) in files)
        {
            var current = File.ReadAllBytes(path);
            if (changed.Contains(path)) Assert.False(bytes.AsSpan().SequenceEqual(current));
            else Assert.Equal(bytes, current);
        }
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("CODEX_HOME", previousCodex);
        Environment.SetEnvironmentVariable("COPILOT_HOME", previousCopilot);
        TestDirectoryCleanup.Delete(Root);
    }
}
