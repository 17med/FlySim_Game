using UnityEditor;
using UnityEngine;

// The dining room of the lobby stage: the floor, four walls with a doorway in the back one, the table
// and its chairs, a sideboard and a few props from the StylizedKitchen pack. See MenuStageBuilder.cs.
// Lobby_Set local space, in metres: the table is at the origin with its long side along x, and +z is
// the door side, where all three cameras are, so every view looks towards -z. The flies face +z, so
// one way of standing serves every view and nothing has to turn them between the menu and the lobby.
// There is no ceiling: no view looks up far enough to miss it.
// Every collider is stripped - nothing here is touched, and the pack's hulls are bigger than its meshes.
public partial class MenuStageBuilder
{
    const string MeshesFolder = "Assets/models/env/Content/StylizedKitchen/Meshes";
    const float RoomHalfWidth = 2.5f;
    // Inner faces of the two end walls. The doorway is in the back wall, behind the cameras.
    const float FrontWallZ = -2.4f;
    const float BackWallZ = 3.2f;
    const float WallHeight = 2.6f;
    const float WallThickness = 0.12f;
    // Where the dark panelling on the lower wall stops and the wallpaper starts.
    const float WainscotHeight = 0.9f;
    // Right of centre, so the human view through it, which looks over the old man's right shoulder,
    // stays inside the opening instead of looking at the wall beside it.
    const float DoorCenterX = 0.55f;
    const float DoorWidth = 0.95f;
    const float DoorHeight = 2.1f;
    const float CasingWidth = 0.07f;
    const float CasingDepth = 0.02f;
    const float TableTop = 0.76f;
    const float TableHalfLength = 0.8f;
    const float TableHalfDepth = 0.45f;
    const float TableTopThickness = 0.04f;
    const float TableLegSize = 0.06f;
    const float TableLegInset = 0.05f;
    const float SideboardTop = 0.86f;
    // Picture frames are 2 cm thick with their pivot in the middle, show their picture on their -X
    // side and are single-sided (as in KitchenMapBuilder): turned 90 degrees and kept off the wall.
    const string PicturePrefix = "SM_Frames_";
    const float PictureYawOffset = 90f;
    const float PictureWallGap = 0.011f;
    // A warm pool of light on the table, under the bulb, which itself has no light of its own nearby.
    static readonly Vector3 TableLightSpot = new Vector3(0f, 1.55f, 0.1f);
    const float TableLightRange = 3.2f;
    // Low: the bulb's spotlight is the table's main light (see TameLights); this only warms the room.
    const float TableLightIntensity = 0.5f;
    static readonly Color WarmLight = new Color(1f, 0.85f, 0.65f);

    static readonly Color WallpaperColor = new Color(0.42f, 0.37f, 0.31f);
    static readonly Color PanellingColor = new Color(0.27f, 0.18f, 0.13f);
    static readonly Color FloorColor = new Color(0.33f, 0.23f, 0.16f);
    static readonly Color TrimColor = new Color(0.82f, 0.78f, 0.7f);
    static readonly Color TableColor = new Color(0.46f, 0.3f, 0.18f);
    static readonly Color ChairColor = new Color(0.38f, 0.25f, 0.16f);

    readonly struct PropSpot
    {
        public readonly string Model;
        public readonly Vector3 Spot;
        public readonly float Facing;

        public PropSpot(string model, Vector3 spot, float facing = 0f)
        {
            Model = model;
            Spot = spot;
            Facing = facing;
        }
    }

    // Facings are yaws for a model's front: 0 looks along +z (towards the door), 90 along +x.
    static readonly PropSpot[] Props =
    {
        new PropSpot("SM_Carpet_02", new Vector3(0f, 0f, 0f)),
        new PropSpot("SM_Candle", new Vector3(-0.6f, TableTop, -0.2f)),
        new PropSpot("SM_BreakfastProps_Bread", new Vector3(0.58f, TableTop, -0.14f), 20f),
        new PropSpot("SM_FoodPot_01", new Vector3(-0.45f, SideboardTop, FrontWallZ + 0.24f)),
        new PropSpot("SM_Candle", new Vector3(0.02f, SideboardTop, FrontWallZ + 0.2f)),
        new PropSpot("SM_FlowerPot_02", new Vector3(0.45f, SideboardTop, FrontWallZ + 0.24f)),
        new PropSpot("SM_FlowerPot_09", new Vector3(-2.15f, 0f, FrontWallZ + 0.35f)),
    };

    // Bottom edge of each frame, on the wall face; the facing is the way the picture looks.
    static readonly PropSpot[] Pictures =
    {
        new PropSpot("SM_Frames_02", new Vector3(-0.42f, 1.3f, FrontWallZ)),
        new PropSpot("SM_Frames_04", new Vector3(0.4f, 1.38f, FrontWallZ)),
        new PropSpot("SM_Frames_01", new Vector3(-RoomHalfWidth, 1.35f, 0.3f), 90f),
    };

    StageCast Build(Transform stage)
    {
        root = new GameObject(LobbySetName).transform;
        root.SetParent(stage, false);
        BuildRoom(Group(root, "Room"));
        BuildFurniture(Group(root, "Furniture"));
        DressRoom(Group(root, "Props"));
        AddTableLight(root);
        var cast = new StageCast { Set = root };
        cast.Flies = PlaceFlies(Group(root, "Flies"));
        cast.OldMan = PlaceOldMan(Group(root, "Doorway"));
        PlaceViews(Group(root, "Views"), cast);
        Undo.RegisterCreatedObjectUndo(root.gameObject, UndoName);
        return cast;
    }

    // ---- the room ------------------------------------------------------------------------------

    void BuildRoom(Transform parent)
    {
        float outerX = RoomHalfWidth + WallThickness;
        Box(parent, "Floor", new Vector3(-outerX, -0.05f, FrontWallZ - WallThickness), new Vector3(outerX, 0f, BackWallZ + WallThickness), PaintSurface("Floor", FloorColor));
        Wall(parent, "Wall_Front", new Vector3(-outerX, 0f, FrontWallZ - WallThickness), new Vector3(outerX, WallHeight, FrontWallZ));
        Wall(parent, "Wall_Left", new Vector3(-outerX, 0f, FrontWallZ), new Vector3(-RoomHalfWidth, WallHeight, BackWallZ));
        Wall(parent, "Wall_Right", new Vector3(RoomHalfWidth, 0f, FrontWallZ), new Vector3(outerX, WallHeight, BackWallZ));

        float doorLeft = DoorCenterX - DoorWidth * 0.5f;
        float doorRight = DoorCenterX + DoorWidth * 0.5f;
        float backOuter = BackWallZ + WallThickness;
        Wall(parent, "Wall_Back_Left", new Vector3(-outerX, 0f, BackWallZ), new Vector3(doorLeft, WallHeight, backOuter));
        Wall(parent, "Wall_Back_Right", new Vector3(doorRight, 0f, BackWallZ), new Vector3(outerX, WallHeight, backOuter));
        Wall(parent, "Wall_Back_Lintel", new Vector3(doorLeft, DoorHeight, BackWallZ), new Vector3(doorRight, WallHeight, backOuter));
        BuildDoorCasing(parent, doorLeft, doorRight);
    }

    // Panelling below WainscotHeight and wallpaper above it, so a wall reads as a wall in a house
    // rather than as a flat box.
    void Wall(Transform parent, string name, Vector3 min, Vector3 max)
    {
        if (min.y < WainscotHeight)
        {
            Box(parent, name + "_Panelling", min, new Vector3(max.x, Mathf.Min(max.y, WainscotHeight), max.z), PaintSurface("Panelling", PanellingColor));
        }
        if (max.y > WainscotHeight)
        {
            Box(parent, name, new Vector3(min.x, Mathf.Max(min.y, WainscotHeight), min.z), max, PaintSurface("Wallpaper", WallpaperColor));
        }
    }

    // A pale frame proud of both faces of the wall, so the doorway stands out in the human view.
    void BuildDoorCasing(Transform parent, float doorLeft, float doorRight)
    {
        Material trim = Paint("Trim", TrimColor);
        float front = BackWallZ - CasingDepth;
        float back = BackWallZ + WallThickness + CasingDepth;
        float top = DoorHeight + CasingWidth;
        Box(parent, "Door_Casing_Left", new Vector3(doorLeft - CasingWidth, 0f, front), new Vector3(doorLeft, top, back), trim);
        Box(parent, "Door_Casing_Right", new Vector3(doorRight, 0f, front), new Vector3(doorRight + CasingWidth, top, back), trim);
        Box(parent, "Door_Casing_Head", new Vector3(doorLeft - CasingWidth, DoorHeight, front), new Vector3(doorRight + CasingWidth, top, back), trim);
    }

    // ---- the furniture -------------------------------------------------------------------------

    void BuildFurniture(Transform parent)
    {
        BuildTable(Group(parent, "Table"));
        // Two on the far side and one at each end: none between the table and the cameras.
        Chair(parent, "Chair_Far_Left", new Vector3(-0.45f, 0f, -0.68f), 0f);
        Chair(parent, "Chair_Far_Right", new Vector3(0.45f, 0f, -0.68f), 0f);
        Chair(parent, "Chair_End_Left", new Vector3(-1.02f, 0f, 0f), 90f);
        Chair(parent, "Chair_End_Right", new Vector3(1.02f, 0f, 0f), -90f);
        BuildSideboard(Group(parent, "Sideboard"));
    }

    void BuildTable(Transform parent)
    {
        Material wood = Paint("Table", TableColor, 0.35f);
        Box(parent, "Top", new Vector3(-TableHalfLength, TableTop - TableTopThickness, -TableHalfDepth), new Vector3(TableHalfLength, TableTop, TableHalfDepth), wood);
        float legX = TableHalfLength - TableLegInset - TableLegSize;
        float legZ = TableHalfDepth - TableLegInset - TableLegSize;
        float legTop = TableTop - TableTopThickness;
        // Between the legs' inner faces, under the top, where it hides the joints.
        Box(parent, "Apron", new Vector3(-legX, legTop - 0.08f, -legZ), new Vector3(legX, legTop, legZ), wood);
        foreach (float x in new[] { -legX - TableLegSize, legX })
        {
            foreach (float z in new[] { -legZ - TableLegSize, legZ })
            {
                Box(parent, "Leg", new Vector3(x, 0f, z), new Vector3(x + TableLegSize, legTop, z + TableLegSize), wood);
            }
        }
    }

    // A dining chair facing its yaw, with its back on the far side from where it faces.
    void Chair(Transform parent, string name, Vector3 spot, float yaw)
    {
        Material wood = Paint("Chair", ChairColor, 0.3f);
        Transform chair = Group(parent, name);
        chair.localPosition = spot;
        chair.localRotation = Quaternion.Euler(0f, yaw, 0f);
        const float half = 0.21f;
        const float seat = 0.45f;
        const float leg = 0.04f;
        Box(chair, "Seat", new Vector3(-half, seat - 0.04f, -half), new Vector3(half, seat, half), wood);
        foreach (float x in new[] { -half, half - leg })
        {
            foreach (float z in new[] { -half, half - leg })
            {
                Box(chair, "Leg", new Vector3(x, 0f, z), new Vector3(x + leg, seat - 0.04f, z + leg), wood);
            }
        }
        Box(chair, "Back", new Vector3(-half, seat, -half), new Vector3(half, 0.95f, -half + 0.04f), wood);
    }

    // Against the front wall, behind the flies in the menu and fly views, so there is something
    // warm to look at past them.
    void BuildSideboard(Transform parent)
    {
        Material wood = Paint("Sideboard", PanellingColor, 0.3f);
        Box(parent, "Body", new Vector3(-0.75f, 0f, FrontWallZ), new Vector3(0.75f, SideboardTop - 0.04f, FrontWallZ + 0.45f), wood);
        Box(parent, "Top", new Vector3(-0.78f, SideboardTop - 0.04f, FrontWallZ), new Vector3(0.78f, SideboardTop, FrontWallZ + 0.48f), Paint("Table", TableColor, 0.35f));
    }

    void DressRoom(Transform parent)
    {
        foreach (PropSpot prop in Props)
        {
            PlaceModel(parent, prop.Model, prop.Spot, prop.Facing);
        }
        foreach (PropSpot picture in Pictures)
        {
            Vector3 outward = Quaternion.Euler(0f, picture.Facing, 0f) * Vector3.forward;
            PlaceModel(parent, picture.Model, picture.Spot + outward * PictureWallGap, picture.Facing);
        }
    }

    void AddTableLight(Transform parent)
    {
        var light = new GameObject("Table_Light").AddComponent<Light>();
        light.transform.SetParent(parent, false);
        light.transform.localPosition = TableLightSpot;
        light.type = LightType.Point;
        light.color = WarmLight;
        light.range = TableLightRange;
        light.intensity = TableLightIntensity;
        // Like every other light on this stage: shadows would cost more than they add here.
        light.shadows = LightShadows.None;
    }

    // ---- placing -------------------------------------------------------------------------------

    // Puts the model's pivot on the spot's x/z and stands its mesh on the spot's y, as KitchenMapBuilder
    // does: a few of the pack's pivots sit off the base vertically.
    void PlaceModel(Transform parent, string modelName, Vector3 spot, float facing)
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{MeshesFolder}/{modelName}.FBX");
        if (model == null)
        {
            // Fails open: a missing prop leaves a gap in the dressing, nothing more.
            Debug.LogWarning($"Build Lobby Stage: {MeshesFolder}/{modelName}.FBX not found; skipped.");
            return;
        }
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
        Transform t = instance.transform;
        float yaw = facing + (modelName.StartsWith(PicturePrefix) ? PictureYawOffset : 0f);
        t.localRotation = Quaternion.Euler(0f, yaw, 0f) * model.transform.localRotation;
        t.localPosition = new Vector3(spot.x, spot.y, spot.z);
        StripColliders(instance);
        if (TryMeshBounds(instance, out Bounds bounds))
        {
            t.position += Vector3.up * (parent.TransformPoint(spot).y - bounds.min.y);
        }
    }

    // Five parameters (over the usual four), as in KitchenMapBuilder.Box: a block needs its name,
    // both corners and a material, and an object holding them would only move them one call away.
    static void Box(Transform parent, string name, Vector3 min, Vector3 max, Material material)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(parent, false);
        box.transform.localPosition = (min + max) * 0.5f;
        box.transform.localScale = max - min;
        box.GetComponent<MeshRenderer>().sharedMaterial = material;
        StripColliders(box);
    }

    static void StripColliders(GameObject target)
    {
        foreach (Collider collider in target.GetComponentsInChildren<Collider>(true))
        {
            Object.DestroyImmediate(collider);
        }
    }

    // Particle and trail renderers are left out: in the Editor their bounds are wherever the last
    // simulation left them.
    static bool TryMeshBounds(GameObject target, out Bounds bounds)
    {
        bounds = default;
        bool found = false;
        foreach (Renderer renderer in target.GetComponentsInChildren<Renderer>(true))
        {
            if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer))
            {
                continue;
            }
            if (found)
            {
                bounds.Encapsulate(renderer.bounds);
            }
            else
            {
                bounds = renderer.bounds;
                found = true;
            }
        }
        return found;
    }

    static Transform Group(Transform parent, string name)
    {
        var group = new GameObject(name).transform;
        group.SetParent(parent, false);
        return group;
    }

    // ---- paints --------------------------------------------------------------------------------

    // A toon material, made once per name. The shader reads smoothness from an ORM map, so each colour
    // gets a 1x1 one; without it its white default means fully rough and fully metallic (as in
    // KitchenMapBuilder.Paint).
    Material Paint(string name, Color color, float smoothness = 0.2f)
    {
        if (materials.TryGetValue(name, out Material material))
        {
            return material;
        }
        material = new Material(assets.Toon) { name = "LobbySet_" + name };
        material.SetColor("_BaseColor", color);
        material.SetTexture("_ORMMap", SolidTexture(name + "_ORM", new Color(1f, 1f - smoothness, 0f)));
        materials[name] = material;
        return material;
    }

    // Walls and floor: big flat surfaces look heavy with an outline, so theirs is off.
    Material PaintSurface(string name, Color color)
    {
        Material material = Paint(name, color);
        material.SetFloat("_OutlineWidth", 0f);
        return material;
    }

    // ORM data is linear, not a colour, so the texture skips sRGB conversion.
    static Texture2D SolidTexture(string name, Color value)
    {
        var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false, true) { name = "LobbySet_" + name };
        texture.SetPixel(0, 0, value);
        texture.Apply();
        return texture;
    }
}
