using System.Numerics;
using OsmSharp;
using OsmSharp.Streams;
using OsmSharp.Tags;
using SharpKml.Dom;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SkiaSharp;
using Smapshot.Models;

namespace Smapshot.Helpers;

internal static class OsmRenderHelper
{
    const int targetWidth = 2500;
    const int targetHeight = 3250;
    const double margin = 0.1; // 10% margin around polygon (0.0 = no margin, 0.1 = 10% margin)

    static readonly AppSettings appSettings = AppSettings.Instance;

    static double minLat;
    static double maxLat;
    static double minLon;
    static double maxLon;
    static double polyMinLat;
    static double polyMaxLat;
    static double polyMinLon;
    static double polyMaxLon;
    static double lonCorrection;
    static double scale;

    internal static byte[] RenderOsmData(XmlOsmStreamSource? osmData, BoundingBoxGeo expandedBoundingBox, CoordinateCollection polygonCoordinates)
    {
        ArgumentNullException.ThrowIfNull(osmData, nameof(osmData));

        // Size of the expanded map bounding box
        minLat = expandedBoundingBox.South;
        maxLat = expandedBoundingBox.North;
        minLon = expandedBoundingBox.West;
        maxLon = expandedBoundingBox.East;
        // Size of the polygon map bounding box
        polyMinLat = polygonCoordinates.Min(c => c.Latitude);
        polyMaxLat = polygonCoordinates.Max(c => c.Latitude);
        polyMinLon = polygonCoordinates.Min(c => c.Longitude);
        polyMaxLon = polygonCoordinates.Max(c => c.Longitude);
        // Correct for longitude distance scaling based on latitude
        lonCorrection = Math.Cos((minLat + maxLat) / 2.0 * Math.PI / 180.0);

        scale = GetPolygonScale();
        Console.WriteLine($"Polygon scale: {scale:F2}");

        float scaleToFit = GetScaleToFit(polygonCoordinates, out float polyCenterX, out float polyCenterY); // MODIFIED
        Console.WriteLine($"Scale to fit: {scaleToFit:F2}");

        GetExpandedMapSize(out int width, out int height);

        using SKBitmap bitmap = new(width, height);
        using SKCanvas canvas = new(bitmap);

        Dictionary<long, (double lat, double lon, string? name)> nodes = [];
        List<(List<long> nodeIds, string highway, string? name)> roads = [];
        List<(List<long> nodeIds, string type, string? name)> waterBodies = [];
        List<(List<long> nodeIds, string type, string? name, float width)> waterways = [];
        List<(List<long> nodeIds, string type, string? name)> buildings = [];
        Dictionary<long, (double lat, double lon, string name, string type)> places = [];

        ParseOsmData(osmData, nodes, roads, waterBodies, waterways, buildings, places);

        DrawWaterBodies(canvas, nodes, waterBodies);

        DrawWaterways(canvas, nodes, waterways);

        DrawRoads(canvas, nodes, roads);

        DrawBuildings(canvas, nodes, buildings);

        DrawToponyms(canvas, places);

        using SKBitmap finalBitmap = new(targetWidth, targetHeight);
        using SKCanvas finalCanvas = new(finalBitmap);
        DrawFinalCanvas(polygonCoordinates, scaleToFit, polyCenterX, polyCenterY, bitmap, finalBitmap, finalCanvas);

        DrawPolygonOutline(scaleToFit, polygonCoordinates, finalCanvas);

        // Convert final bitmap to PNG
        using SKData finalData = finalBitmap.Encode(SKEncodedImageFormat.Png, 100);
        return finalData.ToArray();
    }

    private static void DrawFinalCanvas(CoordinateCollection polygonCoordinates, float scaleToFit, float polyCenterX, float polyCenterY, SKBitmap bitmap, SKBitmap finalBitmap, SKCanvas finalCanvas)
    {
        // Fill with background color
        finalBitmap.Erase(SKColor.Parse(appSettings.BackgroundColor));

        // Calculate final canvas transformations
        float finalCenterX = targetWidth / 2f;
        float finalCenterY = targetHeight / 2f;

        // Calculate optimal rotation angle for the polygon
        double rotationAngle = KmlHelper.GetOptimalRotationAngle(polygonCoordinates);
        Console.WriteLine($"Polygon rotation angle: {rotationAngle:F2}°");

        // Store current transformation state for polygon outline
        finalCanvas.Save();

        // Apply transformations in order: translate to center, rotate, scale, translate to position polygon
        finalCanvas.Translate(finalCenterX, finalCenterY);
        finalCanvas.RotateDegrees((float)rotationAngle);
        finalCanvas.Scale(scaleToFit);
        finalCanvas.Translate(-polyCenterX, -polyCenterY);        // Draw the original bitmap onto the transformed canvas
        finalCanvas.DrawBitmap(bitmap, 0, 0);

        // Restore the canvas state to draw polygon outline
        finalCanvas.Restore();

        // Apply desaturation and brightness reduction to pixels outside the polygon
        ApplyPolygonMask(polygonCoordinates, scaleToFit, polyCenterX, polyCenterY, finalBitmap, rotationAngle);
    }

    private static void GetExpandedMapSize(out int width, out int height)
    {
        double latDiff = maxLat - minLat;
        double correctedLonDiff = (maxLon - minLon) * lonCorrection;

        width = (int)Math.Ceiling(correctedLonDiff * scale);
        height = (int)Math.Ceiling(latDiff * scale);
        Console.WriteLine($"Map dimensions: {width}x{height}");
    }

    private static float GetScaleToFit(CoordinateCollection polygonCoordinates, out float polyCenterX, out float polyCenterY)
    {
        // Calculate unrotated polygon's bounding box in pixel coordinates of the source image
        float unrotatedPolyPixelMinX = (float)((polyMinLon - minLon) * lonCorrection * scale);
        float unrotatedPolyPixelMaxX = (float)((polyMaxLon - minLon) * lonCorrection * scale);
        float unrotatedPolyPixelMinY = (float)((maxLat - polyMaxLat) * scale);
        float unrotatedPolyPixelMaxY = (float)((maxLat - polyMinLat) * scale);

        // Calculate polygon center in pixel coordinates (of the source image)
        // This center is used as the pivot for transformations on the finalCanvas
        polyCenterX = (unrotatedPolyPixelMinX + unrotatedPolyPixelMaxX) / 2f;
        polyCenterY = (unrotatedPolyPixelMinY + unrotatedPolyPixelMaxY) / 2f;

        // Get the optimal rotation angle for the polygon
        double rotationAngleDegrees = KmlHelper.GetOptimalRotationAngle(polygonCoordinates);
        double rotationAngleRadians = rotationAngleDegrees * Math.PI / 180.0;

        List<SKPoint> rotatedPoints = [];
        if (polygonCoordinates != null && polygonCoordinates.Count != 0)
        {
            foreach (var coord in polygonCoordinates)
            {
                // Convert geo coordinate to pixel coordinate in the source image
                float x = (float)((coord.Longitude - minLon) * lonCorrection * scale);
                float y = (float)((maxLat - coord.Latitude) * scale);

                // Translate point so that polyCenterX, polyCenterY is the origin
                float translatedX = x - polyCenterX;
                float translatedY = y - polyCenterY;

                // Rotate point
                float rotatedX = (float)(translatedX * Math.Cos(rotationAngleRadians) - translatedY * Math.Sin(rotationAngleRadians));
                float rotatedY = (float)(translatedX * Math.Sin(rotationAngleRadians) + translatedY * Math.Cos(rotationAngleRadians));

                rotatedPoints.Add(new SKPoint(rotatedX, rotatedY));
            }
        }

        float rotatedBoundingBoxWidth = 0;
        float rotatedBoundingBoxHeight = 0;

        if (rotatedPoints.Count != 0)
        {
            float minRotatedX = rotatedPoints.Min(p => p.X);
            float maxRotatedX = rotatedPoints.Max(p => p.X);
            float minRotatedY = rotatedPoints.Min(p => p.Y);
            float maxRotatedY = rotatedPoints.Max(p => p.Y);

            rotatedBoundingBoxWidth = maxRotatedX - minRotatedX;
            rotatedBoundingBoxHeight = maxRotatedY - minRotatedY;
        }

        // If rotated points are not available, or result in zero area, fallback to unrotated dimensions
        if (rotatedBoundingBoxWidth <= 0 || rotatedBoundingBoxHeight <= 0)
        {
            float unrotatedPolyPixelWidth = unrotatedPolyPixelMaxX - unrotatedPolyPixelMinX;
            float unrotatedPolyPixelHeight = unrotatedPolyPixelMaxY - unrotatedPolyPixelMinY;

            if (unrotatedPolyPixelWidth <= 0 || unrotatedPolyPixelHeight <= 0)
            {
                // Polygon is a point or line, or some other error; avoid division by zero
                Console.WriteLine("Warning: Polygon has zero or negative unrotated dimensions. Defaulting scaleToFit to 1.0.");
                return 1.0f;
            }

            float effectiveTargetWidthFallback = targetWidth * (1f - (float)margin);
            float effectiveTargetHeightFallback = targetHeight * (1f - (float)margin);
            Console.WriteLine("Warning: Rotated polygon dimensions are zero or invalid. Falling back to unrotated dimensions for scaling.");
            return Math.Min(effectiveTargetWidthFallback / unrotatedPolyPixelWidth, effectiveTargetHeightFallback / unrotatedPolyPixelHeight);
        }

        // Proceed with using rotatedBoundingBoxWidth and rotatedBoundingBoxHeight
        float effectiveTargetWidth = targetWidth * (1f - (float)margin);
        float effectiveTargetHeight = targetHeight * (1f - (float)margin);

        return Math.Min(effectiveTargetWidth / rotatedBoundingBoxWidth, effectiveTargetHeight / rotatedBoundingBoxHeight);
    }

    private static double GetPolygonScale()
    {
        double polyLatDiff = polyMaxLat - polyMinLat;
        double polyCorrectedLonDiff = (polyMaxLon - polyMinLon) * lonCorrection;

        // Calculate effective area for polygon fitting
        // margin = 0.0 means polygon fills entire canvas
        // margin = 0.1 means polygon fills 90% of canvas (10% margin)
        int effectiveWidth = (int)(targetWidth * (1 - margin));
        int effectiveHeight = (int)(targetHeight * (1 - margin));

        // Calculate scale to fit polygon in effective area
        double polyScaleX = effectiveWidth / polyCorrectedLonDiff;
        double polyScaleY = effectiveHeight / polyLatDiff;
        return Math.Min(polyScaleX, polyScaleY);
    }
    static void DrawPolygonOutline(float scaleToFit, CoordinateCollection polygonCoordinates, SKCanvas finalCanvas)
    {
        float outlineWidth = appSettings.BorderOffset;
        float offsetDistance = outlineWidth / 6.0f;

        using SKPaint polygonPaint = new()
        {
            Color = SKColors.Red,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = outlineWidth,
            IsAntialias = true
        };

        // Calculate final canvas transformations to match the map bitmap
        float finalCenterX = targetWidth / 2f;
        float finalCenterY = targetHeight / 2f;
        double rotationAngle = KmlHelper.GetOptimalRotationAngle(polygonCoordinates);

        // Get polygon center in map coordinates
        float polyPixelMinX = (float)((polyMinLon - minLon) * lonCorrection * scale);
        float polyPixelMaxX = (float)((polyMaxLon - minLon) * lonCorrection * scale);
        float polyPixelMinY = (float)((maxLat - polyMaxLat) * scale);
        float polyPixelMaxY = (float)((maxLat - polyMinLat) * scale);
        float polyCenterX = (polyPixelMinX + polyPixelMaxX) / 2f;
        float polyCenterY = (polyPixelMinY + polyPixelMaxY) / 2f;

        // Apply the same transformations that were applied to the map bitmap
        finalCanvas.Save();
        finalCanvas.Translate(finalCenterX, finalCenterY);
        finalCanvas.RotateDegrees((float)rotationAngle);
        finalCanvas.Scale(scaleToFit);
        finalCanvas.Translate(-polyCenterX, -polyCenterY);

        // Convert polygon coordinates to points in map coordinate system
        List<SKPoint> points = [];
        foreach (var coord in polygonCoordinates)
        {
            float x = (float)((coord.Longitude - minLon) * lonCorrection * scale);
            float y = (float)((maxLat - coord.Latitude) * scale);
            points.Add(new SKPoint(x, y));
        }

        // Create offset polygon by moving each edge outward
        List<SKPoint> offsetPoints = CreateOffsetPolygon(points, offsetDistance);

        // Create path from offset points
        using SKPath offsetPath = new();
        if (offsetPoints.Count > 0)
        {
            offsetPath.MoveTo(offsetPoints[0]);
            for (int i = 1; i < offsetPoints.Count; i++)
            {
                offsetPath.LineTo(offsetPoints[i]);
            }
            offsetPath.Close();
        }

        // Draw the offset outline
        finalCanvas.DrawPath(offsetPath, polygonPaint);

        // Restore canvas state
        finalCanvas.Restore();
    }

    private static List<SKPoint> CreateOffsetPolygon(List<SKPoint> points, float offset)
    {
        if (points.Count < 3)
            return points;

        List<SKPoint> offsetPoints = [];
        int n = points.Count;

        for (int i = 0; i < n; i++)
        {
            // Get three consecutive points
            SKPoint prev = points[(i - 1 + n) % n];
            SKPoint curr = points[i];
            SKPoint next = points[(i + 1) % n];

            // Calculate edge vectors
            SKPoint edge1 = new(curr.X - prev.X, curr.Y - prev.Y);
            SKPoint edge2 = new(next.X - curr.X, next.Y - curr.Y);            // Calculate perpendicular vectors (normals) pointing outward
            SKPoint normal1 = new(edge1.Y, -edge1.X);
            SKPoint normal2 = new(edge2.Y, -edge2.X);

            // Normalize the normals
            float len1 = (float)Math.Sqrt(normal1.X * normal1.X + normal1.Y * normal1.Y);
            float len2 = (float)Math.Sqrt(normal2.X * normal2.X + normal2.Y * normal2.Y);

            if (len1 > 0)
            {
                normal1.X /= len1;
                normal1.Y /= len1;
            }

            if (len2 > 0)
            {
                normal2.X /= len2;
                normal2.Y /= len2;
            }

            // Calculate the average normal (bisector)
            SKPoint bisector = new(
                (normal1.X + normal2.X) / 2,
                (normal1.Y + normal2.Y) / 2
            );

            // Normalize the bisector
            float bisectorLen = (float)Math.Sqrt(bisector.X * bisector.X + bisector.Y * bisector.Y);
            if (bisectorLen > 0)
            {
                bisector.X /= bisectorLen;
                bisector.Y /= bisectorLen;
            }

            // Calculate the angle between the two edges
            float dot = normal1.X * normal2.X + normal1.Y * normal2.Y;
            float angle = (float)Math.Acos(Math.Clamp(dot, -1.0, 1.0));

            // Calculate the distance to move along the bisector
            float bisectorDistance = offset / (float)Math.Sin(angle / 2);

            // Limit the distance to prevent extremely long spikes on sharp angles
            bisectorDistance = Math.Min(bisectorDistance, offset * 5);

            // Calculate the offset point
            SKPoint offsetPoint = new(
                curr.X + bisector.X * bisectorDistance,
                curr.Y + bisector.Y * bisectorDistance
            );

            offsetPoints.Add(offsetPoint);
        }
        return offsetPoints;
    }
    private static void ApplyPolygonMask(CoordinateCollection polygonCoordinates, float scaleToFit, float polyCenterX, float polyCenterY, SKBitmap finalBitmap, double rotationAngle)
    {
        // Convert SKBitmap to SixLabors Image for faster processing
        byte[] imageBytes;
        using (var data = finalBitmap.Encode(SKEncodedImageFormat.Png, 100))
        {
            imageBytes = data.ToArray();
        }

        using var image = SixLabors.ImageSharp.Image.Load<Rgba32>(imageBytes);

        // Calculate final canvas transformations
        float finalCenterX = targetWidth / 2f;
        float finalCenterY = targetHeight / 2f;        // Convert polygon coordinates to final canvas coordinates
        List<SixLabors.ImageSharp.PointF> transformedPoints = [];

        foreach (var coord in polygonCoordinates)
        {
            // Convert geo coordinate to pixel coordinate in the source image
            float x = (float)((coord.Longitude - minLon) * lonCorrection * scale);
            float y = (float)((maxLat - coord.Latitude) * scale);

            // Apply the same transformations that were applied to the map bitmap
            // Translate to polygon center
            float translatedX = x - polyCenterX;
            float translatedY = y - polyCenterY;

            // Scale
            float scaledX = translatedX * scaleToFit;
            float scaledY = translatedY * scaleToFit;

            // Rotate around origin
            double rotationRadians = rotationAngle * Math.PI / 180.0;
            float rotatedX = (float)(scaledX * Math.Cos(rotationRadians) - scaledY * Math.Sin(rotationRadians));
            float rotatedY = (float)(scaledX * Math.Sin(rotationRadians) + scaledY * Math.Cos(rotationRadians));

            // Translate to final position
            float finalX = rotatedX + finalCenterX;
            float finalY = rotatedY + finalCenterY;

            transformedPoints.Add(new SixLabors.ImageSharp.PointF(finalX, finalY));
        }

        // Create a mask image - white inside polygon, black outside
        using var mask = new SixLabors.ImageSharp.Image<L8>(image.Width, image.Height);
        mask.Mutate(ctx => ctx.Clear(SixLabors.ImageSharp.Color.Black));

        if (transformedPoints.Count > 0)
        {
            mask.Mutate(ctx =>
            {
                var polygon = new SixLabors.ImageSharp.Drawing.Polygon(transformedPoints.ToArray());
                ctx.Fill(SixLabors.ImageSharp.Color.White, polygon);
            });
        }

        // Apply effects: desaturate and reduce brightness for areas outside the polygon
        // First create a desaturated/dimmed version of the entire image
        using var processedImage = image.Clone();
        processedImage.Mutate(ctx =>
        {
            ctx.Grayscale().Brightness(0.8f); // Convert to grayscale and reduce brightness
        });

        // Now blend based on the mask: use original inside polygon, processed outside
        image.Mutate(ctx =>
        {
            // Process pixel by pixel using the mask
            for (int y = 0; y < image.Height; y++)
            {
                for (int x = 0; x < image.Width; x++)
                {
                    var maskPixel = mask[x, y];
                    if (maskPixel.PackedValue == 0) // Black = outside polygon
                    {
                        image[x, y] = processedImage[x, y];
                    }
                    // For white pixels (inside polygon), keep original
                }
            }
        });

        // Convert back to SKBitmap
        using var outputStream = new MemoryStream();
        image.SaveAsPng(outputStream);
        outputStream.Position = 0;

        using var newBitmap = SKBitmap.Decode(outputStream.ToArray());

        // Copy the processed pixels back to the original bitmap
        using var canvas = new SKCanvas(finalBitmap);
        canvas.Clear();
        canvas.DrawBitmap(newBitmap, 0, 0);
    }

    private static void DrawToponyms(SKCanvas canvas, Dictionary<long, (double lat, double lon, string name, string type)> places)
    {
        // Place importance ordering
        string[] placeImportance = ["city", "town", "suburb", "village", "hamlet", "neighbourhood", "isolated_dwelling"];
        Dictionary<string, int> placeOrdering = placeImportance.Select((p, i) => (p, i)).ToDictionary(t => t.p, t => t.i);

        foreach ((double lat, double lon, string name, string type) in places.Values.OrderBy(p => placeOrdering.TryGetValue(p.type, out int order) ? order : 999))
        {
            SKPoint point = GeoToPixel(lon, lat, maxLat, minLon, lonCorrection, scale);
            float fontSize = 16.0f;

            // Adjust font size based on place importance
            fontSize = type switch
            {
                "city" => 24.0f,
                "town" => 20.0f,
                "suburb" => 16.0f,
                "village" => 14.0f,
                _ => 12.0f,
            };

            // Create font and paint
            SKFont labelFont = new(SKTypeface.Default, fontSize);
            SKPaint labelPaint = new()
            {
                Color = SKColor.Parse(appSettings.LabelStyle.Color),
                IsAntialias = true
            };

            // Measure text for centering
            SKRect textBounds = SKRect.Empty;
            labelFont.MeasureText(name, out textBounds, labelPaint);

            // Draw halo
            using (SKPaint haloPaint = new()
            {
                Color = SKColors.White,
                IsAntialias = true,
                IsStroke = true,
                StrokeWidth = 3.0f
            })
            {
                canvas.DrawText(name, point.X - (textBounds.Width / 2), point.Y, SKTextAlign.Left, labelFont, haloPaint);
            }

            // Draw text
            canvas.DrawText(name, point.X - (textBounds.Width / 2), point.Y, SKTextAlign.Left, labelFont, labelPaint);
        }
    }

    private static void DrawBuildings(SKCanvas canvas, Dictionary<long, (double lat, double lon, string? name)> nodes, List<(List<long> nodeIds, string type, string? name)> buildings)
    {
        SKPaint buildingFillPaint = new()
        {
            Color = SKColor.Parse(appSettings.BuildingStyle.Color),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        SKPaint buildingOutlinePaint = new()
        {
            Color = SKColor.Parse(appSettings.BuildingStyle.OutlineColor),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.0f,
            IsAntialias = true
        };

        foreach ((List<long> nodeIds, string buildingType, string? name) in buildings)
        {
            if (nodeIds.Count < 3) continue; // Need at least 3 points for a polygon

            SKPath buildingPath = new();
            bool firstPoint = true;
            foreach (long nodeId in nodeIds)
            {
                if (!nodes.TryGetValue(nodeId, out (double lat, double lon, string? name) coord)) continue;
                SKPoint point = GeoToPixel(coord.lon, coord.lat, maxLat, minLon, lonCorrection, scale);

                if (firstPoint) { buildingPath.MoveTo(point); firstPoint = false; }
                else { buildingPath.LineTo(point); }
            }
            buildingPath.Close();

            // Only draw if we have a valid path
            if (!buildingPath.IsEmpty)
            {
                canvas.DrawPath(buildingPath, buildingFillPaint);
                canvas.DrawPath(buildingPath, buildingOutlinePaint);
            }
        }
    }

    private static void DrawRoads(SKCanvas canvas, Dictionary<long, (double lat, double lon, string? name)> nodes, List<(List<long> nodeIds, string highway, string? name)> roads)
    {
        string[] roadPriority = ["service", "residential", "tertiary", "secondary", "primary", "trunk", "motorway"];
        Dictionary<string, int> roadTypeOrder = roadPriority
            .Select((type, idx) => new { type, idx })
            .ToDictionary(x => x.type, x => x.idx);
        List<(List<long> nodeIds, string highway, string? name)> sortedRoads = [.. roads.OrderBy(r => roadTypeOrder.TryGetValue(r.highway, out int order) ? order : -1)];

        // Cache for road paint objects
        Dictionary<string, (SKPaint? outline, SKPaint fill)> roadPaintCache = [];

        foreach ((List<long> nodeIds, string highway, string? name) in sortedRoads)
        {
            RoadStyle style = appSettings.RoadStyles.TryGetValue(highway, out RoadStyle? value) ? value : appSettings.RoadStyles["default"];

            SKPaint? outlinePaint = null;
            SKPaint fillPaint;

            // Construct style key for paint caching
            string styleKey = $"hw:{highway}_oc:{style.OutlineColor}_ow:{style.OutlineWidth}_fc:{style.Color}_fw:{style.Width}";

            if (roadPaintCache.TryGetValue(styleKey, out (SKPaint? outline, SKPaint fill) paints))
            {
                outlinePaint = paints.outline;
                fillPaint = paints.fill;
            }
            else
            {
                if (!string.IsNullOrEmpty(style.OutlineColor) && style.OutlineWidth > 0)
                {
                    outlinePaint = new SKPaint
                    {
                        Color = SKColor.Parse(style.OutlineColor),
                        StrokeWidth = style.OutlineWidth,
                        IsAntialias = true,
                        Style = SKPaintStyle.Stroke,
                        StrokeCap = SKStrokeCap.Round,
                        StrokeJoin = SKStrokeJoin.Round
                    };
                }

                fillPaint = new SKPaint
                {
                    Color = SKColor.Parse(style.Color),
                    StrokeWidth = style.Width,
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke,
                    StrokeCap = SKStrokeCap.Round,
                    StrokeJoin = SKStrokeJoin.Round
                };
                roadPaintCache[styleKey] = (outlinePaint, fillPaint);
            }

            SKPath roadPath = new();
            bool firstNode = true;
            foreach (long nodeId in nodeIds)
            {
                if (!nodes.TryGetValue(nodeId, out (double lat, double lon, string? name) nodeCoord)) continue;
                SKPoint point = GeoToPixel(nodeCoord.lon, nodeCoord.lat, maxLat, minLon, lonCorrection, scale);

                if (firstNode) { roadPath.MoveTo(point); firstNode = false; }
                else { roadPath.LineTo(point); }
            }

            if (!roadPath.IsEmpty)
            {
                // Draw outline first if applicable and wider
                if (outlinePaint != null && style.OutlineWidth > style.Width)
                {
                    canvas.DrawPath(roadPath, outlinePaint);
                }
                // Draw the road fill
                canvas.DrawPath(roadPath, fillPaint);
            }
        }
    }

    private static void DrawWaterways(SKCanvas canvas, Dictionary<long, (double lat, double lon, string? name)> nodes, List<(List<long> nodeIds, string type, string? name, float width)> waterways)
    {
        SKPaint waterwayPaint = new()
        {
            Color = SKColor.Parse(appSettings.WaterStyle.Color).WithAlpha(230),
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };

        foreach ((List<long> nodeIds, string type, string? name, float waterwayWidth) in waterways)
        {
            waterwayPaint.StrokeWidth = waterwayWidth;

            List<long> ids = nodeIds;
            if (nodeIds.Count > 2 && nodeIds.First() == nodeIds.Last())
                ids = [.. nodeIds.Take(nodeIds.Count - 1)];

            for (int i = 0; i < ids.Count - 1; i++)
            {
                if (!nodes.TryGetValue(ids[i], out (double lat, double lon, string? name) coordA) || !nodes.TryGetValue(ids[i + 1], out (double lat, double lon, string? name) coordB))
                    continue;

                SKPoint pointA = GeoToPixel(coordA.lon, coordA.lat, maxLat, minLon, lonCorrection, scale);
                SKPoint pointB = GeoToPixel(coordB.lon, coordB.lat, maxLat, minLon, lonCorrection, scale);

                using SKPath segPath = new();
                segPath.MoveTo(pointA);
                segPath.LineTo(pointB);
                canvas.DrawPath(segPath, waterwayPaint);
            }
        }
    }

    private static void DrawWaterBodies(SKCanvas canvas, Dictionary<long, (double lat, double lon, string? name)> nodes, List<(List<long> nodeIds, string type, string? name)> waterBodies)
    {
        List<(string name, SKPoint center, float area)> waterLabels = [];

        // Draw water bodies (polygons) with fill style
        SKPaint waterFillPaint = new()
        {
            Color = SKColor.Parse(appSettings.WaterStyle.Color),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        foreach ((List<long> nodeIds, string type, string? name) in waterBodies)
        {
            SKPath path = new();
            bool firstWater = true;
            foreach (long nodeId in nodeIds)
            {
                if (!nodes.TryGetValue(nodeId, out (double lat, double lon, string? name) coord))
                    continue;
                SKPoint point = GeoToPixel(coord.lon, coord.lat, maxLat, minLon, lonCorrection, scale);
                if (firstWater)
                {
                    path.MoveTo(point);
                    firstWater = false;
                }
                else
                {
                    path.LineTo(point);
                }
            }
            path.Close();

            if (path.IsEmpty)
                continue;

            canvas.DrawPath(path, waterFillPaint);

            // Store water body label information if it has a name
            if (string.IsNullOrEmpty(name))
                continue;

            path.GetBounds(out SKRect bounds);
            float centerX = bounds.MidX;
            float centerY = bounds.MidY;
            float area = bounds.Width * bounds.Height;

            if (area > 500.0f)
            {
                // Try to find a better center point for larger water bodies
                if (area > 5000.0f)
                {
                    // Sample grid points to find good label placement
                    int gridSize = 5;
                    float bestDistance = float.MaxValue;
                    float bestX = centerX, bestY = centerY;
                    bool foundBetterCenter = false;

                    for (int gx = 1; gx < gridSize; gx++)
                    {
                        for (int gy = 1; gy < gridSize; gy++)
                        {
                            float testX = bounds.Left + (bounds.Width * gx / gridSize);
                            float testY = bounds.Top + (bounds.Height * gy / gridSize);

                            if (path.Contains(testX, testY))
                            {
                                float distLeft = testX - bounds.Left;
                                float distRight = bounds.Right - testX;
                                float distTop = testY - bounds.Top;
                                float distBottom = bounds.Bottom - testY;
                                float minDist = Math.Min(Math.Min(distLeft, distRight), Math.Min(distTop, distBottom));

                                if (minDist > bestDistance)
                                {
                                    bestDistance = minDist;
                                    bestX = testX;
                                    bestY = testY;
                                    foundBetterCenter = true;
                                }
                            }
                        }
                    }

                    if (foundBetterCenter)
                    {
                        centerX = bestX;
                        centerY = bestY;
                    }
                }

                waterLabels.Add((name!, new SKPoint(centerX, centerY), area));
            }

        }
    }

    private static SKPoint GeoToPixel(double lon, double lat, double maxLat, double minLon, double lonCorrection, double scale)
    {
        float x = (float)((lon - minLon) * lonCorrection * scale);
        float y = (float)((maxLat - lat) * scale); // y axis: top = maxLat
        return new SKPoint(x, y);
    }

    private static void ParseOsmData(
        XmlOsmStreamSource osmData,
        Dictionary<long, (double lat, double lon, string? name)> nodes,
        List<(List<long> nodeIds, string highway, string? name)> roads,
        List<(List<long> nodeIds, string type, string? name)> waterBodies,
        List<(List<long> nodeIds, string type, string? name, float width)> waterways,
        List<(List<long> nodeIds, string type, string? name)> buildings,
        Dictionary<long, (double lat, double lon, string name, string type)> places)
    {
        foreach (OsmGeo? element in osmData)
        {
            if (element.Type == OsmGeoType.Node)
            {
                Node? node = (Node)element;
                string? name = node.Tags?.ContainsKey("name") == true ? node.Tags["name"] : null;

                // Collect place nodes
                if (node.Tags?.ContainsKey("place") == true && node.Tags.ContainsKey("name") && node.Id.HasValue
                    && node.Latitude.HasValue && node.Longitude.HasValue)
                {
                    string place = node.Tags["place"];
                    places.Add(node.Id.Value, (node.Latitude.Value, node.Longitude.Value, node.Tags["name"], place));
                }

                if (node is not null && node.Id is { } id && node.Latitude is { } lat && node.Longitude is { } lon)
                    nodes[id] = (lat, lon, name);
            }
            else if (element.Type == OsmGeoType.Way)
            {
                Way way = (Way)element;
                TagsCollectionBase tags = way.Tags ?? new TagsCollection();

                if (tags.ContainsKey("highway"))
                {
                    string highway = tags["highway"];
                    string? name = tags.ContainsKey("name") ? tags["name"] : null;
                    roads.Add((way.Nodes.ToList(), highway, name));
                }
                else if (tags.ContainsKey("waterway"))
                {
                    string waterway = tags["waterway"];
                    string? name = tags.ContainsKey("name") ? tags["name"] : null;
                    float waterwayWidth = 1.0f;

                    // Check for width tag
                    if (tags.ContainsKey("width"))
                    {
                        if (float.TryParse(tags["width"], out float parsedWidth))
                            waterwayWidth = parsedWidth;
                    }

                    // Adjust width based on waterway type
                    if (waterway == "river")
                        waterwayWidth = Math.Max(waterwayWidth, 4.0f);
                    else if (waterway == "stream")
                        waterwayWidth = Math.Max(waterwayWidth, 2.0f);
                    else if (waterway == "canal")
                        waterwayWidth = Math.Max(waterwayWidth, 3.0f);
                    else
                        waterwayWidth = Math.Max(waterwayWidth, 1.5f);

                    waterways.Add((way.Nodes.ToList(), waterway, name, waterwayWidth));
                }
                else if ((tags.ContainsKey("natural") && tags["natural"] == "water") ||
                            (tags.ContainsKey("landuse") && tags["landuse"] == "reservoir"))
                {
                    string type = tags.ContainsKey("natural") ? tags["natural"] : "reservoir";
                    string? name = tags.ContainsKey("name") ? tags["name"] : null;
                    waterBodies.Add((way.Nodes.ToList(), type, name));
                }
                // Add building recognition
                else if (tags.ContainsKey("building"))
                {
                    string buildingType = tags["building"];
                    string? name = tags.ContainsKey("name") ? tags["name"] : null;
                    buildings.Add((way.Nodes.ToList(), buildingType, name));
                }
            }
        }
    }

    /// <summary>
    /// Renders the OSM map strictly within the polygon's bounding box, tightly cropped, and returns the image and polygon pixel coordinates.
    /// </summary>
    //     public static async Task<byte[]> RenderBasicMapToPngCropped(CoordinateCollection coordinates)
    //     {
    //         Console.WriteLine("Startin OSM...");
    //         // Compute minimal bounding box of the polygon
    //         var minLon = coordinates.Min(c => c.Longitude);
    //         var maxLon = coordinates.Max(c => c.Longitude);
    //         var minLat = coordinates.Min(c => c.Latitude);
    //         var maxLat = coordinates.Max(c => c.Latitude);

    //         // Calculate the center latitude for scaling correction
    //         double centerLat = (minLat + maxLat) / 2.0;

    //         // Correct for longitude distance scaling based on latitude
    //         // At the equator, 1 degree longitude ≈ 1 degree latitude in distance
    //         // At higher latitudes, longitude degrees become shorter
    //         double lonCorrection = Math.Cos(centerLat * Math.PI / 180.0);

    //         // Image size: tightly fit the polygon bounding box with correction for longitude
    //         int targetMaxDim = 2000;
    //         double correctedLonDiff = (maxLon - minLon) * lonCorrection;
    //         double scaleX = targetMaxDim / correctedLonDiff;
    //         double scaleY = targetMaxDim / (maxLat - minLat);
    //         double scale = Math.Min(scaleX, scaleY);
    //         int width = (int)Math.Ceiling(correctedLonDiff * scale);
    //         int height = (int)Math.Ceiling((maxLat - minLat) * scale);

    //         // Increase canvas size for outline
    //         int outlinePad = 10;
    //         width += 2 * outlinePad;
    //         height += 2 * outlinePad;

    //         // Prepare OSM data
    //         var osmPath = await DownloadOsmForBoundingBoxAsync(coordinates, "cache");

    //         // Offload CPU-bound parsing, rendering, and file writing to a background thread
    //         return await Task.Run(() =>
    //         {
    //             using var fileStream = File.OpenRead(osmPath);
    //             var source = new XmlOsmStreamSource(fileStream);
    //             var nodes = new Dictionary<long, (double lat, double lon, string? name)>();
    //             var roads = new List<(List<long> nodeIds, string highway, string? name)>();
    //             var waterBodies = new List<(List<long> nodeIds, string type, string? name)>();
    //             var waterways = new List<(List<long> nodeIds, string type, string? name, float width)>();
    //             var buildings = new List<(List<long> nodeIds, string type, string? name)>(); // Add buildings collection
    //             var places = new Dictionary<long, (double lat, double lon, string name, string type)>(); // Add places collection
    //             var relationMembers = new Dictionary<long, List<(long id, string role)>>();
    //             var relations = new Dictionary<long, (long id, string type, Dictionary<string, string> tags)>();

    //             // First pass: collect all nodes, ways, and relation references
    //             foreach (var element in source)
    //             {
    //                 if (element.Type == OsmGeoType.Node)
    //                 {
    //                     var node = (Node)element;
    //                     string? name = node.Tags?.ContainsKey("name") == true ? node.Tags["name"] : null;

    //                     // Collect place nodes (cities, towns, villages, hamlets, suburbs, etc.)
    //                     if (node.Tags?.ContainsKey("place") == true && node.Tags.ContainsKey("name") && node.Id.HasValue
    //                         && node.Latitude.HasValue && node.Longitude.HasValue)
    //                     {
    //                         string place = node.Tags["place"];
    //                         places.Add(node.Id.Value, (node.Latitude.Value, node.Longitude.Value, node.Tags["name"], place));
    //                     }

    //                     if (node is not null && node.Id is { } id && node.Latitude is { } lat && node.Longitude is { } lon)
    //                         nodes[id] = (lat, lon, name);
    //                 }
    //                 else if (element.Type == OsmGeoType.Way)
    //                 {
    //                     var way = (Way)element;
    //                     var tags = way.Tags ?? new TagsCollection();
    //                     if (tags.ContainsKey("highway"))
    //                     {
    //                         string highway = tags["highway"];
    //                         string? name = tags.ContainsKey("name") ? tags["name"] : null;
    //                         roads.Add((way.Nodes.ToList(), highway, name));
    //                     }
    //                     else if (tags.ContainsKey("waterway"))
    //                     {
    //                         string waterway = tags["waterway"];
    //                         string? name = tags.ContainsKey("name") ? tags["name"] : null;
    //                         float waterwayWidth = 1.0f;

    //                         // Check for width tag (could be in meters or some other unit)
    //                         if (tags.ContainsKey("width"))
    //                         {
    //                             if (float.TryParse(tags["width"], out float parsedWidth))
    //                                 waterwayWidth = parsedWidth;
    //                         }

    //                         // Adjust width based on waterway type if not specified
    //                         if (waterway == "river")
    //                             waterwayWidth = Math.Max(waterwayWidth, 4.0f);
    //                         else if (waterway == "stream")
    //                             waterwayWidth = Math.Max(waterwayWidth, 2.0f);
    //                         else if (waterway == "canal")
    //                             waterwayWidth = Math.Max(waterwayWidth, 3.0f);
    //                         else
    //                             waterwayWidth = Math.Max(waterwayWidth, 1.5f);

    //                         waterways.Add((way.Nodes.ToList(), waterway, name, waterwayWidth));
    //                     }
    //                     else if ((tags.ContainsKey("natural") && tags["natural"] == "water") ||
    //                              (tags.ContainsKey("landuse") && tags["landuse"] == "reservoir"))
    //                     {
    //                         string type = tags.ContainsKey("natural") ? tags["natural"] : "reservoir";
    //                         string? name = tags.ContainsKey("name") ? tags["name"] : null;
    //                         waterBodies.Add((way.Nodes.ToList(), type, name));
    //                     }
    //                     // Add building recognition
    //                     else if (tags.ContainsKey("building"))
    //                     {
    //                         string buildingType = tags["building"];
    //                         string? name = tags.ContainsKey("name") ? tags["name"] : null;
    //                         buildings.Add((way.Nodes.ToList(), buildingType, name));
    //                     }
    //                 }
    //                 else if (element.Type == OsmGeoType.Relation)
    //                 {
    //                     var relation = (Relation)element;
    //                     if (relation.Id.HasValue && relation.Tags != null && relation.Members != null)
    //                     {
    //                         // Store relation metadata
    //                         var relId = relation.Id.Value;
    //                         var tagDict = relation.Tags.ToDictionary(t => t.Key, t => t.Value);

    //                         if (tagDict.TryGetValue("type", out string? relType) &&
    //                             (relType == "multipolygon" || relType == "waterway"))
    //                         {
    //                             relations[relId] = (relId, relType, tagDict);

    //                             // Store relation members
    //                             var memberList = new List<(long id, string role)>();
    //                             foreach (var member in relation.Members)
    //                             {
    //                                 if (member.Type == OsmGeoType.Way)
    //                                     memberList.Add((member.Id, member.Role ?? ""));
    //                             }
    //                             relationMembers[relId] = memberList;
    //                         }
    //                     }
    //                 }
    //             }

    //             // Only keep nodes within the bounding box
    //             var nodesInBox = nodes.Where(kv => kv.Value.lat >= minLat && kv.Value.lat <= maxLat && kv.Value.lon >= minLon && kv.Value.lon <= maxLon)
    //                                 .ToDictionary(kv => kv.Key, kv => kv.Value);

    //             // Keep only places within or near the bounding box (with some padding for context)
    //             double latPadding = (maxLat - minLat) * 0.1; // 10% padding
    //             double lonPadding = (maxLon - minLon) * 0.1;
    //             var filteredPlaces = places.Where(p =>
    //                 p.Value.lat >= minLat - latPadding && p.Value.lat <= maxLat + latPadding &&
    //                 p.Value.lon >= minLon - lonPadding && p.Value.lon <= maxLon + lonPadding)
    //                 .ToDictionary(kv => kv.Key, kv => kv.Value);

    //             // Pre-filter roads: only consider roads that have at least one node within the bounding box.
    //             var candidateRoads = roads.Where(r => r.nodeIds.Any(nodeId => nodesInBox.ContainsKey(nodeId))).ToList();

    //             // Pre-filter buildings: only consider buildings that have at least one node within the bounding box
    //             var candidateBuildings = buildings.Where(b => b.nodeIds.Any(nodeId => nodesInBox.ContainsKey(nodeId))).ToList();

    //             // Build the polygon path (with internal border offset for clarity)
    //             var polygonPath = new SKPath();
    //             var polygonPixels = new List<SKPoint>();
    //             double centroidLon = coordinates.Average(c => c.Longitude);
    //             double centroidLat = coordinates.Average(c => c.Latitude);
    //             bool first = true;
    //             foreach (var coord in coordinates)
    //             {
    //                 double dx = coord.Longitude - centroidLon;
    //                 double dy = coord.Latitude - centroidLat;
    //                 double length = Math.Sqrt(dx * dx + dy * dy);
    //                 double offsetLon = coord.Longitude;
    //                 double offsetLat = coord.Latitude;
    //                 if (length > 0)
    //                 {
    //                     double normDx = dx / length;
    //                     double normDy = dy / length;
    //                     double offsetLonUnits = (BorderOffset / scale) * normDx;
    //                     double offsetLatUnits = (BorderOffset / scale) * normDy;
    //                     offsetLon += offsetLonUnits;
    //                     offsetLat += offsetLatUnits;
    //                 }
    //                 float x = (float)(((offsetLon - minLon) * lonCorrection) * scale);
    //                 float y = (float)((maxLat - offsetLat) * scale); // y axis: top = maxLat
    //                 if (first) { polygonPath.MoveTo(x, y); first = false; }
    //                 else { polygonPath.LineTo(x, y); }
    //                 polygonPixels.Add(new SKPoint(x, y));
    //             }
    //             polygonPath.Close();

    //             // Create the bitmap
    //             using var bitmap = new SKBitmap(width, height);
    //             using var canvas = new SKCanvas(bitmap);
    //             bitmap.Erase(SKColors.Transparent);
    //             // Shift all drawing to the outline pad
    //             canvas.Translate(outlinePad, outlinePad);

    //             // Fill the polygon area with the desired background color from style config
    //             using (var fAppSAppSettingsSKPaint { Color = SKColor.Parse(appSettings.BackgroundColor), Style = SKPaintStyle.Fill, IsAntialias = true })
    //             {
    //             canvas.Save(); // Save before this specific clip and draw
    //             canvas.ClipPath(polygonPath, SKClipOperation.Intersect);
    //             AppSAppSettings(polygonPath, fillPaint);
    //             canvas.Restore(); // Restore after drawing background, so it's clipped, but main clip below is separate
    //         }
    //         AppSettings
    //         AppSettings

    //             // Save canvas state before applying the main clipping for AppSettingss
    //             canvas.Save(); AppSettings
    //             // Now clip the canvas for all subsequent map feature drawing (water, roads)
    //             canvas.ClipPath(polygonPath, SKClipOperation.Intersect);

    //         // --- Orphan detection: Build road connectivity graph and filter using only segments inside the polygon ---
    //         var nodeIdToPoint = nodesInBox.ToDictionary(kv => kv.Key,
    //             kv =>
    //             {
    //                 float x = (float)(((kv.Value.lon - minLon) * lonCorrection) * scale);
    //                 float y = (float)((maxLat - kv.Value.lat) * scale);
    //                 return new SKPoint(x, y);
    //             });
    //         // --- Improved road clipping: keep segments that cross or touch the polygon ---
    //         var clippedRoads = new List<(List<long> nodeIds, string highway, string? name)>();
    //         // Iterate over candidateRoads instead of all roads
    //         for (int i = 0; i < candidateRoads.Count; i++)
    //         {
    //             var (nodeIds, highway, name) = candidateRoads[i];
    //             var segmentNodeIds = new List<long>();
    //             for (int j = 0; j < nodeIds.Count - 1; j++)
    //             {
    //                 if (!nodeIdToPoint.TryGetValue(nodeIds[j], out var ptA) || !nodeIdToPoint.TryGetValue(nodeIds[j + 1], out var ptB))
    //                     continue;
    //                 bool aInside = polygonPath.Contains(ptA.X, ptA.Y);
    //                 bool bInside = polygonPath.Contains(ptB.X, ptB.Y);
    //                 bool crosses = false;
    //                 if (!aInside && !bInside)
    //                 {
    //                     // Check if the segment crosses the polygon boundary
    //                     if (TryIntersectSegmentWithPolygon((ptA.X, ptA.Y), (ptB.X, ptB.Y), polygonPath, out var ix, out var iy))
    //                         crosses = true;
    //                 }
    //                 if (aInside || bInside || crosses)
    //                 {
    //                     if (segmentNodeIds.Count == 0 || segmentNodeIds.Last() != nodeIds[j])
    //                         segmentNodeIds.Add(nodeIds[j]);
    //                     segmentNodeIds.Add(nodeIds[j + 1]);
    //                 }
    //             }
    //             if (segmentNodeIds.Count >= 2)
    //                 clippedRoads.Add((segmentNodeIds.Distinct().ToList(), highway, name));
    //         }
    //         var clippedNodeSet = new HashSet<long>(clippedRoads.SelectMany(r => r.nodeIds));
    //         var roadGraph = new RoadNetworkGraph(clippedRoads, clippedNodeSet);
    //         var connectedRoadIndices = roadGraph.GetMainNetworkComponent();

    //         // --- Border stub pruning: Remove leaf nodes at the border ---
    //         // 1. Identify border nodes (within 2 pixels of the polygon border)
    //         var borderNodeIds = new HashSet<long>();
    //         float borderThreshold = 2.0f;
    //         // Build polygon vertices from KML coordinates
    //         var polyPoints = new List<SKPoint>();
    //         foreach (var coord in coordinates)
    //         {
    //             float x = (float)((coord.Longitude - minLon) * scale);
    //             float y = (float)((maxLat - coord.Latitude) * scale);
    //             polyPoints.Add(new SKPoint(x, y));
    //         }
    //         foreach (var kv in nodeIdToPoint)
    //         {
    //             var pt = kv.Value;
    //             if (!polygonPath.Contains(pt.X, pt.Y)) continue;
    //             foreach (var polyPt in polyPoints)
    //             {
    //                 float dx = polyPt.X - pt.X;
    //                 float dy = polyPt.Y - pt.Y;
    //                 if (dx * dx + dy * dy < borderThreshold * borderThreshold)
    //                 {
    //                     borderNodeIds.Add(kv.Key);
    //                     break;
    //                 }
    //             }
    //         }
    //         // 2. Build node degree map for the main network
    //         var nodeDegree = new Dictionary<long, int>();
    //         foreach (var i in connectedRoadIndices)
    //         {
    //             var (nodeIds, _, _) = clippedRoads[i];
    //             foreach (var nodeId in nodeIds)
    //             {
    //                 if (!nodeDegree.ContainsKey(nodeId)) nodeDegree[nodeId] = 0;
    //                 nodeDegree[nodeId]++;
    //             }
    //         }
    //         // 3. Iteratively prune leaf nodes at the border
    //         var toRemove = new HashSet<int>();
    //         bool changed;
    //         do
    //         {
    //             changed = false;
    //             foreach (var i in connectedRoadIndices.Except(toRemove).ToList())
    //             {
    //                 var (nodeIds, _, _) = clippedRoads[i];
    //                 if (nodeIds.Count < 2) continue;
    //                 bool firstIsBorderLeaf = borderNodeIds.Contains(nodeIds.First()) && nodeDegree[nodeIds.First()] <= 1;
    //                 bool lastIsBorderLeaf = borderNodeIds.Contains(nodeIds.Last()) && nodeDegree[nodeIds.Last()] <= 1;
    //                 if ((firstIsBorderLeaf || lastIsBorderLeaf) && nodeIds.Count <= 3)
    //                 {
    //                     toRemove.Add(i);
    //                     foreach (var nodeId in nodeIds)
    //                     {
    //                         nodeDegree[nodeId] = Math.Max(0, nodeDegree[nodeId] - 1);
    //                     }
    //                     changed = true;
    //                 }
    //             }
    //         } while (changed);

    //         // 4. Update connectedRoadIndices to exclude pruned stubs
    //         connectedRoadIndices.ExceptWith(toRemove);

    //         // --- Improved road label placement: geometry-aware, straightest segment, minimal nudge, allow multiple for long roads ---
    //         var labelFont = new SKFont(SKTypeface.Default, appSettings.LabelStyle.FontSize);
    //         var labelPaint = new SKPaint { Color = SKColor.Parse(appSettings.LabelStyle.Color), IsAntialias = true, IsStroke = false };
    //         var placedLabelRects = new List<SKRect>();
    //         var labelPositionsByName = new Dictionary<string, List<SKPoint>>();
    //         float minLabelDistance = 800f; // Minimum distance in pixels between labels of the same name
    //         float minSegmentLength = 80f; // Lowered minimum seAppSettingsh for label (in pixels)
    //         int window = 2; // Reduced window size for more flexible AppSettings
    //         float insideFraction = 0.75f; // Allow segments where at least 75% of points are inside
    //         float labelSpacing = 1200f; // Minimum spacing between labels on the same road
    //         int maxNudgeAttempts = 12; // More nudge attempAppSettings
    //         float nudgeStep = 24f; // Larger nudge stepAppSettings

    //         // Create reusable paint objects for label backgrounds
    //         float labelCornerRadius = 4.0f; // Radius for rounded corners
    //         var labelBgPaint = new SKPaint
    //         {
    //             Color = SKColor.Parse(appSettings.BackgroundColor),
    //             Style = SKPaintStyle.Fill,
    //             IsAntialias = true
    //         };

    //         var labelBorderPaint = new SKPAppSettings
    //         {
    //             Color = SKColors.Gray.WithAlpha(150),
    //             Style = SKPaintStyle.Stroke,
    //             StrokeWidth = 1,
    //             AppSettings
    //                 IsAntialias = true
    //         };

    //         for (int i = 0; i < clippedRoads.Count; i++)
    //         {
    //             if (!connectedRoadIndices.Contains(i)) continue;
    //             var (nodeIds, highway, name) = clippedRoads[i];
    //             var roadStyle = appSettings.RoadStyles.TryGetValue(highway, out RoadStyle? value) ? value : appSettings.RoadStyles["default"];
    //             bool isWideRoad = highway == "motorway" || highway == "trunk" || highway == "primary" || highway == "secondary";

    //             if (highway == "service" || highway == "residential" || highway == "track" || highway == "footway" || highway == "path" || highway == "cycleway" || highway == "bridleway" || highway == "steps" || highway == "pedestrian")
    //                 continue; AppSettingsAppSettings
    //             string label = name ?? string.Empty;
    //             if (string.IsNullOrWhiteSpace(label))
    //             {
    //                 var way = source.FirstOrDefault(e => e.Type == OsmGeoType.Way && e is Way w && w.Nodes.SequenceEqual(nodeIds)) as Way;
    //                 if (way != nAppSettingsTags != null && way.Tags.ContainsKey("ref")) AppSettings
    //                     {
    //                     label = way.Tags["ref"];
    //                 }
    //                     else
    //                 {
    //                     continue;
    //                 }
    //             }
    //             // Hide roundabout names: skip labeling if the OSM way is a roundabout
    //             var wayForLabel = source.FirstOrDefault(e => e.Type == OsmGeoType.Way && e is Way w && w.Nodes.SequenceEqual(nodeIds)) as Way;
    //             if (wayForLabel != null && wayForLabel.Tags != null && wayForLabel.Tags.ContainsKey("junction") && wayForLabel.Tags["junction"] == "roundabout")
    //                 continue;
    //             // Find all candidate segments (window of N nodes) inside the polygon
    //             var candidates = new List<(int startIdx, float len, double angle, SKPoint midPt, List<SKPoint> segPts, int insideCount)>();
    //             for (int j = 0; j <= nodeIds.Count - window; j++)
    //             {
    //                 var segPts = new List<SKPoint>();
    //                 int insideCount = 0;
    //                 for (int k = 0; k < window; k++)
    //                 {
    //                     if (!nodesInBox.TryGetValue(nodeIds[j + k], out var coord)) break; float x = (float)(((coord.lon - minLon) * lonCorrection) * scale);
    //                     float y = (float)((maxLat - coord.lat) * scale);
    //                     segPts.Add(new SKPoint(x, y));
    //                     if (polygonPath.Contains(x, y)) insideCount++;
    //                 }
    //                 if (segPts.Count < window) continue;
    //                 float dx = segPts.Last().X - segPts.First().X;
    //                 float dy = segPts.Last().Y - segPts.First().Y;
    //                 float len = (float)Math.Sqrt(dx * dx + dy * dy);
    //                 if (len < minSegmentLength) continue;
    //                 if (insideCount < insideFraction * window) continue;
    //                 double angleRad = Math.Atan2(dy, dx);
    //                 float midX = (segPts.First().X + segPts.Last().X) / 2;
    //                 float midY = (segPts.First().Y + segPts.Last().Y) / 2;
    //                 candidates.Add((j, len, angleRad, new SKPoint(midX, midY), segPts, insideCount));
    //             }
    //             // If no candidates, fallback: use the longest available segment (even if not enough inside)
    //             if (candidates.Count == 0 && nodeIds.Count >= 2)
    //             {
    //                 float maxLen = 0; int bestIdx = 0;
    //                 for (int j = 0; j <= nodeIds.Count - 2; j++)
    //                 {
    //                     if (!nodesInBox.TryGetValue(nodeIds[j], out var c0) || !nodesInBox.TryGetValue(nodeIds[j + 1], out var c1)) continue;
    //                     float x0 = (float)(((c0.lon - minLon) * lonCorrection) * scale);
    //                     float y0 = (float)((maxLat - c0.lat) * scale);
    //                     float x1 = (float)(((c1.lon - minLon) * lonCorrection) * scale);
    //                     float y1 = (float)((maxLat - c1.lat) * scale);
    //                     float len = (float)Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
    //                     if (len > maxLen) { maxLen = len; bestIdx = j; }
    //                 }
    //                 if (maxLen > 0)
    //                 {
    //                     var segPts = new List<SKPoint>();
    //                     if (nodesInBox.TryGetValue(nodeIds[bestIdx], out var c0) && nodesInBox.TryGetValue(nodeIds[bestIdx + 1], out var c1))
    //                     {
    //                         float x0 = (float)(((c0.lon - minLon) * lonCorrection) * scale);
    //                         float y0 = (float)((maxLat - c0.lat) * scale);
    //                         float x1 = (float)(((c1.lon - minLon) * lonCorrection) * scale);
    //                         float y1 = (float)((maxLat - c1.lat) * scale);
    //                         segPts.Add(new SKPoint(x0, y0));
    //                         segPts.Add(new SKPoint(x1, y1));
    //                         double angleRad = Math.Atan2(y1 - y0, x1 - x0);
    //                         float midX = (x0 + x1) / 2;
    //                         float midY = (y0 + y1) / 2;
    //                         candidates.Add((bestIdx, maxLen, angleRad, new SKPoint(midX, midY), segPts, 0));
    //                     }
    //                 }
    //             }
    //             // Sort candidates by straightness (max length), then by how centered the segment is in the polygon
    //             var bestCandidates = candidates.OrderByDescending(c => c.len).ToList();
    //             var usedMidpoints = new List<SKPoint>();
    //             int maxLabels = Math.Max(1, (int)(nodeIds.Count * scale / labelSpacing));
    //             int labelsPlaced = 0;

    //             foreach (var cand in bestCandidates)
    //             {
    //                 if (labelsPlaced >= maxLabels) break;
    //                 // Don't place labels too close to each other on the same road feature
    //                 if (usedMidpoints.Any(pt => (pt.X - cand.midPt.X) * (pt.X - cand.midPt.X) + (pt.Y - cand.midPt.Y) * (pt.Y - cand.midPt.Y) < labelSpacing * labelSpacing))
    //                     continue;

    //                 float angleDeg = (float)(cand.angle * 180.0 / Math.PI);
    //                 if (angleDeg > 90) angleDeg -= 180;
    //                 if (angleDeg < -90) angleDeg += 180;

    //                 bool placed = false;
    //                 SKPoint labelAnchor = SKPoint.Empty;
    //                 SKRect currentAabb = SKRect.Empty;

    //                 if (isWideRoad)
    //                 {
    //                     // Only attempt on-road placement, skip nudging
    //                     float currentX = cand.midPt.X;
    //                     float currentY = cand.midPt.Y;

    //                     labelFont.MeasureText(label, out SKRect textBounds, labelPaint);
    //                     // Set font to bold and adjust height
    //                     labelFont.Typeface = SKTypeface.FromFamilyName(SKTypeface.Default.FamilyName, SKFontStyle.Bold);
    //                     labelFont.Size = appSettings.LabelStyle.FontSize * 1.2f; // Example: increase height by 20%
    //                                                                              // For wide roads, always center label if anchor is inside and no overlap
    //                     var labelRect = new SKRect(-textBounds.Width / 2, textBounds.Top, textBounds.Width / 2, textBounds.Bottom);
    //                     var corners = new[]
    //                     {AppSettings
    //                             RotateAndTranslate(labelRect.Left, labelRect.Top, cand.angle, currentX, currentY),
    //                             RotateAndTranslate(labelRect.Right, labelRect.Top, cand.angle, currentX, currentY),
    //                             RotateAndTranslate(labelRect.Left, labelRect.Bottom, cand.angle, currentX, currentY),
    //                             RotateAndTranslate(labelRect.Right, labelRect.Bottom, cand.angle, currentX, currentY)
    //                         }; AppSettings
    //                         var aabb = new SKRect(corners.Min(pt => pt.X), corners.Min(pt => pt.Y), corners.Max(pt => pt.X), corners.Max(pt => pt.Y));
    //                     bool overlaps = placedLabelRects.Any(r => r.IntersectsWith(aabb));
    //                     bool tooClose = false;
    //                     if (labelPositionsByName.TryGetValue(label, out var positions))
    //                     {
    //                         foreach (var pt in positions)
    //                         {
    //                             float dist = (float)Math.Sqrt((pt.X - currentX) * (pt.X - currentX) + (pt.Y - currentY) * (pt.Y - currentY));
    //                             if (dist < minLabelDistance) { tooClose = true; break; }
    //                         }
    //                     }
    //                     bool anchorInsidePolygon = polygonPath.Contains(currentX, currentY);
    //                     if (anchorInsidePolygon && !overlaps && !tooClose)
    //                     {
    //                         labelAnchor = new SKPoint(currentX, currentY);
    //                         currentAabb = aabb;
    //                         placed = true;
    //                     }
    //                 }
    //                 else
    //                 {
    //                     // Only use nudging for narrow roads
    //                     for (int attempt = 0; attempt < maxNudgeAttempts && !placed; attempt++)
    //                     {
    //                         float nudgeAmount = attempt * nudgeStep;

    //                         float currentX = cand.midPt.X + (float)Math.Cos(cand.angle + Math.PI / 2) * nudgeAmount;
    //                         float currentY = cand.midPt.Y + (float)Math.Sin(cand.angle + Math.PI / 2) * nudgeAmount;

    //                         labelFont.MeasureText(label, out SKRect textBounds, labelPaint);
    //                         labelFont.Typeface = SKTypeface.FromFamilyName(SKTypeface.Default.FamilyName, SKFontStyle.Normal);
    //                         labelFont.Size = appSettings.LabelStyle.FontSize;
    //                         var labelRect = new SKRect(-textBounds.Width / 2, textBounds.Top, textBounds.Width / 2, textBounds.Bottom);
    //                         var corners = new[]
    //                         {
    //                                 RotateAndTranAppSettingsRect.Left, labelRect.Top, cand.angle, currentX, currentY),
    //                                 RotateAndTranslate(labelRect.Right, labelRect.Top, cand.angle, currentX, currentY),
    //                                 RotateAndTranslate(labelRect.Left, labelRect.Bottom, cand.angle, currentX, currentY),
    //                                 RotateAndTranslate(labelRect.Right, labelRect.Bottom, cand.angle, currentX, currentY)
    //                             };
    //     bool allCornersInAppSettingsers.All(pt => polygonPath.Contains(pt.X, pt.Y));

    //     var aabb = new SKRect(corners.Min(pt => pt.X), corners.Min(pt => pt.Y), corners.Max(pt => pt.X), corners.Max(pt => pt.Y));
    //     bool overlaps = placedLabelRects.Any(r => r.IntersectsWith(aabb));
    //     bool tooClose = false;
    //                         if (labelPositionsByName.TryGetValue(label, out var positions))
    //                         {
    //                             foreach (var pt in positions)
    //                             {
    //                                 float dist = (float)Math.Sqrt((pt.X - currentX) * (pt.X - currentX) + (pt.Y - currentY) * (pt.Y - currentY));
    //                                 if (dist<minLabelDistance) { tooClose = true; break; }
    //                             }
    //                         }

    //                         bool anchorInsidePolygon = polygonPath.Contains(currentX, currentY);
    // if (anchorInsidePolygon && allCornersInside && !overlaps && !tooClose)
    // {
    //     labelAnchor = new SKPoint(currentX, currentY);
    //     currentAabb = aabb;
    //     placed = true;
    // }
    //                     }
    //                 }
    //                 if (placed)
    // {
    //     canvas.Save();
    //     canvas.Translate(labelAnchor.X, labelAnchor.Y);
    //     canvas.RotateDegrees(angleDeg);                        // Get text measurements for the background rectangle
    //     labelFont.MeasureText(label, out SKRect textBounds, labelPaint);

    //     // Create a slightly expanded rectangle for better legibility
    //     float padding = 8.0f; // Padding around the text
    //     var bgRect = new SKRect(
    //         textBounds.Left - padding,
    //         textBounds.Top - padding,
    //         textBounds.Right + padding,
    //         textBounds.Bottom + padding
    //     );

    //     // Draw background rounded rectangle using the map's background color
    //     canvas.DrawRoundRect(bgRect, labelCornerRadius, labelCornerRadius, labelBgPaint);
    //     canvas.DrawRoundRect(bgRect, labelCornerRadius, labelCornerRadius, labelBorderPaint);

    //     // Draw the label text
    //     canvas.DrawText(label, 0, 0, labelFont, labelPaint);
    //     canvas.Restore();

    //     placedLabelRects.Add(currentAabb);
    //     if (!labelPositionsByName.ContainsKey(label))
    //         labelPositionsByName[label] = new List<SKPoint>();
    //     labelPositionsByName[label].Add(labelAnchor);
    //     usedMidpoints.Add(cand.midPt); labelsPlaced++;
    // }
    //             }
    //         }

    //         // --- Draw water body labels ---
    //         // Sort water bodies by area (largest first) to prioritize larger water bodies
    //         var sortedWaterLabels = waterLabels.OrderByDescending(w => w.area).ToList();

    // var waterLabelFont = new SKFont
    // {
    //     Size = appSettings.WaterLabelStyle.FontSize,
    //     Typeface = SKTypeface.FromFamilyName(
    //         SKTypeface.Default.FamilyName,
    //         appSettings.WaterLabelStyle.FontStyle == "Bold" ? SKFontStyle.Bold :
    //         AppSettAppSettingsabelStyle.FontStyle == "Italic" ? SKFontStyle.Italic :
    //         SKFontStyle.Normal
    //     )
    // }; var waterAppSettings = new SKPaint
    //         {AppSettings
    //             Color = AppSettingsrse(appSettings.WaterLabelStyle.Color),
    //             IsAntialias = true
    //         };
    // AppSettings
    //             // CreatAppSettingAppSettingser readability (subtle outline around text)
    //             var waterHaloPaint = new SKPaint
    //             {
    //                 Color = SKColors.White.WithAlpha(230),
    //                 IsAntialias = true,
    //                 Style = SKPaintStyle.SAppSettings


    //                 StrokeWidth = 2.0f
    //             };

    // foreach (var (name, center, _) in sortedWaterLabels)
    // {
    //     // Skip if name is empty
    //     if (string.IsNullOrEmpty(name)) continue;

    //     // Measure the text to create a bounding box
    //     waterLabelFont.MeasureText(name, out SKRect textBounds, waterLabelPaint);

    //     // Create a slightly expanded rectangle for better legibility
    //     float padding = 8.0f;
    //     var bgRect = new SKRect(
    //         textBounds.Left - padding,
    //         textBounds.Top - padding,
    //         textBounds.Right + padding,
    //         textBounds.Bottom + padding
    //     );

    //     // Calculate the bounds in screen coordinates
    //     var aabb = new SKRect(
    //         center.X + bgRect.Left,
    //         center.Y + bgRect.Top,
    //         center.X + bgRect.Right,
    //         center.Y + bgRect.Bottom
    //     );

    //     // Check if the label overlaps with any existing label
    //     bool overlaps = placedLabelRects.Any(r => r.IntersectsWith(aabb));

    //     // Check if anchor is inside polygon
    //     bool anchorInsidePolygon = polygonPath.Contains(center.X, center.Y);
    //     if (anchorInsidePolygon && !overlaps)
    //     {
    //         canvas.Save();
    //         canvas.Translate(center.X, center.Y);                    // Draw text halo/outline first
    //         canvas.DrawText(name, 0, 0, SKTextAlign.Center, waterLabelFont, waterHaloPaint);
    //         // Draw the label text on top
    //         canvas.DrawText(name, 0, 0, SKTextAlign.Center, waterLabelFont, waterLabelPaint);
    //         canvas.Restore();

    //         placedLabelRects.Add(aabb);
    //         if (!labelPositionsByName.ContainsKey(name))
    //             labelPositionsByName[name] = new List<SKPoint>();
    //         labelPositionsByName[name].Add(center);
    //     }
    // }

    // // --- Draw place toponyms ---
    // // Create place name font based on style configuration
    // var placeLabelFont = new SKFont
    // {
    //     Size = appSettings.PlaceLabelStyle.FontSize,
    //     Typeface = SKTypeface.FromFamilyName(
    //         SKTypeface.Default.FamilyName,
    //         AppSettAppSettingsabelStyle.FontStyle == "Bold" ? SKFontStyle.Bold :
    //         appSettings.PlaceLabelStyle.FontStyle == "Italic" ? SKFontStyle.Italic :
    //         SKFontStyle.Normal)
    // }; AppSettings
    // var placeLabAppSettingsew SKPaint
    //         {
    //             Color = AppSettingsrse(appSettings.PlaceLabelStyle.Color),
    //     IsAntialias = true
    //         };
    // AppSettingsAppSettings
    //             // CreatAppSettingst for better readability (subtle outline around text)
    //             var placeHaloPaint = new SKPaint
    //             {
    //                 Color = SKColors.White.WithAlpha(230),
    //                 IsAntialias = true,
    //                 Style = SKPaintStyle.SAppSettings


    //                 StrokeWidth = 2.0f
    //             };            // Sort places by importance (city, town, village, hamlet, suburb, etc.)
    // string[] placeOrder = new[] { "city", "town", "village", "hamlet", "suburb", "neighbourhood", "locality" };
    // var placeTypeOrder = placeOrder
    //     .Select((type, idx) => new { type, idx })
    //     .ToDictionary(x => x.type, x => x.idx);

    // // Group places by name to detect duplicates and prioritize the most important instance
    // var placesByName = filteredPlaces.Values
    //     .GroupBy(p => p.name)
    //     .ToDictionary(
    //         g => g.Key,
    //         g => g.OrderBy(p => placeTypeOrder.TryGetValue(p.type, out int value) ? value : 999).First()
    //     );

    // // Sort places by importance (city, town, village, hamlet, suburb, etc.)
    // var sortedPlaces = placesByName.Values
    //     .OrderBy(p => placeTypeOrder.TryGetValue(p.type, out int value) ? value : 999)
    //     .ToList();

    // foreach (var (lat, lon, name, type) in sortedPlaces)
    // {
    //     // Convert coordinates to screen position
    //     float x = (float)(((lon - minLon) * lonCorrection) * scale);
    //     float y = (float)((maxLat - lat) * scale);

    //     // Skip if name is empty
    //     if (string.IsNullOrEmpty(name)) continue;                // Adjust font size and style based on place type importance
    //     float fontSizeMultiplier = 1.0f;
    //     SKFontStyleWeight fontWeight = SKFontStyleWeight.Normal;

    //     if (type == "city")
    //     {
    //         fontSizeMultiplier = 1.5f;
    //         fontWeight = SKFontStyleWeight.Bold;
    //     }
    //     else if (type == "town")
    //     {
    //         fontSizeMultiplier = 1.3f;
    //         fontWeight = SKFontStyleWeight.SemiBold;
    //     }
    //     else if (type == "village")
    //     {
    //         fontSizeMultiplier = 1.1f;
    //     }
    //     else if (type == "hamlet" || type == "suburb")
    //     {
    //         fontSizeMultiplier = 0.9f;
    //     }
    //     else
    //     {
    //         fontSizeMultiplier = 0.8f;
    //     }

    //     // Update font with new size and weight
    //     placeLabelFont = new SKFont
    //     {
    //         Size = appSettings.PlaceLabelStyle.FontSize * fontSizeMultiplier,
    //         Typeface = SKTypeface.FromFamilyName(
    //             SKTypefappSettings.FamilyName,
    //             new SKFontStyle(fontWeight, SKFontStyleWidth.Normal,
    //                 appSettings.PlaceLabelStyle.FontStyle == "Italic" ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright)
    //         )
    //     }; AppSettings

    //             // Measure the text to create a bounding box
    //             placeLabelFAppSettingsText(name, out SKRect textBounds, placeLabelPaint);

    //     // Create a slightly expanded rectangle for better legibility
    //     float padding = 8.0f;
    //     var bgRect = AppSettings(
    //         textBounds.Left - padding,
    //         textBounds.Top - padding,
    //         textBounds.Right + padding,
    //         textBounds.Bottom + padding
    //     );

    //     // Calculate the bounds in screen coordinates
    //     var aabb = new SKRect(
    //         x + bgRect.Left,
    //         y + bgRect.Top,
    //         x + bgRect.Right,
    //         y + bgRect.Bottom
    //     );

    //     // Check if the label overlaps with any existing label
    //     bool overlaps = placedLabelRects.Any(r => r.IntersectsWith(aabb));

    //     // Check if anchor is inside polygon
    //     bool anchorInsidePolygon = polygonPath.Contains(x, y); if (anchorInsidePolygon && !overlaps)
    //     {
    //         canvas.Save();
    //         canvas.Translate(x, y);                    // Draw text halo/outline first
    //         canvas.DrawText(name, 0, 0, SKTextAlign.Center, placeLabelFont, placeHaloPaint);
    //         // Draw the label text on top
    //         canvas.DrawText(name, 0, 0, SKTextAlign.Center, placeLabelFont, placeLabelPaint);
    //         canvas.Restore();

    //         placedLabelRects.Add(aabb);
    //         if (!labelPositionsByName.ContainsKey(name))
    //             labelPositionsByName[name] = new List<SKPoint>();
    //         labelPositionsByName[name].Add(new SKPoint(x, y));
    //     }
    // }

    // Console.WriteLine("Finished OSM");

    // return bitmap.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    //     });
    //     }

    // private static SKPoint RotateAndTranslate(float x, float y, double angleRad, float translateX, float translateY)
    // {
    //     // Rotate around the origin (0,0), then translate
    //     double cosTheta = Math.Cos(angleRad);
    //     double sinTheta = Math.Sin(angleRad);
    //     return new SKPoint(
    //         (float)(x * cosTheta - y * sinTheta + translateX),
    //         (float)(x * sinTheta + y * cosTheta + translateY));
    // }

    // private static async Task<string> DownloadOsmForBoundingBoxAsync(CoordinateCollection coordinates, string cacheFolder)
    // {
    //     var maxLon = coordinates.Max(c => c.Longitude);
    //     var minLon = coordinates.Min(c => c.Longitude);
    //     var maxLat = coordinates.Max(c => c.Latitude);
    //     var minLat = coordinates.Min(c => c.Latitude);
    //     return string.Empty;
    //     //return await OsmDownloader.DownloadOsmIfNeededAsync(minLat, minLon, maxLat, maxLon, cacheFolder);
    // }

    // // Helper: Find intersection of segment (p1 to p2) with polygon boundary
    // private static bool TryIntersectSegmentWithPolygon((float xA, float yA) inside, (float xB, float yB) outside, SKPath polygon, out float ix, out float iy)
    // {
    //     // Sample along the segment to find the crossing point (binary search)
    //     float t0 = 0, t1 = 1;
    //     for (int iter = 0; iter < 20; iter++)
    //     {
    //         float tm = (t0 + t1) / 2;
    //         float xm = inside.xA + (outside.xB - inside.xA) * tm;
    //         float ym = inside.yA + (outside.yB - inside.yA) * tm;
    //         if (polygon.Contains(xm, ym))
    //             t0 = tm;
    //         else
    //             t1 = tm;
    //     }
    //     ix = inside.xA + (outside.xB - inside.xA) * t0;
    //     iy = inside.yA + (outside.yB - inside.yA) * t0;
    //     return true;
    // }
}
