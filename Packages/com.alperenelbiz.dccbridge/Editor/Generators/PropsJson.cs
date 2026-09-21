using System;

namespace AlperenElbiz.DccBridge.Editor
{
    /// <summary>Mirrors data/props.json. Field names match the JSON keys for JsonUtility.</summary>
    [Serializable]
    public class PropsManifest
    {
        public int version = 1;
        public string blend_root = "art-source/blender";
        public PropDefaults defaults = new();
        public PropEntry[] props = Array.Empty<PropEntry>();
    }

    [Serializable]
    public class PropDefaults
    {
        public string material = "M_PropAtlas";
        public string collection = "Export";
        public string swatch = "";
        public string collider = "box";
        public int tri_budget;
    }

    [Serializable]
    public class PropEntry
    {
        public string name;
        public string blend;
        public string builder;
        public string category;
        public string material;
        public string swatch;
        public string collider;
        public int tri_budget;
        public float[] size;
        public string[] materials;
    }

    /// <summary>
    /// Written by the Blender exporter. Carries the material slot order Blender produced, which
    /// is the only reliable way for the prefab builder to know which slot is which — a join
    /// decides that order, not the manifest.
    /// </summary>
    [Serializable]
    public class PropReceipt
    {
        public PropReceiptEntry[] props = Array.Empty<PropReceiptEntry>();
    }

    [Serializable]
    public class PropReceiptEntry
    {
        public string name;
        public string output;
        public int triangles;
        public string[] materials;
    }
}
