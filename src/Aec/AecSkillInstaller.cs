using System.Reflection;
using System.Security.Cryptography;

namespace Aec;

internal static class AecSkillInstaller
{
    private const string SkillFileName = "SKILL.md";
    private const string AgentsDirectoryName = "agents";
    private const string OpenAiFileName = "openai.yaml";

    private static readonly SkillResource[] CopilotResources =
    [
        new(
            SkillFileName,
            "Aec.CopilotSkill.SKILL.md",
            [
                "7bfd08be9c22c7efd1dfec14d75ae9060ce7bd69ec288d517477681b9cd394e9",
                "adfaa1f7037cf68484693215e59d988e4c1e5f90d72ed9fc55a14ff4df83d314",
                "2be86f3a85f8daae019da03d7d8977b82a2dd4b641d0b67b95c27aa4351b23f2",
                "bf6b4f2f8c7d5fe6c249dc95d0bf77d8f038db8615d48c8ba2e1a97b7146d179",
                "34130bb3dce9f023dc6717fb88b55e58b82190021b2f3812cedfe1c55de01f43",
                "89cdd1cf025acdd02788a69222c78033f5da11110192d4320516ea362d7ef007",
                "ebac80a1919835527b8588c20c390427af9e0610aafbabffdbe389b398965129",
                "a9eee1bc30bc28bb3aeb8c54955cd974960f451688ae9524fc9af0454339fbdb",
                "a4b377fb690a5305422b4c3ee82ccdae81d75d3812bd6d09c2112ae22693141a",
                "b6bf7e8e64e0b2f7fc8940f2a75445512828b2ff663c5ffe808a2bb31df6c28a"
            ])
    ];

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
                "8cf1c0d8effbdf19cd44520bd96300b5201ba2a71cef69101f5490077159a3a7",
                "1bf54d30a4237801df36dd4949d8a21e843dc6c1f98cfb092694c0999b51eacf",
                "60754cc941dbfaf17042c4eb4093c9706ea0054c526001f096da2fc4a795aec9",
                "af5dd269b6807429d4c6079bcc79340c2c10f2bac8e91f52383fd8d7b766581f",
                "2924f13eb5d90cd088642533461de6f37fa99c73ce94ba95b395ae5f1cd7b535",
                "6181d99ddde36b53ed3007066ec716369f46a84c2067e58337f152005865f352",
                "38c275ecae9f400ff6ce149620889b72d09c04087184ab8d5971f72f62e5a26d",
                "d12924f66bebbf1cb8b9a0acfd620a5ecedd28e4cae3913fe2c8eecc8feae1f7",
                "0b4886e4d1385c1662cc08ef71703dfcb879551b813ff716a9054758978cd6f3",
                "3a7834dc34376c1c9ab26569d79702b8e8f7f19eb4da3bcfa2941b2681fbbd90",
                "7fa537668cf5a1a61e05ea76374370df3f73889d9f198e1094286fbb496eb113",
                "a71ba03a5f0d04aef107ddb3074ae0d7ecfe3e16ac8dad9121571d81ecafb7fe",
                "2bc8e3e77b2389d08c8a995a56b9e5ca3ae47b7c47b1079317b2f1279ffb4087",
                "2a8adf156a11164301fee33193188897dbc770a7a2c3cecb11cf15a74cb65c27",
                "f7154226b4ebe5b21d07b88be4dac6e4c5028eefb746b19ba2bdde556933a5d2",
                "064a3e06366a9d64a739bc7f3aacffdb9f7185668466a7d8665084b2911f6146"
            ]),
        new(
            Path.Combine(AgentsDirectoryName, OpenAiFileName),
            "Aec.Skill.openai.yaml",
            ["5e9636f4f9863bacde37a36a94b71c3450af1017d10e7a7115698a5a62b3ea94"])
    ];

    public static bool Install(string codexHome)
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
        return missingResources.Count > 0;
    }

    // Copilot reads only SKILL.md. Keeping this small installer separate from the
    // Codex bundle avoids creating an OpenAI-specific agents directory in Copilot.
    public static bool InstallCopilot(string copilotHome, bool upgradeExisting = true)
    {
        var resources = LoadResources(CopilotResources);
        var skillsDirectory = Path.Combine(copilotHome, "skills");
        var skillDirectory = Path.Combine(skillsDirectory, "aec");

        PreflightDirectory(skillsDirectory, "Copilot skills directory");
        PreflightDirectory(skillDirectory, "Copilot AEC skill directory");

        var missingResources = new List<SkillResource>();
        var updates = new List<SkillUpdate>();
        foreach (var resource in resources)
        {
            var path = Path.Combine(skillDirectory, resource.RelativePath);
            var actual = AecApplication.ReadOptionalTextFile(path, "Copilot AEC skill file");
            if (actual is null)
            {
                missingResources.Add(resource);
                continue;
            }

            if (actual.AsSpan().SequenceEqual(resource.Content))
            {
                continue;
            }

            if (!IsSupportedPredecessor(actual, resource.SupportedPredecessorHashes))
            {
                throw new InvalidOperationException(
                    $"Existing AEC skill conflicts with the bundled version: {path}");
            }

            if (!upgradeExisting)
            {
                throw new InvalidOperationException(
                    $"An older AEC skill is installed: {path}. Use `aec upgrade` to refresh it.");
            }

            updates.Add(new SkillUpdate(path, actual, resource.Content));
        }

        CreateAndVerifyDirectory(skillsDirectory, "Copilot skills directory");
        CreateAndVerifyDirectory(skillDirectory, "Copilot AEC skill directory");
        foreach (var resource in missingResources)
        {
            CreateFile(Path.Combine(skillDirectory, resource.RelativePath), resource.Content);
        }

        // Ordinary Copilot init retains its exact-predecessor refresh behaviour.
        foreach (var update in updates)
        {
            AtomicFile.ReplaceIfUnchanged(
                update.Path,
                update.Current,
                update.Desired,
                "Copilot AEC skill file");
        }

        VerifyCopilotInstallation(skillDirectory, resources);
        return missingResources.Count > 0 || updates.Count > 0;
    }

    public static bool Upgrade(string codexHome, string copilotHome)
    {
        // Validate both bundles before replacing either. Missing enrollment is a no-op.
        var codex = PrepareUpgrade(codexHome, codex: true);
        var copilot = PrepareUpgrade(copilotHome, codex: false);

        var codexChanged = ApplyUpgrade(codex);
        var copilotChanged = ApplyUpgrade(copilot);
        return codexChanged || copilotChanged;
    }

    private static SkillUpgradePlan? PrepareUpgrade(string home, bool codex)
    {
        var skillDirectory = Path.Combine(home, "skills", "aec");
        var agentsDirectory = codex ? Path.Combine(skillDirectory, AgentsDirectoryName) : null;
        PreflightSkillDirectories(skillDirectory, agentsDirectory);

        var resources = LoadResources(codex ? Resources : CopilotResources);
        var missing = new List<string>();
        var updates = new List<SkillUpdate>();
        foreach (var resource in resources)
        {
            var path = Path.Combine(skillDirectory, resource.RelativePath);
            var actual = AecApplication.ReadOptionalTextFile(path, "AEC skill file");
            if (actual is null)
            {
                missing.Add(path);
                continue;
            }

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

        if (missing.Count == resources.Length)
        {
            return null;
        }

        if (missing.Count > 0)
        {
            throw new FileNotFoundException($"AEC skill file does not exist: {missing[0]}");
        }

        return new SkillUpgradePlan(skillDirectory, agentsDirectory, resources, updates);
    }

    private static bool ApplyUpgrade(SkillUpgradePlan? plan)
    {
        if (plan is null)
        {
            return false;
        }

        PreflightSkillDirectories(plan.SkillDirectory, plan.AgentsDirectory);
        foreach (var update in plan.Updates)
        {
            AtomicFile.ReplaceIfUnchanged(
                update.Path, update.Current, update.Desired, "AEC skill file");
        }

        if (plan.AgentsDirectory is null)
        {
            VerifyCopilotInstallation(plan.SkillDirectory, plan.Resources);
        }
        else
        {
            VerifyInstallation(plan.SkillDirectory, plan.Resources);
        }

        return plan.Updates.Count > 0;
    }

    public static UninstallPlan PrepareUninstall(string home, bool codex)
    {
        var resources = LoadResources(codex ? Resources : CopilotResources);
        var skillDirectory = Path.Combine(home, "skills", "aec");
        var agentsDirectory = codex ? Path.Combine(skillDirectory, AgentsDirectoryName) : null;
        PreflightSkillDirectories(skillDirectory, agentsDirectory);

        var deletions = new List<SkillDeletion>();
        foreach (var resource in resources)
        {
            var path = Path.Combine(skillDirectory, resource.RelativePath);
            var actual = AecApplication.ReadOptionalTextFile(path, "AEC skill file");
            if (actual is null)
            {
                continue;
            }

            if (!actual.AsSpan().SequenceEqual(resource.Content) &&
                !IsSupportedPredecessor(actual, resource.SupportedPredecessorHashes))
            {
                throw new InvalidOperationException(
                    $"Existing AEC skill is not an exact supported official bundle: {skillDirectory}");
            }

            deletions.Add(new SkillDeletion(path, actual));
        }

        return new UninstallPlan(
            skillDirectory,
            agentsDirectory,
            deletions);
    }

    public static void ApplyUninstall(UninstallPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        PreflightSkillDirectories(plan.SkillDirectory, plan.AgentsDirectory);

        foreach (var deletion in plan.Deletions)
        {
            AtomicFile.DeleteIfUnchanged(
                deletion.Path,
                deletion.Content,
                "AEC skill file");
        }

        if (plan.AgentsDirectory is not null)
        {
            RemoveIfEmpty(plan.AgentsDirectory);
        }

        RemoveIfEmpty(plan.SkillDirectory);
    }

    private static SkillResource[] LoadResources()
    {
        return LoadResources(Resources);
    }

    private static SkillResource[] LoadResources(IEnumerable<SkillResource> resources)
    {
        var assembly = typeof(AecSkillInstaller).Assembly;
        return resources
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

    private static void VerifyCopilotInstallation(
        string skillDirectory,
        IReadOnlyCollection<SkillResource> resources)
    {
        AecApplication.EnsureRealDirectory(skillDirectory, "Copilot AEC skill directory");
        foreach (var resource in resources)
        {
            var path = Path.Combine(skillDirectory, resource.RelativePath);
            var actual = AecApplication.ReadRequiredTextFile(path, "Copilot AEC skill file");
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

    private static void PreflightSkillDirectories(string skillDirectory, string? agentsDirectory)
    {
        // Each preflight also checks every existing ancestor for symlinks/reparse points.
        PreflightDirectory(skillDirectory, "AEC skill directory");
        if (agentsDirectory is not null)
        {
            PreflightDirectory(agentsDirectory, "AEC skill agents directory");
        }
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

    private static void RemoveIfEmpty(string path)
    {
        if (Directory.Exists(path) &&
            !Directory.EnumerateFileSystemEntries(path).Any())
        {
            Directory.Delete(path);
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

    private sealed record SkillUpgradePlan(
        string SkillDirectory,
        string? AgentsDirectory,
        SkillResource[] Resources,
        IReadOnlyList<SkillUpdate> Updates);

    internal sealed record UninstallPlan(
        string SkillDirectory,
        string? AgentsDirectory,
        IReadOnlyList<SkillDeletion> Deletions)
    {
        public bool HasFiles => Deletions.Count > 0;
    }

    internal sealed record SkillDeletion(string Path, byte[] Content);
}
