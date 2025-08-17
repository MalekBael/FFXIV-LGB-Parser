using Lumina;
using Lumina.Data;
using Lumina.Data.Files;
using Lumina.Data.Parsing.Layer;
using System;
using Lumina.Data.Structs;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Runtime;
using System.Reflection;

namespace LgbParser
{
    public class GameLgbReader : IDisposable
    {
        private readonly GameData _gameData;
        private bool _disposed = false;
        private List<string> _discoveredLgbPaths = null;

        private Dictionary<string, LgbFile> _lgbFileCache = new();

        private static readonly string[] BaseZonePrefixes = {
            "air", "fst", "lak", "ocn", "roc", "sea", "wil", "zon",
        };

        private static readonly string[] BaseZoneSuffixes = {
            "a1", "f1", "l1", "o1", "r1", "s1", "w1", "z1", "e1", "m1", "g1", "c1"
        };

        private static readonly string[] AreaTypes = {
            "bah", "cnt", "dun", "evt", "fld", "hou", "ind", "pvp", "rad", "twn",
            "chr", "jai", "cut", "inn", "out", "tmp", "pub", "prv", "spc", "ext"
        };

        private static readonly string[] FileTypes = {
            "bg", "planevent", "planlive", "planmap", "planner", "sound", "vfx",
            "collision", "light", "timeline", "camera", "effect"
        };

        public Dictionary<LayerEntryType, int> EntryTypeStats { get; private set; } = new();
        public Dictionary<string, int> FileTypeStats { get; private set; } = new();
        public Dictionary<string, int> ZoneStats { get; private set; } = new();

        public GameLgbReader(string gameInstallPath)
        {
            var sqpackPath = Path.Combine(gameInstallPath, "game", "sqpack");
            if (!Directory.Exists(sqpackPath))
            {
                throw new DirectoryNotFoundException($"FFXIV sqpack directory not found at: {sqpackPath}");
            }

            var luminaOptions = new LuminaOptions
            {
                CacheFileResources = false,
                LoadMultithreaded = false,
                DefaultExcelLanguage = Language.English,
                PanicOnSheetChecksumMismatch = false,
                CurrentPlatform = PlatformId.Win32
            };

            _gameData = new GameData(sqpackPath, luminaOptions);
            Console.WriteLine($"🎯 GameLgbReader initialized - NO MEMORY LIMITS");
        }

        // ✅ SIMPLE: Cache clearing method
        public void ClearCaches()
        {
            try
            {
                _lgbFileCache?.Clear();
                _discoveredLgbPaths?.Clear();
                EntryTypeStats?.Clear();
                FileTypeStats?.Clear();
                ZoneStats?.Clear();

                Console.WriteLine("🧹 Caches cleared");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Cache clearing error: {ex.Message}");
            }
        }

        // ✅ STREAMING: Process files one at a time with immediate export
        public void ParseAllLgbFilesWithStreamingExport(
            CancellationToken cancellationToken,
            Action<string, LgbData> onFileParseCallback,
            Action<string, Exception> onFileErrorCallback)
        {
            var allFiles = GetAvailableLgbFiles();
            Console.WriteLine($"🚀 STREAMING: Processing {allFiles.Count} LGB files with immediate export...");
            Console.WriteLine($"🎯 Using UNRESTRICTED parsing for complete data");

            int processed = 0;
            int failed = 0;

            try
            {
                foreach (var path in allFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        // ✅ FIX: Use UNRESTRICTED parsing for complete data
                        var data = ParseLgbFileUnrestricted(path);
                        if (data != null)
                        {
                            // ✅ CALLBACK: Immediately export this file before parsing the next
                            onFileParseCallback(path, data);
                            processed++;

                            // ✅ CLEANUP: Immediately free this file's data
                            data = null;

                            // ✅ GC: Every 3 files, force cleanup to keep memory low
                            if (processed % 3 == 0)
                            {
                                GC.Collect();
                                GC.WaitForPendingFinalizers();
                                Console.WriteLine($"📝 Streamed {processed}/{allFiles.Count} files... (memory cleaned)");
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        onFileErrorCallback(path, ex);
                    }
                }
            }
            finally
            {
                // Final cleanup
                ClearCaches();
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            Console.WriteLine($"\n=== STREAMING EXPORT FINISHED ===");
            Console.WriteLine($"Successfully processed: {processed}");
            Console.WriteLine($"Failed: {failed}");
            Console.WriteLine($"Total files: {allFiles.Count}");
        }

        /// <summary>
        /// Unrestricted single file parsing - no memory limits - COMPLETE DATA
        /// </summary>
        private LgbData ParseLgbFileUnrestricted(string gamePath)
        {
            try
            {
                if (!_gameData.FileExists(gamePath))
                {
                    throw new FileNotFoundException($"LGB file not found: {gamePath}");
                }

                var lgbFile = SafeLoadLgbFile(gamePath);
                if (lgbFile == null)
                {
                    throw new InvalidDataException($"Lumina failed to load LGB file: {gamePath}");
                }

                var result = ParseLgbFromLuminaFileUnrestricted(lgbFile);
                return result;
            }
            catch (Exception ex)
            {
                throw new InvalidDataException($"Failed to parse LGB file '{gamePath}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// Safely load an LGB file from the game data
        /// </summary>
        private LgbFile SafeLoadLgbFile(string gamePath)
        {
            try
            {
                // Check cache first
                if (_lgbFileCache.ContainsKey(gamePath))
                {
                    return _lgbFileCache[gamePath];
                }

                // Load from game data
                var lgbFile = _gameData.GetFile<LgbFile>(gamePath);
                if (lgbFile != null)
                {
                    // Cache the file for potential reuse
                    _lgbFileCache[gamePath] = lgbFile;
                }

                return lgbFile;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️  Failed to load LGB file '{gamePath}': {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Unrestricted LGB parsing - processes ALL objects, no limits - COMPLETE DATA
        /// </summary>
        private LgbData ParseLgbFromLuminaFileUnrestricted(LgbFile lgbFile)
        {
            var lgbData = new LgbData
            {
                FilePath = lgbFile.FilePath?.Path ?? "Unknown",
                Layers = lgbFile.Layers,
                Metadata = new Dictionary<string, object>
                {
                    ["LayerCount"] = lgbFile.Layers.Length,
                    ["ParsedAt"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC"),
                    ["ProcessingMode"] = "Unrestricted Streaming Mode - Full Data"
                }
            };

            var processedObjects = 0;
            var enhancedObjectData = new Dictionary<uint, Dictionary<string, object>>();

            if (lgbFile.Layers != null)
            {
                foreach (var layer in lgbFile.Layers)
                {
                    if (layer.InstanceObjects != null)
                    {
                        foreach (var instanceObj in layer.InstanceObjects)
                        {
                            try
                            {
                                // ✅ UNRESTRICTED: Use comprehensive parsing for complete data
                                var enhancedData = ParseObjectDataFromLuminaComprehensive(instanceObj);
                                enhancedObjectData[instanceObj.InstanceId] = enhancedData;
                                processedObjects++;
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"    ⚠️  Error processing object {instanceObj.InstanceId}: {ex.Message}");
                            }
                        }
                    }
                }
            }

            if (enhancedObjectData.Count > 0)
            {
                lgbData.Metadata["EnhancedObjectData"] = enhancedObjectData;
            }

            lgbData.Metadata["TotalObjectsProcessed"] = processedObjects;
            return lgbData;
        }

        // ✅ LEGACY: Keep old method for compatibility (used by non-streaming)
        public Dictionary<string, LgbData> ParseAllLgbFilesWithCancellation(CancellationToken cancellationToken = default)
        {
            var results = new Dictionary<string, LgbData>();
            var allFiles = GetAvailableLgbFiles();

            Console.WriteLine($"🚀 LEGACY: Processing {allFiles.Count} LGB files (NOT RECOMMENDED - HIGH MEMORY)...");

            int processed = 0;
            int failed = 0;

            try
            {
                foreach (var path in allFiles)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        var data = ParseLgbFileUnrestricted(path);
                        if (data != null)
                        {
                            results[path] = data;
                            processed++;

                            if (processed % 10 == 0)
                            {
                                Console.WriteLine($"📝 Processed {processed}/{allFiles.Count} files...");
                                GC.Collect();
                                GC.WaitForPendingFinalizers();
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        Console.WriteLine($"✗ Failed to parse {path}: {ex.Message}");
                    }
                }
            }
            finally
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            Console.WriteLine($"\n=== LEGACY PARSING FINISHED ===");
            Console.WriteLine($"Successfully processed: {processed}");
            Console.WriteLine($"Failed: {failed}");
            Console.WriteLine($"Total files: {allFiles.Count}");

            DisplayFinalStatistics();
            return results;
        }

        /// <summary>
        /// Track entry type for statistics
        /// </summary>
        private void TrackEntryType(LayerEntryType entryType)
        {
            EntryTypeStats[entryType] = EntryTypeStats.GetValueOrDefault(entryType, 0) + 1;
        }

        /// <summary>
        /// ✅ COMPLETE: All LgbEntryTypes from SaintCoinach - comprehensive object parsing with all data types
        /// </summary>
        private Dictionary<string, object> ParseObjectDataFromLuminaComprehensive(LayerCommon.InstanceObject instanceObj)
        {
            var data = new Dictionary<string, object>();

            data["Type"] = instanceObj.AssetType.ToString();
            TrackEntryType(instanceObj.AssetType);

            switch (instanceObj.AssetType)
            {
                case LayerEntryType.AssetNone:
                    data["ObjectType"] = "AssetNone";
                    ExtractGenericObjectData(data, instanceObj.Object);
                    break;

                // ✅ BgParts/Model = 1
                case LayerEntryType.BG:
                    if (instanceObj.Object is LayerCommon.BGInstanceObject bg)
                    {
                        data["AssetPath"] = bg.AssetPath ?? "Unknown";
                        data["CollisionAssetPath"] = bg.CollisionAssetPath ?? "Unknown";
                        data["IsVisible"] = bg.IsVisible;
                        data["RenderShadowEnabled"] = bg.RenderShadowEnabled;
                        data["RenderLightShadowEnabled"] = bg.RenderLightShadowEnabled;
                        data["CollisionType"] = bg.CollisionType.ToString();
                        data["AttributeMask"] = bg.AttributeMask;
                        data["Attribute"] = bg.Attribute;
                        data["RenderModelClipRange"] = bg.RenderModelClipRange;
                    }
                    break;

                case LayerEntryType.Attribute:
                    ExtractGenericObjectData(data, instanceObj.Object);
                    break;

                // ✅ Light = 3
                case LayerEntryType.LayLight:
                    if (instanceObj.Object is LayerCommon.LightInstanceObject layLight)
                    {
                        data["LightType"] = layLight.LightType.ToString();
                        data["Attenuation"] = layLight.Attenuation;
                        data["RangeRate"] = layLight.RangeRate;
                        data["TexturePath"] = layLight.TexturePath ?? "Unknown";
                        data["SpecularEnabled"] = layLight.SpecularEnabled;
                        data["BGShadowEnabled"] = layLight.BGShadowEnabled;
                        data["CharacterShadowEnabled"] = layLight.CharacterShadowEnabled;
                        data["ShadowClipRange"] = layLight.ShadowClipRange;
                    }
                    break;

                // ✅ Vfx = 4
                case LayerEntryType.VFX:
                    if (instanceObj.Object is LayerCommon.VFXInstanceObject vfx)
                    {
                        data["AssetPath"] = vfx.AssetPath ?? "Unknown";
                        data["SoftParticleFadeRange"] = vfx.SoftParticleFadeRange;
                        data["IsAutoPlay"] = vfx.IsAutoPlay;
                        data["IsNoFarClip"] = vfx.IsNoFarClip;
                        data["FadeNearStart"] = vfx.FadeNearStart;
                        data["FadeNearEnd"] = vfx.FadeNearEnd;
                        data["FadeFarStart"] = vfx.FadeFarStart;
                        data["FadeFarEnd"] = vfx.FadeFarEnd;
                        data["ZCorrect"] = vfx.ZCorrect;
                    }
                    break;

                // ✅ PositionMarker = 5
                case LayerEntryType.PositionMarker:
                    if (instanceObj.Object is LayerCommon.PositionMarkerInstanceObject positionMarker)
                    {
                        data["PositionMarkerType"] = positionMarker.PositionMarkerType.ToString();
                        data["CommentJP"] = positionMarker.CommentJP;
                        data["CommentEN"] = positionMarker.CommentEN;
                    }
                    break;

                // ✅ Gimmick/SharedGroup6 = 6
                case LayerEntryType.SharedGroup:
                    if (instanceObj.Object is LayerCommon.SharedGroupInstanceObject sharedGroup)
                    {
                        data["AssetPath"] = sharedGroup.AssetPath ?? "Unknown";
                        data["InitialDoorState"] = sharedGroup.InitialDoorState.ToString();
                        data["InitialRotationState"] = sharedGroup.InitialRotationState.ToString();
                        data["RandomTimelineAutoPlay"] = sharedGroup.RandomTimelineAutoPlay;
                        data["RandomTimelineLoopPlayback"] = sharedGroup.RandomTimelineLoopPlayback;
                        data["BoundCLientPathInstanceId"] = sharedGroup.BoundCLientPathInstanceId;
                        data["InitialTransformState"] = sharedGroup.InitialTransformState.ToString();
                        data["InitialColorState"] = sharedGroup.InitialColorState.ToString();
                    }
                    break;

                // ✅ Sound = 7
                case LayerEntryType.Sound:
                    if (instanceObj.Object is LayerCommon.SoundInstanceObject sound)
                    {
                        data["AssetPath"] = sound.AssetPath ?? "Unknown";
                        data["SoundEffectParam"] = sound.SoundEffectParam;
                    }
                    break;

                // ✅ EventNpc = 8
                case LayerEntryType.EventNPC:
                    if (instanceObj.Object is LayerCommon.ENPCInstanceObject enpc)
                    {
                        data["BaseId"] = enpc.ParentData.ParentData.BaseId;
                        data["Behavior"] = enpc.Behavior;
                        data["PopWeather"] = enpc.ParentData.PopWeather;
                        data["PopTimeStart"] = enpc.ParentData.PopTimeStart;
                        data["PopTimeEnd"] = enpc.ParentData.PopTimeEnd;
                        data["MoveAi"] = enpc.ParentData.MoveAi;
                        data["WanderingRange"] = enpc.ParentData.WanderingRange;
                        data["Route"] = enpc.ParentData.Route;
                        data["EventGroup"] = enpc.ParentData.EventGroup;
                    }
                    break;

                // ✅ BattleNpc = 9 - Fixed the type name issue
                case LayerEntryType.BattleNPC:
                    // Note: BattleNpcInstanceObject may not exist in Lumina, use generic extraction
                    ExtractGenericObjectData(data, instanceObj.Object);
                    break;

                case LayerEntryType.RoutePath:
                    ExtractGenericObjectData(data, instanceObj.Object);
                    break;

                case LayerEntryType.Character:
                    ExtractGenericObjectData(data, instanceObj.Object);
                    break;

                // ✅ Aetheryte = 12
                case LayerEntryType.Aetheryte:
                    if (instanceObj.Object is LayerCommon.AetheryteInstanceObject aetheryte)
                    {
                        data["BaseId"] = aetheryte.ParentData.BaseId;
                        data["BoundInstanceID"] = aetheryte.BoundInstanceID;
                    }
                    break;

                // ✅ EnvSpace = 13 (mapped to EnvSet in Lumina)
                case LayerEntryType.EnvSet:
                    if (instanceObj.Object is LayerCommon.EnvSetInstanceObject envSet)
                    {
                        data["AssetPath"] = envSet.AssetPath ?? "Unknown";
                        data["SoundAssetPath"] = envSet.SoundAssetPath ?? "Unknown";
                        data["BoundInstanceId"] = envSet.BoundInstanceId;
                        data["Shape"] = envSet.Shape.ToString();
                        data["IsEnvMapShootingPoint"] = envSet.IsEnvMapShootingPoint;
                        data["Priority"] = envSet.Priority;
                        data["EffectiveRange"] = envSet.EffectiveRange;
                        data["InterpolationTime"] = envSet.InterpolationTime;
                        data["Reverb"] = envSet.Reverb;
                        data["Filter"] = envSet.Filter;
                    }
                    break;

                // ✅ Gathering = 14
                case LayerEntryType.Gathering:
                    if (instanceObj.Object is LayerCommon.GatheringInstanceObject gathering)
                    {
                        data["GatheringPointId"] = gathering.GatheringPointId;
                    }
                    break;

                // ✅ Treasure = 16
                case LayerEntryType.Treasure:
                    if (instanceObj.Object is LayerCommon.TreasureInstanceObject treasure)
                    {
                        data["NonpopInitZone"] = treasure.NonpopInitZone;
                    }
                    break;

                case LayerEntryType.HelperObject:
                    if (instanceObj.Object is LayerCommon.HelperObjInstanceObject helperObj)
                    {
                        data["ObjType"] = helperObj.ObjType.ToString();
                        data["TargetTypeBin"] = helperObj.TargetTypeBin.ToString();
                        data["CharacterSize"] = helperObj.CharacterSize.ToString();
                        data["UseDefaultMotion"] = helperObj.UseDefaultMotion;
                        data["PartyMemberIndex"] = helperObj.PartyMemberIndex;
                        data["TargetInstanceId"] = helperObj.TargetInstanceId;
                        data["DirectId"] = helperObj.DirectId;
                        data["UseDirectId"] = helperObj.UseDirectId;
                        data["KeepHighTexture"] = helperObj.KeepHighTexture;
                        data["AllianceMemberIndex"] = helperObj.AllianceMemberIndex;
                        data["SkyVisibility"] = helperObj.SkyVisibility;
                    }
                    break;

                // ✅ Weapon = 39
                case LayerEntryType.Weapon:
                    if (instanceObj.Object is LayerCommon.WeaponInstanceObject weapon)
                    {
                        data["IsVisible"] = weapon.IsVisible;
                        data["SkeletonId"] = weapon.Model.SkeletonId;
                        data["PatternId"] = weapon.Model.PatternId;
                        data["ImageChangeId"] = weapon.Model.ImageChangeId;
                        data["StainingId"] = weapon.Model.StainingId;
                    }
                    break;

                // ✅ PopRange = 40
                case LayerEntryType.PopRange:
                    if (instanceObj.Object is LayerCommon.PopRangeInstanceObject popRange)
                    {
                        data["PopType"] = popRange.PopType.ToString();
                        data["InnerRadiusRatio"] = popRange.InnerRadiusRatio;
                        data["Index"] = popRange.Index;
                        data["RelativePositionsCount"] = popRange._RelativePositions.PosCount;
                    }
                    break;

                // ✅ ExitRange = 41
                case LayerEntryType.ExitRange:
                    if (instanceObj.Object is LayerCommon.ExitRangeInstanceObject exitRange)
                    {
                        data["ExitType"] = exitRange.ExitType.ToString();
                        data["ZoneId"] = exitRange.ZoneId;
                        data["TerritoryType"] = exitRange.TerritoryType;
                        data["Index"] = exitRange.Index;
                        data["DestInstanceId"] = exitRange.DestInstanceId;
                        data["ReturnInstanceId"] = exitRange.ReturnInstanceId;
                        data["PlayerRunningDirection"] = exitRange.PlayerRunningDirection;
                        data["TriggerBoxShape"] = exitRange.ParentData.TriggerBoxShape.ToString();
                        data["Priority"] = exitRange.ParentData.Priority;
                        data["Enabled"] = exitRange.ParentData.Enabled;
                    }
                    break;

                case LayerEntryType.LVB:
                    ExtractGenericObjectData(data, instanceObj.Object);
                    break;

                // ✅ MapRange = 43
                case LayerEntryType.MapRange:
                    if (instanceObj.Object is LayerCommon.MapRangeInstanceObject mapRange)
                    {
                        data["Map"] = mapRange.Map;
                        data["PlaceNameBlock"] = mapRange.PlaceNameBlock;
                        data["PlaceNameSpot"] = mapRange.PlaceNameSpot;
                        data["Weather"] = mapRange.Weather;
                        data["BGM"] = mapRange.BGM;
                        data["HousingBlockId"] = mapRange.HousingBlockId;
                        data["RestBonusEffective"] = mapRange.RestBonusEffective;
                        data["DiscoveryId"] = mapRange.DiscoveryId;
                        data["MapEnabled"] = mapRange.MapEnabled;
                        data["PlaceNameEnabled"] = mapRange.PlaceNameEnabled;
                        data["DiscoveryEnabled"] = mapRange.DiscoveryEnabled;
                        data["BGMEnabled"] = mapRange.BGMEnabled;
                        data["WeatherEnabled"] = mapRange.WeatherEnabled;
                        data["RestBonusEnabled"] = mapRange.RestBonusEnabled;
                        data["TriggerBoxShape"] = mapRange.ParentData.TriggerBoxShape.ToString();
                        data["Priority"] = mapRange.ParentData.Priority;
                        data["Enabled"] = mapRange.ParentData.Enabled;
                    }
                    break;

                // ✅ NaviMeshRange = 44
                case LayerEntryType.NaviMeshRange:
                    ExtractGenericObjectData(data, instanceObj.Object);
                    break;

                // ✅ EventObject = 45
                case LayerEntryType.EventObject:
                    if (instanceObj.Object is LayerCommon.EventInstanceObject eventObj)
                    {
                        data["BaseId"] = eventObj.ParentData.BaseId;
                        data["BoundInstanceId"] = eventObj.BoundInstanceId;
                    }
                    break;

                case LayerEntryType.DemiHuman:
                    ExtractGenericObjectData(data, instanceObj.Object);
                    break;

                // ✅ EnvLocation = 47
                case LayerEntryType.EnvLocation:
                    if (instanceObj.Object is LayerCommon.EnvLocationInstanceObject envLocation)
                    {
                        data["SHAmbientLightAssetPath"] = envLocation.SHAmbientLightAssetPath ?? "Unknown";
                        data["EnvMapAssetPath"] = envLocation.EnvMapAssetPath ?? "Unknown";
                    }
                    break;

                case LayerEntryType.ControlPoint:
                    ExtractGenericObjectData(data, instanceObj.Object);
                    break;

                // ✅ EventRange = 49
                case LayerEntryType.EventRange:
                    if (instanceObj.Object is LayerCommon.EventRangeInstanceObject eventRange)
                    {
                        data["TriggerBoxShape"] = eventRange.ParentData.TriggerBoxShape.ToString();
                        data["Priority"] = eventRange.ParentData.Priority;
                        data["Enabled"] = eventRange.ParentData.Enabled;
                    }
                    break;

                case LayerEntryType.RestBonusRange:
                    ExtractGenericObjectData(data, instanceObj.Object);
                    break;

                // ✅ QuestMarker = 51
                case LayerEntryType.QuestMarker:
                    if (instanceObj.Object is LayerCommon.QuestMarkerInstanceObject questMarker)
                    {
                        data["RangeType"] = questMarker.RangeType.ToString();
                    }
                    break;

                case LayerEntryType.Timeline:
                    ExtractGenericObjectData(data, instanceObj.Object);
                    break;

                case LayerEntryType.ObjectBehaviorSet:
                    ExtractGenericObjectData(data, instanceObj.Object);
                    break;

                case LayerEntryType.Movie:
                    ExtractGenericObjectData(data, instanceObj.Object);
                    break;

                case LayerEntryType.ScenarioExd:
                    ExtractGenericObjectData(data, instanceObj.Object);
                    break;

                case LayerEntryType.ScenarioText:
                    ExtractGenericObjectData(data, instanceObj.Object);
                    break;

                // ✅ CollisionBox = 57
                case LayerEntryType.CollisionBox:
                    if (instanceObj.Object is LayerCommon.CollisionBoxInstanceObject collisionBox)
                    {
                        data["AttributeMask"] = collisionBox.AttributeMask;
                        data["Attribute"] = collisionBox.Attribute;
                        data["PushPlayerOut"] = collisionBox.PushPlayerOut;
                        data["CollisionAssetPath"] = collisionBox.CollisionAssetPath ?? "Unknown";
                        data["TriggerBoxShape"] = collisionBox.ParentData.TriggerBoxShape.ToString();
                        data["Priority"] = collisionBox.ParentData.Priority;
                        data["Enabled"] = collisionBox.ParentData.Enabled;
                    }
                    break;

                // ✅ DoorRange = 58
                case LayerEntryType.DoorRange:
                    ExtractGenericObjectData(data, instanceObj.Object);
                    break;

                // ✅ LineVfx = 59
                case LayerEntryType.LineVFX:
                    if (instanceObj.Object is LayerCommon.LineVFXInstanceObject lineVFX)
                    {
                        data["LineStyle"] = lineVFX.LineStyle.ToString();
                    }
                    break;

                case LayerEntryType.SoundEnvSet:
                    ExtractGenericObjectData(data, instanceObj.Object);
                    break;

                case LayerEntryType.CutActionTimeline:
                    ExtractGenericObjectData(data, instanceObj.Object);
                    break;

                case LayerEntryType.CharaScene:
                    ExtractGenericObjectData(data, instanceObj.Object);
                    break;

                case LayerEntryType.CutAction:
                    ExtractGenericObjectData(data, instanceObj.Object);
                    break;

                case LayerEntryType.EquipPreset:
                    ExtractGenericObjectData(data, instanceObj.Object);
                    break;

                // ✅ ClientPath = 65
                case LayerEntryType.ClientPath:
                    if (instanceObj.Object is LayerCommon.ClientPathInstanceObject clientPath)
                    {
                        data["Ring"] = clientPath.Ring;
                        data["ControlPointCount"] = clientPath.ParentData.ControlPointCount;
                    }
                    break;

                // ✅ ServerPath = 66
                case LayerEntryType.ServerPath:
                    if (instanceObj.Object is LayerCommon.ServerPathInstanceObject serverPath)
                    {
                        data["ControlPointCount"] = serverPath.ParentData.ControlPointCount;
                    }
                    break;

                // ✅ GimmickRange = 67
                case LayerEntryType.GimmickRange:
                    if (instanceObj.Object is LayerCommon.GimmickRangeInstanceObject gimmickRange)
                    {
                        data["GimmickType"] = gimmickRange.GimmickType.ToString();
                        data["GimmickKey"] = gimmickRange.GimmickKey;
                        data["RoomUseAttribute"] = gimmickRange.RoomUseAttribute;
                        data["GroupId"] = gimmickRange.GroupId;
                        data["EnabledInDead"] = gimmickRange.EnabledInDead;
                        data["TriggerBoxShape"] = gimmickRange.ParentData.TriggerBoxShape.ToString();
                        data["Priority"] = gimmickRange.ParentData.Priority;
                        data["Enabled"] = gimmickRange.ParentData.Enabled;
                    }
                    break;

                // ✅ TargetMarker = 68
                case LayerEntryType.TargetMarker:
                    if (instanceObj.Object is LayerCommon.TargetMarkerInstanceObject targetMarker)
                    {
                        data["NamePlateOffsetY"] = targetMarker.NamePlateOffsetY;
                        data["TargetMakerType"] = targetMarker.TargetMakerType.ToString();
                    }
                    break;

                // ✅ ChairMarker = 69
                case LayerEntryType.ChairMarker:
                    if (instanceObj.Object is LayerCommon.ChairMarkerInstanceObject chairMarker)
                    {
                        data["LeftEnable"] = chairMarker.LeftEnable;
                        data["RightEnable"] = chairMarker.RightEnable;
                        data["BackEnable"] = chairMarker.BackEnable;
                        data["ObjectType"] = chairMarker.ObjectType.ToString();
                    }
                    break;

                // ✅ ClickableRange = 70
                case LayerEntryType.ClickableRange:
                    if (instanceObj.Object is LayerCommon.ClickableRangeInstanceObject clickableRange)
                    {
                        ExtractGenericObjectData(data, clickableRange);
                    }
                    break;

                // ✅ PrefetchRange = 71
                case LayerEntryType.PrefetchRange:
                    if (instanceObj.Object is LayerCommon.PrefetchRangeInstanceObject prefetchRange)
                    {
                        data["BoundInstanceId"] = prefetchRange.BoundInstanceId;
                        data["TriggerBoxShape"] = prefetchRange.ParentData.TriggerBoxShape.ToString();
                        data["Priority"] = prefetchRange.ParentData.Priority;
                        data["Enabled"] = prefetchRange.ParentData.Enabled;
                    }
                    break;

                // ✅ FateRange = 72
                case LayerEntryType.FateRange:
                    if (instanceObj.Object is LayerCommon.FateRangeInstanceObject fateRange)
                    {
                        data["FateLayoutLabelId"] = fateRange.FateLayoutLabelId;
                        data["TriggerBoxShape"] = fateRange.ParentData.TriggerBoxShape.ToString();
                        data["Priority"] = fateRange.ParentData.Priority;
                        data["Enabled"] = fateRange.ParentData.Enabled;
                        Console.WriteLine($"    ★ FOUND FATERANGE! FateLayoutLabelId: {fateRange.FateLayoutLabelId}");
                    }
                    break;

                // ✅ SphereCastRange = 75
                case LayerEntryType.SphereCastRange:
                    ExtractGenericObjectData(data, instanceObj.Object);
                    break;

                // ✅ Clip and related entries (reserved/unknown types)
                case LayerEntryType.Clip:
                case LayerEntryType.ClipCtrlPoint:
                case LayerEntryType.ClipCamera:
                case LayerEntryType.ClipLight:
                case LayerEntryType.ClipReserve00:
                case LayerEntryType.ClipReserve01:
                case LayerEntryType.ClipReserve02:
                case LayerEntryType.ClipReserve03:
                case LayerEntryType.ClipReserve04:
                case LayerEntryType.ClipReserve05:
                case LayerEntryType.ClipReserve06:
                case LayerEntryType.ClipReserve07:
                case LayerEntryType.ClipReserve08:
                case LayerEntryType.ClipReserve09:
                case LayerEntryType.ClipReserve10:
                case LayerEntryType.ClipReserve11:
                case LayerEntryType.ClipReserve12:
                case LayerEntryType.ClipReserve13:
                case LayerEntryType.ClipReserve14:
                case LayerEntryType.CutAssetOnlySelectable:
                case LayerEntryType.Player:
                case LayerEntryType.Monster:
                case LayerEntryType.PartyMember:
                case LayerEntryType.KeepRange:
                case LayerEntryType.IndoorObject:
                case LayerEntryType.OutdoorObject:
                case LayerEntryType.EditGroup:
                case LayerEntryType.StableChocobo:
                    data["ReserveType"] = instanceObj.AssetType.ToString();
                    ExtractGenericObjectData(data, instanceObj.Object);
                    break;

                case LayerEntryType.MaxAssetType:
                    data["MaxAssetType"] = "MaxAssetType";
                    break;

                default:
                    data["ObjectType"] = instanceObj.Object?.GetType().Name ?? "null";
                    data["UnknownType"] = instanceObj.AssetType.ToString();
                    data["EntryTypeId"] = ((int)instanceObj.AssetType).ToString();
                    ExtractGenericObjectData(data, instanceObj.Object);
                    Console.WriteLine($"    ⚠️  UNKNOWN ENTRY TYPE: {instanceObj.AssetType} (ID: {(int)instanceObj.AssetType})");
                    break;
            }

            return data;
        }

        private void ExtractGenericObjectData(Dictionary<string, object> data, object obj)
        {
            if (obj == null)
            {
                data["ObjectData"] = "null";
                return;
            }

            try
            {
                var objType = obj.GetType();
                data["ObjectTypeName"] = objType.Name;

                var properties = objType.GetProperties();
                foreach (var prop in properties)
                {
                    try
                    {
                        if (prop.CanRead && !prop.Name.Equals("GetType"))
                        {
                            var value = prop.GetValue(obj);
                            if (value != null)
                            {
                                data[prop.Name] = value;
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch (Exception ex)
            {
                data["ExtractionError"] = ex.Message;
            }
        }

        // ✅ KEEP: All existing zone/type/discovery methods
        public Dictionary<string, LgbData> ParseLgbFilesByZoneWithCancellation(string zoneName, CancellationToken cancellationToken = default)
        {
            var results = new Dictionary<string, LgbData>();
            var files = GetLgbFilesByZone(zoneName);

            Console.WriteLine($"🚀 ZONE PARSING: {files.Count} files from zone '{zoneName}'...");

            int processed = 0;
            int failed = 0;

            foreach (var path in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var data = ParseLgbFileUnrestricted(path);
                    if (data != null)
                    {
                        results[path] = data;
                        processed++;
                        Console.WriteLine($"✓ [{processed}/{files.Count}] {path}");
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failed++;
                    Console.WriteLine($"✗ Failed to parse {path}: {ex.Message}");
                }
            }

            Console.WriteLine($"Completed zone '{zoneName}': {processed} successful, {failed} failed");
            return results;
        }

        public Dictionary<string, LgbData> ParseLgbFilesByTypeWithCancellation(string fileType, CancellationToken cancellationToken = default)
        {
            var results = new Dictionary<string, LgbData>();
            var files = GetLgbFilesByType(fileType);

            Console.WriteLine($"🚀 TYPE PARSING: {files.Count} {fileType} files...");

            int processed = 0;
            int failed = 0;

            foreach (var path in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var data = ParseLgbFileUnrestricted(path);
                    if (data != null)
                    {
                        results[path] = data;
                        processed++;
                        Console.WriteLine($"✓ [{processed}/{files.Count}] {path}");
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    failed++;
                    Console.WriteLine($"✗ Failed to parse {path}: {ex.Message}");
                }
            }

            Console.WriteLine($"Completed {fileType} files: {processed} successful, {failed} failed");
            return results;
        }

        public LgbData ParseLgbFile(string gamePath)
        {
            return ParseLgbFileUnrestricted(gamePath);
        }

        // ✅ KEEP: All discovery methods unchanged
        public List<string> DiscoverAllLgbFiles()
        {
            if (_discoveredLgbPaths != null)
            {
                Console.WriteLine($"Using cached LGB file list ({_discoveredLgbPaths.Count} files)");
                return _discoveredLgbPaths;
            }

            Console.WriteLine("🚀 Starting comprehensive LGB file discovery...");
            var lgbFiles = new HashSet<string>();

            try
            {
                Console.WriteLine("Phase 1: Known patterns discovery...");
                lgbFiles.UnionWith(DiscoverByKnownPatterns());

                Console.WriteLine("Phase 2: Systematic discovery...");
                lgbFiles.UnionWith(DiscoverBySystematicTestingLimited());

                _discoveredLgbPaths = lgbFiles.ToList();
                Console.WriteLine($"Total discovered: {_discoveredLgbPaths.Count} files");

                GenerateDiscoveryStatistics();
                return _discoveredLgbPaths;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during discovery: {ex.Message}");
                _discoveredLgbPaths = new List<string>();
                return _discoveredLgbPaths;
            }
        }

        /// <summary>
        /// Get all available LGB files - wrapper around DiscoverAllLgbFiles
        /// </summary>
        public List<string> GetAvailableLgbFiles()
        {
            return DiscoverAllLgbFiles();
        }

        public List<string> GetLgbFilesByZone(string zonePattern)
        {
            var allFiles = GetAvailableLgbFiles();
            return allFiles.Where(path => path.Contains(zonePattern, StringComparison.OrdinalIgnoreCase))
                  .ToList();
        }

        public List<string> GetLgbFilesByType(string fileType)
        {
            var allFiles = GetAvailableLgbFiles();
            return allFiles.Where(path => path.EndsWith($"/{fileType}.lgb", StringComparison.OrdinalIgnoreCase))
                  .ToList();
        }

        private List<string> DiscoverByKnownPatterns()
        {
            var discoveredFiles = new List<string>();
            Console.WriteLine("Strategy 1: Comprehensive known pattern discovery...");

            var knownZones = new[] { "air_a1", "fst_f1", "lak_l1", "ocn_o1", "roc_r1", "sea_s1", "wil_w1", "zon_z1" };

            foreach (var zone in knownZones)
            {
                foreach (var areaType in AreaTypes)
                {
                    var areaIds = GenerateAreaIdsForZone(zone, areaType);
                    foreach (var areaId in areaIds)
                    {
                        foreach (var fileType in FileTypes)
                        {
                            var testPath = $"bg/ffxiv/{zone}/{areaType}/{areaId}/level/{fileType}.lgb";
                            if (TestAndAddFile(testPath, discoveredFiles))
                            {
                                Console.WriteLine($"    ✓ Found: {testPath}");
                            }
                        }
                    }
                }
            }

            Console.WriteLine($"Strategy 1 found: {discoveredFiles.Count} files");
            return discoveredFiles;
        }

        private List<string> DiscoverBySystematicTestingLimited()
        {
            var discoveredFiles = new List<string>();
            Console.WriteLine("Comprehensive systematic testing...");

            int testCount = 0;
            const int MAX_TESTS = 10000;

            foreach (var prefix in BaseZonePrefixes)
            {
                foreach (var suffix in BaseZoneSuffixes)
                {
                    var zone = $"{prefix}_{suffix}";

                    foreach (var areaType in AreaTypes.Take(15))
                    {
                        var areaIds = GenerateSystematicAreaIds(zone, areaType).Take(15);

                        foreach (var areaId in areaIds)
                        {
                            foreach (var fileType in FileTypes)
                            {
                                var testPath = $"bg/ffxiv/{zone}/{areaType}/{areaId}/level/{fileType}.lgb";
                                testCount++;

                                if (TestAndAddFile(testPath, discoveredFiles))
                                {
                                    Console.WriteLine($"    ✓ Found: {testPath}");
                                }

                                if (testCount >= MAX_TESTS)
                                {
                                    Console.WriteLine($"    Reached test limit ({MAX_TESTS})");
                                    goto EndSystematicSearch;
                                }
                            }
                        }
                    }
                }
            }

        EndSystematicSearch:
            Console.WriteLine($"Comprehensive search found: {discoveredFiles.Count} files ({testCount} tests)");
            return discoveredFiles;
        }

        private List<string> GenerateSystematicAreaIds(string zone, string areaType)
        {
            var areaIds = new List<string>();
            var parts = zone.Split('_');
            if (parts.Length < 2) return areaIds;

            var zoneLetter = parts[1].Substring(0, 1);
            var zoneNumber = parts[1].Substring(1);
            var areaLetter = areaType.Substring(0, 1);

            for (int i = 1; i <= 10; i++)
            {
                areaIds.Add($"{zoneLetter}{zoneNumber}{areaLetter}{i}");
            }

            for (char c = 'a'; c <= 'j'; c++)
            {
                areaIds.Add($"{zoneLetter}{zoneNumber}{areaLetter}{c}");
            }

            return areaIds;
        }

        private bool TestAndAddFile(string testPath, List<string> fileList)
        {
            try
            {
                if (_gameData.FileExists(testPath))
                {
                    fileList.Add(testPath);
                    TrackFileDiscovery(testPath);
                    return true;
                }
            }
            catch
            {
            }
            return false;
        }

        private void TrackFileDiscovery(string filePath)
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);
            FileTypeStats[fileName] = FileTypeStats.GetValueOrDefault(fileName, 0) + 1;

            var pathParts = filePath.Split('/');
            if (pathParts.Length > 2)
            {
                var zone = pathParts[2];
                ZoneStats[zone] = ZoneStats.GetValueOrDefault(zone, 0) + 1;
            }
        }

        private void GenerateDiscoveryStatistics()
        {
            Console.WriteLine("\n=== Discovery Statistics ===");

            Console.WriteLine($"\nFile Types Found:");
            foreach (var kvp in FileTypeStats.OrderByDescending(x => x.Value))
            {
                Console.WriteLine($"  {kvp.Key}: {kvp.Value} files");
            }

            Console.WriteLine($"\nZones Found:");
            foreach (var kvp in ZoneStats.OrderByDescending(x => x.Value))
            {
                Console.WriteLine($"  {kvp.Key}: {kvp.Value} files");
            }
        }

        /// <summary>
        /// Display final statistics
        /// </summary>
        private void DisplayFinalStatistics()
        {
            Console.WriteLine("\n=== FINAL PARSING STATISTICS ===");

            if (EntryTypeStats.Count > 0)
            {
                Console.WriteLine("\nEntry Types Processed:");
                foreach (var kvp in EntryTypeStats.OrderByDescending(x => x.Value))
                {
                    Console.WriteLine($"  {kvp.Key}: {kvp.Value} objects");
                }
            }

            if (FileTypeStats.Count > 0)
            {
                Console.WriteLine("\nFile Types Discovered:");
                foreach (var kvp in FileTypeStats.OrderByDescending(x => x.Value))
                {
                    Console.WriteLine($"  {kvp.Key}: {kvp.Value} files");
                }
            }

            if (ZoneStats.Count > 0)
            {
                Console.WriteLine("\nZones Discovered:");
                foreach (var kvp in ZoneStats.OrderByDescending(x => x.Value))
                {
                    Console.WriteLine($"  {kvp.Key}: {kvp.Value} files");
                }
            }
        }

        private List<string> GenerateAreaIdsForZone(string zone, string areaType)
        {
            var areaIds = new List<string>();
            var parts = zone.Split('_');
            if (parts.Length < 2) return areaIds;

            switch (zone)
            {
                case "air_a1":
                    if (areaType == "evt") areaIds.AddRange(new[] { "a1e2" });
                    break;

                case "fst_f1":
                    switch (areaType)
                    {
                        case "bah": areaIds.AddRange(new[] { "f1b1", "f1b2", "f1b3", "f1b4", "f1b5" }); break;
                        case "cnt": areaIds.AddRange(new[] { "f1c1", "f1c2", "f1c3" }); break;
                        case "dun": areaIds.AddRange(new[] { "f1d1", "f1d2", "f1d3", "f1d4", "f1d5" }); break;
                        case "evt": areaIds.AddRange(new[] { "f1e4", "f1e5", "f1e1", "f1e2", "f1e3" }); break;
                        case "fld": areaIds.AddRange(new[] { "f1f1", "f1f2", "f1f3", "f1f4" }); break;
                        case "hou": areaIds.AddRange(new[] { "f1h1", "f1h2" }); break;
                        case "ind": areaIds.AddRange(new[] { "f1i1", "f1i2", "f1i3" }); break;
                        case "rad": areaIds.AddRange(new[] { "f1r1", "f1r2" }); break;
                        case "twn": areaIds.AddRange(new[] { "f1t1", "f1t2", "f1t3" }); break;
                    }
                    break;

                case "sea_s1":
                    switch (areaType)
                    {
                        case "bah": areaIds.AddRange(new[] { "s1b1", "s1b2", "s1b3", "s1b4" }); break;
                        case "dun": areaIds.AddRange(new[] { "s1d1", "s1d2", "s1d3", "s1d4", "s1d5" }); break;
                        case "fld": areaIds.AddRange(new[] { "s1f1", "s1f2", "s1f3", "s1f4" }); break;
                        case "twn": areaIds.AddRange(new[] { "s1t1", "s1t2", "s1t3" }); break;
                    }
                    break;

                default:
                    areaIds.AddRange(GenerateSystematicAreaIds(zone, areaType));
                    break;
            }

            return areaIds;
        }

        /// <summary>
        /// Implement IDisposable pattern
        /// </summary>
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
                    // Dispose managed resources
                    try
                    {
                        _lgbFileCache?.Clear();
                        _discoveredLgbPaths?.Clear();
                        EntryTypeStats?.Clear();
                        FileTypeStats?.Clear();
                        ZoneStats?.Clear();
                        _gameData?.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠️  Error during GameLgbReader disposal: {ex.Message}");
                    }
                }

                _disposed = true;
            }
        }

        /// <summary>
        /// Finalizer
        /// </summary>
        ~GameLgbReader()
        {
            Dispose(false);
        }
    }
}