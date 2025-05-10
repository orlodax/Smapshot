// Helper class to store coordinate information with pixel position
public class CoordWithPixels
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double PixelX { get; set; }
    public double PixelY { get; set; }

    public CoordWithPixels(double latitude, double longitude, double pixelX, double pixelY)
    {
        Latitude = latitude;
        Longitude = longitude;
        PixelX = pixelX;
        PixelY = pixelY;
    }
}
