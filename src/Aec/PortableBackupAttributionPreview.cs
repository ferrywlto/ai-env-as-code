using System.Security.Cryptography;
using System.Text;

namespace Aec;

// This preview only attributes a runtime edit. A later backup step must
// revalidate its inputs before it can write an authored source.
internal sealed record PortableBackupAttributionPreview(
    string? Section,
    string? SourceRelativePath,
    string? ProposedContent)
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static PortableBackupAttributionPreview Create(
        string repository,
        string provider,
        string providerHome)
    {
        var snapshot = PortableRuntimeSnapshot.Read(repository, provider, providerHome);
        return Create(snapshot);
    }

    internal static PortableBackupAttributionPreview Create(PortableRuntimeSnapshot snapshot)
    {
        if (snapshot.Runtime is null)
        {
            throw new InvalidOperationException(
                "Provider runtime instructions are missing. Run ordinary provider init first.");
        }

        var lastAppliedDigest = snapshot.Provider switch
        {
            "codex" => AecInstructionBlock.ReadRepositoryBinding(snapshot.Runtime)?.TargetDigest,
            "copilot" => CopilotInstructionBlock.ReadManagedBinding(snapshot.Runtime)?.TargetDigest,
            _ => throw new ArgumentException($"Unsupported provider: {snapshot.Provider}")
        };
        if (lastAppliedDigest is null)
        {
            throw new InvalidOperationException(
                "Runtime instructions have no last-applied target digest. " +
                "Review runtime drift and apply the committed target before backup.");
        }

        // Repository-path binding alone cannot distinguish a local edit from a
        // target applied before the selected repository advanced to a new commit.
        var currentDigest = Convert.ToHexStringLower(SHA256.HashData(snapshot.Target));
        if (!string.Equals(lastAppliedDigest, currentDigest, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Runtime instructions were last applied from an older target in this repository. " +
                "Review runtime drift and apply the current committed target before backup.");
        }

        var baseline = PortableApplyPreview.RenderDesiredRuntime(snapshot);
        if (!baseline.AsSpan().EndsWith(snapshot.Target))
        {
            throw new InvalidDataException(
                "The committed target is not the final section of rendered runtime instructions.");
        }

        // The local AEC control block precedes the rendered target. Keeping its
        // exact bytes fixed prevents a control-block edit from masquerading as
        // an authored-source edit.
        var prefixLength = baseline.Length - snapshot.Target.Length;
        if (!snapshot.Runtime.AsSpan().StartsWith(baseline.AsSpan(0, prefixLength)))
        {
            throw new InvalidDataException(
                "Runtime AEC control block differs from the committed baseline.");
        }

        var baselineParts = InstructionSections.Parse(
            StrictUtf8.GetString(snapshot.Target), hasPlatform: true, hasProvider: true);
        var runtimeParts = InstructionSections.Parse(
            StrictUtf8.GetString(snapshot.Runtime.AsSpan(prefixLength)),
            hasPlatform: true, hasProvider: true);

        var changes = new List<(string Section, string Path, string Content)>();
        AddIfChanged("shared", "environment/shared/instructions.md",
            baselineParts.Shared, runtimeParts.Shared);
        AddIfChanged("platform", $"environment/platforms/{snapshot.Platform}/policy.md",
            baselineParts.Platform!, runtimeParts.Platform!);
        AddIfChanged("provider", $"environment/providers/{snapshot.Provider}/overlay.md",
            baselineParts.Provider!, runtimeParts.Provider!);

        return changes.Count switch
        {
            0 => new PortableBackupAttributionPreview(null, null, null),
            1 => new PortableBackupAttributionPreview(
                changes[0].Section, changes[0].Path, changes[0].Content),
            _ => throw new InvalidDataException(
                "Runtime changes affect multiple authored instruction sections.")
        };

        void AddIfChanged(string section, string path, string original, string current)
        {
            if (!string.Equals(original, current, StringComparison.Ordinal))
            {
                changes.Add((section, path, current));
            }
        }
    }
}
