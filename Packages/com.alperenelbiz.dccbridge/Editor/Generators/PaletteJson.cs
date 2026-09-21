using System;

namespace AlperenElbiz.DccBridge.Editor
{
    /// <summary>Mirrors data/palette.json. Field names match the JSON keys for JsonUtility.</summary>
    [Serializable]
    public class PaletteJson
    {
        public int version = 1;
        public AtlasJson atlas = new();
        public SwatchJson[] swatches = Array.Empty<SwatchJson>();
    }

    [Serializable]
    public class AtlasJson
    {
        public int size = 512;
        public int columns = 8;
        public int rows = 8;
    }

    [Serializable]
    public class SwatchJson
    {
        public string name;
        public string color;
        public string note;
    }
}
