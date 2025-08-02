using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Lumina;
using Lumina.Data.Files;

namespace LgbParser
{
    public class GameLgbReader : IDisposable
    {
        private readonly GameData _gameData;
        private bool _disposed = false;
        private List<string> _discoveredLgbPaths = null;

        // Zone directories extracted from your lgb_file_paths.txt
        private static readonly string[] KnownZones = {
            "air_a1",
            "fst_f1",
            "lak_l1",
            "ocn_o1",
            "roc_r1",
            "sea_s1",
            "wil_w1",
            "zon_z1"
        };

        // Common area types found in the file
        private static readonly string[] AreaTypes = {
            "bah", "cnt", "dun", "evt", "fld", "hou", "ind", "pvp", "rad", "twn", "chr", "jai"
        };

        // File types found in the structure
        private static readonly string[] FileTypes = {
            "bg", "planevent", "planlive", "planmap", "planner", "sound", "vfx"
        };

        // Known problematic file types that often have parsing issues
        private static readonly string[] ProblematicFileTypes = {
            "planner" // These files seem to have parsing issues
        };

        public GameLgbReader(string gameInstallPath)
        {
            var sqpackPath = Path.Combine(gameInstallPath, "game", "sqpack");
            if (!Directory.Exists(sqpackPath))
            {
                throw new DirectoryNotFoundException($"FFXIV sqpack directory not found at: {sqpackPath}");
            }

            _gameData = new GameData(sqpackPath);
        }

        /// <summary>
        /// Safely parse an LGB file with comprehensive error handling
        /// </summary>
        public LgbData ParseLgbFile(string gamePath)
        {
            try
            {
                // First, check if the file exists
                if (!_gameData.FileExists(gamePath))
                {
                    throw new FileNotFoundException($"LGB file not found: {gamePath}");
                }

                Console.WriteLine($"  Parsing {gamePath}");

                // Attempt to load the LGB file using Lumina
                var lgbFile = SafeLoadLgbFile(gamePath);
                if (lgbFile == null)
                {
                    throw new InvalidDataException($"Lumina failed to load LGB file: {gamePath}");
                }

                // Convert to our format
                return ParseLgbFromLuminaFile(lgbFile);
            }
            catch (Exception ex)
            {
                // Wrap in a more descriptive exception
                throw new InvalidDataException($"Failed to parse LGB file '{gamePath}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Safely load an LGB file with multiple retry strategies
        /// </summary>
        private LgbFile SafeLoadLgbFile(string gamePath)
        {
            LgbFile lgbFile = null;
            Exception lastException = null;

            // Strategy 1: Normal loading
            try
            {
                lgbFile = _gameData.GetFile<LgbFile>(gamePath);
                if (lgbFile != null)
                {
                    // Validate the loaded file has reasonable data
                    if (lgbFile.Layers != null && lgbFile.Layers.Length >= 0)
                    {
                        return lgbFile;
                    }
                }
            }
            catch (Exception ex)
            {
                lastException = ex;
                Console.WriteLine($"    Strategy 1 failed: {ex.Message}");
            }

            // Strategy 2: Try loading as raw file resource first
            try
            {
                var rawFile = _gameData.GetFile(gamePath);
                if (rawFile != null && rawFile.Data != null && rawFile.Data.Length > 0)
                {
                    Console.WriteLine($"    Raw file loaded successfully ({rawFile.Data.Length} bytes), retrying as LGB...");

                    // Try again with the raw file loaded
                    lgbFile = _gameData.GetFile<LgbFile>(gamePath);
                    if (lgbFile != null)
                    {
                        return lgbFile;
                    }
                }
            }
            catch (Exception ex)
            {
                lastException = ex;
                Console.WriteLine($"    Strategy 2 failed: {ex.Message}");
            }

            // Strategy 3: Check if it's a known problematic file type
            var fileName = Path.GetFileNameWithoutExtension(gamePath);
            if (ProblematicFileTypes.Contains(fileName, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine($"    File type '{fileName}' is known to be problematic, skipping...");
                return null; // Return null to indicate this file should be skipped
            }

            // If all strategies failed, re-throw the last exception
            if (lastException != null)
            {
                throw lastException;
            }

            return null;
        }

        /// <summary>
        /// Parse ALL available LGB files with robust error handling
        /// </summary>
        public Dictionary<string, LgbData> ParseAllLgbFiles(int maxFiles = 0)
        {
            var results = new Dictionary<string, LgbData>();
            var allFiles = GetAvailableLgbFiles();

            // If maxFiles is 0, parse all files
            var filesToProcess = maxFiles > 0 ? allFiles.Take(maxFiles).ToList() : allFiles;

            Console.WriteLine($"Parsing {filesToProcess.Count} LGB files from game installation...");

            int processed = 0;
            int failed = 0;
            int skipped = 0;

            var problemFiles = new List<string>();

            foreach (var path in filesToProcess)
            {
                try
                {
                    // Check if this is a known problematic file type
                    var fileName = Path.GetFileNameWithoutExtension(path);
                    if (ProblematicFileTypes.Contains(fileName, StringComparer.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"⚠ Skipping known problematic file: {path}");
                        skipped++;
                        continue;
                    }

                    var data = ParseLgbFile(path);
                    if (data != null)
                    {
                        results[path] = data;
                        processed++;
                    }
                    else
                    {
                        skipped++;
                        Console.WriteLine($"⚠ Skipped file (returned null): {path}");
                    }

                    if (processed % 10 == 0 && processed > 0)
                    {
                        Console.WriteLine($"Progress: {processed}/{filesToProcess.Count - skipped} files processed... (Skipped: {skipped}, Failed: {failed})");
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    problemFiles.Add(path);

                    // Log the error but continue processing
                    Console.WriteLine($"✗ Failed to parse {path}: {ex.Message}");

                    // If this is a parsing error with a negative count, it's likely a Lumina issue
                    if (ex.Message.Contains("must be a non-negative value"))
                    {
                        Console.WriteLine($"    This appears to be a Lumina parsing issue with file format");
                    }
                }
            }

            Console.WriteLine($"\nCompleted LGB file processing:");
            Console.WriteLine($"  Successfully processed: {processed}");
            Console.WriteLine($"  Skipped (known issues): {skipped}");
            Console.WriteLine($"  Failed: {failed}");
            Console.WriteLine($"  Total files attempted: {filesToProcess.Count}");

            if (problemFiles.Count > 0)
            {
                Console.WriteLine($"\nProblematic files:");
                foreach (var file in problemFiles.Take(10)) // Show first 10
                {
                    Console.WriteLine($"  - {file}");
                }
                if (problemFiles.Count > 10)
                {
                    Console.WriteLine($"  ... and {problemFiles.Count - 10} more");
                }
            }

            return results;
        }

        /// <summary>
        /// Parse all LGB files of a specific type with error handling
        /// </summary>
        public Dictionary<string, LgbData> ParseLgbFilesByType(string fileType, int maxFiles = 100)
        {
            var results = new Dictionary<string, LgbData>();
            var files = GetLgbFilesByType(fileType).Take(maxFiles);

            Console.WriteLine($"Parsing up to {maxFiles} {fileType} LGB files...");

            // Check if this is a problematic file type
            if (ProblematicFileTypes.Contains(fileType, StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine($"⚠ Warning: '{fileType}' files are known to have parsing issues");
                Console.WriteLine($"Some files may be skipped or fail to parse");
            }

            int processed = 0;
            int failed = 0;
            int skipped = 0;

            foreach (var path in files)
            {
                try
                {
                    var data = ParseLgbFile(path);
                    if (data != null)
                    {
                        results[path] = data;
                        processed++;
                        Console.WriteLine($"✓ Successfully parsed: {path}");
                    }
                    else
                    {
                        skipped++;
                        Console.WriteLine($"⚠ Skipped: {path}");
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    Console.WriteLine($"✗ Failed to parse {path}: {ex.Message}");
                }
            }

            Console.WriteLine($"Completed {fileType} files: {processed} successful, {skipped} skipped, {failed} failed");
            return results;
        }

        /// <summary>
        /// Parse all LGB files from a specific zone with error handling
        /// </summary>
        public Dictionary<string, LgbData> ParseLgbFilesByZone(string zoneName, int maxFiles = 50)
        {
            var results = new Dictionary<string, LgbData>();
            var files = GetLgbFilesByZone(zoneName).Take(maxFiles);

            Console.WriteLine($"Parsing up to {maxFiles} LGB files from zone '{zoneName}'...");

            int processed = 0;
            int failed = 0;
            int skipped = 0;

            foreach (var path in files)
            {
                try
                {
                    var data = ParseLgbFile(path);
                    if (data != null)
                    {
                        results[path] = data;
                        processed++;
                        Console.WriteLine($"✓ Successfully parsed: {path}");
                    }
                    else
                    {
                        skipped++;
                        Console.WriteLine($"⚠ Skipped: {path}");
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    Console.WriteLine($"✗ Failed to parse {path}: {ex.Message}");
                }
            }

            Console.WriteLine($"Completed zone '{zoneName}': {processed} successful, {skipped} skipped, {failed} failed");
            return results;
        }

        // ... (keep all the existing discovery methods unchanged)

        /// <summary>
        /// Dynamically discover all LGB files based on known zone patterns
        /// </summary>
        public List<string> DiscoverAllLgbFiles()
        {
            if (_discoveredLgbPaths != null)
            {
                Console.WriteLine($"Using cached LGB file list ({_discoveredLgbPaths.Count} files)");
                return _discoveredLgbPaths;
            }

            Console.WriteLine("Discovering LGB files using zone-based pattern testing...");
            var lgbFiles = new List<string>();

            try
            {
                lgbFiles = DiscoverLgbFilesByZonePatterns();

                // Cache the result
                _discoveredLgbPaths = lgbFiles.Distinct().ToList();
                Console.WriteLine($"Discovered {_discoveredLgbPaths.Count} LGB files total");

                return _discoveredLgbPaths;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during LGB file discovery: {ex.Message}");
                _discoveredLgbPaths = new List<string>();
                return _discoveredLgbPaths;
            }
        }

        /// <summary>
        /// Discover LGB files using the zone patterns extracted from your file list
        /// </summary>
        private List<string> DiscoverLgbFilesByZonePatterns()
        {
            var discoveredFiles = new List<string>();

            Console.WriteLine("Testing zone-based LGB file patterns...");

            int totalChecked = 0;
            int foundFiles = 0;

            foreach (var zone in KnownZones)
            {
                Console.WriteLine($"  Checking zone: {zone}");

                foreach (var areaType in AreaTypes)
                {
                    // Generate area IDs based on the patterns seen in your file
                    var areaIds = GenerateAreaIdsForZone(zone, areaType);

                    foreach (var areaId in areaIds)
                    {
                        foreach (var fileType in FileTypes)
                        {
                            var testPath = $"bg/ffxiv/{zone}/{areaType}/{areaId}/level/{fileType}.lgb";
                            totalChecked++;

                            if (totalChecked % 100 == 0)
                            {
                                Console.WriteLine($"    Checked {totalChecked} paths, found {foundFiles} files...");
                            }

                            try
                            {
                                if (LgbFileExists(testPath))
                                {
                                    discoveredFiles.Add(testPath);
                                    foundFiles++;
                                    Console.WriteLine($"    ✓ Found: {testPath}");
                                }
                            }
                            catch
                            {
                                // Ignore errors and continue
                            }
                        }
                    }
                }
            }

            Console.WriteLine($"Zone-based discovery complete: checked {totalChecked} paths, found {foundFiles} files");
            return discoveredFiles;
        }

        /// <summary>
        /// Generate area IDs for a specific zone and area type based on observed patterns
        /// </summary>
        private List<string> GenerateAreaIdsForZone(string zone, string areaType)
        {
            var areaIds = new List<string>();

            // Extract zone prefix and number (e.g., "sea" and "1" from "sea_s1")
            var parts = zone.Split('_');
            if (parts.Length < 2) return areaIds;

            var zonePrefix = parts[0]; // e.g., "sea"
            var zoneCode = parts[1]; // e.g., "s1"
            var zoneNumber = zoneCode.Length > 1 ? zoneCode.Substring(1) : "1"; // e.g., "1"
            var zoneLetter = zoneCode.Length > 0 ? zoneCode.Substring(0, 1) : "z"; // e.g., "s"

            var areaPrefix = areaType.Substring(0, 1); // e.g., "f" from "fld"

            // Based on your file patterns, generate common area ID patterns
            switch (zone)
            {
                case "air_a1":
                    if (areaType == "evt") areaIds.AddRange(new[] { "a1e2" });
                    break;

                case "fst_f1":
                    switch (areaType)
                    {
                        case "bah": areaIds.AddRange(new[] { "f1b1", "f1b2", "f1b3", "f1b4", "f1b5" }); break;
                        case "cnt": areaIds.AddRange(new[] { "f1c1", "f1c2", "f1c3", "f1c4" }); break;
                        case "dun": areaIds.AddRange(new[] { "f1d1", "f1d2", "f1d3", "f1d4", "f1d5", "f1d6", "f1d7", "f1d8" }); break;
                        case "evt": areaIds.AddRange(new[] { "f1e4", "f1e5", "f1e6", "f1e7" }); break;
                        case "fld": areaIds.AddRange(new[] { "f1f1", "f1f2", "f1f3", "f1f4", "f1fa", "f1fb", "f1fc" }); break;
                        case "hou": areaIds.AddRange(new[] { "f1h1" }); break;
                        case "ind": areaIds.AddRange(new[] { "f1i1", "f1i2", "f1i3", "f1i4", "f1i5" }); break;
                        case "rad": areaIds.AddRange(new[] { "f1r1" }); break;
                        case "twn": areaIds.AddRange(new[] { "f1t1", "f1t2", "f1ti" }); break;
                    }
                    break;

                case "lak_l1":
                    switch (areaType)
                    {
                        case "dun": areaIds.AddRange(new[] { "l1d1" }); break;
                        case "evt": areaIds.AddRange(new[] { "l1e1", "l1e2" }); break;
                        case "fld": areaIds.AddRange(new[] { "l1f1" }); break;
                        case "pvp": areaIds.AddRange(new[] { "l1p1" }); break;
                        case "rad": areaIds.AddRange(new[] { "l1r1", "l1r2", "l1r3" }); break;
                    }
                    break;

                case "ocn_o1":
                    switch (areaType)
                    {
                        case "evt": areaIds.AddRange(new[] { "o1e1", "o1e2" }); break;
                        case "fld": areaIds.AddRange(new[] { "o1fa" }); break;
                    }
                    break;

                case "roc_r1":
                    switch (areaType)
                    {
                        case "dun": areaIds.AddRange(new[] { "r1d1", "r1d2", "r1d3" }); break;
                        case "evt": areaIds.AddRange(new[] { "r1e1", "r1e2", "r1e3" }); break;
                        case "fld": areaIds.AddRange(new[] { "r1f1", "r1fa", "r1fb", "r1fc", "r1fd", "r1fe" }); break;
                        case "rad": areaIds.AddRange(new[] { "r1r1", "r1r2" }); break;
                    }
                    break;

                case "sea_s1":
                    switch (areaType)
                    {
                        case "bah": areaIds.AddRange(new[] { "s1b1", "s1b2", "s1b3", "s1b4", "s1b5", "s1b7" }); break;
                        case "dun": areaIds.AddRange(new[] { "s1d1", "s1d2", "s1d3", "s1d4", "s1d5", "s1d6", "s1d7", "s1d8", "s1d9", "s1da" }); break;
                        case "evt": areaIds.AddRange(new[] { "s1e4", "s1e5", "s1e6", "s1e7" }); break;
                        case "fld": areaIds.AddRange(new[] { "s1f1", "s1f2", "s1f3", "s1f4", "s1f5", "s1f6", "s1fa", "s1fb" }); break;
                        case "hou": areaIds.AddRange(new[] { "s1h1" }); break;
                        case "ind": areaIds.AddRange(new[] { "s1i1", "s1i2", "s1i3", "s1i4", "s1i5" }); break;
                        case "pvp": areaIds.AddRange(new[] { "s1p1", "s1p2", "s1p3", "s1p4", "s1p5" }); break;
                        case "twn": areaIds.AddRange(new[] { "s1t1", "s1t2", "s1ti" }); break;
                    }
                    break;

                case "wil_w1":
                    switch (areaType)
                    {
                        case "bah": areaIds.AddRange(new[] { "w1b1", "w1b2", "w1b3", "w1b4", "w1b5" }); break;
                        case "cnt": areaIds.AddRange(new[] { "w1c1" }); break;
                        case "dun": areaIds.AddRange(new[] { "w1d1", "w1d2", "w1d3", "w1d4", "w1d5", "w1d6", "w1d7", "w1d8" }); break;
                        case "evt": areaIds.AddRange(new[] { "w1e4", "w1e5", "w1e6", "w1e8", "w1e9", "w1ea", "w1eb", "w1ec", "w1ed" }); break;
                        case "fld": areaIds.AddRange(new[] { "w1f1", "w1f2", "w1f3", "w1f4", "w1f5", "w1fa", "w1fb", "w1fc" }); break;
                        case "hou": areaIds.AddRange(new[] { "w1h1" }); break;
                        case "ind": areaIds.AddRange(new[] { "w1i1", "w1i2", "w1i3", "w1i4", "w1i5" }); break;
                        case "rad": areaIds.AddRange(new[] { "w1r1" }); break;
                        case "twn": areaIds.AddRange(new[] { "w1t1", "w1t2", "w1ti" }); break;
                    }
                    break;

                case "zon_z1":
                    switch (areaType)
                    {
                        case "chr": areaIds.AddRange(new[] { "z1c1", "z1c2", "z1c3", "z1c4", "z1c5" }); break;
                        case "evt": areaIds.AddRange(new[] { "z1e1", "z1e2", "z1e3", "z1e4", "z1e5", "z1e6", "z1e7", "z1e8", "z1e9" }); break;
                        case "jai": areaIds.AddRange(new[] { "z1j1" }); break;
                    }
                    break;
            }

            return areaIds;
        }

        /// <summary>
        /// Get all available LGB files
        /// </summary>
        public List<string> GetAvailableLgbFiles()
        {
            return DiscoverAllLgbFiles();
        }

        /// <summary>
        /// Get LGB files filtered by zone/area
        /// </summary>
        public List<string> GetLgbFilesByZone(string zonePattern)
        {
            var allFiles = GetAvailableLgbFiles();
            return allFiles.Where(path => path.Contains(zonePattern, StringComparison.OrdinalIgnoreCase))
                          .ToList();
        }

        /// <summary>
        /// Get LGB files filtered by type (bg, planevent, planmap, etc.)
        /// </summary>
        public List<string> GetLgbFilesByType(string fileType)
        {
            var allFiles = GetAvailableLgbFiles();
            return allFiles.Where(path => path.EndsWith($"/{fileType}.lgb", StringComparison.OrdinalIgnoreCase))
                          .ToList();
        }

        /// <summary>
        /// Get LGB files by area type (fld, twn, dun, etc.)
        /// </summary>
        public List<string> GetLgbFilesByAreaType(string areaType)
        {
            var allFiles = GetAvailableLgbFiles();
            return allFiles.Where(path => path.Contains($"/{areaType}/", StringComparison.OrdinalIgnoreCase))
                          .ToList();
        }

        /// <summary>
        /// Check if an LGB file exists in the game installation
        /// </summary>
        public bool LgbFileExists(string gamePath)
        {
            return _gameData.FileExists(gamePath);
        }

        /// <summary>
        /// Parse multiple LGB files from common locations (for backward compatibility)
        /// </summary>
        public Dictionary<string, LgbData> ParseCommonLgbFiles(int maxFiles = 50)
        {
            return ParseAllLgbFiles(maxFiles);
        }

        /// <summary>
        /// Save discovered LGB paths to a file for future reference
        /// </summary>
        public void SaveDiscoveredPathsToFile(string filePath)
        {
            var allPaths = GetAvailableLgbFiles();
            File.WriteAllLines(filePath, allPaths);
            Console.WriteLine($"Saved {allPaths.Count} discovered LGB paths to: {filePath}");
        }

        // Include ParseLgbFromLuminaFile and other methods...
        private LgbData ParseLgbFromLuminaFile(LgbFile lgbFile)
        {
            var lgbData = new LgbData
            {
                FilePath = lgbFile.FilePath?.Path ?? "Unknown",
                LayerGroups = new List<LayerGroupData>(),
                Header = new FileHeader
                {
                    FileID = "LGB1",
                    FileSize = 0,
                    TotalChunkCount = 1
                },
                ChunkHeader = new LayerChunk
                {
                    ChunkId = "LYR1",
                    LayersCount = lgbFile.Layers.Length
                },
                Layers = new Layer[lgbFile.Layers.Length]
            };

            // Convert Lumina's LgbFile to our LgbData structure
            for (int i = 0; i < lgbFile.Layers.Length; i++)
            {
                var layer = lgbFile.Layers[i];

                var layerGroupData = new LayerGroupData
                {
                    LayerId = layer.LayerId,
                    Name = layer.Name,
                    InstanceObjects = new List<InstanceObjectData>()
                };

                var layerData = new Layer
                {
                    LayerId = layer.LayerId,
                    Name = layer.Name,
                    InstanceObjectCount = layer.InstanceObjects.Length,
                    InstanceObjects = new InstanceObject[layer.InstanceObjects.Length]
                };

                for (int j = 0; j < layer.InstanceObjects.Length; j++)
                {
                    var instanceObj = layer.InstanceObjects[j];

                    var instanceData = new InstanceObjectData
                    {
                        InstanceId = instanceObj.InstanceId,
                        Name = instanceObj.Name,
                        AssetType = instanceObj.AssetType.ToString(),
                        Transform = new TransformData
                        {
                            Translation = new float[]
                            {
                                instanceObj.Transform.Translation.X,
                                instanceObj.Transform.Translation.Y,
                                instanceObj.Transform.Translation.Z
                            },
                            Rotation = new float[]
                            {
                                instanceObj.Transform.Rotation.X,
                                instanceObj.Transform.Rotation.Y,
                                instanceObj.Transform.Rotation.Z,
                                0.0f
                            },
                            Scale = new float[]
                            {
                                instanceObj.Transform.Scale.X,
                                instanceObj.Transform.Scale.Y,
                                instanceObj.Transform.Scale.Z
                            }
                        },
                        ObjectData = ParseObjectDataFromLumina(instanceObj)
                    };

                    var instanceObjData = new InstanceObject
                    {
                        InstanceId = instanceObj.InstanceId,
                        Name = instanceObj.Name,
                        AssetType = (LayerEntryType)instanceObj.AssetType,
                        Transform = new Transformation
                        {
                            Translation = new Vector3
                            {
                                X = instanceObj.Transform.Translation.X,
                                Y = instanceObj.Transform.Translation.Y,
                                Z = instanceObj.Transform.Translation.Z
                            },
                            Rotation = new Vector4
                            {
                                X = instanceObj.Transform.Rotation.X,
                                Y = instanceObj.Transform.Rotation.Y,
                                Z = instanceObj.Transform.Rotation.Z,
                                W = 0.0f
                            },
                            Scale = new Vector3
                            {
                                X = instanceObj.Transform.Scale.X,
                                Y = instanceObj.Transform.Scale.Y,
                                Z = instanceObj.Transform.Scale.Z
                            }
                        },
                        ObjectData = ParseObjectDataFromLumina(instanceObj)
                    };

                    layerGroupData.InstanceObjects.Add(instanceData);
                    layerData.InstanceObjects[j] = instanceObjData;
                }

                lgbData.LayerGroups.Add(layerGroupData);
                lgbData.Layers[i] = layerData;
            }

            return lgbData;
        }

        private Dictionary<string, object> ParseObjectDataFromLumina(Lumina.Data.Parsing.Layer.LayerCommon.InstanceObject instanceObj)
        {
            var data = new Dictionary<string, object>();
            data["RotationType"] = "Euler";

            switch (instanceObj.AssetType)
            {
                case Lumina.Data.Parsing.Layer.LayerEntryType.EventNPC:
                    if (instanceObj.Object is Lumina.Data.Parsing.Layer.LayerCommon.ENPCInstanceObject enpc)
                    {
                        data["BaseId"] = enpc.ParentData.ParentData.BaseId;
                        data["PopWeather"] = enpc.ParentData.PopWeather;
                        data["PopTimeStart"] = enpc.ParentData.PopTimeStart;
                        data["PopTimeEnd"] = enpc.ParentData.PopTimeEnd;
                        data["Behavior"] = enpc.Behavior;
                    }
                    break;

                case Lumina.Data.Parsing.Layer.LayerEntryType.BG:
                    if (instanceObj.Object is Lumina.Data.Parsing.Layer.LayerCommon.BGInstanceObject bg)
                    {
                        data["AssetPath"] = bg.AssetPath ?? "Unknown";
                        data["CollisionAssetPath"] = bg.CollisionAssetPath ?? "None";
                        data["CollisionType"] = bg.CollisionType.ToString();
                        data["IsVisible"] = bg.IsVisible;
                        data["RenderShadowEnabled"] = bg.RenderShadowEnabled;
                        data["RenderLightShadowEnabled"] = bg.RenderLightShadowEnabled;
                        data["RenderModelClipRange"] = bg.RenderModelClipRange;
                    }
                    break;

                default:
                    data["Type"] = instanceObj.AssetType.ToString();
                    data["Note"] = "Object type parsed from Lumina";
                    break;
            }

            return data;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _gameData?.Dispose();
                }
                _disposed = true;
            }
        }
    }
}