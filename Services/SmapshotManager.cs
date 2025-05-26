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
        JobContext jobContext = new(kmlFilePath);
        jobsInProgress[kmlFilePath] = jobContext;

        jobContext.ParseKmlFile();
        await jobContext.DownloadRegionData();
        jobContext.RenderOsmData();
        jobContext.ExportMapToPdf();
    }
}
