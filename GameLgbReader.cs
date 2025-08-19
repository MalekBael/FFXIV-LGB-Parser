using Lumina;
using Lumina.Data;
using Lumina.Data.Files;
using Lumina.Data.Parsing.Layer;
using System;
using Lumina.Data.Structs;
using System.Collections.Generic;
using System.Collections;
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
        private int _maxCacheSize = 300;        
        private DateTime _lastCacheCleanup = DateTime.Now;

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
            Console.WriteLine($"GameLgbReader initialized");
        }

        public void ClearCaches()
        {
            try
            {
                _lgbFileCache?.Clear();
                
                EntryTypeStats?.Clear();
                FileTypeStats?.Clear();
                ZoneStats?.Clear();

                try
                {
                    var gameDataType = _gameData.GetType();

                    var fileCacheField = gameDataType.GetField("_fileCache",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    if (fileCacheField?.GetValue(_gameData) is IDictionary fileCache)
                    {
                        fileCache.Clear();
                        Console.WriteLine("Cleared Lumina internal file cache");
                    }

                    var repositoryField = gameDataType.GetField("_repositories",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    if (repositoryField?.GetValue(_gameData) is IDictionary repositories)
                    {
                        repositories.Clear();
                        Console.WriteLine("Cleared Lumina repositories");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Lumina internal cleanup failed: {ex.Message}");
                }

                _lastCacheCleanup = DateTime.Now;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lumina cache clearing error: {ex.Message}");
            }
        }

        public void ClearDiscoveredPaths()
        {
            try
            {
                _discoveredLgbPaths?.Clear();
                Console.WriteLine("Cleared discovered LGB file paths");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error clearing discovered paths: {ex.Message}");
            }
        }

        public void ParseAllLgbFilesWithStreamingExport(
            CancellationToken cancellationToken,
            Action<string, LgbData> onFileParseCallback,
            Action<string, Exception> onFileErrorCallback)
        {
            var allFiles = GetAvailableLgbFiles();
            Console.WriteLine($"LUMINA STREAMING: Processing {allFiles.Count} LGB files");

            var fileSnapshot = allFiles.ToArray();       

            int processed = 0;
            int failed = 0;

            try
            {
                foreach (var path in fileSnapshot)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    try
                    {
                        if (_lgbFileCache.Count >= _maxCacheSize)
                        {
                            Console.WriteLine($"LUMINA cache limit reached ({_maxCacheSize}), clearing safely...");
                            ClearLuminaCachesOnly();         
                        }

                        var data = ParseLgbFileWithLuminaOptimization(path);
                        if (data != null)
                        {
                            onFileParseCallback(path, data);
                            processed++;

                            data = null;

                            if (processed % 150 == 0)
                            {
                                ClearLuminaCachesOnly();         
                                GC.Collect(0, GCCollectionMode.Optimized);    

                                Console.WriteLine($"LUMINA Streamed {processed}/{allFiles.Count} files..");
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

                        ClearLuminaCachesOnly();
                    }
                }
            }
            finally
            {
                ClearCaches();           
                for (int i = 0; i < 3; i++)
                {
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
                    GC.WaitForPendingFinalizers();
                }
            }

            Console.WriteLine($"\n=== LUMINA STREAMING EXPORT FINISHED ===");
            Console.WriteLine($"Successfully processed: {processed}");
            Console.WriteLine($"Failed: {failed}");
            Console.WriteLine($"Total files: {allFiles.Count}");
        }

        private LgbData ParseLgbFileWithLuminaOptimization(string gamePath)
        {
            try
            {
                if (!_gameData.FileExists(gamePath))
                {
                    throw new FileNotFoundException($"LGB file not found: {gamePath}");
                }

                var lgbFile = SafeLoadLgbFileWithMemoryOptimization(gamePath);
                if (lgbFile == null)
                {
                    throw new InvalidDataException($"Lumina failed to load LGB file: {gamePath}");
                }

                var result = ParseLgbFromLuminaFileWithMemoryOptimization(lgbFile);

                if (_lgbFileCache.ContainsKey(gamePath))
                {
                    _lgbFileCache.Remove(gamePath);
                }

                return result;
            }
            catch (Exception ex)
            {
                throw new InvalidDataException($"Failed to parse LGB file '{gamePath}': {ex.Message}", ex);
            }
        }

        private LgbFile SafeLoadLgbFileWithMemoryOptimization(string gamePath)
        {
            try
            {
                var lgbFile = _gameData.GetFile<LgbFile>(gamePath);

                if (lgbFile != null)
                {
                    if (_lgbFileCache.Count < _maxCacheSize)
                    {
                        _lgbFileCache[gamePath] = lgbFile;
                    }
                }

                return lgbFile;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load LGB file '{gamePath}': {ex.Message}");
                return null;
            }
        }

        private LgbData ParseLgbFromLuminaFileWithMemoryOptimization(LgbFile lgbFile)
        {
            var lgbData = new LgbData
            {
                FilePath = lgbFile.FilePath?.Path ?? "Unknown",
                Layers = lgbFile.Layers,
                Metadata = new Dictionary<string, object>
                {
                    ["LayerCount"] = lgbFile.Layers.Length,
                    ["ParsedAt"] = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss UTC")
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
                                var enhancedData = ParseObjectDataWithLuminaOptimization(instanceObj);
                                enhancedObjectData[instanceObj.InstanceId] = enhancedData;
                                processedObjects++;

                                if (processedObjects % 1000 == 0)
                                {
                                    GC.Collect(0, GCCollectionMode.Optimized);
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Error processing object {instanceObj.InstanceId}: {ex.Message}");
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

        private Dictionary<string, object> ParseObjectDataWithLuminaOptimization(LayerCommon.InstanceObject instanceObj)
        {
            var data = new Dictionary<string, object>(32);      

            data["Type"] = instanceObj.AssetType.ToString();
            TrackEntryType(instanceObj.AssetType);

            switch (instanceObj.AssetType)
            {
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

                        ExtractAllObjectData(data, bg, "BG");
                    }
                    break;

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

                        ExtractAllObjectData(data, layLight, "LayLight");
                    }
                    break;

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

                        ExtractAllObjectData(data, vfx, "VFX");
                    }
                    break;

                case LayerEntryType.PositionMarker:
                    if (instanceObj.Object is LayerCommon.PositionMarkerInstanceObject positionMarker)
                    {
                        data["PositionMarkerType"] = positionMarker.PositionMarkerType.ToString();
                        data["CommentJP"] = positionMarker.CommentJP.ToString();            
                        data["CommentEN"] = positionMarker.CommentEN.ToString();            

                        ExtractAllObjectData(data, positionMarker, "PositionMarker");
                    }
                    break;

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

                        ExtractAllObjectData(data, sharedGroup, "SharedGroup");
                    }
                    break;

                case LayerEntryType.Sound:
                    if (instanceObj.Object is LayerCommon.SoundInstanceObject sound)
                    {
                        data["AssetPath"] = sound.AssetPath ?? "Unknown";
                        data["SoundEffectParam"] = sound.SoundEffectParam;

                        ExtractAllObjectData(data, sound, "Sound");
                    }
                    break;

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

                        ExtractAllObjectData(data, enpc, "EventNPC");
                    }
                    break;

                case LayerEntryType.BattleNPC:
                    ExtractAllObjectData(data, instanceObj.Object, "BattleNPC");
                    break;

                case LayerEntryType.Aetheryte:
                    if (instanceObj.Object is LayerCommon.AetheryteInstanceObject aetheryte)
                    {
                        data["BaseId"] = aetheryte.ParentData.BaseId;
                        data["BoundInstanceID"] = aetheryte.BoundInstanceID;

                        ExtractAllObjectData(data, aetheryte, "Aetheryte");
                    }
                    break;

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

                        ExtractAllObjectData(data, envSet, "EnvSet");
                    }
                    break;

                case LayerEntryType.Gathering:
                    if (instanceObj.Object is LayerCommon.GatheringInstanceObject gathering)
                    {
                        data["GatheringPointId"] = gathering.GatheringPointId;

                        ExtractAllObjectData(data, gathering, "Gathering");
                    }
                    break;

                case LayerEntryType.Treasure:
                    if (instanceObj.Object is LayerCommon.TreasureInstanceObject treasure)
                    {
                        data["NonpopInitZone"] = treasure.NonpopInitZone;

                        ExtractAllObjectData(data, treasure, "Treasure");
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

                        ExtractAllObjectData(data, helperObj, "HelperObject");
                    }
                    break;

                case LayerEntryType.Weapon:
                    if (instanceObj.Object is LayerCommon.WeaponInstanceObject weapon)
                    {
                        data["IsVisible"] = weapon.IsVisible;
                        data["SkeletonId"] = weapon.Model.SkeletonId;
                        data["PatternId"] = weapon.Model.PatternId;
                        data["ImageChangeId"] = weapon.Model.ImageChangeId;
                        data["StainingId"] = weapon.Model.StainingId;

                        ExtractAllObjectData(data, weapon, "Weapon");
                    }
                    break;

                case LayerEntryType.PopRange:
                    if (instanceObj.Object is LayerCommon.PopRangeInstanceObject popRange)
                    {
                        data["PopType"] = popRange.PopType.ToString();
                        data["InnerRadiusRatio"] = popRange.InnerRadiusRatio;
                        data["Index"] = popRange.Index;
                        data["RelativePositionsCount"] = popRange._RelativePositions.PosCount;

                        ExtractAllObjectData(data, popRange, "PopRange");
                    }
                    break;

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

                        ExtractAllObjectData(data, exitRange, "ExitRange");
                    }
                    break;

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

                        ExtractAllObjectData(data, mapRange, "MapRange");
                    }
                    break;

                case LayerEntryType.EventObject:
                    if (instanceObj.Object is LayerCommon.EventInstanceObject eventObj)
                    {
                        data["BaseId"] = eventObj.ParentData.BaseId;
                        data["BoundInstanceId"] = eventObj.BoundInstanceId;

                        ExtractAllObjectData(data, eventObj, "EventObject");
                    }
                    break;

                case LayerEntryType.EnvLocation:
                    if (instanceObj.Object is LayerCommon.EnvLocationInstanceObject envLocation)
                    {
                        data["SHAmbientLightAssetPath"] = envLocation.SHAmbientLightAssetPath ?? "Unknown";
                        data["EnvMapAssetPath"] = envLocation.EnvMapAssetPath ?? "Unknown";

                        ExtractAllObjectData(data, envLocation, "EnvLocation");
                    }
                    break;

                case LayerEntryType.EventRange:
                    if (instanceObj.Object is LayerCommon.EventRangeInstanceObject eventRange)
                    {
                        data["TriggerBoxShape"] = eventRange.ParentData.TriggerBoxShape.ToString();
                        data["Priority"] = eventRange.ParentData.Priority;
                        data["Enabled"] = eventRange.ParentData.Enabled;

                        ExtractAllObjectData(data, eventRange, "EventRange");
                    }
                    break;

                case LayerEntryType.QuestMarker:
                    if (instanceObj.Object is LayerCommon.QuestMarkerInstanceObject questMarker)
                    {
                        data["RangeType"] = questMarker.RangeType.ToString();

                        ExtractAllObjectData(data, questMarker, "QuestMarker");
                    }
                    break;

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

                        ExtractAllObjectData(data, collisionBox, "CollisionBox");
                    }
                    break;

                case LayerEntryType.LineVFX:
                    if (instanceObj.Object is LayerCommon.LineVFXInstanceObject lineVFX)
                    {
                        data["LineStyle"] = lineVFX.LineStyle.ToString();

                        ExtractAllObjectData(data, lineVFX, "LineVFX");
                    }
                    break;

                case LayerEntryType.ClientPath:
                    if (instanceObj.Object is LayerCommon.ClientPathInstanceObject clientPath)
                    {
                        data["Ring"] = clientPath.Ring;
                        data["ControlPointCount"] = clientPath.ParentData.ControlPointCount;

                        ExtractAllObjectData(data, clientPath, "ClientPath");
                    }
                    break;

                case LayerEntryType.ServerPath:
                    if (instanceObj.Object is LayerCommon.ServerPathInstanceObject serverPath)
                    {
                        data["ControlPointCount"] = serverPath.ParentData.ControlPointCount;

                        ExtractAllObjectData(data, serverPath, "ServerPath");
                    }
                    break;

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

                        ExtractAllObjectData(data, gimmickRange, "GimmickRange");
                    }
                    break;

                case LayerEntryType.TargetMarker:
                    if (instanceObj.Object is LayerCommon.TargetMarkerInstanceObject targetMarker)
                    {
                        data["NamePlateOffsetY"] = targetMarker.NamePlateOffsetY;
                        data["TargetMakerType"] = targetMarker.TargetMakerType.ToString();

                        ExtractAllObjectData(data, targetMarker, "TargetMarker");
                    }
                    break;

                case LayerEntryType.ChairMarker:
                    if (instanceObj.Object is LayerCommon.ChairMarkerInstanceObject chairMarker)
                    {
                        data["LeftEnable"] = chairMarker.LeftEnable;
                        data["RightEnable"] = chairMarker.RightEnable;
                        data["BackEnable"] = chairMarker.BackEnable;
                        data["ObjectType"] = chairMarker.ObjectType.ToString();

                        ExtractAllObjectData(data, chairMarker, "ChairMarker");
                    }
                    break;

                case LayerEntryType.ClickableRange:
                    if (instanceObj.Object is LayerCommon.ClickableRangeInstanceObject clickableRange)
                    {
                        ExtractAllObjectData(data, clickableRange, "ClickableRange");
                    }
                    break;

                case LayerEntryType.PrefetchRange:
                    if (instanceObj.Object is LayerCommon.PrefetchRangeInstanceObject prefetchRange)
                    {
                        data["BoundInstanceId"] = prefetchRange.BoundInstanceId;
                        data["TriggerBoxShape"] = prefetchRange.ParentData.TriggerBoxShape.ToString();
                        data["Priority"] = prefetchRange.ParentData.Priority;
                        data["Enabled"] = prefetchRange.ParentData.Enabled;

                        ExtractAllObjectData(data, prefetchRange, "PrefetchRange");
                    }
                    break;

                case LayerEntryType.FateRange:
                    if (instanceObj.Object is LayerCommon.FateRangeInstanceObject fateRange)
                    {
                        data["FateLayoutLabelId"] = fateRange.FateLayoutLabelId;
                        data["TriggerBoxShape"] = fateRange.ParentData.TriggerBoxShape.ToString();
                        data["Priority"] = fateRange.ParentData.Priority;
                        data["Enabled"] = fateRange.ParentData.Enabled;

                        ExtractAllObjectData(data, fateRange, "FateRange");
                        Console.WriteLine($"FOUND FATERANGE! FateLayoutLabelId: {fateRange.FateLayoutLabelId}");
                    }
                    break;

                default:
                    data["ObjectType"] = instanceObj.Object?.GetType().Name ?? "null";
                    data["UnknownType"] = instanceObj.AssetType.ToString();
                    data["EntryTypeId"] = ((int)instanceObj.AssetType).ToString();
                    ExtractAllObjectData(data, instanceObj.Object, "Unknown");
                    Console.WriteLine($"UNKNOWN ENTRY TYPE: {instanceObj.AssetType} (ID: {(int)instanceObj.AssetType})");
                    break;
            }

            return data;
        }

        private void ExtractAllObjectData(Dictionary<string, object> data, object obj, string typePrefix = "")
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

                if (!string.IsNullOrEmpty(typePrefix))
                {
                    data["TypeCategory"] = typePrefix;
                }

                var properties = objType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

                foreach (var prop in properties)
                {
                    try
                    {
                        if (prop.CanRead && !prop.Name.Equals("GetType"))
                        {
                            var value = prop.GetValue(obj);
                            if (value != null)
                            {
                                var propName = $"{typePrefix}_{prop.Name}";

                                if (IsSimpleType(value.GetType()))
                                {
                                    data[propName] = value;
                                }
                                else if (value is Array array)
                                {
                                    data[propName] = $"Array[{array.Length}]";
                                    data[$"{propName}_Length"] = array.Length;
                                }
                                else if (value.GetType().IsEnum)
                                {
                                    data[propName] = value.ToString();
                                    data[$"{propName}_Value"] = Convert.ToInt32(value);
                                }
                                else
                                {
                                    data[propName] = value.ToString();

                                    ExtractNestedObjectData(data, value, $"{propName}_");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        data[$"{typePrefix}_PropertyError_{prop.Name}"] = ex.Message;
                    }
                }

                var fields = objType.GetFields(BindingFlags.Public | BindingFlags.Instance);

                foreach (var field in fields)
                {
                    try
                    {
                        var value = field.GetValue(obj);
                        if (value != null)
                        {
                            var fieldName = $"{typePrefix}_Field_{field.Name}";

                            if (IsSimpleType(value.GetType()))
                            {
                                data[fieldName] = value;
                            }
                            else
                            {
                                data[fieldName] = value.ToString();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        data[$"{typePrefix}_FieldError_{field.Name}"] = ex.Message;
                    }
                }
            }
            catch (Exception ex)
            {
                data["ExtractionError"] = ex.Message;
            }
        }

        private void ExtractNestedObjectData(Dictionary<string, object> data, object obj, string prefix, int depth = 0)
        {
            if (obj == null || depth > 2)        
                return;

            try
            {
                var objType = obj.GetType();

                if (!objType.Namespace?.Contains("Lumina") == true &&
                    !objType.Namespace?.Contains("FFXIV") == true)
                    return;

                var properties = objType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
                int extractedCount = 0;
                const int MAX_NESTED_PROPERTIES = 10;    

                foreach (var prop in properties.Take(MAX_NESTED_PROPERTIES))
                {
                    try
                    {
                        if (prop.CanRead && !prop.Name.Equals("GetType"))
                        {
                            var value = prop.GetValue(obj);
                            if (value != null && IsSimpleType(value.GetType()))
                            {
                                data[$"{prefix}{prop.Name}"] = value;
                                extractedCount++;
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        private bool IsSimpleType(Type type)
        {
            return type.IsPrimitive ||
           type.IsEnum ||
           type == typeof(string) ||
           type == typeof(decimal) ||
           type == typeof(DateTime) ||
           type == typeof(TimeSpan) ||
           type == typeof(Guid);
        }

        private LgbData ParseLgbFileUnrestricted(string gamePath)
        {
            return ParseLgbFileWithLuminaOptimization(gamePath);
        }

        private LgbFile SafeLoadLgbFile(string gamePath)
        {
            return SafeLoadLgbFileWithMemoryOptimization(gamePath);
        }

        private void TrackEntryType(LayerEntryType entryType)
        {
            EntryTypeStats[entryType] = EntryTypeStats.GetValueOrDefault(entryType, 0) + 1;
        }

        public Dictionary<string, LgbData> ParseLgbFilesByZoneWithCancellation(string zoneName, CancellationToken cancellationToken = default)
        {
            var results = new Dictionary<string, LgbData>();
            var files = GetLgbFilesByZone(zoneName);

            Console.WriteLine($"LUMINA ZONE PARSING: {files.Count} files from zone '{zoneName}'...");

            int processed = 0;
            int failed = 0;

            foreach (var path in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var data = ParseLgbFileWithLuminaOptimization(path);
                    if (data != null)
                    {
                        results[path] = data;
                        processed++;
                        Console.WriteLine($"✓ [{processed}/{files.Count}] {path}");

                        if (processed % 5 == 0)
                        {
                            ClearCaches();
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
                    Console.WriteLine($"Failed to parse {path}: {ex.Message}");
                }
            }

            Console.WriteLine($"Completed zone '{zoneName}': {processed} successful, {failed} failed");

            ClearCaches();
            return results;
        }

        public Dictionary<string, LgbData> ParseLgbFilesByTypeWithCancellation(string fileType, CancellationToken cancellationToken = default)
        {
            var results = new Dictionary<string, LgbData>();
            var files = GetLgbFilesByType(fileType);

            Console.WriteLine($"LUMINA TYPE PARSING: {files.Count} {fileType} files...");

            int processed = 0;
            int failed = 0;

            foreach (var path in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var data = ParseLgbFileWithLuminaOptimization(path);
                    if (data != null)
                    {
                        results[path] = data;
                        processed++;
                        Console.WriteLine($"[{processed}/{files.Count}] {path}");

                        if (processed % 3 == 0)
                        {
                            ClearCaches();
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
                    Console.WriteLine($"Failed to parse {path}: {ex.Message}");
                }
            }

            Console.WriteLine($"Completed {fileType} files: {processed} successful, {failed} failed");

            ClearCaches();
            return results;
        }

        public LgbData ParseLgbFile(string gamePath)
        {
            return ParseLgbFileWithLuminaOptimization(gamePath);
        }

        public List<string> DiscoverAllLgbFiles()
        {
            if (_discoveredLgbPaths != null)
            {
                Console.WriteLine($"Using cached LGB file list ({_discoveredLgbPaths.Count} files)");
                return _discoveredLgbPaths;
            }

            Console.WriteLine("Starting LGB file discovery...");
            var lgbFiles = new HashSet<string>();

            try
            {
                Console.WriteLine("Phase 1: Known patterns discovery...");
                var phase1Files = DiscoverByKnownPatterns();
                lgbFiles.UnionWith(phase1Files);
                Console.WriteLine($"Phase 1 completed: {phase1Files.Count} files found");

                Console.WriteLine("Phase 2: Systematic discovery...");
                var phase2Files = DiscoverBySystematicTestingLimited();
                lgbFiles.UnionWith(phase2Files);
                Console.WriteLine($"Phase 2 completed: {phase2Files.Count} additional files found");

                _discoveredLgbPaths = lgbFiles.ToList();
                Console.WriteLine($"Total discovered: {_discoveredLgbPaths.Count} files");

                GenerateDiscoveryStatistics();

                ClearLuminaCachesOnly();

                return _discoveredLgbPaths;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during discovery: {ex.Message}");
                _discoveredLgbPaths = new List<string>();
                return _discoveredLgbPaths;
            }
        }

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
            Console.WriteLine("Strategy 1: known pattern discovery...");

            var knownZones = new[] { "air_a1", "fst_f1", "lak_l1", "ocn_o1", "roc_r1", "sea_s1", "wil_w1", "zon_z1" };

            foreach (var zone in knownZones)
            {
                Console.WriteLine($"Searching zone: {zone}");
                int zoneFileCount = 0;

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
                                zoneFileCount++;
                                if (zoneFileCount <= 3)
                                {
                                    Console.WriteLine($"    ✓ Found: {testPath}");
                                }
                            }
                        }
                    }
                }

                Console.WriteLine($"Zone {zone}: {zoneFileCount} files found");

                if (discoveredFiles.Count % 100 == 0)
                {
                    GC.Collect(0, GCCollectionMode.Optimized);
                }
            }

            Console.WriteLine($"Strategy 1 found: {discoveredFiles.Count} files total");
            return discoveredFiles;
        }

        private List<string> DiscoverBySystematicTestingLimited()
        {
            var discoveredFiles = new List<string>();
            Console.WriteLine("Comprehensive systematic testing...");

            int testCount = 0;
            const int MAX_TESTS = 20000;        

            foreach (var prefix in BaseZonePrefixes)
            {
                foreach (var suffix in BaseZoneSuffixes)
                {
                    var zone = $"{prefix}_{suffix}";

                    foreach (var areaType in AreaTypes)          
                    {
                        var areaIds = GenerateSystematicAreaIds(zone, areaType);          

                        foreach (var areaId in areaIds)
                        {
                            foreach (var fileType in FileTypes)
                            {
                                var testPath = $"bg/ffxiv/{zone}/{areaType}/{areaId}/level/{fileType}.lgb";
                                testCount++;

                                if (TestAndAddFile(testPath, discoveredFiles))
                                {
                                    Console.WriteLine($"Found: {testPath}");
                                }

                                if (testCount >= MAX_TESTS)
                                {
                                    Console.WriteLine($"Reached test limit ({MAX_TESTS})");
                                    goto EndSystematicSearch;
                                }

                                if (testCount % 500 == 0)
                                {
                                    GC.Collect(0, GCCollectionMode.Optimized);
                                }
                            }
                        }
                    }
                }
            }

        EndSystematicSearch:
            Console.WriteLine($"search found: {discoveredFiles.Count} files ({testCount} tests)");
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

            for (int i = 1; i <= 20; i++)        
            {
                areaIds.Add($"{zoneLetter}{zoneNumber}{areaLetter}{i}");
            }

            for (char c = 'a'; c <= 'z'; c++)         
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

        private void ClearLuminaCachesOnly()
        {
            try
            {
                _lgbFileCache?.Clear();

                try
                {
                    var gameDataType = _gameData.GetType();

                    var fileCacheField = gameDataType.GetField("_fileCache",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    if (fileCacheField?.GetValue(_gameData) is IDictionary fileCache)
                    {
                        fileCache.Clear();
                    }

                    var repositoryField = gameDataType.GetField("_repositories",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    if (repositoryField?.GetValue(_gameData) is IDictionary repositories)
                    {
                        repositories.Clear();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Lumina internal cleanup failed: {ex.Message}");
                }

                _lastCacheCleanup = DateTime.Now;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Lumina cache clearing error: {ex.Message}");
            }
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
                    try
                    {
                        Console.WriteLine("Disposing GameLgbReader with Lumina cleanup...");

                        _lgbFileCache?.Clear();
                        _discoveredLgbPaths?.Clear();        
                        EntryTypeStats?.Clear();
                        FileTypeStats?.Clear();
                        ZoneStats?.Clear();

                        if (_gameData != null)
                        {
                            try
                            {
                                var gameDataType = _gameData.GetType();
                                var disposeMethod = gameDataType.GetMethod("Dispose");
                                disposeMethod?.Invoke(_gameData, null);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Lumina GameData disposal error: {ex.Message}");
                            }
                        }

                        for (int i = 0; i < 3; i++)
                        {
                            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
                            GC.WaitForPendingFinalizers();
                        }

                        Console.WriteLine("GameLgbReader disposed with Lumina cleanup complete");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error during GameLgbReader disposal: {ex.Message}");
                    }
                }

                _disposed = true;
            }
        }

        ~GameLgbReader()
        {
            Dispose(false);
        }
    }
}