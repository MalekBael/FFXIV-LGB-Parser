using System.IO;
using System.Text.Json;

namespace LgbParser
{
    public class JsonExporter : IExporter
    {
        public void Export(LgbData data, string outputPath)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var json = JsonSerializer.Serialize(data, options);
            File.WriteAllText(outputPath, json);
        }
    }

    public interface IExporter
    {
        void Export(LgbData data, string outputPath);
    }
}