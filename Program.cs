using System;
using System.IO;
using System.Linq;

namespace LgbParser
{
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("LGB Parser - Starting...");

            if (args.Length < 1)
            {
                ShowUsage();
                return;
            }

            try
            {
                if (args[0] == "--game")
                {
                    if (args.Length < 2)
                    {
                        Console.WriteLine("Error: Game installation path required.");
                        ShowUsage();
                        return;
                    }

                    HandleGameMode(args);
                }
                else
                {
                    // Original file mode
                    HandleFileMode(args);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Fatal Error: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Inner Exception: {ex.InnerException.Message}");
                }
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
            }
        }

        static void ShowUsage()
        {
            Console.WriteLine("LGB Parser Usage:");
            Console.WriteLine("================");
            Console.WriteLine();
            Console.WriteLine("File Mode:");
            Console.WriteLine("  lgb-parser <input.lgb> <output.txt> [format]              - Parse single extracted file");
            Console.WriteLine("  lgb-parser <input_folder> [output_folder] [format]       - Parse all LGB files in folder");
            Console.WriteLine();
            Console.WriteLine("Game Mode:");
            Console.WriteLine("  lgb-parser --game <game_path> <lgb_file_path> [output]    - Parse from game installation");
            Console.WriteLine("  lgb-parser --game <game_path> --batch [output_folder]     - Parse all LGB files");  // CHANGED
            Console.WriteLine("  lgb-parser --game <game_path> --list                      - List available LGB files");
            Console.WriteLine("  lgb-parser --game <game_path> --list-zones                - List available zones");
            Console.WriteLine("  lgb-parser --game <game_path> --list-types                - List available file types");
            Console.WriteLine("  lgb-parser --game <game_path> --batch-zone <zone>         - Parse files from specific zone");
            Console.WriteLine("  lgb-parser --game <game_path> --batch-type <type>         - Parse files of specific type");
            Console.WriteLine();
            Console.WriteLine("Formats: txt (default), json");
            Console.WriteLine("Game path should point to FFXIV installation directory (contains 'game' folder)");
            Console.WriteLine();
            Console.WriteLine("Examples:");
            Console.WriteLine("  lgb-parser bg.lgb output.txt");
            Console.WriteLine("  lgb-parser C:\\extracted_lgb_files parsed_output");
            Console.WriteLine("  lgb-parser --game \"C:\\Program Files (x86)\\SquareEnix\\FINAL FANTASY XIV - A Realm Reborn\" --list");
        }

        static void HandleGameMode(string[] args)
        {
            string gamePath = args[1];
            Console.WriteLine($"Game mode - Game path: {gamePath}");

            try
            {
                using var gameReader = new GameLgbReader(gamePath);
                Console.WriteLine("GameReader initialized successfully.");

                if (args.Length > 2)
                {
                    string command = args[2];
                    Console.WriteLine($"Command: {command}");

                    switch (command)
                    {
                        case "--list":
                            HandleListCommand(gameReader);
                            break;

                        case "--list-zones":
                            HandleListZonesCommand(gameReader);
                            break;

                        case "--list-types":
                            HandleListTypesCommand(gameReader);
                            break;

                        case "--batch":
                            string outputFolder = args.Length > 3 ? args[3] : "parsed_lgb_output";
                            HandleBatchCommand(gameReader, outputFolder);
                            break;

                        case "--batch-zone":
                            if (args.Length < 4)
                            {
                                Console.WriteLine("Error: Zone name required for --batch-zone option.");
                                return;
                            }
                            string zoneName = args[3];
                            string zoneOutputFolder = args.Length > 4 ? args[4] : $"parsed_lgb_{zoneName}";
                            HandleBatchZoneCommand(gameReader, zoneName, zoneOutputFolder);
                            break;

                        case "--batch-type":
                            if (args.Length < 4)
                            {
                                Console.WriteLine("Error: File type required for --batch-type option.");
                                return;
                            }
                            string fileType = args[3];
                            string typeOutputFolder = args.Length > 4 ? args[4] : $"parsed_lgb_{fileType}";
                            HandleBatchTypeCommand(gameReader, fileType, typeOutputFolder);
                            break;

                        default:
                            // Single file parsing from game
                            HandleSingleGameFile(gameReader, args);
                            break;
                    }
                }
                else
                {
                    Console.WriteLine("Error: Command required for game mode.");
                    ShowUsage();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in game mode: {ex.Message}");
                throw;
            }
        }

        static void HandleListCommand(GameLgbReader gameReader)
        {
            Console.WriteLine("Getting all available LGB files...");
            var allFiles = gameReader.GetAvailableLgbFiles();

            Console.WriteLine($"\nFound {allFiles.Count} available LGB files:");
            foreach (var file in allFiles.Take(20)) // Show first 20
            {
                Console.WriteLine($"  {file}");
            }
            if (allFiles.Count > 20)
            {
                Console.WriteLine($"  ... and {allFiles.Count - 20} more files");
            }
        }

        static void HandleListZonesCommand(GameLgbReader gameReader)
        {
            Console.WriteLine("Available zones:");
            var zones = new[] { "sea_s1", "wil_w1", "fst_f1", "lak_l1", "roc_r1", "zon_z1", "air_a1", "ocn_o1" };
            foreach (var zone in zones)
            {
                var zoneFiles = gameReader.GetLgbFilesByZone(zone);
                Console.WriteLine($"  {zone}: {zoneFiles.Count} files");
            }
        }

        static void HandleListTypesCommand(GameLgbReader gameReader)
        {
            Console.WriteLine("Available LGB file types:");
            var types = new[] { "bg", "planevent", "planmap", "planlive", "sound", "vfx", "planner" };
            foreach (var type in types)
            {
                var typeFiles = gameReader.GetLgbFilesByType(type);
                Console.WriteLine($"  {type}: {typeFiles.Count} files");
            }
        }

        static void HandleBatchCommand(GameLgbReader gameReader, string outputFolder)
        {
            Directory.CreateDirectory(outputFolder);

            Console.WriteLine("Parsing all LGB files from game installation...");  // CHANGED
            Console.WriteLine("This will maintain the original folder structure.");

            // Parse ALL available LGB files, not just a sample
            var results = gameReader.ParseAllLgbFiles(); // Parse all files, no limit

            IExporter textExporter = new TextExporter();

            foreach (var kvp in results)
            {
                try
                {
                    // Convert game path to file system path and preserve structure
                    string gamePath = kvp.Key; // e.g., "bg/ffxiv/air_a1/evt/a1e2/level/bg.lgb"

                    // Convert forward slashes to backslashes for Windows
                    string relativePath = gamePath.Replace('/', Path.DirectorySeparatorChar);

                    // Create the full output path preserving directory structure
                    string outputPath = Path.Combine(outputFolder, relativePath);

                    // Change extension to .txt
                    outputPath = Path.ChangeExtension(outputPath, ".txt");

                    // Ensure the directory exists
                    string outputDir = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrEmpty(outputDir))
                    {
                        Directory.CreateDirectory(outputDir);
                    }

                    // Export the file
                    textExporter.Export(kvp.Value, outputPath);
                    Console.WriteLine($"✓ Exported: {outputPath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"✗ Failed to export {kvp.Key}: {ex.Message}");
                }
            }

            Console.WriteLine($"\nProcessed {results.Count} files with preserved folder structure.");
            Console.WriteLine($"Output directory: {outputFolder}");
        }

        static void HandleBatchZoneCommand(GameLgbReader gameReader, string zoneName, string outputFolder)
        {
            Directory.CreateDirectory(outputFolder);

            Console.WriteLine($"Parsing all LGB files from zone '{zoneName}' with preserved folder structure...");

            var zoneResults = gameReader.ParseLgbFilesByZone(zoneName);
            IExporter zoneExporter = new TextExporter();

            foreach (var kvp in zoneResults)
            {
                try
                {
                    // Preserve folder structure for zone files too
                    string gamePath = kvp.Key;
                    string relativePath = gamePath.Replace('/', Path.DirectorySeparatorChar);
                    string outputPath = Path.Combine(outputFolder, relativePath);
                    outputPath = Path.ChangeExtension(outputPath, ".txt");

                    string outputDir = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrEmpty(outputDir))
                    {
                        Directory.CreateDirectory(outputDir);
                    }

                    zoneExporter.Export(kvp.Value, outputPath);
                    Console.WriteLine($"✓ Exported: {outputPath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"✗ Failed to export {kvp.Key}: {ex.Message}");
                }
            }

            Console.WriteLine($"\nProcessed {zoneResults.Count} files from zone '{zoneName}' with preserved folder structure.");
            Console.WriteLine($"Output directory: {outputFolder}");
        }

        static void HandleBatchTypeCommand(GameLgbReader gameReader, string fileType, string outputFolder)
        {
            Directory.CreateDirectory(outputFolder);

            Console.WriteLine($"Parsing all {fileType} LGB files with preserved folder structure...");

            var typeResults = gameReader.ParseLgbFilesByType(fileType);
            IExporter typeExporter = new TextExporter();

            foreach (var kvp in typeResults)
            {
                try
                {
                    // Preserve folder structure for type files too
                    string gamePath = kvp.Key;
                    string relativePath = gamePath.Replace('/', Path.DirectorySeparatorChar);
                    string outputPath = Path.Combine(outputFolder, relativePath);
                    outputPath = Path.ChangeExtension(outputPath, ".txt");

                    string outputDir = Path.GetDirectoryName(outputPath);
                    if (!string.IsNullOrEmpty(outputDir))
                    {
                        Directory.CreateDirectory(outputDir);
                    }

                    typeExporter.Export(kvp.Value, outputPath);
                    Console.WriteLine($"✓ Exported: {outputPath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"✗ Failed to export {kvp.Key}: {ex.Message}");
                }
            }

            Console.WriteLine($"\nProcessed {typeResults.Count} {fileType} files with preserved folder structure.");
            Console.WriteLine($"Output directory: {outputFolder}");
        }

        static void HandleSingleGameFile(GameLgbReader gameReader, string[] args)
        {
            if (args.Length < 3)
            {
                Console.WriteLine("Error: LGB file path required for single file parsing.");
                return;
            }

            // Single file parsing from game
            string lgbFilePath = args[2];
            string singleOutputPath = args.Length > 3 ? args[3] : lgbFilePath.Replace("/", "_") + ".txt";
            string format = args.Length > 4 ? args[4] : "txt";

            Console.WriteLine($"Parsing LGB file from game: {lgbFilePath}");

            var lgbData = gameReader.ParseLgbFile(lgbFilePath);

            IExporter gameExporter = format.ToLower() switch
            {
                "json" => new JsonExporter(),
                _ => new TextExporter()
            };

            gameExporter.Export(lgbData, singleOutputPath);
            Console.WriteLine($"Successfully exported to: {singleOutputPath}");
        }

        static void HandleFileMode(string[] args)
        {
            string inputPath = args[0];
            Console.WriteLine($"File mode - Input path: {inputPath}");

            if (File.Exists(inputPath))
            {
                if (args.Length < 2)
                {
                    Console.WriteLine("Error: Output file required for single file mode.");
                    return;
                }
                ParseSingleFile(inputPath, args[1], args.Length > 2 ? args[2] : "txt");
            }
            else if (Directory.Exists(inputPath))
            {
                string outputFolder = args.Length > 1 ? args[1] : Path.Combine(inputPath, "parsed_output");
                string format = args.Length > 2 ? args[2] : "txt";
                ParseFolderRecursive(inputPath, outputFolder, format);
            }
            else
            {
                Console.WriteLine($"Error: Input path '{inputPath}' not found.");
            }
        }

        static void ParseSingleFile(string inputFile, string outputFile, string format)
        {
            Console.WriteLine($"Parsing single file: {inputFile}");

            try
            {
                // Use the safe parser with debug mode for problematic files
                bool debugMode = inputFile.Contains("z1e8") || inputFile.Contains("problematic");
                var parser = new SafeLgbParser(debugMode);
                var lgbData = parser.ParseFile(inputFile);

                IExporter fileExporter = format.ToLower() switch
                {
                    "json" => new JsonExporter(),
                    _ => new TextExporter()
                };

                fileExporter.Export(lgbData, outputFile);
                Console.WriteLine($"Successfully exported to {outputFile}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to parse {inputFile}: {ex.Message}");

                // Try to create a minimal error report
                try
                {
                    var errorReport = $"Error parsing file: {inputFile}\n";
                    errorReport += $"Error: {ex.Message}\n";
                    errorReport += $"Time: {DateTime.Now}\n";

                    var errorFile = outputFile.Replace(Path.GetExtension(outputFile), "_ERROR.txt");
                    File.WriteAllText(errorFile, errorReport);
                    Console.WriteLine($"Error report saved to: {errorFile}");
                }
                catch
                {
                    // If we can't even write an error report, just continue
                }
            }
        }

        static void ParseFolderRecursive(string inputFolder, string outputFolder, string format)
        {
            Console.WriteLine($"Parsing folder: {inputFolder}");

            Directory.CreateDirectory(outputFolder);

            var lgbFiles = Directory.GetFiles(inputFolder, "*.lgb", SearchOption.AllDirectories);

            if (lgbFiles.Length == 0)
            {
                Console.WriteLine($"No LGB files found in '{inputFolder}'");
                return;
            }

            Console.WriteLine($"Found {lgbFiles.Length} LGB files to process...");

            IExporter folderExporter = format.ToLower() switch
            {
                "json" => new JsonExporter(),
                _ => new TextExporter()
            };

            int processed = 0;
            int failed = 0;

            foreach (string lgbFile in lgbFiles)
            {
                try
                {
                    string relativePath = Path.GetRelativePath(inputFolder, lgbFile);
                    string folderOutputDir = Path.Combine(outputFolder, Path.GetDirectoryName(relativePath) ?? "");
                    Directory.CreateDirectory(folderOutputDir);

                    string fileName = Path.GetFileNameWithoutExtension(lgbFile);
                    string extension = format.ToLower() == "json" ? ".json" : ".txt";
                    string folderOutputFile = Path.Combine(folderOutputDir, fileName + extension);

                    Console.WriteLine($"Processing: {relativePath}");

                    // Use the safe parser
                    bool debugMode = lgbFile.Contains("z1e8");
                    var parser = new SafeLgbParser(debugMode);
                    var lgbData = parser.ParseFile(lgbFile);
                    folderExporter.Export(lgbData, folderOutputFile);

                    processed++;
                    Console.WriteLine($"✓ Processed: {relativePath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"✗ Failed to process '{lgbFile}': {ex.Message}");
                    failed++;
                }
            }

            Console.WriteLine();
            Console.WriteLine($"Processing complete:");
            Console.WriteLine($"  Successfully processed: {processed}");
            Console.WriteLine($"  Failed: {failed}");
            Console.WriteLine($"  Output folder: {outputFolder}");
        }
    }
}