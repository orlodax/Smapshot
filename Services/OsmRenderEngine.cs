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

namespace Smapshot.Services;

internal class OsmRenderEngine(XmlOsmStreamSource? osmData, BoundingBoxGeo expandedBoundingBox, KmlService kmlHelper)
{
    const int targetWidth = 2500;
    const int targetHeight = 3250;
    const double margin = 0.1; // 10% margin around polygon (0.0 = no margin, 0.1 = 10% margin)

    static readonly AppSettings appSettings = AppSettings.Instance;

    double minLat;
    double maxLat;
    double minLon;
    double maxLon;
    double polyMinLat;
    double polyMaxLat;
    double polyMinLon;
    double polyMaxLon;
    double lonCorrection;
    double scale;
    double rotationAngle;
    CoordinateCollection polygonCoordinates = [];

    internal byte[] RenderOsmData()
    {
        ArgumentNullException.ThrowIfNull(osmData, nameof(osmData));

        polygonCoordinates = kmlHelper.Coordinates ?? throw new ArgumentException("KML coordinates are null or empty.", nameof(kmlHelper));
        rotationAngle = kmlHelper.GetOptimalRotationAngle();

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
        List<(List<long> nodeIds, string highway, string? name, string? roadRef)> roads = [];
        List<(List<long> nodeIds, string type, string? name)> waterBodies = [];
        List<(List<long> nodeIds, string type, string? name, float width)> waterways = [];
        List<(List<long> nodeIds, string type, string? name)> buildings = [];
        Dictionary<long, (double lat, double lon, string name, string type)> places = [];

        ParseOsmData(osmData, nodes, roads, waterBodies, waterways, buildings, places);

        DrawWaterBodies(canvas, nodes, waterBodies);

        DrawWaterways(canvas, nodes, waterways);

        DrawRoads(canvas, nodes, roads);

        DrawBuildings(canvas, nodes, buildings);

        using SKBitmap finalBitmap = new(targetWidth, targetHeight);
        using SKCanvas finalCanvas = new(finalBitmap);
        DrawFinalCanvas(scaleToFit, polyCenterX, polyCenterY, bitmap, finalBitmap, finalCanvas);

        DrawPolygonOutline(scaleToFit, finalCanvas);

        DrawWaterLabels(finalCanvas, nodes, waterBodies, scaleToFit, polyCenterX, polyCenterY);

        DrawRoadLabels(finalCanvas, nodes, roads, scaleToFit, polyCenterX, polyCenterY);

        DrawToponyms(finalCanvas, places, scaleToFit, polyCenterX, polyCenterY);

        // Convert final bitmap to PNG
        using SKData finalData = finalBitmap.Encode(SKEncodedImageFormat.Png, 100);
        return finalData.ToArray();
    }


    void DrawFinalCanvas(float scaleToFit, float polyCenterX, float polyCenterY, SKBitmap bitmap, SKBitmap finalBitmap, SKCanvas finalCanvas)
    {
        // Fill with background color
        finalBitmap.Erase(SKColor.Parse(appSettings.BackgroundColor));

        // Calculate final canvas transformations
        float finalCenterX = targetWidth / 2f;
        float finalCenterY = targetHeight / 2f;

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
        ApplyPolygonMask(scaleToFit, polyCenterX, polyCenterY, finalBitmap);
    }

    void ApplyPolygonMask(float scaleToFit, float polyCenterX, float polyCenterY, SKBitmap finalBitmap)
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

    void GetExpandedMapSize(out int width, out int height)
    {
        double latDiff = maxLat - minLat;
        double correctedLonDiff = (maxLon - minLon) * lonCorrection;

        width = (int)Math.Ceiling(correctedLonDiff * scale);
        height = (int)Math.Ceiling(latDiff * scale);
        Console.WriteLine($"Map dimensions: {width}x{height}");
    }

    float GetScaleToFit(CoordinateCollection polygonCoordinates, out float polyCenterX, out float polyCenterY)
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
        double rotationAngleRadians = rotationAngle * Math.PI / 180.0;

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

    double GetPolygonScale()
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

    void DrawPolygonOutline(float scaleToFit, SKCanvas finalCanvas)
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

    static List<SKPoint> CreateOffsetPolygon(List<SKPoint> points, float offset)
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

    void DrawRoadLabels(
        SKCanvas finalCanvas,
        Dictionary<long, (double lat, double lon, string? name)> nodes,
        List<(List<long> nodeIds, string highway, string? name, string? roadRef)> roads,
        float scaleToFit,
        float polyCenterX,
        float polyCenterY)
    {
        // Create font for road labels
        SKFont roadLabelFont = new()
        {
            Size = appSettings.LabelStyle.FontSize,
            Typeface = SKTypeface.FromFamilyName(SKTypeface.Default.FamilyName)
        };

        // Main paint for road label text
        SKPaint roadLabelPaint = new()
        {
            Color = SKColor.Parse(appSettings.LabelStyle.Color),
            IsAntialias = true
        };        // Create background paint with opacity for road labels
        SKPaint roadBgPaint = new()
        {
            Color = SKColor.Parse(appSettings.RoadLabelStyle.BackgroundColor).WithAlpha((byte)appSettings.RoadLabelStyle.BackgroundOpacity),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        // Create outline paint for road label backgrounds
        SKPaint roadBgOutlinePaint = new()
        {
            Color = SKColor.Parse(appSettings.RoadLabelStyle.OutlineColor),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = appSettings.RoadLabelStyle.OutlineWidth,
            IsAntialias = true
        };

        // Calculate final canvas transformations to match the map bitmap
        float finalCenterX = targetWidth / 2f;
        float finalCenterY = targetHeight / 2f;

        // Apply the same transformations that were applied to the map bitmap
        finalCanvas.Save();
        finalCanvas.Translate(finalCenterX, finalCenterY);
        finalCanvas.RotateDegrees((float)rotationAngle);
        finalCanvas.Scale(scaleToFit);
        finalCanvas.Translate(-polyCenterX, -polyCenterY);        // Extract roads with names or refs for labeling
        Dictionary<string, List<(List<SKPoint> points, string highway, float length, string? roadRef)>> roadSegmentsByName = [];

        foreach (var (nodeIds, highway, name, roadRef) in roads)
        {
            // Skip roads with neither name nor ref
            if (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(roadRef)) continue;

            // Use name as the key, or roadRef if name is null
            string labelKey = name ?? roadRef!;

            // Get road style based on type
            RoadStyle style = appSettings.RoadStyles.TryGetValue(highway, out RoadStyle? value)
                ? value
                : appSettings.RoadStyles["default"];

            List<SKPoint> points = [];
            float totalLength = 0f;
            SKPoint? prevPoint = null; foreach (long nodeId in nodeIds)
            {
                if (!nodes.TryGetValue(nodeId, out var coord)) continue;

                SKPoint point = GeoToPixel(coord.lon, coord.lat, maxLat, minLon, lonCorrection, scale);
                points.Add(point);

                // Calculate segment length for road importance
                if (prevPoint.HasValue)
                {
                    float dx = point.X - prevPoint.Value.X;
                    float dy = point.Y - prevPoint.Value.Y;
                    totalLength += (float)Math.Sqrt(dx * dx + dy * dy);
                }
                prevPoint = point;
            }

            if (points.Count < 2) continue;

            // Group roads by name/ref
            if (!roadSegmentsByName.TryGetValue(labelKey, out var segments))
            {
                roadSegmentsByName[labelKey] = [];
            }

            roadSegmentsByName[labelKey].Add((points, highway, totalLength, roadRef));
        }

        // Sort roads by type importance and length
        string[] roadPriority = ["service", "residential", "tertiary", "secondary", "primary", "trunk", "motorway"];
        var roadTypeOrder = roadPriority
            .Select((type, idx) => new { type, idx })
            .ToDictionary(x => x.type, x => x.idx); foreach (var entry in roadSegmentsByName)
        {
            string labelKey = entry.Key;
            var roadSegments = entry.Value;

            // Get the most important road type for this name/ref
            var mostImportantRoad = roadSegments
                .OrderBy(r => roadTypeOrder.TryGetValue(r.highway, out int order) ? order : 999)
                .ThenByDescending(r => r.length)
                .First();

            var points = mostImportantRoad.points;
            var highway = mostImportantRoad.highway;
            var length = mostImportantRoad.length;
            var roadRef = mostImportantRoad.roadRef;            // Format the label text according to what data we have
            string labelText;
            bool isRefOnly = false;

            if (!string.IsNullOrEmpty(labelKey) && !string.IsNullOrEmpty(roadRef))
            {
                if (labelKey == roadRef)
                {
                    // If name is null and we're using ref as the key
                    labelText = roadRef;
                    isRefOnly = true;
                }
                else
                {
                    // If both name and ref exist, combine them
                    labelText = $"{roadRef} {labelKey}";
                }
            }
            else
            {
                // Just use the key (which is either name or ref)
                labelText = labelKey;

                // Check if this is a ref-only label
                if (!string.IsNullOrEmpty(roadRef) && (string.IsNullOrEmpty(labelKey) || labelKey == roadRef))
                {
                    isRefOnly = true;
                }
            }// Skip extremely short roads, but be more lenient for important road types
            float lengthThreshold = 30f; // Base threshold

            // Adjust threshold based on road importance - show more important roads even if shorter
            if (highway == "motorway" || highway == "trunk")
                lengthThreshold = 20f;
            else if (highway == "primary")
                lengthThreshold = 25f;

            // Make ref-only labels (road numbers) visible on shorter segments
            if (!string.IsNullOrEmpty(roadRef) && string.IsNullOrEmpty(labelKey))
                lengthThreshold = 15f;

            if (length < lengthThreshold) continue;

            // Use the middle segment for label placement
            int middleIndex = points.Count / 2;
            if (middleIndex <= 0) continue;

            SKPoint p1 = points[middleIndex - 1];
            SKPoint p2 = points[middleIndex];

            // Calculate angle for the label
            float dx = p2.X - p1.X;
            float dy = p2.Y - p1.Y;
            float angle = (float)(Math.Atan2(dy, dx) * 180 / Math.PI);

            // Normalize angle for text readability (-90 to 90 degrees)
            if (angle > 90) angle -= 180;
            if (angle < -90) angle += 180;            // Adjust font size based on road type
            float fontSizeMultiplier = 1.0f;
            if (highway == "motorway") fontSizeMultiplier = 1.3f;
            else if (highway == "trunk") fontSizeMultiplier = 1.2f;
            else if (highway == "primary") fontSizeMultiplier = 1.1f;
            else if (highway == "secondary") fontSizeMultiplier = 1.0f;
            else if (highway == "tertiary") fontSizeMultiplier = 0.9f;
            else fontSizeMultiplier = 0.8f;            // Update font with new size
            // Check if this is a road number (ref) that should be styled differently
            if (isRefOnly)
            {
                // Use bold text for road number labels
                roadLabelFont = new SKFont
                {
                    Size = appSettings.LabelStyle.FontSize * fontSizeMultiplier * 1.1f, // Slightly larger
                    Typeface = SKTypeface.FromFamilyName(
                        SKTypeface.Default.FamilyName,
                        SKFontStyle.Bold)
                };
            }
            else
            {
                roadLabelFont = new SKFont
                {
                    Size = appSettings.LabelStyle.FontSize * fontSizeMultiplier,
                    Typeface = SKTypeface.FromFamilyName(SKTypeface.Default.FamilyName)
                };
            }// Measure the text to create a bounding box
            roadLabelFont.MeasureText(labelText, out SKRect textBounds, roadLabelPaint);

            // Check if road segment is long enough to fit the label
            if (length < textBounds.Width * 1.2) continue;

            // Draw the label centered at the middle segment
            SKPoint middlePoint = new((p1.X + p2.X) / 2, (p1.Y + p2.Y) / 2);

            finalCanvas.Save();
            finalCanvas.Translate(middlePoint.X, middlePoint.Y);
            finalCanvas.RotateDegrees(angle);            // Create a properly centered background rectangle
            // Text will be centered at (0,0) in the translated/rotated coordinates
            float textHeight = textBounds.Height;
            float textWidth = textBounds.Width;

            // Get proper text metrics for vertical centering
            // In typography, text is usually positioned with its baseline at y=0
            // We need to account for both ascenders (parts above baseline) and descenders (parts below)
            roadLabelFont.MeasureText("ÁjgpqQ|", out SKRect metrics, roadLabelPaint); // Text with both ascenders and descenders            // Calculate vertical offset - move text up by the difference between baseline and visual center
            // This is approximately 1/3 of the text height from the bottom of the bounding box
            float baselineOffset = textHeight * 0.35f; // More accurate than using metrics.Height/2

            // Create background rectangle centered horizontally and aligned vertically with the text's visual center
            // Shift the rectangle vertically by the baselineOffset to match where the text will be drawn
            SKRect bgRect = new(-textWidth / 2, -textHeight / 2 - baselineOffset / 2,
                               textWidth / 2, textHeight / 2 - baselineOffset / 2);

            // Apply padding from style settings
            bgRect.Inflate(appSettings.RoadLabelStyle.PaddingX, appSettings.RoadLabelStyle.PaddingY);

            // Draw rounded rectangle background with outline
            float cornerRadius = appSettings.RoadLabelStyle.CornerRadius;
            finalCanvas.DrawRoundRect(bgRect, cornerRadius, cornerRadius, roadBgPaint);
            finalCanvas.DrawRoundRect(bgRect, cornerRadius, cornerRadius, roadBgOutlinePaint);// Draw the text with proper vertical centering
            // Move text up by the baseline offset to visually center it in the background rectangle
            finalCanvas.DrawText(labelText, 0, baselineOffset, SKTextAlign.Center, roadLabelFont, roadLabelPaint);

            finalCanvas.Restore();
        }

        // Restore the canvas state
        finalCanvas.Restore();

    }

    void DrawWaterLabels(
        SKCanvas finalCanvas,
        Dictionary<long, (double lat, double lon, string? name)> nodes,
        List<(List<long> nodeIds, string type, string? name)> waterBodies,
        float scaleToFit,
        float polyCenterX,
        float polyCenterY)
    {
        SKFont waterLabelFont = new()
        {
            Size = appSettings.WaterLabelStyle.FontSize,
            Typeface = SKTypeface.FromFamilyName(
                SKTypeface.Default.FamilyName,
                appSettings.WaterLabelStyle.FontStyle == "Bold" ? SKFontStyle.Bold :
                appSettings.WaterLabelStyle.FontStyle == "Italic" ? SKFontStyle.Italic :
                SKFontStyle.Normal)
        };

        SKPaint waterLabelPaint = new()
        {
            Color = SKColor.Parse(appSettings.WaterLabelStyle.Color),
            IsAntialias = true
        };

        // Create background paint with opacity
        SKPaint waterBgPaint = new()
        {
            Color = SKColor.Parse(appSettings.WaterLabelStyle.BackgroundColor).WithAlpha((byte)appSettings.WaterLabelStyle.BackgroundOpacity),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };

        // Calculate final canvas transformations to match the map bitmap
        float finalCenterX = targetWidth / 2f;
        float finalCenterY = targetHeight / 2f;

        // Apply the same transformations that were applied to the map bitmap
        finalCanvas.Save();
        finalCanvas.Translate(finalCenterX, finalCenterY);
        finalCanvas.RotateDegrees((float)rotationAngle);
        finalCanvas.Scale(scaleToFit);
        finalCanvas.Translate(-polyCenterX, -polyCenterY);

        // Process water bodies from DrawWaterBodies function
        List<(string name, SKPoint center, float area)> waterLabels = [];

        // Extract water bodies with names for labeling
        foreach (var (nodeIds, type, name) in waterBodies)
        {
            if (string.IsNullOrEmpty(name)) continue;

            // Create a path to represent the water body
            SKPath path = new();
            bool firstPoint = true;
            foreach (long nodeId in nodeIds)
            {
                if (!nodes.TryGetValue(nodeId, out (double lat, double lon, string? name) coord))
                    continue;
                SKPoint point = GeoToPixel(coord.lon, coord.lat, maxLat, minLon, lonCorrection, scale);

                if (firstPoint) { path.MoveTo(point); firstPoint = false; }
                else { path.LineTo(point); }
            }
            path.Close();

            if (path.IsEmpty) continue;

            // Get bounding box to determine size and center
            path.GetBounds(out SKRect bounds);
            float centerX = bounds.MidX;
            float centerY = bounds.MidY;
            float area = bounds.Width * bounds.Height;

            // Only label water bodies of significant size
            if (area > 500.0f)
            {
                waterLabels.Add((name!, new SKPoint(centerX, centerY), area));
            }
        }

        // Sort water labels by area (largest first)
        waterLabels.Sort((a, b) => b.area.CompareTo(a.area));

        foreach (var (name, center, area) in waterLabels)
        {
            // Skip if name is empty or area too small
            if (string.IsNullOrEmpty(name) || area < 100) continue;

            // Adjust font size based on water body area
            float fontSizeMultiplier = 1.0f;
            if (area > 50000) fontSizeMultiplier = 1.4f;
            else if (area > 20000) fontSizeMultiplier = 1.2f;
            else if (area > 5000) fontSizeMultiplier = 1.1f;
            else if (area < 1000) fontSizeMultiplier = 0.85f;

            // Update font with new size
            waterLabelFont = new SKFont
            {
                Size = appSettings.WaterLabelStyle.FontSize * fontSizeMultiplier,
                Typeface = SKTypeface.FromFamilyName(
                    SKTypeface.Default.FamilyName,
                    appSettings.WaterLabelStyle.FontStyle == "Bold" ? SKFontStyle.Bold :
                    appSettings.WaterLabelStyle.FontStyle == "Italic" ? SKFontStyle.Italic :
                    SKFontStyle.Normal)
            };

            // Measure the text to create a bounding box
            waterLabelFont.MeasureText(name, out SKRect textBounds, waterLabelPaint);

            finalCanvas.Save();
            finalCanvas.Translate(center.X, center.Y);

            finalCanvas.DrawText(name, 0, 0, SKTextAlign.Center, waterLabelFont, waterLabelPaint);

            finalCanvas.Restore();
        }

        // Restore the canvas state
        finalCanvas.Restore();
    }

    void DrawToponyms(
        SKCanvas finalCanvas,
        Dictionary<long, (double lat, double lon, string name, string type)> places,
        float scaleToFit,
        float polyCenterX,
        float polyCenterY)
    {
        SKFont placeLabelFont = new()
        {
            Size = appSettings.PlaceLabelStyle.FontSize,
            Typeface = SKTypeface.FromFamilyName(
                SKTypeface.Default.FamilyName,
                appSettings.PlaceLabelStyle.FontStyle == "Bold" ? SKFontStyle.Bold :
                appSettings.PlaceLabelStyle.FontStyle == "Italic" ? SKFontStyle.Italic :
                SKFontStyle.Normal)
        };

        SKPaint placeLabelPaint = new()
        {
            Color = SKColor.Parse(appSettings.PlaceLabelStyle.Color),
            IsAntialias = true
        };

        SKPaint placeHaloPaint = new()
        {
            Color = SKColors.White.WithAlpha(230),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2.0f
        };

        // Sort places by importance (city, town, village, hamlet, suburb, etc.)
        string[] placeOrder = ["city", "town", "village", "hamlet", "suburb", "neighbourhood", "locality"];
        var placeTypeOrder = placeOrder
            .Select((type, idx) => new { type, idx })
            .ToDictionary(x => x.type, x => x.idx);

        // Group places by name to detect duplicates and prioritize the most important instance
        var placesByName = places.Values
            .GroupBy(p => p.name)
            .ToDictionary(
                g => g.Key,
                g => g.OrderBy(p => placeTypeOrder.TryGetValue(p.type, out int value) ? value : 999).First()
            );

        // Sort places by importance (city, town, village, hamlet, suburb, etc.)
        var sortedPlaces = placesByName.Values
            .OrderBy(p => placeTypeOrder.TryGetValue(p.type, out int value) ? value : 999)
            .ToList();

        // Calculate final canvas transformations to match the map bitmap
        float finalCenterX = targetWidth / 2f;
        float finalCenterY = targetHeight / 2f;

        // Apply the same transformations that were applied to the map bitmap
        finalCanvas.Save();
        finalCanvas.Translate(finalCenterX, finalCenterY);
        finalCanvas.RotateDegrees((float)rotationAngle);
        finalCanvas.Scale(scaleToFit);
        finalCanvas.Translate(-polyCenterX, -polyCenterY);

        foreach (var (lat, lon, name, type) in sortedPlaces)
        {
            // Convert coordinates to screen position (in original map coordinate system)
            float x = (float)((lon - minLon) * lonCorrection * scale);
            float y = (float)((maxLat - lat) * scale);

            // Skip if name is empty
            if (string.IsNullOrEmpty(name)) continue;

            // Adjust font size and style based on place type importance
            float fontSizeMultiplier = 1.0f;
            SKFontStyleWeight fontWeight = SKFontStyleWeight.Normal;

            if (type == "city")
            {
                fontSizeMultiplier = 1.5f;
                fontWeight = SKFontStyleWeight.Bold;
            }
            else if (type == "town")
            {
                fontSizeMultiplier = 1.3f;
                fontWeight = SKFontStyleWeight.Bold;
            }
            else if (type == "village")
            {
                fontSizeMultiplier = 1.1f;
                fontWeight = SKFontStyleWeight.SemiBold;
            }
            else if (type == "hamlet" || type == "suburb")
            {
                fontSizeMultiplier = 0.9f;
            }
            else
            {
                fontSizeMultiplier = 0.8f;
            }

            // Update font with new size and weight
            placeLabelFont = new SKFont
            {
                Size = appSettings.PlaceLabelStyle.FontSize * fontSizeMultiplier,
                Typeface = SKTypeface.FromFamilyName(
                        SKTypeface.Default.FamilyName,
                        new SKFontStyle(
                            fontWeight,
                            SKFontStyleWidth.Normal,
                            appSettings.PlaceLabelStyle.FontStyle == "Italic" ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright)
                    )
            };

            // Measure the text to create a bounding box
            placeLabelFont.MeasureText(name, out SKRect textBounds, placeLabelPaint);

            finalCanvas.Save();
            finalCanvas.Translate(x, y);
            // Draw text halo/outline first
            finalCanvas.DrawText(name, 0, 0, SKTextAlign.Center, placeLabelFont, placeHaloPaint);
            // Draw the label text on top
            finalCanvas.DrawText(name, 0, 0, SKTextAlign.Center, placeLabelFont, placeLabelPaint);
            finalCanvas.Restore();
        }

        // Restore the canvas state
        finalCanvas.Restore();
    }

    void DrawBuildings(SKCanvas canvas, Dictionary<long, (double lat, double lon, string? name)> nodes, List<(List<long> nodeIds, string type, string? name)> buildings)
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

    void DrawRoads(SKCanvas canvas, Dictionary<long, (double lat, double lon, string? name)> nodes, List<(List<long> nodeIds, string highway, string? name, string? roadRef)> roads)
    {
        string[] roadPriority = ["service", "residential", "tertiary", "secondary", "primary", "trunk", "motorway"];
        Dictionary<string, int> roadTypeOrder = roadPriority
            .Select((type, idx) => new { type, idx })
            .ToDictionary(x => x.type, x => x.idx);
        List<(List<long> nodeIds, string highway, string? name, string? roadRef)> sortedRoads = [.. roads.OrderBy(r => roadTypeOrder.TryGetValue(r.highway, out int order) ? order : -1)];

        // Cache for road paint objects
        Dictionary<string, (SKPaint? outline, SKPaint fill)> roadPaintCache = [];

        foreach ((List<long> nodeIds, string highway, string? name, string? roadRef) in sortedRoads)
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

    void DrawWaterways(SKCanvas canvas, Dictionary<long, (double lat, double lon, string? name)> nodes, List<(List<long> nodeIds, string type, string? name, float width)> waterways)
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

    void DrawWaterBodies(SKCanvas canvas, Dictionary<long, (double lat, double lon, string? name)> nodes, List<(List<long> nodeIds, string type, string? name)> waterBodies)
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

    static SKPoint GeoToPixel(double lon, double lat, double maxLat, double minLon, double lonCorrection, double scale)
    {
        float x = (float)((lon - minLon) * lonCorrection * scale);
        float y = (float)((maxLat - lat) * scale); // y axis: top = maxLat
        return new SKPoint(x, y);
    }

    static void ParseOsmData(
        XmlOsmStreamSource osmData,
        Dictionary<long, (double lat, double lon, string? name)> nodes,
        List<(List<long> nodeIds, string highway, string? name, string? roadRef)> roads,
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
                    string? roadRef = tags.ContainsKey("ref") ? tags["ref"] : null;
                    roads.Add((way.Nodes.ToList(), highway, name, roadRef));
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
}
