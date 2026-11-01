using System.Globalization;

namespace Aec;

// Shared immutable-object proof only. Coordinators own branch selection,
// snapshots, staging, signing and publication; this helper changes no files.
internal static class MigrationCommitProof
{
    internal static List<(string RelativePath, string Blob)> VerifyStagedFiles(string repository,
        string parent, Dictionary<string, byte[]> expectedFiles,
        Dictionary<string, string> modes)
    {
        VerifyPaths(repository, expectedFiles.Keys.ToArray(),
            "diff", "--cached", "--no-renames", "--name-only", "-z", parent, "--");
        var staged = new List<(string RelativePath, string Blob)>();
        foreach (var (relative, expectedContent) in expectedFiles)
        {
            var blob = GitProcess.RunRequired(repository, "Could not resolve staged migration file",
                "--no-replace-objects", "rev-parse", $":{relative}").Output.Trim();
            var entry = MigrationScaffoldPreview.ReadIndexEntry(repository, relative);
            if (entry != $"{modes[relative]} {blob} 0\t{relative}\0")
                throw new InvalidOperationException($"Staged migration file mode differs from review: {relative}; expected {modes[relative]}.");
            var size = GitProcess.RunRequired(repository, "Could not inspect staged migration file size",
                "--no-replace-objects", "cat-file", "-s", blob).Output.Trim();
            if (!int.TryParse(size, NumberStyles.None, CultureInfo.InvariantCulture, out var length))
                throw new InvalidDataException($"Invalid staged migration file size: {relative}.");
            if (length != expectedContent.Length)
                throw new InvalidOperationException($"Staged migration file byte length differs from review: {relative}.");
            var content = GitProcess.RunRequiredBytes(repository, "Could not read staged migration file",
                "--no-replace-objects", "cat-file", "blob", blob);
            if (!content.AsSpan().SequenceEqual(expectedContent))
                throw new InvalidOperationException($"Staged migration file bytes differ from review: {relative}.");
            staged.Add((relative, blob));
        }
        return staged;
    }

    internal static void VerifyCommit(string repository, string parent, string commit,
        IReadOnlyList<(string RelativePath, string Blob)> files, Dictionary<string, string> modes)
    {
        var parents = GitProcess.RunRequired(repository, "Could not inspect migration commit parent",
            "--no-replace-objects", "rev-list", "--parents", "-n", "1", commit).Output.Trim();
        if (parents != $"{commit} {parent}")
            throw new InvalidOperationException("Migration commit is not one child of the reviewed HEAD.");
        var paths = files.Select(file => file.RelativePath).ToArray();
        VerifyPaths(repository, paths, "diff-tree", "--no-commit-id", "--no-renames", "--name-only", "-r", "-z", parent, commit, "--");
        foreach (var (relative, blob) in files)
        {
            var entry = GitProcess.RunRequired(repository, "Could not inspect committed migration file",
                "--no-replace-objects", "ls-tree", "-z", commit, "--", relative).Output;
            if (entry != $"{modes[relative]} blob {blob}\t{relative}\0")
                throw new InvalidOperationException($"Committed migration file bytes or mode differ from review: {relative}.");
        }
    }

    private static void VerifyPaths(string repository, string[] expected, params string[] arguments)
    {
        var output = GitProcess.RunRequired(repository, "Could not inspect exact migration paths",
            ["--no-replace-objects", .. arguments]).Output;
        var paths = output.Split('\0', StringSplitOptions.RemoveEmptyEntries).Order(StringComparer.Ordinal);
        if (!paths.SequenceEqual(expected.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidOperationException("Migration changes are not the exact reviewed path set.");
    }
}
