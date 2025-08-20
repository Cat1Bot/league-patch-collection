using System.ComponentModel;
using System.Diagnostics;

namespace RiotHelperLib;

public static class ProcessUtil
{
    private static bool IsProcessRunning(params string[] processNames)
    {
        if (OperatingSystem.IsMacOS())
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "/bin/ps",
                    Arguments = "-axo comm",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return false;

                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                foreach (var line in output.Split('\n'))
                {
                    var procName = Path.GetFileName(line.Trim());
                    foreach (var target in processNames)
                    {
                        if (procName.Equals(target, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine($" [WARN] Could not check processes: {ex.Message}");
            }

            return false;
        }
        else
        {
            foreach (var name in processNames)
            {
                try
                {
                    if (Process.GetProcessesByName(name).Length != 0)
                    {
                        return true;
                    }
                }
                catch
                {
                    Trace.WriteLine(" [WARN] Could not check process status, user should try running app as administrator.");
                }
            }
            return false;
        }
    }

    public static bool IsLeagueClientRunning()
    {
        return IsProcessRunning("LeagueClient");
    }

    public static bool IsRiotClientRunning()
    {
        return IsProcessRunning("RiotClientServices");
    }

    public static void TerminateRiotServices()
    {
        string[] riotProcesses = ["RiotClientServices", "LeagueClient"];

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
            }
        }
    }

    public static bool RemoveVanguard()
    {
        if (!OperatingSystem.IsWindows())
        {
            return true;
        }

        string vgkpath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Riot Vanguard", "installer.exe");

        if (!File.Exists(vgkpath))
        {
            return true;
        }

        try
        {
            using var process = Process.Start(vgkpath, "--quiet");
            if (process != null)
            {
                Trace.WriteLine(" [INFO] Attempting to uninstall Vanguard...");

                process.WaitForExit();
                Thread.Sleep(5000);

                for (int i = 0; i < 30; i++)
                {
                    if (!File.Exists(vgkpath))
                    {
                        MessageBox.Show("Vanguard uninstalled successfully!", "League Patch Collection", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return true;
                    }

                    Thread.Sleep(1000);
                }

                MessageBox.Show("Vanguard uninstallation failed, try running this app as administrator", "League Patch Collection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Trace.WriteLine(" [WARN] Vanguard uninstallation failed, user should try running app as administrator.");
                return false;
            }
            else
            {
                MessageBox.Show("Could not start Vanguard uninstaller, try running this app as administrator", "League Patch Collection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Trace.WriteLine(" [WARN] Could not start Vanguard uninstaller, user should try running app as administrator.");
                return false;
            }
        }
        catch (Exception ex)
        {
            Trace.WriteLine($" [ERROR] Vanguard uninstallation aborted: {ex}");
            return false;
        }
    }
}