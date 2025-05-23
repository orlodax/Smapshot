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
    const float BorderOffset = 15f;

    /// <summary>
    /// Renders the OSM map strictly within the polygon's bounding box, tightly cropped, and returns the image and polygon pixel coordinates.
    /// </summary>
    public static async Task<byte[]> RenderBasicMapToPngCropped(CoordinateCollection coordinates)
    {
        Console.WriteLine("Startin OSM...");
        // Compute minimal bounding box of the polygon
        var minLon = coordinates.Min(c => c.Longitude);
        var maxLon = coordinates.Max(c => c.Longitude);
        var minLat = coordinates.Min(c => c.Latitude);
        var maxLat = coordinates.Max(c => c.Latitude);

        // Calculate the center latitude for scaling correction
        double centerLat = (minLat + maxLat) / 2.0;

        // Correct for longitude distance scaling based on latitude
        // At the equator, 1 degree longitude ≈ 1 degree latitude in distance
        // At higher latitudes, longitude degrees become shorter
        double lonCorrection = Math.Cos(centerLat * Math.PI / 180.0);

        // Image size: tightly fit the polygon bounding box with correction for longitude
        int targetMaxDim = 2000;
        double correctedLonDiff = (maxLon - minLon) * lonCorrection;
        double scaleX = targetMaxDim / correctedLonDiff;
        double scaleY = targetMaxDim / (maxLat - minLat);
        double scale = Math.Min(scaleX, scaleY);
        int width = (int)Math.Ceiling(correctedLonDiff * scale);
        int height = (int)Math.Ceiling((maxLat - minLat) * scale);

        // Increase canvas size for outline
        int outlinePad = 10;
        width += 2 * outlinePad;
        height += 2 * outlinePad;

        // Prepare OSM data
        var osmPath = await DownloadOsmForBoundingBoxAsync(coordinates, "cache");

        // Offload CPU-bound parsing, rendering, and file writing to a background thread
        return await Task.Run(() =>
        {
            using var fileStream = File.OpenRead(osmPath);
            var source = new XmlOsmStreamSource(fileStream);
            var nodes = new Dictionary<long, (double lat, double lon, string? name)>();
            var roads = new List<(List<long> nodeIds, string highway, string? name)>();
            var waterBodies = new List<(List<long> nodeIds, string type, string? name)>();
            var waterways = new List<(List<long> nodeIds, string type, string? name, float width)>();
            var buildings = new List<(List<long> nodeIds, string type, string? name)>(); // Add buildings collection
            var places = new Dictionary<long, (double lat, double lon, string name, string type)>(); // Add places collection
            var relationMembers = new Dictionary<long, List<(long id, string role)>>();
            var relations = new Dictionary<long, (long id, string type, Dictionary<string, string> tags)>();

            // First pass: collect all nodes, ways, and relation references
            foreach (var element in source)
            {
                if (element.Type == OsmGeoType.Node)
                {
                    var node = (Node)element;
                    string? name = node.Tags?.ContainsKey("name") == true ? node.Tags["name"] : null;

                    // Collect place nodes (cities, towns, villages, hamlets, suburbs, etc.)
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
                    var way = (Way)element;
                    var tags = way.Tags ?? new TagsCollection();
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

                        // Check for width tag (could be in meters or some other unit)
                        if (tags.ContainsKey("width"))
                        {
                            if (float.TryParse(tags["width"], out float parsedWidth))
                                waterwayWidth = parsedWidth;
                        }

                        // Adjust width based on waterway type if not specified
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
                else if (element.Type == OsmGeoType.Relation)
                {
                    var relation = (Relation)element;
                    if (relation.Id.HasValue && relation.Tags != null && relation.Members != null)
                    {
                        // Store relation metadata
                        var relId = relation.Id.Value;
                        var tagDict = relation.Tags.ToDictionary(t => t.Key, t => t.Value);

                        if (tagDict.TryGetValue("type", out string? relType) &&
                            (relType == "multipolygon" || relType == "waterway"))
                        {
                            relations[relId] = (relId, relType, tagDict);

                            // Store relation members
                            var memberList = new List<(long id, string role)>();
                            foreach (var member in relation.Members)
                            {
                                if (member.Type == OsmGeoType.Way)
                                    memberList.Add((member.Id, member.Role ?? ""));
                            }
                            relationMembers[relId] = memberList;
                        }
                    }
                }
            }

            // Only keep nodes within the bounding box
            var nodesInBox = nodes.Where(kv => kv.Value.lat >= minLat && kv.Value.lat <= maxLat && kv.Value.lon >= minLon && kv.Value.lon <= maxLon)
                                .ToDictionary(kv => kv.Key, kv => kv.Value);

            // Keep only places within or near the bounding box (with some padding for context)
            double latPadding = (maxLat - minLat) * 0.1; // 10% padding
            double lonPadding = (maxLon - minLon) * 0.1;
            var filteredPlaces = places.Where(p =>
                p.Value.lat >= minLat - latPadding && p.Value.lat <= maxLat + latPadding &&
                p.Value.lon >= minLon - lonPadding && p.Value.lon <= maxLon + lonPadding)
                .ToDictionary(kv => kv.Key, kv => kv.Value);

            // Pre-filter roads: only consider roads that have at least one node within the bounding box.
            var candidateRoads = roads.Where(r => r.nodeIds.Any(nodeId => nodesInBox.ContainsKey(nodeId))).ToList();

            // Pre-filter buildings: only consider buildings that have at least one node within the bounding box
            var candidateBuildings = buildings.Where(b => b.nodeIds.Any(nodeId => nodesInBox.ContainsKey(nodeId))).ToList();

            // Build the polygon path (with internal border offset for clarity)
            var polygonPath = new SKPath();
            var polygonPixels = new List<SKPoint>();
            double centroidLon = coordinates.Average(c => c.Longitude);
            double centroidLat = coordinates.Average(c => c.Latitude);
            bool first = true;
            foreach (var coord in coordinates)
            {
                double dx = coord.Longitude - centroidLon;
                double dy = coord.Latitude - centroidLat;
                double length = Math.Sqrt(dx * dx + dy * dy);
                double offsetLon = coord.Longitude;
                double offsetLat = coord.Latitude;
                if (length > 0)
                {
                    double normDx = dx / length;
                    double normDy = dy / length;
                    double offsetLonUnits = (BorderOffset / scale) * normDx;
                    double offsetLatUnits = (BorderOffset / scale) * normDy;
                    offsetLon += offsetLonUnits;
                    offsetLat += offsetLatUnits;
                }
                float x = (float)(((offsetLon - minLon) * lonCorrection) * scale);
                float y = (float)((maxLat - offsetLat) * scale); // y axis: top = maxLat
                if (first) { polygonPath.MoveTo(x, y); first = false; }
                else { polygonPath.LineTo(x, y); }
                polygonPixels.Add(new SKPoint(x, y));
            }
            polygonPath.Close();

            // Create the bitmap
            using var bitmap = new SKBitmap(width, height);
            using var canvas = new SKCanvas(bitmap);
            bitmap.Erase(SKColors.Transparent);
            // Shift all drawing to the outline pad
            canvas.Translate(outlinePad, outlinePad);

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
                canvas.Save(); // Save before this specific clip and draw
                canvas.ClipPath(polygonPath, SKClipOperation.Intersect);
                canvas.DrawPath(polygonPath, fillPaint);
                canvas.Restore(); // Restore after drawing background, so it's clipped, but main clip below is separate
            }

            // Save canvas state before applying the main clipping for map features
            canvas.Save();
            // Now clip the canvas for all subsequent map feature drawing (water, roads)
            canvas.ClipPath(polygonPath, SKClipOperation.Intersect);

            // --- Orphan detection: Build road connectivity graph and filter using only segments inside the polygon ---
            var nodeIdToPoint = nodesInBox.ToDictionary(kv => kv.Key,
                kv =>
                {
                    float x = (float)(((kv.Value.lon - minLon) * lonCorrection) * scale);
                    float y = (float)((maxLat - kv.Value.lat) * scale);
                    return new SKPoint(x, y);
                });
            // --- Improved road clipping: keep segments that cross or touch the polygon ---
            var clippedRoads = new List<(List<long> nodeIds, string highway, string? name)>();
            // Iterate over candidateRoads instead of all roads
            for (int i = 0; i < candidateRoads.Count; i++)
            {
                var (nodeIds, highway, name) = candidateRoads[i];
                var segmentNodeIds = new List<long>();
                for (int j = 0; j < nodeIds.Count - 1; j++)
                {
                    if (!nodeIdToPoint.TryGetValue(nodeIds[j], out var ptA) || !nodeIdToPoint.TryGetValue(nodeIds[j + 1], out var ptB))
                        continue;
                    bool aInside = polygonPath.Contains(ptA.X, ptA.Y);
                    bool bInside = polygonPath.Contains(ptB.X, ptB.Y);
                    bool crosses = false;
                    if (!aInside && !bInside)
                    {
                        // Check if the segment crosses the polygon boundary
                        if (TryIntersectSegmentWithPolygon((ptA.X, ptA.Y), (ptB.X, ptB.Y), polygonPath, out var ix, out var iy))
                            crosses = true;
                    }
                    if (aInside || bInside || crosses)
                    {
                        if (segmentNodeIds.Count == 0 || segmentNodeIds.Last() != nodeIds[j])
                            segmentNodeIds.Add(nodeIds[j]);
                        segmentNodeIds.Add(nodeIds[j + 1]);
                    }
                }
                if (segmentNodeIds.Count >= 2)
                    clippedRoads.Add((segmentNodeIds.Distinct().ToList(), highway, name));
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
                float x = (float)((coord.Longitude - minLon) * scale);
                float y = (float)((maxLat - coord.Latitude) * scale);
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

            // Process any multipolygon water relations
            foreach (var (relId, (_, relType, tagDict)) in relations)
            {
                if (relType == "multipolygon" &&
                    ((tagDict.ContainsKey("natural") && tagDict["natural"] == "water") ||
                     (tagDict.ContainsKey("landuse") && tagDict["landuse"] == "reservoir") ||
                     tagDict.ContainsKey("water")))
                {
                    if (relationMembers.TryGetValue(relId, out var members))
                    {
                        string type = tagDict.ContainsKey("natural") ? tagDict["natural"] : "reservoir";
                        string? name = tagDict.ContainsKey("name") ? tagDict["name"] : null;

                        foreach (var (memberId, role) in members.Where(m => m.role == "outer"))
                        {
                            // Find the referenced way in the source data
                            var wayElement = source.FirstOrDefault(e => e.Type == OsmGeoType.Way && e.Id == memberId);
                            if (wayElement is Way way && way.Nodes != null && way.Nodes.Count() > 0)
                            {
                                waterBodies.Add((way.Nodes.ToList(), type, name));
                            }
                        }
                    }
                }
            }

            // Draw water bodies (polygons) with fill style
            var waterFillPaint = new SKPaint
            {
                Color = SKColor.Parse(styleConfig.waterStyle.color),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };

            // Prepare storage for water body centers for label placement
            var waterLabels = new List<(string name, SKPoint center, float area)>();

            foreach (var (nodeIds, type, name) in waterBodies)
            {
                SKPath path = new();
                bool firstWater = true;
                foreach (var nodeId in nodeIds)
                {
                    if (!nodesInBox.TryGetValue(nodeId, out var coord)) continue; float x = (float)(((coord.lon - minLon) * lonCorrection) * scale);
                    float y = (float)((maxLat - coord.lat) * scale);
                    if (firstWater) { path.MoveTo(x, y); firstWater = false; }
                    else { path.LineTo(x, y); }
                }
                path.Close();

                // Clip the water path to the polygon
                using var clipped = new SKPath();
                if (path.Op(polygonPath, SKPathOp.Intersect, clipped))
                {
                    canvas.DrawPath(clipped, waterFillPaint);                    // Store water body label information if it has a name
                    if (!string.IsNullOrEmpty(name))
                    {
                        // Calculate center point and area for the water body
                        clipped.GetBounds(out SKRect bounds);
                        float centerX = bounds.MidX;
                        float centerY = bounds.MidY;
                        float area = bounds.Width * bounds.Height; // Approximate area

                        // Only add if center is inside the clipped polygon and the water body is large enough
                        if (clipped.Contains(centerX, centerY) && area > 500.0f)  // Minimum size threshold
                        {
                            // Try to find a better center point using visual center calculation
                            bool foundBetterCenter = false;

                            // Only attempt visual center calculation for larger water bodies
                            if (area > 5000.0f)
                            {
                                // Sample grid points within the bounds to find the most centered point
                                int gridSize = 5;
                                float bestDistance = float.MaxValue;
                                float bestX = centerX, bestY = centerY;

                                for (int gx = 1; gx < gridSize; gx++)
                                {
                                    for (int gy = 1; gy < gridSize; gy++)
                                    {
                                        float testX = bounds.Left + (bounds.Width * gx / gridSize);
                                        float testY = bounds.Top + (bounds.Height * gy / gridSize);

                                        if (clipped.Contains(testX, testY))
                                        {
                                            // Calculate distance from boundaries
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
            }

            // Draw waterways (lines) with stroke style
            // Create paint object outside the loop, set variable properties inside
            var waterwayPaint = new SKPaint
            {
                Color = SKColor.Parse(styleConfig.waterStyle.color).WithAlpha(230), // Assuming waterStyle.color is constant
                Style = SKPaintStyle.Stroke,
                IsAntialias = true
                // StrokeWidth will be set in the loop
            };

            foreach (var (nodeIds, type, name, waterwayWidth) in waterways)
            {
                waterwayPaint.StrokeWidth = waterwayWidth; // Set variable property

                var ids = nodeIds;
                if (nodeIds.Count > 2 && nodeIds.First() == nodeIds.Last())
                    ids = nodeIds.Take(nodeIds.Count - 1).ToList();

                for (int i = 0; i < ids.Count - 1; i++)
                {
                    if (!nodesInBox.TryGetValue(ids[i], out var coordA) || !nodesInBox.TryGetValue(ids[i + 1], out var coordB))
                        continue; float xA = (float)(((coordA.lon - minLon) * lonCorrection) * scale);
                    float yA = (float)((maxLat - coordA.lat) * scale);
                    float xB = (float)(((coordB.lon - minLon) * lonCorrection) * scale);
                    float yB = (float)((maxLat - coordB.lat) * scale);

                    bool aInside = polygonPath.Contains(xA, yA);
                    bool bInside = polygonPath.Contains(xB, yB);

                    if (aInside && bInside)
                    {
                        // Both inside: draw as usual
                        using var segPath = new SKPath();
                        segPath.MoveTo(xA, yA);
                        segPath.LineTo(xB, yB);
                        canvas.DrawPath(segPath, waterwayPaint);
                    }
                    else if (aInside || bInside)
                    {
                        // One inside, one outside: interpolate intersection with polygon
                        var inside = aInside ? (xA, yA) : (xB, yB);
                        var outside = aInside ? (xB, yB) : (xA, yA);

                        // Find intersection with polygon boundary
                        if (TryIntersectSegmentWithPolygon(inside, outside, polygonPath, out var ix, out var iy))
                        {
                            using var segPath = new SKPath();
                            segPath.MoveTo(inside.Item1, inside.Item2);
                            segPath.LineTo(ix, iy);
                            canvas.DrawPath(segPath, waterwayPaint);
                        }
                    }
                    // else: both outside, skip
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

            // Cache for road paint objects
            var roadPaintCache = new Dictionary<string, (SKPaint? outline, SKPaint fill)>();

            // Draw roads (different color/width by type), clipped to the polygon using SkiaSharp
            foreach (var (road, i) in sortedRoads)
            {
                if (!connectedRoadIndices.Contains(i)) continue; // Omit orphan roads
                var (nodeIds, highway, name) = road;
                var style = styleConfig.roadStyles.ContainsKey(highway) ? styleConfig.roadStyles[highway] : styleConfig.roadStyles["default"];

                SKPaint? outlinePaint = null;
                SKPaint fillPaint;

                // Construct a unique key for the style
                string styleKey = $"hw:{highway}_oc:{style.outlineColor}_ow:{style.outlineWidth}_fc:{style.color}_fw:{style.width}";

                if (roadPaintCache.TryGetValue(styleKey, out var paints))
                {
                    outlinePaint = paints.outline;
                    fillPaint = paints.fill;
                }
                else
                {
                    if (!string.IsNullOrEmpty(style.outlineColor) && style.outlineWidth > 0)
                    {
                        outlinePaint = new SKPaint
                        {
                            Color = SKColor.Parse(style.outlineColor),
                            StrokeWidth = style.outlineWidth,
                            IsAntialias = true,
                            Style = SKPaintStyle.Stroke,
                            StrokeCap = SKStrokeCap.Round,
                            StrokeJoin = SKStrokeJoin.Round
                        };
                    }

                    fillPaint = new SKPaint
                    {
                        Color = SKColor.Parse(style.color),
                        StrokeWidth = style.width,
                        IsAntialias = true,
                        Style = SKPaintStyle.Stroke,
                        StrokeCap = SKStrokeCap.Round,
                        StrokeJoin = SKStrokeJoin.Round
                    };
                    roadPaintCache[styleKey] = (outlinePaint, fillPaint);
                }

                SKPath roadPath = new();
                bool firstNode = true;
                foreach (var nodeId in nodeIds)
                {
                    if (!nodesInBox.TryGetValue(nodeId, out var nodeCoord)) continue;
                    float x = (float)(((nodeCoord.lon - minLon) * lonCorrection) * scale);
                    float y = (float)((maxLat - nodeCoord.lat) * scale);
                    if (firstNode) { roadPath.MoveTo(x, y); firstNode = false; }
                    else { roadPath.LineTo(x, y); }
                }

                if (!roadPath.IsEmpty)
                {
                    // Draw outline first (if applicable and wider)
                    if (outlinePaint != null && style.outlineWidth > style.width)
                    {
                        canvas.DrawPath(roadPath, outlinePaint);
                    }
                    // Draw the road fill (main line)
                    canvas.DrawPath(roadPath, fillPaint);
                }
            }

            // --- Draw buildings with fill style ---
            var buildingFillPaint = new SKPaint
            {
                Color = SKColor.Parse(styleConfig.buildingStyle.color),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };

            var buildingOutlinePaint = new SKPaint
            {
                Color = SKColor.Parse(styleConfig.buildingStyle.outlineColor),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.0f,
                IsAntialias = true
            };

            foreach (var (nodeIds, buildingType, name) in candidateBuildings)
            {
                if (nodeIds.Count < 3) continue; // Need at least 3 points for a building polygon

                SKPath buildingPath = new SKPath();
                bool firstPoint = true;
                foreach (var nodeId in nodeIds)
                {
                    if (!nodesInBox.TryGetValue(nodeId, out var coord)) continue;
                    float x = (float)(((coord.lon - minLon) * lonCorrection) * scale);
                    float y = (float)((maxLat - coord.lat) * scale);
                    if (firstPoint) { buildingPath.MoveTo(x, y); firstPoint = false; }
                    else { buildingPath.LineTo(x, y); }
                }
                buildingPath.Close();

                // Only draw if we have a valid path
                if (!buildingPath.IsEmpty)
                {
                    // Clip the building path to the polygon
                    using var clipped = new SKPath();
                    if (buildingPath.Op(polygonPath, SKPathOp.Intersect, clipped))
                    {
                        canvas.DrawPath(clipped, buildingFillPaint);
                        canvas.DrawPath(clipped, buildingOutlinePaint);
                    }
                }
            }

            // Restore canvas state to remove polygonPath clipping before drawing labels or final outline
            canvas.Restore(); // This matches the canvas.Save() before the main ClipPath

            // Draw the 10px outline completely outside the polygon
            using (var outlinePaint = new SKPaint
            {
                Color = SKColors.Red,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 20,  // Make it thicker because we'll only see half
                IsAntialias = true
            })
            {
                // Save the canvas state
                canvas.Save();

                // Create a clipping region that excludes the polygon area
                canvas.ClipPath(polygonPath, SKClipOperation.Difference);

                // Draw the outline - only the outside portion will be visible due to clipping
                canvas.DrawPath(polygonPath, outlinePaint);

                // Restore the canvas state
                canvas.Restore();
            }            // --- Improved road label placement: geometry-aware, straightest segment, minimal nudge, allow multiple for long roads ---
            var labelFont = new SKFont(SKTypeface.Default, styleConfig.labelStyle.fontSize);
            var labelPaint = new SKPaint { Color = SKColor.Parse(styleConfig.labelStyle.color), IsAntialias = true, IsStroke = false };
            var placedLabelRects = new List<SKRect>();
            var labelPositionsByName = new Dictionary<string, List<SKPoint>>();
            float minLabelDistance = 800f; // Minimum distance in pixels between labels of the same name
            float minSegmentLength = 80f; // Lowered minimum segment length for label (in pixels)
            int window = 2; // Reduced window size for more flexible placement
            float insideFraction = 0.75f; // Allow segments where at least 75% of points are inside
            float labelSpacing = 1200f; // Minimum spacing between labels on the same road
            int maxNudgeAttempts = 12; // More nudge attempts
            float nudgeStep = 24f; // Larger nudge step

            // Create reusable paint objects for label backgrounds
            float labelCornerRadius = 4.0f; // Radius for rounded corners
            var labelBgPaint = new SKPaint
            {
                Color = SKColor.Parse(styleConfig.backgroundColor),
                Style = SKPaintStyle.Fill,
                IsAntialias = true
            };

            var labelBorderPaint = new SKPaint
            {
                Color = SKColors.Gray.WithAlpha(150),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1,
                IsAntialias = true
            };

            for (int i = 0; i < clippedRoads.Count; i++)
            {
                if (!connectedRoadIndices.Contains(i)) continue;
                var (nodeIds, highway, name) = clippedRoads[i];
                var roadStyle = styleConfig.roadStyles.ContainsKey(highway) ? styleConfig.roadStyles[highway] : styleConfig.roadStyles["default"];
                bool isWideRoad = highway == "motorway" || highway == "trunk" || highway == "primary" || highway == "secondary";

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
                        if (!nodesInBox.TryGetValue(nodeIds[j + k], out var coord)) break; float x = (float)(((coord.lon - minLon) * lonCorrection) * scale);
                        float y = (float)((maxLat - coord.lat) * scale);
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
                        float x0 = (float)(((c0.lon - minLon) * lonCorrection) * scale);
                        float y0 = (float)((maxLat - c0.lat) * scale);
                        float x1 = (float)(((c1.lon - minLon) * lonCorrection) * scale);
                        float y1 = (float)((maxLat - c1.lat) * scale);
                        float len = (float)Math.Sqrt((x1 - x0) * (x1 - x0) + (y1 - y0) * (y1 - y0));
                        if (len > maxLen) { maxLen = len; bestIdx = j; }
                    }
                    if (maxLen > 0)
                    {
                        var segPts = new List<SKPoint>();
                        if (nodesInBox.TryGetValue(nodeIds[bestIdx], out var c0) && nodesInBox.TryGetValue(nodeIds[bestIdx + 1], out var c1))
                        {
                            float x0 = (float)(((c0.lon - minLon) * lonCorrection) * scale);
                            float y0 = (float)((maxLat - c0.lat) * scale);
                            float x1 = (float)(((c1.lon - minLon) * lonCorrection) * scale);
                            float y1 = (float)((maxLat - c1.lat) * scale);
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
                    // Don't place labels too close to each other on the same road feature
                    if (usedMidpoints.Any(pt => (pt.X - cand.midPt.X) * (pt.X - cand.midPt.X) + (pt.Y - cand.midPt.Y) * (pt.Y - cand.midPt.Y) < labelSpacing * labelSpacing))
                        continue;

                    float angleDeg = (float)(cand.angle * 180.0 / Math.PI);
                    if (angleDeg > 90) angleDeg -= 180;
                    if (angleDeg < -90) angleDeg += 180;

                    bool placed = false;
                    SKPoint labelAnchor = SKPoint.Empty;
                    SKRect currentAabb = SKRect.Empty;

                    if (isWideRoad)
                    {
                        // Only attempt on-road placement, skip nudging
                        float currentX = cand.midPt.X;
                        float currentY = cand.midPt.Y;

                        labelFont.MeasureText(label, out SKRect textBounds, labelPaint);
                        // Set font to bold and adjust height
                        labelFont.Typeface = SKTypeface.FromFamilyName(SKTypeface.Default.FamilyName, SKFontStyle.Bold);
                        labelFont.Size = styleConfig.labelStyle.fontSize * 1.2f; // Example: increase height by 20%
                        // For wide roads, always center label if anchor is inside and no overlap
                        var labelRect = new SKRect(-textBounds.Width / 2, textBounds.Top, textBounds.Width / 2, textBounds.Bottom);
                        var corners = new[]
                        {
                            RotateAndTranslate(labelRect.Left, labelRect.Top, cand.angle, currentX, currentY),
                            RotateAndTranslate(labelRect.Right, labelRect.Top, cand.angle, currentX, currentY),
                            RotateAndTranslate(labelRect.Left, labelRect.Bottom, cand.angle, currentX, currentY),
                            RotateAndTranslate(labelRect.Right, labelRect.Bottom, cand.angle, currentX, currentY)
                        };
                        var aabb = new SKRect(corners.Min(pt => pt.X), corners.Min(pt => pt.Y), corners.Max(pt => pt.X), corners.Max(pt => pt.Y));
                        bool overlaps = placedLabelRects.Any(r => r.IntersectsWith(aabb));
                        bool tooClose = false;
                        if (labelPositionsByName.TryGetValue(label, out var positions))
                        {
                            foreach (var pt in positions)
                            {
                                float dist = (float)Math.Sqrt((pt.X - currentX) * (pt.X - currentX) + (pt.Y - currentY) * (pt.Y - currentY));
                                if (dist < minLabelDistance) { tooClose = true; break; }
                            }
                        }
                        bool anchorInsidePolygon = polygonPath.Contains(currentX, currentY);
                        if (anchorInsidePolygon && !overlaps && !tooClose)
                        {
                            labelAnchor = new SKPoint(currentX, currentY);
                            currentAabb = aabb;
                            placed = true;
                        }
                    }
                    else
                    {
                        // Only use nudging for narrow roads
                        for (int attempt = 0; attempt < maxNudgeAttempts && !placed; attempt++)
                        {
                            float nudgeAmount = attempt * nudgeStep;

                            float currentX = cand.midPt.X + (float)Math.Cos(cand.angle + Math.PI / 2) * nudgeAmount;
                            float currentY = cand.midPt.Y + (float)Math.Sin(cand.angle + Math.PI / 2) * nudgeAmount;

                            labelFont.MeasureText(label, out SKRect textBounds, labelPaint);
                            labelFont.Typeface = SKTypeface.FromFamilyName(SKTypeface.Default.FamilyName, SKFontStyle.Normal);
                            labelFont.Size = styleConfig.labelStyle.fontSize;
                            var labelRect = new SKRect(-textBounds.Width / 2, textBounds.Top, textBounds.Width / 2, textBounds.Bottom);
                            var corners = new[]
                            {
                                RotateAndTranslate(labelRect.Left, labelRect.Top, cand.angle, currentX, currentY),
                                RotateAndTranslate(labelRect.Right, labelRect.Top, cand.angle, currentX, currentY),
                                RotateAndTranslate(labelRect.Left, labelRect.Bottom, cand.angle, currentX, currentY),
                                RotateAndTranslate(labelRect.Right, labelRect.Bottom, cand.angle, currentX, currentY)
                            };
                            bool allCornersInside = corners.All(pt => polygonPath.Contains(pt.X, pt.Y));

                            var aabb = new SKRect(corners.Min(pt => pt.X), corners.Min(pt => pt.Y), corners.Max(pt => pt.X), corners.Max(pt => pt.Y));
                            bool overlaps = placedLabelRects.Any(r => r.IntersectsWith(aabb));
                            bool tooClose = false;
                            if (labelPositionsByName.TryGetValue(label, out var positions))
                            {
                                foreach (var pt in positions)
                                {
                                    float dist = (float)Math.Sqrt((pt.X - currentX) * (pt.X - currentX) + (pt.Y - currentY) * (pt.Y - currentY));
                                    if (dist < minLabelDistance) { tooClose = true; break; }
                                }
                            }

                            bool anchorInsidePolygon = polygonPath.Contains(currentX, currentY);
                            if (anchorInsidePolygon && allCornersInside && !overlaps && !tooClose)
                            {
                                labelAnchor = new SKPoint(currentX, currentY);
                                currentAabb = aabb;
                                placed = true;
                            }
                        }
                    }
                    if (placed)
                    {
                        canvas.Save();
                        canvas.Translate(labelAnchor.X, labelAnchor.Y);
                        canvas.RotateDegrees(angleDeg);                        // Get text measurements for the background rectangle
                        labelFont.MeasureText(label, out SKRect textBounds, labelPaint);

                        // Create a slightly expanded rectangle for better legibility
                        float padding = 8.0f; // Padding around the text
                        var bgRect = new SKRect(
                            textBounds.Left - padding,
                            textBounds.Top - padding,
                            textBounds.Right + padding,
                            textBounds.Bottom + padding
                        );

                        // Draw background rounded rectangle using the map's background color
                        canvas.DrawRoundRect(bgRect, labelCornerRadius, labelCornerRadius, labelBgPaint);
                        canvas.DrawRoundRect(bgRect, labelCornerRadius, labelCornerRadius, labelBorderPaint);

                        // Draw the label text
                        canvas.DrawText(label, 0, 0, labelFont, labelPaint);
                        canvas.Restore();

                        placedLabelRects.Add(currentAabb);
                        if (!labelPositionsByName.ContainsKey(label))
                            labelPositionsByName[label] = new List<SKPoint>();
                        labelPositionsByName[label].Add(labelAnchor);
                        usedMidpoints.Add(cand.midPt); labelsPlaced++;
                    }
                }
            }

            // --- Draw water body labels ---
            // Sort water bodies by area (largest first) to prioritize larger water bodies
            var sortedWaterLabels = waterLabels.OrderByDescending(w => w.area).ToList();

            var waterLabelFont = new SKFont
            {
                Size = styleConfig.waterLabelStyle.fontSize,
                Typeface = SKTypeface.FromFamilyName(
                    SKTypeface.Default.FamilyName,
                    styleConfig.waterLabelStyle.fontStyle == "Bold" ? SKFontStyle.Bold :
                    styleConfig.waterLabelStyle.fontStyle == "Italic" ? SKFontStyle.Italic :
                    SKFontStyle.Normal
                )
            }; var waterLabelPaint = new SKPaint
            {
                Color = SKColor.Parse(styleConfig.waterLabelStyle.color),
                IsAntialias = true
            };

            // Create halo paint for better readability (subtle outline around text)
            var waterHaloPaint = new SKPaint
            {
                Color = SKColors.White.WithAlpha(230),
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2.0f
            };

            foreach (var (name, center, _) in sortedWaterLabels)
            {
                // Skip if name is empty
                if (string.IsNullOrEmpty(name)) continue;

                // Measure the text to create a bounding box
                waterLabelFont.MeasureText(name, out SKRect textBounds, waterLabelPaint);

                // Create a slightly expanded rectangle for better legibility
                float padding = 8.0f;
                var bgRect = new SKRect(
                    textBounds.Left - padding,
                    textBounds.Top - padding,
                    textBounds.Right + padding,
                    textBounds.Bottom + padding
                );

                // Calculate the bounds in screen coordinates
                var aabb = new SKRect(
                    center.X + bgRect.Left,
                    center.Y + bgRect.Top,
                    center.X + bgRect.Right,
                    center.Y + bgRect.Bottom
                );

                // Check if the label overlaps with any existing label
                bool overlaps = placedLabelRects.Any(r => r.IntersectsWith(aabb));

                // Check if anchor is inside polygon
                bool anchorInsidePolygon = polygonPath.Contains(center.X, center.Y);
                if (anchorInsidePolygon && !overlaps)
                {
                    canvas.Save();
                    canvas.Translate(center.X, center.Y);                    // Draw text halo/outline first
                    canvas.DrawText(name, 0, 0, SKTextAlign.Center, waterLabelFont, waterHaloPaint);
                    // Draw the label text on top
                    canvas.DrawText(name, 0, 0, SKTextAlign.Center, waterLabelFont, waterLabelPaint);
                    canvas.Restore();

                    placedLabelRects.Add(aabb);
                    if (!labelPositionsByName.ContainsKey(name))
                        labelPositionsByName[name] = new List<SKPoint>();
                    labelPositionsByName[name].Add(center);
                }
            }

            // --- Draw place toponyms ---
            // Create place name font based on style configuration
            var placeLabelFont = new SKFont
            {
                Size = styleConfig.placeLabelStyle.fontSize,
                Typeface = SKTypeface.FromFamilyName(
                    SKTypeface.Default.FamilyName,
                    styleConfig.placeLabelStyle.fontStyle == "Bold" ? SKFontStyle.Bold :
                    styleConfig.placeLabelStyle.fontStyle == "Italic" ? SKFontStyle.Italic :
                    SKFontStyle.Normal)
            };
            var placeLabelPaint = new SKPaint
            {
                Color = SKColor.Parse(styleConfig.placeLabelStyle.color),
                IsAntialias = true
            };

            // Create halo paint for better readability (subtle outline around text)
            var placeHaloPaint = new SKPaint
            {
                Color = SKColors.White.WithAlpha(230),
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2.0f
            };            // Sort places by importance (city, town, village, hamlet, suburb, etc.)
            string[] placeOrder = new[] { "city", "town", "village", "hamlet", "suburb", "neighbourhood", "locality" };
            var placeTypeOrder = placeOrder
                .Select((type, idx) => new { type, idx })
                .ToDictionary(x => x.type, x => x.idx);

            // Group places by name to detect duplicates and prioritize the most important instance
            var placesByName = filteredPlaces.Values
                .GroupBy(p => p.name)
                .ToDictionary(
                    g => g.Key,
                    g => g.OrderBy(p => placeTypeOrder.ContainsKey(p.type) ? placeTypeOrder[p.type] : 999).First()
                );

            // Sort places by importance (city, town, village, hamlet, suburb, etc.)
            var sortedPlaces = placesByName.Values
                .OrderBy(p => placeTypeOrder.ContainsKey(p.type) ? placeTypeOrder[p.type] : 999)
                .ToList();

            foreach (var (lat, lon, name, type) in sortedPlaces)
            {
                // Convert coordinates to screen position
                float x = (float)(((lon - minLon) * lonCorrection) * scale);
                float y = (float)((maxLat - lat) * scale);

                // Skip if name is empty
                if (string.IsNullOrEmpty(name)) continue;                // Adjust font size and style based on place type importance
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
                    fontWeight = SKFontStyleWeight.SemiBold;
                }
                else if (type == "village")
                {
                    fontSizeMultiplier = 1.1f;
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
                    Size = styleConfig.placeLabelStyle.fontSize * fontSizeMultiplier,
                    Typeface = SKTypeface.FromFamilyName(
                        SKTypeface.Default.FamilyName,
                        new SKFontStyle(fontWeight, SKFontStyleWidth.Normal,
                            styleConfig.placeLabelStyle.fontStyle == "Italic" ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright)
                    )
                };

                // Measure the text to create a bounding box
                placeLabelFont.MeasureText(name, out SKRect textBounds, placeLabelPaint);

                // Create a slightly expanded rectangle for better legibility
                float padding = 8.0f;
                var bgRect = new SKRect(
                    textBounds.Left - padding,
                    textBounds.Top - padding,
                    textBounds.Right + padding,
                    textBounds.Bottom + padding
                );

                // Calculate the bounds in screen coordinates
                var aabb = new SKRect(
                    x + bgRect.Left,
                    y + bgRect.Top,
                    x + bgRect.Right,
                    y + bgRect.Bottom
                );

                // Check if the label overlaps with any existing label
                bool overlaps = placedLabelRects.Any(r => r.IntersectsWith(aabb));

                // Check if anchor is inside polygon
                bool anchorInsidePolygon = polygonPath.Contains(x, y); if (anchorInsidePolygon && !overlaps)
                {
                    canvas.Save();
                    canvas.Translate(x, y);                    // Draw text halo/outline first
                    canvas.DrawText(name, 0, 0, SKTextAlign.Center, placeLabelFont, placeHaloPaint);
                    // Draw the label text on top
                    canvas.DrawText(name, 0, 0, SKTextAlign.Center, placeLabelFont, placeLabelPaint);
                    canvas.Restore();

                    placedLabelRects.Add(aabb);
                    if (!labelPositionsByName.ContainsKey(name))
                        labelPositionsByName[name] = new List<SKPoint>();
                    labelPositionsByName[name].Add(new SKPoint(x, y));
                }
            }

            Console.WriteLine("Finished OSM");

            return bitmap.Encode(SKEncodedImageFormat.Png, 100).ToArray();
        });
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

    // Helper: Find intersection of segment (p1 to p2) with polygon boundary
    private static bool TryIntersectSegmentWithPolygon((float xA, float yA) inside, (float xB, float yB) outside, SKPath polygon, out float ix, out float iy)
    {
        // Sample along the segment to find the crossing point (binary search)
        float t0 = 0, t1 = 1;
        for (int iter = 0; iter < 20; iter++)
        {
            float tm = (t0 + t1) / 2;
            float xm = inside.xA + (outside.xB - inside.xA) * tm;
            float ym = inside.yA + (outside.yB - inside.yA) * tm;
            if (polygon.Contains(xm, ym))
                t0 = tm;
            else
                t1 = tm;
        }
        ix = inside.xA + (outside.xB - inside.xA) * t0;
        iy = inside.yA + (outside.yB - inside.yA) * t0;
        return true;
    }
}
