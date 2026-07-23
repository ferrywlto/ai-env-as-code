namespace Aec;

internal static class ChatGptInitCommand
{
    private static readonly string[] ScaffoldFileNames =
    [
        "custom-instructions.md",
        "project-baseline.md",
        "gpt-baseline.md"
    ];

    public static int Run(string repository, TextWriter output)
    {
        ValidateRepository(repository);

        var canonicalPath = Path.Combine(repository, AecApplication.SourceRelativePath);
        var canonical = AecApplication.ReadRequiredTextFile(canonicalPath, "Canonical source");
        var merged = AecInstructionBlock.MergeForChatGptProvider(canonical);
        var providerDirectory = Path.Combine(repository, "environment", "providers", "chatgpt");
        var scaffoldPaths = ScaffoldFileNames
            .Select(fileName => Path.Combine(providerDirectory, fileName))
            .ToArray();

        // Preflight every destination before creating anything to avoid predictable partial scaffolds.
        PreflightProviderDirectory(providerDirectory);
        foreach (var path in scaffoldPaths)
        {
            PreflightScaffoldFile(path);
        }

        var missingPaths = scaffoldPaths.Where(path => !File.Exists(path)).ToArray();
        var sourceChanged = !canonical.AsSpan().SequenceEqual(merged);
        if (missingPaths.Length == 0 && !sourceChanged)
        {
            output.WriteLine("unchanged");
            return 0;
        }

        Directory.CreateDirectory(providerDirectory);

        // Create-only writes preserve manual backups; a rerun completes safely after partial I/O.
        foreach (var path in missingPaths)
        {
            AtomicFile.WriteNew(path, []);
        }

        if (sourceChanged)
        {
            AtomicFile.ReplaceIfUnchanged(canonicalPath, canonical, merged, "Canonical source");
        }

        VerifyResult(canonicalPath, merged, missingPaths);
        output.WriteLine("initialized");
        return 0;
    }

    private static void ValidateRepository(string repository)
    {
        AecApplication.EnsureNoLinksInExistingPath(repository, "Repository path");
        AecApplication.EnsureRealDirectory(repository, "Repository");
        AecApplication.EnsureSourceDirectories(repository);

        var insideWorkTree = RunRequired(
            repository,
            "Repository is not a Git work tree",
            "rev-parse",
            "--is-inside-work-tree").Output.Trim();
        if (insideWorkTree != "true")
        {
            throw new InvalidOperationException($"Repository is not a Git work tree: {repository}");
        }

        var bare = RunRequired(
            repository,
            "Git could not inspect the repository",
            "rev-parse",
            "--is-bare-repository").Output.Trim();
        if (bare != "false")
        {
            throw new InvalidOperationException($"Repository must not be bare: {repository}");
        }

        var prefix = RunRequired(
            repository,
            "Git could not locate the repository root",
            "rev-parse",
            "--show-prefix").Output;
        if (prefix.Trim().Length != 0)
        {
            throw new InvalidOperationException($"init --provider=chatgpt requires the Git repository root: {repository}");
        }
    }

    private static void PreflightProviderDirectory(string path)
    {
        AecApplication.EnsureNoLinksInExistingPath(path, "ChatGPT provider path");
        var directory = new DirectoryInfo(path);
        directory.Refresh();

        if (directory.LinkTarget is not null)
        {
            throw new InvalidOperationException($"ChatGPT provider directory must not be a symbolic link: {path}");
        }

        if (directory.Exists)
        {
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException($"ChatGPT provider directory must be real: {path}");
            }

            return;
        }

        if (File.Exists(path))
        {
            throw new InvalidOperationException($"ChatGPT provider path must be a directory: {path}");
        }
    }

    private static void PreflightScaffoldFile(string path)
    {
        var file = new FileInfo(path);
        file.Refresh();

        if (file.LinkTarget is not null)
        {
            throw new InvalidOperationException($"ChatGPT instruction file must not be a symbolic link: {path}");
        }

        if (file.Exists)
        {
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException($"ChatGPT instruction file must be real: {path}");
            }

            return;
        }

        if (Directory.Exists(path))
        {
            throw new InvalidOperationException($"ChatGPT instruction path must be a regular file: {path}");
        }
    }

    private static void VerifyResult(string canonicalPath, byte[] expected, string[] createdPaths)
    {
        var actual = AecApplication.ReadRequiredTextFile(canonicalPath, "Canonical source");
        if (!actual.AsSpan().SequenceEqual(expected))
        {
            throw new IOException("Canonical source verification failed after ChatGPT initialization.");
        }

        foreach (var path in createdPaths)
        {
            var content = AecApplication.ReadRequiredTextFile(path, "ChatGPT instruction file");
            if (content.Length != 0)
            {
                throw new IOException($"ChatGPT instruction file was not created empty: {path}");
            }
        }
    }

    private static GitResult RunRequired(
        string repository,
        string failureMessage,
        params string[] arguments)
    {
        return GitProcess.RunRequired(
            repository,
            failureMessage,
            ["--no-replace-objects", .. arguments]);
    }
}
