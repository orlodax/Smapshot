using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Smapshot.Helpers;

public static class PdfGenerator
{
    public static void Generate(string kmlFilePath, byte[] mapImage)
    {
        // Register QuestPDF license (community edition)
        QuestPDF.Settings.License = LicenseType.Community;

        // Get KML file name for header
        string kmlFileName = Path.GetFileName(kmlFilePath);
        string outputPdfPath = Path.ChangeExtension(kmlFilePath, "pdf");

        // Create PDF document
        Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(10); // Reduced margin to maximize map area

                page.Header().Height(50).Element(header => // Fixed header height
                {
                    header.Row(row =>
                    {
                        row.RelativeItem().Column(col =>
                        {
                            col.Item().Text($"Map from KML: {kmlFileName}")
                                .FontSize(14).Bold();

                            col.Item().Text($"Generated: {DateTime.Now:g}")
                                        .FontSize(8);
                        });
                    });
                });
                // Maximize the content area by giving it all available space
                page.Content().Element(content =>
                {
                    content.AlignMiddle().AlignCenter().Image(mapImage)
                        .FitArea()
                        .WithCompressionQuality(ImageCompressionQuality.VeryHigh); // High quality for PDF image
                });
            });
        })
        .GeneratePdf(outputPdfPath);

        Console.WriteLine($"Successfully created PDF at: {outputPdfPath}");
    }
}
