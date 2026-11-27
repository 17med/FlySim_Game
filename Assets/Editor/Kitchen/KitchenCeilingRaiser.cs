using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Lifts the ceiling of the house already built into the open scene to KitchenMapBuilder.RoomHeight,
// without rebuilding it (Tools > Stylized Kitchen > Raise Ceiling To Room Height).
// The ceiling was at 2.6 m and the old man's slap reaches about 2.72 m looking up, so a fly could never
// get out of his reach. Rebuilding is not an option: the builder only targets Kitchen.unity and throws
// the whole KitchenMap root away, with everything placed in it by hand since, and Kitchen_online.unity
// has a copy of its own.
// What it does, in KitchenMap's local space (house metres):
// - the ceiling moves up so its underside is at the new height;
// - every block whose top met the old ceiling - walls, partition halves, lintels, the walls above the
//   windows, the hood chimney - keeps its bottom and grows to the new height;
// - the living room's ceiling lamp and its light move up with the ceiling.
// The kitchen's two pendant lamps keep their shades and lights where they are, 1.65 m up over the
// dining table, and only their cords grow to reach the new ceiling (a cord is one of the blocks that
// met the old ceiling). Moved up with the ceiling the shades would hang at 3.05 m, far above the
// table their light was tuned for, and FlyLifeWiring's fly spawns were placed clear of them where
// they are now.
// The current height is read from the ceiling itself, so a second run finds nothing to do.
// Run it with each kitchen scene open in turn (Kitchen, then Kitchen_online), then save.
public static class KitchenCeilingRaiser
{
    const string MapRootName = "KitchenMap";
    // Built by KitchenMapBuilder.Build as a direct child of the root's Shell group.
    const string CeilingPath = "Shell/Ceiling";
    const string UndoName = "Raise Ceiling";
    // 1 mm in house metres. The builder puts every top that meets the ceiling exactly on RoomHeight,
    // and the float error left after the x45 root scale is far below this, so nothing that was not
    // built to touch the ceiling is caught.
    const float Epsilon = 0.001f;
    // Every block the builder makes is Unity's built-in cube; the pack's models are all named SM_*,
    // so no furniture is mistaken for a wall and stretched.
    const string CubeMeshName = "Cube";
    // Fixtures hung from the ceiling, which move with it. The pendant shades and lights are
    // deliberately not here (see the class comment).
    static readonly string[] CeilingFixtureNames = { "CeilingLamp", "CeilingLight" };

    [MenuItem("Tools/Stylized Kitchen/Raise Ceiling To Room Height")]
    static void Raise()
    {
        Scene scene = SceneManager.GetActiveScene();
        Transform map = FindMapRoot(scene);
        Transform ceiling = map != null ? map.Find(CeilingPath) : null;
        if (ceiling == null)
        {
            // Fails open: nothing is changed. Without the ceiling there is no old height to measure the
            // walls against, and guessing one could stretch the wrong blocks.
            Debug.LogWarning($"Raise Ceiling: '{scene.name}' has no '{MapRootName}/{CeilingPath}'; nothing was changed. " +
                             "Open Kitchen or Kitchen_online, or build the house first (Tools > Stylized Kitchen > Build Kitchen Map).");
            return;
        }

        float oldHeight = BottomInMap(map, ceiling);
        float newHeight = KitchenMapBuilder.RoomHeight;
        float delta = newHeight - oldHeight;
        if (Mathf.Abs(delta) < Epsilon)
        {
            Debug.Log($"Raise Ceiling: '{scene.name}' already has its ceiling at {newHeight} m; nothing to do.");
            return;
        }

        Undo.SetCurrentGroupName(UndoName);
        int undoGroup = Undo.GetCurrentGroup();
        MoveUp(map, ceiling, delta);
        List<string> stretched = StretchBlocksMeetingCeiling(map, ceiling, oldHeight, newHeight);
        int fixtures = MoveCeilingFixtures(map, delta);
        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"Raise Ceiling: '{scene.name}' ceiling moved from {oldHeight:0.###} m to {newHeight} m. " +
                  $"{stretched.Count} blocks stretched to meet it ({string.Join(", ", stretched.Distinct())}), " +
                  $"{fixtures} ceiling fixtures moved up. Save the scene (Ctrl+S).");
    }

    // Returns the names of the blocks it stretched, for the log.
    static List<string> StretchBlocksMeetingCeiling(Transform map, Transform ceiling, float oldTop, float newTop)
    {
        var stretched = new List<string>();
        foreach (MeshFilter filter in map.GetComponentsInChildren<MeshFilter>(true))
        {
            Transform block = filter.transform;
            if (block == ceiling || !IsCube(filter))
                continue;
            if (Mathf.Abs(TopInMap(map, block) - oldTop) > Epsilon)
                continue;
            if (TryStretchTop(map, block, newTop))
                stretched.Add(block.name);
        }
        return stretched;
    }

    // Grows the block along its own up axis so its bottom stays where it is and its top lands on
    // newTop. The bottom is measured again after scaling, so whatever scale its parents carry, the
    // correction is exact.
    static bool TryStretchTop(Transform map, Transform block, float newTop)
    {
        float bottom = BottomInMap(map, block);
        float height = TopInMap(map, block) - bottom;
        if (height <= Epsilon || newTop - bottom <= Epsilon)
        {
            // Fails open: an upside-down or flat block, or a ceiling lowered below this block's bottom,
            // has no sensible stretch, so it is left as it is and reported.
            Debug.LogWarning($"Raise Ceiling: '{block.name}' cannot be stretched to {newTop} m (it spans {bottom:0.###} m " +
                             $"to {bottom + height:0.###} m); left unchanged.", block);
            return false;
        }
        Undo.RecordObject(block, UndoName);
        Vector3 scale = block.localScale;
        scale.y *= (newTop - bottom) / height;
        block.localScale = scale;
        block.position += map.TransformVector(Vector3.up * (bottom - BottomInMap(map, block)));
        return true;
    }

    static int MoveCeilingFixtures(Transform map, float delta)
    {
        int moved = 0;
        foreach (Transform child in map.GetComponentsInChildren<Transform>(true))
        {
            if (!CeilingFixtureNames.Contains(child.name))
                continue;
            MoveUp(map, child, delta);
            moved++;
        }
        return moved;
    }

    // delta is in house metres, along KitchenMap's up axis.
    static void MoveUp(Transform map, Transform target, float delta)
    {
        Undo.RecordObject(target, UndoName);
        target.position += map.TransformVector(Vector3.up * delta);
    }

    static bool IsCube(MeshFilter filter) =>
        filter.sharedMesh != null && filter.sharedMesh.name == CubeMeshName;

    // A unit cube's faces are at +-0.5 on its local axes, so these are the middles of its top and
    // bottom faces, in house metres above the floor.
    static float TopInMap(Transform map, Transform block) =>
        map.InverseTransformPoint(block.TransformPoint(0f, 0.5f, 0f)).y;

    static float BottomInMap(Transform map, Transform block) =>
        map.InverseTransformPoint(block.TransformPoint(0f, -0.5f, 0f)).y;

    static Transform FindMapRoot(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == MapRootName)
                return root.transform;
        }
        return null;
    }
}
