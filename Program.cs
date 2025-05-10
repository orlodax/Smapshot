using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using SharpKml.Engine;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

// Use qualified name for Path to avoid ambiguity
using Path = System.IO.Path;

// Register QuestPDF license (community edition)
QuestPDF.Settings.License = LicenseType.Community;

// Implement application
await ProcessKmlFileAsync(args);

static async Task ProcessKmlFileAsync(string[] args)
{
    try
    {        // Validate command-line arguments
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: Smapshot <path-to-kml-file> [output-pdf-path] [--style=<map-style>]");
            Console.WriteLine("Example: Smapshot polygon.kml map.pdf --style=topo");
            Console.WriteLine("\nAvailable map styles:");
            Console.WriteLine("  standard  - Default OpenStreetMap style (default)");
            Console.WriteLine("  cycle     - CyclOSM style with more road details");
            Console.WriteLine("  topo      - OpenTopoMap with elevation contours");
            Console.WriteLine("  terrain   - Stamen Terrain with hill shading");
            Console.WriteLine("  toner     - Stamen Toner high contrast B&W (good for printing)");
            return;
        }

        string kmlFilePath = args[0];
        string outputPdfPath = args.Length > 1 && !args[1].StartsWith("--")
            ? args[1]
            : Path.ChangeExtension(kmlFilePath, "pdf");

        // Process optional style parameter
        string mapStyle = "standard"; // Default style
        foreach (var arg in args)
        {
            if (arg.StartsWith("--style="))
            {
                mapStyle = arg["--style=".Length..].ToLower();
            }
        }

        Console.WriteLine($"Processing KML file: {kmlFilePath}");
        Console.WriteLine($"Output PDF will be created at: {outputPdfPath}");
        Console.WriteLine($"Using map style: {mapStyle}");

        // Parse KML file
        if (!File.Exists(kmlFilePath))
        {
            Console.WriteLine($"Error: KML file not found at {kmlFilePath}");
            return;
        }

        // Read and parse the KML file
        KmlFile kmlFile;
        try
        {
            using var fileStream = File.OpenRead(kmlFilePath);
            kmlFile = KmlFile.Load(fileStream);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error parsing KML file: {ex.Message}");
            Console.WriteLine("Attempting to parse KML file using XmlDocument instead...");

            try
            {
                // Alternative parsing method
                var xmlDoc = new System.Xml.XmlDocument();
                xmlDoc.Load(kmlFilePath);

                // Check for default KML namespaces
                var nsmgr = new System.Xml.XmlNamespaceManager(xmlDoc.NameTable);
                nsmgr.AddNamespace("kml", "http://www.opengis.net/kml/2.2");

                // Save the file with explicit namespace
                var tempKmlPath = Path.Combine(Path.GetTempPath(), $"fixed_kml_{Guid.NewGuid()}.kml");
                xmlDoc.Save(tempKmlPath);

                // Try parsing again
                using (var fileStream = File.OpenRead(tempKmlPath))
                {
                    kmlFile = KmlFile.Load(fileStream);
                }

                // Clean up temp file
                File.Delete(tempKmlPath);
            }
            catch (Exception innerEx)
            {
                Console.WriteLine($"Error during alternative KML parsing: {innerEx.Message}");
                return;
            }
        }

        // Extract polygon coordinates from KML
        var polygon = kmlFile.Root.Flatten().OfType<SharpKml.Dom.Polygon>().FirstOrDefault();

        if (polygon == null)
        {
            Console.WriteLine("Error: No polygon found in the KML file");
            return;
        }

        var coordinates = polygon.OuterBoundary?.LinearRing?.Coordinates;

        if (coordinates == null || coordinates.Count == 0)
        {
            Console.WriteLine("Error: No coordinates found in the polygon");
            return;
        }

        // Calculate bounding box
        var minLat = coordinates.Min(c => c.Latitude);
        var maxLat = coordinates.Max(c => c.Latitude);
        var minLon = coordinates.Min(c => c.Longitude);
        var maxLon = coordinates.Max(c => c.Longitude);

        Console.WriteLine($"Bounding box: ({minLon},{minLat}) to ({maxLon},{maxLat})");

        // Add some padding to the bounding box (10%)
        double latPadding = (maxLat - minLat) * 0.1;
        double lonPadding = (maxLon - minLon) * 0.1;

        minLat -= latPadding;
        maxLat += latPadding;
        minLon -= lonPadding;
        maxLon += lonPadding;        // Generate a map image using OpenStreetMap tiles
        var mapImage = await GenerateOpenStreetMapTilesImageAsync(minLat, minLon, maxLat, maxLon, [.. coordinates], mapStyle);

        if (mapImage == null)
        {
            Console.WriteLine("Error: Could not generate map image");
            return;
        }

        // Save the map image temporarily
        string tempImagePath = Path.Combine(Path.GetTempPath(), $"map_{Guid.NewGuid()}.png");
        mapImage.Save(tempImagePath);

        // Create PDF with the map
        GeneratePdf(outputPdfPath, tempImagePath, kmlFilePath, minLat, minLon, maxLat, maxLon);

        // Clean up the temporary image
        File.Delete(tempImagePath);

        Console.WriteLine($"Successfully created PDF at: {outputPdfPath}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error: {ex.Message}");
        Console.WriteLine(ex.StackTrace);
    }
}

static async Task<SixLabors.ImageSharp.Image> GenerateOpenStreetMapTilesImageAsync(
    double minLat, double minLon, double maxLat, double maxLon,
    List<SharpKml.Base.Vector> coordinates, string mapStyle = "standard")
{
    // Calculate the optimal zoom level based on the bounding box size
    int baseZoom = CalculateOptimalZoomLevel(minLat, minLon, maxLat, maxLon);

    // Use a higher zoom level for downloading the tiles (2 levels higher for larger labels)
    int downloadZoom = Math.Min(19, baseZoom + 2); // Two zoom levels higher, maximum 19
    Console.WriteLine($"Base zoom level: {baseZoom}, downloading at zoom level: {downloadZoom}");

    // Calculate the expanded bounds to download (wider area)
    // The factor 2.0 gives us approximately twice the area in each dimension
    double expandFactor = 2.0;
    double latRange = maxLat - minLat;
    double lonRange = maxLon - minLon;
    double expandedMinLat = minLat - (latRange * (expandFactor - 1) / 2);
    double expandedMaxLat = maxLat + (latRange * (expandFactor - 1) / 2);
    double expandedMinLon = minLon - (lonRange * (expandFactor - 1) / 2);
    double expandedMaxLon = maxLon + (lonRange * (expandFactor - 1) / 2);

    Console.WriteLine($"Expanded bounding box: ({expandedMinLon},{expandedMinLat}) to ({expandedMaxLon},{expandedMaxLat})");

    // Convert expanded lat/lon to tile coordinates at the higher zoom level
    var minTileX = LonToTileX(expandedMinLon, downloadZoom);
    var maxTileX = LonToTileX(expandedMaxLon, downloadZoom);
    var minTileY = LatToTileY(expandedMaxLat, downloadZoom); // Note: Y is inverted in tile system
    var maxTileY = LatToTileY(expandedMinLat, downloadZoom);

    // Calculate tile counts
    int tilesX = maxTileX - minTileX + 1;
    int tilesY = maxTileY - minTileY + 1;
    int totalTiles = tilesX * tilesY;
    int maxTotalTiles = 100; // Increased from 64 to allow for larger area download

    Console.WriteLine($"Map would require {tilesX}x{tilesY}={totalTiles} tiles at zoom level {downloadZoom}");

    // If we need too many tiles, reduce the zoom level
    while (totalTiles > maxTotalTiles && downloadZoom > 1)
    {
        downloadZoom--;
        Console.WriteLine($"Reducing zoom level to {downloadZoom} to limit tile count");

        // Recalculate tile coordinates with new zoom
        minTileX = LonToTileX(expandedMinLon, downloadZoom);
        maxTileX = LonToTileX(expandedMaxLon, downloadZoom);
        minTileY = LatToTileY(expandedMaxLat, downloadZoom);
        maxTileY = LatToTileY(expandedMinLat, downloadZoom);

        tilesX = maxTileX - minTileX + 1;
        tilesY = maxTileY - minTileY + 1;
        totalTiles = tilesX * tilesY;
    }

    // Calculate the full image dimensions at the download zoom level
    int tileSize = 256; // Standard OSM tile size
    int fullWidth = tilesX * tileSize;
    int fullHeight = tilesY * tileSize;

    Console.WriteLine($"Creating full map image with dimensions {fullWidth}x{fullHeight} pixels");

    // Create a new image with the calculated dimensions
    var fullImage = new Image<Rgba32>(fullWidth, fullHeight);

    // Download and composite tiles (same as before)
    using var httpClient = new HttpClient();
    httpClient.DefaultRequestHeaders.Add("User-Agent", "Smapshot/1.0 MapGenerator");

    string tileUrlTemplate;
    switch (mapStyle.ToLower())
    {
        // Same map style cases as before
        case "cycle":
            tileUrlTemplate = "https://a.tile-cyclosm.openstreetmap.fr/cyclosm/{0}/{1}/{2}.png";
            Console.WriteLine("Using CyclOSM map style with enhanced road details");
            break;
        case "topo":
            tileUrlTemplate = "https://a.tile.opentopomap.org/{0}/{1}/{2}.png";
            Console.WriteLine("Using OpenTopoMap style with elevation contours");
            break;
        case "terrain":
            tileUrlTemplate = "https://stamen-tiles.a.ssl.fastly.net/terrain/{0}/{1}/{2}.png";
            Console.WriteLine("Using Stamen Terrain style with hill shading");
            break;
        case "toner":
            tileUrlTemplate = "https://stamen-tiles.a.ssl.fastly.net/toner/{0}/{1}/{2}.png";
            Console.WriteLine("Using Stamen Toner high contrast B&W style");
            break;
        case "standard":
        default:
            tileUrlTemplate = "https://tile.openstreetmap.org/{0}/{1}/{2}.png";
            Console.WriteLine("Using standard OpenStreetMap style");
            break;
    }

    // Download tiles (same as before)
    var downloadTasks = new List<Task>();
    var tileImages = new Dictionary<(int, int), SixLabors.ImageSharp.Image>();
    var sem = new SemaphoreSlim(4);

    for (int x = minTileX; x <= maxTileX; x++)
    {
        for (int y = minTileY; y <= maxTileY; y++)
        {
            int tileX = x;
            int tileY = y;

            var downloadTask = Task.Run(async () =>
            {
                await sem.WaitAsync();
                try
                {
                    var tileUrl = string.Format(tileUrlTemplate, downloadZoom, tileX, tileY);
                    Console.WriteLine($"Downloading tile: {tileUrl}");

                    try
                    {
                        var tileImageBytes = await httpClient.GetByteArrayAsync(tileUrl);
                        using var tileImageStream = new MemoryStream(tileImageBytes);
                        var tileImage = SixLabors.ImageSharp.Image.Load(tileImageStream);

                        lock (tileImages)
                        {
                            tileImages[(tileX, tileY)] = tileImage;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error downloading tile {tileX},{tileY}: {ex.Message}");
                    }

                    await Task.Delay(200);
                }
                finally
                {
                    sem.Release();
                }
            });

            downloadTasks.Add(downloadTask);
        }
    }

    await Task.WhenAll(downloadTasks);

    // Compose all tiles into the full image (same as before)
    for (int x = minTileX; x <= maxTileX; x++)
    {
        for (int y = minTileY; y <= maxTileY; y++)
        {
            if (tileImages.TryGetValue((x, y), out var tileImage))
            {
                int offsetX = (x - minTileX) * tileSize;
                int offsetY = (y - minTileY) * tileSize;

                fullImage.Mutate(ctx => ctx.DrawImage(tileImage, new Point(offsetX, offsetY), 1.0f));
                tileImage.Dispose();
            }
        }
    }

    // Calculate the pixel coordinates of the original (non-expanded) bounding box
    int origMinX = (int)((LonToPixelX(minLon, downloadZoom) - (minTileX * tileSize)));
    int origMaxX = (int)((LonToPixelX(maxLon, downloadZoom) - (minTileX * tileSize)));
    int origMinY = (int)((LatToPixelY(maxLat, downloadZoom) - (minTileY * tileSize)));
    int origMaxY = (int)((LatToPixelY(minLat, downloadZoom) - (minTileY * tileSize)));

    // Make sure the crop rectangle is within bounds
    origMinX = Math.Max(0, origMinX);
    origMinY = Math.Max(0, origMinY);
    origMaxX = Math.Min(fullWidth - 1, origMaxX);
    origMaxY = Math.Min(fullHeight - 1, origMaxY);

    int cropWidth = origMaxX - origMinX;
    int cropHeight = origMaxY - origMinY;

    Console.WriteLine($"Cropping to original area: ({origMinX},{origMinY}) to ({origMaxX},{origMaxY}) - {cropWidth}x{cropHeight} pixels");    // Convert coordinates of the polygon from original zoom level to the higher zoom level
    var adjustedCoordinates = coordinates.Select(coord =>
    {
        double x = LonToPixelX(coord.Longitude, downloadZoom) - minTileX * tileSize;
        double y = LatToPixelY(coord.Latitude, downloadZoom) - minTileY * tileSize;
        return new CoordWithPixels(coord.Latitude, coord.Longitude, x, y);
    }).ToList();

    // Draw the polygon on the full image before cropping
    DrawPolygonBoundary(fullImage, adjustedCoordinates, expandedMinLat, expandedMinLon, expandedMaxLat, expandedMaxLon, minTileX, minTileY, downloadZoom, tileSize);

    // Crop the image to the original bounding box area
    var croppedImage = fullImage.Clone(ctx => ctx.Crop(new Rectangle(origMinX, origMinY, cropWidth, cropHeight)));

    // Apply an additional scale for higher visibility of map details
    float zoomFactor = 1.5f;
    Console.WriteLine($"Applying additional scale factor of {zoomFactor}x for better readability");
    var scaledImage = croppedImage.Clone(ctx => ctx.Resize((int)(cropWidth * zoomFactor), (int)(cropHeight * zoomFactor)));

    // Dispose of the original images to free memory
    fullImage.Dispose();
    croppedImage.Dispose();

    return scaledImage;
}

static void DrawPolygonBoundary(Image<Rgba32> image, List<CoordWithPixels> coordinates,
    double minLat, double minLon, double maxLat, double maxLon,
    int minTileX, int minTileY, int zoom, int tileSize)
{
    Console.WriteLine("Applying visual effect: colored polygon on grayscale background...");

    // Convert the polygon coordinates to pixel coordinates
    var points = new List<PointF>();
    foreach (var coord in coordinates)
    {
        points.Add(new PointF((float)coord.PixelX, (float)coord.PixelY));
    }

    // Create a slightly larger polygon for clarity
    var expandedPoints = ExpandPolygon(points, 8);

    // STEP 1: Keep a copy of the original colored image
    using var originalImage = image.Clone();

    // STEP 2: Create fully desaturated grayscale version with slightly reduced brightness
    image.Mutate(ctx => ctx.Grayscale(1.0f).Brightness(0.9f));

    // STEP 3: Create a polygon mask based on the expanded points
    using var mask = new Image<Rgba32>(image.Width, image.Height, Color.Black);

    // Fill the polygon area with white
    mask.Mutate(ctx =>
    {
        ctx.FillPolygon(Color.White, [.. expandedPoints]);
    });

    // STEP 4: Copy back colored pixels from original image where mask is white (inside polygon)
    for (int y = 0; y < image.Height; y++)
    {
        for (int x = 0; x < image.Width; x++)
        {
            // Check if the pixel is inside the polygon (white in mask)
            if (mask[x, y].R > 128) // Use threshold for white detection
            {
                // Copy the pixel from the original colored image
                image[x, y] = originalImage[x, y];
            }
        }
    }

    // STEP 5: Add a solid red outline around the polygon
    image.Mutate(ctx =>
    {
        ctx.DrawPolygon(Color.Red, 4f, [.. expandedPoints]);
    });

    Console.WriteLine("Visualization effect applied successfully!");
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
    var expandedPoints = new List<PointF>();
    foreach (var point in points)
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

static int CalculateOptimalZoomLevel(double minLat, double minLon, double maxLat, double maxLon)
{
    // Calculate the degrees per pixel needed for the bounding box
    double latDiff = maxLat - minLat;
    double lonDiff = maxLon - minLon;

    // Take into account the cos of the latitude for more accurate distance calculation
    // This is important because longitude degrees vary in actual distance based on latitude
    double midLat = (minLat + maxLat) / 2.0;
    double cosLat = Math.Cos(midLat * Math.PI / 180.0);
    double adjustedLonDiff = lonDiff * cosLat;

    // Use the larger of the two dimensions for zoom calculation
    double maxDiff = Math.Max(latDiff, adjustedLonDiff);
    // Target more pixels for better detail - adjusted for A4 paper at 300 DPI
    // A4 is roughly 210x297mm, which is about 2480x3508 pixels at 300 DPI
    const int targetPixels = 2000; // Significantly increased for better detail
    double degreesPerPixel = maxDiff / targetPixels;

    // Calculate zoom level based on degrees per pixel
    // At zoom level 0, one pixel is about 0.703125 degrees (180/256)
    double zoom = Math.Log(0.703125 / degreesPerPixel) / Math.Log(2);

    // Add a zoom bias to get more detail (+1 means one zoom level higher)
    double zoomBias = 1.0;
    zoom += zoomBias;

    // Round to an integer zoom level, clamping between reasonable bounds
    // Increased max zoom from 18 to 19 for more detail in small areas
    return Math.Max(1, Math.Min(19, (int)Math.Round(zoom)));
}

static int LonToTileX(double lon, int zoom)
{
    return (int)Math.Floor((lon + 180.0) / 360.0 * (1 << zoom));
}

static int LatToTileY(double lat, int zoom)
{
    return (int)Math.Floor((1 - Math.Log(Math.Tan(lat * Math.PI / 180.0) +
        1.0 / Math.Cos(lat * Math.PI / 180.0)) / Math.PI) / 2.0 * (1 << zoom));
}

static int LonToPixelX(double lon, int zoom)
{
    return (int)Math.Floor((lon + 180.0) / 360.0 * (1 << zoom) * 256);
}

static int LatToPixelY(double lat, int zoom)
{
    return (int)Math.Floor((1 - Math.Log(Math.Tan(lat * Math.PI / 180.0) +
        1.0 / Math.Cos(lat * Math.PI / 180.0)) / Math.PI) / 2.0 * (1 << zoom) * 256);
}


static void GeneratePdf(string outputPath, string mapImagePath, string kmlFilePath,
    double minLat, double minLon, double maxLat, double maxLon)
{
    // Get KML file name for header
    string kmlFileName = Path.GetFileName(kmlFilePath);

    // Create PDF document
    QuestPDF.Fluent.Document.Create(document =>
    {
        document.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(10); // Reduced margin to maximize map area

            page.Header().Height(50).Element(header => // Fixed header height
            {
                header.Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text($"Map from KML: {kmlFileName}")
                            .FontSize(14).Bold();

                        col.Item().Text($"Generated: {DateTime.Now:g} • Coords: ({minLon:F4},{minLat:F4}) to ({maxLon:F4},{maxLat:F4})")
                            .FontSize(8);
                    });
                });
            });
            // Maximize the content area by giving it all available space
            page.Content().Element(content =>
            {
                content.Image(mapImagePath)
                    .FitArea()
                    .WithCompressionQuality(ImageCompressionQuality.High); // High quality for PDF image
            });

            page.Footer().Height(15).AlignCenter().Text(text => // Smaller footer
            {
                text.Span("Page ").FontSize(8);
                text.CurrentPageNumber().FontSize(8);
                text.Span(" of ").FontSize(8);
                text.TotalPages().FontSize(8);
            });
        });
    })
    .GeneratePdf(outputPath);
}
