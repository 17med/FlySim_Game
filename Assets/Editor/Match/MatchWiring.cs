using TMPro;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Puts the raid into the kitchen (Tools > Match > Wire Match): the fly gets its eating, every vessel
// holding food becomes food that can be eaten and run out, and the online kitchen gets the Match
// object that keeps one clock and one verdict for the whole room. Both kitchens get the HUD (timer,
// food bar, result screen) as scene UI if they have none yet; Tools > Match > Add HUD To Open Scene
// does only that.
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
    const string HudObjectName = "Match_HUD";
    const int HudSortingOrder = 850;

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
            Debug.LogError($"Wire Match: nothing in {scene.name} implements {nameof(IDishFill)}, so there is no food to raid. " +
                           $"The couscous needs to be in the scene with its {nameof(CouscousDishController)} on it, and the coffee mug with its {nameof(CoffeeCup)} " +
                           "(Tools > Match > Place Coffee In Living Room puts one in the living room).");
            return false;
        }
        if (isOnline && !WireMatchObject(scene))
        {
            return false;
        }
        WireHud();

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

    // Everything in the scene that holds food becomes a FoodDish: the couscous through its
    // CouscousDishController, the living room's mug through its CoffeeCup, and anything else somebody
    // writes an IDishFill for. Looked up by the interface rather than by either component, so a new
    // kind of food is wired by this step without it being edited. In the online kitchen each one also
    // gets the NetworkObject and FoodDishNetwork that make the server's plate the one everybody sees.
    static int WireDishes(Scene scene, bool isOnline)
    {
        int count = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            // GetComponentsInChildren takes an interface, which is the whole reason FoodDish holds one.
            foreach (IDishFill food in root.GetComponentsInChildren<IDishFill>(true))
            {
                // The scene was asked for an interface, so the cast back is made honestly: an
                // IDishFill that is not a Component has no GameObject to put a FoodDish on.
                Component holder = food as Component;
                if (holder == null)
                {
                    continue;
                }
                GameObject dish = holder.gameObject;
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

    // ---- the HUD -------------------------------------------------------------------------------

    // Puts the timer, food bar and result screen into the open kitchen as ordinary scene UI, laid
    // out the way the old code-built HUD was. Made once: after that the layout is the scene's, moved
    // and resized by hand, and this never touches a HUD that is already there.
    [MenuItem("Tools/Match/Add HUD To Open Scene")]
    static void AddHudToOpenScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != GameSceneManager.KitchenScene && scene.name != GameSceneManager.KitchenOnlineScene)
        {
            Debug.LogError($"Add HUD: open {GameSceneManager.KitchenScene} or {GameSceneManager.KitchenOnlineScene} first (the open scene is '{scene.name}').");
            return;
        }
        MatchHud hud = WireHud();
        Selection.activeGameObject = hud.gameObject;
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"Add HUD: {HudObjectName} is in {scene.name}. Move its children where you want them, then save (Ctrl+S).", hud);
    }

    static MatchHud WireHud()
    {
        MatchHud existing = Object.FindAnyObjectByType<MatchHud>(FindObjectsInactive.Include);
        if (existing != null)
        {
            Debug.Log($"Wire Match: {existing.name} is already in the scene; its layout was left as it is.", existing);
            return existing;
        }

        var canvasObject = new GameObject(HudObjectName, typeof(RectTransform));
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        // Below the pause menu (900), which should cover the HUD when it opens.
        canvas.sortingOrder = HudSortingOrder;
        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        // No GraphicRaycaster: nothing on the HUD is clickable, and one more raycaster would take
        // clicks away from the pause menu underneath it.

        MatchHud hud = canvasObject.AddComponent<MatchHud>();
        var fields = new SerializedObject(hud);
        BuildHudTop(canvasObject.transform, fields);
        BuildHudResult(canvasObject.transform, fields);
        fields.ApplyModifiedPropertiesWithoutUndo();

        Undo.RegisterCreatedObjectUndo(canvasObject, "Add Match HUD");
        return hud;
    }

    static void BuildHudTop(Transform root, SerializedObject fields)
    {
        TMP_Text clock = NewLabel("Timer", root, "05:00", 64f, new Color(0.95f, 0.95f, 0.97f, 1f));
        TopCentre(clock.rectTransform, 0f, 150f, 74f);
        SetReference(fields, "clockText", clock);

        Image track = NewImage("Food_Bar", root, new Color(0f, 0f, 0f, 0.45f));
        TopCentre(track.rectTransform, -92f, 420f, 18f);
        Image fill = NewImage("Food_Bar_Fill", track.transform, new Color(0.98f, 0.76f, 0.3f, 1f));
        Stretch(fill.rectTransform);
        SetReference(fields, "foodFill", fill);

        Color dim = new Color(0.72f, 0.72f, 0.78f, 0.9f);
        TMP_Text foodLabel = NewLabel("Food_Label", root, "FOOD  100%", 24f, dim);
        TopCentre(foodLabel.rectTransform, -118f, 420f, 30f);
        SetReference(fields, "foodText", foodLabel);

        TMP_Text state = NewLabel("State", root, "HOLD  E", 30f, dim);
        TopCentre(state.rectTransform, -158f, 620f, 36f);
        SetReference(fields, "stateText", state);

        TMP_Text hint = NewLabel("Hint", root, "LAND ON THE FOOD AND HOLD  E  TO EAT IT", 26f, dim);
        RectTransform hintRect = hint.rectTransform;
        hintRect.anchorMin = hintRect.anchorMax = new Vector2(0.5f, 0f);
        hintRect.pivot = new Vector2(0.5f, 0f);
        hintRect.anchoredPosition = new Vector2(0f, 120f);
        hintRect.sizeDelta = new Vector2(1100f, 36f);
        SetReference(fields, "hintText", hint);
    }

    // Left inactive in the scene so it does not cover the kitchen while editing; tick it on to edit it.
    static void BuildHudResult(Transform root, SerializedObject fields)
    {
        var resultRoot = new GameObject("Result", typeof(RectTransform));
        resultRoot.transform.SetParent(root, false);
        Stretch((RectTransform)resultRoot.transform);
        Stretch(NewImage("Scrim", resultRoot.transform, new Color(0f, 0f, 0f, 0.55f)).rectTransform);

        TMP_Text headline = NewLabel("Headline", resultRoot.transform, "THE FOOD SURVIVED", 78f, new Color(0.95f, 0.95f, 0.97f, 1f));
        Centre(headline.rectTransform, 40f, 1400f, 96f);
        TMP_Text note = NewLabel("Note", resultRoot.transform, "YOU KEPT THEM OFF IT TO THE LAST SECOND  ·  YOU WIN", 30f, new Color(0.72f, 0.72f, 0.78f, 0.9f));
        Centre(note.rectTransform, -40f, 1400f, 40f);

        SetReference(fields, "resultRoot", resultRoot);
        SetReference(fields, "resultText", headline);
        SetReference(fields, "resultNoteText", note);
        resultRoot.SetActive(false);
    }

    // Fails closed: a field renamed in MatchHud without this being updated stops the run loudly
    // instead of leaving a HUD that switches itself off at runtime.
    static void SetReference(SerializedObject fields, string fieldName, Object value)
    {
        SerializedProperty property = fields.FindProperty(fieldName);
        if (property == null)
        {
            throw new System.InvalidOperationException($"Add HUD: {nameof(MatchHud)} has no serialized field '{fieldName}'. Update {nameof(MatchWiring)} to match it.");
        }
        property.objectReferenceValue = value;
    }

    static void TopCentre(RectTransform rect, float fromTop, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, fromTop - 120f);
        rect.sizeDelta = new Vector2(width, height);
    }

    static void Centre(RectTransform rect, float fromMiddle, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, fromMiddle);
        rect.sizeDelta = new Vector2(width, height);
    }

    static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    static Image NewImage(string name, Transform parent, Color color)
    {
        var imageObject = new GameObject(name, typeof(RectTransform));
        imageObject.transform.SetParent(parent, false);
        Image image = imageObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    static TMP_Text NewLabel(string name, Transform parent, string content, float size, Color color)
    {
        var textObject = new GameObject(name, typeof(RectTransform));
        textObject.transform.SetParent(parent, false);
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.text = content;
        text.fontSize = size;
        text.color = color;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        return text;
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
