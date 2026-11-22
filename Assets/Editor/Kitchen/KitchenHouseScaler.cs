using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Resizes the house already built into the open scene to KitchenMapBuilder.WorldScale, without
// rebuilding it (Tools > Stylized Kitchen > Resize House To World Scale).
// Rebuilding would throw away everything placed in the scene by hand since - the spawn points, the
// couscous, the networking - so the map root is simply rescaled and everything standing outside it
// is moved to keep its place in the house.
// What scales and what does not: everything measured in house metres grows with the house, which
// means the old man too, so he stays a man and not a child in a hall. The fly keeps its own size,
// and that is the whole point of a bigger house - the room it gains is room to fly in.
// It also sets every ScaledGravity multiplier to the new scale, because that number is the scene's
// scale: left behind, the props drift to the floor like feathers in a house this size.
// Run it with each kitchen scene open in turn, then save.
public static class KitchenHouseScaler
{
    const string MapRootName = "KitchenMap";

    [MenuItem("Tools/Stylized Kitchen/Resize House To World Scale")]
    static void Resize()
    {
        Scene scene = SceneManager.GetActiveScene();
        Transform map = FindMapRoot(scene);
        if (map == null)
        {
            // Fails closed: without the map root there is no house to resize and no scale to measure
            // the rest of the scene against.
            Debug.LogError($"Resize House: '{scene.name}' has no root object named '{MapRootName}'. " +
                           "Open Kitchen or Kitchen_online, or build the house first (Tools > Stylized Kitchen > Build Kitchen Map).");
            return;
        }

        float current = map.localScale.x;
        float target = KitchenMapBuilder.WorldScale;
        if (current <= 0f)
        {
            Debug.LogError($"Resize House: '{MapRootName}' is scaled to {current}; it has to be a positive, uniform scale.");
            return;
        }
        float factor = target / current;
        if (Mathf.Approximately(factor, 1f))
        {
            Debug.Log($"Resize House: '{scene.name}' is already built at {target} units per metre; nothing to do.");
            return;
        }

        int moved = 0;
        int rescaled = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.transform == map || IsScreenSpaceUi(root))
            {
                continue;
            }
            Undo.RecordObject(root.transform, "Resize House");
            // Objects at the origin stay at the origin, so the managers and the HUD are unaffected
            // and only what actually stands somewhere in the house is moved.
            root.transform.localPosition *= factor;
            moved++;
            if (IsSizedInHouseMetres(root))
            {
                // Set, not multiplied: the old man is one metre-scaled body, so his scale simply is
                // the house's units per metre. Multiplying would scale him again every run, and it
                // did - the prefab had already been put at the new scale, so he came out at 67.5.
                root.transform.localScale = Vector3.one * target;
                rescaled++;
            }
        }

        Undo.RecordObject(map, "Resize House");
        map.localScale = Vector3.one * target;
        int fallers = RetuneFalling(scene, target);
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"Resize House: '{scene.name}' is now {target} units per metre (x{factor}). " +
                  $"{moved} objects outside the map were moved with it, {rescaled} of them resized, " +
                  $"and {fallers} falling objects now fall at the house's own scale. Save the scene (Ctrl+S).");
    }

    // A screen-space canvas is not in the house at all: its RectTransform is in pixels and Unity
    // rewrites it from the screen size every frame, so moving it means nothing and only leaves a
    // puzzling number in the Inspector.
    static bool IsScreenSpaceUi(GameObject root)
    {
        Canvas canvas = root.GetComponent<Canvas>();
        return canvas != null && canvas.renderMode != RenderMode.WorldSpace;
    }

    // ScaledGravity's Multiplier is how many times normal gravity an object falls with, and it is
    // meant to be the scale of the scene it stands in, so the house's scale is the answer for all of
    // them. They were left at 2 and 10 from earlier, much smaller houses, which is why a knocked
    // bottle sank to the floor instead of dropping.
    static int RetuneFalling(Scene scene, float worldScale)
    {
        ScaledGravity[] falling = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<ScaledGravity>(true))
            .ToArray();
        foreach (ScaledGravity faller in falling)
        {
            var serialized = new SerializedObject(faller);
            serialized.FindProperty("multiplier").floatValue = worldScale;
            serialized.ApplyModifiedProperties();
        }
        return falling.Length;
    }

    // The old man is a 1.75 m man and has to stay one, so his scale is set to the house's units per
    // metre. The fly deliberately is not touched: it keeps its own size and so gains the room.
    static bool IsSizedInHouseMetres(GameObject root) =>
        root.GetComponentInChildren<OldPersonFPSController>(true) != null;

    static Transform FindMapRoot(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == MapRootName)
            {
                return root.transform;
            }
        }
        return null;
    }
}
