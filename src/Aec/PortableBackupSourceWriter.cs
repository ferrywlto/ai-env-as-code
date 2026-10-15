using System.Text;

namespace Aec;

// This internal step writes only one attributed source. Targets and the backup
// commit remain separate steps; it is not connected to the public backup CLI.
internal static class PortableBackupSourceWriter
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static string Write(
        PortableRuntimeSnapshot reviewed,
        PortableBackupAttributionPreview attribution)
    {
        ArgumentNullException.ThrowIfNull(reviewed);
        ArgumentNullException.ThrowIfNull(attribution);
        if (reviewed.Runtime is null || attribution.Section is null ||
            attribution.SourceRelativePath is null || attribution.ProposedContent is null)
        {
            throw new InvalidOperationException(
                "Backup requires one reviewed runtime instruction section change.");
        }

        var providerHome = Path.GetDirectoryName(reviewed.RuntimePath)
            ?? throw new InvalidOperationException("Provider runtime target has no parent directory.");
        Recheck();

        var sourcePath = Path.Combine(reviewed.Repository, attribution.SourceRelativePath);
        AecApplication.EnsureNoLinksInExistingPath(
            Path.GetDirectoryName(sourcePath)!, "Authored source path");
        var original = ApplyCommand.ReadCommittedFile(
            reviewed.Repository, reviewed.Commit, attribution.SourceRelativePath,
            "Authored source");
        var proposed = StrictUtf8.GetBytes(attribution.ProposedContent);
        if (proposed.Length > AecApplication.MaximumTextBytes)
        {
            throw new InvalidDataException("Authored source exceeds 1 MiB.");
        }

        // Git provenance and runtime may have changed while the source was read.
        // The atomic helper also compares the source bytes just before replacement.
        Recheck();
        try
        {
            AtomicFile.ReplaceIfUnchanged(sourcePath, original, proposed, "Authored source");
        }
        catch (IOException error)
        {
            throw new IOException(
                "Authored source replacement failed; inspect the source before retrying " +
                "because it may have changed.", error);
        }

        return attribution.SourceRelativePath;

        void Recheck()
        {
            var current = PortableRuntimeSnapshot.Read(
                reviewed.Repository, reviewed.Provider, providerHome);
            if (current.Runtime is null ||
                !string.Equals(current.RuntimePath, reviewed.RuntimePath, StringComparison.Ordinal) ||
                !string.Equals(current.Platform, reviewed.Platform, StringComparison.Ordinal) ||
                !string.Equals(current.Commit, reviewed.Commit, StringComparison.Ordinal) ||
                current.BlockVersion != reviewed.BlockVersion ||
                !current.Target.AsSpan().SequenceEqual(reviewed.Target) ||
                !current.Runtime.AsSpan().SequenceEqual(reviewed.Runtime))
            {
                throw new InvalidOperationException(
                    "Portable backup inputs changed after review; no authored source was replaced.");
            }

            var actual = PortableBackupAttributionPreview.Create(
                reviewed.Repository, reviewed.Provider, providerHome);
            if (!string.Equals(actual.Section, attribution.Section, StringComparison.Ordinal) ||
                !string.Equals(actual.SourceRelativePath, attribution.SourceRelativePath,
                    StringComparison.Ordinal) ||
                !string.Equals(actual.ProposedContent, attribution.ProposedContent,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    "Portable backup attribution changed after review; no authored source was replaced.");
            }
        }
    }
}
