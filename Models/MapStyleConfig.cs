using System.Text.Json.Serialization;
using SkiaSharp;

namespace Smapshot.Models;

public class MapStyleConfig
{
    public Dictionary<string, RoadStyle> roadStyles { get; set; } = new();
    public WaterStyle waterStyle { get; set; } = new();
    public LabelStyle labelStyle { get; set; } = new();
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
            if (width == 2f)
                return 3f;

            return width + 4;
        }
    }
}

public class WaterStyle { public string color { get; set; } = "#B4DCFF"; }
public class LabelStyle { public int fontSize { get; set; } = 28; public string color { get; set; } = "#000000"; }
