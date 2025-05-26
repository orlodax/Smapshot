namespace Smapshot.Models;

public class BoundingBoxGeo(double north, double south, double east, double west)
{
    static readonly AppSettings appSettings = AppSettings.Instance;

    public double North { get; set; } = north;
    public double South { get; set; } = south;
    public double East { get; set; } = east;
    public double West { get; set; } = west;

    public double Width => East - West;
    public double Height => North - South;


    public BoundingBoxGeo Pad(double padding)
    {
        double latPadding = Height * padding;
        double lonPadding = Width * padding;

        return new BoundingBoxGeo(
            North + latPadding,
            South - latPadding,
            East + lonPadding,
            West - lonPadding
        );
    }

    /// <summary>
    /// Expands the bounding box by a percentage of its width and height, according to which side is larger.
    /// </summary>
    public BoundingBoxGeo GetExpandedBoundingBox()
    {
        double higherDimension = Math.Max(Width, Height);
        double padding = appSettings.MapContextExtensionCoefficient * higherDimension;
        return new BoundingBoxGeo(
            North + padding,
            South - padding,
            East + padding,
            West - padding
        );
    }
}