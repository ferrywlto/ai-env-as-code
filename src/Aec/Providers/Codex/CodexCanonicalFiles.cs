namespace Aec;

internal static class CodexCanonicalFiles
{
    internal static byte[] ReadCommittedSource(string repository, string commit)
    {
        return CommittedFileReader.ReadCommittedFile(
            repository,
            commit,
            AecApplication.SourceRelativePath,
            "Canonical source");
    }

    internal static byte[] ReadCommittedConfig(string repository, string commit)
    {
        return CommittedFileReader.ReadCommittedFile(
            repository,
            commit,
            AecApplication.ConfigSourceRelativePath,
            "Canonical config");
    }

}
