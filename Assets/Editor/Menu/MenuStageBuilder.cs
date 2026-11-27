using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Builds the 3D set behind the main menu and the lobby (Tools > Main Menu > Build Lobby Stage): a
// dining room with four flies on the table, the old man in the doorway, and the three camera views
// MenuStageDirector moves the menu camera between. It works on the open MainMenu scene.
// The old set - a bottle cap on a counter with sugar cubes and crumbs round it - is deleted; the
// lights, the swinging bulb, the dust and the hover accents the buttons switch on are kept and moved
// over the table, so every reference to them in the scene still holds.
// Running it again is safe: the previous Lobby_Set is replaced, and everything goes through Undo.
// It marks the scene dirty and leaves saving to you; RenderPreviewBatch (MenuStageBuilder.Preview.cs)
// builds, saves and photographs it from the command line.
// The set is laid out in real metres, around the table at the origin of Lobby_Set; the room's own
// files are MenuStageBuilder.Set.cs (walls, furniture, props) and MenuStageBuilder.Cast.cs (flies,
// old man, camera views).
public partial class MenuStageBuilder
{
    const string UndoName = "Build Lobby Stage";
    const string StageRootName = "Stage_Root";
    const string LobbySetName = "Lobby_Set";
    const string CameraName = "Main Camera";
    const string BackdropName = "Backdrop";
    const string BulbPivotName = "Bulb_Pivot";
    const string DustName = "Dust";
    const string AccentRedName = "Accent_Red";
    const string AccentBlueName = "Accent_Blue";
    const string UiRootName = "UI_Root";
    const string RoomScrimName = "Room_Scrim";
    const string MenuFlyPrefabPath = "Assets/Prefabs/MenuFly.prefab";
    // The plain model with its idle animation, not the networked variant the match spawns.
    const string OldManPrefabPath = "Assets/models/old_person/old_person.prefab";
    const string OldManLayerName = "old guy";
    const string ToonShaderName = "FlySimulator/StylizedToon";

    // The old bottle-cap set. Matched only under Stage_Root, because UI_Root reuses names like these.
    static readonly string[] ReplacedNames = { "Counter", "Cap_Top", "Cap_Flange", "Fly_Stage_Anchor" };
    static readonly string[] ReplacedPrefixes = { "Sugar_", "Crumb_" };

    // The kept pieces of the old set, which was built about ten times larger than real life. The
    // bulb's model hangs 5.5 of its units under its pivot, so at this scale it is a 15 cm bulb on a
    // 0.9 m wire. Only its rotation is animated (BulbSway.anim), so moving and scaling it is safe.
    const float BulbScale = 0.16f;
    static readonly Vector3 BulbPivotSpot = new Vector3(0.12f, 2.6f, -0.05f);
    // The dust's box emitter and particles are in its own scale (Local scaling, local simulation).
    const float DustScale = 0.15f;
    static readonly Vector3 DustSpot = new Vector3(0f, 1.25f, 0.05f);
    // Either side of the table on the camera side, so the button hover tints the flies' faces.
    static readonly Vector3 AccentRedSpot = new Vector3(-0.85f, 1.05f, 0.6f);
    static readonly Vector3 AccentBlueSpot = new Vector3(0.85f, 1.05f, 0.6f);
    const float AccentRange = 2.4f;
    // The old menu's lights were set for one fly on a bottle cap: a 3.8 directional "spot" and a 1.25
    // red rim, both lighting the whole scene. In a lit room they blew the tablecloth out to white, so
    // the spot becomes a real spotlight hanging from the bulb - a warm pool on the table that swings
    // with it - and the directional lights drop to a soft key, a faint rim and the fill as it was.
    const string StageSpotName = "Stage_Spot";
    const string KeyLightName = "Key_Light";
    const string RimLightName = "Rim_Light";
    const float StageSpotIntensity = 2.2f;
    const float StageSpotRange = 3.5f;
    const float StageSpotAngle = 80f;
    const float StageSpotInnerAngle = 40f;
    const float KeyLightIntensity = 0.7f;
    const float RimLightIntensity = 0.3f;
    // The lobby's full-screen scrim was 46% dark, which buried the set the lobby now shows off.
    const float RoomScrimMaxAlpha = 0.15f;

    // Everything the build needs, found before anything in the scene is touched.
    readonly struct StageAssets
    {
        public readonly Shader Toon;
        public readonly GameObject FlyPrefab;
        public readonly GameObject OldManPrefab;
        public readonly int OldManLayer;
        // Root scale that makes one fly FlyTargetLength long.
        public readonly float FlyScale;

        // Five parameters (over the usual four): this struct is what spares every other method of the
        // build from taking these five one by one.
        public StageAssets(Shader toon, GameObject flyPrefab, GameObject oldManPrefab, int oldManLayer, float flyScale)
        {
            Toon = toon;
            FlyPrefab = flyPrefab;
            OldManPrefab = oldManPrefab;
            OldManLayer = oldManLayer;
            FlyScale = flyScale;
        }
    }

    // What the director is wired to, as the build makes it.
    sealed class StageCast
    {
        public Transform Set;
        public Transform MenuView;
        public Transform FlyLobbyView;
        public Transform HumanLobbyView;
        public MenuFlyDance[] Flies;
        public GameObject OldMan;
    }

    readonly StageAssets assets;
    readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
    Transform root;

    MenuStageBuilder(StageAssets assets)
    {
        this.assets = assets;
    }

    [MenuItem("Tools/Main Menu/Build Lobby Stage")]
    static void BuildFromMenu()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (TryBuild(scene, out _))
        {
            Debug.Log($"Build Lobby Stage: {LobbySetName} is under {StageRootName} and {nameof(MenuStageDirector)} is wired. Save {scene.name} (Ctrl+S).");
        }
    }

    // Fails closed: every check that can fail is made before the scene is changed, so a missing
    // prefab or shader leaves the old set as it was rather than half of a new one.
    static bool TryBuild(Scene scene, out MenuStageDirector director)
    {
        director = null;
        if (scene.name != GameSceneManager.MainMenuScene)
        {
            Debug.LogError($"Build Lobby Stage: open {GameSceneManager.MainMenuScene} first (the open scene is '{scene.name}').");
            return false;
        }
        Transform stage = FindRoot(scene, StageRootName);
        if (stage == null)
        {
            Debug.LogError($"Build Lobby Stage: no root object named {StageRootName} in {scene.name}; the stage is built under it, so nothing was changed.");
            return false;
        }
        Camera camera = FindCamera(stage);
        if (camera == null || !TryLoadAssets(out StageAssets loaded))
        {
            return false;
        }

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(UndoName);
        int undoGroup = Undo.GetCurrentGroup();
        RemoveReplacedObjects(stage);
        StageCast cast = new MenuStageBuilder(loaded).Build(stage);
        RestageKeptObjects(stage, cast.Set);
        TameLights(stage);
        SoftenRoomScrim(scene);
        director = WireDirector(stage, camera, cast, loaded.OldManLayer);
        Undo.CollapseUndoOperations(undoGroup);
        EditorSceneManager.MarkSceneDirty(scene);
        return true;
    }

    static bool TryLoadAssets(out StageAssets loaded)
    {
        loaded = default;
        Shader toon = Shader.Find(ToonShaderName);
        var flyPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(MenuFlyPrefabPath);
        var oldManPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(OldManPrefabPath);
        int oldManLayer = LayerMask.NameToLayer(OldManLayerName);
        if (toon == null)
        {
            Debug.LogError($"Build Lobby Stage: shader '{ToonShaderName}' not found; nothing was changed.");
            return false;
        }
        if (flyPrefab == null || oldManPrefab == null)
        {
            Debug.LogError($"Build Lobby Stage: no prefab at {(flyPrefab == null ? MenuFlyPrefabPath : OldManPrefabPath)}; nothing was changed.");
            return false;
        }
        if (oldManLayer < 0)
        {
            Debug.LogError($"Build Lobby Stage: there is no layer named '{OldManLayerName}' (Project Settings > Tags and Layers); the fly view hides the old man by it, so nothing was changed.");
            return false;
        }
        if (!TryMeasureFlyScale(flyPrefab, out float flyScale))
        {
            return false;
        }
        loaded = new StageAssets(toon, flyPrefab, oldManPrefab, oldManLayer, flyScale);
        return true;
    }

    // ---- the old set ---------------------------------------------------------------------------

    static void RemoveReplacedObjects(Transform stage)
    {
        var doomed = new List<Transform>();
        foreach (Transform t in stage.GetComponentsInChildren<Transform>(true))
        {
            if (t != stage && IsReplaced(t.name))
            {
                doomed.Add(t);
            }
        }
        foreach (Transform t in doomed)
        {
            // Already gone when its parent was on the list too.
            if (t != null)
            {
                Undo.DestroyObjectImmediate(t.gameObject);
            }
        }
    }

    static bool IsReplaced(string name)
    {
        if (name == LobbySetName || System.Array.IndexOf(ReplacedNames, name) >= 0)
        {
            return true;
        }
        foreach (string prefix in ReplacedPrefixes)
        {
            if (name.StartsWith(prefix))
            {
                return true;
            }
        }
        return false;
    }

    // Fails open, piece by piece: these only dress the set, and the stage works without any of them.
    static void RestageKeptObjects(Transform stage, Transform lobbySet)
    {
        Transform bulb = FindIn(stage, BulbPivotName);
        if (bulb != null)
        {
            Undo.RecordObject(bulb, UndoName);
            bulb.position = lobbySet.TransformPoint(BulbPivotSpot);
            bulb.localScale = Vector3.one * BulbScale;
        }
        Transform dust = FindIn(stage, DustName);
        if (dust != null)
        {
            Undo.RecordObject(dust, UndoName);
            dust.position = lobbySet.TransformPoint(DustSpot);
            dust.localScale = Vector3.one * DustScale;
        }
        MoveAccent(FindIn(stage, AccentRedName), lobbySet.TransformPoint(AccentRedSpot));
        MoveAccent(FindIn(stage, AccentBlueName), lobbySet.TransformPoint(AccentBlueSpot));
        // The old gradient wall and floor glow sat behind the bottle cap; the room's walls now hide them.
        Transform backdrop = FindIn(stage, BackdropName);
        if (backdrop != null)
        {
            Undo.RecordObject(backdrop.gameObject, UndoName);
            backdrop.gameObject.SetActive(false);
        }
    }

    // Fails open, light by light: a light that is missing simply is not tamed.
    static void TameLights(Transform stage)
    {
        Light spot = LightIn(stage, StageSpotName);
        if (spot != null)
        {
            Undo.RecordObject(spot, UndoName);
            Undo.RecordObject(spot.transform, UndoName);
            spot.type = LightType.Spot;
            spot.intensity = StageSpotIntensity;
            spot.range = StageSpotRange;
            spot.spotAngle = StageSpotAngle;
            spot.innerSpotAngle = StageSpotInnerAngle;
            // Straight down from where it hangs; it is under the bulb's pivot, so the sway carries it.
            spot.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
        }
        SetIntensity(LightIn(stage, KeyLightName), KeyLightIntensity);
        SetIntensity(LightIn(stage, RimLightName), RimLightIntensity);
    }

    static Light LightIn(Transform stage, string lightName)
    {
        Transform found = FindIn(stage, lightName);
        return found != null ? found.GetComponent<Light>() : null;
    }

    static void SetIntensity(Light light, float intensity)
    {
        if (light == null)
        {
            return;
        }
        Undo.RecordObject(light, UndoName);
        light.intensity = intensity;
    }

    // The button hover switches these on and off by reference, so they are moved, never rebuilt.
    static void MoveAccent(Transform accent, Vector3 position)
    {
        if (accent == null)
        {
            return;
        }
        Undo.RecordObject(accent, UndoName);
        accent.position = position;
        var light = accent.GetComponent<Light>();
        if (light != null)
        {
            Undo.RecordObject(light, UndoName);
            light.range = AccentRange;
        }
    }

    // Fails open: a missing scrim only means a darker lobby.
    static void SoftenRoomScrim(Scene scene)
    {
        Transform uiRoot = FindRoot(scene, UiRootName);
        Transform scrim = uiRoot != null ? FindIn(uiRoot, RoomScrimName) : null;
        Image image = scrim != null ? scrim.GetComponent<Image>() : null;
        if (image == null)
        {
            Debug.LogWarning($"Build Lobby Stage: no {RoomScrimName} image under {UiRootName}; the lobby's scrim was left alone.");
            return;
        }
        Undo.RecordObject(image, UndoName);
        Color color = image.color;
        color.a = Mathf.Min(color.a, RoomScrimMaxAlpha);
        image.color = color;
    }

    static MenuStageDirector WireDirector(Transform stage, Camera camera, StageCast cast, int oldManLayer)
    {
        var director = stage.GetComponent<MenuStageDirector>();
        if (director == null)
        {
            director = Undo.AddComponent<MenuStageDirector>(stage.gameObject);
        }
        // The players' names over the cast; it reads everything it needs from the director.
        if (stage.GetComponent<MenuFlyNameTags>() == null)
        {
            Undo.AddComponent<MenuFlyNameTags>(stage.gameObject);
        }
        Undo.RecordObject(director, UndoName);
        director.stageCamera = camera;
        director.menuView = cast.MenuView;
        director.flyLobbyView = cast.FlyLobbyView;
        director.humanLobbyView = cast.HumanLobbyView;
        director.flies = cast.Flies;
        director.oldMan = cast.OldMan;
        director.oldManLayer = oldManLayer;
        director.seatCentre = SeatCentre;
        director.menuSpacing = FlySpacing;
        director.lobbySpacing = LobbyFlySpacing;
        director.arcDepth = FlyArcDepth;
        // Saved as the menu looks, so the frames before the director's first Update match it.
        Undo.RecordObject(camera, UndoName);
        Undo.RecordObject(camera.transform, UndoName);
        director.ShowImmediate(MenuStageDirector.StageState.Menu);
        return director;
    }

    // ---- finding things ------------------------------------------------------------------------

    static Camera FindCamera(Transform stage)
    {
        Transform cameraObject = FindIn(stage, CameraName);
        Camera camera = cameraObject != null ? cameraObject.GetComponent<Camera>() : null;
        if (camera == null)
        {
            // Fails closed: the director needs the menu's camera, and a new one would bring a second
            // AudioListener into the scene.
            Debug.LogError($"Build Lobby Stage: no camera named '{CameraName}' under {StageRootName}; nothing was changed.");
        }
        return camera;
    }

    static Transform FindRoot(Scene scene, string name)
    {
        foreach (GameObject sceneRoot in scene.GetRootGameObjects())
        {
            if (sceneRoot.name == name)
            {
                return sceneRoot.transform;
            }
        }
        return null;
    }

    static Transform FindIn(Transform parent, string name)
    {
        foreach (Transform t in parent.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == name)
            {
                return t;
            }
        }
        return null;
    }
}
