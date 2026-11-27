using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Gives the fly its two lives and the old man his boot (Tools > Match > Wire Fly Lives): FlyHealth and
// FlyHealthNetwork on the fly prefab, OldPersonStomp on the old man's prefab, and the spawn points the
// fly comes back at tagged FlySpawn in the open kitchen.
// A menu step rather than hand-edited files for the usual reason - Netcode works out a NetworkObject's
// ids when its components are added through the Editor - and because the respawn points are found at
// runtime by tag, which means something has to carry that tag in both kitchens. The online kitchen
// already has the "fly pawner" objects KitchenSpawner spawns players at, so those are tagged; the
// single player kitchen has none, so it gets one empty per FlySpawn_ marker KitchenMapBuilder puts
// under KitchenMap/Markers.
// Run it with Kitchen open, then again with Kitchen_online open. Running it again is safe: it reuses
// the components and the spawn points it made before, and never moves a spawn point that already
// exists, so one dragged somewhere better by hand stays there.
public static class FlyLifeWiring
{
    const string FlyPrefabPath = "Assets/objectnetwork/fly.prefab";
    // The old man as both kitchens place him. Assets/models/old_person/old_person.prefab is the bare
    // FBX this is a variant of, and carries none of his scripts; see WireStomp.
    const string HumanVariantPrefabPath = "Assets/objectnetwork/old_person Variant.prefab";
    const string TagManagerPath = "ProjectSettings/TagManager.asset";

    // The model under the fly's Animator. Its renderers are what gets hidden while the fly is dead;
    // the orbit camera hangs off the prefab root instead, so the dead player still sees the kitchen.
    const string FlyModelName = "Fly_Model";

    // The spawn points Kitchen_online already has, named as KitchenSpawner's wiring finds them
    // (KitchenOnlineWiring.FlySpawnPrefix, repeated here because it is private to that file - these two
    // have to agree, and the names are in the scene).
    const string ExistingSpawnPrefix = "fly pawner";
    // The ones this makes for the single player kitchen.
    const string SpawnGroupName = "FlySpawns";
    const string SpawnNamePrefix = "FlySpawn_";

    // Where a fly can appear in the single player kitchen: the house's own FlySpawn_ markers, as
    // KitchenMapBuilder builds them (KitchenMap/Markers/FlySpawn_1..). Read from the house rather than
    // written here, so the spawn points move with the layout instead of being left in the old one -
    // which is what happened to the six positions this used to list.
    const string MapRootName = "KitchenMap";
    const string MarkersGroupName = "Markers";

    [MenuItem("Tools/Match/Wire Fly Lives")]
    static void WireFromMenu()
    {
        if (WireFlyLives())
        {
            Debug.Log($"Wire Fly Lives: done. Save {SceneManager.GetActiveScene().name} (Ctrl+S)." +
                      (SceneManager.GetActiveScene().name == GameSceneManager.KitchenScene
                          ? $" Now open {GameSceneManager.KitchenOnlineScene} and run it again."
                          : ""));
        }
    }

    // The whole step: the tag, both prefabs and the open kitchen's spawn points. Marks the scene dirty
    // but does not save it, the way Wire Match does; MatchRaidSetup saves it.
    // Fails closed: a fly with health that cannot respawn, or an old man whose boot does nothing, is
    // worse than a run that stops and says why.
    public static bool WireFlyLives()
    {
        Scene scene = SceneManager.GetActiveScene();
        bool isOnline = scene.name == GameSceneManager.KitchenOnlineScene;
        if (!isOnline && scene.name != GameSceneManager.KitchenScene)
        {
            Debug.LogError($"Wire Fly Lives: open {GameSceneManager.KitchenScene} or {GameSceneManager.KitchenOnlineScene} first (the open scene is '{scene.name}').");
            return false;
        }
        if (!EnsureRespawnTag() || !WireFlyPrefab() || !WireStomp())
        {
            return false;
        }

        int spawns = TagSpawnPoints(scene, isOnline);
        if (spawns == 0)
        {
            return false;
        }
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"Wire Fly Lives: the fly has lives and {spawns} place(s) to come back at in {scene.name}, and the old man can stand on it.");
        return true;
    }

    // ---- the tag -------------------------------------------------------------------------------

    // FlyHealth finds its respawn points with FindGameObjectsWithTag, so the tag has to exist in the
    // project before anything can carry it - and setting an undefined tag on a GameObject throws.
    // Appended rather than inserted, so the tags already in the project keep their place.
    static bool EnsureRespawnTag()
    {
        UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(TagManagerPath);
        if (assets == null || assets.Length == 0)
        {
            Debug.LogError($"Wire Fly Lives: could not open {TagManagerPath}, so the '{FlyHealth.DefaultRespawnTag}' tag could not be checked.");
            return false;
        }

        var tagManager = new SerializedObject(assets[0]);
        SerializedProperty tags = tagManager.FindProperty("tags");
        if (tags == null)
        {
            Debug.LogError($"Wire Fly Lives: {TagManagerPath} has no 'tags' list, so the '{FlyHealth.DefaultRespawnTag}' tag could not be added. " +
                           "Add it by hand in Edit > Project Settings > Tags and Layers.");
            return false;
        }
        for (int i = 0; i < tags.arraySize; i++)
        {
            if (tags.GetArrayElementAtIndex(i).stringValue == FlyHealth.DefaultRespawnTag)
            {
                return true;
            }
        }
        tags.arraySize++;
        tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = FlyHealth.DefaultRespawnTag;
        tagManager.ApplyModifiedPropertiesWithoutUndo();
        AssetDatabase.SaveAssets();
        Debug.Log($"Wire Fly Lives: added the '{FlyHealth.DefaultRespawnTag}' tag to the project.");
        return true;
    }

    // ---- the fly -------------------------------------------------------------------------------

    // FlyHealth next to FlyController, where the body and everything death switches off live, and
    // FlyHealthNetwork on the root next to the other network behaviours, where Netcode wants it.
    static bool WireFlyPrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(FlyPrefabPath);
        try
        {
            FlyController fly = root.GetComponentInChildren<FlyController>(true);
            if (fly == null)
            {
                Debug.LogError($"Wire Fly Lives: {FlyPrefabPath} has no {nameof(FlyController)}; there is no fly to give the lives to.");
                return false;
            }
            Transform model = FindModelRoot(fly.transform);
            if (model == null)
            {
                Debug.LogError($"Wire Fly Lives: {FlyPrefabPath} has no '{FlyModelName}' under {fly.name} and no Animator either, so there is nothing to hide while the fly is dead.");
                return false;
            }

            FlyHealth health = EnsureComponent<FlyHealth>(fly.gameObject);
            var fields = new SerializedObject(health);
            SetReference(fields, "modelRoot", model);
            fields.ApplyModifiedPropertiesWithoutUndo();

            // Only wanted once the fly is a network player. Fails open: single player death and respawn
            // run off FlyHealth alone, so this is a warning rather than a stop.
            if (root.GetComponent<NetworkObject>() == null)
            {
                Debug.LogWarning($"Wire Fly Lives: {FlyPrefabPath} has no NetworkObject yet, so in a room the players would each count hits on their own copy. " +
                                 "Run Tools > Kitchen Online > Wire Network Players, then this again.");
            }
            else
            {
                EnsureComponent<FlyHealthNetwork>(root);
            }

            PrefabUtility.SaveAsPrefabAsset(root, FlyPrefabPath, out bool saved);
            if (!saved)
            {
                Debug.LogError($"Wire Fly Lives: could not save {FlyPrefabPath}.");
            }
            return saved;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // The model whose renderers are hidden while the fly is dead. By name first, because the .anim
    // clips address everything under it by path and that name is not allowed to change; by Animator as
    // a fallback, so a fly rebuilt from a new model still wires up.
    static Transform FindModelRoot(Transform body)
    {
        Transform named = body.Find(FlyModelName);
        if (named != null)
        {
            return named;
        }
        Animator animator = body.GetComponentInChildren<Animator>(true);
        return animator != null ? animator.transform : null;
    }

    // ---- the old man ---------------------------------------------------------------------------

    // OldPersonStomp on the old man's root, next to OldPersonFPSController and OldPersonSlap - which
    // means the Variant, and only the Variant. The base prefab is the bare FBX: no CharacterController
    // and no controller, so a stomp there would find nothing to measure a shoe from and switch itself
    // off, and the Variant would inherit that dead copy. Both kitchens place the Variant, so one
    // prefab covers single player and online alike.
    // Its serialized references are left empty on purpose: OldPersonStomp finds the controller, the
    // controls and the camera shake on its own object in Awake, and that object is this root. The
    // squish clip is left empty too - a stomp is silent until somebody draws one, which fails open.
    static bool WireStomp()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(HumanVariantPrefabPath);
        try
        {
            if (root.GetComponent<OldPersonStomp>() != null)
            {
                return true;
            }
            if (root.GetComponent<CharacterController>() == null)
            {
                // Fails open: the component goes on anyway so a controller added later finds it there,
                // but said out loud because until then the stomp disables itself at Awake.
                Debug.LogWarning($"Wire Fly Lives: {HumanVariantPrefabPath} has no CharacterController on its root, so {nameof(OldPersonStomp)} will switch itself off - " +
                                 "it measures the stomp from the sole of his shoe. Run Tools > Kitchen Online > Wire Network Players, then this again.");
            }
            root.AddComponent<OldPersonStomp>();

            PrefabUtility.SaveAsPrefabAsset(root, HumanVariantPrefabPath, out bool saved);
            if (!saved)
            {
                Debug.LogError($"Wire Fly Lives: could not save {HumanVariantPrefabPath}.");
                return false;
            }
            Debug.Log($"Wire Fly Lives: {nameof(OldPersonStomp)} added to {HumanVariantPrefabPath}.");
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // ---- where the fly comes back --------------------------------------------------------------

    // Everything in this kitchen a dead fly can reappear at, carrying the tag FlyHealth looks for.
    // In the online kitchen those are the player spawn points that are already there; in the single
    // player kitchen they are made here, because it has none. Returns how many the scene ended up with.
    static int TagSpawnPoints(Scene scene, bool isOnline)
    {
        List<Transform> spawns = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Where(IsSpawnPoint)
            .ToList();

        if (!isOnline)
        {
            spawns.AddRange(MakeSinglePlayerSpawns(scene));
        }
        else if (spawns.Count == 0)
        {
            Debug.LogError($"Wire Fly Lives: {scene.name} has nothing named '{ExistingSpawnPrefix}...' to tag, so a dead fly would have nowhere to come back to. " +
                           "Run Tools > Kitchen Online > Wire Network Players first - it needs the same objects.");
            return 0;
        }

        foreach (Transform spawn in spawns)
        {
            if (spawn.gameObject.CompareTag(FlyHealth.DefaultRespawnTag))
            {
                continue;
            }
            Undo.RecordObject(spawn.gameObject, "Wire Fly Lives");
            spawn.gameObject.tag = FlyHealth.DefaultRespawnTag;
        }
        return spawns.Count;
    }

    // The house's own markers are left out: they are the layout the spawn points are copied from, and
    // they go with every rebuild of KitchenMap, so tagging them would only count each spot twice.
    static bool IsSpawnPoint(Transform candidate)
    {
        if (IsHouseMarker(candidate))
        {
            return false;
        }
        return candidate.name.StartsWith(ExistingSpawnPrefix, StringComparison.Ordinal)
               || candidate.name.StartsWith(SpawnNamePrefix, StringComparison.Ordinal)
               || candidate.gameObject.CompareTag(FlyHealth.DefaultRespawnTag);
    }

    static bool IsHouseMarker(Transform candidate)
    {
        Transform group = candidate.parent;
        return group != null && group.name == MarkersGroupName && group.parent != null && group.parent.name == MapRootName;
    }

    // The single player kitchen's own spawn points, as empties under one group at the scene root rather
    // than under KitchenMap: Build Kitchen Map throws that whole root away and rebuilds it, and spawn
    // points that disappear with the house would take the respawn with them. Each is a copy of one
    // FlySpawn_ marker, placed in world space so the group's own transform does not matter.
    // Only the ones that are missing from the group are made: a spawn point moved somewhere better by
    // hand keeps its place however often this is run. HouseBuildPipeline empties the group before it
    // runs this, which is how a new layout replaces the old spawn points.
    static List<Transform> MakeSinglePlayerSpawns(Scene scene)
    {
        var made = new List<Transform>();
        GameObject groupObject = scene.GetRootGameObjects().FirstOrDefault(root => root.name == SpawnGroupName);
        if (groupObject == null)
        {
            groupObject = new GameObject(SpawnGroupName);
            Undo.RegisterCreatedObjectUndo(groupObject, "Wire Fly Lives");
        }

        List<Transform> markers = FindSpawnMarkers(scene);
        foreach (Transform marker in markers)
        {
            if (groupObject.transform.Find(marker.name) != null)
            {
                continue;
            }
            var spawnObject = new GameObject(marker.name);
            Undo.RegisterCreatedObjectUndo(spawnObject, "Wire Fly Lives");
            spawnObject.transform.SetParent(groupObject.transform, false);
            spawnObject.transform.SetPositionAndRotation(marker.position, marker.rotation);
            made.Add(spawnObject.transform);
        }
        if (markers.Count == 0 && groupObject.transform.childCount == 0)
        {
            // Fails closed (TagSpawnPoints then finds nothing): a fly that dies with nowhere to come back is
            // worse than a run that stops. The old hard-coded spots are gone because they belonged to the old house.
            Debug.LogError($"Wire Fly Lives: {scene.name} has no {MapRootName}/{MarkersGroupName}/{SpawnNamePrefix}* markers and nothing under '{SpawnGroupName}'. " +
                           "Build the house first (Tools > Stylized Kitchen > Build House (Both Scenes)).", groupObject);
        }
        if (made.Count > 0)
        {
            Debug.Log($"Wire Fly Lives: made {made.Count} fly spawn point(s) under '{SpawnGroupName}' in {scene.name}, one per {SpawnNamePrefix} marker in the house.", groupObject);
        }
        return made;
    }

    static List<Transform> FindSpawnMarkers(Scene scene)
    {
        GameObject map = scene.GetRootGameObjects().FirstOrDefault(root => root.name == MapRootName);
        Transform markers = map != null ? map.transform.Find(MarkersGroupName) : null;
        if (markers == null)
        {
            return new List<Transform>();
        }
        return markers.Cast<Transform>()
            .Where(marker => marker.name.StartsWith(SpawnNamePrefix, StringComparison.Ordinal))
            .OrderBy(marker => marker.name, StringComparer.Ordinal)
            .ToList();
    }

    // ---- small helpers -------------------------------------------------------------------------

    static T EnsureComponent<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : target.AddComponent<T>();
    }

    // Fails closed: a field renamed without this being updated stops the run loudly instead of leaving
    // a fly that dies without ever disappearing.
    static void SetReference(SerializedObject fields, string fieldName, UnityEngine.Object value)
    {
        SerializedProperty property = fields.FindProperty(fieldName);
        if (property == null)
        {
            throw new InvalidOperationException(
                $"Wire Fly Lives: {fields.targetObject.GetType().Name} has no serialized field '{fieldName}'. Update {nameof(FlyLifeWiring)} to match it.");
        }
        property.objectReferenceValue = value;
    }
}
