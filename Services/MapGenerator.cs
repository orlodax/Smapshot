using System.Data;
using SharpKml.Dom; // For KML Point, if used elsewhere
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Smapshot.Helpers;
using Smapshot.Models;
using System.IO; // For Path operations

namespace Smapshot.Services;

public class MapGenerator
{
    const int TileSize = 256;
    readonly double padding = 0.1;
    readonly CoordinateCollection coordinates;
    readonly BoundingBoxGeo expandedBoundingBox;
    readonly TileManager tileManager;

    public MapGenerator(CoordinateCollection coordinates, string mapStyle)
    {
        this.coordinates = coordinates;
        BoundingBoxGeo polygonBoundingBox = new(
            north: coordinates.Max(c => c.Latitude),
            south: coordinates.Min(c => c.Latitude),
            east: coordinates.Max(c => c.Longitude),
            west: coordinates.Min(c => c.Longitude)
        );
        BoundingBoxGeo paddedBoundingBox = polygonBoundingBox.Pad(padding);
        expandedBoundingBox = paddedBoundingBox.Pad(0.5);
        Console.WriteLine($"Expanded bounding box: ({expandedBoundingBox.West},{expandedBoundingBox.South}) to ({expandedBoundingBox.East},{expandedBoundingBox.North})");
        tileManager = new(polygonBoundingBox, expandedBoundingBox, mapStyle, TileSize);
    }

    internal async Task<string> GenerateMapAsync()
    {
        Image<Rgba32> fullImage = await tileManager.GenerateTilesImageAsync();
        if (fullImage is null)
        {
            Console.WriteLine("Error: Could not generate map image");
            return string.Empty;
        }

        int zoom = tileManager.Zoom;
        (int minTileX, _, int minTileY, _) = MapHelper.GetTileBounds(expandedBoundingBox, zoom);

        var basePolygonVertices = coordinates.Select(coord =>
        {
            double x = MapHelper.LonToPixelX(coord.Longitude, zoom) - minTileX * TileSize;
            double y = MapHelper.LatToPixelY(coord.Latitude, zoom) - minTileY * TileSize;
            return new PointF((float)x, (float)y);
        }).ToList();

        var pixelPoints = basePolygonVertices; // Used by MBR code later
        List<PointF> expandedPolygonVertices = ExpandPolygon(basePolygonVertices, 8);

        (string outputPngPath, _) =
            await OsmSkiaRenderer.RenderBasicMapToPngCropped($"osm_{Guid.NewGuid()}.png", coordinates);

        Console.WriteLine("Applying OSM overlay and visual effects...");
        using (Image<Rgba32> osmOverlayImage = await Image.LoadAsync<Rgba32>(outputPngPath))
        {
            fullImage.Mutate(ctx =>
            {
                // 1. Desaturate the entire fullImage (background)
                ctx.Grayscale(1.0f).Brightness(0.8f);

                // 2. Prepare and draw the OSM overlay (foreground within polygon)
                var targetRectOnFullImage = GetBoundingBox(expandedPolygonVertices);

                if (targetRectOnFullImage.Width > 0 && targetRectOnFullImage.Height > 0)
                {
                    using (var preparedOsmImage = osmOverlayImage.Clone(osmCtx =>
                    {
                        osmCtx.Resize(new ResizeOptions
                        {
                            Size = targetRectOnFullImage.Size,
                            Mode = ResizeMode.Stretch
                        });
                    }))
                    {
                        // Create a temporary image for the clipped OSM overlay
                        using (var clippedOsmPortion = new Image<Rgba32>(targetRectOnFullImage.Width, targetRectOnFullImage.Height, Color.Transparent))
                        {
                            var relativeExpandedVertices = expandedPolygonVertices
                                .Select(p => new PointF(p.X - targetRectOnFullImage.X, p.Y - targetRectOnFullImage.Y))
                                .ToArray();
                            var clippingPathForOverlay = new PathBuilder().AddLines(relativeExpandedVertices).Build();

                            clippedOsmPortion.Mutate(octx =>
                            {
                                octx.SetGraphicsOptions(new GraphicsOptions { Antialias = true });
                                var imageBrush = new ImageBrush(preparedOsmImage);
                                octx.Fill(imageBrush, clippingPathForOverlay);
                            });
                            // Draw this clipped portion onto the main fullImage
                            ctx.DrawImage(clippedOsmPortion, targetRectOnFullImage.Location, 1f);
                        }
                    }
                }
                // 3. Draw the red outline (topmost)
                ctx.DrawPolygon(Color.Red, 4f, expandedPolygonVertices.ToArray());
            });
        }
        Console.WriteLine("OSM overlay and visual effects applied successfully!");

        if (System.IO.File.Exists(outputPngPath))
        {
            try { System.IO.File.Delete(outputPngPath); }
            catch (System.IO.IOException ex) { Console.WriteLine($"Warning: Could not delete temporary OSM image file {outputPngPath}. {ex.Message}"); }
        }

        // --- Rotated rectangle mask crop approach with MBR ---
        // pixelPoints is already defined above using basePolygonVertices
        var (Center, Width, Height, Angle) = ComputeMinimumBoundingRectangle(pixelPoints);
        float rectCenterX = Center.X;
        float rectCenterY = Center.Y;
        float rectW = Width * 1.1f; // Add 10% margin (5% each side)
        float rectH = Height * 1.1f;

        if (rectW >= rectH)
            rectH = rectW * 2400 / 3250;
        else
            rectW = rectH * 3250 / 2400;

        float rotationAngle = Angle;
        float radians = rotationAngle * (float)Math.PI / 180f;
        var halfW = rectW / 2f;
        var halfH = rectH / 2f;

        // 3. Create a mask image (same size as fullImage), draw a white filled rectangle rotated by rotationAngle
        using Image<Rgba32> mask = new(fullImage.Width, fullImage.Height, Color.Black);
        var corners = new[] {
            new PointF(-halfW, -halfH),
            new PointF(halfW, -halfH),
            new PointF(halfW, halfH),
            new PointF(-halfW, halfH)
        };
        var rotatedCorners = corners.Select(p =>
        {
            float x = p.X * (float)Math.Cos(radians) - p.Y * (float)Math.Sin(radians) + rectCenterX;
            float y = p.X * (float)Math.Sin(radians) + p.Y * (float)Math.Cos(radians) + rectCenterY;
            return new PointF(x, y);
        }).ToArray();
        mask.Mutate(ctx => ctx.FillPolygon(Color.White, rotatedCorners));

        // fullImage.Save("full_image.png");
        // mask.Save("mask.png");

        // 4. Cut out the region from the original image using the mask
        Image<Rgba32> cutout = new((int)rectW, (int)rectH);
        cutout.Mutate(ctx => ctx.Clear(Color.Transparent));
        for (int y = 0; y < (int)rectH; y++)
        {
            for (int x = 0; x < (int)rectW; x++)
            {
                float relX = x - halfW;
                float relY = y - halfH;
                float srcX = relX * (float)Math.Cos(radians) - relY * (float)Math.Sin(radians) + rectCenterX;
                float srcY = relX * (float)Math.Sin(radians) + relY * (float)Math.Cos(radians) + rectCenterY;
                int ix = (int)Math.Round(srcX);
                int iy = (int)Math.Round(srcY);
                if (ix >= 0 && ix < fullImage.Width && iy >= 0 && iy < fullImage.Height)
                {
                    if (mask[ix, iy].R > 128)
                    {
                        cutout[x, y] = fullImage[ix, iy];
                    }
                }
            }
        }
        mask.Dispose();

        // 5. Optionally, rotate to portrait (A4) orientation
        Image<Rgba32> portrait;
        if (cutout.Width > cutout.Height)
        {
            // Rotate 90 degrees to make the longer side vertical
            portrait = cutout.Clone(ctx => ctx.Rotate(90));
        }
        else
        {
            portrait = cutout.Clone();
        }
        cutout.Dispose();
        fullImage.Dispose();

        // 6. Optionally, scale for readability
        // float zoomFactor = 1.5f;
        // var scaled = portrait.Clone(ctx => ctx.Resize((int)(portrait.Width * zoomFactor), (int)(portrait.Height * zoomFactor)));
        // portrait.Dispose();

        // Save the map image temporarily
        string tempImagePath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"map_{Guid.NewGuid()}.png");
        portrait.Save(tempImagePath);
        portrait.Dispose();

        return tempImagePath;
    }

    static List<PointF> ExpandPolygon(List<PointF> points, ushort expandBy)
    {
        if (points.Count < 3)
            return points;

        // Find the centroid of the polygon
        float sumX = 0, sumY = 0;
        foreach (var point in points)
        {
            sumX += point.X;
            sumY += point.Y;
        }

        PointF centroid = new(sumX / points.Count, sumY / points.Count);

        // Create expanded points by moving each point away from the centroid
        List<PointF> expandedPoints = [];
        foreach (PointF point in points)
        {
            // Calculate direction vector from centroid to point
            float dirX = point.X - centroid.X;
            float dirY = point.Y - centroid.Y;

            // Calculate distance from centroid to point
            float distance = (float)Math.Sqrt(dirX * dirX + dirY * dirY);

            // Normalize direction vector
            if (distance > 0)
            {
                dirX /= distance;
                dirY /= distance;
            }

            // Add expanded point
            expandedPoints.Add(new PointF(
                point.X + dirX * expandBy,
                point.Y + dirY * expandBy
            ));
        }

        return expandedPoints;
    }

    private static (PointF Center, float Width, float Height, float Angle) ComputeMinimumBoundingRectangle(List<PointF> points)
    {
        // Rotating calipers algorithm for MBR
        float minArea = float.MaxValue;
        PointF bestCenter = default;
        float bestW = 0, bestH = 0, bestAngle = 0;
        for (int i = 0; i < points.Count; i++)
        {
            int j = (i + 1) % points.Count;
            float dx = points[j].X - points[i].X;
            float dy = points[j].Y - points[i].Y;
            float edgeAngle = (float)Math.Atan2(dy, dx);
            float cos = (float)Math.Cos(-edgeAngle);
            float sin = (float)Math.Sin(-edgeAngle);
            var rot = points.Select(p => new PointF(
                p.X * cos - p.Y * sin,
                p.X * sin + p.Y * cos)).ToList();
            float minX = rot.Min(p => p.X);
            float maxX = rot.Max(p => p.X);
            float minY = rot.Min(p => p.Y);
            float maxY = rot.Max(p => p.Y);
            float w = maxX - minX;
            float h = maxY - minY;
            float area = w * h;
            if (area < minArea)
            {
                minArea = area;
                bestW = w;
                bestH = h;
                bestAngle = edgeAngle * 180f / (float)Math.PI;
                float cx = (minX + maxX) / 2f;
                float cy = (minY + maxY) / 2f;
                // Rotate center back
                float origCx = cx * (float)Math.Cos(edgeAngle) - cy * (float)Math.Sin(edgeAngle);
                float origCy = cx * (float)Math.Sin(edgeAngle) + cy * (float)Math.Cos(edgeAngle);
                bestCenter = new PointF(origCx, origCy);
            }
        }
        return (bestCenter, bestW, bestH, bestAngle);
    }

    // Helper method to get the bounding box of a list of points
    private static Rectangle GetBoundingBox(List<PointF> points)
    {
        if (points == null || !points.Any())
        {
            return Rectangle.Empty;
        }
        float minX = points.Min(p => p.X);
        float minY = points.Min(p => p.Y);
        float maxX = points.Max(p => p.X);
        float maxY = points.Max(p => p.Y);
        return new Rectangle((int)Math.Floor(minX), (int)Math.Floor(minY), (int)Math.Ceiling(maxX - minX), (int)Math.Ceiling(maxY - minY));
    }
}