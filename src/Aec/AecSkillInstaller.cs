using System.Reflection;

namespace Aec;

internal static class AecSkillInstaller
{
    private const string SkillFileName = "SKILL.md";
    private const string AgentsDirectoryName = "agents";
    private const string OpenAiFileName = "openai.yaml";

    private static readonly SkillResource[] Resources =
    [
        new(SkillFileName, "Aec.Skill.SKILL.md"),
        new(Path.Combine(AgentsDirectoryName, OpenAiFileName), "Aec.Skill.openai.yaml")
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
        byte[] Content)
    {
        public SkillResource(string relativePath, string resourceName)
            : this(relativePath, resourceName, [])
        {
        }
    }
}
