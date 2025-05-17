using System.Xml;
using SharpKml.Dom;
using SharpKml.Engine;

namespace Smapshot.Helpers;

public static class KmlProcessor
{
    static KmlFile? kmlFile = null;
    static Polygon? polygon = null;
    static public CoordinateCollection? Coordinates { get; set; } = null;

    public static void LoadKmlFile(string kmlFilePath)
    {
        // Parse KML file
        if (!File.Exists(kmlFilePath))
        {
            Console.WriteLine($"Error: KML file not found at {kmlFilePath}");
        }

        // Read and parse the KML file
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
                XmlDocument xmlDoc = new();
                xmlDoc.Load(kmlFilePath);

                // Check for default KML namespaces
                XmlNamespaceManager nsmgr = new(xmlDoc.NameTable);
                nsmgr.AddNamespace("kml", "http://www.opengis.net/kml/2.2");

                // Save the file with explicit namespace
                string tempKmlPath = Path.Combine(Path.GetTempPath(), $"fixed_kml_{Guid.NewGuid()}.kml");
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
            }
        }

        if (kmlFile is null)
        {
            Console.WriteLine("Error: KML file not found or invalid.");
            return;
        }

        polygon = kmlFile.Root.Flatten()
            .OfType<Polygon>()
            .FirstOrDefault();

        if (polygon is null)
        {
            Console.WriteLine("Error: No Polygon found in the KML file.");
            return;
        }

        Coordinates = polygon.OuterBoundary?.LinearRing?.Coordinates;
    }
}
