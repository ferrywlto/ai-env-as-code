namespace Aec.Tests;

internal static class TestTemporaryDirectory
{
    // macOS may report a temporary directory through a symbolic link.
    // Fixture roots must also work with AEC's no-link path validation.
    public static string Root => Environment.GetEnvironmentVariable("AEC_TEST_TEMP_ROOT")
        ?? (OperatingSystem.IsMacOS() ? "/private/tmp" : Path.GetTempPath());
}
