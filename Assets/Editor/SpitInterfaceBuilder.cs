using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

// Builds the on-screen spit drops into the open scene and wires them to the fly
// (Tools > Fly Spit > Build Spit Interface).
// Done as a menu step instead of editing the .unity file by hand because the scene is usually open
// in the Editor with unsaved changes, and a hand edit would be overwritten or conflict with them.
// It makes one Image per shot (FlySpit's Max Shots), puts them top-left where the FlyHUD README
// places the droplets, sets the drop_full / drop_empty sprites on a spitinterface, and drags that
// into FlySpit's Spit Interface slot. Running it again replaces the old row, so it is safe to repeat
// after changing Max Shots. Everything goes through Undo, so Ctrl+Z takes it back.
public static class SpitInterfaceBuilder
{
    const string RootName = "SpitInterface";
    const string CanvasName = "HUD Canvas";
    // The 1x set is the one imported as Sprites; the 2x pngs are plain textures and cannot go on an Image.
    const string FullSpritePath = "Assets/ui game/FlyHUD/PNG@1x/spit/drop_full.png";
    const string EmptySpritePath = "Assets/ui game/FlyHUD/PNG@1x/spit/drop_empty.png";
    // FlyHUD README: layout is designed at 1280x720, stats panel anchored top-left at (22, -20).
    static readonly Vector2 ReferenceResolution = new Vector2(1280f, 720f);
    static readonly Vector2 TopLeftOffset = new Vector2(22f, -20f);
    // Drawn at twice the 20x22 source size so the drops read at 720p.
    static readonly Vector2 DropSize = new Vector2(40f, 44f);
    const float DropSpacing = 6f;

    [MenuItem("Tools/Fly Spit/Build Spit Interface")]
    static void Build()
    {
        Sprite fullSprite = AssetDatabase.LoadAssetAtPath<Sprite>(FullSpritePath);
        Sprite emptySprite = AssetDatabase.LoadAssetAtPath<Sprite>(EmptySpritePath);
        // Fails closed: a row of blank white squares is worse than a clear message.
        if (fullSprite == null || emptySprite == null)
        {
            Debug.LogError($"Spit Interface: could not load {FullSpritePath} or {EmptySpritePath} as Sprites. Check they exist and are imported as Sprite (2D and UI).");
            return;
        }

        FlySpit flySpit = Object.FindAnyObjectByType<FlySpit>(FindObjectsInactive.Include);
        if (flySpit == null)
        {
            Debug.LogError("Spit Interface: no FlySpit in the open scene. Open the scene with the fly and run this again.");
            return;
        }

        var flySpitSettings = new SerializedObject(flySpit);
        int shotCount = flySpitSettings.FindProperty("ammo.maxShots").intValue;

        spitinterface ui = CreateRow(FindOrCreateCanvas(), shotCount);
        ui.fullSprite = fullSprite;
        ui.emptySprite = emptySprite;
        // Shown full in the Editor too, which is how the fly starts in Play mode.
        ui.ShowShots(shotCount);

        flySpitSettings.FindProperty("spitInterface").objectReferenceValue = ui;
        flySpitSettings.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(flySpit.gameObject.scene);
        Selection.activeGameObject = ui.gameObject;
        Debug.Log($"Spit Interface: built {shotCount} drops and connected them to FlySpit on '{flySpit.name}'. Save the scene to keep it.", ui);
    }

    static Canvas FindOrCreateCanvas()
    {
        foreach (Canvas existing in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (existing.isRootCanvas && existing.renderMode == RenderMode.ScreenSpaceOverlay)
                return existing;
        }

        var canvasObject = new GameObject(CanvasName, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        Undo.RegisterCreatedObjectUndo(canvasObject, "Build Spit Interface");
        canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = canvasObject.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = ReferenceResolution;
        return canvasObject.GetComponent<Canvas>();
    }

    static spitinterface CreateRow(Canvas canvas, int shotCount)
    {
        Transform oldRow = canvas.transform.Find(RootName);
        if (oldRow != null)
            Undo.DestroyObjectImmediate(oldRow.gameObject);

        var root = new GameObject(RootName, typeof(RectTransform), typeof(HorizontalLayoutGroup));
        Undo.RegisterCreatedObjectUndo(root, "Build Spit Interface");
        var rect = root.GetComponent<RectTransform>();
        rect.SetParent(canvas.transform, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = TopLeftOffset;
        rect.sizeDelta = new Vector2(shotCount * DropSize.x + (shotCount - 1) * DropSpacing, DropSize.y);

        var layout = root.GetComponent<HorizontalLayoutGroup>();
        layout.spacing = DropSpacing;
        layout.childControlWidth = layout.childControlHeight = false;
        layout.childForceExpandWidth = layout.childForceExpandHeight = false;

        var ui = root.AddComponent<spitinterface>();
        ui.spitImages = new Image[shotCount];
        for (int i = 0; i < shotCount; i++)
            ui.spitImages[i] = CreateDrop(rect, i);
        return ui;
    }

    static Image CreateDrop(RectTransform row, int index)
    {
        var drop = new GameObject($"Drop {index + 1}", typeof(RectTransform), typeof(Image));
        var rect = drop.GetComponent<RectTransform>();
        rect.SetParent(row, false);
        rect.sizeDelta = DropSize;
        var image = drop.GetComponent<Image>();
        image.preserveAspect = true;
        // Drops are display only; leaving raycasts on would let them block clicks meant for the game.
        image.raycastTarget = false;
        return image;
    }
}
