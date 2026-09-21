using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AlperenElbiz.DccBridge.Editor
{
    /// <summary>
    /// Turns each exported FBX into a placeable prefab: mesh, shared material, collider.
    ///
    /// This is the step that makes a prop usable. An imported FBX cannot carry a collider or a
    /// gameplay component, so anything downstream should reference the prefab, never the model.
    /// </summary>
    public static class PrefabBuilder
    {
        public const string PrefabDir = "Assets/Art/Prefabs";
        private const string ModelDir = "Assets/Art/Models";
        private const string ReceiptPath = ".pipeline-cache/props-receipt.json";

        public static string Rebuild(string projectRoot)
        {
            var manifestPath = Path.Combine(projectRoot, "data", "props.json");
            if (!File.Exists(manifestPath))
            {
                return "data/props.json not found.";
            }

            PropsManifest manifest;
            try
            {
                manifest = JsonUtility.FromJson<PropsManifest>(File.ReadAllText(manifestPath));
            }
            catch (Exception e)
            {
                return $"props.json could not be parsed: {e.Message}";
            }

            var materials = MaterialBuilder.RebuildAll(projectRoot);
            if (materials.Count == 0)
            {
                return "No materials could be built — generate the palette atlas first.";
            }

            // Blender decides material slot order during the join, so the exporter records it.
            // Guessing from props.json would silently swap materials between slots.
            var slotOrder = LoadSlotOrder(projectRoot);

            MaterialBuilder.EnsureFolder(PrefabDir);

            var built = new List<string>();
            var missing = new List<string>();

            foreach (var prop in manifest.props)
            {
                var modelPath = $"{ModelDir}/SM_{ToPascal(prop.name)}.fbx";
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);

                if (model == null)
                {
                    missing.Add(prop.name);
                    continue;
                }

                slotOrder.TryGetValue(prop.name, out var slots);
                if (BuildPrefab(prop, model, materials, slots, manifest.defaults))
                {
                    built.Add(prop.name);
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var report = $"{built.Count} prefab(s) -> {PrefabDir}";
            if (missing.Count > 0)
            {
                report += $"\nnot exported yet, skipped: {string.Join(", ", missing)}";
            }

            return report;
        }

        private static bool BuildPrefab(PropEntry prop, GameObject model,
            IReadOnlyDictionary<string, Material> materials, string[] slots, PropDefaults defaults)
        {
            var prefabPath = $"{PrefabDir}/P_{ToPascal(prop.name)}.prefab";

            // Rebuilt from the FBX each time rather than edited in place, so a prefab never
            // accumulates stale state. Saving to the same path keeps its GUID, so scene
            // references survive.
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model);
            if (instance == null)
            {
                Debug.LogError($"[DCC Bridge] could not instantiate {model.name}");
                return false;
            }

            try
            {
                instance.name = $"P_{ToPascal(prop.name)}";

                var fallback = materials.TryGetValue(MaterialBuilder.AtlasMaterialName, out var atlas)
                    ? atlas
                    : materials.Values.First();

                foreach (var renderer in instance.GetComponentsInChildren<MeshRenderer>())
                {
                    var count = Mathf.Max(1, renderer.sharedMaterials.Length);
                    var assigned = new Material[count];

                    for (var i = 0; i < count; i++)
                    {
                        var wanted = slots != null && i < slots.Length ? slots[i] : null;
                        assigned[i] = Resolve(wanted, materials) ?? fallback;

                        if (wanted != null && assigned[i] == fallback)
                        {
                            Debug.LogWarning(
                                $"[DCC Bridge] {prop.name} slot {i} wants '{wanted}', which does not exist. " +
                                "Declare it in data/materials.json, or paint that set in Substance.");
                        }
                    }

                    renderer.sharedMaterials = assigned;
                }

                AddCollider(instance, string.IsNullOrEmpty(prop.collider) ? defaults?.collider ?? "box" : prop.collider);

                PrefabUtility.SaveAsPrefabAsset(instance, prefabPath, out var success);
                if (!success)
                {
                    Debug.LogError($"[DCC Bridge] failed to save {prefabPath}");
                }

                return success;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// Finds a material by slot name, preferring one this run built but falling back to
        /// whatever is already on disk.
        ///
        /// A material can legitimately come from somewhere this builder does not know about —
        /// painted in Substance, or authored by hand. Only recognising generated ones silently
        /// dropped those slots back to the atlas material.
        /// </summary>
        private static Material Resolve(string name, IReadOnlyDictionary<string, Material> generated)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            return generated.TryGetValue(name, out var material)
                ? material
                : AssetDatabase.LoadAssetAtPath<Material>($"{MaterialBuilder.MaterialDir}/{name}.mat");
        }

        private static void AddCollider(GameObject instance, string kind)
        {
            switch (kind)
            {
                case "none":
                    return;

                case "mesh":
                    // Deliberately rare: a convex mesh collider costs far more than a box and is
                    // only worth it for a shape a box genuinely misrepresents.
                    foreach (var filter in instance.GetComponentsInChildren<MeshFilter>())
                    {
                        var collider = filter.gameObject.AddComponent<MeshCollider>();
                        collider.sharedMesh = filter.sharedMesh;
                        collider.convex = true;
                    }
                    return;

                default:
                    var bounds = CalculateBounds(instance);
                    var box = instance.AddComponent<BoxCollider>();
                    box.center = bounds.center;
                    box.size = bounds.size;
                    return;
            }
        }

        /// <summary>
        /// Bounds in the prefab root's space, derived from the mesh.
        ///
        /// Renderer.bounds is a world-space AABB that is not reliably current on a freshly
        /// instantiated prefab, which produces a correctly sized collider in the wrong place.
        /// Mesh bounds through the transform chain are deterministic.
        /// </summary>
        private static Bounds CalculateBounds(GameObject instance)
        {
            var filters = instance.GetComponentsInChildren<MeshFilter>()
                .Where(f => f.sharedMesh != null)
                .ToArray();

            if (filters.Length == 0)
            {
                return new Bounds(Vector3.zero, Vector3.one * 0.1f);
            }

            var initialised = false;
            var bounds = new Bounds();

            foreach (var filter in filters)
            {
                var local = filter.sharedMesh.bounds;
                var toInstance = instance.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;

                // Every corner, because a rotated child makes the centre alone insufficient.
                for (var corner = 0; corner < 8; corner++)
                {
                    var point = local.center + Vector3.Scale(local.extents, new Vector3(
                        (corner & 1) == 0 ? -1 : 1,
                        (corner & 2) == 0 ? -1 : 1,
                        (corner & 4) == 0 ? -1 : 1));

                    var transformed = toInstance.MultiplyPoint3x4(point);

                    if (!initialised)
                    {
                        bounds = new Bounds(transformed, Vector3.zero);
                        initialised = true;
                    }
                    else
                    {
                        bounds.Encapsulate(transformed);
                    }
                }
            }

            return bounds;
        }

        private static Dictionary<string, string[]> LoadSlotOrder(string projectRoot)
        {
            var order = new Dictionary<string, string[]>();
            var path = Path.Combine(projectRoot, ReceiptPath);

            if (!File.Exists(path))
            {
                return order;   // no export yet; every slot falls back to the atlas material
            }

            try
            {
                var receipt = JsonUtility.FromJson<PropReceipt>(File.ReadAllText(path));
                foreach (var entry in receipt?.props ?? Array.Empty<PropReceiptEntry>())
                {
                    if (entry.materials is { Length: > 0 })
                    {
                        order[entry.name] = entry.materials;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[DCC Bridge] export receipt unreadable ({e.Message}); using the atlas material everywhere");
            }

            return order;
        }

        internal static string ToPascal(string snake) =>
            string.Concat((snake ?? string.Empty).Replace("-", "_").Split('_')
                .Where(part => part.Length > 0)
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }
}
