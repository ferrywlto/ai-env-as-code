namespace Aec;

internal static class CopilotStatusCommand
{
    public static int Run(string repository, string copilotHome, TextWriter output)
    {
        AecApplication.EnsureNoLinksInExistingPath(repository, "Repository path");
        AecApplication.EnsureNoLinksInExistingPath(copilotHome, "Copilot home path");
        ApplyCommand.EnsureRuntimeOutsideRepository(
            repository,
            copilotHome,
            "copilot-instructions.md",
            "Copilot runtime target");
        AecApplication.EnsureRealDirectory(repository, "Repository");
        AecApplication.EnsureSourceDirectories(repository);
        AecApplication.EnsureRealDirectory(copilotHome, "Copilot home");

        var canonicalPath = Path.Combine(repository, AecApplication.CopilotSourceRelativePath);
        var providerDirectory = Path.GetDirectoryName(canonicalPath)
            ?? throw new InvalidOperationException(
                "Canonical Copilot instructions have no provider directory.");
        AecApplication.EnsureNoLinksInExistingPath(providerDirectory, "Copilot provider path");
        AecApplication.EnsureRealDirectory(providerDirectory, "Copilot provider directory");

        var canonical = AecApplication.ReadRequiredTextFile(
            canonicalPath,
            "Canonical Copilot instructions");
        var runtimePath = Path.Combine(copilotHome, "copilot-instructions.md");
        var runtime = AecApplication.ReadOptionalTextFile(
            runtimePath,
            "Copilot runtime instructions");
        var status = runtime is null
            ? "missing"
            : canonical.AsSpan().SequenceEqual(runtime)
                ? "in_sync"
                : "different";

        output.WriteLine($"copilot/copilot-instructions.md {status}");
        return status == "in_sync" ? 0 : 2;
    }
}
