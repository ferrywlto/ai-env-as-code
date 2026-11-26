using System.Text;

namespace Aec;

internal static class ManagedBlockRemoval
{
    internal static byte[] Remove(byte[] content, int start, int end, string newline)
    {
        var bodyOffset = content.AsSpan().StartsWith("\uFEFF"u8) ? 3 : 0;
        var lineBytes = Encoding.ASCII.GetBytes(newline);
        var suffix = end;
        // A leading block owns two separator lines; an in-place block owns one.
        if (start == bodyOffset &&
            content.AsSpan(suffix).StartsWith(lineBytes) &&
            content.AsSpan(suffix + lineBytes.Length).StartsWith(lineBytes))
        {
            suffix += lineBytes.Length * 2;
        }
        else if (content.AsSpan(suffix).StartsWith(lineBytes))
        {
            suffix += lineBytes.Length;
        }

        var result = new byte[start + content.Length - suffix];
        content.AsSpan(0, start).CopyTo(result);
        content.AsSpan(suffix).CopyTo(result.AsSpan(start));
        return result;
    }
}
