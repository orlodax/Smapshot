using Smapshot.Helpers;
using Smapshot.Services;

namespace Smapshot;

public static partial class Program
{
    static string? kmlFilePath;
    static string? outputPdfPath;
    static string mapStyle = "standard";

    public static async Task Main(string[] args)
    {
        try
        {
            ParseArgs(args);

            KmlProcessor.LoadKmlFile(kmlFilePath!);

            if (KmlProcessor.Coordinates is null || KmlProcessor.Coordinates.Count == 0)
            {
                Console.WriteLine("Error: No coordinates found in the polygon.");
                return;
            }

            string tempImagePath = await new MapGenerator(KmlProcessor.Coordinates, mapStyle)
                .GenerateMapAsync();

            if (string.IsNullOrEmpty(tempImagePath))
            {
                Console.WriteLine("Error: Could not generate map image");
                return;
            }

            PdfGenerator.Generate(kmlFilePath!, tempImagePath, outputPdfPath!);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
        }
    }

    static void ParseArgs(string[] args)
    {
        // Validate command-line arguments
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: Smapshot <path-to-kml-file> [output-pdf-path] [--style=<map-style>]");
            Console.WriteLine("Example: Smapshot polygon.kml map.pdf --style=topo");
            Console.WriteLine("\nAvailable background map styles:");
            Console.WriteLine("  standard        - Default OpenStreetMap style (default)");
            Console.WriteLine("  cycle           - CyclOSM style with more road details");
            Console.WriteLine("  topo            - OpenTopoMap with elevation contours");
            Console.WriteLine("  carto-light     - OpenStreetMap Carto style");
            Console.WriteLine("  carto-dark      - OpenStreetMap Carto dark style");
            Console.WriteLine("\nAdditional background map styles available with a Geoapify API key:");
            Console.WriteLine("  osm-bright      - Geoapify OSM Bright style");
            Console.WriteLine("  osm-liberty     - Geoapify OSM Liberty style");
            Console.WriteLine("  maptiler-3d     - Geoapify Maptiler 3D style");
            Console.WriteLine("  toner           - Geoapify Toner style (black and white)");
            Console.WriteLine("  positron        - Geoapify Positron style (light)");
            Console.WriteLine("  dark-matter     - Geoapify Dark Matter style");
            Console.WriteLine("  klokantech      - Geoapify Klokantech Basic style");
            Console.WriteLine("  outdoor         - Geoapify Outdoor style");
            Console.WriteLine("  satellite       - Geoapify Satellite imagery");
            Console.WriteLine("  hybrid          - Geoapify Hybrid (satellite + labels)");

            return;
        }

        kmlFilePath = args[0];
        if (kmlFilePath is null || !File.Exists(kmlFilePath))
        {
            Console.WriteLine($"Error: KML file not found at {kmlFilePath}");
            return;
        }

        outputPdfPath = args.Length > 1 && !args[1].StartsWith("--")
            ? args[1]
            : Path.ChangeExtension(kmlFilePath, "pdf");

        foreach (var arg in args)
            if (arg.StartsWith("--style="))
                mapStyle = arg["--style=".Length..].ToLower();

        Console.WriteLine($"Processing KML file: {kmlFilePath}");
        Console.WriteLine($"Output PDF will be created at: {outputPdfPath}");
        Console.WriteLine($"Using map style: {mapStyle}");
    }
}