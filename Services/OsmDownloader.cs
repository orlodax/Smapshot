using OsmSharp.Streams;
using Smapshot.Models;

namespace Smapshot.Services;

internal class OsmDownloader(BoundingBoxGeo boundingBox)
{
    public async Task<XmlOsmStreamSource> DownloadRegion()
    {
        string cacheFileName = $"osm_{boundingBox.South:F5}_m{Math.Abs(boundingBox.West):F5}_{boundingBox.North:F5}_m{Math.Abs(boundingBox.East):F5}.osm";
        string cacheDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cache");
        Directory.CreateDirectory(cacheDir);
        string cacheFile = Path.Combine(cacheDir, cacheFileName);

        // Only use cache in debug mode
#if DEBUG
        // Check if we have a cached version
        if (File.Exists(cacheFile))
        {
            // Return the cached file with FileShare.ReadWrite to allow multiple readers
            return new XmlOsmStreamSource(File.Open(cacheFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        }
#endif

        // Download the data
        string url = $"https://overpass-api.de/api/map?bbox={boundingBox.West},{boundingBox.South},{boundingBox.East},{boundingBox.North}";
        Console.WriteLine($"Downloading OSM data from: {url}");

        using HttpClient client = new();
        byte[] osmData = await client.GetByteArrayAsync(url);
        Console.WriteLine($"Downloaded {osmData.Length} bytes");

#if DEBUG
        // Only save to cache in debug mode
        // Fix: Use a temporary file to avoid file access conflicts
        string tempFile = Path.Combine(cacheDir, $"temp_{Guid.NewGuid()}_{cacheFileName}");
        await File.WriteAllBytesAsync(tempFile, osmData);

        // Atomically move the temporary file to the final location
        if (File.Exists(cacheFile))
        {
            // Another thread/process may have created the file in the meantime
            File.Delete(tempFile);
            return new XmlOsmStreamSource(new MemoryStream(osmData));
        }

        try
        {
            File.Move(tempFile, cacheFile);
        }
        catch (IOException)
        {
            // If we still have a conflict, just use the data in memory
            File.Delete(tempFile);
            return new XmlOsmStreamSource(new MemoryStream(osmData));
        }

        // Return the stream from the cache file
        return new XmlOsmStreamSource(File.Open(cacheFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
#else
        // In release mode, just return the data directly from memory
        return new XmlOsmStreamSource(new MemoryStream(osmData));
#endif
    }
}
