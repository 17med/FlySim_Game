using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Builds a flyable two-room house, a kitchen and a living room joined by a doorway, into
// Assets/Scenes/Kitchen.unity (Tools > Stylized Kitchen > Build Kitchen Map).
// The pack's room shells, cabinets, fridge, stove, chairs and bookshelf only exist as Unreal .uasset
// files, which Unity cannot read, so those are coloured blocks; every prop with an FBX is the real model.
// The first run copies scene.unity, so the fly, the camera and the wiring between them come over untouched.
// Later runs only rebuild the KitchenMap root, the scene's ambient light and the sun's angle, so other
// edits are kept.
// Everything is shaded with the project's toon shader (FlySimulator/StylizedToon), which takes its bands,
// highlight and rim from the sun, so the sun is on and the ceiling lets it in (see AimSun).
// This file holds the entry point, the shared shell and the helpers; each room has its own file
// (KitchenMapBuilder.Kitchen.cs, KitchenMapBuilder.LivingRoom.cs).
// Every layout number is in real-world metres; the KitchenMap root scales them by WorldScale.
// Blockout materials and textures are created in code and saved inside Kitchen.unity, not as assets.
// Over the 400-line file guideline (about 480): the shared shell pieces (partition, window wall,
// baseboards), the placement helpers and the material helpers are used by both rooms. Moving the
// material helpers (Paint*) and the shell pieces to their own files is the natural next cut.
public partial class KitchenMapBuilder
{
    const string SourceScenePath = "Assets/scene.unity";
    const string KitchenScenePath = "Assets/Scenes/Kitchen.unity";
    // Same folder as KitchenMaterialBuilder.MeshesFolder.
    const string MeshesFolder = "Assets/models/env/Content/StylizedKitchen/Meshes";
    const string MapRootName = "KitchenMap";
    // The ground plane in scene.unity; the house floor replaces it.
    const string OldGroundName = "Plane";
    const string ToonShaderName = "FlySimulator/StylizedToon";
    // Only for glowing lamp shades: the toon shader has no emission.
    const string LitShaderName = "Universal Render Pipeline/Lit";
    // The fly's collider is 0.4 m across, so a real-size house would be cramped: at 10x a counter is
    // 9 m high. The FBX files are in centimetres and Unity already imports them at real size.
    const float WorldScale = 10f;

    // z runs from -HalfDepth (south) to +HalfDepth (north) in both rooms. The kitchen spans x from
    // KitchenWest to PartitionX, then comes the partition wall, then the living room up to LivingEast.
    const float HalfDepth = 2.5f;
    const float RoomHeight = 2.6f;
    const float WallThickness = 0.1f;
    const float KitchenWest = -3f;
    const float PartitionX = 3f;
    const float LivingWest = PartitionX + WallThickness;
    const float LivingEast = 9.1f;
    // Each room builds and paints its own half of the partition, meeting here.
    const float PartitionMiddle = PartitionX + WallThickness * 0.5f;
    const float DoorwaySouth = -1.9f;
    const float DoorwayNorth = -0.7f;
    const float DoorwayHeight = 2.1f;
    const float TrimWidth = 0.07f;
    const float TrimDepth = 0.02f;
    const float WindowHalfWidth = 0.6f;
    const float WindowTop = 2f;
    const float SillHeight = 1.05f;
    const float SillDepth = 0.2f;
    const float BaseboardHeight = 0.1f;
    const float BaseboardDepth = 0.015f;
    // Picture frames are 2 cm thick with their pivot in the middle; this keeps the back on the wall.
    const float PictureWallGap = 0.011f;
    static readonly Color WarmLight = new Color(1f, 0.85f, 0.65f);
    // High and from the south-south-east: the kitchen counters, the dining table and the TV screen all
    // face the sun, and the 2.6 m walls cast shadows only about 1.5 m long.
    static readonly Vector3 SunAngles = new Vector3(60f, -30f, 0f);
    // The toon shader adds ambient on top of the sun, so it is kept low to avoid washing out the bands.
    static readonly Color AmbientSky = new Color(0.46f, 0.44f, 0.41f);
    static readonly Color AmbientEquator = new Color(0.36f, 0.33f, 0.3f);
    static readonly Color AmbientGround = new Color(0.22f, 0.2f, 0.18f);
    static readonly Color TrimWhite = new Color(0.97f, 0.96f, 0.93f);

    // Facings are compass yaws for a model's front: 0 = looks north (+Z), 90 = east, 180 = south, -90 = west.
    // Most models' fronts point +Z after import; these were measured from the FBX geometry.
    static readonly Dictionary<string, float> FrontYawOffsets = new Dictionary<string, float>
    {
        { "SM_LeatherCouch", -90f },  // the armchair's seat faces +X
        { "SM_TableBasse_01", -90f }, // the nightstand's drawer faces +X
    };
    // Every picture frame shows its picture on its -X side, and is single-sided: from behind it is invisible.
    const string PicturePrefix = "SM_Frames_";
    const float PictureYawOffset = 90f;

    // These have no texture set in the pack and import as flat dark grey, so the placed copies are repainted.
    static readonly Dictionary<string, string> UntexturedModelPaints = new Dictionary<string, string>
    {
        { "SM_MainTable", "Wood" }, { "SM_KitchenTable", "Wood" },
        { "SM_LampTall_01", "Brass" }, { "SM_LampTall_02", "Brass" }, { "SM_LampTall_03", "Brass" },
    };

    readonly struct Prop
    {
        public readonly string Model;
        public readonly float X;
        public readonly float Z;
        public readonly float Facing;

        public Prop(string model, float x, float z, float facing = 0f)
        {
            Model = model;
            X = x;
            Z = z;
            Facing = facing;
        }
    }

    readonly Shader toonShader;
    readonly Shader litShader;
    readonly Dictionary<string, Material> materials = new Dictionary<string, Material>();
    Transform root;

    KitchenMapBuilder(Shader toonShader, Shader litShader)
    {
        this.toonShader = toonShader;
        this.litShader = litShader;
    }

    [MenuItem("Tools/Stylized Kitchen/Build Kitchen Map")]
    public static void BuildKitchenMap()
    {
        Shader toonShader = Shader.Find(ToonShaderName);
        Shader litShader = Shader.Find(LitShaderName);
        if (toonShader == null || litShader == null)
        {
            // Fails closed: every block would render magenta without them.
            Debug.LogError($"KitchenMapBuilder: shader '{ToonShaderName}' or '{LitShaderName}' not found. " +
                           "Check the Console for shader compile errors and that URP is the active render pipeline.");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;

        bool isFirstBuild = AssetDatabase.LoadAssetAtPath<SceneAsset>(KitchenScenePath) == null;
        if (isFirstBuild && !AssetDatabase.CopyAsset(SourceScenePath, KitchenScenePath))
        {
            // Fails closed: building into whatever scene happens to be open could wreck the player's work.
            Debug.LogError($"KitchenMapBuilder: could not copy {SourceScenePath} to {KitchenScenePath}.");
            return;
        }

        Scene scene = EditorSceneManager.OpenScene(KitchenScenePath, OpenSceneMode.Single);
        DestroyRootObjects(scene, MapRootName);
        new KitchenMapBuilder(toonShader, litShader).Build();
        AimSun(scene);
        if (isFirstBuild)
            SetUpCopiedScene(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"KitchenMapBuilder: house built in {KitchenScenePath}.");
    }

    // Only on the first build, so the player can move the fly afterwards.
    static void SetUpCopiedScene(Scene scene)
    {
        DestroyRootObjects(scene, OldGroundName);
        foreach (GameObject sceneRoot in scene.GetRootGameObjects())
        {
            FlyController fly = sceneRoot.GetComponentInChildren<FlyController>(true);
            if (fly != null)
                fly.transform.position = FlySpawn * WorldScale;
        }
    }

    // The scene's directional light is the toon shader's main light. The ceiling casts no shadow
    // (see Build), so the sun reaches every room; walls and furniture still cast shadows, which the
    // URP assets' shadow distance (130 m) covers across the whole 120 m house.
    static void AimSun(Scene scene)
    {
        foreach (GameObject sceneRoot in scene.GetRootGameObjects())
        {
            foreach (Light light in sceneRoot.GetComponentsInChildren<Light>(true))
            {
                if (light.type != LightType.Directional)
                    continue;
                light.enabled = true;
                light.shadows = LightShadows.Soft;
                light.transform.rotation = Quaternion.Euler(SunAngles);
            }
        }
    }

    static void DestroyRootObjects(Scene scene, string name)
    {
        foreach (GameObject sceneRoot in scene.GetRootGameObjects())
        {
            if (sceneRoot.name == name)
                Object.DestroyImmediate(sceneRoot);
        }
    }

    void Build()
    {
        root = new GameObject(MapRootName).transform;
        // Must stay at the origin, unrotated: LocalBounds converts world bounds to layout metres by
        // dividing by WorldScale, and room groups are plain groups so their local space is the root's.
        root.localScale = Vector3.one * WorldScale;
        SetIndoorAmbientLight();

        GameObject ceiling = Box(Group(root, "Shell"), "Ceiling", new Vector3(KitchenWest - WallThickness, RoomHeight, -HalfDepth - WallThickness),
            new Vector3(LivingEast + WallThickness, RoomHeight + WallThickness, HalfDepth + WallThickness), PaintSurface("Ceiling", new Color(0.96f, 0.95f, 0.92f)));
        // A roof shadow would put the whole interior in the toon shader's shadow band; letting the sun
        // through is the usual stylized-interior cheat, and nobody sees the ceiling from above.
        ceiling.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        BuildKitchen(Group(root, "Kitchen"));
        BuildLivingRoom(Group(root, "LivingRoom"));
    }

    // A warm ceiling-to-floor gradient reads as light bouncing around a room and keeps the sides of
    // objects facing away from the sun readable.
    static void SetIndoorAmbientLight()
    {
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = AmbientSky;
        RenderSettings.ambientEquatorColor = AmbientEquator;
        RenderSettings.ambientGroundColor = AmbientGround;
    }

    // One room's half of the partition: the wall around the doorway plus the trim on its own face.
    // faceX is the side the room sees, backX the middle of the partition.
    void PartitionHalf(Transform parent, float faceX, float backX, Material wall)
    {
        float x0 = Mathf.Min(faceX, backX), x1 = Mathf.Max(faceX, backX);
        Box(parent, "Partition_South", new Vector3(x0, 0f, -HalfDepth), new Vector3(x1, RoomHeight, DoorwaySouth), wall);
        Box(parent, "Partition_North", new Vector3(x0, 0f, DoorwayNorth), new Vector3(x1, RoomHeight, HalfDepth), wall);
        Box(parent, "Partition_Lintel", new Vector3(x0, DoorwayHeight, DoorwaySouth), new Vector3(x1, RoomHeight, DoorwayNorth), wall);

        Material trim = PaintSurface("Trim", TrimWhite, 0.4f);
        float out0 = Mathf.Min(faceX, faceX + Mathf.Sign(faceX - backX) * TrimDepth);
        float out1 = Mathf.Max(faceX, faceX + Mathf.Sign(faceX - backX) * TrimDepth);
        float top = DoorwayHeight + TrimWidth;
        Box(parent, "DoorTrim_South", new Vector3(out0, 0f, DoorwaySouth - TrimWidth), new Vector3(out1, top, DoorwaySouth), trim);
        Box(parent, "DoorTrim_North", new Vector3(out0, 0f, DoorwayNorth), new Vector3(out1, top, DoorwayNorth + TrimWidth), trim);
        Box(parent, "DoorTrim_Head", new Vector3(out0, DoorwayHeight, DoorwaySouth - TrimWidth), new Vector3(out1, top, DoorwayNorth + TrimWidth), trim);
    }

    // An outer wall with a window, built in its own frame: local x runs along the wall from 0 to
    // length, local z = 0 is the room-side face and +z points outside. The glass is an invisible
    // collider, so the fly cannot leave into the empty world outside.
    void BuildWindowWall(Transform frame, float length, float windowCenter, Material wall)
    {
        float left = windowCenter - WindowHalfWidth, right = windowCenter + WindowHalfWidth, t = WallThickness;
        Box(frame, "Wall_Left", new Vector3(0f, 0f, 0f), new Vector3(left, RoomHeight, t), wall);
        Box(frame, "Wall_Right", new Vector3(right, 0f, 0f), new Vector3(length, RoomHeight, t), wall);
        Box(frame, "Wall_BelowWindow", new Vector3(left, 0f, 0f), new Vector3(right, SillHeight, t), wall);
        Box(frame, "Wall_AboveWindow", new Vector3(left, WindowTop, 0f), new Vector3(right, RoomHeight, t), wall);

        Material trim = PaintSurface("Trim", TrimWhite, 0.4f);
        Box(frame, "WindowSill", new Vector3(left - 0.08f, SillHeight - 0.03f, -SillDepth), new Vector3(right + 0.08f, SillHeight, t), trim);
        Box(frame, "WindowTrim_Left", new Vector3(left - TrimWidth, SillHeight, -TrimDepth), new Vector3(left, WindowTop + TrimWidth, 0f), trim);
        Box(frame, "WindowTrim_Right", new Vector3(right, SillHeight, -TrimDepth), new Vector3(right + TrimWidth, WindowTop + TrimWidth, 0f), trim);
        Box(frame, "WindowTrim_Head", new Vector3(left - TrimWidth, WindowTop, -TrimDepth), new Vector3(right + TrimWidth, WindowTop + TrimWidth, 0f), trim);
        Box(frame, "WindowMullion", new Vector3(windowCenter - 0.02f, SillHeight, t * 0.3f), new Vector3(windowCenter + 0.02f, WindowTop, t * 0.7f), trim);
        Box(frame, "WindowTransom", new Vector3(left, 1.62f, t * 0.3f), new Vector3(right, 1.66f, t * 0.7f), trim);
        GameObject glass = Box(frame, "WindowGlass", new Vector3(left, SillHeight, 0f), new Vector3(right, WindowTop, t), trim);
        Object.DestroyImmediate(glass.GetComponent<MeshRenderer>());
    }

    // A wall frame placed at origin with the given compass yaw (see BuildWindowWall).
    static Transform WallFrame(Transform parent, string name, Vector3 origin, float yaw)
    {
        Transform frame = Group(parent, name);
        frame.localPosition = origin;
        frame.localRotation = Quaternion.Euler(0f, yaw, 0f);
        return frame;
    }

    // A baseboard along a wall face from start to end (floor level), on the left of the walking
    // direction, which is the room side when each room walks its walls counter-clockwise seen from
    // above with north up (south wall eastwards, east wall northwards, and so on).
    void Baseboard(Transform parent, Vector3 start, Vector3 end)
    {
        Vector3 direction = (end - start).normalized;
        Vector3 left = new Vector3(-direction.z, 0f, direction.x) * BaseboardDepth;
        Vector3 min = Vector3.Min(start, end + left);
        Vector3 max = Vector3.Max(start, end + left) + Vector3.up * BaseboardHeight;
        Box(parent, "Baseboard", min, max, PaintSurface("Trim", TrimWhite, 0.4f));
    }

    // Stands a picture frame's back on a wall. wallPoint is on the wall face, at the frame's bottom edge.
    void HangPicture(Transform parent, string model, Vector3 wallPoint, float facing)
    {
        Vector3 outward = Quaternion.Euler(0f, facing, 0f) * Vector3.forward;
        PlaceModel(parent, model, wallPoint + outward * PictureWallGap, facing);
    }

    void PlaceProps(Transform parent, Prop[] props, float surfaceHeight)
    {
        foreach (Prop prop in props)
            PlaceModel(parent, prop.Model, new Vector3(prop.X, surfaceHeight, prop.Z), prop.Facing);
    }

    // Piles models on top of each other from spot upwards, each slightly turned so a pile of books
    // looks stacked by hand. Returns the height of the top of the pile.
    float PlaceStack(Transform parent, string[] models, Vector3 spot, float facing)
    {
        float[] twists = { 0f, 7f, -5f, 4f, -8f, 3f };
        float height = spot.y;
        for (int i = 0; i < models.Length; i++)
        {
            GameObject placed = PlaceModel(parent, models[i], new Vector3(spot.x, height, spot.z), facing + twists[i % twists.Length]);
            if (placed != null)
                height = LocalBounds(placed).max.y;
        }
        return height;
    }

    // Puts the model's pivot on spot's x/z and stands its mesh on spot's y. The pivot is used
    // sideways because the artist put it where the object belongs (a pan's pivot is its centre, not
    // halfway along its handle); the mesh is used vertically because a few pivots sit off the base.
    GameObject PlaceModel(Transform parent, string modelName, Vector3 spot, float facing)
    {
        GameObject model = LoadModel(modelName);
        if (model == null)
        {
            // Fails open: one missing prop should not stop the rest of the house from being built.
            Debug.LogWarning($"KitchenMapBuilder: {MeshesFolder}/{modelName}.FBX not found; skipped.");
            return null;
        }
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
        Transform t = instance.transform;
        t.localRotation = Quaternion.Euler(0f, facing + FrontYawOffset(modelName), 0f) * model.transform.localRotation;
        t.localPosition = new Vector3(spot.x, 0f, spot.z);
        t.localPosition += Vector3.up * (spot.y - LocalBounds(instance).min.y);
        RepaintIfUntextured(instance, modelName);
        AddCollidersIfMissing(instance);
        return instance;
    }

    static float FrontYawOffset(string modelName)
    {
        if (modelName.StartsWith(PicturePrefix))
            return PictureYawOffset;
        return FrontYawOffsets.TryGetValue(modelName, out float offset) ? offset : 0f;
    }

    void RepaintIfUntextured(GameObject instance, string modelName)
    {
        if (!UntexturedModelPaints.TryGetValue(modelName, out string paintName))
            return;
        Material paint = paintName == "Wood"
            ? Paint("Wood", new Color(0.58f, 0.4f, 0.26f), 0.3f)
            : Paint("Brass", new Color(0.8f, 0.64f, 0.4f), 0.6f);
        foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>())
        {
            var slots = new Material[renderer.sharedMaterials.Length];
            for (int i = 0; i < slots.Length; i++)
                slots[i] = paint;
            renderer.sharedMaterials = slots;
        }
    }

    // Most of the pack's FBX files carry UCX_ collision hulls (made into colliders by
    // KitchenCollisionImporter); the rest get an exact, non-convex collider per mesh, which is fine
    // for static props and lets the fly land on them.
    static void AddCollidersIfMissing(GameObject instance)
    {
        if (instance.GetComponentInChildren<Collider>() != null)
            return;
        foreach (MeshFilter filter in instance.GetComponentsInChildren<MeshFilter>())
            filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
    }

    static GameObject LoadModel(string modelName) =>
        AssetDatabase.LoadAssetAtPath<GameObject>($"{MeshesFolder}/{modelName}.FBX");

    static Bounds LocalBounds(GameObject instance)
    {
        Renderer[] renderers = instance.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
            return new Bounds(instance.transform.position / WorldScale, Vector3.zero);
        Bounds bounds = renderers[0].bounds;
        foreach (Renderer renderer in renderers)
            bounds.Encapsulate(renderer.bounds);
        return new Bounds(bounds.center / WorldScale, bounds.size / WorldScale);
    }

    static Transform Group(Transform parent, string name)
    {
        var group = new GameObject(name).transform;
        group.SetParent(parent, false);
        return group;
    }

    // Five parameters (over the usual four): a block needs its name, both corners and a material, and
    // bundling them into an object would only move the same values one call further away.
    static GameObject Box(Transform parent, string name, Vector3 min, Vector3 max, Material material)
    {
        GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
        box.name = name;
        box.transform.SetParent(parent, false);
        box.transform.localPosition = (min + max) * 0.5f;
        box.transform.localScale = max - min;
        box.GetComponent<MeshRenderer>().sharedMaterial = material;
        return box;
    }

    // size is (diameter, height, diameter). Five parameters for the same reason as Box.
    static void Cylinder(Transform parent, string name, Vector3 bottomCenter, Vector3 size, Material material)
    {
        GameObject cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        cylinder.name = name;
        cylinder.transform.SetParent(parent, false);
        // Unity's cylinder mesh is 2 units tall and centred on its pivot.
        cylinder.transform.localPosition = bottomCenter + Vector3.up * (size.y * 0.5f);
        cylinder.transform.localScale = new Vector3(size.x, size.y * 0.5f, size.z);
        // Its capsule collider would round off the flat ends; a box fits a burner or a shade better.
        Object.DestroyImmediate(cylinder.GetComponent<CapsuleCollider>());
        cylinder.AddComponent<BoxCollider>();
        cylinder.GetComponent<MeshRenderer>().sharedMaterial = material;
    }

    static Light NewLight(Transform parent, string name, Vector3 position, LightType type)
    {
        var light = new GameObject(name).AddComponent<Light>();
        light.transform.SetParent(parent, false);
        light.transform.localPosition = position;
        light.type = type;
        light.color = WarmLight;
        return light;
    }

    // A toon material for furniture and fittings, outlined with the shader's default width. The toon
    // shader reads smoothness from an ORM map (R occlusion, G roughness, B metallic), so each colour
    // gets a 1x1 one; without it the shader's white default means fully rough and fully metallic.
    Material Paint(string name, Color color, float smoothness = 0.2f)
    {
        if (materials.TryGetValue(name, out Material material))
            return material;
        material = new Material(toonShader) { name = "Blockout_" + name };
        material.SetColor("_BaseColor", color);
        material.SetTexture("_ORMMap", SolidTexture(name + "_ORM", new Color(1f, 1f - smoothness, 0f)));
        materials[name] = material;
        return material;
    }

    // Walls, floors, ceiling and trim: big flat surfaces look heavy with an outline, so theirs is off.
    Material PaintSurface(string name, Color color, float smoothness = 0.2f)
    {
        Material material = Paint(name, color, smoothness);
        material.SetFloat("_OutlineWidth", 0f);
        return material;
    }

    // A lamp shade or light fitting that looks switched on. URP Lit, because the toon shader has no emission.
    Material PaintGlow(string name, Color color)
    {
        if (materials.TryGetValue(name, out Material material))
            return material;
        material = new Material(litShader) { name = "Blockout_" + name };
        material.SetColor("_BaseColor", color);
        material.SetFloat("_Smoothness", 0.3f);
        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", color * 0.8f);
        materials[name] = material;
        return material;
    }

    // A floor pattern from a tiny pixel grid repeated across the surface: the top face of a box is
    // one UV square, so tiling is how many times the grid fits along the floor.
    Material PaintPattern(string name, Color[] pixels, int width, Vector2 tiling)
    {
        var texture = new Texture2D(width, pixels.Length / width, TextureFormat.RGBA32, false)
        {
            name = "Blockout_" + name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Repeat,
        };
        texture.SetPixels(pixels);
        texture.Apply();
        Material material = PaintSurface(name, Color.white, 0.35f);
        material.SetTexture("_BaseMap", texture);
        material.SetTextureScale("_BaseMap", tiling);
        return material;
    }

    // ORM data is linear, not a colour, so the texture skips sRGB conversion.
    static Texture2D SolidTexture(string name, Color value)
    {
        var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false, true) { name = "Blockout_" + name };
        texture.SetPixel(0, 0, value);
        texture.Apply();
        return texture;
    }
}
