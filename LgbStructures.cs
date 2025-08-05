using System.Collections.Generic;
using Lumina.Data.Parsing.Layer;

namespace LgbParser
{
    public class LgbData
    {
        public string FilePath { get; set; } = string.Empty;
        public LayerCommon.Layer[] Layers { get; set; } = [];
        public Dictionary<string, object> Metadata { get; set; } = new();
    }
}