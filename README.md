
[![.NET](https://github.com/MalekBael/FFXIV-LGB-Parser/actions/workflows/dotnet.yml/badge.svg)](https://github.com/MalekBael/FFXIV-LGB-Parser/actions/workflows/dotnet.yml)

 
 # LGB Parser

A tool for parsing and extracting data from Final Fantasy XIV (FFXIV) LGB files. This tool can work with both extracted LGB files and directly with FFXIV game installations using the Lumina library.

This tool extracts this information into human-readable text or JSON format, making it useful for game research, modding, or data analysis.

## Features

- ✅ **Direct Game Integration** - Parse files directly from FFXIV installation
- ✅ **Batch Processing** - Process multiple of LGB files at once
- ✅ **Zone-Based Filtering** - Target specific game zones or areas
- ✅ **File Type Filtering** - Process only specific types (bg, sound, vfx, etc.)
- ✅ **Multiple Output Formats** - Export as text or JSON
- ✅ **Folder Structure Preservation** - Maintains original game directory structure


## Prerequisites

- **.NET 8.0** or later
- **Final Fantasy XIV** installation (for game mode)

## Installation & Building

1. Clone the repository:
```bash
git clone https://github.com/yourusername/LGB-Parser.git
cd LGB-Parser
```

2. Build the project:
```bash
dotnet build --configuration Release
```

3. Run the tool:
```bash
dotnet run -- [arguments]
```

Or build as executable:
```bash
dotnet publish -c Release -r win-x64 --self-contained
```

## Usage

The LGB Parser operates in two main modes:

### File Mode
Work with pre-extracted LGB files from your file system.

### Game Mode
Parse files directly from your FFXIV game installation using `--game` flag.

## Command Reference

### File Mode Commands

#### Parse Single File
```bash
lgb-parser <input.lgb> <output.txt> [format]
```
**Example:**
```bash
lgb-parser bg.lgb output.txt
lgb-parser bg.lgb output.json json
```

#### Parse Folder (Batch)
```bash
lgb-parser <input_folder> [output_folder] [format]
```
**Example:**
```bash
lgb-parser C:\extracted_lgb_files parsed_output
lgb-parser C:\extracted_lgb_files parsed_output json
```

### Game Mode Commands

#### List Available Files
```bash
lgb-parser --game <game_path> --list
```
**Example:**
```bash
lgb-parser --game "C:\Program Files (x86)\SquareEnix\FINAL FANTASY XIV - A Realm Reborn" --list
```

#### List Available Zones
```bash
lgb-parser --game <game_path> --list-zones
```
Shows all available zones with file counts.

#### List Available File Types
```bash
lgb-parser --game <game_path> --list-types
```
Shows all LGB file types (bg, sound, vfx, etc.) with counts.

#### Parse All LGB Files
```bash
lgb-parser --game <game_path> --batch [output_folder]
```
**Example:**
```bash
lgb-parser --game "C:\Program Files (x86)\SquareEnix\FINAL FANTASY XIV - A Realm Reborn" --batch my_output
```

#### Parse Files from Specific Zone
```bash
lgb-parser --game <game_path> --batch-zone <zone> [output_folder]
```
**Example:**
```bash
lgb-parser --game "C:\Program Files (x86)\SquareEnix\FINAL FANTASY XIV - A Realm Reborn" --batch-zone sea_s1 limsa_files
```

#### Parse Files of Specific Type
```bash
lgb-parser --game <game_path> --batch-type <type> [output_folder]
```
**Example:**
```bash
lgb-parser --game "C:\Program Files (x86)\SquareEnix\FINAL FANTASY XIV - A Realm Reborn" --batch-type bg background_files
```

#### Parse Single File from Game
```bash
lgb-parser --game <game_path> <lgb_file_path> [output] [format]
```
**Example:**
```bash
lgb-parser --game "C:\Program Files (x86)\SquareEnix\FINAL FANTASY XIV - A Realm Reborn" bg/ffxiv/sea_s1/fld/s1f1/level/bg.lgb limsa_field.txt
```

## Supported File Types

| Type | Description | Common Issues |
|------|-------------|---------------|
| `bg` | Background objects, terrain, props | ✅ Stable |
| `planevent` | Event triggers and interactions | ✅ Stable |
| `planmap` | Map/navigation data | ✅ Stable |
| `planlive` | Dynamic/live event data | ✅ Stable |
| `sound` | Audio zones and sound triggers | ✅ Stable |
| `vfx` | Visual effects placement | ✅ Stable |
| `planner` | Layout planning data | ⚠️ May have parsing issues |

## Output Structure

### Text Format
```
LGB File: bg/ffxiv/sea_s1/fld/s1f1/level/bg.lgb
================================================================================

Header Information:
- File ID: LGB1
- Total Chunks: 1
- Layers: 3

Layer 1: Background Objects (ID: 1)
  Instance Objects: 245
  
  Object 1: Large_Building_01
  - Type: BG
  - Position: (100.5, 25.0, -50.2)
  - Rotation: (0.0, 45.0, 0.0)
  - Scale: (1.0, 1.0, 1.0)
  - Asset Path: bg/common/building/large_01.mdl
```

### JSON Format
```json
{
  "FilePath": "bg/ffxiv/sea_s1/fld/s1f1/level/bg.lgb",
  "Header": {
    "FileID": "LGB1",
    "TotalChunkCount": 1
  },
  "Layers": [
    {
      "LayerId": 1,
      "Name": "Background Objects",
      "InstanceObjects": [
        {
          "InstanceId": 1,
          "Name": "Large_Building_01",
          "AssetType": "BG",
          "Transform": {
            "Translation": [100.5, 25.0, -50.2],
            "Rotation": [0.0, 45.0, 0.0, 0.0],
            "Scale": [1.0, 1.0, 1.0]
          },
          "ObjectData": {
            "AssetPath": "bg/common/building/large_01.mdl",
            "IsVisible": true,
            "RenderShadowEnabled": true
          }
        }
      ]
    }
  ]
}
```

## Folder Structure Preservation

When using batch commands, the tool preserves the original FFXIV folder structure:

```
output_folder/
├── bg/
│   └── ffxiv/
│       ├── sea_s1/
│       │   ├── fld/
│       │   │   └── s1f1/
│       │   │       └── level/
│       │   │           ├── bg.txt
│       │   │           ├── sound.txt
│       │   │           └── vfx.txt
│       │   └── twn/
│       │       └── s1t1/
│       │           └── level/
│       │               └── bg.txt
│       └── wil_w1/
│           └── fld/
│               └── w1f1/
│                   └── level/
│                       └── bg.txt
```

## Common Usage Examples

### Research All Background Objects in Limsa Lominsa
```bash
lgb-parser --game "C:\Program Files (x86)\SquareEnix\FINAL FANTASY XIV - A Realm Reborn" --batch-zone sea_s1 limsa_research
```

### Extract All Sound Files as JSON
```bash
lgb-parser --game "C:\Program Files (x86)\SquareEnix\FINAL FANTASY XIV - A Realm Reborn" --batch-type sound sound_data
# Then convert to JSON format by modifying the exporter
```

### Quick File Discovery
```bash
# See what zones are available
lgb-parser --game "C:\Program Files (x86)\SquareEnix\FINAL FANTASY XIV - A Realm Reborn" --list-zones

# See what file types exist
lgb-parser --game "C:\Program Files (x86)\SquareEnix\FINAL FANTASY XIV - A Realm Reborn" --list-types

# Get a sample of available files
lgb-parser --game "C:\Program Files (x86)\SquareEnix\FINAL FANTASY XIV - A Realm Reborn" --list
```

## Performance Considerations

- **Large batch operations** can take several minutes to complete
- **Memory usage** scales with the number of files processed simultaneously

## Troubleshooting

### Common Errors

#### "FFXIV sqpack directory not found"
**Solution:** Ensure your game path points to the root FFXIV installation directory (contains the `game` folder).

### Getting Help

1. **Check file existence:**
   ```bash
   lgb-parser --game "your_game_path" --list
   ```

2. **Test with a single file first:**
   ```bash
   lgb-parser --game "your_game_path" bg/ffxiv/sea_s1/fld/s1f1/level/bg.lgb test_output.txt
   ```

3. **Use zone-specific commands for better performance:**
   ```bash
   lgb-parser --game "your_game_path" --batch-zone sea_s1
   ```

## Contributing

Contributions are welcome! Areas for improvement:
- Support for additional LGB file types
- Enhanced object data parsing
- Export format options (XML, CSV)
- GUI interface
- Performance optimizations

## Acknowledgments

- **Lumina** - For FFXIV file format support


---
