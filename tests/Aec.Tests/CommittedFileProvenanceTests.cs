using System.Text;

namespace Aec.Tests;

[Collection(ProcessStateTestGroup.Name)]
public sealed class CommittedFileProvenanceTests
{
    [Fact]
    public void AcceptsColdIndexWithoutChangingRepository()
    {
        using var fixture = new Repository();
        fixture.Git("read-tree", "HEAD");

        Assert.Equal(Repository.Original, fixture.ReadUnchanged());
    }

    [Fact]
    public void AcceptsTimestampOnlyChangeWithoutRefreshingIndex()
    {
        using var fixture = new Repository();
        File.SetLastWriteTimeUtc(fixture.FilePath, DateTime.UtcNow.AddMinutes(-10));

        Assert.Equal(Repository.Original, fixture.ReadUnchanged());
    }

    [Fact]
    public void RejectsChangedBytesOfSameLength()
    {
        using var fixture = new Repository();
        File.WriteAllBytes(fixture.FilePath, Encoding.UTF8.GetBytes("changed!\n"));

        var error = Assert.Throws<InvalidOperationException>(() => fixture.ReadUnchanged());
        Assert.Contains("do not exactly match", error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectsWorkingExecutableModeChangeInEitherDirection(bool initiallyExecutable)
    {
        if (OperatingSystem.IsWindows()) return;

        using var fixture = new Repository(initiallyExecutable);
        fixture.Git("config", "core.filemode", "true");
        fixture.SetExecutable(!initiallyExecutable);

        var error = Assert.Throws<InvalidOperationException>(() => fixture.ReadUnchanged());
        Assert.Contains("has unstaged changes", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void IgnoresWorkingExecutableModeWhenGitDoes()
    {
        if (OperatingSystem.IsWindows()) return;

        using var fixture = new Repository();
        fixture.Git("config", "core.filemode", "false");
        fixture.SetExecutable(true);

        Assert.Equal(Repository.Original, fixture.ReadUnchanged());
    }

    [Fact]
    public void RejectsStagedExecutableModeEvenWhenFileModeIsDisabled()
    {
        using var fixture = new Repository();
        fixture.Git("config", "core.filemode", "false");
        fixture.Git("update-index", "--chmod=+x", "--", Repository.RelativePath);

        var error = Assert.Throws<InvalidOperationException>(() => fixture.ReadUnchanged());
        Assert.Contains("has staged changes", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RejectsStagedContentEvenWhenWorkingBytesAreReverted()
    {
        using var fixture = new Repository();
        File.WriteAllBytes(fixture.FilePath, Encoding.UTF8.GetBytes("changed!\n"));
        fixture.Git("add", "--", Repository.RelativePath);
        File.WriteAllBytes(fixture.FilePath, Repository.Original);

        var error = Assert.Throws<InvalidOperationException>(() => fixture.ReadUnchanged());
        Assert.Contains("has staged changes", error.Message, StringComparison.Ordinal);
    }

    private sealed class Repository : IDisposable
    {
        public const string RelativePath = "source.txt";
        public static readonly byte[] Original = Encoding.UTF8.GetBytes("original\n");
        private readonly string root;
        private readonly string commit;

        public Repository(bool executable = false)
        {
            root = Path.Combine(Path.GetTempPath(), "aec-provenance-tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            File.WriteAllBytes(FilePath, Original);
            Git("init", "--quiet");
            if (executable && !OperatingSystem.IsWindows()) SetExecutable(true);
            Git("add", "--", RelativePath);
            Git("-c", "user.name=AEC Test", "-c", "user.email=aec-test@example.invalid",
                "-c", "commit.gpgsign=false", "commit", "--quiet", "-m", "Fixture");
            commit = Git("rev-parse", "HEAD").Trim();
        }

        public string FilePath => Path.Combine(root, RelativePath);

        public string Git(params string[] arguments) =>
            GitProcess.RunRequired(root, "Git fixture command failed", arguments).Output;

        public void SetExecutable(bool executable)
        {
            if (OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            var mode = File.GetUnixFileMode(FilePath);
            const UnixFileMode execute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute |
                UnixFileMode.OtherExecute;
            File.SetUnixFileMode(FilePath, executable ? mode | execute : mode & ~execute);
        }

        public byte[] ReadUnchanged()
        {
            var before = Snapshot();
            try
            {
                return ApplyCommand.ReadCommittedFile(root, commit, RelativePath, "Source");
            }
            finally
            {
                var after = Snapshot();
                Assert.Equal(before.Keys.OrderBy(x => x), after.Keys.OrderBy(x => x));
                foreach (var (path, bytes) in before)
                {
                    Assert.Equal(bytes, after[path]);
                }
            }
        }

        private Dictionary<string, byte[]> Snapshot() =>
            Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .ToDictionary(path => Path.GetRelativePath(root, path), File.ReadAllBytes);

        public void Dispose() => TestDirectoryCleanup.Delete(root);
    }
}
