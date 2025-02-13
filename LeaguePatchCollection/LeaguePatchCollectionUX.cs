using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Text;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows.Forms;
using System.Drawing;
using System.Windows.Forms;
using Newtonsoft.Json;
using System.Reflection;

namespace LeaguePatchCollection
{
    public partial class LeaguePatchCollectionUX : Form
    {
        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 0x2;
        private static readonly char[] separator = [' '];

        public LeaguePatchCollectionUX()
        {
            InitializeComponent();

            MainHeaderBackdrop.MouseDown += MainHeaderBackdrop_MouseDown!;
            WindowTitle.MouseDown += WindowTitle_MouseDown!;
            TopWindowIcon.MouseDown += TopWindowIcon_MouseDown!;

            SettingsManager.LoadSettings();

            DisableVanguard.Checked = SettingsManager.ConfigSettings.Novgk;
            LegacyHonor.Checked = SettingsManager.ConfigSettings.Legacyhonor;
            SupressBehavior.Checked = SettingsManager.ConfigSettings.Nobehavior;
            NameChangeBypass.Checked = SettingsManager.ConfigSettings.Namebypass;
            NoBloatware.Checked = SettingsManager.ConfigSettings.Nobloatware;
            NoStore.Checked = SettingsManager.ConfigSettings.NoStore;
            ShowOfflineButton.Checked = SettingsManager.ChatSettings.EnableOffline;
            ShowMobileButton.Checked = SettingsManager.ChatSettings.EnableMobile;
            ShowAwayButton.Checked = SettingsManager.ChatSettings.EnableAway;
            ShowOnlineButton.Checked = SettingsManager.ChatSettings.EnableOnline;
            ArgsBox.Text = SettingsManager.ConfigSettings.Args;
            this.Shown += LeaguePatchCollectionUX_Shown;
        }

        private async void LeaguePatchCollectionUX_Shown(object? sender, EventArgs e)
        {
            LeagueProxy.Start(out _, out _, out _);

            await Task.Delay(1000);

            StartButton.Enabled = true;
            StartButton.Text = "LAUNCH CLIENT";
        }

        private void MainHeaderBackdrop_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _ = ReleaseCapture();
                _ = SendMessage(this.Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
            }
        }
        private void TopWindowIcon_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _ = ReleaseCapture();
                _ = SendMessage(this.Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
            }

        }
        private void WindowTitle_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _ = ReleaseCapture();
                _ = SendMessage(this.Handle, WM_NCLBUTTONDOWN, HTCAPTION, 0);
            }
        }

        private void BanReasonButton_Click(object sender, EventArgs e)
        {
            MessageBox.Show("Ban reason checker is not implemented yet. Coming soon!", "League Patch Collection", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void CleanLogsButton_Click(object sender, EventArgs e)
        {
            if (!IsRunningAsAdmin())
            {
                MessageBox.Show("Please run this app as administrator to perform this action.", "League Patch Collection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                Trace.WriteLine("[INFO] Log cleaner action requires administrator privileges. UAC request initiated.");
                return;
            }

            try
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
                }
                else
                {
                    Trace.WriteLine("[WARN] League of Legends folder not found.");
                    MessageBox.Show("League of Legends folder not found!", "League Patch Collection", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                MessageBox.Show("Logs cleaned successfully!", "League Patch Collection", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"[ERROR] Log cleaner failed with exception: {ex.Message}");
                MessageBox.Show($"An error occurred while cleaning logs: {ex.Message}", "League Patch Collection", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private static bool IsRunningAsAdmin()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

        private static string? FindLeagueOfLegendsPath()
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

        private void StartButton_Click(object sender, EventArgs e)
        {
            string args = SettingsManager.ConfigSettings.Args;

            string[] argsArray = args.Split(separator, StringSplitOptions.RemoveEmptyEntries);

            if (!argsArray.Contains("--allow-multiple-clients"))
            {
                Utility.TerminateRiotServices();
            }

            LeagueProxy.LaunchRCS(argsArray);
        }

        private void CloseClientsButton_Click(object sender, EventArgs e)
        {
            Utility.TerminateRiotServices();
        }
        private void RestartUXbutton_Click(object sender, EventArgs e)
        {
            Utility.RestartUX();
        }
        private void DisconnectChatButton_Click(object sender, EventArgs e)
        {
        }

        private void DisableVanguard_CheckedChanged(object sender, EventArgs e)
        {
            SettingsManager.ConfigSettings.Novgk = DisableVanguard.Checked;
            SettingsManager.SaveSettings();  // Save settings when updated
        }

        private void LegacyHonor_CheckedChanged(object sender, EventArgs e)
        {
            SettingsManager.ConfigSettings.Legacyhonor = LegacyHonor.Checked;
            SettingsManager.SaveSettings();
        }

        private void SupressBehavior_CheckedChanged(object sender, EventArgs e)
        {
            SettingsManager.ConfigSettings.Nobehavior = SupressBehavior.Checked;
            SettingsManager.SaveSettings();
        }

        private void NameChangeBypass_CheckedChanged(object sender, EventArgs e)
        {
            SettingsManager.ConfigSettings.Namebypass = NameChangeBypass.Checked;
            SettingsManager.SaveSettings();
        }

        private void NoBloatware_CheckedChanged(object sender, EventArgs e)
        {
            SettingsManager.ConfigSettings.Nobloatware = NoBloatware.Checked;
            SettingsManager.SaveSettings();
        }

        private void NoStore_CheckedChanged(object sender, EventArgs e)
        {
            SettingsManager.ConfigSettings.NoStore = NoStore.Checked;
            SettingsManager.SaveSettings();
        }

        private void ShowOfflineButton_CheckedChanged(object sender, EventArgs e)
        {
            SettingsManager.ChatSettings.EnableOffline = ShowOfflineButton.Checked;
            SettingsManager.SaveSettings();
        }

        private void ShowMobileButton_CheckedChanged(object sender, EventArgs e)
        {
            SettingsManager.ChatSettings.EnableMobile = ShowMobileButton.Checked;
            SettingsManager.SaveSettings();
        }

        private void ShowAwayButton_CheckedChanged(object sender, EventArgs e)
        {
            SettingsManager.ChatSettings.EnableAway = ShowAwayButton.Checked;
            SettingsManager.SaveSettings();
        }

        private void ShowOnlineButton_CheckedChanged(object sender, EventArgs e)
        {
            SettingsManager.ChatSettings.EnableOnline = ShowOnlineButton.Checked;
            SettingsManager.SaveSettings();
        }
        private void ArgsBox_TextChanged(object sender, EventArgs e)
        {
            SettingsManager.ConfigSettings.Args = ArgsBox.Text;
            SettingsManager.SaveSettings();
        }

        public static class SettingsManager
        {
            private static readonly string settingsFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LeaguePatchCollection");
            private static readonly string settingsFilePath = Path.Combine(settingsFolderPath, "settings.json");

            public static ChatSettings ChatSettings { get; set; } = new ChatSettings();
            public static ConfigSettings ConfigSettings { get; set; } = new ConfigSettings();

            public static void LoadSettings()
            {
                if (!Directory.Exists(settingsFolderPath))
                {
                    Directory.CreateDirectory(settingsFolderPath);
                }

                try
                {
                    if (File.Exists(settingsFilePath))
                    {
                        string json = File.ReadAllText(settingsFilePath);
                        dynamic? settings = JsonConvert.DeserializeObject(json);

                        if (settings?.ChatSettings == null ||
                            settings?.ConfigSettings == null ||
                            settings?.ChatSettings.EnableOffline == null ||
                            settings?.ChatSettings.EnableMobile == null ||
                            settings?.ChatSettings.EnableAway == null ||
                            settings?.ChatSettings.EnableOnline == null ||
                            settings?.ConfigSettings.Novgk == null ||
                            settings?.ConfigSettings.Legacyhonor == null ||
                            settings?.ConfigSettings.Namebypass == null ||
                            settings?.ConfigSettings.NoStore == null ||
                            settings?.ConfigSettings.Nobloatware == null ||
                            settings?.ConfigSettings.Nobehavior == null ||
                            settings?.ConfigSettings.Args == null)
                        {
                            Trace.WriteLine("[WARN] Settings file is invalid or corrupted. Resetting to default values.");
                            SaveSettings();
                        }
                        else
                        {
                            ChatSettings.EnableOffline = settings?.ChatSettings.EnableOffline;
                            ChatSettings.EnableMobile = settings?.ChatSettings.EnableMobile;
                            ChatSettings.EnableAway = settings?.ChatSettings.EnableAway;
                            ChatSettings.EnableOnline = settings?.ChatSettings.EnableOnline;

                            ConfigSettings.Novgk = settings?.ConfigSettings.Novgk;
                            ConfigSettings.Legacyhonor = settings?.ConfigSettings.Legacyhonor;
                            ConfigSettings.Namebypass = settings?.ConfigSettings.Namebypass;
                            ConfigSettings.NoStore = settings?.ConfigSettings.NoStore;
                            ConfigSettings.Nobloatware = settings?.ConfigSettings.Nobloatware;
                            ConfigSettings.Nobehavior = settings?.ConfigSettings.Nobehavior;
                            ConfigSettings.Args = settings?.ConfigSettings.Args ?? string.Empty;
                        }
                    }
                    else
                    {
                        SaveSettings();
                        Trace.WriteLine($"[INFO] Created settings.json in {settingsFilePath}");
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($"[ERROR] An error occurred while loading settings, resetting to defaults: {ex.Message}");
                    SaveSettings();
                }
            }
            public static void SaveSettings()
            {
                var settings = new
                {
                    ChatSettings = new
                    {
                        ChatSettings.EnableOffline,
                        ChatSettings.EnableMobile,
                        ChatSettings.EnableAway,
                        ChatSettings.EnableOnline
                    },
                    ConfigSettings = new
                    {
                        ConfigSettings.Novgk,
                        ConfigSettings.Legacyhonor,
                        ConfigSettings.Namebypass,
                        ConfigSettings.NoStore,
                        ConfigSettings.Nobloatware,
                        ConfigSettings.Nobehavior,
                        ConfigSettings.Args
                    }
                };

                string json = JsonConvert.SerializeObject(settings, Formatting.Indented);
                File.WriteAllText(settingsFilePath, json);
            }
        }

        public class ChatSettings
        {
            public bool EnableOffline { get; set; } = false;
            public bool EnableMobile { get; set; } = false;
            public bool EnableAway { get; set; } = false;
            public bool EnableOnline { get; set; } = true;
        }

        public class ConfigSettings
        {
            public bool Novgk { get; set; } = true;
            public bool Legacyhonor { get; set; } = false;
            public bool Namebypass { get; set; } = false;
            public bool NoStore { get; set; } = false;
            public bool Nobloatware { get; set; } = true;
            public bool Nobehavior { get; set; } = false;
            public string Args { get; set; } = "--launch-product=league_of_legends --launch-patchline=live";

        }
        public class TimestampedTextWriterTraceListener(string fileName) : TextWriterTraceListener(fileName)
        {
            public override void WriteLine(string? message)
            {
                base.WriteLine($"[{DateTime.Now:MM-dd-yyyy hh:mm:ss tt}] {message}");
            }

            public override void Write(string? message)
            {
                base.Write($"[{DateTime.Now:MM-dd-yyyy hh:mm:ss tt}] {message}");
            }
        }

        internal static class Program
        {
            [STAThread]
            static void Main()
            {
                string logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LeaguePatchCollection");
                if (!Directory.Exists(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }
                string logFilePath = Path.Combine(logDirectory, "debug.log");
                Trace.Listeners.Add(new TimestampedTextWriterTraceListener(logFilePath));
                Trace.AutoFlush = true;
                Trace.WriteLine("[INFO] Trace listener initialized.");
                ApplicationConfiguration.Initialize();
                Application.Run(new LeaguePatchCollectionUX());
                LeagueProxy.Stop();
            }
        }
    }
}
