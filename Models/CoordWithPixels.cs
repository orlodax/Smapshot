namespace Smapshot.Models;

public class CoordWithPixels(double latitude, double longitude, double pixelX, double pixelY)
{
    public double Latitude { get; set; } = latitude;
    public double Longitude { get; set; } = longitude;
    public double PixelX { get; set; } = pixelX;
    public double PixelY { get; set; } = pixelY;
}
