using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Builds the fly that replaces the mouse pointer in the main menu
// (Tools > Main Menu > Build Fly Cursor).
// Done as a menu step instead of editing MainMenu.unity by hand because the scene is usually open in
// the Editor with unsaved changes, and a hand edit would be overwritten or conflict with them.
// The fly goes on a Canvas of its own rather than inside UI_Root: UI_Root sorts at 0 and its panels
// would cover the cursor, and anything parented under its GraphicRaycaster can end up blocking the
// clicks it is supposed to be pointing at.
// Running it again rebuilds the same rig in place, so it is safe to repeat after changing the icon.
// Everything goes through Undo, so Ctrl+Z takes it back.
public static class MenuFlyCursorBuilder
{
    const string CursorRootName = "Cursor_Root";
    const string CursorName = "Fly_Cursor";
    // The 2x icon: a cursor is small on screen and the 64px version softens as soon as it is scaled.
    const string FlyIconPath = "Assets/ui game/FlyHUD/PNG@2x/badge/fly_icon.png";
    // Above every UI_Root panel (which sorts at 0) so the fly is never drawn behind a menu.
    const int CursorSortingOrder = 1000;
    // Matches UI_Root's CanvasScaler, so the fly grows and shrinks with the rest of the menu.
    static readonly Vector2 ReferenceResolution = new Vector2(1920f, 1080f);
    static readonly Vector2 CursorSize = new Vector2(52f, 52f);

    [MenuItem("Tools/Main Menu/Build Fly Cursor")]
    static void Build()
    {
        Scene scene = SceneManager.GetActiveScene();
        // Fails closed: building this into the wrong scene would hide the pointer there too.
        if (scene.name != GameSceneManager.MainMenuScene)
        {
            Debug.LogError($"Build Fly Cursor: open {GameSceneManager.MainMenuScene} first (the open scene is '{scene.name}').");
            return;
        }

        Texture2D flyIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(FlyIconPath);
        // A blank white square for a cursor is worse than a clear message.
        if (flyIcon == null)
        {
            Debug.LogError($"Build Fly Cursor: could not load {FlyIconPath}.");
            return;
        }

        // Rebuilt from scratch each run so old tuning never lingers in a half-updated rig.
        GameObject existingRoot = FindInScene(scene, CursorRootName);
        if (existingRoot != null)
        {
            Undo.DestroyObjectImmediate(existingRoot);
        }

        GameObject root = new GameObject(CursorRootName, typeof(Canvas), typeof(CanvasScaler));
        Undo.RegisterCreatedObjectUndo(root, "Build Fly Cursor");
        root.layer = LayerMask.NameToLayer("UI");

        Canvas canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = CursorSortingOrder;

        CanvasScaler scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = ReferenceResolution;
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
        // No GraphicRaycaster on purpose: the cursor is drawn, never clicked.

        GameObject cursor = new GameObject(CursorName, typeof(RawImage), typeof(MenuFlyCursor));
        Undo.RegisterCreatedObjectUndo(cursor, "Build Fly Cursor");
        cursor.layer = root.layer;
        cursor.transform.SetParent(root.transform, false);

        RectTransform cursorRect = (RectTransform)cursor.transform;
        // Pinned to the middle so the pointer position the script reads maps straight to anchoredPosition.
        cursorRect.anchorMin = new Vector2(0.5f, 0.5f);
        cursorRect.anchorMax = new Vector2(0.5f, 0.5f);
        cursorRect.pivot = new Vector2(0.5f, 0.5f);
        cursorRect.anchoredPosition = Vector2.zero;
        cursorRect.sizeDelta = CursorSize;

        // RawImage, not Image: the HUD pngs are imported as plain textures, and retyping a shared
        // asset as a Sprite would change it for everything else that uses it.
        RawImage image = cursor.GetComponent<RawImage>();
        image.texture = flyIcon;
        image.raycastTarget = false;

        SerializedObject serialized = new SerializedObject(cursor.GetComponent<MenuFlyCursor>());
        serialized.FindProperty("canvasRect").objectReferenceValue = (RectTransform)root.transform;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        Selection.activeGameObject = cursor;
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"Build Fly Cursor: {CursorRootName}/{CursorName} is in {scene.name}, sorting above UI_Root at {CursorSortingOrder}. Enter Play mode to see the fly take over the pointer.");
    }

    // Walks the scene instead of GameObject.Find so an inactive leftover rig is found and replaced too.
    static GameObject FindInScene(Scene scene, string name)
    {
        foreach (GameObject rootObject in scene.GetRootGameObjects())
        {
            foreach (Transform child in rootObject.GetComponentsInChildren<Transform>(true))
            {
                if (child.name == name)
                {
                    return child.gameObject;
                }
            }
        }
        return null;
    }
}
