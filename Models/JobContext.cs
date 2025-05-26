using OsmSharp.Streams;
using SharpKml.Dom;
using Smapshot.Helpers;

namespace Smapshot.Models;

internal class JobContext
{
    readonly CoordinateCollection coordinates;
    readonly BoundingBoxGeo boundingBox;
    XmlOsmStreamSource? osmData;
    byte[]? fullMapImage;

    internal JobContext(CoordinateCollection coordinates)
    {
        this.coordinates = coordinates ?? throw new ArgumentNullException(nameof(coordinates));

        boundingBox = new(
            north: coordinates.Max(c => c.Latitude),
            south: coordinates.Min(c => c.Latitude),
            east: coordinates.Max(c => c.Longitude),
            west: coordinates.Min(c => c.Longitude)
        );
    }

    internal async Task DownloadRegionData()
    {
        osmData = await OsmDataHelper.DownloadRegion(boundingBox.GetExpandedBoundingBox(0.5));
        Console.WriteLine($"OSM data downloaded for bounding box: N{boundingBox.North}, S{boundingBox.South}, E{boundingBox.East}, W{boundingBox.West}");
    }
    internal void RenderOsmData()
    {
        fullMapImage = OsmRenderHelper.RenderOsmData(osmData, boundingBox.GetExpandedBoundingBox(0.5), coordinates);
        Console.WriteLine("OSM full map rendering completed.");
    }

    internal void ExportMapToPdf(string kmlFilePath)
    {
        ArgumentNullException.ThrowIfNull(fullMapImage, nameof(fullMapImage));

        PdfGenerator.Generate(kmlFilePath, fullMapImage);
    }
}