﻿using System;
using System.Collections.Generic;

namespace LgbParser
{
    public class LgbData
    {
        public FileHeader Header { get; set; }
        public LayerChunk ChunkHeader { get; set; }
        public Layer[] Layers { get; set; }

        // New properties for GameLgbReader compatibility
        public string FilePath { get; set; }
        public List<LayerGroupData> LayerGroups { get; set; } = new List<LayerGroupData>();
    }

    public class FileHeader
    {
        public string FileID { get; set; }
        public int FileSize { get; set; }
        public int TotalChunkCount { get; set; }
    }

    public class LayerChunk
    {
        public string ChunkId { get; set; }
        public int ChunkSize { get; set; }
        public int LayerGroupId { get; set; }
        public string Name { get; set; }
        public int Layers { get; set; }
        public int LayersCount { get; set; }
    }

    public class Layer
    {
        public uint LayerId { get; set; }
        public string Name { get; set; }
        public int InstanceObjectsOffset { get; set; }
        public int InstanceObjectCount { get; set; }
        public byte ToolModeVisible { get; set; }
        public byte ToolModeReadOnly { get; set; }
        public byte IsBushLayer { get; set; }
        public byte PS3Visible { get; set; }
        public InstanceObject[] InstanceObjects { get; set; }
    }

    public class InstanceObject
    {
        public LayerEntryType AssetType { get; set; }
        public uint InstanceId { get; set; }
        public string Name { get; set; }
        public Transformation Transform { get; set; }
        public Dictionary<string, object> ObjectData { get; set; }
    }

    public class Transformation
    {
        public Vector3 Translation { get; set; }
        public Vector4 Rotation { get; set; }
        public Vector3 Scale { get; set; }
    }

    public class Vector3
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
    }

    public class Vector4
    {
        public float X { get; set; }
        public float Y { get; set; }
        public float Z { get; set; }
        public float W { get; set; }
    }

    // New data structures for GameLgbReader compatibility
    public class LayerGroupData
    {
        public uint LayerId { get; set; }
        public string Name { get; set; }
        public List<InstanceObjectData> InstanceObjects { get; set; } = new List<InstanceObjectData>();
    }

    public class InstanceObjectData
    {
        public uint InstanceId { get; set; }
        public string Name { get; set; }
        public string AssetType { get; set; }
        public TransformData Transform { get; set; }
        public Dictionary<string, object> ObjectData { get; set; }
    }

    public class TransformData
    {
        public float[] Translation { get; set; }
        public float[] Rotation { get; set; }
        public float[] Scale { get; set; }
    }

    public enum LayerEntryType
    {
        AssetNone = 0x0,
        BG = 0x1,
        Attribute = 0x2,
        LayLight = 0x3,
        VFX = 0x4,
        PositionMarker = 0x5,
        SharedGroup = 0x6,
        Sound = 0x7,
        EventNPC = 0x8,
        BattleNPC = 0x9,
        RoutePath = 0xA,
        Character = 0xB,
        Aetheryte = 0xC,
        EnvSet = 0xD,
        Gathering = 0xE,
        HelperObject = 0xF,
        Treasure = 0x10,
        Clip = 0x11,
        ClipCtrlPoint = 0x12,
        ClipCamera = 0x13,
        ClipLight = 0x14,
        ClipReserve00 = 0x15,
        ClipReserve01 = 0x16,
        ClipReserve02 = 0x17,
        ClipReserve03 = 0x18,
        ClipReserve04 = 0x19,
        ClipReserve05 = 0x1A,
        ClipReserve06 = 0x1B,
        ClipReserve07 = 0x1C,
        ClipReserve08 = 0x1D,
        ClipReserve09 = 0x1E,
        ClipReserve10 = 0x1F,
        ClipReserve11 = 0x20,
        ClipReserve12 = 0x21,
        ClipReserve13 = 0x22,
        ClipReserve14 = 0x23,
        CutAssetOnlySelectable = 0x24,
        Player = 0x25,
        Monster = 0x26,
        Weapon = 0x27,
        PopRange = 0x28,
        ExitRange = 0x29,
        LVB = 0x2A,
        MapRange = 0x2B,
        NaviMeshRange = 0x2C,
        EventObject = 0x2D,
        DemiHuman = 0x2E,
        EnvLocation = 0x2F,
        ControlPoint = 0x30,
        EventRange = 0x31,
        RestBonusRange = 0x32,
        QuestMarker = 0x33,
        Timeline = 0x34,
        ObjectBehaviorSet = 0x35,
        Movie = 0x36,
        ScenarioExd = 0x37,
        ScenarioText = 0x38,
        CollisionBox = 0x39,
        DoorRange = 0x3A,
        LineVFX = 0x3B,
        SoundEnvSet = 0x3C,
        CutActionTimeline = 0x3D,
        CharaScene = 0x3E,
        CutAction = 0x3F,
        EquipPreset = 0x40,
        ClientPath = 0x41,
        ServerPath = 0x42,
        GimmickRange = 0x43,
        TargetMarker = 0x44,
        ChairMarker = 0x45,
        ClickableRange = 0x46,
        PrefetchRange = 0x47,
        FateRange = 0x48,
        PartyMember = 0x49,
        KeepRange = 0x4A,
        SphereCastRange = 0x4B,
        IndoorObject = 0x4C,
        OutdoorObject = 0x4D,
        EditGroup = 0x4E,
        StableChocobo = 0x4F,
        MaxAssetType = 0x50
    }
}