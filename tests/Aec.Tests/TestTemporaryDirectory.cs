namespace Aec.Tests;

internal static class TestTemporaryDirectory
{
    // Keep default fixtures beside the test binaries in the checkout's ignored
    // build output. AEC checks fixture paths for symbolic links.
    public static string Root => Environment.GetEnvironmentVariable("AEC_TEST_TEMP_ROOT")
        ?? Path.Combine(AppContext.BaseDirectory, "fixtures");
}
