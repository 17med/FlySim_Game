using UnityEditor;
using UnityEngine;

// Unreal FBX exports carry their collision hulls as extra meshes named UCX_<mesh>. Unreal hides
// them on import, but Unity renders them, wrapping every kitchen object in a visible shell.
// Turn them into invisible convex colliders, which is what they were authored for.
public class KitchenCollisionImporter : AssetPostprocessor
{
    // Same folder as KitchenMaterialBuilder.MeshesFolder.
    const string MeshesFolder = "Assets/models/env/Content/StylizedKitchen/Meshes";
    const string CollisionPrefix = "UCX_";

    // Bumping this makes Unity reimport the affected models with the new rule.
    public override uint GetVersion() => 1;

    void OnPostprocessModel(GameObject root)
    {
        if (!assetPath.StartsWith(MeshesFolder))
            return;

        foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
        {
            if (!filter.name.StartsWith(CollisionPrefix))
                continue;

            GameObject hullObject = filter.gameObject;
            Mesh hull = filter.sharedMesh;
            Object.DestroyImmediate(hullObject.GetComponent<MeshRenderer>());
            Object.DestroyImmediate(filter);

            var collider = hullObject.AddComponent<MeshCollider>();
            collider.sharedMesh = hull;
            collider.convex = true;
        }
    }
}
