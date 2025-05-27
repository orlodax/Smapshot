using System.Text.Json;
using Smapshot.Models;
using Smapshot.Services;

namespace Smapshot;

public static partial class Program
{
    public static void Main(string[] args)
    {
        if (args is null || args.Length == 0)
        {
            Console.WriteLine("Please specify the .kml file(s) path/name.");
            return;
        }

        // Load app settings from JSON file
        try
        {
            string json = File.ReadAllText("appSettings.json");
            AppSettings? appSettings =
                JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (appSettings == null)
            {
                Console.WriteLine("Failed to deserialize appSettings.json. Using default settings.");
                appSettings = new AppSettings();
            }
            else
            {
                AppSettings.UpdateInstance(appSettings);
            }
        }
        catch
        {
            Console.WriteLine("Error reading appSettings.json. Using default settings.");
            AppSettings appSettings = new();
            using FileStream fs = File.Create("appSettings.json");
            JsonSerializer.Serialize(fs, appSettings, new JsonSerializerOptions { WriteIndented = true });
        }

        // Start the jobs with the provided KML file(s)
        SmapshotManager.StartJobs(args[0]);
    }
}