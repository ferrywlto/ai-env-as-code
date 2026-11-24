using System.Security.Cryptography;

namespace Aec;

internal static class InstructionTargetDigest
{
    internal static string Compute(ReadOnlySpan<byte> target) =>
        Convert.ToHexStringLower(SHA256.HashData(target));

    internal static bool IsValid(string digest) =>
        digest.Length == 64 && digest.All(character =>
            character is >= '0' and <= '9' or >= 'a' and <= 'f');
}
