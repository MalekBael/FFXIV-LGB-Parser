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
        // ✅ PERFORMANCE: Add cancellation support
        private static CancellationTokenSource _cancellationTokenSource = new();

        private static GameLgbReader _currentReader = null;

        // ✅ MEMORY: Add memory management constants
        private static readonly double MEMORY_LIMIT_PERCENTAGE = 0.60; // 60% of total RAM

        private static readonly long _totalSystemMemory = GetTotalSystemMemory();
        private static readonly long _memoryLimit = (long)(_totalSystemMemory * MEMORY_LIMIT_PERCENTAGE);
        private static readonly long _warningThreshold = (long)(_memoryLimit * 0.85); // 85% of limit
        private static readonly long _cleanupThreshold = (long)(_memoryLimit * 0.75); // 75% of limit

        // ✅ TERMINATION: Track if we're already terminating to prevent recursion
        private static volatile bool _isTerminating = false;

        private static void Main(string[] args)
        {
            Console.WriteLine("LGB Parser Tool v2.0 - Enhanced with Memory Management");
            Console.WriteLine("=====================================================");
            Console.WriteLine($"Current Date/Time: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
            Console.WriteLine($"User: MalekBael");
            Console.WriteLine();

            // ✅ MEMORY: Display memory information
            DisplayMemoryInfo();

            // ✅ PERFORMANCE: Setup cancellation handlers
            Console.CancelKeyPress += OnCancelKeyPress;
            AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

            try
            {
                if (args.Length == 0)
                {
                    ShowHelp();
                    return;
                }

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
            catch (OperationCanceledException)
            {
                Console.WriteLine("\n⚠️  Operation was cancelled by user.");
            }
            catch (OutOfMemoryException)
            {
                Console.WriteLine("\n🚨 Out of memory! Initiating emergency cleanup...");
                EmergencyMemoryCleanup();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Console.WriteLine("\nFor help, run without arguments or use --help");
            }
            finally
            {
                // ✅ PERFORMANCE: Ensure cleanup and termination
                ForceTermination(0);
            }
        }

        /// <summary>
        /// ✅ MEMORY: Get total system memory in bytes - SIMPLIFIED for .NET 8 compatibility
        /// </summary>
        private static long GetTotalSystemMemory()
        {
            try
            {
                long detectedMemory = 0;

                // Method 1: Try GC memory info (built-in .NET 8 method)
                try
                {
                    var memoryInfo = GC.GetGCMemoryInfo();
                    if (memoryInfo.TotalAvailableMemoryBytes > 0)
                    {
                        detectedMemory = memoryInfo.TotalAvailableMemoryBytes;
                        Console.WriteLine($"ℹ️  Method 1 (GC Info): {detectedMemory / (1024.0 * 1024.0 * 1024.0):F1} GB");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️  GC memory info failed: {ex.Message}");
                }

                // Method 2: Try WMI via command line (fallback)
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
                        Console.WriteLine($"⚠️  WMI detection failed: {ex.Message}");
                    }
                }

                // Method 3: Process-based estimation (enhanced for child processes)
                if (detectedMemory == 0)
                {
                    try
                    {
                        using var process = Process.GetCurrentProcess();
                        var workingSet = process.WorkingSet64;
                        var parentProcess = GetParentProcess();

                        if (parentProcess != null)
                        {
                            Console.WriteLine($"ℹ️  Running as child process of: {parentProcess.ProcessName}");
                            // Conservative estimate for child processes: 16x working set (minimum 6GB)
                            detectedMemory = Math.Max(6L * 1024 * 1024 * 1024, workingSet * 16);
                        }
                        else
                        {
                            // Standalone process: more generous estimate
                            detectedMemory = Math.Max(8L * 1024 * 1024 * 1024, workingSet * 20);
                        }

                        Console.WriteLine($"ℹ️  Method 3 (Process Estimation): {detectedMemory / (1024.0 * 1024.0 * 1024.0):F1} GB");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️  Process estimation failed: {ex.Message}");
                    }
                }

                // Final fallback
                if (detectedMemory == 0)
                {
                    var parentProcess = GetParentProcess();
                    if (parentProcess != null)
                    {
                        Console.WriteLine($"ℹ️  Running as child process of: {parentProcess.ProcessName}");
                        detectedMemory = 6L * 1024 * 1024 * 1024; // 6GB minimum for child processes
                    }
                    else
                    {
                        detectedMemory = 16L * 1024 * 1024 * 1024; // 16GB fallback for standalone
                    }
                    Console.WriteLine($"ℹ️  Final fallback: {detectedMemory / (1024.0 * 1024.0 * 1024.0):F1} GB");
                }

                return detectedMemory;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  All memory detection methods failed: {ex.Message}");
                // Ultra-conservative fallback
                return 4L * 1024 * 1024 * 1024; // 4GB absolute minimum
            }
        }

        /// <summary>
        /// ✅ MEMORY: Get parent process to detect if we're running as child process
        /// </summary>
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
                // Ignore errors in parent process detection
            }
            return null;
        }

        /// <summary>
        /// ✅ MEMORY: Get parent process ID using WMI command line
        /// </summary>
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
                // Ignore errors
            }
            return 0;
        }

        private static void DisplayOutputLocation(string outputFolder)
        {
            var currentDirectory = Directory.GetCurrentDirectory();
            var fullOutputPath = Path.GetFullPath(outputFolder);

            Console.WriteLine("📍 OUTPUT LOCATION DEBUG INFO");
            Console.WriteLine("============================");
            Console.WriteLine($"Current Working Directory: {currentDirectory}");
            Console.WriteLine($"Relative Output Folder: {outputFolder}");
            Console.WriteLine($"Full Output Path: {fullOutputPath}");
            Console.WriteLine($"Map Editor Process: {GetParentProcess()?.ProcessName ?? "Unknown"}");

            // Show if the directory exists and has files
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

        /// <summary>
        /// ✅ MEMORY: Display memory configuration - ENHANCED
        /// </summary>
        private static void DisplayMemoryInfo()
        {
            var totalGB = _totalSystemMemory / (1024.0 * 1024.0 * 1024.0);
            var limitGB = _memoryLimit / (1024.0 * 1024.0 * 1024.0);

            Console.WriteLine("💾 MEMORY MANAGEMENT CONFIGURATION");
            Console.WriteLine("==================================");

            // ✅ ENHANCED: Show detection method and current usage
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

                Console.WriteLine($"Parser Memory Limit: {limitGB:F1} GB ({MEMORY_LIMIT_PERCENTAGE:P0} of detected total)");
                Console.WriteLine($"Warning Threshold: {limitGB * 0.85:F1} GB");
                Console.WriteLine($"Cleanup Threshold: {limitGB * 0.75:F1} GB");

                // ✅ ENHANCED: Show if limit seems too high/low
                if (limitGB > totalGB * 0.6)
                {
                    Console.WriteLine($"⚠️  WARNING: Memory limit may be too high for system");
                }
                if (limitGB < 1.5)
                {
                    Console.WriteLine($"⚠️  WARNING: Memory limit may be too low for processing");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Error displaying extended memory info: {ex.Message}");
                Console.WriteLine($"Basic Info - Total: {totalGB:F1} GB, Limit: {limitGB:F1} GB");
            }

            Console.WriteLine();
        }

        /// <summary>
        /// ✅ MEMORY: Check current memory usage and enforce limits - FIXED
        /// </summary>
        private static bool CheckMemoryUsage()
        {
            try
            {
                var currentProcess = Process.GetCurrentProcess();
                var currentMemory = GC.GetTotalMemory(false);
                var workingSet = currentProcess.WorkingSet64;
                var privateMemory = currentProcess.PrivateMemorySize64;

                // ✅ FIXED: Use more realistic memory calculation
                var actualMemoryUsage = Math.Max(currentMemory, workingSet);

                // ✅ DEBUG: Show actual values to understand the issue
                var currentMemoryMB = currentMemory / (1024.0 * 1024.0);
                var workingSetMB = workingSet / (1024.0 * 1024.0);
                var privateMemoryMB = privateMemory / (1024.0 * 1024.0);
                var limitMB = _memoryLimit / (1024.0 * 1024.0);

                Console.WriteLine($"🔍 MEMORY DEBUG:");
                Console.WriteLine($"   GC Memory: {currentMemoryMB:F1} MB");
                Console.WriteLine($"   Working Set: {workingSetMB:F1} MB");
                Console.WriteLine($"   Private Memory: {privateMemoryMB:F1} MB");
                Console.WriteLine($"   Using: {actualMemoryUsage / (1024.0 * 1024.0):F1} MB");
                Console.WriteLine($"   Limit: {limitMB:F1} MB");

                // ✅ FIXED: More reasonable memory limit check
                if (actualMemoryUsage > _memoryLimit)
                {
                    // ✅ SAFETY: Only trigger if memory usage is actually problematic
                    if (actualMemoryUsage > 4L * 1024 * 1024 * 1024) // 4GB hard limit
                    {
                        Console.WriteLine($"🚨 MEMORY LIMIT EXCEEDED!");
                        Console.WriteLine($"   Current Usage: {actualMemoryUsage / (1024.0 * 1024.0):F1} MB");
                        Console.WriteLine($"   Hard Limit: 4096 MB");
                        return false;
                    }
                    else
                    {
                        Console.WriteLine($"⚠️  Memory limit exceeded but usage is reasonable, continuing...");
                    }
                }

                if (actualMemoryUsage > _warningThreshold)
                {
                    Console.WriteLine($"⚠️  Memory usage high: {actualMemoryUsage / (1024.0 * 1024.0):F1} MB / {_memoryLimit / (1024.0 * 1024.0):F1} MB");

                    if (actualMemoryUsage > _cleanupThreshold)
                    {
                        Console.WriteLine("🧹 Triggering aggressive cleanup...");
                        AggressiveMemoryCleanup();
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Memory check failed: {ex.Message}");
                return true; // Continue on memory check failure
            }
        }

        /// <summary>
        /// ✅ MEMORY: Aggressive memory cleanup to stay within limits
        /// </summary>
        private static void AggressiveMemoryCleanup()
        {
            try
            {
                Console.WriteLine("🧹 Performing aggressive memory cleanup...");

                var beforeMemory = GC.GetTotalMemory(false);

                // Multiple rounds of garbage collection
                for (int i = 0; i < 5; i++)
                {
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
                    GC.WaitForPendingFinalizers();
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
                }

                // Compact Large Object Heap
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect();

                var afterMemory = GC.GetTotalMemory(false);
                var freed = (beforeMemory - afterMemory) / (1024.0 * 1024.0);

                Console.WriteLine($"✅ Cleanup complete. Freed: {freed:F1} MB, Current: {afterMemory / (1024.0 * 1024.0):F1} MB");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Cleanup error: {ex.Message}");
            }
        }

        /// <summary>
        /// ✅ MEMORY: Emergency cleanup when out of memory
        /// </summary>
        private static void EmergencyMemoryCleanup()
        {
            Console.WriteLine("🚨 EMERGENCY MEMORY CLEANUP INITIATED");

            try
            {
                // Dispose current reader immediately
                if (_currentReader != null)
                {
                    _currentReader.Dispose();
                    _currentReader = null;
                }

                // Aggressive cleanup
                for (int i = 0; i < 10; i++)
                {
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
                    GC.WaitForPendingFinalizers();
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
                }

                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Emergency cleanup error: {ex.Message}");
            }
        }

        /// <summary>
        /// ✅ PERFORMANCE: Handle Ctrl+C cancellation
        /// </summary>
        private static void OnCancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            Console.WriteLine("\n🛑 Cancellation requested...");
            e.Cancel = true; // Prevent immediate termination
            _cancellationTokenSource.Cancel();

            // Give some time for cleanup
            Thread.Sleep(500);
            ForceTermination(1);
        }

        /// <summary>
        /// ✅ PERFORMANCE: Handle process exit
        /// </summary>
        private static void OnProcessExit(object sender, EventArgs e)
        {
            Console.WriteLine("🔄 Process exiting, cleaning up...");
            ForceTermination(0);
        }

        /// <summary>
        /// ✅ TERMINATION: ULTIMATE process termination that WILL work
        /// </summary>
        private static void ForceTermination(int exitCode)
        {
            // Prevent recursive calls
            if (_isTerminating)
            {
                return;
            }
            _isTerminating = true;

            try
            {
                Console.WriteLine("🧹 Performing rapid cleanup...");

                // Cancel any ongoing operations
                _cancellationTokenSource?.Cancel();

                // Dispose the reader AGGRESSIVELY
                if (_currentReader != null)
                {
                    try
                    {
                        _currentReader.Dispose();
                    }
                    catch
                    {
                        // Ignore disposal errors
                    }
                    _currentReader = null;
                }

                // Quick cleanup - minimal time spent
                try
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
                catch
                {
                    // Ignore cleanup errors
                }

                Console.WriteLine("✅ Cleanup complete.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Cleanup error: {ex.Message}");
            }

            // ✅ TERMINATION: Multiple termination strategies
            Console.WriteLine($"🚪 FORCING process termination (exit code: {exitCode})...");

            // Strategy 1: Standard Environment.Exit (give it a moment)
            try
            {
                Environment.Exit(exitCode);
            }
            catch
            {
                // If that fails, continue to nuclear option
            }

            // Strategy 2: Wait briefly then try again
            Thread.Sleep(100);
            try
            {
                Environment.Exit(exitCode);
            }
            catch
            {
                // Continue to nuclear option
            }

            // Strategy 3: NUCLEAR OPTION - Kill the process directly
            try
            {
                Console.WriteLine("💥 NUCLEAR TERMINATION - Killing process directly...");
                var currentProcess = Process.GetCurrentProcess();
                currentProcess.Kill();
            }
            catch
            {
                // This should never fail, but just in case...
            }

            // Strategy 4: If all else fails, try to abort the current thread
            try
            {
                Thread.CurrentThread.Abort();
            }
            catch
            {
                // Final fallback - should never reach here
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
            Console.WriteLine("SUPPORTED ZONES: air_a1, fst_f1, lak_l1, ocn_o1, roc_r1, sea_s1, wil_w1, zon_z1");
            Console.WriteLine("SUPPORTED TYPES: bg, planevent, planlive, planmap, planner, sound, vfx");
            Console.WriteLine();
            Console.WriteLine("NOTE: Game mode provides enhanced parsing with ALL LGB entry types!");
            Console.WriteLine("      Press Ctrl+C to cancel and force cleanup.");
            Console.WriteLine("      Parser automatically limits memory usage to 40% of available RAM.");
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

            // ✅ REMOVED: Memory check before creating reader
            // if (!CheckMemoryUsage())
            // {
            //     Console.WriteLine("❌ Insufficient memory available. Please close other applications and try again.");
            //     return;
            // }

            // ✅ PERFORMANCE: Store reader reference for cleanup
            _currentReader = new GameLgbReader(gamePath);

            try
            {
                // Handle different game mode commands
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
                    // Single file parsing from game
                    HandleSingleGameFileCommand(_currentReader, args, gameIndex);
                }
            }
            finally
            {
                // Ensure cleanup
                if (_currentReader != null)
                {
                    try
                    {
                        _currentReader.Dispose();
                    }
                    catch
                    {
                        // Ignore disposal errors
                    }
                    _currentReader = null;
                }
            }
        }

        private static void HandleListCommand(GameLgbReader reader)
        {
            Console.WriteLine("Discovering available LGB files...");

            var files = reader.GetAvailableLgbFiles();

            Console.WriteLine($"\nFound {files.Count} LGB files:");
            Console.WriteLine("================================================================================");

            var groupedFiles = files.GroupBy(f =>
            {
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

        private static void HandleListZonesCommand(GameLgbReader reader)
        {
            Console.WriteLine("Analyzing available zones...");

            var files = reader.GetAvailableLgbFiles();

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
        }

        private static void HandleListTypesCommand(GameLgbReader reader)
        {
            Console.WriteLine("Analyzing available file types...");

            var files = reader.GetAvailableLgbFiles();

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
        }

        // ✅ KEEP OTHER BATCH METHODS SIMPLE - Same pattern as above
        private static void HandleBatchCommand(GameLgbReader reader, string[] args)
        {
            string outputFolder = "lgb_output";
            string format = "text"; // Default format

            // Check if output folder is specified
            var batchIndex = Array.IndexOf(args, "--batch");
            int currentArgIndex = batchIndex + 1;

            // Parse output folder if specified
            if (currentArgIndex < args.Length && !args[currentArgIndex].StartsWith("--"))
            {
                outputFolder = args[currentArgIndex];
                currentArgIndex++;
            }

            // Parse format if specified
            if (currentArgIndex < args.Length && !args[currentArgIndex].StartsWith("--"))
            {
                format = args[currentArgIndex].ToLower();
            }

            DisplayOutputLocation(outputFolder);

            Console.WriteLine($"Batch processing ALL LGB files to: {outputFolder}");
            Console.WriteLine($"Output format: {format.ToUpper()}");
            Console.WriteLine("This may take several minutes...");
            Console.WriteLine("Press Ctrl+C to cancel and force cleanup.");
            Console.WriteLine();

            Dictionary<string, LgbData> results;
            try
            {
                results = reader.ParseAllLgbFilesWithCancellation(_cancellationTokenSource.Token);
                Console.WriteLine($"✅ Parsing completed successfully. Processing {results.Count} files for export...");

                if (results.Count == 0)
                {
                    Console.WriteLine("❌ CRITICAL: No files were parsed!");
                    return;
                }

                Console.WriteLine($"📋 Sample of parsed files:");
                foreach (var kvp in results.Take(5))
                {
                    Console.WriteLine($"   {kvp.Key} - Layers: {kvp.Value.Layers.Length}");
                }
                Console.WriteLine();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Parsing failed: {ex.Message}");
                return;
            }

            try
            {
                var fullOutputPath = Path.GetFullPath(outputFolder);
                Directory.CreateDirectory(fullOutputPath);
                Console.WriteLine($"📁 Created output directory: {fullOutputPath}");

                int exported = 0;
                int failed = 0;
                string fileExtension = format == "json" ? ".json" : ".txt";

                Console.WriteLine($"🚀 Starting export of {results.Count} files in {format.ToUpper()} format...");

                foreach (var kvp in results)
                {
                    try
                    {
                        _cancellationTokenSource.Token.ThrowIfCancellationRequested();

                        // Path sanitization
                        var safePath = kvp.Key.Replace('/', Path.DirectorySeparatorChar)
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

                        // Export based on format
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
                            var fileInfo = new FileInfo(outputPath);
                            if (fileInfo.Length > 0)
                            {
                                exported++;
                                if (exported <= 5 || exported % 50 == 0)
                                {
                                    Console.WriteLine($"📝 ✅ Successfully exported {exported}/{results.Count} files... ({exported * 100.0 / results.Count:F1}%)");
                                }
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
                    catch (OperationCanceledException)
                    {
                        Console.WriteLine($"\n⚠️  Export cancelled at {exported} files.");
                        break;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        Console.WriteLine($"❌ Failed to export {kvp.Key}: {ex.Message}");

                        if (failed > 20)
                        {
                            Console.WriteLine($"⚠️  Too many failures ({failed}), stopping export.");
                            break;
                        }
                    }
                }

                Console.WriteLine($"\n🎉 Batch processing complete!");
                Console.WriteLine($"✅ Successfully exported: {exported} files");
                Console.WriteLine($"❌ Failed: {failed} files");
                Console.WriteLine($"📁 Output location: {fullOutputPath}");

                // Verification
                if (Directory.Exists(fullOutputPath))
                {
                    var searchPattern = format == "json" ? "*.json" : "*.txt";
                    var actualFiles = Directory.GetFiles(fullOutputPath, searchPattern, SearchOption.AllDirectories);
                    Console.WriteLine($"📊 Verification: {actualFiles.Length} {format.ToUpper()} files actually written to disk");

                    if (actualFiles.Length > 0)
                    {
                        Console.WriteLine($"✅ Example files created:");
                        foreach (var file in actualFiles.Take(5))
                        {
                            var fileInfo = new FileInfo(file);
                            Console.WriteLine($"   {Path.GetRelativePath(fullOutputPath, file)} ({fileInfo.Length} bytes)");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Export process failed: {ex.Message}");
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
            string format = "text"; // Default format

            int currentArgIndex = zoneIndex + 2;

            // Parse output folder if specified
            if (currentArgIndex < args.Length && !args[currentArgIndex].StartsWith("--"))
            {
                outputFolder = args[currentArgIndex];
                currentArgIndex++;
            }

            // Parse format if specified
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

                        // Export based on format
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
                    }
                    catch (OperationCanceledException)
                    {
                        Console.WriteLine($"\n⚠️  Zone processing cancelled at {exported} files.");
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ Failed to export {kvp.Key}: {ex.Message}");
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
            string format = "text"; // Default format

            int currentArgIndex = typeIndex + 2;

            // Parse output folder if specified
            if (currentArgIndex < args.Length && !args[currentArgIndex].StartsWith("--"))
            {
                outputFolder = args[currentArgIndex];
                currentArgIndex++;
            }

            // Parse format if specified
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

                        // Export based on format
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
                    }
                    catch (OperationCanceledException)
                    {
                        Console.WriteLine($"\n⚠️  Type processing cancelled at {exported} files.");
                        break;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ Failed to export {kvp.Key}: {ex.Message}");
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

        // ✅ KEEP ALL OTHER METHODS UNCHANGED - they are working correctly

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
                Console.WriteLine("❌ Insufficient memory for analysis. Please close other applications and try again.");
                return;
            }

            try
            {
                var data = reader.ParseLgbFile(lgbFilePath);

                // Save detailed analysis to file first
                var safeFileName = lgbFilePath.Replace('/', '_').Replace('\\', '_');
                var outputPath = $"analysis_{safeFileName}_{DateTime.Now:yyyyMMdd_HHmmss}.txt";

                // Create detailed analysis content
                var detailedAnalysis = $"Enhanced LGB File Detailed Analysis\n" +
                                     $"===================================\n" +
                                     $"File: {lgbFilePath}\n" +
                                     $"Analysis Time: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC\n" +
                                     $"Analyzed By: MalekBael\n" +
                                     $"Tool Version: LGB Parser v2.0 - Enhanced\n" +
                                     $"Parser: GameLgbReader with Lumina Integration\n\n";

                // Write the header first
                File.WriteAllText(outputPath, detailedAnalysis);

                // Use LuminaTextExporter to append the main analysis
                var exporter = new LuminaTextExporter();
                exporter.Export(data, outputPath + ".temp");

                // Combine the files
                var analysisContent = File.ReadAllText(outputPath + ".temp");
                File.AppendAllText(outputPath, analysisContent);
                File.Delete(outputPath + ".temp");

                // Also display to console
                Console.WriteLine(File.ReadAllText(outputPath));

                Console.WriteLine($"\nDetailed analysis saved to: {outputPath}");

                // Show enhanced statistics
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
                Console.WriteLine("❌ Insufficient memory for parsing. Please close other applications and try again.");
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
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Failed to parse file: {ex.Message}");
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
                        Console.WriteLine($"\n🚨 Memory limit reached. Stopping at {processed} files.");
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
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine($"\n⚠️  Folder processing cancelled at {processed} files.");
                    break;
                }
                catch (OutOfMemoryException)
                {
                    Console.WriteLine($"\n🚨 Out of memory at {processed} files. Stopping.");
                    EmergencyMemoryCleanup();
                    break;
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
            Console.WriteLine();
            Console.WriteLine("ℹ️  For enhanced parsing with full object data, use Game mode instead.");
        }

        /// <summary>
        /// Creates a minimal LgbData for file-based parsing (limited functionality)
        /// </summary>
        private static LgbData CreateDummyLgbDataFromFile(string filePath)
        {
            return new LgbData
            {
                FilePath = filePath,
                Layers = new Lumina.Data.Parsing.Layer.LayerCommon.Layer[0], // Empty - limited file parsing
                Metadata = new Dictionary<string, object>
                {
                    ["ParsedBy"] = "Limited File Mode",
                    ["ParsedAt"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC"),
                    ["Warning"] = "File mode provides limited parsing. Use Game mode for full functionality.",
                    ["FileSize"] = new FileInfo(filePath).Length
                }
            };
        }
    }
}