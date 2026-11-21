using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Puts the raid into the kitchen (Tools > Match > Wire Match): the fly gets its eating, the couscous
// becomes food that can be eaten and run out, and the online kitchen gets the Match object that keeps
// one clock and one verdict for the whole room.
// A menu step rather than hand-edited scene files because of that last part: Netcode works out a
// NetworkObject's scene id when the component is added through the Editor, so a Match object written
// into the YAML by hand would never spawn.
// Run it with Kitchen open, then again with Kitchen_online open. Running it again is safe: it reuses
// the objects and components it made before.
public static class MatchWiring
{
    const string FlyPrefabPath = "Assets/objectnetwork/fly.prefab";
    const string EatClipFolder = "Assets/audio/SFX/Fly/eat_sound/";
    const string MatchObjectName = "Match";
    const string EatSourceName = "SFX_Eat";

    static readonly string[] EatClips = { "eat.wav", "eat_2.wav", "eat_3.wav" };

    [MenuItem("Tools/Match/Wire Match")]
    static void WireOpenScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        bool isKitchen = scene.name == GameSceneManager.KitchenScene;
        if (!isKitchen && scene.name != GameSceneManager.KitchenOnlineScene)
        {
            Debug.LogError($"Wire Match: open {GameSceneManager.KitchenScene} or {GameSceneManager.KitchenOnlineScene} first (the open scene is '{scene.name}').");
            return;
        }
        if (!WireScene(scene))
        {
            return;
        }
        Debug.Log($"Wire Match: done. Save {scene.name} (Ctrl+S)." +
                  (isKitchen ? $" Now open {GameSceneManager.KitchenOnlineScene} and run it again." : ""));
    }

    // The whole of one kitchen: the fly prefab, the dishes in this scene, and in the online kitchen the
    // Match object. Marks the scene dirty but does not save it, so a hand run can be looked over first.
    // Fails closed: anything missing stops the run before the scene is touched further.
    internal static bool WireScene(Scene scene)
    {
        bool isOnline = scene.name == GameSceneManager.KitchenOnlineScene;

        // A half-wired fly would fly into a kitchen it cannot eat.
        if (!WireFlyPrefab())
        {
            return false;
        }
        int dishes = WireDishes(scene, isOnline);
        if (dishes == 0)
        {
            Debug.LogError($"Wire Match: no {nameof(CouscousDishController)} in {scene.name}, so there is no food to raid. The couscous needs to be in the scene with its controller on it.");
            return false;
        }
        if (isOnline && !WireMatchObject(scene))
        {
            return false;
        }

        EditorSceneManager.MarkSceneDirty(scene);
        string room = isOnline ? $", and {MatchObjectName} keeps the room's clock" : "";
        Debug.Log($"Wire Match: the fly can eat and {dishes} dish(es) in {scene.name} can be eaten{room}.");
        return true;
    }

    // ---- the fly -------------------------------------------------------------------------------

    // FlyEating next to FlyController with the chewing clips, and FlyEatNetwork on the root so the
    // other players see a fly with its head in the food.
    static bool WireFlyPrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(FlyPrefabPath);
        try
        {
            FlyController fly = root.GetComponentInChildren<FlyController>(true);
            if (fly == null)
            {
                Debug.LogError($"Wire Match: {FlyPrefabPath} has no {nameof(FlyController)}; there is nothing to give the eating to.");
                return false;
            }

            AudioClip[] clips = LoadEatClips();
            if (clips == null)
            {
                return false;
            }

            FlyEating eating = EnsureComponent<FlyEating>(fly.gameObject);
            var fields = new SerializedObject(eating);
            SetClips(fields.FindProperty("eatSounds"), clips);
            // Positional or flat is decided at runtime, by whose copy of the fly this is.
            fields.FindProperty("eatSource").objectReferenceValue = EnsureEatSource(fly.transform);
            fields.ApplyModifiedPropertiesWithoutUndo();

            // Only wanted once the fly is a network player. Fails open: the fly still eats for its own
            // player, so this is a warning rather than a stop.
            if (root.GetComponent<NetworkObject>() == null)
            {
                Debug.LogWarning($"Wire Match: {FlyPrefabPath} has no NetworkObject yet, so other players will not see this fly eat. " +
                                 "Run Tools > Kitchen Online > Wire Network Players, then this again.");
            }
            else
            {
                EnsureComponent<FlyEatNetwork>(root);
            }

            PrefabUtility.SaveAsPrefabAsset(root, FlyPrefabPath, out bool saved);
            if (!saved)
            {
                Debug.LogError($"Wire Match: could not save {FlyPrefabPath}.");
            }
            return saved;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static AudioSource EnsureEatSource(Transform parent)
    {
        Transform child = parent.Find(EatSourceName);
        if (child == null)
        {
            child = new GameObject(EatSourceName).transform;
            child.SetParent(parent, false);
        }
        AudioSource source = EnsureComponent<AudioSource>(child.gameObject);
        source.playOnAwake = false;
        source.loop = false;
        return source;
    }

    // ---- the food ------------------------------------------------------------------------------

    // Every couscous in the scene becomes a FoodDish. In the online kitchen it also gets the
    // NetworkObject and FoodDishNetwork that make the server's plate the one everybody sees.
    static int WireDishes(Scene scene, bool isOnline)
    {
        int count = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (CouscousDishController controller in root.GetComponentsInChildren<CouscousDishController>(true))
            {
                GameObject dish = controller.gameObject;
                EnsureComponentWithUndo<FoodDish>(dish);
                if (isOnline)
                {
                    // The NetworkObject first: FoodDishNetwork requires one, and adding it afterwards
                    // would let Unity add a second.
                    EnsureComponentWithUndo<NetworkObject>(dish);
                    EnsureComponentWithUndo<FoodDishNetwork>(dish);
                }
                count++;
            }
        }
        return count;
    }

    // ---- the room's clock ----------------------------------------------------------------------

    static bool WireMatchObject(Scene scene)
    {
        if (Object.FindAnyObjectByType<MatchNetwork>(FindObjectsInactive.Include) != null)
        {
            return true;
        }
        NetworkManager network = Object.FindAnyObjectByType<NetworkManager>(FindObjectsInactive.Include);
        if (network == null && NetworkManager.Singleton == null)
        {
            // Not fatal: the room's NetworkManager comes from the menu scene, which is not open. Said
            // out loud only because a missing one is also what a broken setup looks like.
            Debug.Log($"Wire Match: no NetworkManager in {scene.name}; that is expected, the room brings its own from {GameSceneManager.MainMenuScene}.");
        }

        var matchObject = new GameObject(MatchObjectName);
        Undo.RegisterCreatedObjectUndo(matchObject, "Wire Match");
        Undo.AddComponent<NetworkObject>(matchObject);
        Undo.AddComponent<MatchNetwork>(matchObject);
        return true;
    }

    // ---- small helpers -------------------------------------------------------------------------

    static T EnsureComponent<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : target.AddComponent<T>();
    }

    static T EnsureComponentWithUndo<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : Undo.AddComponent<T>(target);
    }

    static void SetClips(SerializedProperty list, AudioClip[] clips)
    {
        list.arraySize = clips.Length;
        for (int i = 0; i < clips.Length; i++)
        {
            list.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
        }
    }

    static AudioClip[] LoadEatClips()
    {
        var clips = new AudioClip[EatClips.Length];
        for (int i = 0; i < EatClips.Length; i++)
        {
            string path = EatClipFolder + EatClips[i];
            clips[i] = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (clips[i] == null)
            {
                Debug.LogError($"Wire Match: no audio clip at {path}. Was it moved or renamed?");
                return null;
            }
        }
        return clips;
    }
}
