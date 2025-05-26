using OsmSharp.Streams;
using Smapshot.Services;

namespace Smapshot.Models;

internal class JobContext(string kmlFilePath)
{
    readonly KmlService kmlHelper = new(kmlFilePath);
    BoundingBoxGeo? boundingBox;
    XmlOsmStreamSource? osmData;
    byte[]? fullMapImage;

    internal void ParseKmlFile()
    {
        ArgumentNullException.ThrowIfNull(kmlFilePath, nameof(kmlFilePath));

        try
        {
            kmlHelper.ParsePolygonCoordinates();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing KML file {kmlFilePath}: {ex.Message}");
            throw;
        }

        if (kmlHelper.Coordinates == null || kmlHelper.Coordinates.Count == 0)
        {
            Console.WriteLine("No coordinates found in the KML file.");
            throw new ArgumentException("The KML file does not contain valid coordinates.", nameof(kmlFilePath));
        }

        boundingBox = new(
            north: kmlHelper.Coordinates.Max(c => c.Latitude),
            south: kmlHelper.Coordinates.Min(c => c.Latitude),
            east: kmlHelper.Coordinates.Max(c => c.Longitude),
            west: kmlHelper.Coordinates.Min(c => c.Longitude)
        );
    }

    internal async Task DownloadRegionData()
    {
        ArgumentNullException.ThrowIfNull(boundingBox, nameof(boundingBox));

        OsmDownloader osmDownloader = new(boundingBox.GetExpandedBoundingBox());
        osmData = await osmDownloader.DownloadRegion();
        Console.WriteLine($"{kmlFilePath} OSM data downloaded for bounding box: N{boundingBox.North}, S{boundingBox.South}, E{boundingBox.East}, W{boundingBox.West}");
    }
    internal void RenderOsmData()
    {
        ArgumentNullException.ThrowIfNull(osmData, nameof(osmData));
        ArgumentNullException.ThrowIfNull(boundingBox, nameof(boundingBox));

        OsmRenderEngine osmRenderEngine = new(osmData, boundingBox.GetExpandedBoundingBox(), kmlHelper);
        fullMapImage = osmRenderEngine.RenderOsmData();
        Console.WriteLine($"{kmlFilePath} OSM full map rendering completed.");
    }

    internal void ExportMapToPdf()
    {
        ArgumentNullException.ThrowIfNull(fullMapImage, nameof(fullMapImage));

        PdfGenerator pdfGenerator = new(kmlFilePath, fullMapImage);
        pdfGenerator.Generate();
    }
}