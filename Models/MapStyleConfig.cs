using System.Text.Json.Serialization;
using SkiaSharp;

namespace Smapshot.Models;

public class MapStyleConfig
{
    public Dictionary<string, RoadStyle> roadStyles { get; set; } = new();
    public WaterStyle waterStyle { get; set; } = new();
    public LabelStyle labelStyle { get; set; } = new();
    public BuildingStyle buildingStyle { get; set; } = new();
    public WaterLabelStyle waterLabelStyle { get; set; } = new();
    public PlaceLabelStyle placeLabelStyle { get; set; } = new();
    public float borderOffset { get; set; } = 10f;
    public string backgroundColor { get; set; } = "#f1efe9";
}

public class RoadStyle
{
    public string color { get; set; } = "#000000"; // Main color
    public float width { get; set; } = 1f; // Main line width
    public string outlineColor { get; set; } = "#888888"; // Outline color

    [JsonIgnore]
    public float outlineWidth // Outline width (should be > main width)
    {
        get
        {
            if (width <= 2f)
                return width + 0.5f;

            return width + 2f;
        }
    }
}

public class WaterStyle { public string color { get; set; } = "#B4DCFF"; }
public class BuildingStyle { public string color { get; set; } = "#D0C9C0"; public string outlineColor { get; set; } = "#A9A295"; }
public class LabelStyle { public int fontSize { get; set; } = 28; public string color { get; set; } = "#000000"; }
public class WaterLabelStyle
{
    public int fontSize { get; set; } = 22;
    public string color { get; set; } = "#3B78A3";
    public string fontStyle { get; set; } = "Italic"; // Normal, Bold, Italic
    public string backgroundColor { get; set; } = "#FFFFFF";
    public int backgroundOpacity { get; set; } = 120; // 0-255
}
public class PlaceLabelStyle
{
    public int fontSize { get; set; } = 30;
    public string color { get; set; } = "#333333";
    public string backgroundColor { get; set; } = "#FFFFFF";
    public int backgroundOpacity { get; set; } = 150; // 0-255
    public string fontStyle { get; set; } = "Italic"; // Normal, Bold, Italic
}
