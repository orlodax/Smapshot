using OsmSharp.Streams;
using Smapshot.Models;

namespace Smapshot.Services;

internal class SmapshotManager
{
    static internal void StartJobs(string inputPath)
    {
        IEnumerable<string> kmlFilePaths = [];

        if (Directory.Exists(inputPath))
        {
            // If a directory is provided, look for a .kml file in it
            kmlFilePaths = Directory.GetFiles(inputPath, "*.kml");
            if (!kmlFilePaths.Any())
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
            tasks.Add(Task.Run(async () => await StartJob(kmlFilePath)));

        Task.WaitAll(tasks);
        Console.WriteLine("All jobs completed.");
    }

    static async Task StartJob(string kmlFilePath)
    {
        ArgumentNullException.ThrowIfNull(kmlFilePath, nameof(kmlFilePath));
        KmlService kmlService = new(kmlFilePath);

        // Parse the KML file to extract coordinates
        try
        {
            kmlService.ParsePolygonCoordinates();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing KML file {kmlFilePath}: {ex.Message}");
            throw;
        }

        if (kmlService.Coordinates == null || kmlService.Coordinates.Count == 0)
        {
            Console.WriteLine("No coordinates found in the KML file.");
            throw new ArgumentException("The KML file does not contain valid coordinates.", nameof(kmlFilePath));
        }

        BoundingBoxGeo boundingBox = new(
            north: kmlService.Coordinates.Max(c => c.Latitude),
            south: kmlService.Coordinates.Min(c => c.Latitude),
            east: kmlService.Coordinates.Max(c => c.Longitude),
            west: kmlService.Coordinates.Min(c => c.Longitude)
        );

        // Download OSM data for the bounding box
        OsmDownloader osmDownloader = new(boundingBox.GetExpandedBoundingBox());
        XmlOsmStreamSource osmData = await osmDownloader.DownloadRegion();

        // Render the OSM data
        OsmRenderEngine osmRenderEngine = new(osmData, boundingBox.GetExpandedBoundingBox(), kmlService);
        byte[] fullMapImage = osmRenderEngine.RenderOsmData();

        // Export the map to PDF
        PdfGenerator pdfGenerator = new(kmlFilePath, fullMapImage);
        pdfGenerator.Generate();

        Console.WriteLine($"OSM full map rendering completed for {kmlFilePath}");
    }
}
