using System.Text;

namespace Aec;

internal static class UninstallCommand
{
    private static readonly byte[] AecSkillReference = Encoding.UTF8.GetBytes("$aec");

    public static int Run(string codexHome, TextWriter output)
    {
        AecApplication.EnsureNoLinksInExistingPath(codexHome, "Codex home path");
        AecApplication.EnsureRealDirectory(codexHome, "Codex home");

        var agentsPath = Path.Combine(codexHome, "AGENTS.md");
        var currentInstructions = AecApplication.ReadOptionalTextFile(
            agentsPath,
            "Runtime instructions");
        var removal = currentInstructions is null
            ? new AecInstructionBlock.RemovalResult([], Removed: false)
            : AecInstructionBlock.Remove(currentInstructions);

        if (removal.Content.AsSpan().IndexOf(AecSkillReference) >= 0)
        {
            throw new InvalidOperationException(
                "Runtime instructions contain an unmanaged `$aec` reference outside " +
                "the AEC block; remove or consolidate it before uninstalling.");
        }

        // Skill preflight happens before the AGENTS.md write so a customized skill
        // cannot leave the environment only partly uninstalled.
        var skillPlan = AecSkillInstaller.PrepareUninstall(codexHome);

        if (removal.Removed)
        {
            AtomicFile.ReplaceIfUnchanged(
                agentsPath,
                currentInstructions,
                removal.Content,
                "Runtime instructions");
        }

        // Instructions are removed first. If deletion is interrupted, the remaining
        // unused official skill files can be safely removed by a repeated command.
        AecSkillInstaller.ApplyUninstall(skillPlan);

        var changed = removal.Removed || skillPlan.HasFiles;
        output.WriteLine(changed ? "uninstalled" : "unchanged");
        return 0;
    }
}
