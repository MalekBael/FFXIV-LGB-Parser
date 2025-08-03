using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using LgbParser;

namespace LgbParser
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("LGB Parser Tool v2.0");
            Console.WriteLine("====================");
            Console.WriteLine($"Current Date/Time: 2025-08-02 01:57:55 UTC");
            Console.WriteLine($"User: MalekBael");
            Console.WriteLine();

            if (args.Length == 0)
            {
                ShowHelp();
                return;
            }

            try
            {
                // Check if this is game mode (direct FFXIV installation parsing)
                if (args.Contains("--game"))
                {
                    HandleGameMode(args);
                }
                else
                {
                    HandleFileMode(args);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine("\nFor help, run without arguments or use --help");
                Environment.Exit(1);
            }
        }

        static void ShowHelp()
        {
            Console.WriteLine("LGB Parser - Extract data from Final Fantasy XIV LGB files");
            Console.WriteLine();
            Console.WriteLine("FILE MODE (Pre-extracted LGB files):");
            Console.WriteLine("  lgb-parser <input.lgb> <output.txt> [format]              - Parse single file");
            Console.WriteLine("  lgb-parser <input_folder> [output_folder] [format]       - Parse folder (batch)");
            Console.WriteLine();
            Console.WriteLine("GAME MODE (Direct FFXIV installation):");
            Console.WriteLine("  lgb-parser --game <game_path> --list                     - List available LGB files");
            Console.WriteLine("  lgb-parser --game <game_path> --list-zones               - List available zones");
            Console.WriteLine("  lgb-parser --game <game_path> --list-types               - List available file types");
            Console.WriteLine("  lgb-parser --game <game_path> --batch [output_folder]    - Parse all LGB files");
            Console.WriteLine("  lgb-parser --game <game_path> --batch-zone <zone> [output_folder] - Parse zone files");
            Console.WriteLine("  lgb-parser --game <game_path> --batch-type <type> [output_folder] - Parse type files");
            Console.WriteLine("  lgb-parser --game <game_path> --analyze <lgb_file_path>  - Detailed analysis of specific file");
            Console.WriteLine("  lgb-parser --game <game_path> <lgb_file_path> [output] [format] - Parse single game file");
            Console.WriteLine();
            Console.WriteLine("FORMATS:");
            Console.WriteLine("  text (default) - Human-readable text format");
            Console.WriteLine("  json          - JSON format");
            Console.WriteLine();
            Console.WriteLine("EXAMPLES:");
            Console.WriteLine("  File Mode:");
            Console.WriteLine("    lgb-parser bg.lgb output.txt");
            Console.WriteLine("    lgb-parser extracted_files/ parsed_output/ json");
            Console.WriteLine();
            Console.WriteLine("  Game Mode:");
            Console.WriteLine("    lgb-parser --game \"C:\\Program Files (x86)\\SquareEnix\\FINAL FANTASY XIV - A Realm Reborn\" --list");
            Console.WriteLine("    lgb-parser --game \"C:\\FFXIV\" --batch-zone sea_s1 limsa_files");
            Console.WriteLine("    lgb-parser --game \"C:\\FFXIV\" --batch-type planner planner_analysis");
            Console.WriteLine("    lgb-parser --game \"C:\\FFXIV\" --analyze bg/ffxiv/sea_s1/fld/s1f1/level/planner.lgb");
            Console.WriteLine();
            Console.WriteLine("SUPPORTED ZONES: air_a1, fst_f1, lak_l1, ocn_o1, roc_r1, sea_s1, wil_w1, zon_z1");
            Console.WriteLine("SUPPORTED TYPES: bg, planevent, planlive, planmap, planner, sound, vfx");
            Console.WriteLine();
            Console.WriteLine("NOTE: planner.lgb files use enhanced parsing due to compatibility issues");
        }

        static void HandleGameMode(string[] args)
        {
            var gameIndex = Array.IndexOf(args, "--game");
            if (gameIndex == -1 || gameIndex >= args.Length - 1)
            {
                Console.WriteLine("Error: --game requires a path to FFXIV installation");
                return;
            }

            string gamePath = args[gameIndex + 1];

            // Validate game path
            if (!Directory.Exists(gamePath))
            {
                Console.WriteLine($"Error: Game path does not exist: {gamePath}");
                return;
            }

            var sqpackPath = Path.Combine(gamePath, "game", "sqpack");
            if (!Directory.Exists(sqpackPath))
            {
                Console.WriteLine($"Error: FFXIV sqpack directory not found at: {sqpackPath}");
                Console.WriteLine("Please ensure the path points to the root FFXIV installation directory.");
                return;
            }

            Console.WriteLine($"Using FFXIV installation: {gamePath}");
            Console.WriteLine($"SqPack directory: {sqpackPath}");
            Console.WriteLine();

            using var reader = new GameLgbReader(gamePath);

            // Handle different game mode commands
            if (args.Contains("--list"))
            {
                HandleListCommand(reader);
            }
            else if (args.Contains("--list-zones"))
            {
                HandleListZonesCommand(reader);
            }
            else if (args.Contains("--list-types"))
            {
                HandleListTypesCommand(reader);
            }
            else if (args.Contains("--batch"))
            {
                HandleBatchCommand(reader, args);
            }
            else if (args.Contains("--batch-zone"))
            {
                HandleBatchZoneCommand(reader, args);
            }
            else if (args.Contains("--batch-type"))
            {
                HandleBatchTypeCommand(reader, args);
            }
            else if (args.Contains("--analyze"))
            {
                HandleAnalyzeCommand(reader, args);
            }
            else
            {
                // Single file parsing from game
                HandleSingleGameFileCommand(reader, args, gameIndex);
            }
        }

        static void HandleListCommand(GameLgbReader reader)
        {
            Console.WriteLine("Discovering available LGB files...");
            var files = reader.GetAvailableLgbFiles();

            Console.WriteLine($"\nFound {files.Count} LGB files:");
            Console.WriteLine("================================================================================");

            var groupedFiles = files.GroupBy(f => {
                var parts = f.Split('/');
                return parts.Length >= 3 ? parts[2] : "Unknown"; // Zone
            }).OrderBy(g => g.Key);

            foreach (var group in groupedFiles)
            {
                Console.WriteLine($"\n{group.Key.ToUpperInvariant()} ({group.Count()} files):");
                foreach (var file in group.Take(10)) // Show first 10 per zone
                {
                    Console.WriteLine($"  {file}");
                }
                if (group.Count() > 10)
                {
                    Console.WriteLine($"  ... and {group.Count() - 10} more");
                }
            }
        }

        static void HandleListZonesCommand(GameLgbReader reader)
        {
            Console.WriteLine("Analyzing available zones...");
            var files = reader.GetAvailableLgbFiles();

            var zoneStats = files
                .GroupBy(f => {
                    var parts = f.Split('/');
                    return parts.Length >= 3 ? parts[2] : "Unknown";
                })
                .Select(g => new { Zone = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ToList();

            Console.WriteLine($"\nAvailable Zones ({zoneStats.Count} total):");
            Console.WriteLine("================================================================================");
            Console.WriteLine($"{"Zone",-15} {"Files",-10} {"Description"}");
            Console.WriteLine(new string('-', 60));

            var zoneDescriptions = new Dictionary<string, string>
            {
                ["air_a1"] = "The Mist (Airship zones)",
                ["fst_f1"] = "East Shroud / Gridania areas",
                ["lak_l1"] = "Mor Dhona / Lake areas",
                ["ocn_o1"] = "Ocean/Island zones",
                ["roc_r1"] = "Coerthas / Rocky areas",
                ["sea_s1"] = "La Noscea / Limsa Lominsa",
                ["wil_w1"] = "Black Shroud / Forest areas",
                ["zon_z1"] = "Thanalan / Desert zones"
            };

            foreach (var zone in zoneStats)
            {
                var description = zoneDescriptions.ContainsKey(zone.Zone)
                    ? zoneDescriptions[zone.Zone]
                    : "Unknown zone";
                Console.WriteLine($"{zone.Zone,-15} {zone.Count,-10} {description}");
            }
        }

        static void HandleListTypesCommand(GameLgbReader reader)
        {
            Console.WriteLine("Analyzing available file types...");
            var files = reader.GetAvailableLgbFiles();

            var typeStats = files
                .GroupBy(f => {
                    var fileName = Path.GetFileNameWithoutExtension(f);
                    return fileName;
                })
                .Select(g => new { Type = g.Key, Count = g.Count() })
                .OrderByDescending(x => x.Count)
                .ToList();

            Console.WriteLine($"\nAvailable File Types ({typeStats.Count} total):");
            Console.WriteLine("================================================================================");
            Console.WriteLine($"{"Type",-15} {"Files",-10} {"Description",-30} {"Status"}");
            Console.WriteLine(new string('-', 80));

            var typeDescriptions = new Dictionary<string, (string desc, string status)>
            {
                ["bg"] = ("Background objects, terrain, props", "✅ Stable"),
                ["planevent"] = ("Event triggers and interactions", "✅ Stable"),
                ["planmap"] = ("Map/navigation data", "✅ Stable"),
                ["planlive"] = ("Dynamic/live event data", "✅ Stable"),
                ["sound"] = ("Audio zones and sound triggers", "✅ Stable"),
                ["vfx"] = ("Visual effects placement", "✅ Stable"),
                ["planner"] = ("Layout planning data", "🔧 Enhanced parsing")
            };

            foreach (var type in typeStats)
            {
                var (description, status) = typeDescriptions.ContainsKey(type.Type)
                    ? typeDescriptions[type.Type]
                    : ("Unknown file type", "❓ Unknown");
                Console.WriteLine($"{type.Type,-15} {type.Count,-10} {description,-30} {status}");
            }
        }

        static void HandleBatchCommand(GameLgbReader reader, string[] args)
        {
            string outputFolder = "lgb_output";

            // Check if output folder is specified
            var batchIndex = Array.IndexOf(args, "--batch");
            if (batchIndex < args.Length - 1 && !args[batchIndex + 1].StartsWith("--"))
            {
                outputFolder = args[batchIndex + 1];
            }

            Console.WriteLine($"Batch processing all LGB files to: {outputFolder}");
            Console.WriteLine("This may take several minutes...");
            Console.WriteLine();

            var results = reader.ParseAllLgbFiles();

            Directory.CreateDirectory(outputFolder);

            int exported = 0;
            foreach (var kvp in results)
            {
                try
                {
                    var relativePath = kvp.Key.Replace('/', Path.DirectorySeparatorChar);
                    var outputPath = Path.Combine(outputFolder, relativePath + ".txt");

                    Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

                    // Use TextExporter correctly - it takes (data, outputPath) and returns void
                    var exporter = new TextExporter();
                    exporter.Export(kvp.Value, outputPath);

                    exported++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to export {kvp.Key}: {ex.Message}");
                }
            }

            Console.WriteLine($"\nBatch processing complete!");
            Console.WriteLine($"Exported {exported} files to: {outputFolder}");
        }

        static void HandleBatchZoneCommand(GameLgbReader reader, string[] args)
        {
            var zoneIndex = Array.IndexOf(args, "--batch-zone");
            if (zoneIndex >= args.Length - 1)
            {
                Console.WriteLine("Error: --batch-zone requires a zone name");
                return;
            }

            string zoneName = args[zoneIndex + 1];
            string outputFolder = $"zone_{zoneName}_output";

            // Check if output folder is specified
            if (zoneIndex < args.Length - 2 && !args[zoneIndex + 2].StartsWith("--"))
            {
                outputFolder = args[zoneIndex + 2];
            }

            Console.WriteLine($"Batch processing zone '{zoneName}' to: {outputFolder}");

            var results = reader.ParseLgbFilesByZone(zoneName);

            Directory.CreateDirectory(outputFolder);

            int exported = 0;
            foreach (var kvp in results)
            {
                try
                {
                    var relativePath = kvp.Key.Replace('/', Path.DirectorySeparatorChar);
                    var outputPath = Path.Combine(outputFolder, relativePath + ".txt");

                    Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

                    var exporter = new TextExporter();
                    exporter.Export(kvp.Value, outputPath);

                    exported++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to export {kvp.Key}: {ex.Message}");
                }
            }

            Console.WriteLine($"\nZone processing complete!");
            Console.WriteLine($"Exported {exported} files to: {outputFolder}");
        }

        static void HandleBatchTypeCommand(GameLgbReader reader, string[] args)
        {
            var typeIndex = Array.IndexOf(args, "--batch-type");
            if (typeIndex >= args.Length - 1)
            {
                Console.WriteLine("Error: --batch-type requires a file type");
                return;
            }

            string fileType = args[typeIndex + 1];
            string outputFolder = $"type_{fileType}_output";

            // Check if output folder is specified
            if (typeIndex < args.Length - 2 && !args[typeIndex + 2].StartsWith("--"))
            {
                outputFolder = args[typeIndex + 2];
            }

            Console.WriteLine($"Batch processing '{fileType}' files to: {outputFolder}");

            var results = reader.ParseLgbFilesByType(fileType);

            Directory.CreateDirectory(outputFolder);

            int exported = 0;
            foreach (var kvp in results)
            {
                try
                {
                    var relativePath = kvp.Key.Replace('/', Path.DirectorySeparatorChar);
                    var outputPath = Path.Combine(outputFolder, relativePath + ".txt");

                    Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

                    var exporter = new TextExporter();
                    exporter.Export(kvp.Value, outputPath);

                    exported++;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to export {kvp.Key}: {ex.Message}");
                }
            }

            Console.WriteLine($"\nType processing complete!");
            Console.WriteLine($"Exported {exported} files to: {outputFolder}");
        }

        static void HandleAnalyzeCommand(GameLgbReader reader, string[] args)
        {
            var analyzeIndex = Array.IndexOf(args, "--analyze");
            if (analyzeIndex >= args.Length - 1)
            {
                Console.WriteLine("Error: --analyze requires an LGB file path");
                return;
            }

            string lgbFilePath = args[analyzeIndex + 1];

            Console.WriteLine($"Detailed Analysis of LGB File");
            Console.WriteLine("================================================================================");
            Console.WriteLine($"File: {lgbFilePath}");
            Console.WriteLine($"Analysis Time: 2025-08-02 01:57:55 UTC");
            Console.WriteLine($"Analyzed By: MalekBael");
            Console.WriteLine();

            try
            {
                var data = reader.ParseLgbFile(lgbFilePath);

                // Save detailed analysis to file first
                var safeFileName = lgbFilePath.Replace('/', '_').Replace('\\', '_');
                var outputPath = $"analysis_{safeFileName}_{DateTime.Now:yyyyMMdd_HHmmss}.txt";

                // Create detailed analysis content
                var detailedAnalysis = $"LGB File Detailed Analysis\n" +
                                     $"=========================\n" +
                                     $"File: {lgbFilePath}\n" +
                                     $"Analysis Time: 2025-08-02 01:57:55 UTC\n" +
                                     $"Analyzed By: MalekBael\n" +
                                     $"Tool Version: LGB Parser v2.0\n\n";

                // Write the header first
                File.WriteAllText(outputPath, detailedAnalysis);

                // Use TextExporter to append the main analysis
                var exporter = new TextExporter();
                exporter.Export(data, outputPath + ".temp");

                // Combine the files
                var analysisContent = File.ReadAllText(outputPath + ".temp");
                File.AppendAllText(outputPath, analysisContent);
                File.Delete(outputPath + ".temp");

                // Also display to console
                Console.WriteLine(File.ReadAllText(outputPath));

                Console.WriteLine($"\nDetailed analysis saved to: {outputPath}");

                // If this is a planner file, show additional technical details
                if (lgbFilePath.Contains("planner.lgb"))
                {
                    Console.WriteLine("\n🔧 PLANNER FILE ANALYSIS");
                    Console.WriteLine("========================");
                    Console.WriteLine("This file used enhanced parsing due to Lumina compatibility issues.");
                    Console.WriteLine("Enhanced features used:");
                    Console.WriteLine("  - Raw binary file analysis");
                    Console.WriteLine("  - Hex dump interpretation");
                    Console.WriteLine("  - Fallback parsing strategies");
                    Console.WriteLine("  - Special error handling for negative array counts");

                    if (data.LayerGroups.Any())
                    {
                        var firstLayer = data.LayerGroups.First();
                        if (firstLayer.InstanceObjects.Any())
                        {
                            var firstObject = firstLayer.InstanceObjects.First();
                            if (firstObject.ObjectData.ContainsKey("FullHexDump"))
                            {
                                Console.WriteLine($"\nRaw file hex data: {firstObject.ObjectData["FullHexDump"]}");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Analysis failed: {ex.Message}");
                Console.WriteLine($"\nError Details:");
                Console.WriteLine($"Type: {ex.GetType().Name}");
                Console.WriteLine($"Message: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                }
                Console.WriteLine($"\nStack Trace:");
                Console.WriteLine(ex.StackTrace);
            }
        }

        static void HandleSingleGameFileCommand(GameLgbReader reader, string[] args, int gameIndex)
        {
            if (gameIndex >= args.Length - 2)
            {
                Console.WriteLine("Error: Single file parsing requires an LGB file path");
                return;
            }

            string lgbFilePath = args[gameIndex + 2];
            string outputPath = args.Length > gameIndex + 3 ? args[gameIndex + 3] : null;
            string format = args.Length > gameIndex + 4 ? args[gameIndex + 4] : "text";

            if (string.IsNullOrEmpty(outputPath))
            {
                var fileName = Path.GetFileNameWithoutExtension(lgbFilePath);
                outputPath = $"{fileName}.txt";
            }

            Console.WriteLine($"Parsing single file: {lgbFilePath}");
            Console.WriteLine($"Output: {outputPath}");
            Console.WriteLine($"Format: {format}");
            Console.WriteLine();

            try
            {
                var data = reader.ParseLgbFile(lgbFilePath);

                if (format.ToLower() == "json")
                {
                    var jsonExporter = new JsonExporter();
                    jsonExporter.Export(data, outputPath);
                }
                else
                {
                    var textExporter = new TextExporter();
                    textExporter.Export(data, outputPath);
                }

                Console.WriteLine($"✅ Successfully parsed and exported to: {outputPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to parse file: {ex.Message}");
                Environment.Exit(1);
            }
        }

        static void HandleFileMode(string[] args)
        {
            if (args.Length < 2)
            {
                Console.WriteLine("Error: File mode requires at least 2 arguments");
                ShowHelp();
                return;
            }

            string inputPath = args[0];
            string outputPath = args[1];
            string format = args.Length > 2 ? args[2] : "text";

            Console.WriteLine($"File Mode Processing");
            Console.WriteLine($"Input: {inputPath}");
            Console.WriteLine($"Output: {outputPath}");
            Console.WriteLine($"Format: {format}");
            Console.WriteLine();

            try
            {
                if (File.Exists(inputPath))
                {
                    // Single file processing
                    HandleSingleFile(inputPath, outputPath, format);
                }
                else if (Directory.Exists(inputPath))
                {
                    // Folder processing
                    HandleFolderProcessing(inputPath, outputPath, format);
                }
                else
                {
                    Console.WriteLine($"Error: Input path does not exist: {inputPath}");
                    Environment.Exit(1);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Processing failed: {ex.Message}");
                Environment.Exit(1);
            }
        }

        static void HandleSingleFile(string inputPath, string outputPath, string format)
        {
            Console.WriteLine($"Processing single file...");

            try
            {
                // Use the SafeLgbParser for file mode
                var parser = new SafeLgbParser(debugMode: true);
                var data = parser.ParseFile(inputPath);

                if (format.ToLower() == "json")
                {
                    var exporter = new JsonExporter();
                    exporter.Export(data, outputPath);
                }
                else
                {
                    var exporter = new TextExporter();
                    exporter.Export(data, outputPath);
                }

                Console.WriteLine($"✅ Successfully processed: {outputPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to process file: {ex.Message}");
                Environment.Exit(1);
            }
        }

        static void HandleFolderProcessing(string inputFolder, string outputFolder, string format)
        {
            Console.WriteLine($"Processing folder...");

            var lgbFiles = Directory.GetFiles(inputFolder, "*.lgb", SearchOption.AllDirectories);

            if (lgbFiles.Length == 0)
            {
                Console.WriteLine("No LGB files found in the input folder.");
                return;
            }

            Console.WriteLine($"Found {lgbFiles.Length} LGB files");
            Directory.CreateDirectory(outputFolder);

            var parser = new SafeLgbParser(debugMode: false);
            int processed = 0;
            int failed = 0;

            foreach (var lgbFile in lgbFiles)
            {
                try
                {
                    var relativePath = Path.GetRelativePath(inputFolder, lgbFile);
                    var outputExtension = format.ToLower() == "json" ? ".json" : ".txt";
                    var outputPath = Path.Combine(outputFolder, Path.ChangeExtension(relativePath, outputExtension));

                    Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

                    var data = parser.ParseFile(lgbFile);

                    if (format.ToLower() == "json")
                    {
                        var exporter = new JsonExporter();
                        exporter.Export(data, outputPath);
                    }
                    else
                    {
                        var exporter = new TextExporter();
                        exporter.Export(data, outputPath);
                    }

                    processed++;
                    Console.WriteLine($"✅ Processed: {relativePath}");
                }
                catch (Exception ex)
                {
                    failed++;
                    Console.WriteLine($"❌ Failed: {lgbFile} - {ex.Message}");
                }
            }

            Console.WriteLine($"\nFolder processing complete!");
            Console.WriteLine($"Processed: {processed} files");
            Console.WriteLine($"Failed: {failed} files");
            Console.WriteLine($"Output folder: {outputFolder}");
        }
    }
}