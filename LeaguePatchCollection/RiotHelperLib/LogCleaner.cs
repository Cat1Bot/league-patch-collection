using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;

namespace RiotHelperLib;

public class LogCleaner
{
    public static void ClearLogs()
    {
        ProcessUtil.TerminateRiotServices();
        if (OperatingSystem.IsWindows())
        {
            if (!IsRunningAsAdmin())
            {
                MessageBox.Show("Please run this app as administrator to perform this action.", "League Patch Collection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Trace.WriteLine(" [INFO] Log cleaner action requires administrator privileges. UAC request initiated.");
                return;
            }
        }

        try
        {
            if (OperatingSystem.IsMacOS())
            {
                DeleteFolder(Path.Combine("/Applications/League of Legends.app/Contents/LoL", "Logs"));
                DeleteFolder(Path.Combine("/Applications/League of Legends.app/Contents/LoL", "Saved"));
                DeleteFolder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Application Support/CrashReporter"));
                DeleteFolder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Logs/riot-client-ux"));
                DeleteFolder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Caches/com.riotgames.RiotGames.RiotClient"));
                DeleteFolder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Caches/com.riotgames.LeagueofLegends.LeagueClient"));
                DeleteFolder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Application Support/Riot Games"));
                DeleteFolder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Application Support/com.riotgames.LeagueofLegends.GameClient"));
                DeleteFolder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Application Support/com.riotgames.LeagueofLegends.LeagueClient"));
                DeleteFolder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Application Support/riot-client-ux"));
                DeleteFolder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Logs/Riot Games"));
                DeleteFolder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/HTTPStorages/com.riotgames.LeagueofLegends.LeagueClient"));
                DeleteFolder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/HTTPStorages/com.riotgames.RiotGames.RiotClient"));
                DeleteFolder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Saved Application State/com.riotgames.LeagueofLegends.GameClient.savedState"));
                DeleteFolder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Saved Application State/com.riotgames.LeagueofLegends.LeagueClientUx.savedState"));
                DeleteFolder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Saved Application State/com.riotgames.RiotGames.RiotClient.savedState"));

                DeleteFile(Path.Combine("/Applications/League of Legends.app/Contents/LoL", "Web Data"));
                DeleteFile(Path.Combine("/Applications/League of Legends.app/Contents/LoL", "Web Data-journal"));
                DeleteFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Preferences/com.riotgames.LeagueofLegends.LeagueClientUxHelper.plist"));
                DeleteFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Preferences/com.riotgames.RiotGames.RiotClient.plist"));
                DeleteFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library/Logs/LeagueClientUx_debug.log"));

                string? tmpDir = Environment.GetEnvironmentVariable("TMPDIR");
                if (!string.IsNullOrEmpty(tmpDir) && Directory.Exists(tmpDir))
                {
                    foreach (var file in Directory.GetFiles(tmpDir))
                    {
                        DeleteFile(file);
                    }
                }

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(" [SUCCESS] Logs cleaned successfully.");
                Console.ResetColor();
            }
            else
            {
                DeleteFolder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Riot Games"));
                DeleteFolder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Riot Games"));
                DeleteFolder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "riot-client-ux"));

                string? leagueOfLegendsPath = FindLeagueOfLegendsPath();
                if (!string.IsNullOrEmpty(leagueOfLegendsPath))
                {
                    DeleteFile(Path.Combine(leagueOfLegendsPath, "debug.log"));
                    DeleteFolder(Path.Combine(leagueOfLegendsPath, "Config"));
                    DeleteFolder(Path.Combine(leagueOfLegendsPath, "Cookies"));
                    DeleteFolder(Path.Combine(leagueOfLegendsPath, "Logs"));
                    DeleteFolder(Path.Combine(leagueOfLegendsPath, "GPUCache"));
                    DeleteFolder(Path.Combine(leagueOfLegendsPath, "Game", "Logs"));

                    MessageBox.Show("Logs cleaned successfully!", "League Patch Collection", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine(" [WARN] League of Legends folder not found.");
                    Console.ResetColor();
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to clear logs, contact c4t_bot on Discord for assistance.", "League Patch Collection", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Trace.WriteLine($" [ERROR] Log cleaner failed with exception: {ex}");
        }
    }
    private static void DeleteFolder(string folderPath)
    {
        try
        {
            if (Directory.Exists(folderPath))
            {
                Directory.Delete(folderPath, true);
                Trace.WriteLine($" [INFO] Deleted folder: {folderPath}");
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($" [ERROR] Failed to delete folder {folderPath}: {ex.Message}");
        }
    }
    private static void DeleteFile(string filePath)
    {
        try
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                Trace.WriteLine($" [INFO] Deleted file: {filePath}");
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($" [ERROR] Failed to delete file {filePath}: {ex.Message}");
        }
    }
    private static string? FindLeagueOfLegendsPath()
    {
        {
            foreach (var drive in DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Fixed))
            {
                string path = Path.Combine(drive.RootDirectory.FullName, "Riot Games", "League of Legends");
                if (Directory.Exists(path))
                {
                    return path;
                }
            }
            return null;
        }
    }
    [SupportedOSPlatform("windows")]
    private static bool IsRunningAsAdmin()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }
}
