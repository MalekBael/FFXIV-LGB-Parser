using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Linq;
using System;
using System.Text;      

namespace LgbParser
{
    public class LuminaJsonExporter : IExporter
    {
        public void Export(LgbData data, string outputPath)
        {
            using var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write);
            using var writer = new Utf8JsonWriter(fileStream, new JsonWriterOptions 
            { 
                Indented = true 
            });

            WriteJsonToStream(writer, data);
        }

        private void WriteJsonToStream(Utf8JsonWriter writer, LgbData data)
        {
            writer.WriteStartObject();

            writer.WriteString("FilePath", data.FilePath);

            writer.WritePropertyName("Metadata");
            WriteMetadata(writer, data);

            writer.WritePropertyName("Layers");
            WriteLayers(writer, data);

            writer.WritePropertyName("EnhancedObjectData");
            WriteEnhancedObjectData(writer, data);

            writer.WriteEndObject();
        }

        private void WriteMetadata(Utf8JsonWriter writer, LgbData data)
        {
            writer.WriteStartObject();

            foreach (var kvp in data.Metadata)
            {
                if (kvp.Key != "EnhancedObjectData")
                {
                    writer.WritePropertyName(kvp.Key);
                    WriteJsonValue(writer, kvp.Value);
                }
            }

            if (data.Metadata.ContainsKey("EnhancedObjectData"))
            {
                var enhancedData = (Dictionary<uint, Dictionary<string, object>>)data.Metadata["EnhancedObjectData"];
                writer.WriteNumber("EnhancedObjectDataCount", enhancedData.Count);
            }

            writer.WriteEndObject();
        }

        private void WriteLayers(Utf8JsonWriter writer, LgbData data)
        {
            writer.WriteStartArray();

            foreach (var layer in data.Layers)
            {
                writer.WriteStartObject();

                writer.WriteNumber("LayerId", layer.LayerId);
                writer.WriteString("Name", layer.Name ?? "");

                writer.WritePropertyName("Properties");
                WriteLayerProperties(writer, layer);

                writer.WritePropertyName("InstanceObjects");
                WriteInstanceObjects(writer, layer, data);

                writer.WritePropertyName("LayerSetReferences");
                WriteLayerSetReferences(writer, layer);

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        private void WriteLayerProperties(Utf8JsonWriter writer, Lumina.Data.Parsing.Layer.LayerCommon.Layer layer)
        {
            writer.WriteStartObject();

            writer.WriteNumber("InstanceObjectCount", layer.InstanceObjects.Length);
            writer.WriteBoolean("ToolModeVisible", layer.ToolModeVisible != 0);
            writer.WriteBoolean("ToolModeReadOnly", layer.ToolModeReadOnly != 0);
            writer.WriteBoolean("IsBushLayer", layer.IsBushLayer != 0);
            writer.WriteBoolean("PS3Visible", layer.PS3Visible != 0);
            writer.WriteBoolean("IsTemporary", layer.IsTemporary != 0);
            writer.WriteBoolean("IsHousing", layer.IsHousing != 0);
            writer.WriteNumber("FestivalID", layer.FestivalID);
            writer.WriteNumber("FestivalPhaseID", layer.FestivalPhaseID);
            writer.WriteNumber("VersionMask", layer.VersionMask);

            writer.WriteEndObject();
        }

        private void WriteInstanceObjects(Utf8JsonWriter writer, Lumina.Data.Parsing.Layer.LayerCommon.Layer layer, LgbData data)
        {
            writer.WriteStartArray();

            if (layer.InstanceObjects != null)
            {
                foreach (var obj in layer.InstanceObjects)
                {
                    writer.WriteStartObject();

                    writer.WriteNumber("InstanceId", obj.InstanceId);
                    writer.WriteString("Name", obj.Name ?? "");
                    writer.WriteString("Type", obj.AssetType.ToString());

                    writer.WritePropertyName("Transform");
                    WriteTransform(writer, obj);

                    writer.WritePropertyName("EnhancedData");
                    WriteEnhancedDataForObject(writer, data, obj.InstanceId);

                    writer.WriteEndObject();
                }
            }

            writer.WriteEndArray();
        }

        private void WriteTransform(Utf8JsonWriter writer, Lumina.Data.Parsing.Layer.LayerCommon.InstanceObject obj)
        {
            writer.WriteStartObject();

            writer.WritePropertyName("Position");
            writer.WriteStartObject();
            writer.WriteNumber("X", Math.Round(obj.Transform.Translation.X, 3));
            writer.WriteNumber("Y", Math.Round(obj.Transform.Translation.Y, 3));
            writer.WriteNumber("Z", Math.Round(obj.Transform.Translation.Z, 3));
            writer.WriteEndObject();

            writer.WritePropertyName("Rotation");
            writer.WriteStartObject();
            writer.WriteNumber("X", Math.Round(obj.Transform.Rotation.X, 3));
            writer.WriteNumber("Y", Math.Round(obj.Transform.Rotation.Y, 3));
            writer.WriteNumber("Z", Math.Round(obj.Transform.Rotation.Z, 3));
            writer.WriteEndObject();

            writer.WritePropertyName("Scale");
            writer.WriteStartObject();
            writer.WriteNumber("X", Math.Round(obj.Transform.Scale.X, 3));
            writer.WriteNumber("Y", Math.Round(obj.Transform.Scale.Y, 3));
            writer.WriteNumber("Z", Math.Round(obj.Transform.Scale.Z, 3));
            writer.WriteEndObject();

            writer.WriteEndObject();
        }

        private void WriteLayerSetReferences(Utf8JsonWriter writer, Lumina.Data.Parsing.Layer.LayerCommon.Layer layer)
        {
            writer.WriteStartArray();

            if (layer.LayerSetReferences != null)
            {
                foreach (var lsr in layer.LayerSetReferences)
                {
                    writer.WriteStartObject();
                    writer.WriteNumber("LayerSetId", lsr.LayerSetId);
                    writer.WriteEndObject();
                }
            }

            writer.WriteEndArray();
        }

        private void WriteEnhancedDataForObject(Utf8JsonWriter writer, LgbData data, uint instanceId)
        {
            writer.WriteStartObject();

            if (data.Metadata.ContainsKey("EnhancedObjectData"))
            {
                var enhancedData = (Dictionary<uint, Dictionary<string, object>>)data.Metadata["EnhancedObjectData"];
                if (enhancedData.ContainsKey(instanceId))
                {
                    foreach (var kvp in enhancedData[instanceId])
                    {
                        writer.WritePropertyName(kvp.Key);
                        WriteJsonValue(writer, kvp.Value);
                    }
                }
            }

            writer.WriteEndObject();
        }

        private void WriteEnhancedObjectData(Utf8JsonWriter writer, LgbData data)
        {
            writer.WriteStartObject();

            if (data.Metadata.ContainsKey("EnhancedObjectData"))
            {
                var enhancedData = (Dictionary<uint, Dictionary<string, object>>)data.Metadata["EnhancedObjectData"];
                foreach (var kvp in enhancedData)
                {
                    writer.WritePropertyName(kvp.Key.ToString());
                    writer.WriteStartObject();
                    
                    foreach (var innerKvp in kvp.Value)
                    {
                        writer.WritePropertyName(innerKvp.Key);
                        WriteJsonValue(writer, innerKvp.Value);
                    }
                    
                    writer.WriteEndObject();
                }
            }

            writer.WriteEndObject();
        }

        private void WriteJsonValue(Utf8JsonWriter writer, object value)
        {
            switch (value)
            {
                case null:
                    writer.WriteNullValue();
                    break;
                case string s:
                    writer.WriteStringValue(s);
                    break;
                case int i:
                    writer.WriteNumberValue(i);
                    break;
                case uint ui:
                    writer.WriteNumberValue(ui);
                    break;
                case long l:
                    writer.WriteNumberValue(l);
                    break;
                case ulong ul:
                    writer.WriteNumberValue(ul);
                    break;
                case float f:
                    writer.WriteNumberValue(f);
                    break;
                case double d:
                    writer.WriteNumberValue(d);
                    break;
                case bool b:
                    writer.WriteBooleanValue(b);
                    break;
                case byte by:
                    writer.WriteNumberValue(by);
                    break;
                case sbyte sb:
                    writer.WriteNumberValue(sb);
                    break;
                case short sh:
                    writer.WriteNumberValue(sh);
                    break;
                case ushort us:
                    writer.WriteNumberValue(us);
                    break;
                default:
                    writer.WriteStringValue(value.ToString());
                    break;
            }
        }

    }

    public class LayerData
    {
        public uint LayerId { get; set; }
        public string Name { get; set; } = "";
        public LayerPropertiesData Properties { get; set; } = new();
        public ObjectData[] InstanceObjects { get; set; } = Array.Empty<ObjectData>();
        public LayerSetRefData[] LayerSetReferences { get; set; } = Array.Empty<LayerSetRefData>();
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
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public TransformData Transform { get; set; } = new();
        public Dictionary<string, object> EnhancedData { get; set; } = new();
    }

    public class TransformData
    {
        public PositionData Position { get; set; } = new();
        public RotationData Rotation { get; set; } = new();
        public ScaleData Scale { get; set; } = new();
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

    public class JsonExporter : IExporter
    {
        public void Export(LgbData data, string outputPath)
        {
            var enhancedExporter = new LuminaJsonExporter();
            enhancedExporter.Export(data, outputPath);
        }
    }
}