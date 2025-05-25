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
using System.Net;
using Newtonsoft.Json.Linq;
using System.Text;
using RiotHelperLib;

namespace LeaguePatchCollection
{
    public partial class LeaguePatchCollectionUX : Form
    {
        public static bool Headless { get; private set; } = false;
        public static string? LatestBloatKey { get; private set; }

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        private const int WM_NCLBUTTONDOWN = 0xA1;
        private const int HTCAPTION = 0x2;
        private static readonly char[] separator = [' '];

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        private const int SW_HIDE = 0;

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
            AutoAccept.Checked = SettingsManager.ConfigSettings.AutoAccept;
            ShowOfflineButton.Checked = SettingsManager.ChatSettings.EnableOffline;
            ShowMobileButton.Checked = SettingsManager.ChatSettings.EnableMobile;
            ShowAwayButton.Checked = SettingsManager.ChatSettings.EnableAway;
            ShowOnlineButton.Checked = SettingsManager.ChatSettings.EnableOnline;
            ArgsBox.Text = SettingsManager.ConfigSettings.Args;
            this.Shown += LeaguePatchCollectionUX_Shown;
        }

        private async void LeaguePatchCollectionUX_Shown(object? sender, EventArgs e)
        {
            await FetchLatestBloatKeyAsync();

            await LeagueProxy.Start();

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
            MessageBox.Show("Ban reason pulled from login queue endpoint: " + PlatformProxy.banReason, "League Patch Collection", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void CleanLogsButton_Click(object sender, EventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Application.ExecutablePath,
                Arguments = "--logclean",
                Verb = "runas",
                UseShellExecute = true
            });
        }

        private void StartButton_Click(object sender, EventArgs e)
        {
            string args = SettingsManager.ConfigSettings.Args;

            string[] argsArray = args.Split(separator, StringSplitOptions.RemoveEmptyEntries);

            if (!argsArray.Contains("--allow-multiple-clients"))
            {
                RiotClient.TerminateRiotServices();
            }

            LeagueProxy.LaunchRCS(argsArray);
        }

        private void CloseClientsButton_Click(object sender, EventArgs e)
        {
            RiotClient.TerminateRiotServices();
        }
        private async void RestartUXbutton_Click(object sender, EventArgs e)
        {
            await LcuApi.SendRequest("/riotclient/kill-and-restart-ux", HttpMethod.Post);
        }
        private async void DodgeButton_Click(object sender, EventArgs e)
        {
            await LcuApi.SendRequest("/lol-login/v1/session/invoke?destination=lcdsServiceProxy&method=call&args=[\"\",\"teambuilder-draft\",\"quitV2\",\"\"]", HttpMethod.Post);
        }
        private async void DisconnectChatButton_Click(object sender, EventArgs e)
        {
            var response = await RcsApi.SendRequest("/chat/v1/session", HttpMethod.Get);
            if (response == null || !response.IsSuccessStatusCode)
            {
                Trace.WriteLine("[ERROR] Failed to fetch chat session state.");
                return;
            }

            string content = await response.Content.ReadAsStringAsync();

            var sessionData = JsonConvert.DeserializeObject<JObject>(content);

            if (sessionData?["state"]?.ToString() == "connected")
            {
                var suspendContent = new StringContent("{\"config\":\"disable\"}", Encoding.UTF8, "application/json");
                await RcsApi.SendRequest("/chat/v1/suspend", HttpMethod.Post, suspendContent);
            }
            else
            {
                await RcsApi.SendRequest("/chat/v1/resume", HttpMethod.Post);
            }
        }
        private void RemoveVgk_Click(object sender, EventArgs e)
        {
            ProcessUtil.RemoveVanguard();
        }
        private void TpmBypass_Click(object sender, EventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = Application.ExecutablePath,
                Arguments = "--tpm",
                Verb = "runas",
                UseShellExecute = true
            });
        }
        private void DisableVanguard_CheckedChanged(object sender, EventArgs e)
        {
            SettingsManager.ConfigSettings.Novgk = DisableVanguard.Checked;
            SettingsManager.SaveSettings();
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

        private void AutoAccept_CheckedChanged(object sender, EventArgs e)
        {
            SettingsManager.ConfigSettings.AutoAccept = AutoAccept.Checked;
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
                            settings?.ConfigSettings.AutoAccept == null ||
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
                            ConfigSettings.AutoAccept = settings?.ConfigSettings.AutoAccept;
                            ConfigSettings.Nobloatware = settings?.ConfigSettings.Nobloatware;
                            ConfigSettings.Nobehavior = settings?.ConfigSettings.Nobehavior;
                            ConfigSettings.Args = settings?.ConfigSettings.Args ?? string.Empty;
                        }
                    }
                    else
                    {
                        SaveSettings();
                    }
                }
                catch (Exception ex)
                {
                    Trace.WriteLine($" [ERROR] An error occurred while loading settings, resetting to defaults: {ex.Message}");
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
                        ConfigSettings.AutoAccept,
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
            public bool AutoAccept { get; set; } = true;
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
        private static async Task FetchLatestBloatKeyAsync()
        {
            try
            {
                var githubUrl = "https://raw.githubusercontent.com/Cat1Bot/league-patch-collection/refs/heads/main/latestbloatkey";

                using var client = new HttpClient(new HttpClientHandler
                {
                    UseCookies = false,
                    UseProxy = false,
                    Proxy = null,
                    ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true,
                    CheckCertificateRevocationList = false
                });
                client.Timeout = TimeSpan.FromSeconds(5);

                var result = await client.GetStringAsync(githubUrl);
                LatestBloatKey = result.Trim();
            }
            catch (TaskCanceledException)
            {
                Trace.WriteLine(" [ERROR] Request timed out while fetching the bloat key.");
            }
            catch (Exception ex)
            {
                Trace.WriteLine($" [ERROR] Failed to fetch latest bloat key: {ex.Message}");
            }
        }
        internal static class Program
        {
            [STAThread]
            static void Main(string[] args)
            {
                string logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LeaguePatchCollection");
                if (!Directory.Exists(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }
                string logFilePath = Path.Combine(logDirectory, "debug.log");
                Trace.Listeners.Add(new TimestampedTextWriterTraceListener(logFilePath));
                Trace.AutoFlush = true;

                if (args.Contains("--logclean"))
                {
                    LogCleaner.ClearLogs();
                    return;
                }

                if (args.Contains("--tpm"))
                {
                    Trace.WriteLine(" [INFO] Running TPM bypass.");
                    EnumWindows((hWnd, lParam) =>
                    {
                        StringBuilder sb = new StringBuilder(256);
                        GetWindowText(hWnd, sb, sb.Capacity);
                        string windowTitle = sb.ToString();

                        if (windowTitle.StartsWith("VAN"))
                        {
                            ShowWindow(hWnd, SW_HIDE);
                            Trace.WriteLine($" [INFO] Hiding VAN popup: {windowTitle}");
                        }

                        return true;
                    }, IntPtr.Zero);
                    return;
                }

                if (args.Contains("--headless"))
                {
                    Headless = true;
                }

                ApplicationConfiguration.Initialize();
                Application.Run(new LeaguePatchCollectionUX());
                LeagueProxy.Stop();
            }
        }
    }
}
