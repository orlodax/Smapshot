using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace Smapshot.Helpers
{
    public static class OsmDownloader
    {
        // Downloads OSM XML data for the given bounding box and caches it in the specified cache folder
        public static async Task<string> DownloadOsmIfNeededAsync(double minLat, double minLon, double maxLat, double maxLon, string cacheFolder)
        {
            Directory.CreateDirectory(cacheFolder);
            string bboxKey = $"{minLat:F5}_{minLon:F5}_{maxLat:F5}_{maxLon:F5}".Replace('.', '_').Replace('-', 'm');
            string cachePath = Path.Combine(cacheFolder, $"osm_{bboxKey}.osm");
            if (File.Exists(cachePath))
            {
                Console.WriteLine($"OSM data found in cache: {cachePath}");
                return cachePath;
            }

            string url = $"https://overpass-api.de/api/map?bbox={minLon},{minLat},{maxLon},{maxLat}";
            Console.WriteLine($"Downloading OSM data from: {url}");
            using var http = new HttpClient();
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;
            using var stream = await response.Content.ReadAsStreamAsync();
            using var fileStream = new FileStream(cachePath, FileMode.Create, FileAccess.Write, FileShare.None);

            var buffer = new byte[81920];
            long totalRead = 0;
            int read;
            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                await fileStream.WriteAsync(buffer, 0, read);
                totalRead += read;
                if (totalBytes > 0)
                {
                    Console.Write($"\rDownloaded {totalRead} of {totalBytes} bytes ({(totalRead * 100 / totalBytes)}%)");
                }
                else
                {
                    Console.Write($"\rDownloaded {totalRead} bytes");
                }
            }
            Console.WriteLine();
            Console.WriteLine($"OSM data saved to: {cachePath}");
            return cachePath;
        }
    }
}
