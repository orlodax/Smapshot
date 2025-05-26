using System.Text.Json;
using Smapshot.Models;
using Smapshot.Services;

namespace Smapshot;

public static partial class Program
{
    public static AppSettings AppSettings { get; private set; } = new();

    public static void Main(string[] args)
    {
        if (args is null || args.Length == 0)
        {
            Console.WriteLine("Please specify the .kml file(s) path/name.");
            return;
        }

        LoadAppSettings();

        new SmapshotManager()
            .StartJob(args[0]);
    }

    static void LoadAppSettings()
    {
        try
        {
            string json = File.ReadAllText("appSettings.json");
            AppSettings = JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch
        {
            Console.WriteLine("Error reading appSettings.json. Using default settings.");
            AppSettings = new AppSettings();
            using FileStream fs = File.Create("appSettings.json");
            JsonSerializer.Serialize(fs, AppSettings, new JsonSerializerOptions { WriteIndented = true });
        }
    }
}