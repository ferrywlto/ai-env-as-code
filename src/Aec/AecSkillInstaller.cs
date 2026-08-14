using System.Reflection;
using System.Security.Cryptography;

namespace Aec;

internal static class AecSkillInstaller
{
    private const string SkillFileName = "SKILL.md";
    private const string AgentsDirectoryName = "agents";
    private const string OpenAiFileName = "openai.yaml";

    private static readonly SkillResource[] Resources =
    [
        // Only byte-exact released predecessors are eligible for replacement.
        // The retired pre-release v0.8 skill is deliberately outside this boundary.
        new(
            SkillFileName,
            "Aec.Skill.SKILL.md",
            [
                "dc5b81445caa9ea6d039504b67676d05ef2e19d2f98394eda826522056d4a6a8",
                "728a706eadd9a802a17960a940430466d71b841d612b1b1953c99caf6df2d0ec",
                "9cddc5727f0e491a1735e7c2d40e4cee865dc3675dfe24c4fb5842c2119b61c0",
                "8cf1c0d8effbdf19cd44520bd96300b5201ba2a71cef69101f5490077159a3a7"
            ]),
        new(
            Path.Combine(AgentsDirectoryName, OpenAiFileName),
            "Aec.Skill.openai.yaml",
            ["5e9636f4f9863bacde37a36a94b71c3450af1017d10e7a7115698a5a62b3ea94"])
    ];

    public static void Install(string codexHome)
    {
        var resources = LoadResources();
        var skillsDirectory = Path.Combine(codexHome, "skills");
        var skillDirectory = Path.Combine(skillsDirectory, "aec");
        var agentsDirectory = Path.Combine(skillDirectory, AgentsDirectoryName);

        PreflightDirectory(skillsDirectory, "Codex skills directory");
        PreflightDirectory(skillDirectory, "AEC skill directory");
        PreflightDirectory(agentsDirectory, "AEC skill agents directory");

        var missingResources = new List<SkillResource>();
        foreach (var resource in resources)
        {
            var path = Path.Combine(skillDirectory, resource.RelativePath);
            var actual = AecApplication.ReadOptionalTextFile(path, "AEC skill file");
            if (actual is null)
            {
                missingResources.Add(resource);
                continue;
            }

            EnsureMatches(path, actual, resource.Content);
        }

        CreateAndVerifyDirectory(skillsDirectory, "Codex skills directory");
        CreateAndVerifyDirectory(skillDirectory, "AEC skill directory");
        CreateAndVerifyDirectory(agentsDirectory, "AEC skill agents directory");

        // Complete only missing managed files after every existing managed path has passed preflight.
        foreach (var resource in missingResources)
        {
            CreateFile(Path.Combine(skillDirectory, resource.RelativePath), resource.Content);
        }

        VerifyInstallation(skillDirectory, resources);
    }

    public static bool Upgrade(string codexHome)
    {
        var resources = LoadResources();
        var skillsDirectory = Path.Combine(codexHome, "skills");
        var skillDirectory = Path.Combine(skillsDirectory, "aec");
        var agentsDirectory = Path.Combine(skillDirectory, AgentsDirectoryName);

        RequireExistingDirectory(codexHome, "Codex home");
        RequireExistingDirectory(skillsDirectory, "Codex skills directory");
        RequireExistingDirectory(skillDirectory, "AEC skill directory");
        RequireExistingDirectory(agentsDirectory, "AEC skill agents directory");

        var updates = new List<SkillUpdate>();
        foreach (var resource in resources)
        {
            var path = Path.Combine(skillDirectory, resource.RelativePath);
            var actual = AecApplication.ReadRequiredTextFile(path, "AEC skill file");
            if (actual.AsSpan().SequenceEqual(resource.Content))
            {
                continue;
            }

            if (!IsSupportedPredecessor(actual, resource.SupportedPredecessorHashes))
            {
                throw new InvalidOperationException(
                    $"Existing AEC skill is not an exact supported official bundle: {skillDirectory}");
            }

            updates.Add(new SkillUpdate(path, actual, resource.Content));
        }

        // Recheck the complete directory chain after preflight. Each subsequent
        // replacement also compares the file bytes so a concurrent edit fails closed.
        RequireExistingDirectory(codexHome, "Codex home");
        RequireExistingDirectory(skillsDirectory, "Codex skills directory");
        RequireExistingDirectory(skillDirectory, "AEC skill directory");
        RequireExistingDirectory(agentsDirectory, "AEC skill agents directory");

        foreach (var update in updates)
        {
            AtomicFile.ReplaceIfUnchanged(
                update.Path,
                update.Current,
                update.Desired,
                "AEC skill file");
        }

        VerifyInstallation(skillDirectory, resources);
        return updates.Count > 0;
    }

    private static SkillResource[] LoadResources()
    {
        var assembly = typeof(AecSkillInstaller).Assembly;
        return Resources
            .Select(resource => resource with
            {
                Content = ReadResource(assembly, resource.ResourceName)
            })
            .ToArray();
    }

    private static byte[] ReadResource(Assembly assembly, string name)
    {
        using var stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Embedded AEC skill resource is missing: {name}");
        using var content = new MemoryStream();
        stream.CopyTo(content);
        return content.ToArray();
    }

    private static void VerifyInstallation(
        string skillDirectory,
        IReadOnlyCollection<SkillResource> resources)
    {
        AecApplication.EnsureRealDirectory(skillDirectory, "AEC skill directory");

        var agentsDirectory = Path.Combine(skillDirectory, AgentsDirectoryName);
        AecApplication.EnsureRealDirectory(agentsDirectory, "AEC skill agents directory");

        foreach (var resource in resources)
        {
            var path = Path.Combine(skillDirectory, resource.RelativePath);
            var actual = AecApplication.ReadRequiredTextFile(path, "AEC skill file");
            EnsureMatches(path, actual, resource.Content);
        }
    }

    private static void PreflightDirectory(string path, string label)
    {
        AecApplication.EnsureNoLinksInExistingPath(path, $"{label} path");
        if (Directory.Exists(path))
        {
            AecApplication.EnsureRealDirectory(path, label);
        }
    }

    private static void RequireExistingDirectory(string path, string label)
    {
        AecApplication.EnsureNoLinksInExistingPath(path, $"{label} path");
        AecApplication.EnsureRealDirectory(path, label);
    }

    private static bool IsSupportedPredecessor(
        byte[] actual,
        IReadOnlyCollection<string> supportedHashes)
    {
        var actualHash = Convert.ToHexStringLower(SHA256.HashData(actual));
        return supportedHashes.Contains(actualHash, StringComparer.Ordinal);
    }

    private static void CreateAndVerifyDirectory(string path, string label)
    {
        Directory.CreateDirectory(path);
        AecApplication.EnsureNoLinksInExistingPath(path, $"{label} path");
        AecApplication.EnsureRealDirectory(path, label);
    }

    private static void CreateFile(string path, byte[] content)
    {
        var directory = Path.GetDirectoryName(path)
            ?? throw new InvalidOperationException($"AEC skill file has no parent directory: {path}");
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(path)}.aec-{Guid.NewGuid():N}");

        try
        {
            // A flushed sibling plus a non-overwriting move prevents partial or silent replacement.
            AtomicFile.WriteNew(temporaryPath, content);
            File.Move(temporaryPath, path);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static void EnsureMatches(string path, byte[] actual, byte[] expected)
    {
        if (!actual.AsSpan().SequenceEqual(expected))
        {
            throw new InvalidOperationException(
                $"Existing AEC skill conflicts with the bundled version: {path}");
        }
    }

    private sealed record SkillResource(
        string RelativePath,
        string ResourceName,
        string[] SupportedPredecessorHashes,
        byte[] Content)
    {
        public SkillResource(
            string relativePath,
            string resourceName,
            string[] supportedPredecessorHashes)
            : this(relativePath, resourceName, supportedPredecessorHashes, [])
        {
        }
    }

    private sealed record SkillUpdate(string Path, byte[] Current, byte[] Desired);
}
