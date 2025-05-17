using System.Text.Json;
using OsmSharp;
using OsmSharp.Streams;
using OsmSharp.Tags;
using SharpKml.Dom;
using SkiaSharp;
using Smapshot.Models;

namespace Smapshot.Helpers;

public static class OsmSkiaRenderer
{
    public static async Task RenderBasicMapToPng(string outputPng, CoordinateCollection coordinates)
    {
        var maxLon = coordinates.Max(c => c.Longitude);
        var minLon = coordinates.Min(c => c.Longitude);
        var maxLat = coordinates.Max(c => c.Latitude);
        var minLat = coordinates.Min(c => c.Latitude);

        var osmPath = await DownloadOsmForBoundingBoxAsync(coordinates, "cache");
        var fileStream = File.OpenRead(osmPath);
        var source = new XmlOsmStreamSource(fileStream);
        var nodes = new Dictionary<long, (double lat, double lon, string? name)>();
        var roads = new List<(List<long> nodeIds, string highway, string? name)>();
        var waters = new List<List<long>>();

        foreach (var element in source)
        {
            if (element.Type == OsmGeoType.Node)
            {
                var node = (Node)element;
                string? name = node.Tags?.ContainsKey("name") == true ? node.Tags["name"] : null;
                if (node is not null && node.Id is { } id && node.Latitude is { } lat && node.Longitude is { } lon)
                    nodes[id] = (lat, lon, name);
            }
            else if (element.Type == OsmGeoType.Way)
            {
                var way = (Way)element;
                var tags = way.Tags ?? new TagsCollection();
                if (tags.ContainsKey("highway"))
                {
                    string highway = tags["highway"];
                    string? name = tags.ContainsKey("name") ? tags["name"] : null;
                    roads.Add((way.Nodes.ToList(), highway, name));
                }
                else if (tags.ContainsKey("waterway") || (tags.ContainsKey("natural") && tags["natural"] == "water") || (tags.ContainsKey("landuse") && tags["landuse"] == "reservoir"))
                {
                    waters.Add(way.Nodes.ToList());
                }
            }
        }

        // Only keep nodes within the bounding box
        var nodesInBox = nodes.Where(kv => kv.Value.lat >= minLat && kv.Value.lat <= maxLat && kv.Value.lon >= minLon && kv.Value.lon <= maxLon)
                            .ToDictionary(kv => kv.Key, kv => kv.Value);

        // Calculate uniform scale to fit the bounding box into the bitmap (same scale for both axes)
        double scaleX = 2400 / (maxLon - minLon);
        double scaleY = 3250 / (maxLat - minLat);
        double scale = Math.Min(scaleX, scaleY);
        // Use the same scale for both axes
        // Center the map in the bitmap
        double xOffset = (2400 - (maxLon - minLon) * scale) / 2.0;
        double yOffset = (3250 - (maxLat - minLat) * scale) / 2.0;

        using var bitmap = new SKBitmap(2400, 3250);
        using var canvas = new SKCanvas(bitmap);
        // Set transparent background for the bitmap
        bitmap.Erase(SKColors.Transparent);

        // Build a polygon path from the CoordinateCollection for clipping, with an outward offset
        float borderOffset = 10f; // pixels
        var polygonPath = new SKPath();
        bool firstPoly = true;
        foreach (var coord in coordinates)
        {
            // Calculate the vector from the centroid to the point
            double centroidLon = coordinates.Average(c => c.Longitude);
            double centroidLat = coordinates.Average(c => c.Latitude);
            double dx = coord.Longitude - centroidLon;
            double dy = coord.Latitude - centroidLat;
            double length = Math.Sqrt(dx * dx + dy * dy);
            double offsetLon = coord.Longitude;
            double offsetLat = coord.Latitude;
            if (length > 0)
            {
                // Offset outward by a small amount in lat/lon space, scaled to borderOffset in pixels
                double normDx = dx / length;
                double normDy = dy / length;
                // Convert borderOffset from pixels to lon/lat units
                double offsetLonUnits = (borderOffset / scale) * normDx;
                double offsetLatUnits = (borderOffset / scale) * normDy;
                offsetLon += offsetLonUnits;
                offsetLat += offsetLatUnits;
            }
            float x = (float)((offsetLon - minLon) * scale + xOffset);
            float y = (float)((maxLat - offsetLat) * scale + yOffset);
            if (firstPoly) { polygonPath.MoveTo(x, y); firstPoly = false; }
            else { polygonPath.LineTo(x, y); }
        }
        polygonPath.Close();

        // --- Load style config ---
        MapStyleConfig styleConfig;
        try
        {
            var json = File.ReadAllText("mapstyle.json");
            styleConfig = JsonSerializer.Deserialize<MapStyleConfig>(json) ?? new MapStyleConfig();
        }
        catch { styleConfig = new MapStyleConfig(); }

        // Fill the polygon area with the desired background color from style config
        using (var fillPaint = new SKPaint { Color = SKColor.Parse(styleConfig.backgroundColor), Style = SKPaintStyle.Fill, IsAntialias = true })
        {
            canvas.Save();
            canvas.ClipPath(polygonPath, SKClipOperation.Intersect);
            canvas.DrawPath(polygonPath, fillPaint);
            canvas.Restore();
        }
        // Now clip the canvas for all subsequent drawing
        canvas.ClipPath(polygonPath, SKClipOperation.Intersect);

        // --- Orphan detection: Build road connectivity graph and filter using only segments inside the polygon ---
        var nodeIdToPoint = nodesInBox.ToDictionary(
            kv => kv.Key,
            kv =>
            {
                float x = (float)((kv.Value.lon - minLon) * scale + xOffset);
                float y = (float)((maxLat - kv.Value.lat) * scale + yOffset);
                return new SKPoint(x, y);
            });
        var clippedRoads = new List<(List<long> nodeIds, string highway, string? name)>();
        for (int i = 0; i < roads.Count; i++)
        {
            var (nodeIds, highway, name) = roads[i];
            var insideNodeIds = new List<long>();
            for (int j = 0; j < nodeIds.Count; j++)
            {
                if (!nodeIdToPoint.TryGetValue(nodeIds[j], out var pt)) continue;
                if (polygonPath.Contains(pt.X, pt.Y))
                    insideNodeIds.Add(nodeIds[j]);
            }
            // Only keep roads with at least 2 consecutive inside nodes (i.e., at least one segment inside)
            if (insideNodeIds.Count >= 2)
                clippedRoads.Add((insideNodeIds, highway, name));
        }
        var clippedNodeSet = new HashSet<long>(clippedRoads.SelectMany(r => r.nodeIds));
        var roadGraph = new RoadNetworkGraph(clippedRoads, clippedNodeSet);
        var connectedRoadIndices = roadGraph.GetMainNetworkComponent();

        // --- Border stub pruning: Remove leaf nodes at the border ---
        // 1. Identify border nodes (within 2 pixels of the polygon border)
        var borderNodeIds = new HashSet<long>();
        float borderThreshold = 2.0f;
        // Build polygon vertices from KML coordinates
        var polyPoints = new List<SKPoint>();
        foreach (var coord in coordinates)
        {
            float x = (float)((coord.Longitude - minLon) * scale + xOffset);
            float y = (float)((maxLat - coord.Latitude) * scale + yOffset);
            polyPoints.Add(new SKPoint(x, y));
        }
        foreach (var kv in nodeIdToPoint)
        {
            var pt = kv.Value;
            if (!polygonPath.Contains(pt.X, pt.Y)) continue;
            foreach (var polyPt in polyPoints)
            {
                float dx = polyPt.X - pt.X;
                float dy = polyPt.Y - pt.Y;
                if (dx * dx + dy * dy < borderThreshold * borderThreshold)
                {
                    borderNodeIds.Add(kv.Key);
                    break;
                }
            }
        }
        // 2. Build node degree map for the main network
        var nodeDegree = new Dictionary<long, int>();
        foreach (var i in connectedRoadIndices)
        {
            var (nodeIds, _, _) = clippedRoads[i];
            foreach (var nodeId in nodeIds)
            {
                if (!nodeDegree.ContainsKey(nodeId)) nodeDegree[nodeId] = 0;
                nodeDegree[nodeId]++;
            }
        }
        // 3. Iteratively prune leaf nodes at the border
        var toRemove = new HashSet<int>();
        bool changed;
        do
        {
            changed = false;
            foreach (var i in connectedRoadIndices.Except(toRemove).ToList())
            {
                var (nodeIds, _, _) = clippedRoads[i];
                if (nodeIds.Count < 2) continue;
                bool firstIsBorderLeaf = borderNodeIds.Contains(nodeIds.First()) && nodeDegree[nodeIds.First()] <= 1;
                bool lastIsBorderLeaf = borderNodeIds.Contains(nodeIds.Last()) && nodeDegree[nodeIds.Last()] <= 1;
                if ((firstIsBorderLeaf || lastIsBorderLeaf) && nodeIds.Count <= 3)
                {
                    toRemove.Add(i);
                    foreach (var nodeId in nodeIds)
                    {
                        nodeDegree[nodeId] = Math.Max(0, nodeDegree[nodeId] - 1);
                    }
                    changed = true;
                }
            }
        } while (changed);
        // 4. Update connectedRoadIndices to exclude pruned stubs
        connectedRoadIndices.ExceptWith(toRemove);

        // --- Improved border stub pruning: Only keep roads reachable from interior (non-border) nodes ---
        // 1. Identify interior nodes (not near border)
        var interiorNodeIds = nodeIdToPoint.Keys.Except(borderNodeIds).ToHashSet();
        // 2. Build node-to-road index for the main network
        var nodeToRoads = new Dictionary<long, List<int>>();
        foreach (var i in connectedRoadIndices)
        {
            var (nodeIds, _, _) = clippedRoads[i];
            foreach (var nodeId in nodeIds)
            {
                if (!nodeToRoads.ContainsKey(nodeId)) nodeToRoads[nodeId] = new List<int>();
                nodeToRoads[nodeId].Add(i);
            }
        }
        // 3. BFS/DFS from all interior nodes to find all reachable roads
        var reachableRoads = new HashSet<int>();
        var visitedNodes = new HashSet<long>();
        var stack = new Stack<long>(interiorNodeIds);
        while (stack.Count > 0)
        {
            var nodeId = stack.Pop();
            if (!visitedNodes.Add(nodeId)) continue;
            if (!nodeToRoads.TryGetValue(nodeId, out var roadIndices)) continue;
            foreach (var roadIdx in roadIndices)
            {
                if (reachableRoads.Add(roadIdx))
                {
                    var (nodeIds, _, _) = clippedRoads[roadIdx];
                    foreach (var nid in nodeIds)
                    {
                        if (!visitedNodes.Contains(nid))
                            stack.Push(nid);
                    }
                }
            }
        }
        // 4. Only keep roads that are reachable from the interior
        connectedRoadIndices.IntersectWith(reachableRoads);

        // Draw water bodies (light blue), clipped to the polygon using SkiaSharp
        var waterPaint = new SKPaint { Color = SKColor.Parse(styleConfig.waterStyle.color), Style = SKPaintStyle.Fill, IsAntialias = true };
        foreach (var way in waters)
        {
            SKPath path = new();
            bool first = true;
            foreach (var nodeId in way)
            {
                if (!nodesInBox.TryGetValue(nodeId, out var coord)) continue;
                float x = (float)((coord.lon - minLon) * scale + xOffset);
                float y = (float)((maxLat - coord.lat) * scale + yOffset);
                if (first) { path.MoveTo(x, y); first = false; }
                else { path.LineTo(x, y); }
            }
            path.Close();
            // Clip the water path to the polygon
            using var clipped = new SKPath();
            if (path.Op(polygonPath, SKPathOp.Intersect, clipped))
            {
                canvas.DrawPath(clipped, waterPaint);
            }
        }

        // --- Z-order: Draw roads from lowest to highest priority ---
        string[] roadPriority = new[] { "service", "residential", "tertiary", "secondary", "primary", "trunk", "motorway" };
        var roadTypeOrder = roadPriority
            .Select((type, idx) => new { type, idx })
            .ToDictionary(x => x.type, x => x.idx);
        var sortedRoads = clippedRoads
            .Select((r, i) => (r, i))
            .OrderBy(tuple => roadTypeOrder.TryGetValue(tuple.r.highway, out int order) ? order : -1)
            .ToList();
        // Draw roads (different color/width by type), clipped to the polygon using SkiaSharp
        foreach (var (road, i) in sortedRoads)
        {
            if (!connectedRoadIndices.Contains(i)) continue; // Omit orphan roads
            var (nodeIds, highway, name) = road;
            var style = styleConfig.roadStyles.ContainsKey(highway) ? styleConfig.roadStyles[highway] : styleConfig.roadStyles["default"];
            // Draw outline first (darker, thicker)
            if (!string.IsNullOrEmpty(style.outlineColor) && style.outlineWidth > style.width)
            {
                var outlinePaint = new SKPaint
                {
                    Color = SKColor.Parse(style.outlineColor),
                    StrokeWidth = style.outlineWidth,
                    IsAntialias = true,
                    Style = SKPaintStyle.Stroke
                };
                SKPath path = new();
                bool first = true;
                foreach (var nodeId in nodeIds)
                {
                    if (!nodesInBox.TryGetValue(nodeId, out var coord)) continue;
                    float x = (float)((coord.lon - minLon) * scale + xOffset);
                    float y = (float)((maxLat - coord.lat) * scale + yOffset);
                    if (first) { path.MoveTo(x, y); first = false; }
                    else { path.LineTo(x, y); }
                }
                canvas.Save();
                canvas.ClipPath(polygonPath, SKClipOperation.Intersect);
                canvas.DrawPath(path, outlinePaint);
                canvas.Restore();
            }
            // Draw main road line (lighter, thinner)
            var roadPaint = new SKPaint
            {
                Color = SKColor.Parse(style.color),
                StrokeWidth = style.width,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke
            };
            SKPath mainPath = new();
            bool firstMain = true;
            foreach (var nodeId in nodeIds)
            {
                if (!nodesInBox.TryGetValue(nodeId, out var coord)) continue;
                float x = (float)((coord.lon - minLon) * scale + xOffset);
                float y = (float)((maxLat - coord.lat) * scale + yOffset);
                if (firstMain) { mainPath.MoveTo(x, y); firstMain = false; }
                else { mainPath.LineTo(x, y); }
            }
            canvas.Save();
            canvas.ClipPath(polygonPath, SKClipOperation.Intersect);
            canvas.DrawPath(mainPath, roadPaint);
            canvas.Restore();
        }

        // --- Improved road label placement: geometry-aware, straightest segment, minimal nudge, allow multiple for long roads ---
        var labelFont = new SKFont(SKTypeface.Default, styleConfig.labelStyle.fontSize);
        var labelPaint = new SKPaint { Color = SKColor.Parse(styleConfig.labelStyle.color), IsAntialias = true, IsStroke = false };
        var placedLabelRects = new List<SKRect>();
        var labelPositionsByName = new Dictionary<string, List<SKPoint>>();
        float minLabelDistance = 800f; // Minimum distance in pixels between labels of the same name
        float minSegmentLength = 80f; // Lowered minimum segment length for label (in pixels)
        int window = 2; // Reduced window size for more flexible placement
        float insideFraction = 0.75f; // Allow segments where at least 75% of points are inside
        float labelSpacing = 1200f; // Minimum spacing between labels on the same road
        for (int i = 0; i < clippedRoads.Count; i++)
        {
            if (!connectedRoadIndices.Contains(i)) continue;
            var (nodeIds, highway, name) = clippedRoads[i];
            if (highway == "service" || highway == "residential" || highway == "track" || highway == "footway" || highway == "path" || highway == "cycleway" || highway == "bridleway" || highway == "steps" || highway == "pedestrian")
                continue;
            string label = name ?? string.Empty;
            if (string.IsNullOrWhiteSpace(label))
            {
                var way = source.FirstOrDefault(e => e.Type == OsmGeoType.Way && e is Way w && w.Nodes.SequenceEqual(nodeIds)) as Way;
                if (way != null && way.Tags != null && way.Tags.ContainsKey("ref"))
                {
                    label = way.Tags["ref"];
                }
                else
                {
                    continue;
                }
            }
            // Hide roundabout names: skip labeling if the OSM way is a roundabout
            var wayForLabel = source.FirstOrDefault(e => e.Type == OsmGeoType.Way && e is Way w && w.Nodes.SequenceEqual(nodeIds)) as Way;
            if (wayForLabel != null && wayForLabel.Tags != null && wayForLabel.Tags.ContainsKey("junction") && wayForLabel.Tags["junction"] == "roundabout")
                continue;
            // Find all candidate segments (window of N nodes) inside the polygon
            var candidates = new List<(int startIdx, float len, double angle, SKPoint midPt, List<SKPoint> segPts, int insideCount)>();
            for (int j = 0; j <= nodeIds.Count - window; j++)
            {
                var segPts = new List<SKPoint>();
                int insideCount = 0;
                for (int k = 0; k < window; k++)
                {
                    if (!nodesInBox.TryGetValue(nodeIds[j + k], out var coord)) break;
                    float x = (float)((coord.lon - minLon) * scale + xOffset);
                    float y = (float)((maxLat - coord.lat) * scale + yOffset);
                    segPts.Add(new SKPoint(x, y));
                    if (polygonPath.Contains(x, y)) insideCount++;
                }
                if (segPts.Count < window) continue;
                float dx = segPts.Last().X - segPts.First().X;
                float dy = segPts.Last().Y - segPts.First().Y;
                float len = (float)Math.Sqrt(dx * dx + dy * dy);
                if (len < minSegmentLength) continue;
                if (insideCount < insideFraction * window) continue;
                double angleRad = Math.Atan2(dy, dx);
                float midX = (segPts.First().X + segPts.Last().X) / 2;
                float midY = (segPts.First().Y + segPts.Last().Y) / 2;
                candidates.Add((j, len, angleRad, new SKPoint(midX, midY), segPts, insideCount));
            }
            // If no candidates, fallback: use the longest available segment (even if not enough inside)
            if (candidates.Count == 0 && nodeIds.Count >= 2)
            {
                float maxLen = 0; int bestIdx = 0;
                for (int j = 0; j <= nodeIds.Count - 2; j++)
                {
                    if (!nodesInBox.TryGetValue(nodeIds[j], out var c0) || !nodesInBox.TryGetValue(nodeIds[j + 1], out var c1)) continue;
                    float x0 = (float)((c0.lon - minLon) * scale + xOffset);
                    float y0 = (float)((maxLat - c0.lat) * scale + yOffset);
                    float x1 = (float)((c1.lon - minLon) * scale + xOffset);
                    float y1 = (float)((maxLat - c1.lat) * scale + yOffset);
                    float len = (float)Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
                    if (len > maxLen) { maxLen = len; bestIdx = j; }
                }
                if (maxLen > 0)
                {
                    var segPts = new List<SKPoint>();
                    if (nodesInBox.TryGetValue(nodeIds[bestIdx], out var c0) && nodesInBox.TryGetValue(nodeIds[bestIdx + 1], out var c1))
                    {
                        float x0 = (float)((c0.lon - minLon) * scale + xOffset);
                        float y0 = (float)((maxLat - c0.lat) * scale + yOffset);
                        float x1 = (float)((c1.lon - minLon) * scale + xOffset);
                        float y1 = (float)((maxLat - c1.lat) * scale + yOffset);
                        segPts.Add(new SKPoint(x0, y0));
                        segPts.Add(new SKPoint(x1, y1));
                        double angleRad = Math.Atan2(y1 - y0, x1 - x0);
                        float midX = (x0 + x1) / 2;
                        float midY = (y0 + y1) / 2;
                        candidates.Add((bestIdx, maxLen, angleRad, new SKPoint(midX, midY), segPts, 0));
                    }
                }
            }
            // Sort candidates by straightness (max length), then by how centered the segment is in the polygon
            var bestCandidates = candidates.OrderByDescending(c => c.len).ToList();
            var usedMidpoints = new List<SKPoint>();
            int maxLabels = Math.Max(1, (int)(nodeIds.Count * scale / labelSpacing));
            int labelsPlaced = 0;
            foreach (var cand in bestCandidates)
            {
                if (labelsPlaced >= maxLabels) break;
                // Don't place labels too close to each other
                if (usedMidpoints.Any(pt => (pt.X - cand.midPt.X) * (pt.X - cand.midPt.X) + (pt.Y - cand.midPt.Y) * (pt.Y - cand.midPt.Y) < labelSpacing * labelSpacing))
                    continue;
                float angleDeg = (float)(cand.angle * 180.0 / Math.PI);
                if (angleDeg > 90) angleDeg -= 180;
                if (angleDeg < -90) angleDeg += 180;
                // Try to place label at midpoint, minimal nudge if needed
                bool placed = false;
                for (int attempt = 0; attempt < 4 && !placed; attempt++)
                {
                    float nudge = attempt * 12f;
                    float x = cand.midPt.X + (float)Math.Cos(cand.angle + Math.PI / 2) * nudge;
                    float y = cand.midPt.Y + (float)Math.Sin(cand.angle + Math.PI / 2) * nudge;
                    labelFont.MeasureText(label, out SKRect textBounds, labelPaint);
                    var labelRect = new SKRect(-textBounds.Width / 2, textBounds.Top, textBounds.Width / 2, textBounds.Bottom);
                    var corners = new[]
                    {
                        RotateAndTranslate(labelRect.Left, labelRect.Top, cand.angle, x, y),
                        RotateAndTranslate(labelRect.Right, labelRect.Top, cand.angle, x, y),
                        RotateAndTranslate(labelRect.Left, labelRect.Bottom, cand.angle, x, y),
                        RotateAndTranslate(labelRect.Right, labelRect.Bottom, cand.angle, x, y)
                    };
                    bool allCornersInside = corners.All(pt => polygonPath.Contains(pt.X, pt.Y));
                    float minX = corners.Min(pt => pt.X);
                    float maxX = corners.Max(pt => pt.X);
                    float minY = corners.Min(pt => pt.Y);
                    float maxY = corners.Max(pt => pt.Y);
                    var aabb = new SKRect(minX, minY, maxX, maxY);
                    bool overlaps = placedLabelRects.Any(r => r.IntersectsWith(aabb));
                    bool tooClose = false;
                    if (labelPositionsByName.TryGetValue(label, out var positions))
                    {
                        foreach (var pt in positions)
                        {
                            float dist = (float)Math.Sqrt((pt.X - x) * (pt.X - x) + (pt.Y - y) * (pt.Y - y));
                            if (dist < minLabelDistance)
                            {
                                tooClose = true;
                                break;
                            }
                        }
                    }
                    if (allCornersInside && !overlaps && !tooClose)
                    {
                        canvas.Save();
                        canvas.Translate(x, y);
                        canvas.RotateDegrees(angleDeg);
                        canvas.DrawText(label, 0, 0, labelFont, labelPaint);
                        canvas.Restore();
                        placedLabelRects.Add(aabb);
                        if (!labelPositionsByName.ContainsKey(label))
                            labelPositionsByName[label] = new List<SKPoint>();
                        labelPositionsByName[label].Add(new SKPoint(x, y));
                        usedMidpoints.Add(cand.midPt);
                        placed = true;
                        labelsPlaced++;
                    }
                }
            }
        }

        // Save the bitmap to the specified file
        using var stream = File.OpenWrite(outputPng);
        bitmap.Encode(stream, SKEncodedImageFormat.Png, 100);
    }

    private static (double lat, double lon) GetCoordinateOffset(double lat, double lon, double distance, double bearing)
    {
        // Simplified Haversine formula implementation for offset calculation
        double R = 6371e3; // Earth radius in meters
        double phi1 = lat * Math.PI / 180;
        double phi2 = (lat + distance * Math.Cos(bearing * Math.PI / 180)) * Math.PI / 180;
        double lambda1 = lon * Math.PI / 180;
        double lambda2 = lon + Math.Atan2(Math.Sin(bearing * Math.PI / 180) * Math.Sin(distance / R) * Math.Cos(phi1),
                                           Math.Cos(distance / R) - Math.Sin(phi1) * Math.Sin(phi2)) * 180 / Math.PI;
        return (phi2 * 180 / Math.PI, lambda2);
    }

    private static SKPoint RotateAndTranslate(float x, float y, double angleRad, float translateX, float translateY)
    {
        // Rotate around the origin (0,0), then translate
        double cosTheta = Math.Cos(angleRad);
        double sinTheta = Math.Sin(angleRad);
        return new SKPoint(
            (float)(x * cosTheta - y * sinTheta + translateX),
            (float)(x * sinTheta + y * cosTheta + translateY));
    }

    private static async Task<string> DownloadOsmForBoundingBoxAsync(CoordinateCollection coordinates, string cacheFolder)
    {
        var maxLon = coordinates.Max(c => c.Longitude);
        var minLon = coordinates.Min(c => c.Longitude);
        var maxLat = coordinates.Max(c => c.Latitude);
        var minLat = coordinates.Min(c => c.Latitude);
        return await OsmDownloader.DownloadOsmIfNeededAsync(minLat, minLon, maxLat, maxLon, cacheFolder);
    }
}
