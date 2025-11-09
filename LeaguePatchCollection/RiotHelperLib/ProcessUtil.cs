using System.Diagnostics;

namespace LeaguePatchCollection.RiotHelperLib;

public static class ProcessUtil
{
    public static bool IsProcessRunning(string process)
    {
        try
        {
            if (Process.GetProcessesByName(process).Length != 0)
            {
                return true;
            }
        }
        catch
        {
            Trace.WriteLine(" [WARN] Could not check process status, user should try running app as administrator.");
        }
        return false;
    }

    public static bool TerminateRiotServices()
    {
        string[] riotProcesses = ["RiotClientServices", "LeagueClient", "Lion-Win64-Shipping"];

        foreach (var processName in riotProcesses)
        {
            try
            {
                var processes = Process.GetProcessesByName(processName);

                foreach (var process in processes)
                {
                    process.Kill();
                    process.WaitForExit();
                }
            }
            catch (Exception)
            {
                Trace.WriteLine($" [WARN] Could not terminate {processName}, user should run app as administrator.");
                return false;
            }
        }
        return true;
    }

    public static async Task RemoveVanguard()
    {
        string vgkpath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Riot Vanguard", "installer.exe");

        if (!File.Exists(vgkpath))
        {
            return;
        }

        try
        {
            using var process = Process.Start(vgkpath, "--quiet");
            if (process != null)
            {
                Trace.WriteLine(" [INFO] Attempting to uninstall Vanguard...");

                await process.WaitForExitAsync();
                Thread.Sleep(5000);

                for (int i = 0; i < 30; i++)
                {
                    if (!File.Exists(vgkpath))
                    {
                        MessageBox.Show("Vanguard uninstalled successfully!", "League Patch Collection", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }

                    Thread.Sleep(1000);
                }

                MessageBox.Show("Vanguard uninstallation failed, try running this app as administrator", "League Patch Collection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Trace.WriteLine(" [WARN] Vanguard uninstallation failed, user should try running app as administrator.");
            }
            else
            {
                MessageBox.Show("Could not start Vanguard uninstaller, try running this app as administrator", "League Patch Collection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Trace.WriteLine(" [WARN] Could not start Vanguard uninstaller, user should try running app as administrator.");
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($" [ERROR] Vanguard uninstallation aborted: {ex}");
        }
    }
}