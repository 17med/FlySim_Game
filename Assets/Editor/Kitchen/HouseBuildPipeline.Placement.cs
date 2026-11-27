using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// The throwables, the dishes and the spawn points: what HouseBuildPipeline moves and wires in a freshly
// built house before the existing wiring steps run. See HouseBuildPipeline.cs.
public static partial class HouseBuildPipeline
{
    const string ThrowablesGroupName = "Throwables";
    // OldPersonPickup picks up anything with this tag (or a Pickable component).
    const string PickableTag = "Pickable";

    // The second couscous, a copy of the first that stands in the pantry.
    const string PantryDishName = "Couscous_Pantry";
    // Every dish eats at the same rate (see FoodDish's tooltip and CoffeeCupSetup.MugBiteRate). The
    // guard is measured flat in dish radii, and 6 is standing at the table, not across the room.
    const float DishBiteRate = 1f;
    const float DishBiteRangeScale = 1.3f;
    const float DishGuardRangeScale = 6f;

    // The spawn points as KitchenOnlineWiring finds them in Kitchen_online (its HumanSpawnName and
    // FlySpawnPrefix, private to that file); the names are in the scene, so these have to agree.
    const string OnlineHumanSpawnName = "humanspaner";
    const string OnlineFlySpawnPrefix = "fly pawner";
    // The single player kitchen: the old man's props and the fly's respawn group (FlyLifeWiring's).
    const string OfflineHumanPropsName = "just obj";
    const string OfflineFlySpawnGroupName = "FlySpawns";

    // ---- what the old man can throw ------------------------------------------------------------

    // Every direct child of every Throwables group falls, is thrown at the house's gravity and can be
    // picked up. KitchenOnlineWiring later networks every loose Rigidbody, so these are what it finds.
    static int WireThrowables(Transform map)
    {
        List<Transform> groups = map.GetComponentsInChildren<Transform>(true).Where(t => t.name == ThrowablesGroupName).ToList();
        int wired = 0;
        foreach (Transform group in groups)
        {
            foreach (Transform prop in group)
            {
                wired += WireThrowable(prop.gameObject) ? 1 : 0;
            }
        }
        return wired;
    }

    static bool WireThrowable(GameObject prop)
    {
        if (prop.GetComponentInChildren<Collider>(true) == null)
        {
            // Fails closed for this prop: a body with nothing to collide with drops through the floor the
            // moment play starts, so it is left standing as a static ornament instead.
            Debug.LogWarning($"Build House: '{prop.name}' has no collider, so it was left static rather than made throwable.", prop);
            return false;
        }
        foreach (MeshCollider mesh in prop.GetComponentsInChildren<MeshCollider>(true).Where(mesh => !mesh.convex))
        {
            // PhysX does not move a non-convex mesh; the builder gives props without a UCX hull an exact one.
            Undo.RecordObject(mesh, UndoName);
            mesh.convex = true;
        }
        Rigidbody body = EnsureComponent<Rigidbody>(prop);
        Undo.RecordObject(body, UndoName);
        body.isKinematic = false;
        body.useGravity = true;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        // ScaledGravity sets this again at Awake; set here too so the scene says what will happen.
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        // The house's scale is the gravity multiplier, as KitchenHouseScaler sets it.
        var gravity = new SerializedObject(EnsureComponent<ScaledGravity>(prop));
        SetFloat(gravity, "multiplier", KitchenMapBuilder.WorldScale);
        gravity.ApplyModifiedProperties();
        Undo.RecordObject(prop, UndoName);
        prop.tag = PickableTag;
        return true;
    }

    // ---- the dishes ----------------------------------------------------------------------------

    // The couscous already in the scene goes to the kitchen island; a copy of it goes to the pantry.
    // Fails closed: a kitchen with no couscous to place is not one the match can be played in.
    static bool PlaceDishes(Scene scene, HouseMarkers markers)
    {
        GameObject couscous = scene.GetRootGameObjects()
            .FirstOrDefault(root => root.name != PantryDishName && root.GetComponent<CouscousDishController>() != null);
        if (couscous == null)
        {
            Debug.LogError($"Build House: {scene.name} has no couscous (a root object with {nameof(CouscousDishController)}) to stand on the kitchen island.");
            return false;
        }
        GameObject pantry = EnsurePantryCopy(scene, couscous);
        if (!StandOnSpot(couscous, markers.KitchenDish) || !StandOnSpot(pantry, markers.PantryDish))
        {
            return false;
        }
        SetDishValues(couscous);
        SetDishValues(pantry);
        return true;
    }

    // Reused when it is already there, so in the online kitchen its NetworkObject keeps its id from
    // one build to the next. A fresh copy loses the network components it was copied with: an id
    // duplicated from the original would make Netcode see two objects as one, and Wire Match adds new
    // ones through the Editor, which is what gives the copy an id of its own.
    static GameObject EnsurePantryCopy(Scene scene, GameObject source)
    {
        GameObject existing = scene.GetRootGameObjects().FirstOrDefault(root => root.name == PantryDishName);
        if (existing != null)
        {
            return existing;
        }
        GameObject copy = UnityEngine.Object.Instantiate(source);
        copy.name = PantryDishName;
        Undo.RegisterCreatedObjectUndo(copy, UndoName);
        // The behaviours first: Unity refuses to remove a component another one on the object needs.
        foreach (NetworkBehaviour behaviour in copy.GetComponentsInChildren<NetworkBehaviour>(true))
        {
            UnityEngine.Object.DestroyImmediate(behaviour);
        }
        foreach (NetworkObject networkObject in copy.GetComponentsInChildren<NetworkObject>(true))
        {
            UnityEngine.Object.DestroyImmediate(networkObject);
        }
        return copy;
    }

    // The bottom of the dish's meshes on the spot's height and their middle over the spot, because a
    // model's pivot is not reliably at its base or its centre.
    static bool StandOnSpot(GameObject dish, Transform spot)
    {
        if (!TryMeshBounds(dish, out Bounds bounds))
        {
            Debug.LogError($"Build House: '{dish.name}' has no renderers, so it cannot be stood on '{spot.name}'.", dish);
            return false;
        }
        Undo.RecordObject(dish.transform, UndoName);
        Vector3 spotPosition = spot.position;
        dish.transform.position += new Vector3(spotPosition.x - bounds.center.x, spotPosition.y - bounds.min.y, spotPosition.z - bounds.center.z);
        return true;
    }

    // Added here if missing rather than left to Wire Match, which would add it with its defaults.
    static void SetDishValues(GameObject dish)
    {
        var fields = new SerializedObject(EnsureComponent<FoodDish>(dish));
        SetFloat(fields, "biteRate", DishBiteRate);
        SetFloat(fields, "biteRangeScale", DishBiteRangeScale);
        SetFloat(fields, "guardRangeScale", DishGuardRangeScale);
        fields.ApplyModifiedProperties();
    }

    // ---- the spawn points ----------------------------------------------------------------------

    // The online kitchen: the human spawn and exactly four fly spawns, made as empty roots if missing.
    static bool PlaceOnlineSpawns(Scene scene, HouseMarkers markers)
    {
        MoveTo(EnsureRootObject(scene, OnlineHumanSpawnName), markers.Human);
        string[] flyNames = Enumerable.Range(1, FlySpawnCount)
            .Select(i => i == 1 ? OnlineFlySpawnPrefix : $"{OnlineFlySpawnPrefix} _{i}")
            .ToArray();
        RemoveExtraFlySpawns(scene, flyNames);
        for (int i = 0; i < flyNames.Length; i++)
        {
            MoveTo(EnsureRootObject(scene, flyNames[i]), markers.Flies[i]);
        }
        return true;
    }

    // KitchenOnlineWiring takes every object whose name starts with the prefix as a fly spawn, so one
    // left over from an old layout would spawn a fly in it.
    static void RemoveExtraFlySpawns(Scene scene, string[] wanted)
    {
        var kept = new HashSet<string>();
        List<Transform> candidates = scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
            .Where(t => t.name.StartsWith(OnlineFlySpawnPrefix, StringComparison.Ordinal))
            .ToList();
        foreach (Transform candidate in candidates)
        {
            // Only a root one is kept, because EnsureRootObject only finds roots.
            if (candidate.parent == null && wanted.Contains(candidate.name) && kept.Add(candidate.name))
            {
                continue;
            }
            if (candidate.childCount > 0 || candidate.GetComponents<Component>().Length > 1)
            {
                // Fails open: it is more than an empty spawn point, so it is reported rather than deleted.
                Debug.LogWarning($"Build House: '{candidate.name}' looks like a fly spawn but is not one of the four and is not empty; rename or remove it by hand.", candidate);
                continue;
            }
            Undo.DestroyObjectImmediate(candidate.gameObject);
        }
    }

    // The single player kitchen: the old man and his props at the human spawn, the fly at the first
    // fly spawn, and the respawn group emptied so Wire Fly Lives remakes it from the markers.
    static bool PlaceOfflineSpawns(Scene scene, HouseMarkers markers)
    {
        GameObject[] roots = scene.GetRootGameObjects();
        // Active ones only: the inactive "just obj" may carry a copy of the old man's controller too.
        GameObject human = roots.FirstOrDefault(root => root.activeSelf && root.GetComponentInChildren<OldPersonFPSController>(true) != null);
        GameObject fly = roots.FirstOrDefault(root => root.activeSelf && root.GetComponentInChildren<FlyController>(true) != null);
        if (human == null || fly == null)
        {
            Debug.LogError($"Build House: {scene.name} needs the old man ({nameof(OldPersonFPSController)}) and the fly ({nameof(FlyController)}) as active root objects.");
            return false;
        }
        MoveTo(human.transform, markers.Human);
        MoveTo(fly.transform, markers.Flies[0]);
        GameObject humanProps = roots.FirstOrDefault(root => root.name == OfflineHumanPropsName);
        if (humanProps != null)
        {
            MoveTo(humanProps.transform, markers.Human);
        }
        GameObject respawnGroup = roots.FirstOrDefault(root => root.name == OfflineFlySpawnGroupName);
        if (respawnGroup != null)
        {
            foreach (Transform old in respawnGroup.transform.Cast<Transform>().ToList())
            {
                Undo.DestroyObjectImmediate(old.gameObject);
            }
        }
        return true;
    }

    // Position and compass yaw only: a spawn point tilted with the marker would spawn a tilted player.
    static void MoveTo(Transform target, Transform marker)
    {
        Undo.RecordObject(target, UndoName);
        target.SetPositionAndRotation(marker.position, Quaternion.Euler(0f, marker.eulerAngles.y, 0f));
    }

    static Transform EnsureRootObject(Scene scene, string name)
    {
        GameObject existing = scene.GetRootGameObjects().FirstOrDefault(root => root.name == name);
        if (existing != null)
        {
            return existing.transform;
        }
        var created = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(created, UndoName);
        return created.transform;
    }
}
