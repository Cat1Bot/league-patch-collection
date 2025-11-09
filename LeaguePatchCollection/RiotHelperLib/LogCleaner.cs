using System.Diagnostics;

namespace LeaguePatchCollection.RiotHelperLib;

public class LogCleaner
{
    public static void ClearLogs()
    {
        bool stopped = ProcessUtil.TerminateRiotServices();
        if (stopped) throw new Exception("Failed to terminate Riot services.");
        
        try
        {
            DeleteFolder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Riot Games"));
            DeleteFolder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lion"));
            DeleteFolder(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "riot-client-ux"));
            DeleteFolder(@"C:\Riot Games\League of Legends\Logs");
            DeleteFolder(@"C:\Riot Games\League of Legends\Cookies");
            DeleteFolder(@"C:\Riot Games\League of Legends\Saved");
            DeleteFolder(@"C:\Riot Games\League of Legends\Config");
            DeleteFolder(@"C:\Riot Games\League of Legends\GPUCache");
            DeleteFolder(@"C:\Riot Games\League of Legends\Game\Logs");

            DeleteFile(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), @"Riot Games\machine.cfg"));
            DeleteFile(@"C:\Riot Games\League of Legends\debug.log");

            MessageBox.Show("Logs cleaned successfully!", "League Patch Collection", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to clear logs, try running as admin. If this issue persist, open a new issue on Github.", "League Patch Collection", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Trace.WriteLine($" [ERROR] Log cleaner failed with exception: {ex.Message}");
        }
    }

    private static void DeleteFolder(string folderPath)
    {
        if (Directory.Exists(folderPath))
        {
            Directory.Delete(folderPath, true);
        }
    }

    private static void DeleteFile(string filePath)
    {
        if (File.Exists(filePath))
        {
            File.Delete(filePath);
        }
    }
}
