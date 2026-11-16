using System.Text;

namespace Aec;

internal static class MigrationLfsPointer
{
    private static readonly byte[] Header = Encoding.ASCII.GetBytes("version https://git-lfs.github.com/spec/v1");

    internal static void Reject(byte[] content, string label)
    {
        if (content.AsSpan().StartsWith(Header) &&
            (content.Length == Header.Length || content[Header.Length] is (byte)'\n' or (byte)'\r'))
        {
            throw new InvalidDataException($"{label} is a Git LFS pointer; restore the actual file content before migration.");
        }
    }
}
