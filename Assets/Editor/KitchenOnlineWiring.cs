using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Turns the fly and the old man into network players and sets the online kitchen up to spawn them
// (Tools > Kitchen Online > Wire Network Players, with Kitchen_online open).
// A menu step rather than hand-edited prefab and scene files: Netcode generates ids when its
// components are added through the Editor, and the scene is usually open with unsaved changes.
// Running it again is safe: it reuses what it finds and rewrites the same settings.
public static class KitchenOnlineWiring
{
    const string FlyPrefabPath = "Assets/objectnetwork/fly.prefab";
    const string HumanPrefabPath = "Assets/objectnetwork/old_person Variant.prefab";
    const string PrefabListPath = "Assets/DefaultNetworkPrefabs.asset";
    const string SpawnerObjectName = "Kitchen_Spawner";
    // The spawn points as they are named in Kitchen_online. Every object whose name starts with the
    // fly prefix is a fly spawn, so adding "fly pawner _3" needs no change here.
    const string HumanSpawnName = "humanspaner";
    const string FlySpawnPrefix = "fly pawner";

    // Everything on a body that reads this machine's input or draws to this machine's screen.
    // Black, rotate, spitinterface and OldPersonSpitScreen are left out: they only animate the HUD, which the Canvas
    // entry already hides on other players' copies. OldPersonSlap and OldPersonHoldArm stay on, because
    // other players' copies replay the arm through them (OldPersonArmNetwork); neither reads input there.
    static readonly Type[] OwnerOnlyTypes =
    {
        typeof(FlyController), typeof(OldPersonFPSController), typeof(OrbitCamera), typeof(AimShootsystem),
        typeof(FlySpit), typeof(OldPersonPickup),
        typeof(HideCursorWhileEnabled), typeof(CameraShake), typeof(Camera), typeof(AudioListener), typeof(Canvas),
    };

    [MenuItem("Tools/Kitchen Online/Wire Network Players")]
    static void Wire()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != GameSceneManager.KitchenOnlineScene)
        {
            Debug.LogError($"Wire Network Players: open {GameSceneManager.KitchenOnlineScene} first (the open scene is '{scene.name}').");
            return;
        }

        bool prefabsWired = WirePrefab(FlyPrefabPath, typeof(FlyController), typeof(FlySpitNetwork), typeof(FlyKnockbackNetwork))
                            && WirePrefab(HumanPrefabPath, typeof(OldPersonFPSController), typeof(OldPersonArmNetwork), typeof(OldPersonSpitNetwork))
                            && RegisterPrefabs();
        if (!prefabsWired)
        {
            return;
        }
        RemovePlacedBodies(scene);
        if (!WireSpawner(scene, WireProps(scene)))
        {
            return;
        }
        DisableSwapping(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"Wire Network Players: done. Save {scene.name} (Ctrl+S) and make sure it is in File > Build Profiles > Scene List.");
    }

    // NetworkObject on the root, NetworkTransform on the object the controller moves (the fly's
    // controller sits on a child), NetworkAnimator on the model's Animator, OwnerOnlyControls, and
    // the scripts that pass this body's actions (and, for the fly, the hits it takes) between players.
    static bool WirePrefab(string path, Type controllerType, params Type[] actionsNetworkTypes)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            Component controller = root.GetComponentInChildren(controllerType, true);
            if (controller == null)
            {
                Debug.LogError($"Wire Network Players: {path} has no {controllerType.Name}; nothing to make playable.");
                return false;
            }

            EnsureComponent<NetworkObject>(root);
            WireTransform(EnsureComponent<NetworkTransform>(controller.gameObject), controller.gameObject != root);
            WireAnimator(root, controller, path);
            FillOwnerOnly(EnsureComponent<OwnerOnlyControls>(root));
            foreach (Type actionsNetworkType in actionsNetworkTypes)
            {
                if (root.GetComponent(actionsNetworkType) == null)
                {
                    root.AddComponent(actionsNetworkType);
                }
            }

            PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
            if (!saved)
            {
                Debug.LogError($"Wire Network Players: could not save {path}.");
            }
            return saved;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static void WireTransform(NetworkTransform networkTransform, bool isChild)
    {
        // The owner moves the body with its own controller, so the owner sends the position; the
        // default (server) would snap every client's body back to where the host last saw it.
        networkTransform.AuthorityMode = NetworkTransform.AuthorityModes.Owner;
        // A child moves relative to the prefab root, which stays where it was spawned.
        networkTransform.InLocalSpace = isChild;
        // Nothing rescales a body at runtime.
        networkTransform.SyncScaleX = false;
        networkTransform.SyncScaleY = false;
        networkTransform.SyncScaleZ = false;
    }

    static void WireAnimator(GameObject root, Component controller, string path)
    {
        Animator animator = controller.GetComponentInChildren<Animator>(true);
        if (animator == null)
        {
            animator = root.GetComponentInChildren<Animator>(true);
        }
        if (animator == null)
        {
            // Fails open: other players still see the body move, just without its animation.
            Debug.LogWarning($"Wire Network Players: {path} has no Animator, so its animation is not sent to other players.");
            return;
        }
        NetworkAnimator networkAnimator = EnsureComponent<NetworkAnimator>(animator.gameObject);
        networkAnimator.Animator = animator;
        // The owner's controller sets the parameters, so the owner sends them.
        networkAnimator.AuthorityMode = NetworkAnimator.AuthorityModes.Owner;
    }

    static void FillOwnerOnly(OwnerOnlyControls ownerControls)
    {
        List<Behaviour> ownerOnly = ownerControls.GetComponentsInChildren<Behaviour>(true)
            .Where(behaviour => OwnerOnlyTypes.Any(type => type.IsInstanceOfType(behaviour)))
            .ToList();

        SerializedObject serialized = new SerializedObject(ownerControls);
        SerializedProperty list = serialized.FindProperty("ownerOnly");
        list.arraySize = ownerOnly.Count;
        for (int i = 0; i < ownerOnly.Count; i++)
        {
            list.GetArrayElementAtIndex(i).objectReferenceValue = ownerOnly[i];
        }
        serialized.ApplyModifiedPropertiesWithoutUndo();
    }

    // Netcode only spawns prefabs it was told about, on the host and on every client alike.
    static bool RegisterPrefabs()
    {
        NetworkPrefabsList prefabList = AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>(PrefabListPath);
        if (prefabList == null)
        {
            Debug.LogError($"Wire Network Players: no NetworkPrefabsList at {PrefabListPath}.");
            return false;
        }
        foreach (string path in new[] { FlyPrefabPath, HumanPrefabPath })
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (!prefabList.Contains(prefab))
            {
                prefabList.Add(new NetworkPrefab { Prefab = prefab });
            }
        }
        EditorUtility.SetDirty(prefabList);
        AssetDatabase.SaveAssets();
        return true;
    }

    // Every free physics object in the kitchen: owner-driven NetworkTransform plus NetworkRigidbody,
    // so the human player (who owns them all, see KitchenSpawner) simulates them and everyone else
    // sees them move. Runs after the placed bodies are gone, so nothing of theirs is picked up.
    static List<NetworkObject> WireProps(Scene scene)
    {
        List<NetworkObject> props = new List<NetworkObject>();
        IEnumerable<Rigidbody> bodies = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Rigidbody>(true))
            .Where(body => !body.isKinematic);
        foreach (Rigidbody body in bodies.ToList())
        {
            NetworkObject parentObject = body.transform.parent != null ? body.transform.parent.GetComponentInParent<NetworkObject>(true) : null;
            if (parentObject != null)
            {
                // Fails open: left as a local object rather than nesting one network object in another.
                Debug.LogWarning($"Wire Network Players: skipped '{body.name}', it sits under the network object '{parentObject.name}'.", body);
                continue;
            }
            props.Add(WireProp(body.gameObject));
        }
        Debug.Log($"Wire Network Players: {props.Count} kitchen objects are networked.");
        return props;
    }

    static NetworkObject WireProp(GameObject prop)
    {
        NetworkObject networkObject = EnsureSceneComponent<NetworkObject>(prop);
        NetworkTransform networkTransform = EnsureSceneComponent<NetworkTransform>(prop);
        Undo.RecordObject(networkTransform, "Wire Network Players");
        WireTransform(networkTransform, false);
        EnsureSceneComponent<NetworkRigidbody>(prop);
        return networkObject;
    }

    // Swaping toggles object sets with the C key on one machine only, which online would leave every
    // player looking at a different kitchen.
    static void DisableSwapping(Scene scene)
    {
        foreach (Swaping swapping in scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Swaping>(true)))
        {
            Undo.RecordObject(swapping, "Wire Network Players");
            swapping.enabled = false;
            Debug.Log($"Wire Network Players: turned off Swaping on '{swapping.name}'.", swapping);
        }
    }

    static bool WireSpawner(Scene scene, List<NetworkObject> props)
    {
        List<Transform> sceneTransforms = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .ToList();
        Transform humanSpawn = sceneTransforms.FirstOrDefault(t => t.name == HumanSpawnName);
        Transform[] flySpawns = sceneTransforms
            .Where(t => t.name.StartsWith(FlySpawnPrefix, StringComparison.Ordinal))
            .OrderBy(t => t.name, StringComparer.Ordinal)
            .ToArray();
        if (humanSpawn == null || flySpawns.Length == 0)
        {
            Debug.LogError($"Wire Network Players: {scene.name} needs an object named '{HumanSpawnName}' and at least one whose name starts with '{FlySpawnPrefix}'.");
            return false;
        }

        GameObject spawnerObject = scene.GetRootGameObjects().FirstOrDefault(root => root.name == SpawnerObjectName);
        if (spawnerObject == null)
        {
            spawnerObject = new GameObject(SpawnerObjectName);
            Undo.RegisterCreatedObjectUndo(spawnerObject, "Wire Network Players");
        }
        KitchenSpawner spawner = spawnerObject.GetComponent<KitchenSpawner>();
        if (spawner == null)
        {
            spawner = Undo.AddComponent<KitchenSpawner>(spawnerObject);
        }

        SerializedObject serialized = new SerializedObject(spawner);
        serialized.FindProperty("humanPrefab").objectReferenceValue = LoadNetworkObject(HumanPrefabPath);
        serialized.FindProperty("flyPrefab").objectReferenceValue = LoadNetworkObject(FlyPrefabPath);
        serialized.FindProperty("humanSpawn").objectReferenceValue = humanSpawn;
        SerializedProperty flySpawnList = serialized.FindProperty("flySpawns");
        flySpawnList.arraySize = flySpawns.Length;
        for (int i = 0; i < flySpawns.Length; i++)
        {
            flySpawnList.GetArrayElementAtIndex(i).objectReferenceValue = flySpawns[i];
        }
        SerializedProperty propList = serialized.FindProperty("props");
        propList.arraySize = props.Count;
        for (int i = 0; i < props.Count; i++)
        {
            propList.GetArrayElementAtIndex(i).objectReferenceValue = props[i];
        }
        serialized.ApplyModifiedProperties();
        return true;
    }

    // The bodies placed by hand would be a second, unowned fly and old man standing in everyone's
    // kitchen next to the spawned ones.
    static void RemovePlacedBodies(Scene scene)
    {
        List<GameObject> placed = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Select(t => t.gameObject)
            .Where(IsPlacedBody)
            .ToList();
        foreach (GameObject body in placed)
        {
            Debug.Log($"Wire Network Players: removed the placed '{body.name}'; it is spawned per player now.");
            Undo.DestroyObjectImmediate(body);
        }
    }

    static bool IsPlacedBody(GameObject candidate)
    {
        if (!PrefabUtility.IsOutermostPrefabInstanceRoot(candidate))
        {
            return false;
        }
        string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(candidate);
        return path == FlyPrefabPath || path == HumanPrefabPath;
    }

    static NetworkObject LoadNetworkObject(string path)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        return prefab != null ? prefab.GetComponent<NetworkObject>() : null;
    }

    static T EnsureSceneComponent<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : Undo.AddComponent<T>(target);
    }

    static T EnsureComponent<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : target.AddComponent<T>();
    }
}
