using System.Numerics;
using System.Xml;
using SharpKml.Dom;
using SharpKml.Engine;

namespace Smapshot.Services;

internal class KmlService(string kmlFilePath)
{
    internal CoordinateCollection? Coordinates { get; private set; }

    internal void ParsePolygonCoordinates()
    {
        ArgumentNullException.ThrowIfNull(kmlFilePath, nameof(kmlFilePath));

        KmlFile? kmlFile = null;

        try
        {
            using FileStream fs = File.OpenRead(kmlFilePath);
            kmlFile = KmlFile.Load(fs);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing KML file: {ex.Message}");
            Console.WriteLine("Attempting to parse KML file using XmlDocument instead...");

            try
            {
                // Alternative parsing method
                XmlDocument xmlDoc = new();
                xmlDoc.Load(kmlFilePath);

                // Check for default KML namespaces
                XmlNamespaceManager namespaceManager = new(xmlDoc.NameTable);
                namespaceManager.AddNamespace("kml", "http://www.opengis.net/kml/2.2");

                // Save the file with explicit namespace
                string tempKmlPath = Path.Combine(Path.GetTempPath(), $"fixed_kml_{Guid.NewGuid()}.kml");
                xmlDoc.Save(tempKmlPath);

                // Try parsing again
                using (FileStream fs = File.OpenRead(tempKmlPath))
                {
                    kmlFile = KmlFile.Load(fs);
                }

                // Clean up temp file
                File.Delete(tempKmlPath);
            }
            catch (Exception innerEx)
            {
                throw new Exception($"Error during alternative KML parsing: {innerEx.Message}", innerEx);
            }
        }

        ArgumentNullException.ThrowIfNull(kmlFile, nameof(kmlFile));

        Polygon? polygon = kmlFile.Root.Flatten()
            .OfType<Polygon>()
            .FirstOrDefault();

        ArgumentNullException.ThrowIfNull(polygon, nameof(polygon));

        Coordinates = polygon.OuterBoundary.LinearRing.Coordinates;
    }

    internal double GetOptimalRotationAngle()
    {
        ArgumentNullException.ThrowIfNull(Coordinates, nameof(Coordinates));

        // Convert coordinates to Vector2 array for easier processing
        Vector2[] points = [.. Coordinates.Select(c => new Vector2((float)c.Longitude, (float)c.Latitude))];

        // Find the convex hull of the points to simplify calculations
        Vector2[] hull = ComputeConvexHull(points);

        // Calculate the minimum area bounding rectangle
        (double angle, double width, double height) = CalculateMinAreaBoundingRectangle(hull);

        double targetRatio = 2500.0 / 3250.0; // Target aspect ratio (portrait)

        // Decide if we need to rotate by 90 degrees
        double currentRatio = width / height;
        double currentRatioInverse = height / width;

        // Check which orientation (original or 90-degree rotated) better matches our target ratio
        bool shouldRotate90 = Math.Abs(currentRatioInverse - targetRatio) < Math.Abs(currentRatio - targetRatio);

        // Adjust angle if needed
        if (shouldRotate90)
        {
            angle += 90;
            (width, height) = (height, width);
        }

        Console.WriteLine($"Polygon rotation angle: {angle:F2}°");
        return angle;
    }

    static Vector2[] ComputeConvexHull(Vector2[] points)
    {
        if (points.Length <= 3) return points;

        // Implementation of the Graham scan algorithm for convex hull

        // Find the point with the lowest y-coordinate (and leftmost if tied)
        int lowestIndex = 0;
        for (int i = 1; i < points.Length; i++)
        {
            if (points[i].Y < points[lowestIndex].Y ||
                (points[i].Y == points[lowestIndex].Y && points[i].X < points[lowestIndex].X))
            {
                lowestIndex = i;
            }
        }

        // Swap the lowest point to the first position
        (points[0], points[lowestIndex]) = (points[lowestIndex], points[0]);

        // Sort points based on polar angle with respect to the lowest point
        Vector2 pivot = points[0];
        Array.Sort(points, 1, points.Length - 1, new PolarAngleComparer(pivot));

        // Build convex hull
        var hull = new Stack<Vector2>();
        hull.Push(points[0]);
        hull.Push(points[1]);

        for (int i = 2; i < points.Length; i++)
        {
            while (hull.Count > 1)
            {
                Vector2 p2 = hull.Pop();
                Vector2 p1 = hull.Peek();
                hull.Push(p2);

                // Check if we make a non-left turn
                if (Cross(p1, p2, points[i]) >= 0)
                {
                    hull.Pop(); // Remove p2
                }
                else
                {
                    break;
                }
            }

            hull.Push(points[i]);
        }

        return [.. hull.Reverse()];
    }

    static float Cross(Vector2 o, Vector2 a, Vector2 b)
    {
        return (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
    }

    class PolarAngleComparer(Vector2 pivot) : IComparer<Vector2>
    {
        private Vector2 _pivot = pivot;

        public int Compare(Vector2 a, Vector2 b)
        {
            float crossProduct = Cross(_pivot, a, b);
            if (crossProduct == 0)
            {
                // If collinear, sort by distance from pivot
                float distA = Vector2.DistanceSquared(_pivot, a);
                float distB = Vector2.DistanceSquared(_pivot, b);
                return distA.CompareTo(distB);
            }
            return -crossProduct.CompareTo(0); // Counterclockwise order
        }
    }

    static (double angle, double width, double height) CalculateMinAreaBoundingRectangle(Vector2[] hull)
    {
        if (hull.Length <= 1)
            return (0, 0, 0);

        double minArea = double.MaxValue;
        double bestAngle = 0;
        double bestWidth = 0;
        double bestHeight = 0;

        // For each edge of the convex hull, compute the bounding box
        for (int i = 0; i < hull.Length; i++)
        {
            int j = (i + 1) % hull.Length;

            // Calculate the edge direction
            Vector2 edge = hull[j] - hull[i];
            double edgeLength = edge.Length();

            if (edgeLength < 0.00001)
                continue;

            // Calculate the edge angle
            double angle = Math.Atan2(edge.Y, edge.X);

            // Normalize and rotate the points
            double cos = Math.Cos(-angle);
            double sin = Math.Sin(-angle);

            double minX = double.MaxValue;
            double maxX = double.MinValue;
            double minY = double.MaxValue;
            double maxY = double.MinValue;

            // Calculate bounding box in the rotated coordinate system
            foreach (var point in hull)
            {
                double rotatedX = point.X * cos - point.Y * sin;
                double rotatedY = point.X * sin + point.Y * cos;

                minX = Math.Min(minX, rotatedX);
                maxX = Math.Max(maxX, rotatedX);
                minY = Math.Min(minY, rotatedY);
                maxY = Math.Max(maxY, rotatedY);
            }

            double width = maxX - minX;
            double height = maxY - minY;
            double area = width * height;

            if (area < minArea)
            {
                minArea = area;
                bestAngle = angle * (180.0 / Math.PI); // Convert to degrees
                bestWidth = width;
                bestHeight = height;
            }
        }

        return (bestAngle, bestWidth, bestHeight);
    }
}
