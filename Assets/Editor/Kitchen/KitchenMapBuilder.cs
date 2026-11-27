using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Builds the flyable house - a dining hub along the south with a kitchen, a pantry and a living room
// side by side to its north - into Assets/Scenes/Kitchen.unity (Tools > Stylized Kitchen > Build
// Kitchen Map), or into any open scene through BuildHouseInto.
// It is laid out for a fair match: the old man starts in the middle of the hub at about the same
// walk from all three dishes, every north room has a doorway from the hub and one from its
// neighbour so nobody is cornered at a single choke point, the dishes stand on free-standing tables
// he can walk round and guard, and the flies start high in the corners, away from the food.
// Anything that only exists as an Unreal .uasset is a coloured block; every prop with an FBX is the
// real model, except the pack's big tables, couch and two of its floor lamps, whose collision hulls
// are solid boxes far bigger than what you see, so a player would bump into thin air: those pieces
// of furniture are blocks too, so what you see is what you hit.
// The first run copies scene.unity, so the fly, the camera and their wiring come over.
// Later runs only rebuild the KitchenMap root, the ambient light and the sun, so other edits are kept.
// Everything is shaded with FlySimulator/StylizedToon, which takes its bands, highlight and rim from
// the sun, so the sun is on and the ceiling lets it in (see AimSun).
// This file holds the entry points and the placing helpers; the shell, the paints, the markers and
// each room have their own files.
// Layout numbers are in real-world metres, scaled by WorldScale on the KitchenMap root.
public partial class KitchenMapBuilder
{
    const string SourceScenePath = "Assets/scene.unity";
    const string KitchenScenePath = "Assets/Scenes/Kitchen.unity";
    // Same folder as KitchenMaterialBuilder.MeshesFolder.
    const string MeshesFolder = "Assets/models/env/Content/StylizedKitchen/Meshes";
    const string MapRootName = "KitchenMap";
    // The outdoor ground, made by hand under the KitchenMap root after a build; see KeepTerrain.
    const string TerrainName = "Terrain";
    // The ground plane in scene.unity; the house floor replaces it.
    const string OldGroundName = "Plane";
    // Every prop the old man may pick up and throw is a direct child of a group with this name; the
    // match pipeline makes each of those children a physics object. Anything outside one stays put.
    const string ThrowablesGroupName = "Throwables";
    const string ToonShaderName = "FlySimulator/StylizedToon";
    // Only for glowing lamp shades: the toon shader has no emission.
    const string LitShaderName = "Universal Render Pipeline/Lit";
    // Units per real-world metre: the whole house is blown up by this, so the fly - 2 units across,
    // and not resized with it - has room to fly in a kitchen that is only metres wide in its own
    // terms. The FBX files are in centimetres and Unity already imports them at real size.
    // Everything sized in house metres is scaled by it, the old man included; the fly is the one
    // thing that keeps its own size, which is what gives it the extra room.
    // Tools > Stylized Kitchen > Resize House To World Scale applies a change here to a built scene
    // without rebuilding it.
    internal const float WorldScale = 45f;

    // 4 m, not a real kitchen's 2.6 m: the old man's slap reaches about 2.72 m looking up, so under
    // a 2.6 m ceiling a fly had nowhere out of his reach. Tools > Stylized Kitchen > Raise Ceiling To
    // Room Height applies a change here to a built scene without rebuilding it.
    internal const float RoomHeight = 4.0f;
    // Picture frames are 2 cm thick with their pivot in the middle; this keeps the back on the wall.
    const float PictureWallGap = 0.011f;
    static readonly Color WarmLight = new Color(1f, 0.85f, 0.65f);
    // High and from the south-south-east: the counters, the dining table and the sofa all face the
    // sun through the south windows, and the 4 m walls cast shadows only about 2.3 m long.
    static readonly Vector3 SunAngles = new Vector3(60f, -30f, 0f);
    // The toon shader adds ambient on top of the sun, so it is kept low to avoid washing out the bands.
    static readonly Color AmbientSky = new Color(0.46f, 0.44f, 0.41f);
    static readonly Color AmbientEquator = new Color(0.36f, 0.33f, 0.3f);
    static readonly Color AmbientGround = new Color(0.22f, 0.2f, 0.18f);
    static readonly Color TrimWhite = new Color(0.97f, 0.96f, 0.93f);

    // Facings are compass yaws for a model's front: 0 = looks north (+Z), 90 = east, 180 = south, -90 = west.
    // The models placed here all have their fronts on +Z after import, except picture frames: every
    // one shows its picture on its -X side, and is single-sided: from behind it is invisible.
    const string PicturePrefix = "SM_Frames_";
    const float PictureYawOffset = 90f;

    // These have no texture set in the pack and import as flat dark grey, so the placed copies are repainted.
    static readonly Dictionary<string, string> UntexturedModelPaints = new Dictionary<string, string>
    {
        { "SM_LampTall_03", "Brass" },
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
        // Checked before the first build copies a scene, so a broken shader leaves nothing behind.
        if (!TryFindShaders(out _, out _))
            return;
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
        if (!BuildHouseInto(scene))
            return;
        if (isFirstBuild)
            SetUpCopiedScene(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"KitchenMapBuilder: house built in {KitchenScenePath}.");
    }

    // Replaces the house in a scene that is already open, for callers that build more than one scene.
    // It neither opens, closes nor saves anything: that is the caller's job. Returns false, having
    // changed nothing, when the scene is not loaded or the shaders are missing.
    internal static bool BuildHouseInto(Scene scene)
    {
        if (!scene.IsValid() || !scene.isLoaded)
        {
            // Fails closed: there is nothing to build into, and building into the active scene instead
            // could wreck a scene nobody asked to change.
            Debug.LogError($"KitchenMapBuilder: scene '{scene.name}' is not open; the house was not built.");
            return false;
        }
        if (!TryFindShaders(out Shader toonShader, out Shader litShader))
            return false;

        // New objects and RenderSettings (the ambient light) both go to the active scene.
        SceneManager.SetActiveScene(scene);
        KeepTerrain(scene);
        FlattenTerrainUnderHouse(scene);
        DestroyRootObjects(scene, MapRootName);
        new KitchenMapBuilder(toonShader, litShader).Build();
        AimSun(scene);
        return true;
    }

    static bool TryFindShaders(out Shader toonShader, out Shader litShader)
    {
        toonShader = Shader.Find(ToonShaderName);
        litShader = Shader.Find(LitShaderName);
        if (toonShader != null && litShader != null)
            return true;
        // Fails closed: every block would render magenta without them.
        Debug.LogError($"KitchenMapBuilder: shader '{ToonShaderName}' or '{LitShaderName}' not found. " +
                       "Check the Console for shader compile errors and that URP is the active render pipeline.");
        return false;
    }

    // The outdoor ground was made by hand inside the KitchenMap root, so throwing the old root away
    // would delete it with the house. It is lifted out to the scene root first, keeping its world
    // position, rotation and size; from then on rebuilds never touch it.
    static void KeepTerrain(Scene scene)
    {
        foreach (GameObject sceneRoot in scene.GetRootGameObjects())
        {
            if (sceneRoot.name != MapRootName)
                continue;
            Transform terrain;
            while ((terrain = sceneRoot.transform.Find(TerrainName)) != null)
                terrain.SetParent(null, true);
        }
    }

    // Only on the first build, so the player can move the fly afterwards. The fly starts where the
    // first fly spawn marker is (see BuildMarkers).
    static void SetUpCopiedScene(Scene scene)
    {
        DestroyRootObjects(scene, OldGroundName);
        foreach (GameObject sceneRoot in scene.GetRootGameObjects())
        {
            FlyController fly = sceneRoot.GetComponentInChildren<FlyController>(true);
            if (fly != null)
                fly.transform.position = FirstFlySpawn * WorldScale;
        }
    }

    // The scene's directional light is the toon shader's main light. The ceiling casts no shadow (see
    // BuildShell), so the sun reaches every room; walls and furniture still do.
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

        BuildShell(Group(root, "Shell"));
        BuildHub(Group(root, "Hub"));
        BuildKitchen(Group(root, "Kitchen"));
        BuildPantry(Group(root, "Pantry"));
        BuildLivingRoom(Group(root, "LivingRoom"));
        BuildMarkers(Group(root, "Markers"));
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

    // Puts the model's pivot on spot's x/z and stands its mesh on spot's y: the artist's pivot is
    // where the object belongs sideways, but a few of them sit off the base vertically.
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

    static float FrontYawOffset(string modelName) =>
        modelName.StartsWith(PicturePrefix) ? PictureYawOffset : 0f;

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

    // Most of the pack's FBX files carry UCX_ collision hulls (KitchenCollisionImporter turns them
    // into colliders); the rest get an exact non-convex collider per mesh, which the fly can land on.
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
        // Only the sun casts shadows: each shadowed point light renders six extra shadow maps, and
        // with eight lamps in the house that would cost more than the soft fill they add is worth.
        light.shadows = LightShadows.None;
        return light;
    }
}
