namespace Aec.Tests;

internal static class TestDirectoryCleanup
{
    public static void Delete(string root)
    {
        if (!Directory.Exists(root))
        {
            return;
        }

        // Windows refuses to recursively delete read-only files left by Git in
        // these disposable test repositories. Change only files under this root.
        if (OperatingSystem.IsWindows())
        {
            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
            }
        }

        Directory.Delete(root, recursive: true);
    }
}
