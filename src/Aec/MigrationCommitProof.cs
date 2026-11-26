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
        VerifyStagedPaths(repository, parent, expectedFiles.Keys.ToArray());
        var staged = new List<(string RelativePath, string Blob)>();
        foreach (var (relative, expectedContent) in expectedFiles)
        {
            var blob = GitRepository.ReadStagedObjectIdWithoutReplacements(repository, relative,
                "Could not resolve staged migration file");
            var entry = GitRepository.ReadIndexEntry(repository, relative, "Could not inspect scaffold index entry");
            if (entry != $"{modes[relative]} {blob} 0\t{relative}\0")
                throw new InvalidOperationException($"Staged migration file mode differs from review: {relative}; expected {modes[relative]}.");
            var size = GitRepository.ReadObjectSizeText(repository, blob,
                "Could not inspect staged migration file size");
            if (!int.TryParse(size, NumberStyles.None, CultureInfo.InvariantCulture, out var length))
                throw new InvalidDataException($"Invalid staged migration file size: {relative}.");
            if (length != expectedContent.Length)
                throw new InvalidOperationException($"Staged migration file byte length differs from review: {relative}.");
            var content = GitRepository.ReadBlob(repository, blob,
                "Could not read staged migration file");
            if (!content.AsSpan().SequenceEqual(expectedContent))
                throw new InvalidOperationException($"Staged migration file bytes differ from review: {relative}.");
            staged.Add((relative, blob));
        }
        return staged;
    }

    internal static void VerifyStagedPaths(string repository, string parent,
        string[] expected, bool portableCapture = false)
    {
        var stagedPaths = GitRepository.ReadStagedPathsComparedToCommit(
            repository, parent, PathInspectionError(portableCapture));
        VerifyExactPaths(expected, stagedPaths, portableCapture);
    }

    internal static void VerifyCommit(string repository, string parent, string commit,
        IReadOnlyList<(string RelativePath, string Blob)> files, Dictionary<string, string> modes,
        bool portableCapture = false)
    {
        var parents = GitRepository.ReadCommitParentLine(repository, commit,
            portableCapture ? "Could not inspect portable capture parent" : "Could not inspect migration commit parent");
        if (parents != $"{commit} {parent}")
            throw new InvalidOperationException(portableCapture
                ? "Portable capture did not create exactly one child of the reviewed commit."
                : "Migration commit is not one child of the reviewed HEAD.");
        var paths = files.Select(file => file.RelativePath).ToArray();
        var committedPaths = GitRepository.ReadChangedPathsBetweenCommits(
            repository, parent, commit, PathInspectionError(portableCapture));
        VerifyExactPaths(paths, committedPaths, portableCapture);
        foreach (var (relative, blob) in files)
        {
            var entry = GitRepository.ReadTreeEntry(repository, commit, relative,
                portableCapture ? "Could not inspect committed portable capture" : "Could not inspect committed migration file");
            if (entry != $"{modes[relative]} blob {blob}\t{relative}\0")
                throw new InvalidOperationException(portableCapture
                    ? $"Committed portable capture bytes or mode differ from review: {relative}."
                    : $"Committed migration file bytes or mode differ from review: {relative}.");
        }
    }

    private static string PathInspectionError(bool portableCapture) => portableCapture
        ? "Could not inspect exact portable capture paths"
        : "Could not inspect exact migration paths";

    private static void VerifyExactPaths(string[] expected, string[] actual, bool portableCapture)
    {
        if (!actual.Order(StringComparer.Ordinal)
                .SequenceEqual(expected.Order(StringComparer.Ordinal), StringComparer.Ordinal))
            throw new InvalidOperationException(portableCapture
                ? "Portable capture changes are not the exact reviewed path set."
                : "Migration changes are not the exact reviewed path set.");
    }
}
