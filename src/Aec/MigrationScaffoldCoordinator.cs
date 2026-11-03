namespace Aec;

internal sealed record MigrationScaffoldResult(
    MigrationPreparation Preparation, IReadOnlyList<string> WrittenPaths);

internal static class MigrationScaffoldCoordinator
{
    public static MigrationScaffoldResult CaptureAndScaffold(MigrationPreparation preparation)
    {
        var captured = MigrationCaptureCoordinator.Capture(preparation);
        var preview = MigrationScaffoldPreview.Read(captured);
        var written = MigrationScaffoldWriter.Write(preview);
        return new MigrationScaffoldResult(captured, written);
    }
}
