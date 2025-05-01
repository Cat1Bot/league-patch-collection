using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace RiotHelperLib;

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
            return OperatingSystem.IsMacOS() ? await ReadFileMac(lockfilePath) : await ReadFileWindows(lockfilePath);
        }
        catch (Exception ex)
        {
            Trace.WriteLine($" [ERROR] Failed to access lockfile: {ex.Message}");
            return null;
        }
    }

    private static async Task<string> ReadFileWindows(string filePath)
    {
        using var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fileStream);
        return await reader.ReadToEndAsync();
    }

    private static async Task<string> ReadFileMac(string filePath)
    {
        ProcessStartInfo psi = new()
        {
            FileName = "/bin/cat",
            Arguments = $"\"{filePath}\"",
            RedirectStandardOutput = true,
            UseShellExecute = false
        };

        using Process process = Process.Start(psi);
        using StreamReader reader = process.StandardOutput;
        string content = await reader.ReadToEndAsync();
        process.WaitForExit();

        return content;
    }
}
