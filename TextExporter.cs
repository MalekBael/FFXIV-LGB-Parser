using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Lumina.Data.Parsing.Layer;

namespace LgbParser
{
    public interface IExporter
    {
        void Export(LgbData data, string outputPath);
    }

    public class LuminaTextExporter : IExporter
    {
        public void Export(LgbData data, string outputPath)
        {
            var sb = new StringBuilder();

            sb.AppendLine("=== Enhanced LGB File Analysis ===");
            sb.AppendLine($"File Path: {data.FilePath}");

            if (data.Metadata.ContainsKey("ParsedAt"))
            {
                sb.AppendLine($"Parsed At: {data.Metadata["ParsedAt"]}");
            }

            if (data.Metadata.ContainsKey("LayerCount"))
            {
                sb.AppendLine($"Layer Count: {data.Metadata["LayerCount"]}");
            }

            sb.AppendLine();

            foreach (var layer in data.Layers)
            {
                sb.AppendLine($"Layer [{layer.LayerId}]: {layer.Name}");
                sb.AppendLine($"  Instance Objects: {layer.InstanceObjects.Length}");
                sb.AppendLine($"  Tool Mode Visible: {layer.ToolModeVisible != 0}");
                sb.AppendLine($"  Tool Mode Read Only: {layer.ToolModeReadOnly != 0}");
                sb.AppendLine($"  Is Bush Layer: {layer.IsBushLayer != 0}");
                sb.AppendLine($"  PS3 Visible: {layer.PS3Visible != 0}");
                sb.AppendLine($"  Is Temporary: {layer.IsTemporary != 0}");
                sb.AppendLine($"  Is Housing: {layer.IsHousing != 0}");
                sb.AppendLine($"  Festival ID: {layer.FestivalID}");
                sb.AppendLine($"  Festival Phase ID: {layer.FestivalPhaseID}");
                sb.AppendLine($"  Version Mask: {layer.VersionMask}");
                sb.AppendLine();

                if (layer.InstanceObjects != null && layer.InstanceObjects.Length > 0)
                {
                    foreach (var obj in layer.InstanceObjects)
                    {
                        sb.AppendLine($"  Object [{obj.InstanceId}]: {obj.Name}");
                        sb.AppendLine($"    Type: {obj.AssetType}");
                        sb.AppendLine($"    Position: ({obj.Transform.Translation.X:F3}, {obj.Transform.Translation.Y:F3}, {obj.Transform.Translation.Z:F3})");
                        sb.AppendLine($"    Rotation: ({obj.Transform.Rotation.X:F3}, {obj.Transform.Rotation.Y:F3}, {obj.Transform.Rotation.Z:F3})");
                        sb.AppendLine($"    Scale: ({obj.Transform.Scale.X:F3}, {obj.Transform.Scale.Y:F3}, {obj.Transform.Scale.Z:F3})");

                        if (data.Metadata.ContainsKey("EnhancedObjectData"))
                        {
                            var enhancedData = (Dictionary<uint, Dictionary<string, object>>)data.Metadata["EnhancedObjectData"];
                            if (enhancedData.ContainsKey(obj.InstanceId))
                            {
                                foreach (var kvp in enhancedData[obj.InstanceId])
                                {
                                    sb.AppendLine($"      {kvp.Key}: {kvp.Value}");
                                }
                            }
                        }
                        sb.AppendLine();
                    }
                }

                if (layer.LayerSetReferences != null && layer.LayerSetReferences.Length > 0)
                {
                    sb.AppendLine($"  Layer Set References:");
                    foreach (var lsr in layer.LayerSetReferences)
                    {
                        sb.AppendLine($"    Layer Set ID: {lsr.LayerSetId}");
                    }
                    sb.AppendLine();
                }
            }

            if (data.Metadata.Count > 0)
            {
                sb.AppendLine("=== Metadata ===");
                foreach (var kvp in data.Metadata)
                {
                    if (kvp.Key != "EnhancedObjectData")     
                    {
                        sb.AppendLine($"{kvp.Key}: {kvp.Value}");
                    }
                    else
                    {
                        var enhancedData = (Dictionary<uint, Dictionary<string, object>>)kvp.Value;
                        sb.AppendLine($"{kvp.Key}: {enhancedData.Count} entries");
                    }
                }
            }

            File.WriteAllText(outputPath, sb.ToString());
        }
    }

    public class TextExporter : IExporter
    {
        public void Export(LgbData data, string outputPath)
        {
            var enhancedExporter = new LuminaTextExporter();
            enhancedExporter.Export(data, outputPath);
        }
    }
}