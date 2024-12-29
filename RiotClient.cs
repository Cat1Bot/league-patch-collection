﻿using System.Diagnostics;
using System.Text.Json.Nodes;
using System.Text.Json;

namespace LeaguePatchCollection
{
    internal sealed class RiotClient
    {
        public RiotClient() { }

        public Process? Launch(string configServerUrl, IEnumerable<string>? args = null)
        {
            var path = GetPath();
            if (path is null)
                return null;

            IEnumerable<string> allArgs = [$"--client-config-url={configServerUrl}", "--launch-product=league_of_legends", "--launch-patchline=live", .. args ?? []];

            var processStartInfo = new ProcessStartInfo
            {
                FileName = path,
                Arguments = string.Join(" ", allArgs),
                RedirectStandardOutput = true,  // Redirecting standard output
                RedirectStandardError = true,   // Redirecting standard error
            };

            var process = Process.Start(processStartInfo);

            if (process != null)
            {
                process.OutputDataReceived += (sender, e) => {  };
                process.ErrorDataReceived += (sender, e) => {  };
            }

            return process;
        }

        private string? GetPath()
        {
            string installPath;

            if (OperatingSystem.IsMacOS())
            {
                installPath = "/Users/Shared/Riot Games/RiotClientInstalls.json";
            }
            else
            {
                installPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                                           "Riot Games/RiotClientInstalls.json");
            }

            if (!File.Exists(installPath))
                return null;

            try
            {
                var data = JsonSerializer.Deserialize<JsonNode>(File.ReadAllText(installPath));
                var rcPaths = new List<string?> { data?["rc_default"]?.ToString(), data?["rc_live"]?.ToString(), data?["rc_beta"]?.ToString() };

                return rcPaths.FirstOrDefault(File.Exists);
            }
            catch
            {
                return null;
            }
        }
    }
}
