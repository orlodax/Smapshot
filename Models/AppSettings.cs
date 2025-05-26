using System.Text.Json.Serialization;

namespace Smapshot.Models;

public class AppSettings
{
    private static AppSettings instance = new();

    // Public accessor for the singleton instance
    public static AppSettings Instance => instance;

    // Method to update the singleton instance
    public static void UpdateInstance(AppSettings settings)
    {
        instance = settings ?? new AppSettings();
    }

    // Instance properties that will be used throughout the application
    public Dictionary<string, RoadStyle> RoadStyles { get; set; } = new()
    {
        { "motorway", new RoadStyle { Color = "#FF4500", Width = 16f, OutlineColor = "#B22222" } },
        { "trunk", new RoadStyle { Color = "#FFA500", Width = 16f, OutlineColor = "#B8860B" } },
        { "primary", new RoadStyle { Color = "#FFFF00", Width = 16f, OutlineColor = "#CCCC00" } },
        { "secondary", new RoadStyle { Color = "#FFFFE0", Width = 14f, OutlineColor = "#BDB76B" } },
        { "tertiary", new RoadStyle { Color = "#FFFFFF", Width = 12f, OutlineColor = "#BCBCBC" } },
        { "residential", new RoadStyle { Color = "#FFFFFF", Width = 10f, OutlineColor = "#BCBCBC" } },
        { "service", new RoadStyle { Color = "#FFFFFF", Width = 8f, OutlineColor = "#BCBCBC" } },
        { "unclassified", new RoadStyle { Color = "#FFFFFF", Width = 8f, OutlineColor = "#BCBCBC" } },
        { "default", new RoadStyle { Color = "#FFFFFF", Width = 8f, OutlineColor = "#BCBCBC" } }
    };
    public WaterStyle WaterStyle { get; set; } = new() { Color = "#B4DCFF" };
    public LabelStyle LabelStyle { get; set; } = new() { FontSize = 28, Color = "#000000" };
    public BuildingStyle BuildingStyle { get; set; } = new() { Color = "#C0B9B0", OutlineColor = "#999285" };
    public WaterLabelStyle WaterLabelStyle { get; set; } = new()
    {
        FontSize = 22,
        Color = "#3B78A3",
        FontStyle = "Italic",
        BackgroundColor = "#FFFFFF",
        BackgroundOpacity = 120
    };
    public PlaceLabelStyle PlaceLabelStyle { get; set; } = new()
    {
        FontSize = 30,
        Color = "#333333",
        BackgroundColor = "#FFFFFF",
        BackgroundOpacity = 150,
        FontStyle = "Italic"
    };
    public float BorderOffset { get; set; } = 10f;
    public string BackgroundColor { get; set; } = "#f1efe9";
}

public class RoadStyle
{
    public string Color { get; set; } = "#000000"; // Main color
    public float Width { get; set; } = 1f; // Main line width
    public string OutlineColor { get; set; } = "#888888"; // Outline color

    [JsonIgnore]
    public float OutlineWidth // Outline width (should be > main width)
    {
        get
        {
            if (Width <= 2f)
                return Width + 0.5f;

            return Width + 2f;
        }
    }
}

public class WaterStyle
{
    public string Color { get; set; } = "#B4DCFF";
}

public class BuildingStyle
{
    public string Color { get; set; } = "#C0B9B0";
    public string OutlineColor { get; set; } = "#999285";
}

public class LabelStyle
{
    public int FontSize { get; set; } = 28;
    public string Color { get; set; } = "#000000";
}

public class WaterLabelStyle
{
    public int FontSize { get; set; } = 22;
    public string Color { get; set; } = "#3B78A3";
    public string FontStyle { get; set; } = "Italic"; // Normal, Bold, Italic
    public string BackgroundColor { get; set; } = "#FFFFFF";
    public int BackgroundOpacity { get; set; } = 120; // 0-255
}
public class PlaceLabelStyle
{
    public int FontSize { get; set; } = 30;
    public string Color { get; set; } = "#333333";
    public string BackgroundColor { get; set; } = "#FFFFFF";
    public int BackgroundOpacity { get; set; } = 150; // 0-255
    public string FontStyle { get; set; } = "Italic"; // Normal, Bold, Italic
}
