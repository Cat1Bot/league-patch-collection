﻿using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LeaguePatchCollection;

internal sealed class RiotClient
{
    public RiotClient()
    {

    }

    public static Process? Launch(string configServerUrl, IEnumerable<string>? args = null)
    {
        var path = GetPath();
        if (path is null)
            return null;

        IEnumerable<string> allArgs = [$"--client-config-url={configServerUrl}", "--launch-product=league_of_legends", "--launch-patchline=live", .. args ?? []];

        return Process.Start(path, allArgs);
    }

    private static string? GetPath()
    {
        string installPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                                           "Riot Games/RiotClientInstalls.json");

        if (File.Exists(installPath))
        {
            try
            {
                var data = JsonSerializer.Deserialize<JsonNode>(File.ReadAllText(installPath));
                var rcPaths = new List<string?>
            {
                data?["rc_default"]?.ToString(),
                data?["rc_live"]?.ToString(),
                data?["rc_beta"]?.ToString()
            };

                var validPath = rcPaths.FirstOrDefault(File.Exists);
                if (validPath != null)
                    return validPath;
            }
            catch (Exception ex)
            {
                Trace.WriteLine($"An error occurred while processing the install path: {ex.Message}");
            }
        }

        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
        {
            var potentialPath = Path.Combine(drive.RootDirectory.FullName, "Riot Games", "Riot Client", "RiotClientServices.exe");
            if (File.Exists(potentialPath))
                return potentialPath;
        }

        return null;
    }
}