// Unity editor tool for the Blade & Sorcery SDK (Unity 2021.3).
// Menu: Tools > Revolver > Build Revolver Prefab
//
// Turns any revolver model under Assets/revolver into a working B&S item:
// copies the SDK's ProtoGun example (handles, colliders, holder point...),
// drops the model in, works out which way the barrel and the grip point,
// scales it to real size, puts the hand on the grip and the muzzle ("Aim")
// on the barrel tip, fits the colliders, hooks up the textures, and sets up
// the Addressables group + Asset Bundle Group so it only needs "Build".

using System.Collections.Generic;
using System.IO;
using System.Linq;
using ThunderRoad;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

public static class RevolverPrefabBuilder
{
    private const string ModelFolder = "Assets/revolver";
    private const string PrefabPath = "Assets/revolver/RevolverItem.prefab";
    private const string ItemId = "Revolver";
    private const string Address = "Danil.Revolver";
    private const string GroupName = "Revolver";
    private const string BundleGroupPath = "Assets/Personal/AssetBundleGroups/Revolver.asset";
    private const float RevolverLength = 0.30f; // metres, muzzle to back of the grip

    private const string FlipForwardKey = "RevolverBuilder.FlipForward";
    private const string FlipUpKey = "RevolverBuilder.FlipUp";

    [MenuItem("Tools/Revolver/Build Revolver Prefab", false, 1)]
    public static void BuildMenu() => Build(askOverwrite: true);

    [MenuItem("Tools/Revolver/Fix: barrel points backwards", false, 20)]
    public static void FlipForward()
    {
        EditorPrefs.SetBool(FlipForwardKey, !EditorPrefs.GetBool(FlipForwardKey));
        Build(askOverwrite: false);
    }

    [MenuItem("Tools/Revolver/Fix: revolver is upside down", false, 21)]
    public static void FlipUp()
    {
        EditorPrefs.SetBool(FlipUpKey, !EditorPrefs.GetBool(FlipUpKey));
        Build(askOverwrite: false);
    }

    private static void Build(bool askOverwrite)
    {
        GameObject model = FindModel();
        if (model == null)
        {
            EditorUtility.DisplayDialog("Revolver", "No model found. Put the revolver .fbx (or .obj) somewhere inside Assets/revolver.", "OK");
            return;
        }
        string protoPath = AssetDatabase.FindAssets("ProtoGun t:Prefab").Select(AssetDatabase.GUIDToAssetPath).FirstOrDefault();
        if (protoPath == null)
        {
            EditorUtility.DisplayDialog("Revolver", "ProtoGun prefab not found in the SDK (Assets/SDK/Examples/Items).", "OK");
            return;
        }
        if (askOverwrite && File.Exists(PrefabPath)
            && !EditorUtility.DisplayDialog("Revolver", PrefabPath + " already exists. Rebuild it?", "Rebuild", "Cancel"))
            return;

        GameObject root = PrefabUtility.LoadPrefabContents(protoPath);
        try
        {
            SetupItem(root);
            Transform mesh = root.transform.Find("Mesh");
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(model, mesh);
            instance.name = "Model";
            PlaceModel(root.transform, mesh, instance.transform);
            AssignTextures(instance, model);
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }

        SetupAddressables();
        AssetDatabase.SaveAssets();

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Selection.activeObject = prefab;
        EditorGUIUtility.PingObject(prefab);
        AssetDatabase.OpenAsset(prefab);
        Debug.Log("[Revolver] Built " + PrefabPath + " from " + AssetDatabase.GetAssetPath(model));
        EditorUtility.DisplayDialog("Revolver",
            "Done! The prefab is open.\n\n" +
            "Check: the barrel points along the BLUE arrow and the grip is down.\n" +
            "If not, use Tools > Revolver > Fix...\n\n" +
            "Then: ThunderRoad (SDK) > Asset Bundle Builder > tick 'Revolver' > Build.", "OK");
    }

    private static GameObject FindModel()
    {
        if (Selection.activeObject is GameObject selected
            && PrefabUtility.GetPrefabAssetType(selected) == PrefabAssetType.Model)
            return selected;
        if (!AssetDatabase.IsValidFolder(ModelFolder)) return null;
        return AssetDatabase.FindAssets("t:Model", new[] { ModelFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
            .FirstOrDefault(go => go != null);
    }

    private static void SetupItem(GameObject root)
    {
        root.name = "RevolverItem";
        Item item = root.GetComponent<Item>();
        if (item != null) item.itemId = ItemId;

        AIFireable fireable = root.GetComponent<AIFireable>();
        if (fireable != null) Object.DestroyImmediate(fireable, true);

        // The example spawns an apple; we don't want that.
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true).ToArray())
            if (child != null && child.name.StartsWith("ProtoApple")) Object.DestroyImmediate(child.gameObject, true);
    }

    // ---------------------------------------------------------------- placement

    private static void PlaceModel(Transform root, Transform mesh, Transform model)
    {
        model.localPosition = Vector3.zero;
        model.localRotation = Quaternion.identity;
        model.localScale = Vector3.one;

        // 1. Orientation, worked out in the model's own space.
        List<Vector3> local = Points(model, model);
        if (local.Count == 0) { Debug.LogWarning("[Revolver] Model has no mesh."); return; }
        Bounds lb = BoundsOf(local);
        int[] axes = Enumerable.Range(0, 3).OrderByDescending(a => lb.size[a]).ToArray();
        int L = axes[0], S = axes[1];                     // length axis, height axis

        // The grip end is "tall" (grip hangs down), the barrel end is thin.
        float rearLow = MeanExtent(local, L, S, lb.min[L], lb.min[L] + lb.size[L] * 0.3f);
        float rearHigh = MeanExtent(local, L, S, lb.max[L] - lb.size[L] * 0.3f, lb.max[L]);
        float forwardSign = rearLow >= rearHigh ? 1f : -1f;   // barrel towards the thin end

        // The barrel sits at the top.
        float tipStart = forwardSign > 0 ? lb.max[L] - lb.size[L] * 0.25f : lb.min[L];
        float tipEnd = forwardSign > 0 ? lb.max[L] : lb.min[L] + lb.size[L] * 0.25f;
        List<Vector3> tip = local.Where(p => p[L] >= tipStart && p[L] <= tipEnd).ToList();
        float barrelS = tip.Count > 0 ? tip.Average(p => p[S]) : lb.center[S];
        float upSign = barrelS >= lb.center[S] ? 1f : -1f;

        if (EditorPrefs.GetBool(FlipForwardKey)) forwardSign = -forwardSign;
        if (EditorPrefs.GetBool(FlipUpKey)) upSign = -upSign;

        Vector3 fwd = Axis(L) * forwardSign;
        Vector3 up = Axis(S) * upSign;
        model.rotation = root.rotation * Quaternion.Inverse(Quaternion.LookRotation(fwd, up));

        // 2. Real-world size.
        Bounds rb = BoundsOf(Points(model, root));
        if (rb.size.z > 0.0001f) model.localScale *= RevolverLength / rb.size.z;

        // 3. Put the grip into the hand.
        List<Vector3> pts = Points(model, root);
        rb = BoundsOf(pts);
        float gripTop = rb.min.y + rb.size.y * 0.45f;
        List<Vector3> grip = pts.Where(p => p.y <= gripTop).ToList();
        Vector3 gripCenter = grip.Count > 0 ? Mean(grip) : new Vector3(rb.center.x, rb.min.y, rb.min.z);

        Transform handle = root.Find("Handle");
        Transform bottomHandle = root.Find("BottomHandle");
        Vector3 handleLocal = handle != null ? root.InverseTransformPoint(handle.position) : Vector3.zero;
        Vector3 shift = handleLocal - gripCenter;
        model.position += root.TransformVector(shift);
        for (int i = 0; i < pts.Count; i++) pts[i] += shift;
        rb = BoundsOf(pts);

        if (bottomHandle != null && handle != null)
        {
            // Keep the lower handle on the grip, a little below the main one.
            Vector3 low = new Vector3(handleLocal.x, Mathf.Max(rb.min.y + 0.02f, handleLocal.y - 0.05f), handleLocal.z);
            bottomHandle.position = root.TransformPoint(low);
        }

        // 4. Muzzle on the barrel tip, pointing forward.
        List<Vector3> front = pts.Where(p => p.z >= rb.max.z - rb.size.z * 0.05f).ToList();
        Vector3 muzzle = front.Count > 0 ? new Vector3(front.Average(p => p.x), front.Average(p => p.y), rb.max.z) : new Vector3(rb.center.x, rb.max.y, rb.max.z);
        Transform aim = root.Find("Aim");
        if (aim == null) { aim = new GameObject("Aim").transform; aim.SetParent(root, false); }
        aim.position = root.TransformPoint(muzzle);
        aim.rotation = root.rotation;

        // 5. Colliders: a box for frame + barrel, a box for the grip. Hide the grey blocks.
        float split = rb.min.y + rb.size.y * 0.55f;
        FitBox(mesh, "Cube", root, pts.Where(p => p.y >= split).ToList(), rb);
        FitBox(mesh, "Cylinder", root, pts.Where(p => p.y < split).ToList(), rb);
    }

    private static void FitBox(Transform mesh, string name, Transform root, List<Vector3> rootPoints, Bounds fallback)
    {
        Transform t = mesh.Find(name);
        if (t == null) { t = new GameObject(name).transform; t.SetParent(mesh, false); }
        t.localPosition = Vector3.zero;
        t.localRotation = Quaternion.identity;
        t.localScale = Vector3.one;

        MeshRenderer renderer = t.GetComponent<MeshRenderer>();
        if (renderer != null) renderer.enabled = false;
        foreach (Collider other in t.GetComponents<Collider>())
            if (!(other is BoxCollider)) Object.DestroyImmediate(other, true);
        BoxCollider box = t.GetComponent<BoxCollider>();
        if (box == null) box = t.gameObject.AddComponent<BoxCollider>(); // no ??: Unity fake-null

        Bounds rootBounds = rootPoints.Count > 0 ? BoundsOf(rootPoints) : fallback;
        // Convert the root-space box into the collider's local space.
        List<Vector3> corners = new List<Vector3>();
        for (int i = 0; i < 8; i++)
        {
            Vector3 c = new Vector3(
                (i & 1) == 0 ? rootBounds.min.x : rootBounds.max.x,
                (i & 2) == 0 ? rootBounds.min.y : rootBounds.max.y,
                (i & 4) == 0 ? rootBounds.min.z : rootBounds.max.z);
            corners.Add(t.InverseTransformPoint(root.TransformPoint(c)));
        }
        Bounds localBounds = BoundsOf(corners);
        box.center = localBounds.center;
        box.size = Vector3.Max(localBounds.size, Vector3.one * 0.01f);
    }

    // ---------------------------------------------------------------- textures

    private static void AssignTextures(GameObject instance, GameObject model)
    {
        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
        bool hasTexture = renderers.SelectMany(r => r.sharedMaterials)
            .Any(m => m != null && (m.HasProperty("_BaseMap") && m.GetTexture("_BaseMap") != null
                                    || m.HasProperty("_MainTex") && m.GetTexture("_MainTex") != null));
        if (hasTexture) return;

        string[] textures = AssetDatabase.FindAssets("t:Texture2D", new[] { ModelFolder }).Select(AssetDatabase.GUIDToAssetPath).ToArray();
        string baseMap = Pick(textures, "basecolor", "base_color", "albedo", "diffuse", "basemap", "color", "col", "diff");
        if (baseMap == null && textures.Length > 0) baseMap = textures.FirstOrDefault(t => Pick(new[] { t }, "normal", "nrm", "metal", "rough", "ao", "occlusion", "height", "emiss") == null);
        if (baseMap == null) return;
        string normal = Pick(textures, "normal", "nrm", "_n.");
        string metal = Pick(textures, "metallic", "metal");

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) return;
        string matPath = ModelFolder + "/RevolverMaterial.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null) { mat = new Material(shader); AssetDatabase.CreateAsset(mat, matPath); }
        mat.shader = shader;
        mat.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(baseMap));
        if (normal != null)
        {
            TextureImporter importer = AssetImporter.GetAtPath(normal) as TextureImporter;
            if (importer != null && importer.textureType != TextureImporterType.NormalMap)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.SaveAndReimport();
            }
            mat.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normal));
            mat.EnableKeyword("_NORMALMAP");
        }
        if (metal != null)
        {
            mat.SetTexture("_MetallicGlossMap", AssetDatabase.LoadAssetAtPath<Texture2D>(metal));
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
        }
        EditorUtility.SetDirty(mat);

        foreach (Renderer r in renderers)
            r.sharedMaterials = Enumerable.Repeat(mat, Mathf.Max(1, r.sharedMaterials.Length)).ToArray();
    }

    private static string Pick(string[] paths, params string[] keys)
    {
        foreach (string key in keys)
            foreach (string path in paths)
                if (Path.GetFileName(path).ToLowerInvariant().Contains(key)) return path;
        return null;
    }

    // ---------------------------------------------------------------- addressables

    private static void SetupAddressables()
    {
        AddressableAssetSettings settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) { Debug.LogError("[Revolver] Addressables settings not found."); return; }

        AddressableAssetGroup group = settings.FindGroup(GroupName);
        if (group == null)
        {
            AddressableAssetGroup template = settings.FindGroup("ProtoAssets") ?? settings.DefaultGroup;
            group = settings.CreateGroup(GroupName, false, false, true, template.Schemas);
        }
        string guid = AssetDatabase.AssetPathToGUID(PrefabPath);
        AddressableAssetEntry entry = settings.CreateOrMoveEntry(guid, group, false, true);
        entry.address = Address;
        settings.SetDirty(AddressableAssetSettings.ModificationEvent.EntryMoved, entry, true);

        AssetBundleGroup bundleGroup = AssetDatabase.LoadAssetAtPath<AssetBundleGroup>(BundleGroupPath);
        if (bundleGroup == null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(BundleGroupPath));
            bundleGroup = ScriptableObject.CreateInstance<AssetBundleGroup>();
            bundleGroup.folderName = "Revolver";
            bundleGroup.addressableAssetGroups = new List<AddressableAssetGroup>();
            bundleGroup.modDescription = "Six-shot revolver";
            bundleGroup.modAuthor = "Danil";
            AssetDatabase.CreateAsset(bundleGroup, BundleGroupPath);
        }
        if (bundleGroup.addressableAssetGroups == null) bundleGroup.addressableAssetGroups = new List<AddressableAssetGroup>();
        if (!bundleGroup.addressableAssetGroups.Contains(group)) bundleGroup.addressableAssetGroups.Add(group);
        bundleGroup.selected = true;
        bundleGroup.exportAfterBuild = true;
        EditorUtility.SetDirty(bundleGroup);
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>All mesh vertices of <paramref name="model"/>, in <paramref name="space"/>'s local coordinates.</summary>
    private static List<Vector3> Points(Transform model, Transform space)
    {
        List<Vector3> result = new List<Vector3>();
        foreach (MeshFilter filter in model.GetComponentsInChildren<MeshFilter>(true))
            AddVertices(filter.sharedMesh, filter.transform, space, result);
        foreach (SkinnedMeshRenderer skinned in model.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            AddVertices(skinned.sharedMesh, skinned.transform, space, result);
        return result;
    }

    private static void AddVertices(Mesh mesh, Transform owner, Transform space, List<Vector3> result)
    {
        if (mesh == null) return;
        Vector3[] vertices = mesh.vertices;
        int step = Mathf.Max(1, vertices.Length / 20000);
        for (int i = 0; i < vertices.Length; i += step)
            result.Add(space.InverseTransformPoint(owner.TransformPoint(vertices[i])));
    }

    private static Bounds BoundsOf(List<Vector3> points)
    {
        Bounds b = new Bounds(points[0], Vector3.zero);
        foreach (Vector3 p in points) b.Encapsulate(p);
        return b;
    }

    private static Vector3 Mean(List<Vector3> points)
    {
        Vector3 sum = Vector3.zero;
        foreach (Vector3 p in points) sum += p;
        return sum / points.Count;
    }

    /// <summary>Average height (axis S) of slices between from..to along axis L.</summary>
    private static float MeanExtent(List<Vector3> points, int L, int S, float from, float to)
    {
        const int slices = 6;
        float total = 0f;
        int counted = 0;
        for (int i = 0; i < slices; i++)
        {
            float a = Mathf.Lerp(from, to, i / (float)slices);
            float b = Mathf.Lerp(from, to, (i + 1) / (float)slices);
            float min = float.MaxValue, max = float.MinValue;
            foreach (Vector3 p in points)
            {
                if (p[L] < a || p[L] > b) continue;
                min = Mathf.Min(min, p[S]);
                max = Mathf.Max(max, p[S]);
            }
            if (max >= min) { total += max - min; counted++; }
        }
        return counted > 0 ? total / counted : 0f;
    }

    private static Vector3 Axis(int index) => index == 0 ? Vector3.right : index == 1 ? Vector3.up : Vector3.forward;
}
