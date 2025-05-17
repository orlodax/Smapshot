namespace Smapshot.Models;

public class BoundingBoxGeo(double north, double south, double east, double west)
{
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
}