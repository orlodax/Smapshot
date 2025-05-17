using Smapshot.Models;

namespace Smapshot.Helpers;

public static class MapHelper
{
    public static (int minTileX, int maxTileX, int minTileY, int maxTileY) GetTileBounds(BoundingBoxGeo expandedBoundingBox, int zoom) =>
        (
            LonToTileX(expandedBoundingBox.West, zoom),
            LonToTileX(expandedBoundingBox.East, zoom),
            LatToTileY(expandedBoundingBox.North, zoom), // Note: Y is inverted in tile system
            LatToTileY(expandedBoundingBox.South, zoom)
        );

    public static int LonToTileX(double lon, int zoom) => (int)Math.Floor((lon + 180.0) / 360.0 * (1 << zoom));

    public static int LatToTileY(double lat, int zoom) =>
        (int)Math.Floor((1 - Math.Log(Math.Tan(lat * Math.PI / 180.0) + 1.0 / Math.Cos(lat * Math.PI / 180.0)) / Math.PI) / 2.0 * (1 << zoom));

    public static int LonToPixelX(double lon, int zoom) => (int)Math.Floor((lon + 180.0) / 360.0 * (1 << zoom) * 256);

    public static int LatToPixelY(double lat, int zoom) =>
        (int)Math.Floor((1 - Math.Log(Math.Tan(lat * Math.PI / 180.0) + 1.0 / Math.Cos(lat * Math.PI / 180.0)) / Math.PI) / 2.0 * (1 << zoom) * 256);
}
