using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Builds the house into both kitchens and wires everything that stands in it, in one go
// (Tools > Stylized Kitchen > Build House (Both Scenes)), or from a command line with
// -executeMethod HouseBuildPipeline.BuildAllBatch, which also renders a top-down shot of each.
// One pipeline because a rebuild throws KitchenMap away and the throwables' physics, the dishes, the
// coffee mug, the spawn points and the networking all hang off what it builds: by hand that is half a
// dozen menu items in the right order in two scenes, and one forgotten leaves a kitchen whose props
// float, whose dish is missing in a room, or whose flies spawn inside the old walls.
// Each kitchen is built and saved on its own: one that fails is left unsaved and the next still runs.
// Running it again is safe: every step reuses what it made before.
// The class is split by responsibility: this file is the per-scene flow, HouseBuildPipeline.Placement
// moves and wires what stands in the house, HouseBuildPipeline.Report logs the result and takes the shot.
public static partial class HouseBuildPipeline
{
    const string ScenesFolder = "Assets/Scenes/";
    const string UndoName = "Build House";

    // KitchenMapBuilder's contract: the empties under KitchenMap/Markers and the Throwables groups.
    const string MapRootName = "KitchenMap";
    const string MarkersGroupName = "Markers";
    const string HumanMarkerName = "HumanSpawn";
    const string FlyMarkerPrefix = "FlySpawn_";
    const int FlySpawnCount = 4;
    const string KitchenDishMarkerName = "DishSpot_Kitchen";
    const string PantryDishMarkerName = "DishSpot_Pantry";

    const string SpawnerObjectName = "Kitchen_Spawner";
    // In fly widths; KitchenSpawner explains why below 1 the flies spawn inside each other.
    const float FlySpacing = 1.5f;

    sealed class HouseMarkers
    {
        public Transform Map;
        public Transform Human;
        public Transform KitchenDish;
        public Transform PantryDish;
        public Transform[] Flies;
    }

    [MenuItem("Tools/Stylized Kitchen/Build House (Both Scenes)")]
    static void BuildFromMenu()
    {
        // Each kitchen is opened over whatever is open now, so unsaved work is offered up first.
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            Debug.Log("Build House: cancelled; nothing was changed.");
            return;
        }
        List<string> failed = BuildAll(null);
        if (failed.Count == 0)
        {
            Debug.Log("Build House: done. Both kitchens are rebuilt, wired and saved.");
        }
    }

    // Unity -batchmode -projectPath <project> -executeMethod HouseBuildPipeline.BuildAllBatch
    //       [-houseShotDir <dir>] -quit
    // Exit code 1 when any kitchen failed, so a script running it can tell.
    public static void BuildAllBatch()
    {
        List<string> failed = BuildAll(CommandLineValue(ShotDirectoryArgument));
        EditorApplication.Exit(failed.Count == 0 ? 0 : 1);
    }

    static List<string> BuildAll(string shotDirectory)
    {
        var failed = new List<string>();
        foreach (string sceneName in new[] { GameSceneManager.KitchenScene, GameSceneManager.KitchenOnlineScene })
        {
            if (!BuildScene(sceneName, shotDirectory))
            {
                failed.Add(sceneName);
            }
        }
        if (failed.Count > 0)
        {
            Debug.LogError($"Build House: {string.Join(" and ", failed)} failed and {(failed.Count == 1 ? "was" : "were")} not saved; the errors above say why.");
        }
        return failed;
    }

    // Fails closed per scene: any step that fails leaves this kitchen unsaved, and opening the next one
    // over it throws the half-built version away.
    static bool BuildScene(string sceneName, string shotDirectory)
    {
        string path = ScenesFolder + sceneName + ".unity";
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(path) == null)
        {
            Debug.LogError($"Build House: no scene at {path}. Was it moved or renamed?");
            return false;
        }
        Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
        bool isOnline = sceneName == GameSceneManager.KitchenOnlineScene;
        try
        {
            if (!KitchenMapBuilder.BuildHouseInto(scene))
            {
                Debug.LogError($"Build House: the house could not be built into {sceneName}; it was left unsaved.");
                return false;
            }
            if (!TryFindMarkers(scene, out HouseMarkers markers))
            {
                return false;
            }
            int throwables = WireThrowables(markers.Map);
            bool placed = PlaceDishes(scene, markers) && (isOnline ? PlaceOnlineSpawns(scene, markers) : PlaceOfflineSpawns(scene, markers));
            if (!placed || !RunWiringSteps(scene, isOnline))
            {
                Debug.LogError($"Build House: {sceneName} was left unsaved.");
                return false;
            }
            if (!EditorSceneManager.SaveScene(scene))
            {
                Debug.LogError($"Build House: could not save {sceneName}.");
                return false;
            }
            LogSummary(scene, markers, throwables);
            if (!string.IsNullOrEmpty(shotDirectory))
            {
                RenderTopDown(scene, markers.Map, shotDirectory);
            }
            return true;
        }
        catch (InvalidOperationException e)
        {
            // What the wiring tools throw when a serialized field they set has been renamed.
            Debug.LogError($"Build House: {sceneName} stopped and was left unsaved: {e.Message}");
            return false;
        }
    }

    // ---- the house's markers -------------------------------------------------------------------

    static bool TryFindMarkers(Scene scene, out HouseMarkers markers)
    {
        markers = null;
        GameObject map = scene.GetRootGameObjects().FirstOrDefault(root => root.name == MapRootName);
        Transform group = map != null ? map.transform.Find(MarkersGroupName) : null;
        if (group == null)
        {
            Debug.LogError($"Build House: {scene.name} has no {MapRootName}/{MarkersGroupName} after the build, so nothing can be placed.");
            return false;
        }
        var found = new HouseMarkers
        {
            Map = map.transform,
            Human = group.Find(HumanMarkerName),
            KitchenDish = group.Find(KitchenDishMarkerName),
            PantryDish = group.Find(PantryDishMarkerName),
            Flies = Enumerable.Range(1, FlySpawnCount).Select(i => group.Find(FlyMarkerPrefix + i)).ToArray(),
        };
        if (found.Human == null || found.KitchenDish == null || found.PantryDish == null || found.Flies.Any(fly => fly == null))
        {
            Debug.LogError($"Build House: {MapRootName}/{MarkersGroupName} in {scene.name} needs {HumanMarkerName}, {FlyMarkerPrefix}1..{FlySpawnCount}, " +
                           $"{KitchenDishMarkerName} and {PantryDishMarkerName}; at least one is missing.", group);
            return false;
        }
        markers = found;
        return true;
    }

    // ---- the existing wiring steps -------------------------------------------------------------

    // MatchRaidSetup's order, for its reason: the mug has to stand in the living room before Wire Match
    // looks for food. The network players come last: they network every loose Rigidbody, which now
    // includes the throwables, and they need the four fly spawns placed above.
    static bool RunWiringSteps(Scene scene, bool isOnline)
    {
        // The minimap after Wire Match, which makes the HUD it goes on.
        if (!CoffeeCupSetup.PlaceCoffeeInOpenScene() || !FlyLifeWiring.WireFlyLives() || !MatchWiring.WireScene(scene) || !MinimapWiring.AddToScene(scene))
        {
            return false;
        }
        return !isOnline || (KitchenOnlineWiring.WireScene(scene) && SetFlySpacing(scene));
    }

    // After Wire Network Players, which makes the spawner when the scene has none.
    static bool SetFlySpacing(Scene scene)
    {
        GameObject spawnerObject = scene.GetRootGameObjects().FirstOrDefault(root => root.name == SpawnerObjectName);
        KitchenSpawner spawner = spawnerObject != null ? spawnerObject.GetComponent<KitchenSpawner>() : null;
        if (spawner == null)
        {
            Debug.LogError($"Build House: {scene.name} has no {nameof(KitchenSpawner)} on '{SpawnerObjectName}' after Wire Network Players.");
            return false;
        }
        var fields = new SerializedObject(spawner);
        SetFloat(fields, "flySpacing", FlySpacing);
        fields.ApplyModifiedProperties();
        return true;
    }

    // ---- small helpers -------------------------------------------------------------------------

    static string CommandLineValue(string name)
    {
        string[] args = Environment.GetCommandLineArgs();
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    // Particle renderers are left out: in the Editor their bounds are wherever the last simulation was.
    static bool TryMeshBounds(GameObject target, out Bounds bounds)
    {
        return TryUnion(target.GetComponentsInChildren<Renderer>(true).Where(r => !(r is ParticleSystemRenderer)).ToList(), out bounds);
    }

    static bool TryUnion(List<Renderer> renderers, out Bounds bounds)
    {
        bounds = default;
        if (renderers.Count == 0)
        {
            return false;
        }
        bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers)
        {
            bounds.Encapsulate(renderer.bounds);
        }
        return true;
    }

    static T EnsureComponent<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : Undo.AddComponent<T>(target);
    }

    // Fails closed: a field renamed without this being updated stops the scene loudly (BuildScene
    // catches it) instead of leaving a dish or a prop with the wrong numbers.
    static void SetFloat(SerializedObject fields, string fieldName, float value)
    {
        SerializedProperty property = fields.FindProperty(fieldName);
        if (property == null)
        {
            throw new InvalidOperationException(
                $"{fields.targetObject.GetType().Name} has no serialized field '{fieldName}'. Update {nameof(HouseBuildPipeline)} to match it.");
        }
        property.floatValue = value;
    }
}
