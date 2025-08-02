using System;
using System.IO;
using System.Collections.Generic;

namespace LgbParser
{
    public class SafeLgbParser
    {
        private bool _debugMode = false;

        public SafeLgbParser(bool debugMode = false)
        {
            _debugMode = debugMode;
        }

        public LgbData ParseFile(string filePath)
        {
            try
            {
                if (_debugMode)
                    Console.WriteLine($"Starting to parse: {filePath}");

                using var fileStream = File.OpenRead(filePath);
                using var reader = new BinaryReader(fileStream);

                return ParseLgbData(reader, filePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing {filePath}: {ex.Message}");
                if (_debugMode)
                {
                    Console.WriteLine($"Stack trace: {ex.StackTrace}");
                }
                throw new InvalidDataException($"Failed to parse LGB file {filePath}: {ex.Message}", ex);
            }
        }

        private LgbData ParseLgbData(BinaryReader reader, string filePath)
        {
            var lgbData = new LgbData();

            // Parse header with validation
            lgbData.Header = ParseFileHeaderSafe(reader);

            if (_debugMode)
                Console.WriteLine($"Header parsed - FileSize: {lgbData.Header.FileSize}, ChunkCount: {lgbData.Header.TotalChunkCount}");

            // Parse chunk header with validation
            lgbData.ChunkHeader = ParseLayerChunkSafe(reader);

            if (_debugMode)
                Console.WriteLine($"Chunk parsed - LayersCount: {lgbData.ChunkHeader.LayersCount}");

            // Validate layer count before proceeding
            if (lgbData.ChunkHeader.LayersCount < 0 || lgbData.ChunkHeader.LayersCount > 1000)
            {
                throw new InvalidDataException($"Invalid layers count: {lgbData.ChunkHeader.LayersCount}. Expected 0-1000.");
            }

            // Parse layers with validation
            lgbData.Layers = ParseLayersSafe(reader, lgbData.ChunkHeader.LayersCount);

            return lgbData;
        }

        private FileHeader ParseFileHeaderSafe(BinaryReader reader)
        {
            var header = new FileHeader();

            try
            {
                // Read file ID (4 bytes)
                var fileIdBytes = reader.ReadBytes(4);
                header.FileID = System.Text.Encoding.ASCII.GetString(fileIdBytes);

                // Read file size
                header.FileSize = reader.ReadInt32();
                ValidateNonNegative(header.FileSize, "FileSize");

                // Read chunk count
                header.TotalChunkCount = reader.ReadInt32();
                ValidateNonNegative(header.TotalChunkCount, "TotalChunkCount");

                if (_debugMode)
                    Console.WriteLine($"FileHeader - ID: {header.FileID}, Size: {header.FileSize}, Chunks: {header.TotalChunkCount}");

                return header;
            }
            catch (Exception ex)
            {
                throw new InvalidDataException($"Error parsing file header at position {reader.BaseStream.Position}: {ex.Message}", ex);
            }
        }

        private LayerChunk ParseLayerChunkSafe(BinaryReader reader)
        {
            var chunk = new LayerChunk();

            try
            {
                var currentPos = reader.BaseStream.Position;

                // Read chunk ID (4 bytes)
                var chunkIdBytes = reader.ReadBytes(4);
                chunk.ChunkId = System.Text.Encoding.ASCII.GetString(chunkIdBytes);

                // Read chunk size
                chunk.ChunkSize = reader.ReadInt32();
                ValidateNonNegative(chunk.ChunkSize, "ChunkSize");

                // Read layer group ID
                chunk.LayerGroupId = reader.ReadInt32();

                // Read name offset
                var nameOffset = reader.ReadInt32();
                if (_debugMode)
                    Console.WriteLine($"Name offset: {nameOffset} at position {reader.BaseStream.Position}");

                // Read layers offset
                chunk.Layers = reader.ReadInt32();
                ValidateNonNegative(chunk.Layers, "LayersOffset");

                // Read layers count
                chunk.LayersCount = reader.ReadInt32();
                ValidateNonNegative(chunk.LayersCount, "LayersCount");

                // Read name if offset is valid
                if (nameOffset > 0 && nameOffset < reader.BaseStream.Length)
                {
                    var savedPos = reader.BaseStream.Position;
                    reader.BaseStream.Seek(currentPos + nameOffset, SeekOrigin.Begin);
                    chunk.Name = ReadNullTerminatedString(reader);
                    reader.BaseStream.Seek(savedPos, SeekOrigin.Begin);
                }
                else
                {
                    chunk.Name = "Unknown";
                    if (_debugMode)
                        Console.WriteLine($"Invalid name offset: {nameOffset}, using default name");
                }

                if (_debugMode)
                    Console.WriteLine($"LayerChunk - ID: {chunk.ChunkId}, Size: {chunk.ChunkSize}, Name: {chunk.Name}, LayersCount: {chunk.LayersCount}");

                return chunk;
            }
            catch (Exception ex)
            {
                throw new InvalidDataException($"Error parsing layer chunk at position {reader.BaseStream.Position}: {ex.Message}", ex);
            }
        }

        private Layer[] ParseLayersSafe(BinaryReader reader, int layersCount)
        {
            if (layersCount <= 0)
            {
                if (_debugMode)
                    Console.WriteLine("No layers to parse");
                return new Layer[0];
            }

            var layers = new Layer[layersCount];
            var currentPos = reader.BaseStream.Position;

            try
            {
                // Read layer offsets first
                var layerOffsets = new int[layersCount];
                for (int i = 0; i < layersCount; i++)
                {
                    layerOffsets[i] = reader.ReadInt32();
                    ValidateNonNegative(layerOffsets[i], $"LayerOffset[{i}]");

                    if (_debugMode)
                        Console.WriteLine($"Layer {i} offset: {layerOffsets[i]}");
                }

                // Parse each layer
                for (int i = 0; i < layersCount; i++)
                {
                    try
                    {
                        var layerPos = currentPos + layerOffsets[i];
                        ValidateStreamPosition(reader, layerPos, $"Layer[{i}]");

                        reader.BaseStream.Seek(layerPos, SeekOrigin.Begin);
                        layers[i] = ParseLayerSafe(reader, i);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Warning: Failed to parse layer {i}: {ex.Message}");
                        // Create a default layer to continue parsing
                        layers[i] = CreateDefaultLayer(i);
                    }
                }

                return layers;
            }
            catch (Exception ex)
            {
                throw new InvalidDataException($"Error parsing layers at position {reader.BaseStream.Position}: {ex.Message}", ex);
            }
        }

        private Layer ParseLayerSafe(BinaryReader reader, int layerIndex)
        {
            var layer = new Layer();
            var startPos = reader.BaseStream.Position;

            try
            {
                // Read layer ID
                layer.LayerId = reader.ReadUInt32();

                // Read name offset
                var nameOffset = reader.ReadInt32();

                // Read instance objects offset
                layer.InstanceObjectsOffset = reader.ReadInt32();
                ValidateNonNegative(layer.InstanceObjectsOffset, $"Layer[{layerIndex}].InstanceObjectsOffset");

                // Read instance object count
                layer.InstanceObjectCount = reader.ReadInt32();
                ValidateNonNegative(layer.InstanceObjectCount, $"Layer[{layerIndex}].InstanceObjectCount");

                // Validate instance object count is reasonable
                if (layer.InstanceObjectCount > 10000)
                {
                    Console.WriteLine($"Warning: Layer {layerIndex} has unusually high object count: {layer.InstanceObjectCount}");
                    layer.InstanceObjectCount = Math.Min(layer.InstanceObjectCount, 1000); // Cap at reasonable limit
                }

                // Read other layer properties
                layer.ToolModeVisible = reader.ReadByte();
                layer.ToolModeReadOnly = reader.ReadByte();
                layer.IsBushLayer = reader.ReadByte();
                layer.PS3Visible = reader.ReadByte();

                // Read name if offset is valid
                if (nameOffset > 0)
                {
                    try
                    {
                        var namePos = startPos + nameOffset;
                        ValidateStreamPosition(reader, namePos, $"Layer[{layerIndex}].Name");

                        var savedPos = reader.BaseStream.Position;
                        reader.BaseStream.Seek(namePos, SeekOrigin.Begin);
                        layer.Name = ReadNullTerminatedString(reader);
                        reader.BaseStream.Seek(savedPos, SeekOrigin.Begin);
                    }
                    catch (Exception ex)
                    {
                        layer.Name = $"Layer_{layerIndex}";
                        Console.WriteLine($"Warning: Could not read layer {layerIndex} name: {ex.Message}");
                    }
                }
                else
                {
                    layer.Name = $"Layer_{layerIndex}";
                }

                // Parse instance objects
                if (layer.InstanceObjectCount > 0)
                {
                    try
                    {
                        layer.InstanceObjects = ParseInstanceObjectsSafe(reader, startPos, layer.InstanceObjectsOffset, layer.InstanceObjectCount, layerIndex);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Warning: Failed to parse instance objects for layer {layerIndex}: {ex.Message}");
                        layer.InstanceObjects = new InstanceObject[0];
                    }
                }
                else
                {
                    layer.InstanceObjects = new InstanceObject[0];
                }

                if (_debugMode)
                    Console.WriteLine($"Layer {layerIndex} - ID: {layer.LayerId}, Name: {layer.Name}, Objects: {layer.InstanceObjectCount}");

                return layer;
            }
            catch (Exception ex)
            {
                throw new InvalidDataException($"Error parsing layer {layerIndex} at position {reader.BaseStream.Position}: {ex.Message}", ex);
            }
        }

        private InstanceObject[] ParseInstanceObjectsSafe(BinaryReader reader, long layerStartPos, int objectsOffset, int objectCount, int layerIndex)
        {
            if (objectCount <= 0)
                return new InstanceObject[0];

            var objects = new InstanceObject[objectCount];

            try
            {
                // Read object offsets first
                var objectOffsetsPos = layerStartPos + objectsOffset;
                ValidateStreamPosition(reader, objectOffsetsPos, $"Layer[{layerIndex}].ObjectOffsets");

                reader.BaseStream.Seek(objectOffsetsPos, SeekOrigin.Begin);

                var objectOffsets = new int[objectCount];
                for (int i = 0; i < objectCount; i++)
                {
                    objectOffsets[i] = reader.ReadInt32();
                    ValidateNonNegative(objectOffsets[i], $"Layer[{layerIndex}].ObjectOffset[{i}]");
                }

                // Parse each object
                for (int i = 0; i < objectCount; i++)
                {
                    try
                    {
                        var objPos = objectOffsetsPos + objectOffsets[i];
                        ValidateStreamPosition(reader, objPos, $"Layer[{layerIndex}].Object[{i}]");

                        reader.BaseStream.Seek(objPos, SeekOrigin.Begin);
                        objects[i] = ParseInstanceObjectSafe(reader, layerIndex, i);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Warning: Failed to parse object {i} in layer {layerIndex}: {ex.Message}");
                        objects[i] = CreateDefaultInstanceObject(layerIndex, i);
                    }
                }

                return objects;
            }
            catch (Exception ex)
            {
                throw new InvalidDataException($"Error parsing instance objects for layer {layerIndex}: {ex.Message}", ex);
            }
        }

        private InstanceObject ParseInstanceObjectSafe(BinaryReader reader, int layerIndex, int objectIndex)
        {
            var obj = new InstanceObject();
            var startPos = reader.BaseStream.Position;

            try
            {
                // Read asset type
                var assetTypeValue = reader.ReadInt32();
                obj.AssetType = (LayerEntryType)assetTypeValue;

                // Read instance ID
                obj.InstanceId = reader.ReadUInt32();

                // Read name offset
                var nameOffset = reader.ReadInt32();

                // Read transformation (12 floats = 48 bytes)
                obj.Transform = new Transformation
                {
                    Translation = new Vector3
                    {
                        X = reader.ReadSingle(),
                        Y = reader.ReadSingle(),
                        Z = reader.ReadSingle()
                    },
                    Rotation = new Vector4
                    {
                        X = reader.ReadSingle(),
                        Y = reader.ReadSingle(),
                        Z = reader.ReadSingle(),
                        W = reader.ReadSingle()
                    },
                    Scale = new Vector3
                    {
                        X = reader.ReadSingle(),
                        Y = reader.ReadSingle(),
                        Z = reader.ReadSingle()
                    }
                };

                // Read name if offset is valid
                if (nameOffset > 0)
                {
                    try
                    {
                        var namePos = startPos + nameOffset;
                        ValidateStreamPosition(reader, namePos, $"Layer[{layerIndex}].Object[{objectIndex}].Name");

                        var savedPos = reader.BaseStream.Position;
                        reader.BaseStream.Seek(namePos, SeekOrigin.Begin);
                        obj.Name = ReadNullTerminatedString(reader);
                        reader.BaseStream.Seek(savedPos, SeekOrigin.Begin);
                    }
                    catch (Exception ex)
                    {
                        obj.Name = $"Object_{layerIndex}_{objectIndex}";
                        Console.WriteLine($"Warning: Could not read object name: {ex.Message}");
                    }
                }
                else
                {
                    obj.Name = $"Object_{layerIndex}_{objectIndex}";
                }

                // Initialize object data
                obj.ObjectData = new Dictionary<string, object>
                {
                    ["AssetType"] = obj.AssetType.ToString(),
                    ["ParsedFromFile"] = true
                };

                return obj;
            }
            catch (Exception ex)
            {
                throw new InvalidDataException($"Error parsing instance object {objectIndex} in layer {layerIndex} at position {reader.BaseStream.Position}: {ex.Message}", ex);
            }
        }

        // Helper methods
        private void ValidateNonNegative(int value, string fieldName)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(fieldName, value, $"Value for {fieldName} must be non-negative");
            }
        }

        private void ValidateStreamPosition(BinaryReader reader, long position, string context)
        {
            if (position < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(position), position, $"Stream position for {context} cannot be negative");
            }

            if (position >= reader.BaseStream.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(position), position, $"Stream position for {context} ({position}) exceeds file length ({reader.BaseStream.Length})");
            }
        }

        private string ReadNullTerminatedString(BinaryReader reader)
        {
            var bytes = new List<byte>();
            byte b;

            while ((b = reader.ReadByte()) != 0)
            {
                bytes.Add(b);

                // Prevent infinite loops on corrupted data
                if (bytes.Count > 1000)
                {
                    Console.WriteLine("Warning: String reading exceeded 1000 characters, truncating");
                    break;
                }
            }

            return System.Text.Encoding.UTF8.GetString(bytes.ToArray());
        }

        private Layer CreateDefaultLayer(int index)
        {
            return new Layer
            {
                LayerId = (uint)index,
                Name = $"Layer_{index}_DefaultDueToError",
                InstanceObjectCount = 0,
                InstanceObjects = new InstanceObject[0],
                ToolModeVisible = 1,
                ToolModeReadOnly = 0,
                IsBushLayer = 0,
                PS3Visible = 1
            };
        }

        private InstanceObject CreateDefaultInstanceObject(int layerIndex, int objectIndex)
        {
            return new InstanceObject
            {
                InstanceId = (uint)objectIndex,
                Name = $"Object_{layerIndex}_{objectIndex}_DefaultDueToError",
                AssetType = LayerEntryType.AssetNone,
                Transform = new Transformation
                {
                    Translation = new Vector3 { X = 0, Y = 0, Z = 0 },
                    Rotation = new Vector4 { X = 0, Y = 0, Z = 0, W = 1 },
                    Scale = new Vector3 { X = 1, Y = 1, Z = 1 }
                },
                ObjectData = new Dictionary<string, object> { ["Error"] = "Created due to parsing error" }
            };
        }
    }
}