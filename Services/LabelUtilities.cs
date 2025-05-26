using SkiaSharp;

namespace Smapshot.Services;

// Helper class for managing label collisions on maps
internal static class LabelUtilities
{
    // List of rectangles for labels that have been drawn
    private static readonly List<SKRect> DrawnLabelRects = [];

    // Clear all tracked labels
    public static void ClearLabelRects()
    {
        DrawnLabelRects.Clear();
    }

    // Add a label rectangle to the collection
    public static void AddLabelRect(SKRect rect)
    {
        DrawnLabelRects.Add(rect);
    }

    // Check if a rectangle would overlap with any existing labels
    public static bool RectangleOverlapsWithExistingLabels(SKRect rect, float padding = 5.0f)
    {
        // Add padding to the rectangle to ensure some space between labels
        var paddedRect = rect;
        paddedRect.Inflate(padding, padding);

        foreach (var existingRect in DrawnLabelRects)
        {
            if (RectanglesIntersect(paddedRect, existingRect))
            {
                return true;
            }
        }
        return false;
    }

    // Check if two rectangles intersect
    private static bool RectanglesIntersect(SKRect a, SKRect b)
    {
        return a.Left < b.Right && a.Right > b.Left && a.Top < b.Bottom && a.Bottom > b.Top;
    }

    // Convert geo coordinates to final canvas coordinates for collision detection
    public static SKPoint GeoToFinalCanvasPoint(
        double lon, double lat,
        double minLon, double maxLat,
        double lonCorrection, double scale,
        float polyCenterX, float polyCenterY,
        double rotationAngle, float scaleToFit,
        float finalCenterX, float finalCenterY)
    {
        // Convert geo to pixel coordinates in the source map
        float x = (float)((lon - minLon) * lonCorrection * scale);
        float y = (float)((maxLat - lat) * scale);

        // Apply the same transformations used to draw the map
        // 1. Translate to center on the polygon
        float translatedX = x - polyCenterX;
        float translatedY = y - polyCenterY;

        // 2. Scale
        float scaledX = translatedX * scaleToFit;
        float scaledY = translatedY * scaleToFit;

        // 3. Rotate
        double rotationRadians = rotationAngle * Math.PI / 180.0;
        float rotatedX = (float)(scaledX * Math.Cos(rotationRadians) - scaledY * Math.Sin(rotationRadians));
        float rotatedY = (float)(scaledX * Math.Sin(rotationRadians) + scaledY * Math.Cos(rotationRadians));

        // 4. Translate to final position
        float finalX = rotatedX + finalCenterX;
        float finalY = rotatedY + finalCenterY;

        return new SKPoint(finalX, finalY);
    }
}
