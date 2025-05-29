# Smapshot

A .NET console application that generates PDF maps from KML territory files using OpenStreetMap data.

## Overview

Smapshot is a mapping application that takes KML (Keyhole Markup Language) files as input and generates high-quality PDF maps. The application downloads OpenStreetMap data for the specified territories, renders them with customizable styling, and outputs professional PDF documents suitable for territory management and planning.

## Features

- **KML File Processing**: Parse KML files to extract territory boundaries and geographic data
- **OpenStreetMap Integration**: Automatically download and cache OSM data for specified regions
- **Custom Map Rendering**: Render maps with configurable road styles, labels, and visual elements
- **PDF Generation**: Create professional PDF documents with territory maps
- **Configurable Styling**: Customize road colors, widths, labels, and other visual elements
- **Batch Processing**: Process multiple KML files or entire directories

## Requirements

- .NET 9.0 or later
- Windows operating system
- Internet connection (for downloading OpenStreetMap data)

## Installation

1. Clone or download the repository
2. Ensure you have .NET 9.0 SDK installed
3. Build the application:

   ```powershell
   dotnet build
   ```

## Usage

### Basic Usage

**You can just drag the .kml file you want to process on top of the executable file.**

### Terminal

```powershell
# Process a specific KML file
Smapshot.exe "file.kml"

# Process all KML files in a folder
Smapshot.exe "path_to_folder"
```

The application will:

1. Parse the KML file(s)
2. Download required OpenStreetMap data
3. Render the map with territories highlighted
4. Generate a PDF file with the same name as the KML file

## Configuration

The application uses `appSettings.json` for styling and configuration. If the file doesn't exist, it will be created with default settings.

### Road Styles

Configure different road types with custom colors, widths, and outline colors:

```json
{
  "roadStyles": {
    "motorway": {
      "color": "#FF4500",
      "width": 16,
      "outlineColor": "#B22222"
    },
    "primary": {
      "color": "#FFFF00",
      "width": 16,
      "outlineColor": "#CCCC00"
    }
  }
}
```

### Label Styles

Customize text labels for roads, places, and water features:

```json
{
  "labelStyle": {
    "fontSize": 28,
    "color": "#000000"
  },
  "placeLabelStyle": {
    "fontSize": 40,
    "color": "#333333",
    "backgroundColor": "#FFFFFF",
    "backgroundOpacity": 150,
    "fontStyle": "Italic"
  }
}
```

### Other Settings

- `waterStyle`: Configure water body appearance
- `buildingStyle`: Configure building rendering
- `backgroundColor`: Set the map background color
- `borderOffset`: Adjust territory border rendering

## Output

For each processed KML file, the application generates:

- A PDF file with the same name as the input KML file
- Cached OSM data in the `cache` directory

## Dependencies

The application uses several key libraries:

- **OsmSharp**: OpenStreetMap data processing
- **QuestPDF**: PDF document generation
- **SharpKml.Core**: KML file parsing
- **SixLabors.ImageSharp**: Image processing and drawing
- **SkiaSharp**: Graphics rendering
- **HtmlAgilityPack**: HTML processing

## Project Structure

```
Smapshot/
├── Models/               # Data models and configuration
│   ├── AppSettings.cs    # Application settings model
│   ├── BoundingBoxGeo.cs # Geographic bounding box
│   └── CoordWithPixels.cs# Coordinate conversion
├── Services/             # Core application services
│   ├── KmlService.cs     # KML file processing
│   ├── OsmDownloader.cs  # OpenStreetMap data download
│   ├── OsmRenderEngine.cs# Map rendering engine
│   ├── PdfGenerator.cs   # PDF document creation
│   ├── LabelUtilities.cs # Text label utilities
│   └── SmapshotManager.cs# Main workflow coordinator
├── cache/                # Cached OSM data
├── appSettings.json      # Configuration file
└── Program.cs            # Application entry point
```

## Error Handling

The application includes error handling for:

- Missing or invalid KML files
- Network connectivity issues
- Invalid configuration files
- File system access problems

If `appSettings.json` is missing or invalid, the application will create a new file with default settings.

## Performance

- OSM data is cached locally to avoid repeated downloads
- Batch processing is optimized for multiple files

## Contributing

Feel free to submit issues, feature requests, or pull requests to improve the application.
