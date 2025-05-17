using SharpKml.Dom;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Smapshot.Helpers;
using Smapshot.Models;

namespace Smapshot.Services;

public class TileManager(BoundingBoxGeo boundingBox, BoundingBoxGeo expandedBoundingBox, string mapStyle, int tileSize)
{
    const int MaxTotalTiles = 400; // Increased from 64 to allow for larger area download

    readonly BoundingBoxGeo boundingBox = boundingBox;
    readonly BoundingBoxGeo expandedBoundingBox = expandedBoundingBox; // Expanded bounding box for tile download
    readonly string mapStyle = mapStyle;
    readonly int tileSize = tileSize; // Size of each tile in pixels

    public int Zoom { get; private set; } = 15; // Default zoom level

    public async Task<Image<Rgba32>> GenerateTilesImageAsync()
    {
        int baseZoom = CalculateOptimalZoomLevel();

        // Use a higher zoom level for downloading the tiles (2 levels higher for larger labels)
        Zoom = Math.Min(19, baseZoom + 2); // Two zoom levels higher, maximum 19
        Console.WriteLine($"Base zoom level: {baseZoom}, downloading at zoom level: {Zoom}");

        // Convert expanded lat/lon to tile coordinates at the higher zoom level
        (int minTileX, int maxTileX, int minTileY, int maxTileY) = MapHelper.GetTileBounds(expandedBoundingBox, Zoom);

        // Calculate tile counts
        int tilesX = maxTileX - minTileX + 1;
        int tilesY = maxTileY - minTileY + 1;
        int totalTiles = tilesX * tilesY;

        // If we need too many tiles, reduce the zoom level
        while (totalTiles > MaxTotalTiles && Zoom > 1)
        {
            Zoom--;

            Console.WriteLine($"Reducing zoom level to {Zoom} to limit tile count");

            // Recalculate tile coordinates with new zoom
            (minTileX, maxTileX, minTileY, maxTileY) = MapHelper.GetTileBounds(expandedBoundingBox, Zoom);

            tilesX = maxTileX - minTileX + 1;
            tilesY = maxTileY - minTileY + 1;
            totalTiles = tilesX * tilesY;
            Console.WriteLine($"Map would require {tilesX}x{tilesY}={totalTiles} tiles at zoom level {Zoom}");
        }

        // Calculate the full image dimensions at the download zoom level

        int fullWidth = tilesX * tileSize;
        int fullHeight = tilesY * tileSize;

        Console.WriteLine($"Creating full map image with dimensions {fullWidth}x{fullHeight} pixels");

        return await DownloadAndComposeTilesAsync(fullWidth, fullHeight);
    }

    int CalculateOptimalZoomLevel()
    {
        // Use the larger of the two dimensions for zoom calculation
        double higherDimension = Math.Max(boundingBox.Height, boundingBox.Width);

        // Target more pixels for better detail - adjusted for A4 paper at 300 DPI
        // A4 is roughly 210x297mm, which is about 2480x3508 pixels at 300 DPI
        const int targetPixels = 3000; // Significantly increased for better detail
        double degreesPerPixel = higherDimension / targetPixels;

        // Calculate zoom level based on degrees per pixel
        // At zoom level 0, one pixel is about 0.703125 degrees (180/256)
        double zoom = Math.Round(Math.Log(0.703125 / degreesPerPixel) / Math.Log(2));

        // Add a zoom bias to get more detail (+1 means one zoom level higher)
        double zoomBias = 1.0;
        zoom += zoomBias;

        // Round to an integer zoom level, clamping between reasonable bounds
        // Increased max zoom from 18 to 19 for more detail in small areas
        return Math.Max(1, Math.Min(19, (int)Math.Round(zoom)));
    }

    async Task<Image<Rgba32>> DownloadAndComposeTilesAsync(
        int fullWidth,
        int fullHeight)

    {
        Dictionary<(int, int), Image> tileImages = [];
        List<(int x, int y, int zoom, string cachePath)> missingTiles = [];

        TryLoadTilesFromCache(tileImages, missingTiles);

        await DownloadMissingTilesAsync(tileImages, missingTiles);

        return ComposeTiles(fullWidth, fullHeight, tileImages);
    }

    private List<(int x, int y, int zoom, string cachePath)> TryLoadTilesFromCache(
        Dictionary<(int, int), Image> tileImages,
        List<(int x, int y, int zoom, string cachePath)> missingTiles)
    {
        // Tile cache directory, now includes map style as a subfolder
        string cacheRoot = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tiles_cache", mapStyle.ToLower());
        Directory.CreateDirectory(cacheRoot);

        (int minTileX, int maxTileX, int minTileY, int maxTileY) = MapHelper.GetTileBounds(expandedBoundingBox, Zoom);

        Parallel.For(minTileX, maxTileX + 1, x =>
        {
            for (int y = minTileY; y <= maxTileY; y++)
            {
                int tileX = x;
                int tileY = y;
                int tileZoom = Zoom;
                string zoomFolder = Path.Combine(cacheRoot, tileZoom.ToString());
                Directory.CreateDirectory(zoomFolder);
                string cachePath = Path.Combine(zoomFolder, $"{tileX}_{tileY}.png");
                if (File.Exists(cachePath))
                {
                    try
                    {
                        var imageBytes = File.ReadAllBytes(cachePath);
                        using var ms = new MemoryStream(imageBytes);
                        var tileImage = SixLabors.ImageSharp.Image.Load(ms);
                        lock (tileImages)
                        {
                            tileImages[(tileX, tileY)] = tileImage;
                        }
                        Console.WriteLine($"Loaded tile from cache: {cachePath}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error loading cached tile {cachePath}: {ex.Message}");
                        lock (missingTiles)
                        {
                            missingTiles.Add((tileX, tileY, tileZoom, cachePath));
                        }
                    }
                }
                else
                {
                    lock (missingTiles)
                    {
                        missingTiles.Add((tileX, tileY, tileZoom, cachePath));
                    }
                }
            }
        });

        return missingTiles;
    }

    private async Task DownloadMissingTilesAsync(
        Dictionary<(int, int), Image> tileImages,
        List<(int x, int y, int zoom, string cachePath)> missingTiles)
    {
        SemaphoreSlim sem = new(4);
        using HttpClient httpClient = new();
        httpClient.DefaultRequestHeaders.Add("User-Agent", "Smapshot/1.0 MapGenerator");

        List<Task> downloadTasks = [];
        foreach (var (tileX, tileY, tileZoom, cachePath) in missingTiles)
        {
            var downloadTask = Task.Run(async () =>
            {
                await sem.WaitAsync();
                try
                {
                    var tileUrl = string.Format(GetTitleUrlTemplate(mapStyle), tileZoom, tileX, tileY);
                    Console.WriteLine($"Downloading tile: {tileUrl}");
                    try
                    {
                        byte[] tileImageBytes = await httpClient.GetByteArrayAsync(tileUrl);
                        using MemoryStream tileImageStream = new(tileImageBytes);
                        var tileImage = Image.Load(tileImageStream);
                        tileImage.Save(cachePath);
                        lock (tileImages)
                        {
                            tileImages[(tileX, tileY)] = tileImage;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error downloading tile {tileX},{tileY}: {ex.Message}");
                    }
                    await Task.Delay(200); // Only delay for downloads
                }
                finally
                {
                    sem.Release();
                }
            });
            downloadTasks.Add(downloadTask);
        }
        await Task.WhenAll(downloadTasks);
    }

    Image<Rgba32> ComposeTiles(
        int fullWidth,
        int fullHeight,
        Dictionary<(int, int), Image> tileImages)
    {
        // Create a blank image to hold the tiles
        Image<Rgba32> fullImage = new(fullWidth, fullHeight);

        (int minTileX, int maxTileX, int minTileY, int maxTileY) = MapHelper.GetTileBounds(expandedBoundingBox, Zoom);

        for (int x = minTileX; x <= maxTileX; x++)
        {
            for (int y = minTileY; y <= maxTileY; y++)
            {
                if (tileImages.TryGetValue((x, y), out var tileImage))
                {
                    int offsetX = (x - minTileX) * tileSize;
                    int offsetY = (y - minTileY) * tileSize;

                    fullImage.Mutate(ctx => ctx.DrawImage(tileImage, new SixLabors.ImageSharp.Point(offsetX, offsetY), 1.0f));
                    tileImage.Dispose();
                }
            }
        }

        return fullImage;
    }

    static string GetTitleUrlTemplate(string mapStyle)
    {
        return mapStyle.ToLower() switch
        {
            "cycle" => "https://a.tile-cyclosm.openstreetmap.fr/cyclosm/{0}/{1}/{2}.png",
            "topo" => "https://a.tile.opentopomap.org/{0}/{1}/{2}.png",
            "carto-light" => "https://cartodb-basemaps-a.global.ssl.fastly.net/light_all/{0}/{1}/{2}.png",
            "carto-dark" => "https://cartodb-basemaps-a.global.ssl.fastly.net/dark_all/{0}/{1}/{2}.png",
            "standard" => "https://tile.openstreetmap.org/{0}/{1}/{2}.png",
            _ => "https://tile.openstreetmap.org/{0}/{1}/{2}.png",
        };
    }
}