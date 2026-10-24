using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// The StylizedKitchen pack was exported from Unreal Engine: its .uasset materials cannot be
// read by Unity, so materials are rebuilt here from the TGA texture sets (T_<Name>_BC/_N/_ORM)
// and remapped onto the FBX material slots by name.
public static class KitchenMaterialBuilder
{
    const string EnvRoot = "Assets/models/env";
    const string TexturesFolder = EnvRoot + "/Content/StylizedKitchen/Textures";
    const string MeshesFolder = EnvRoot + "/Content/StylizedKitchen/Meshes";
    const string MaterialsFolder = EnvRoot + "/Materials";
    // The project's toon shader (Assets/Shaders/StylizedToon.shader) reads the same _BaseMap/_BumpMap/
    // _ORMMap properties as the pack's KitchenORM_Lit graph, so props keep their textures under the
    // toon look. Point this back at "StylizedKitchen/KitchenORM_Lit" for the original shading.
    const string OrmShaderName = "FlySimulator/StylizedToon";

    static readonly Regex TextureSetPattern = new Regex(@"^T_(.+)_(BC|N|ORM)$");
    static readonly Regex UnrealMaterialPrefix = new Regex(@"^MI?_");
    static readonly Regex TrailingNumber = new Regex(@"_?\d+$");

    class TextureSet
    {
        public Texture2D BaseColor;
        public Texture2D Normal;
        public Texture2D Orm;
    }

    [MenuItem("Tools/Stylized Kitchen/Rebuild Materials")]
    public static void RebuildMaterials()
    {
        Shader ormShader = Shader.Find(OrmShaderName);
        if (ormShader == null)
        {
            // Fails closed: without the ORM shader every generated material would render wrong.
            Debug.LogError($"KitchenMaterialBuilder: shader '{OrmShaderName}' not found. " +
                           "Check that the shader file defining it compiled without errors in the Console.");
            return;
        }

        Dictionary<string, TextureSet> textureSets = FindTextureSets();
        Dictionary<string, Material> materials = CreateMaterials(textureSets, ormShader);
        List<string> unmatchedSlots = RemapModelMaterials(materials);

        Debug.Log($"KitchenMaterialBuilder: built {materials.Count} materials in {MaterialsFolder}.");
        if (unmatchedSlots.Count > 0)
            Debug.LogWarning("KitchenMaterialBuilder: no TGA texture set for these slots, left as imported:\n" +
                             string.Join("\n", unmatchedSlots));
    }

    static Dictionary<string, TextureSet> FindTextureSets()
    {
        var texturePaths = new List<(string path, string setName, string channel)>();
        foreach (string guid in AssetDatabase.FindAssets("t:Texture2D", new[] { TexturesFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            Match match = TextureSetPattern.Match(Path.GetFileNameWithoutExtension(path));
            if (match.Success)
                texturePaths.Add((path, match.Groups[1].Value, match.Groups[2].Value));
        }

        // Batch the importer changes so Unity reimports once instead of per texture.
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (var (path, _, channel) in texturePaths)
                ConfigureTextureImporter(path, channel);
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }

        var textureSets = new Dictionary<string, TextureSet>();
        foreach (var (path, setName, channel) in texturePaths)
        {
            if (!textureSets.TryGetValue(setName, out TextureSet set))
                textureSets[setName] = set = new TextureSet();
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (channel == "BC") set.BaseColor = texture;
            else if (channel == "N") set.Normal = texture;
            else set.Orm = texture;
        }
        return textureSets;
    }

    static void ConfigureTextureImporter(string path, string channel)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        bool isNormal = channel == "N";
        TextureImporterType textureType = isNormal ? TextureImporterType.NormalMap : TextureImporterType.Default;
        // ORM holds linear data (occlusion, roughness, metallic), not colour.
        bool isSRGB = channel == "BC";
        // Unreal exports DirectX-style normal maps (green points down); Unity expects OpenGL-style.
        bool shouldFlipGreen = isNormal;

        if (importer.textureType == textureType && importer.sRGBTexture == isSRGB &&
            importer.flipGreenChannel == shouldFlipGreen)
            return;

        importer.textureType = textureType;
        importer.sRGBTexture = isSRGB;
        importer.flipGreenChannel = shouldFlipGreen;
        importer.SaveAndReimport();
    }

    static Dictionary<string, Material> CreateMaterials(Dictionary<string, TextureSet> textureSets, Shader ormShader)
    {
        if (!AssetDatabase.IsValidFolder(MaterialsFolder))
            AssetDatabase.CreateFolder(EnvRoot, "Materials");

        var materials = new Dictionary<string, Material>();
        foreach (var (setName, set) in textureSets)
        {
            // Sets without colour or ORM (e.g. a lone normal or alpha mask) are not a full material.
            if (set.BaseColor == null || set.Orm == null)
            {
                Debug.Log($"KitchenMaterialBuilder: skipping incomplete texture set '{setName}'.");
                continue;
            }
            materials[setName] = CreateOrUpdateMaterial(setName, set, ormShader);
        }
        AssetDatabase.SaveAssets();
        return materials;
    }

    static Material CreateOrUpdateMaterial(string setName, TextureSet set, Shader ormShader)
    {
        string path = $"{MaterialsFolder}/M_{setName}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(ormShader);
            AssetDatabase.CreateAsset(material, path);
        }

        material.shader = ormShader;
        material.SetTexture("_BaseMap", set.BaseColor);
        material.SetTexture("_ORMMap", set.Orm);
        if (set.Normal != null)
            material.SetTexture("_BumpMap", set.Normal);
        EditorUtility.SetDirty(material);
        return material;
    }

    static List<string> RemapModelMaterials(Dictionary<string, Material> materials)
    {
        var unmatchedSlots = new List<string>();
        AssetDatabase.StartAssetEditing();
        try
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Model", new[] { MeshesFolder }))
                RemapModel(AssetDatabase.GUIDToAssetPath(guid), materials, unmatchedSlots);
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }
        return unmatchedSlots;
    }

    static void RemapModel(string modelPath, Dictionary<string, Material> materials, List<string> unmatchedSlots)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(modelPath);
        bool hasChanged = false;
        foreach (string slotName in GetMaterialSlotNames(modelPath, importer))
        {
            string setName = ResolveTextureSet(slotName, materials.Keys);
            if (setName == null)
            {
                unmatchedSlots.Add($"{Path.GetFileName(modelPath)}: {slotName}");
                continue;
            }
            var slot = new AssetImporter.SourceAssetIdentifier(typeof(Material), slotName);
            importer.GetExternalObjectMap().TryGetValue(slot, out Object current);
            if (current == materials[setName])
                continue;
            importer.AddRemap(slot, materials[setName]);
            hasChanged = true;
        }
        if (hasChanged)
            importer.SaveAndReimport();
    }

    // Slots not yet remapped are embedded Material sub-assets; remapped ones live in the external map.
    static IEnumerable<string> GetMaterialSlotNames(string modelPath, ModelImporter importer)
    {
        IEnumerable<string> embedded = AssetDatabase.LoadAllAssetsAtPath(modelPath).OfType<Material>().Select(m => m.name);
        IEnumerable<string> remapped = importer.GetExternalObjectMap().Keys
            .Where(id => id.type == typeof(Material)).Select(id => id.name);
        return embedded.Concat(remapped).Distinct();
    }

    // Unreal slot names differ from texture set names: "MI_Books" -> "Book",
    // "M_Bottles_05" -> "Bottles", "MI_Carpet_Inst" -> "Carpet_05".
    static string ResolveTextureSet(string slotName, ICollection<string> setNames)
    {
        string key = UnrealMaterialPrefix.Replace(slotName, "").Replace("_Inst", "");
        foreach (string candidate in new[] { key, TrailingNumber.Replace(key, "") })
        {
            if (setNames.Contains(candidate)) return candidate;
            if (setNames.Contains(candidate + "s")) return candidate + "s";
            if (setNames.Contains(candidate.TrimEnd('s'))) return candidate.TrimEnd('s');
        }

        // Only one variant of some sets shipped as TGA (e.g. Carpet_05 for Carpet_01..05).
        string baseName = TrailingNumber.Replace(key, "");
        string[] variants = setNames.Where(name => name.StartsWith(baseName + "_")).ToArray();
        return variants.Length == 1 ? variants[0] : null;
    }
}
