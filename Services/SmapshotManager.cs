using System.Text.Json;
using SharpKml.Dom;
using Smapshot.Helpers;
using Smapshot.Models;

namespace Smapshot.Services;

internal class SmapshotManager
{
    readonly Dictionary<string, JobContext> jobsInProgress = [];

    internal void StartJob(string inputPath)
    {
        IEnumerable<string> kmlFilePaths = [];

        if (Directory.Exists(inputPath))
        {
            // If a directory is provided, look for a .kml file in it
            kmlFilePaths = Directory.GetFiles(inputPath, "*.kml");
            if (kmlFilePaths.Any())
            {
                Console.WriteLine($"No .kml files found in directory: {inputPath}");
                return;
            }
        }
        else if (File.Exists(inputPath) && Path.GetExtension(inputPath).Equals(".kml", StringComparison.OrdinalIgnoreCase))
        {
            kmlFilePaths = kmlFilePaths.Append(inputPath);
        }
        else
        {
            Console.WriteLine($"Invalid input: {inputPath}. Please provide a valid .kml file or directory containing .kml files.");
            return;
        }

        List<Task> tasks = [];
        foreach (var kmlFilePath in kmlFilePaths)
            tasks.Add(Task.Run(async () => await ProcessKmlFile(kmlFilePath)));

        Task.WaitAll(tasks);
        Console.WriteLine("All jobs completed.");
    }


    private async Task ProcessKmlFile(string kmlFilePath)
    {
        CoordinateCollection coordinates;
        try
        {
            coordinates = KmlHelper.GetPolygonCoordinates(kmlFilePath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing KML file {kmlFilePath}: {ex.Message}");
            return;
        }

        JobContext jobContext = new(coordinates);
        jobsInProgress[kmlFilePath] = jobContext;

        await jobContext.DownloadRegionData();
        jobContext.RenderOsmData();
        jobContext.ExportMapToPdf(kmlFilePath);
    }
}
