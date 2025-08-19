[![.NET](https://github.com/MalekBael/FFXIV-LGB-Parser/actions/workflows/dotnet.yml/badge.svg)](https://github.com/MalekBael/FFXIV-LGB-Parser/actions/workflows/dotnet.yml)

# LGB Parser

A tool for parsing and extracting data from Final Fantasy XIV (FFXIV) LGB files. Extract game world data including background objects, sound zones, visual effects, and more into readable text or JSON format.

## Features

- **Direct FFXIV Integration** - Parse files directly from game installation
- **Batch Processing** - Process multiple files or entire zones
- **Multiple Formats** - Export as text or JSON
- **Zone/Type Filtering** - Target specific areas or file types

## Quick Start

### Prerequisites
- .NET 8.0 or later
- Final Fantasy XIV installation

### Installation
git clone https://github.com/yourusername/LGB-Parser.git cd LGB-Parser dotnet build --configuration Release


## Usage

### Basic Commands

**List available files:**
lgb-parser --game "C:\Program Files (x86)\SquareEnix\FINAL FANTASY XIV - A Realm Reborn" --list

**Parse all files:**
lgb-parser --game "C:\FFXIV" --batch my_output json

**Parse specific zone (e.g., Limsa Lominsa):**
lgb-parser --game "C:\FFXIV" --batch-zone sea_s1 limsa_files json


## Common Issues

**"FFXIV sqpack directory not found"**
- Ensure your game path points to the root FFXIV installation directory


## Contributing

Contributions welcome! 

## Acknowledgments

- **Lumina** - For FFXIV file format support