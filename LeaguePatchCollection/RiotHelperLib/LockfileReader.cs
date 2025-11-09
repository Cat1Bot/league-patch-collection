using System.Diagnostics;

namespace LeaguePatchCollection.RiotHelperLib;

internal static class LockfileReader
{
    internal static async Task<string?> ReadLockfileAsync(string lockfilePath)
    {
        if (!File.Exists(lockfilePath))
        {
            Trace.WriteLine($" [ERROR] Lockfile does not exist at {lockfilePath}");
            return null;
        }

        try
        {
            return await ReadFile(lockfilePath);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($" [ERROR] Failed to access lockfile: {ex.Message}");
            return null;
        }
    }

    private static async Task<string> ReadFile(string filePath)
    {
        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fileStream);
        return await reader.ReadToEndAsync();
    }
}
