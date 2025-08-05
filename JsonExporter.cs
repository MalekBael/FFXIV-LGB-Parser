using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Linq;
using System;

namespace LgbParser
{
    // ✅ FIXED: Use EXACTLY the same approach as the working text exporter
    public class LuminaJsonExporter : IExporter
    {
        public void Export(LgbData data, string outputPath)
        {
            var jsonOutput = new
            {
                FilePath = data.FilePath,
                Metadata = BuildMetadata(data),
                Layers = BuildLayersFromLuminaData(data),
                EnhancedObjectData = GetEnhancedObjectData(data)
            };

            // ✅ CRITICAL FIX: Add IncludeFields = true for struct serialization
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                IncludeFields = true,
                PropertyNamingPolicy = null // Keep original property names
            };

            var json = JsonSerializer.Serialize(jsonOutput, options);
            File.WriteAllText(outputPath, json);
        }

        private object BuildMetadata(LgbData data)
        {
            var metadata = new Dictionary<string, object>();

            foreach (var kvp in data.Metadata)
            {
                if (kvp.Key != "EnhancedObjectData")
                {
                    metadata[kvp.Key] = kvp.Value;
                }
            }

            if (data.Metadata.ContainsKey("EnhancedObjectData"))
            {
                var enhancedData = (Dictionary<uint, Dictionary<string, object>>)data.Metadata["EnhancedObjectData"];
                metadata["EnhancedObjectDataCount"] = enhancedData.Count;
            }

            return metadata;
        }

        // ✅ FIXED: Use direct Layer serialization instead of anonymous objects
        private LayerData[] BuildLayersFromLuminaData(LgbData data)
        {
            var layers = new List<LayerData>();

            // ✅ Use EXACTLY the same iteration as text exporter
            foreach (var layer in data.Layers)
            {
                var layerObjects = new List<ObjectData>();

                // ✅ Use EXACTLY the same condition as text exporter
                if (layer.InstanceObjects != null && layer.InstanceObjects.Length > 0)
                {
                    foreach (var obj in layer.InstanceObjects)
                    {
                        var enhancedData = GetEnhancedDataForObject(data, obj.InstanceId);

                        layerObjects.Add(new ObjectData
                        {
                            InstanceId = obj.InstanceId,
                            Name = obj.Name ?? "",
                            Type = obj.AssetType.ToString(),
                            Transform = new TransformData
                            {
                                Position = new PositionData
                                {
                                    X = Math.Round(obj.Transform.Translation.X, 3),
                                    Y = Math.Round(obj.Transform.Translation.Y, 3),
                                    Z = Math.Round(obj.Transform.Translation.Z, 3)
                                },
                                Rotation = new RotationData
                                {
                                    X = Math.Round(obj.Transform.Rotation.X, 3),
                                    Y = Math.Round(obj.Transform.Rotation.Y, 3),
                                    Z = Math.Round(obj.Transform.Rotation.Z, 3)
                                },
                                Scale = new ScaleData
                                {
                                    X = Math.Round(obj.Transform.Scale.X, 3),
                                    Y = Math.Round(obj.Transform.Scale.Y, 3),
                                    Z = Math.Round(obj.Transform.Scale.Z, 3)
                                }
                            },
                            EnhancedData = enhancedData
                        });
                    }
                }

                // ✅ Build layer set references
                var layerSetReferences = new List<LayerSetRefData>();
                if (layer.LayerSetReferences != null && layer.LayerSetReferences.Length > 0)
                {
                    foreach (var lsr in layer.LayerSetReferences)
                    {
                        layerSetReferences.Add(new LayerSetRefData
                        {
                            LayerSetId = lsr.LayerSetId
                        });
                    }
                }

                // ✅ Use EXACTLY the same field access as text exporter
                layers.Add(new LayerData
                {
                    LayerId = layer.LayerId,
                    Name = layer.Name ?? "",
                    Properties = new LayerPropertiesData
                    {
                        InstanceObjectCount = layer.InstanceObjects.Length, // Direct access like text exporter
                        ToolModeVisible = layer.ToolModeVisible != 0,
                        ToolModeReadOnly = layer.ToolModeReadOnly != 0,
                        IsBushLayer = layer.IsBushLayer != 0,
                        PS3Visible = layer.PS3Visible != 0,
                        IsTemporary = layer.IsTemporary != 0,
                        IsHousing = layer.IsHousing != 0,
                        FestivalID = layer.FestivalID,
                        FestivalPhaseID = layer.FestivalPhaseID,
                        VersionMask = layer.VersionMask
                    },
                    InstanceObjects = layerObjects.ToArray(),
                    LayerSetReferences = layerSetReferences.ToArray()
                });
            }

            return layers.ToArray();
        }

        private Dictionary<string, object> GetEnhancedDataForObject(LgbData data, uint instanceId)
        {
            if (data.Metadata.ContainsKey("EnhancedObjectData"))
            {
                var enhancedData = (Dictionary<uint, Dictionary<string, object>>)data.Metadata["EnhancedObjectData"];
                if (enhancedData.ContainsKey(instanceId))
                {
                    return enhancedData[instanceId];
                }
            }
            return new Dictionary<string, object>();
        }

        private Dictionary<string, Dictionary<string, object>> GetEnhancedObjectData(LgbData data)
        {
            if (data.Metadata.ContainsKey("EnhancedObjectData"))
            {
                var enhancedData = (Dictionary<uint, Dictionary<string, object>>)data.Metadata["EnhancedObjectData"];
                return enhancedData.ToDictionary(kvp => kvp.Key.ToString(), kvp => kvp.Value);
            }
            return new Dictionary<string, Dictionary<string, object>>();
        }
    }

    // ✅ Data classes for proper JSON serialization
    public class LayerData
    {
        public uint LayerId { get; set; }
        public string Name { get; set; }
        public LayerPropertiesData Properties { get; set; }
        public ObjectData[] InstanceObjects { get; set; }
        public LayerSetRefData[] LayerSetReferences { get; set; }
    }

    public class LayerPropertiesData
    {
        public int InstanceObjectCount { get; set; }
        public bool ToolModeVisible { get; set; }
        public bool ToolModeReadOnly { get; set; }
        public bool IsBushLayer { get; set; }
        public bool PS3Visible { get; set; }
        public bool IsTemporary { get; set; }
        public bool IsHousing { get; set; }
        public ushort FestivalID { get; set; }
        public ushort FestivalPhaseID { get; set; }
        public ushort VersionMask { get; set; }
    }

    public class ObjectData
    {
        public uint InstanceId { get; set; }
        public string Name { get; set; }
        public string Type { get; set; }
        public TransformData Transform { get; set; }
        public Dictionary<string, object> EnhancedData { get; set; }
    }

    public class TransformData
    {
        public PositionData Position { get; set; }
        public RotationData Rotation { get; set; }
        public ScaleData Scale { get; set; }
    }

    public class PositionData
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }

    public class RotationData
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }

    public class ScaleData
    {
        public double X { get; set; }
        public double Y { get; set; }
        public double Z { get; set; }
    }

    public class LayerSetRefData
    {
        public uint LayerSetId { get; set; }
    }

    // ✅ LEGACY: Keep original exporter for compatibility
    public class JsonExporter : IExporter
    {
        public void Export(LgbData data, string outputPath)
        {
            var enhancedExporter = new LuminaJsonExporter();
            enhancedExporter.Export(data, outputPath);
        }
    }
}