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
// single player kitchen has none, so a handful of empties are made in the air around the two rooms.
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

    // Where a fly can appear in the single player kitchen, in the layout metres KitchenMapBuilder
    // builds the house in, with the compass yaw it faces (0 = north). All of them are between 1.4 m and
    // 1.7 m up: above the counters, the tables and the bookshelf, below the 2.6 m ceiling and the wall
    // cabinets' 2.15 m top, and clear of the pendant and ceiling lamps. Multiplied by WorldScale on the
    // way into the scene, which is the whole reason they are written as metres.
    static readonly (Vector3 Spot, float Facing)[] SinglePlayerSpawns =
    {
        // Beside the dining table, facing the counters and the window: where the fly already starts.
        (new Vector3(-0.4f, 1.4f, -0.6f), 0f),
        // Over the prep table by the doorway, looking west across the kitchen.
        (new Vector3(1.6f, 1.5f, -0.4f), -90f),
        // The kitchen's west half, out from the microwave and the upper cabinets, looking at the stove.
        (new Vector3(-1.8f, 1.7f, 0.9f), 180f),
        // Straight over the coffee table and the mug on it, looking at the TV.
        (new Vector3(6.1f, 1.6f, 0.35f), 0f),
        // By the living room window, looking back in at the armchairs.
        (new Vector3(8.3f, 1.5f, 1.2f), 180f),
        // Just inside the living room through the doorway, looking east.
        (new Vector3(4.2f, 1.6f, 1.3f), 90f),
    };

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
            spawns.AddRange(MakeSinglePlayerSpawns(scene, spawns));
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

    static bool IsSpawnPoint(Transform candidate)
    {
        return candidate.name.StartsWith(ExistingSpawnPrefix, StringComparison.Ordinal)
               || candidate.name.StartsWith(SpawnNamePrefix, StringComparison.Ordinal)
               || candidate.gameObject.CompareTag(FlyHealth.DefaultRespawnTag);
    }

    // The single player kitchen's own spawn points, as empties under one group at the scene root rather
    // than under KitchenMap: Build Kitchen Map throws that whole root away and rebuilds it, and spawn
    // points that disappear with the house would take the respawn with them. At the root the group's
    // scale is 1, so the layout metres above are multiplied by WorldScale here.
    // Only the ones that are missing are made: a spawn point moved somewhere better by hand keeps its
    // place however often this is run.
    static List<Transform> MakeSinglePlayerSpawns(Scene scene, List<Transform> existing)
    {
        var made = new List<Transform>();
        GameObject groupObject = scene.GetRootGameObjects().FirstOrDefault(root => root.name == SpawnGroupName);
        if (groupObject == null)
        {
            groupObject = new GameObject(SpawnGroupName);
            Undo.RegisterCreatedObjectUndo(groupObject, "Wire Fly Lives");
        }

        for (int i = 0; i < SinglePlayerSpawns.Length; i++)
        {
            string name = SpawnNamePrefix + (i + 1);
            if (existing.Any(spawn => spawn.name == name))
            {
                continue;
            }
            (Vector3 spot, float facing) = SinglePlayerSpawns[i];
            var spawnObject = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(spawnObject, "Wire Fly Lives");
            spawnObject.transform.SetParent(groupObject.transform, false);
            spawnObject.transform.localPosition = spot * KitchenMapBuilder.WorldScale;
            spawnObject.transform.localRotation = Quaternion.Euler(0f, facing, 0f);
            made.Add(spawnObject.transform);
        }
        if (made.Count > 0)
        {
            Debug.Log($"Wire Fly Lives: made {made.Count} fly spawn point(s) under '{SpawnGroupName}' in {scene.name}, in the air around the kitchen and the living room.", groupObject);
        }
        return made;
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
