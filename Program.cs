using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using LgbParser;
using System.Runtime;
using System.Diagnostics;

namespace LgbParser
{
    internal class Program
    {
        private static CancellationTokenSource _cancellationTokenSource = new();
        private static GameLgbReader _currentReader = null;

        private static readonly double MEMORY_LIMIT_PERCENTAGE = 0.6;      
        private static readonly long ABSOLUTE_MEMORY_LIMIT = 2048L * 1024 * 1024;    

        private static readonly long _totalSystemMemory = GetTotalSystemMemory();
        private static readonly long _memoryLimit = Math.Min(
            (long)(_totalSystemMemory * MEMORY_LIMIT_PERCENTAGE),
            ABSOLUTE_MEMORY_LIMIT);
        private static readonly long _warningThreshold = (long)(_memoryLimit * 0.70);    
        private static readonly long _cleanupThreshold = (long)(_memoryLimit * 0.60);    
        private static readonly long _emergencyThreshold = (long)(_memoryLimit * 0.85);    

        private static volatile bool _isTerminating = false;
        private static int _consecutiveMemoryWarnings = 0;
        private static DateTime _lastMemoryCheck = DateTime.Now;

        private static int _processedFileCount = 0;
        private static DateTime _lastLuminaCleanup = DateTime.Now;

        private static void Main(string[] args)
        {
            Console.WriteLine("LGB Parser");
            Console.WriteLine("=====================================================");
            Console.WriteLine($"Current Date/Time: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            Console.WriteLine();

            Console.CancelKeyPress += OnCancelKeyPress;
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

            try
            {
                if (args.Length == 0)
                {
                    ShowHelp();
                    return;
                }

                if (args.Contains("--game"))
                {
                    HandleGameMode(args);
                }
                else
                {
                    HandleFileMode(args);
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("\n⚠️  Operation was cancelled by user.");
            }
            catch (OutOfMemoryException)
            {
                Console.WriteLine("\n🚨 Out of memory! Terminating immediately...");
                EmergencyLuminaCleanup();
                Environment.Exit(1);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine("\nFor help, run without arguments or use --help");
            }
            finally
            {
                ForceTermination(0);
            }
        }

        private static long GetTotalSystemMemory()
        {
            try
            {
                long detectedMemory = 0;

                try
                {
                    var memoryInfo = GC.GetGCMemoryInfo();
                    if (memoryInfo.TotalAvailableMemoryBytes > 0)
                    {
                        detectedMemory = memoryInfo.TotalAvailableMemoryBytes;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"GC memory info failed: {ex.Message}");
                }

                if (detectedMemory == 0)
                {
                    try
                    {
                        var process = new Process
                        {
                            StartInfo = new ProcessStartInfo
                            {
                                FileName = "wmic",
                                Arguments = "computersystem get TotalPhysicalMemory /value",
                                UseShellExecute = false,
                                RedirectStandardOutput = true,
                                CreateNoWindow = true
                            }
                        };

                        process.Start();
                        var output = process.StandardOutput.ReadToEnd();
                        process.WaitForExit();

                        foreach (var line in output.Split('\n'))
                        {
                            if (line.StartsWith("TotalPhysicalMemory="))
                            {
                                var memStr = line.Substring("TotalPhysicalMemory=".Length).Trim();
                                if (long.TryParse(memStr, out var totalMem) && totalMem > 0)
                                {
                                    detectedMemory = totalMem;
                                    Console.WriteLine($"ℹ️  Method 2 (WMI): {detectedMemory / (1024.0 * 1024.0 * 1024.0):F1} GB");
                                    break;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"WMI detection failed: {ex.Message}");
                    }
                }

                if (detectedMemory == 0)
                {
                    try
                    {
                        using var process = Process.GetCurrentProcess();
                        var workingSet = process.WorkingSet64;
                        var parentProcess = GetParentProcess();

                        if (parentProcess != null)
                        {
                            Console.WriteLine($"Running as child process of: {parentProcess.ProcessName}");
                            detectedMemory = Math.Max(6L * 1024 * 1024 * 1024, workingSet * 16);
                        }
                        else
                        {
                            detectedMemory = Math.Max(8L * 1024 * 1024 * 1024, workingSet * 20);
                        }

                        Console.WriteLine($"Method 3 (Process Estimation): {detectedMemory / (1024.0 * 1024.0 * 1024.0):F1} GB");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Process estimation failed: {ex.Message}");
                    }
                }

                if (detectedMemory == 0)
                {
                    var parentProcess = GetParentProcess();
                    if (parentProcess != null)
                    {
                        Console.WriteLine($"Running as child process of: {parentProcess.ProcessName}");
                        detectedMemory = 6L * 1024 * 1024 * 1024;
                    }
                    else
                    {
                        detectedMemory = 16L * 1024 * 1024 * 1024;
                    }
                    Console.WriteLine($"Final fallback: {detectedMemory / (1024.0 * 1024.0 * 1024.0):F1} GB");
                }

                return detectedMemory;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"All memory detection methods failed: {ex.Message}");
                return 4L * 1024 * 1024 * 1024;
            }
        }

        private static Process GetParentProcess()
        {
            try
            {
                var currentProcess = Process.GetCurrentProcess();
                var parentPid = GetParentProcessId(currentProcess.Id);

                if (parentPid > 0)
                {
                    return Process.GetProcessById(parentPid);
                }
            }
            catch
            {
            }
            return null;
        }

        private static int GetParentProcessId(int processId)
        {
            try
            {
                var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "wmic",
                        Arguments = $"process where processid={processId} get parentprocessid /value",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                var output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                foreach (var line in output.Split('\n'))
                {
                    if (line.StartsWith("ParentProcessId="))
                    {
                        var pidStr = line.Substring("ParentProcessId=".Length).Trim();
                        if (int.TryParse(pidStr, out var parentPid))
                        {
                            return parentPid;
                        }
                    }
                }
            }
            catch
            {
            }
            return 0;
        }

        private static void DisplayOutputLocation(string outputFolder)
        {
            var currentDirectory = Directory.GetCurrentDirectory();
            var fullOutputPath = Path.GetFullPath(outputFolder);

            Console.WriteLine("OUTPUT LOCATION");
            Console.WriteLine("============================");
            Console.WriteLine($"Current Working Directory: {currentDirectory}");
            Console.WriteLine($"Relative Output Folder: {outputFolder}");
            Console.WriteLine($"Full Output Path: {fullOutputPath}");
            Console.WriteLine($"Map Editor Process: {GetParentProcess()?.ProcessName ?? "Unknown"}");

            if (Directory.Exists(fullOutputPath))
            {
                var files = Directory.GetFiles(fullOutputPath, "*", SearchOption.AllDirectories);
                Console.WriteLine($"Directory exists with {files.Length} files");

                if (files.Length > 0)
                {
                    Console.WriteLine("Sample files:");
                    foreach (var file in files.Take(3))
                    {
                        Console.WriteLine($"  {Path.GetRelativePath(fullOutputPath, file)}");
                    }
                }
            }
            else
            {
                Console.WriteLine("Directory does not exist yet");
            }
            Console.WriteLine();
        }

        private static void DisplayMemoryInfo()
        {
            var totalGB = _totalSystemMemory / (1024.0 * 1024.0 * 1024.0);
            var limitGB = _memoryLimit / (1024.0 * 1024.0 * 1024.0);

            Console.WriteLine("=====================================");

            try
            {
                var currentProcess = Process.GetCurrentProcess();
                var currentUsageGB = currentProcess.WorkingSet64 / (1024.0 * 1024.0 * 1024.0);
                var parentProcess = GetParentProcess();

                Console.WriteLine($"Total System RAM: {totalGB:F1} GB");
                Console.WriteLine($"Current Process Usage: {currentUsageGB:F2} GB");

                if (parentProcess != null)
                {
                    Console.WriteLine($"Parent Process: {parentProcess.ProcessName}");
                    Console.WriteLine($"Running as: Child Process");
                }
                else
                {
                    Console.WriteLine($"Running as: Standalone Process");
                }

                if (limitGB > totalGB * 0.4)
                {
                    Console.WriteLine($"WARNING: Memory limit may be too high for Lumina operations");
                }
                if (limitGB < 1.0)
                {
                    Console.WriteLine($"WARNING: Memory limit may be too low for Lumina operations");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error displaying extended memory info: {ex.Message}");
                Console.WriteLine($"Basic Info - Total: {totalGB:F1} GB, Limit: {limitGB:F1} GB");
            }

            Console.WriteLine();
        }

        private static bool CheckMemoryUsage()
        {
            try
            {
                var currentProcess = Process.GetCurrentProcess();
                var currentMemory = GC.GetTotalMemory(false);
                var workingSet = currentProcess.WorkingSet64;
                var privateMemory = currentProcess.PrivateMemorySize64;

                var actualMemoryUsage = Math.Max(currentMemory, workingSet);

                var currentMemoryMB = currentMemory / (1024.0 * 1024.0);
                var workingSetMB = workingSet / (1024.0 * 1024.0);
                var privateMemoryMB = privateMemory / (1024.0 * 1024.0);
                var limitMB = _memoryLimit / (1024.0 * 1024.0);

                if (actualMemoryUsage > ABSOLUTE_MEMORY_LIMIT)
                {
                    Console.WriteLine($"🚨 LUMINA HARD LIMIT EXCEEDED!");
                    Console.WriteLine($"   Current Usage: {actualMemoryUsage / (1024.0 * 1024.0):F1} MB");
                    Console.WriteLine($"   Lumina Hard Limit: {ABSOLUTE_MEMORY_LIMIT / (1024.0 * 1024.0):F1} MB");
                    return false;
                }

                if (actualMemoryUsage > _warningThreshold)
                {
                    Console.WriteLine($"⚠️  LUMINA memory usage high: {actualMemoryUsage / (1024.0 * 1024.0):F1} MB / {_memoryLimit / (1024.0 * 1024.0):F1} MB");
                    UltraAggressiveLuminaCleanup();
                }
                else if (actualMemoryUsage > _cleanupThreshold)
                {
                    Console.WriteLine($"🧹 LUMINA cleanup threshold reached: {actualMemoryUsage / (1024.0 * 1024.0):F1} MB");
                    AggressiveLuminaCleanup();
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  LUMINA memory check failed: {ex.Message}");
                return true;
            }
        }

        private static void UltraAggressiveLuminaCleanup()
        {
            if (_isTerminating)
            {
                return;
            }

            try
            {
                var beforeMemory = GC.GetTotalMemory(false);

                if (_currentReader != null)
                {
                    _currentReader.ClearCaches();      
                }

                for (int i = 0; i < 3; i++)    
                {
                    if (_isTerminating) return;    

                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Optimized, true, true);
                    GC.WaitForPendingFinalizers();
                    Thread.Sleep(50);    
                }

                var afterMemory = GC.GetTotalMemory(false);
                _lastLuminaCleanup = DateTime.Now;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Ultra Lumina cleanup error: {ex.Message}");
            }
        }

        private static void AggressiveLuminaCleanup()
        {
            try
            {
                Console.WriteLine("🧹 AGGRESSIVE LUMINA CLEANUP...");

                var beforeMemory = GC.GetTotalMemory(false);

                if (_currentReader != null)
                {
                    _currentReader.ClearCaches();
                }

                for (int i = 0; i < 4; i++)
                {
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
                    GC.WaitForPendingFinalizers();
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
                    Thread.Sleep(50);
                }

                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect();

                var afterMemory = GC.GetTotalMemory(false);
                var freed = (beforeMemory - afterMemory) / (1024.0 * 1024.0);

                Console.WriteLine($"🧹 LUMINA cleanup freed: {freed:F1} MB, Current: {afterMemory / (1024.0 * 1024.0):F1} MB");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Lumina cleanup error: {ex.Message}");
            }
        }

        private static void EmergencyLuminaCleanup()
        {
            Console.WriteLine("🚨 EMERGENCY LUMINA CLEANUP INITIATED");

            try
            {
                if (_currentReader != null)
                {
                    _currentReader.Dispose();
                    _currentReader = null;
                    Console.WriteLine("💥 GameLgbReader disposed for emergency cleanup");
                }

                for (int i = 0; i < 15; i++)   
                {
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
                    GC.WaitForPendingFinalizers();
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
                    Thread.Sleep(100);
                }

                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);

                Console.WriteLine("💥 EMERGENCY LUMINA cleanup completed");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Emergency Lumina cleanup error: {ex.Message}");
            }
        }

        private static void AggressiveMemoryCleanup()
        {
            AggressiveLuminaCleanup();    
        }

        private static void EmergencyMemoryCleanup()
        {
            EmergencyLuminaCleanup();    
        }

        private static void OnCancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            Console.WriteLine("\nCancellation requested...");
            e.Cancel = true;    

            _isTerminating = true;

            _cancellationTokenSource?.Cancel();

            try
            {
                if (_currentReader != null)
                {
                    _currentReader.Dispose();
                    _currentReader = null;
                }
            }
            catch
            {
            }

            Console.WriteLine("Forcing immediate termination...");
            Environment.Exit(1);
        }

        private static void OnProcessExit(object sender, EventArgs e)
        {
            Console.WriteLine("Process exiting, cleaning up...");
            ForceTermination(0);
        }

        private static void ForceTermination(int exitCode)
        {
            if (_isTerminating)
            {
                return;
            }
            _isTerminating = true;

            try
            {
                Console.WriteLine("Performing rapid cleanup...");

                _cancellationTokenSource?.Cancel();

                if (_currentReader != null)
                {
                    try
                    {
                        _currentReader.Dispose();
                    }
                    catch
                    {
                    }
                    _currentReader = null;
                }

                try
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
                catch
                {
                }

                Console.WriteLine("Cleanup complete.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Cleanup error: {ex.Message}");
            }

            Console.WriteLine($"Forcing process termination (exit code: {exitCode})...");

            try
            {
                Environment.Exit(exitCode);
            }
            catch
            {
                try
                {
                    var currentProcess = Process.GetCurrentProcess();
                    currentProcess.Kill();
                }
                catch
                {
                    Environment.FailFast("Force termination failed");
                }
            }
        }

        private static void SimpleTermination(int exitCode)
        {
            try
            {
                Console.WriteLine("🧹 Simple Lumina cleanup...");

                _cancellationTokenSource?.Cancel();

                if (_currentReader != null)
                {
                    try
                    {
                        _currentReader.Dispose();
                    }
                    catch
                    {
                    }
                    _currentReader = null;
                }

                try
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
                catch
                {
                }

                Console.WriteLine("Simple Lumina cleanup complete.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Simple Lumina cleanup error: {ex.Message}");
            }

        }

        private static void ShowHelp()
        {
            Console.WriteLine("LGB Parser - Extract data from Final Fantasy XIV LGB files");
            Console.WriteLine();
            Console.WriteLine("GAME MODE (Direct FFXIV installation - RECOMMENDED):");
            Console.WriteLine("  lgb-parser --game <game_path> --list                     - List available LGB files");
            Console.WriteLine("  lgb-parser --game <game_path> --list-zones               - List available zones");
            Console.WriteLine("  lgb-parser --game <game_path> --list-types               - List available file types");
            Console.WriteLine("  lgb-parser --game <game_path> --batch [output_folder] [format] - Parse all LGB files");
            Console.WriteLine("  lgb-parser --game <game_path> --batch-zone <zone> [output_folder] [format] - Parse zone files");
            Console.WriteLine("  lgb-parser --game <game_path> --batch-type <type> [output_folder] [format] - Parse type files");
            Console.WriteLine("  lgb-parser --game <game_path> --analyze <lgb_file_path>  - Detailed analysis of specific file");
            Console.WriteLine("  lgb-parser --game <game_path> <lgb_file_path> [output] [format] - Parse single game file");
            Console.WriteLine();
            Console.WriteLine("FILE MODE (Pre-extracted LGB files - Limited support):");
            Console.WriteLine("  lgb-parser <input.lgb> <output.txt> [format]              - Parse single file");
            Console.WriteLine("  lgb-parser <input_folder> [output_folder] [format]       - Parse folder (batch)");
            Console.WriteLine();
            Console.WriteLine("FORMATS:");
            Console.WriteLine("  text (default) - Human-readable text format");
            Console.WriteLine("  json          - JSON format");
            Console.WriteLine();
            Console.WriteLine("EXAMPLES:");
            Console.WriteLine("  Game Mode (Recommended):");
            Console.WriteLine("    lgb-parser --game \"C:\\Program Files (x86)\\SquareEnix\\FINAL FANTASY XIV - A Realm Reborn\" --list");
            Console.WriteLine("    lgb-parser --game \"C:\\FFXIV\" --batch-zone sea_s1 limsa_files");
            Console.WriteLine("    lgb-parser --game \"C:\\FFXIV\" --batch-zone sea_s1 limsa_files json");
            Console.WriteLine("    lgb-parser --game \"C:\\FFXIV\" --batch-type planner planner_analysis json");
            Console.WriteLine("    lgb-parser --game \"C:\\FFXIV\" --batch my_output json");
            Console.WriteLine("    lgb-parser --game \"C:\\FFXIV\" --analyze bg/ffxiv/sea_s1/fld/s1f1/level/planner.lgb");
            Console.WriteLine();
            Console.WriteLine("  File Mode:");
            Console.WriteLine("    lgb-parser bg.lgb output.txt");
            Console.WriteLine("    lgb-parser extracted_files/ parsed_output/ json");
            Console.WriteLine();
            Console.WriteLine("Supported Expansion: A Realm Reborn");
            Console.WriteLine("SUPPORTED TYPES: bg, planevent, planlive, planmap, planner, sound, vfx");
            Console.WriteLine();
            Console.WriteLine("NOTE: Game mode provides enhanced parsing with ALL LGB entry types!");
            Console.WriteLine("      Press Ctrl+C to cancel and force cleanup.");
        }

        private static void HandleGameMode(string[] args)
        {
            var gameIndex = Array.IndexOf(args, "--game");
            if (gameIndex == -1 || gameIndex >= args.Length - 1)
            {
                Console.WriteLine("Error: --game requires a path to FFXIV installation");
                return;
            }

            string gamePath = args[gameIndex + 1];

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

            DisplayMemoryInfo();     

            _currentReader = new GameLgbReader(gamePath);

            try
            {
                if (args.Contains("--list"))
                {
                    HandleListCommand(_currentReader);
                }
                else if (args.Contains("--list-zones"))
                {
                    HandleListZonesCommand(_currentReader);
                }
                else if (args.Contains("--list-types"))
                {
                    HandleListTypesCommand(_currentReader);
                }
                else if (args.Contains("--batch"))
                {
                    HandleBatchCommand(_currentReader, args);
                }
                else if (args.Contains("--batch-zone"))
                {
                    HandleBatchZoneCommand(_currentReader, args);
                }
                else if (args.Contains("--batch-type"))
                {
                    HandleBatchTypeCommand(_currentReader, args);
                }
                else if (args.Contains("--analyze"))
                {
                    HandleAnalyzeCommand(_currentReader, args);
                }
                else
                {
                    HandleSingleGameFileCommand(_currentReader, args, gameIndex);
                }
            }
            finally
            {
                if (_currentReader != null)
                {
                    try
                    {
                        _currentReader.Dispose();
                    }
                    catch
                    {
                    }
                    _currentReader = null;
                }
            }
        }

        private static void HandleListCommand(GameLgbReader reader)
        {
            Console.WriteLine("Discovering available LGB files...");

            var files = reader.DiscoverAllLgbFiles();

            Console.WriteLine($"\nFound {files.Count} LGB files:");
            Console.WriteLine("================================================================================");

            var groupedFiles = files.GroupBy(f =>
            {
                var parts = f.Split('/');
                return parts.Length >= 3 ? parts[2] : "Unknown";
            }).OrderBy(g => g.Key);

            foreach (var group in groupedFiles)
            {
                Console.WriteLine($"\n{group.Key.ToUpperInvariant()} ({group.Count()} files):");
                foreach (var file in group.Take(10))
                {
                    Console.WriteLine($"  {file}");
                }
                if (group.Count() > 10)
                {
                    Console.WriteLine($"  ... and {group.Count() - 10} more");
                }
            }

            AggressiveLuminaCleanup();
        }

        private static void HandleListZonesCommand(GameLgbReader reader)
        {
            Console.WriteLine("Analyzing available zones...");

            var files = reader.DiscoverAllLgbFiles();

            var zoneStats = files
                .GroupBy(f =>
                {
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

            AggressiveLuminaCleanup();
        }

        private static void HandleListTypesCommand(GameLgbReader reader)
        {
            Console.WriteLine("Analyzing available file types...");

            var files = reader.DiscoverAllLgbFiles();

            var typeStats = files
                .GroupBy(f =>
                {
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
                ["bg"] = ("Background objects, terrain, props", "✅ Enhanced"),
                ["planevent"] = ("Event triggers and interactions", "✅ Enhanced"),
                ["planmap"] = ("Map/navigation data", "✅ Enhanced"),
                ["planlive"] = ("Dynamic/live event data", "✅ Enhanced"),
                ["sound"] = ("Audio zones and sound triggers", "✅ Enhanced"),
                ["vfx"] = ("Visual effects placement", "✅ Enhanced"),
                ["planner"] = ("Layout planning data", "✅ Enhanced")
            };

            foreach (var type in typeStats)
            {
                var (description, status) = typeDescriptions.ContainsKey(type.Type)
                    ? typeDescriptions[type.Type]
                    : ("Unknown file type", "❓ Unknown");
                Console.WriteLine($"{type.Type,-15} {type.Count,-10} {description,-30} {status}");
            }

            AggressiveLuminaCleanup();
        }

        private static void HandleBatchCommand(GameLgbReader reader, string[] args)
        {
            string outputFolder = "lgb_output";
            string format = "text";

            var batchIndex = Array.IndexOf(args, "--batch");
            int currentArgIndex = batchIndex + 1;

            if (currentArgIndex < args.Length && !args[currentArgIndex].StartsWith("--"))
            {
                outputFolder = args[currentArgIndex];
                currentArgIndex++;
            }

            if (currentArgIndex < args.Length && !args[currentArgIndex].StartsWith("--"))
            {
                format = args[currentArgIndex].ToLower();
            }

            DisplayOutputLocation(outputFolder);

            Console.WriteLine($"OPTIMIZED STREAMING BATCH: Processing ALL LGB files to: {outputFolder}");
            Console.WriteLine($"Output format: {format.ToUpper()}");
            Console.WriteLine("Press Ctrl+C to cancel.");
            Console.WriteLine();

            try
            {
                var fullOutputPath = Path.GetFullPath(outputFolder);
                Directory.CreateDirectory(fullOutputPath);
                Console.WriteLine($"Created output directory: {fullOutputPath}");

                int exported = 0;
                int failed = 0;
                string fileExtension = format == "json" ? ".json" : ".txt";

                _processedFileCount = 0;
                _lastLuminaCleanup = DateTime.Now;

                reader.ParseAllLgbFilesWithStreamingExport(
                    _cancellationTokenSource.Token,
                                        onFileParseCallback: (filePath, lgbData) =>
                                        {
                                            try
                                            {
                                                if (_cancellationTokenSource.Token.IsCancellationRequested || _isTerminating)
                                                {
                                                    return;
                                                }

                                                if (!CheckMemoryUsage())
                                                {
                                                    Console.WriteLine("LUMINA memory limit exceeded during file processing - aborting!");
                                                    _cancellationTokenSource.Cancel();
                                                    return;
                                                }

                                                var safePath = filePath.Replace('/', Path.DirectorySeparatorChar)
                                                              .Replace('\\', Path.DirectorySeparatorChar);

                                                foreach (char c in Path.GetInvalidPathChars())
                                                {
                                                    safePath = safePath.Replace(c, '_');
                                                }

                                                var pathParts = safePath.Split(Path.DirectorySeparatorChar);
                                                for (int i = 0; i < pathParts.Length; i++)
                                                {
                                                    foreach (char c in Path.GetInvalidFileNameChars())
                                                    {
                                                        pathParts[i] = pathParts[i].Replace(c, '_');
                                                    }
                                                }
                                                safePath = string.Join(Path.DirectorySeparatorChar, pathParts);

                                                var outputPath = Path.Combine(fullOutputPath, safePath + fileExtension);
                                                var outputDir = Path.GetDirectoryName(outputPath);

                                                if (!string.IsNullOrEmpty(outputDir))
                                                {
                                                    Directory.CreateDirectory(outputDir);
                                                }

                                                if (format == "json")
                                                {
                                                    var jsonExporter = new LuminaJsonExporter();
                                                    jsonExporter.Export(lgbData, outputPath);
                                                }
                                                else
                                                {
                                                    var textExporter = new LuminaTextExporter();
                                                    textExporter.Export(lgbData, outputPath);
                                                }

                                                lgbData = null;

                                                if (File.Exists(outputPath))
                                                {
                                                    var fileInfo = new FileInfo(outputPath);
                                                    if (fileInfo.Length > 0)
                                                    {
                                                        exported++;
                                                        _processedFileCount++;

                                                        if (!_isTerminating)
                                                        {
                                                            UltraAggressiveLuminaCleanup();
                                                        }

                                                        Console.WriteLine($"Wrote to file: {filePath}");
                                                    }
                                                    else
                                                    {
                                                        failed++;
                                                    }
                                                }
                                                else
                                                {
                                                    failed++;
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                failed++;
                                                Console.WriteLine($"Failed to export {filePath}: {ex.Message}");

                                                if (!_isTerminating)
                                                {
                                                    UltraAggressiveLuminaCleanup();
                                                }
                                            }
                                        },
                    onFileErrorCallback: (filePath, exception) =>
                    {
                        failed++;
                        Console.WriteLine($"✗ Failed to parse {filePath}: {exception.Message}");

                        UltraAggressiveLuminaCleanup();
                    }
                );

                Console.WriteLine($"\nLUMINA-OPTIMIZED STREAMING BATCH COMPLETE!");
                Console.WriteLine($"Successfully exported: {exported} files");
                Console.WriteLine($"Failed: {failed} files");
                Console.WriteLine($"Output location: {fullOutputPath}");

                if (Directory.Exists(fullOutputPath))
                {
                    var searchPattern = format == "json" ? "*.json" : "*.txt";
                    var actualFiles = Directory.GetFiles(fullOutputPath, searchPattern, SearchOption.AllDirectories);
                    Console.WriteLine($"Verification: {actualFiles.Length} {format.ToUpper()} files actually written to disk");

                    if (actualFiles.Length > 0)
                    {
                        Console.WriteLine($"Example files created:");
                        foreach (var file in actualFiles.Take(5))
                        {
                            var fileInfo = new FileInfo(file);
                            Console.WriteLine($"   {Path.GetRelativePath(fullOutputPath, file)} ({fileInfo.Length} bytes)");
                        }
                    }
                }

                Console.WriteLine("Final Lumina cleanup...");
                UltraAggressiveLuminaCleanup();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lumina streaming batch failed: {ex.Message}");
                UltraAggressiveLuminaCleanup();     
            }
        }

        private static void HandleBatchZoneCommand(GameLgbReader reader, string[] args)
        {
            var zoneIndex = Array.IndexOf(args, "--batch-zone");
            if (zoneIndex >= args.Length - 1)
            {
                Console.WriteLine("Error: --batch-zone requires a zone name");
                return;
            }

            string zoneName = args[zoneIndex + 1];
            string outputFolder = $"zone_{zoneName}_output";
            string format = "text";

            int currentArgIndex = zoneIndex + 2;

            if (currentArgIndex < args.Length && !args[currentArgIndex].StartsWith("--"))
            {
                outputFolder = args[currentArgIndex];
                currentArgIndex++;
            }

            if (currentArgIndex < args.Length && !args[currentArgIndex].StartsWith("--"))
            {
                format = args[currentArgIndex].ToLower();
            }

            Console.WriteLine($"Batch processing zone '{zoneName}' to: {outputFolder}");
            Console.WriteLine($"Output format: {format.ToUpper()}");

            Dictionary<string, LgbData> results;
            try
            {
                results = reader.ParseLgbFilesByZoneWithCancellation(zoneName, _cancellationTokenSource.Token);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Zone parsing failed: {ex.Message}");
                return;
            }

            try
            {
                var fullOutputPath = Path.GetFullPath(outputFolder);
                Directory.CreateDirectory(fullOutputPath);

                int exported = 0;
                string fileExtension = format == "json" ? ".json" : ".txt";

                foreach (var kvp in results)
                {
                    try
                    {
                        _cancellationTokenSource.Token.ThrowIfCancellationRequested();

                        var safePath = kvp.Key.Replace('/', Path.DirectorySeparatorChar);
                        var outputPath = Path.Combine(fullOutputPath, safePath + fileExtension);

                        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

                        if (format == "json")
                        {
                            var jsonExporter = new LuminaJsonExporter();
                            jsonExporter.Export(kvp.Value, outputPath);
                        }
                        else
                        {
                            var textExporter = new LuminaTextExporter();
                            textExporter.Export(kvp.Value, outputPath);
                        }

                        if (File.Exists(outputPath))
                        {
                            exported++;
                        }

                        AggressiveLuminaCleanup();
                    }
                    catch (OperationCanceledException)
                    {
                        Console.WriteLine($"\n⚠️  Zone processing cancelled at {exported} files.");
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ Failed to export {kvp.Key}: {ex.Message}");
                        AggressiveLuminaCleanup();    
                    }
                }

                Console.WriteLine($"\n🎉 Zone processing complete!");
                Console.WriteLine($"✅ Exported {exported} {format.ToUpper()} files to: {fullOutputPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Zone export failed: {ex.Message}");
            }
        }

        private static void HandleBatchTypeCommand(GameLgbReader reader, string[] args)
        {
            var typeIndex = Array.IndexOf(args, "--batch-type");
            if (typeIndex >= args.Length - 1)
            {
                Console.WriteLine("Error: --batch-type requires a file type");
                return;
            }

            string fileType = args[typeIndex + 1];
            string outputFolder = $"type_{fileType}_output";
            string format = "text";

            int currentArgIndex = typeIndex + 2;

            if (currentArgIndex < args.Length && !args[currentArgIndex].StartsWith("--"))
            {
                outputFolder = args[currentArgIndex];
                currentArgIndex++;
            }

            if (currentArgIndex < args.Length && !args[currentArgIndex].StartsWith("--"))
            {
                format = args[currentArgIndex].ToLower();
            }

            Console.WriteLine($"Batch processing '{fileType}' files to: {outputFolder}");
            Console.WriteLine($"Output format: {format.ToUpper()}");

            Dictionary<string, LgbData> results;
            try
            {
                results = reader.ParseLgbFilesByTypeWithCancellation(fileType, _cancellationTokenSource.Token);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Type parsing failed: {ex.Message}");
                return;
            }

            try
            {
                var fullOutputPath = Path.GetFullPath(outputFolder);
                Directory.CreateDirectory(fullOutputPath);

                int exported = 0;
                string fileExtension = format == "json" ? ".json" : ".txt";

                foreach (var kvp in results)
                {
                    try
                    {
                        _cancellationTokenSource.Token.ThrowIfCancellationRequested();

                        var safePath = kvp.Key.Replace('/', Path.DirectorySeparatorChar);
                        var outputPath = Path.Combine(fullOutputPath, safePath + fileExtension);

                        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

                        if (format == "json")
                        {
                            var jsonExporter = new LuminaJsonExporter();
                            jsonExporter.Export(kvp.Value, outputPath);
                        }
                        else
                        {
                            var textExporter = new LuminaTextExporter();
                            textExporter.Export(kvp.Value, outputPath);
                        }

                        if (File.Exists(outputPath))
                        {
                            exported++;
                        }

                        AggressiveLuminaCleanup();
                    }
                    catch (OperationCanceledException)
                    {
                        Console.WriteLine($"\n⚠️  Type processing cancelled at {exported} files.");
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ Failed to export {kvp.Key}: {ex.Message}");
                        AggressiveLuminaCleanup();    
                    }
                }

                Console.WriteLine($"\n🎉 Type processing complete!");
                Console.WriteLine($"✅ Exported {exported} {format.ToUpper()} files to: {fullOutputPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Type export failed: {ex.Message}");
            }
        }

        private static void HandleAnalyzeCommand(GameLgbReader reader, string[] args)
        {
            var analyzeIndex = Array.IndexOf(args, "--analyze");
            if (analyzeIndex >= args.Length - 1)
            {
                Console.WriteLine("Error: --analyze requires an LGB file path");
                return;
            }

            string lgbFilePath = args[analyzeIndex + 1];

            Console.WriteLine($"Enhanced Detailed Analysis of LGB File");
            Console.WriteLine("================================================================================");
            Console.WriteLine($"File: {lgbFilePath}");
            Console.WriteLine($"Analysis Time: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            Console.WriteLine($"Analyzed By: MalekBael");
            Console.WriteLine($"Parser: Enhanced Lumina Integration");
            Console.WriteLine();

            if (!CheckMemoryUsage())
            {
                Console.WriteLine("❌ Insufficient memory for Lumina analysis. Please close other applications and try again.");
                return;
            }

            try
            {
                var data = reader.ParseLgbFile(lgbFilePath);

                var safeFileName = lgbFilePath.Replace('/', '_').Replace('\\', '_');
                var outputPath = $"analysis_{safeFileName}_{DateTime.Now:yyyyMMdd_HHmmss}.txt";

                var detailedAnalysis = $"Enhanced LGB File Detailed Analysis\n" +
                                     $"===================================\n" +
                                     $"File: {lgbFilePath}\n" +
                                     $"Analysis Time: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC\n" +
                                     $"Analyzed By: MalekBael\n" +
                                     $"Tool Version: LGB Parser v2.0 - Enhanced\n" +
                                     $"Parser: GameLgbReader with Lumina Integration\n\n";

                File.WriteAllText(outputPath, detailedAnalysis);

                var exporter = new LuminaTextExporter();
                exporter.Export(data, outputPath + ".temp");

                var analysisContent = File.ReadAllText(outputPath + ".temp");
                File.AppendAllText(outputPath, analysisContent);
                File.Delete(outputPath + ".temp");

                Console.WriteLine(File.ReadAllText(outputPath));

                Console.WriteLine($"\nDetailed analysis saved to: {outputPath}");

                Console.WriteLine("\n🔧 ENHANCED ANALYSIS FEATURES");
                Console.WriteLine("=============================");
                Console.WriteLine("This analysis includes:");
                Console.WriteLine("  - ALL LGB entry types support");
                Console.WriteLine("  - Enhanced BaseId extraction for EventNPC, EventObject, Aetheryte, Treasure");
                Console.WriteLine("  - Layer-level metadata (IsBushLayer, IsHousing, PS3Visible, etc.)");
                Console.WriteLine("  - Comprehensive object data parsing");
                Console.WriteLine("  - Direct Lumina integration for maximum compatibility");

                if (data.Layers.Length > 0)
                {
                    Console.WriteLine($"\nLayer Information:");
                    Console.WriteLine($"  Total Layers: {data.Layers.Length}");

                    var totalObjects = data.Layers.Sum(layer => layer.InstanceObjects.Length);
                    Console.WriteLine($"  Total Objects: {totalObjects}");

                    if (data.Metadata.ContainsKey("EnhancedObjectData"))
                    {
                        var enhancedData = (Dictionary<uint, Dictionary<string, object>>)data.Metadata["EnhancedObjectData"];
                        Console.WriteLine($"  Enhanced Object Data Entries: {enhancedData.Count}");
                    }
                }

                UltraAggressiveLuminaCleanup();
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

                UltraAggressiveLuminaCleanup();
            }
        }

        private static void HandleSingleGameFileCommand(GameLgbReader reader, string[] args, int gameIndex)
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

            if (!CheckMemoryUsage())
            {
                Console.WriteLine("❌ Insufficient memory for Lumina parsing. Please close other applications and try again.");
                return;
            }

            try
            {
                var data = reader.ParseLgbFile(lgbFilePath);

                if (format.ToLower() == "json")
                {
                    var jsonExporter = new LuminaJsonExporter();
                    jsonExporter.Export(data, outputPath);
                }
                else
                {
                    var textExporter = new LuminaTextExporter();
                    textExporter.Export(data, outputPath);
                }

                Console.WriteLine($"✅ Successfully parsed and exported to: {outputPath}");

                UltraAggressiveLuminaCleanup();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to parse file: {ex.Message}");
                UltraAggressiveLuminaCleanup();    
                ForceTermination(1);
            }
        }

        private static void HandleFileMode(string[] args)
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

            Console.WriteLine($"File Mode Processing (Limited Support)");
            Console.WriteLine($"Input: {inputPath}");
            Console.WriteLine($"Output: {outputPath}");
            Console.WriteLine($"Format: {format}");
            Console.WriteLine();

            Console.WriteLine("⚠️  WARNING: File mode provides limited parsing compared to Game mode!");
            Console.WriteLine("   For best results, use Game mode: --game <game_path>");
            Console.WriteLine();

            try
            {
                if (File.Exists(inputPath))
                {
                    HandleSingleFile(inputPath, outputPath, format);
                }
                else if (Directory.Exists(inputPath))
                {
                    HandleFolderProcessing(inputPath, outputPath, format);
                }
                else
                {
                    Console.WriteLine($"Error: Input path does not exist: {inputPath}");
                    ForceTermination(1);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Processing failed: {ex.Message}");
                ForceTermination(1);
            }
        }

        private static void HandleSingleFile(string inputPath, string outputPath, string format)
        {
            Console.WriteLine($"Processing single file...");

            try
            {
                Console.WriteLine("⚠️  File mode parsing is not fully supported in this version.");
                Console.WriteLine("   Please use Game mode for enhanced parsing: --game <your_ffxiv_path>");
                Console.WriteLine("   File mode may not extract all object data properly.");
                Console.WriteLine();

                var dummyData = CreateDummyLgbDataFromFile(inputPath);

                if (format.ToLower() == "json")
                {
                    var exporter = new LuminaJsonExporter();
                    exporter.Export(dummyData, outputPath);
                }
                else
                {
                    var exporter = new LuminaTextExporter();
                    exporter.Export(dummyData, outputPath);
                }

                Console.WriteLine($"✅ File processed with limited parsing: {outputPath}");
                Console.WriteLine("   For enhanced parsing, use Game mode instead.");

                AggressiveLuminaCleanup();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to process file: {ex.Message}");
                Console.WriteLine("   Consider using Game mode for better compatibility.");
                ForceTermination(1);
            }
        }

        private static void HandleFolderProcessing(string inputFolder, string outputFolder, string format)
        {
            Console.WriteLine($"Processing folder...");

            var lgbFiles = Directory.GetFiles(inputFolder, "*.lgb", SearchOption.AllDirectories);

            if (lgbFiles.Length == 0)
            {
                Console.WriteLine("No LGB files found in the input folder.");
                return;
            }

            Console.WriteLine($"Found {lgbFiles.Length} LGB files");
            Console.WriteLine("⚠️  WARNING: Folder processing uses limited file-based parsing.");
            Console.WriteLine("   For enhanced results, extract files and use Game mode instead.");
            Console.WriteLine();

            Directory.CreateDirectory(outputFolder);

            int processed = 0;
            int failed = 0;

            foreach (var lgbFile in lgbFiles)
            {
                try
                {
                    _cancellationTokenSource.Token.ThrowIfCancellationRequested();

                    if (!CheckMemoryUsage())
                    {
                        Console.WriteLine($"\n🚨 LUMINA memory limit reached. Stopping at {processed} files.");
                        break;
                    }

                    var relativePath = Path.GetRelativePath(inputFolder, lgbFile);
                    var outputExtension = format.ToLower() == "json" ? ".json" : ".txt";
                    var outputPath = Path.Combine(outputFolder, Path.ChangeExtension(relativePath, outputExtension));

                    Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

                    var data = CreateDummyLgbDataFromFile(lgbFile);

                    if (format.ToLower() == "json")
                    {
                        var exporter = new LuminaJsonExporter();
                        exporter.Export(data, outputPath);
                    }
                    else
                    {
                        var exporter = new LuminaTextExporter();
                        exporter.Export(data, outputPath);
                    }

                    processed++;
                    Console.WriteLine($"✅ Processed: {relativePath}");

                    if (processed % 1 == 0)   
                    {
                        UltraAggressiveLuminaCleanup();
                    }

                    if (processed % 5 == 0)
                    {
                        Console.WriteLine($"📝 ✅ Processed {processed}/{lgbFiles.Length} files... (ultra aggressive Lumina cleanup)");
                    }
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine($"\n⚠️  Folder processing cancelled at {processed} files.");
                    break;
                }
                catch (OutOfMemoryException)
                {
                    Console.WriteLine($"\n🚨 Out of memory at {processed} files. Stopping.");
                    EmergencyLuminaCleanup();
                    break;
                }
                catch (Exception ex)
                {
                    failed++;
                    Console.WriteLine($"❌ Failed: {lgbFile} - {ex.Message}");
                    UltraAggressiveLuminaCleanup();    
                }
            }

            Console.WriteLine($"\nFolder processing complete!");
            Console.WriteLine($"Processed: {processed} files");
            Console.WriteLine($"Failed: {failed} files");
            Console.WriteLine($"Output folder: {outputFolder}");
            Console.WriteLine();
            Console.WriteLine("ℹ️  For enhanced parsing with full object data, use Game mode instead.");

            UltraAggressiveLuminaCleanup();
        }

        private static LgbData CreateDummyLgbDataFromFile(string filePath)
        {
            return new LgbData
            {
                FilePath = filePath,
                Layers = new Lumina.Data.Parsing.Layer.LayerCommon.Layer[0],
                Metadata = new Dictionary<string, object>
                {
                    ["ParsedBy"] = "Limited File Mode",
                    ["ParsedAt"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC"),
                    ["Warning"] = "File mode provides limited parsing. Use Game mode for full functionality.",
                    ["FileSize"] = new FileInfo(filePath).Length,
                    ["LuminaOptimized"] = "Ultra Aggressive Memory Management Active"
                }
            };
        }
    }
}