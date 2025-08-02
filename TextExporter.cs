using System;
using System.IO;
using System.Text;

namespace LgbParser
{
    public class TextExporter : IExporter
    {
        public void Export(LgbData data, string outputPath)
        {
            var sb = new StringBuilder();

            sb.AppendLine("=== LGB File Analysis ===");
            sb.AppendLine($"File ID: {data.Header.FileID}");
            sb.AppendLine($"File Size: {data.Header.FileSize}");
            sb.AppendLine($"Total Chunks: {data.Header.TotalChunkCount}");
            sb.AppendLine();

            sb.AppendLine($"Layer Group: {data.ChunkHeader.Name}");
            sb.AppendLine($"Layer Count: {data.ChunkHeader.LayersCount}");
            sb.AppendLine();

            foreach (var layer in data.Layers)
            {
                sb.AppendLine($"Layer [{layer.LayerId}]: {layer.Name}");
                sb.AppendLine($"  Instance Objects: {layer.InstanceObjectCount}");
                sb.AppendLine($"  Tool Mode Visible: {layer.ToolModeVisible != 0}");
                sb.AppendLine($"  Read Only: {layer.ToolModeReadOnly != 0}");
                sb.AppendLine();

                if (layer.InstanceObjects != null)
                {
                    foreach (var obj in layer.InstanceObjects)
                    {
                        sb.AppendLine($"  Object [{obj.InstanceId}]: {obj.Name}");
                        sb.AppendLine($"    Type: {obj.AssetType}");
                        sb.AppendLine($"    Position: ({obj.Transform.Translation.X:F3}, {obj.Transform.Translation.Y:F3}, {obj.Transform.Translation.Z:F3})");
                        sb.AppendLine($"    Rotation: ({obj.Transform.Rotation.X:F3}, {obj.Transform.Rotation.Y:F3}, {obj.Transform.Rotation.Z:F3}, {obj.Transform.Rotation.W:F3})");
                        sb.AppendLine($"    Scale: ({obj.Transform.Scale.X:F3}, {obj.Transform.Scale.Y:F3}, {obj.Transform.Scale.Z:F3})");

                        if (obj.ObjectData != null)
                        {
                            foreach (var kvp in obj.ObjectData)
                            {
                                sb.AppendLine($"    {kvp.Key}: {kvp.Value}");
                            }
                        }
                        sb.AppendLine();
                    }
                }
            }

            File.WriteAllText(outputPath, sb.ToString());
        }
    }
}