using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Tools > Match > Add Minimap To Open Scene: photographs the open scene's house from above into the
// minimap picture, and puts the minimap (MinimapHud) in the top-right corner of the scene's Match_HUD.
// Run it in Kitchen and in Kitchen_online. Build House (Both Scenes) runs it too, after Wire Match has
// made the HUD. Running it again replaces the picture and the minimap, so it is safe to repeat after
// the house changes; nothing else in the scene is touched, so a dish removed by hand stays removed.
// The picture leaves out the players' bodies (they are markers on the map, not part of the house)
// and the ceiling (it would hide every room).
public static class MinimapWiring
{
    const string MapRootName = "KitchenMap";
    const string MinimapObjectName = "Minimap";
    const string PicturePath = "Assets/UI/Minimap/house_minimap.png";
    const string MarkerFolder = "Assets/ui game/FlyHUD/PNG@2x/minimap/";
    // House metres of floor shown around the outer walls, so a fly against a wall is still on the map.
    const float PictureMargin = 0.15f;
    // In the HUD's 1920 x 1080 reference pixels: the map's width, and its gap from the screen's corner.
    // The height follows the house's own proportions, so nothing is stretched.
    const float MapWidth = 380f;
    const float CornerGap = 24f;
    const float FrameThickness = 4f;
    // The FlyHUD pack's "Ink", its outline colour.
    static readonly Color FrameColor = new Color(0.169f, 0.165f, 0.188f, 0.9f);

    [MenuItem("Tools/Match/Add Minimap To Open Scene")]
    static void AddFromMenu()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (AddToScene(scene))
            Debug.Log($"Add Minimap: the minimap is in {scene.name}'s HUD. Save the scene (Ctrl+S).");
    }

    // Works on the open scene and does not save it, like the other wiring steps. False, having changed
    // nothing in the scene, when there is no house or no HUD to put the map on.
    internal static bool AddToScene(Scene scene)
    {
        GameObject map = scene.GetRootGameObjects().FirstOrDefault(root => root.name == MapRootName);
        MatchHud hud = Object.FindAnyObjectByType<MatchHud>(FindObjectsInactive.Include);
        if (map == null || hud == null)
        {
            // Fails closed: without the house there is nothing to draw, and without the HUD nowhere to
            // put it; guessing either would leave a map that shows the wrong place.
            Debug.LogError($"Add Minimap: {scene.name} needs a {MapRootName} root and a Match HUD (Tools > Match > Wire Match). Nothing was added.");
            return false;
        }
        if (!TryTakePicture(map.transform, out Texture2D picture, out Rect worldArea))
            return false;
        BuildMinimap(hud.transform, picture, worldArea);
        EditorSceneManager.MarkSceneDirty(scene);
        return true;
    }

    // The picture is one asset shared by both kitchen scenes, which hold the same house.
    static bool TryTakePicture(Transform map, out Texture2D picture, out Rect worldArea)
    {
        picture = null;
        worldArea = default;
        if (!HouseBuildPipeline.TryFrameHouse(map, out Bounds house, out List<Renderer> ceiling))
        {
            Debug.LogError($"Add Minimap: nothing to photograph under {map.name}. Nothing was added.");
            return false;
        }
        float margin = PictureMargin * map.lossyScale.x;
        List<Renderer> hidden = ceiling.Concat(PlayerRenderers()).Where(r => r.enabled).ToList();
        RenderTexture shot = null;
        try
        {
            hidden.ForEach(r => r.enabled = false);
            shot = HouseBuildPipeline.RenderFromAbove(house, margin, house.max.y - map.position.y);
            HouseBuildPipeline.WritePng(shot, PicturePath);
        }
        finally
        {
            hidden.ForEach(r => r.enabled = true);
            if (shot != null)
            {
                shot.Release();
                Object.DestroyImmediate(shot);
            }
        }
        ImportPicture();
        picture = AssetDatabase.LoadAssetAtPath<Texture2D>(PicturePath);
        // The same rectangle RenderFromAbove points its camera at: the house plus the margin all round.
        worldArea = new Rect(house.min.x - margin, house.min.z - margin, house.size.x + 2f * margin, house.size.z + 2f * margin);
        return picture != null;
    }

    // The fly and the old man stand in the single player Kitchen; online they are spawned later and
    // there is nothing to hide.
    static IEnumerable<Renderer> PlayerRenderers()
    {
        IEnumerable<Transform> players = Object.FindObjectsByType<FlyController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Select(fly => fly.transform.root)
            .Concat(Object.FindObjectsByType<OldPersonFPSController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Select(human => human.transform.root));
        return players.SelectMany(player => player.GetComponentsInChildren<Renderer>());
    }

    static void ImportPicture()
    {
        AssetDatabase.ImportAsset(PicturePath, ImportAssetOptions.ForceUpdate);
        var importer = (TextureImporter)AssetImporter.GetAtPath(PicturePath);
        if (importer == null)
            return;
        // A UI picture seen at one size: no mipmaps, clamped edges, and full resolution kept so the
        // furniture still reads on a small map.
        importer.textureType = TextureImporterType.Default;
        importer.mipmapEnabled = false;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.maxTextureSize = 2048;
        importer.SaveAndReimport();
    }

    static void BuildMinimap(Transform hud, Texture2D picture, Rect worldArea)
    {
        Transform old = hud.Find(MinimapObjectName);
        if (old != null)
            Undo.DestroyObjectImmediate(old.gameObject);

        var root = new GameObject(MinimapObjectName, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(root, "Add Minimap");
        var rect = (RectTransform)root.transform;
        rect.SetParent(hud, false);
        rect.anchorMin = rect.anchorMax = rect.pivot = Vector2.one;
        rect.anchoredPosition = new Vector2(-CornerGap, -CornerGap);
        rect.sizeDelta = new Vector2(MapWidth, MapWidth * worldArea.height / worldArea.width);

        Image frame = NewChild<Image>("Frame", rect, -FrameThickness);
        frame.color = FrameColor;
        RawImage mapImage = NewChild<RawImage>("Map", rect, 0f);
        mapImage.texture = picture;
        RectTransform markers = NewChild<RectTransform>("Markers", rect, 0f);

        MinimapHud minimap = root.AddComponent<MinimapHud>();
        var fields = new SerializedObject(minimap);
        fields.FindProperty("map").objectReferenceValue = mapImage;
        fields.FindProperty("worldArea").rectValue = worldArea;
        fields.FindProperty("markerLayer").objectReferenceValue = markers;
        fields.FindProperty("selfFlyMarker").objectReferenceValue = Marker("marker_player");
        fields.FindProperty("selfHumanMarker").objectReferenceValue = Marker("marker_human");
        fields.FindProperty("otherFlyMarker").objectReferenceValue = Marker("marker_player");
        fields.FindProperty("foodMarker").objectReferenceValue = Marker("marker_fruit_orange");
        fields.ApplyModifiedPropertiesWithoutUndo();
    }

    // A child stretched over its parent, grown by inset on every side (negative grows it outwards).
    static T NewChild<T>(string childName, RectTransform parent, float inset) where T : Component
    {
        var child = new GameObject(childName, typeof(RectTransform));
        var rect = (RectTransform)child.transform;
        rect.SetParent(parent, false);
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.one * inset;
        rect.offsetMax = -Vector2.one * inset;
        T component = typeof(T) == typeof(RectTransform) ? (T)(Component)rect : child.AddComponent<T>();
        if (component is Graphic graphic)
            graphic.raycastTarget = false;
        return component;
    }

    static Texture2D Marker(string fileName)
    {
        Texture2D marker = AssetDatabase.LoadAssetAtPath<Texture2D>(MarkerFolder + fileName + ".png");
        // Fails open: a missing marker leaves that kind of marker blank (white squares), the map still works.
        if (marker == null)
            Debug.LogWarning($"Add Minimap: {MarkerFolder}{fileName}.png not found; that marker will be a plain square.");
        return marker;
    }
}
