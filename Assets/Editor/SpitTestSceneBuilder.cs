using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Builds a standalone range for trying the spit out, into Assets/Scenes/SpitTest.unity
// (Tools > Fly Spit > Build Spit Test Scene).
// The first run copies Assets/scene.unity, the same trick KitchenMapBuilder uses, so the fly, its
// animator, the orbit camera and the wiring between them come over already working instead of being
// rebuilt here and drifting out of sync with the real scene.
// Later runs only rebuild the SpitRange root and the fly's Muzzle, so anything else moved or added in
// the test scene is kept. The spit prefabs under Assets/VFX/Spit are always rebuilt; the materials
// there are created once and then left alone, so colour tweaks survive (see
// SpitTestSceneBuilder.Vfx.cs, which holds the VFX half of this builder).
// Layout is measured from wherever the fly happens to be standing, in its own forward direction, so
// moving the fly in the scene and rebuilding moves the whole range with it.
// Range props are blockout: coloured primitives, with their materials saved inside the scene rather
// than as assets, which is how the kitchen blockout does it too.
public partial class SpitTestSceneBuilder
{
    const string SourceScenePath = "Assets/scene.unity";
    const string SpitScenePath = "Assets/Scenes/SpitTest.unity";
    const string VfxFolder = "Assets/VFX/Spit";
    const string ToonShaderName = "FlySimulator/StylizedToon";
    const string ToonParticleShaderName = "FlySimulator/StylizedToonParticle";
    const string RangeRootName = "SpitRange";
    const string MuzzleName = "Muzzle";
    const string MuzzleBurstName = "MuzzleBurst";

    // Mouth position on the fly, in the fly root's own space: +Z is its nose, and its collider is
    // 0.4 units across, so 0.18 forward sits just in front of the head.
    static readonly Vector3 MuzzleOffset = new Vector3(0f, 0.02f, 0.18f);

    // Range layout, in units, measured from the fly: across is to its right, ahead is its forward,
    // and height is above the floor (y = 0), not above the fly.
    const float GroundCansAhead = 2.8f;
    const float ShelfAhead = 5f;
    const float BackWallAhead = 7.5f;
    const float ShelfHeight = 0.6f;
    // Spread of the five fixed cans, and of the three loose ones on the shelf.
    static readonly float[] GroundCanOffsets = { -1.6f, -0.8f, 0f, 0.8f, 1.6f };
    static readonly float[] ShelfCanOffsets = { -0.9f, 0f, 0.9f };

    static readonly Color WallColor = new Color(0.82f, 0.78f, 0.70f);
    static readonly Color ShelfColor = new Color(0.55f, 0.40f, 0.28f);
    static readonly Color FixedCanColor = new Color(0.85f, 0.35f, 0.30f);
    static readonly Color LooseCanColor = new Color(0.35f, 0.58f, 0.82f);

    [MenuItem("Tools/Fly Spit/Build Spit Test Scene")]
    static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            return;
        if (!EnsureSceneExists())
            return;

        Scene scene = EditorSceneManager.OpenScene(SpitScenePath, OpenSceneMode.Single);

        // After the scene is open, so the throwaway objects the prefab builders make land in the test
        // scene and not in whatever the user had open.
        SpitAssets assets = BuildVfxAssets();

        FlyController fly = Object.FindAnyObjectByType<FlyController>();
        if (fly == null)
        {
            Debug.LogError($"SpitTestSceneBuilder: no FlyController in {SpitScenePath}; nothing to give a weapon to.");
            return;
        }

        BuildRange(fly.transform);
        WireFly(fly, assets);

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log($"Spit test scene built: {SpitScenePath}. Press Play, then left click to spit.");
    }

    static bool EnsureSceneExists()
    {
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SpitScenePath) != null)
            return true;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SourceScenePath) == null)
        {
            Debug.LogError($"SpitTestSceneBuilder: {SourceScenePath} is missing, so there is no fly rig to copy.");
            return false;
        }
        if (!AssetDatabase.CopyAsset(SourceScenePath, SpitScenePath))
        {
            Debug.LogError($"SpitTestSceneBuilder: could not copy {SourceScenePath} to {SpitScenePath}.");
            return false;
        }
        AssetDatabase.Refresh();
        return true;
    }

    // ---- The range -------------------------------------------------------------------------------

    static void BuildRange(Transform fly)
    {
        GameObject existing = GameObject.Find(RangeRootName);
        if (existing != null)
            Object.DestroyImmediate(existing);

        GameObject root = new GameObject(RangeRootName);
        // Flattened: the range is laid out on the floor, and a fly that happens to be pitched when
        // the scene was saved should not tilt the whole thing.
        Vector3 ahead = Vector3.ProjectOnPlane(fly.forward, Vector3.up);
        ahead = ahead.sqrMagnitude > 1e-6f ? ahead.normalized : Vector3.forward;
        Vector3 across = Vector3.Cross(Vector3.up, ahead);
        Vector3 origin = new Vector3(fly.position.x, 0f, fly.position.z);
        Quaternion facing = Quaternion.LookRotation(ahead, Vector3.up);

        Material wall = BlockoutMaterial("SpitRange Wall", WallColor);
        Material shelf = BlockoutMaterial("SpitRange Shelf", ShelfColor);
        Material fixedCan = BlockoutMaterial("SpitRange Fixed Can", FixedCanColor);
        Material looseCan = BlockoutMaterial("SpitRange Loose Can", LooseCanColor);

        // Something for the spit to stain. Shots aimed level fall short of it, which is the clearest
        // way to show that the blob travels in an arc.
        Box(root, "BackWall", Point(origin, across, ahead, 0f, 1.1f, BackWallAhead), facing,
            new Vector3(4f, 2.2f, 0.25f), wall);

        // Bolted down: these rock and spring back, and drop out after three hits.
        for (int i = 0; i < GroundCanOffsets.Length; i++)
        {
            GameObject can = Cylinder(root, $"FixedCan{i}",
                Point(origin, across, ahead, GroundCanOffsets[i], 0.14f, GroundCansAhead), facing,
                new Vector3(0.1f, 0.14f, 0.1f), fixedCan);
            can.AddComponent<SpitTarget>();
        }

        Box(root, "Shelf", Point(origin, across, ahead, 0f, ShelfHeight, ShelfAhead), facing,
            new Vector3(2.6f, 0.1f, 0.5f), shelf);

        // Loose: a Rigidbody, so SpitTarget pushes these with physics and they fall off the shelf
        // instead of rocking in place. hitsToBreak is left at its default, so they also respawn.
        for (int i = 0; i < ShelfCanOffsets.Length; i++)
        {
            GameObject can = Cylinder(root, $"LooseCan{i}",
                Point(origin, across, ahead, ShelfCanOffsets[i], ShelfHeight + 0.15f, ShelfAhead), facing,
                new Vector3(0.08f, 0.1f, 0.08f), looseCan);
            Rigidbody body = can.AddComponent<Rigidbody>();
            // Light enough that the roughly 1 unit impulse of a square-on hit knocks it over.
            body.mass = 0.4f;
            can.AddComponent<SpitTarget>();
        }
    }

    static Vector3 Point(Vector3 origin, Vector3 across, Vector3 ahead, float sideways, float height, float forward)
    {
        return origin + across * sideways + ahead * forward + Vector3.up * height;
    }

    // ---- The weapon ------------------------------------------------------------------------------

    static void WireFly(FlyController fly, SpitAssets assets)
    {
        FlySpit spit = fly.GetComponent<FlySpit>();
        if (spit == null)
            spit = fly.gameObject.AddComponent<FlySpit>();

        Transform muzzle = RebuildMuzzle(fly.transform, assets);
        Animator animator = fly.GetComponentInChildren<Animator>();
        // Camera.main first: the fly should aim down the camera the player actually looks through.
        Camera camera = Camera.main != null ? Camera.main : Object.FindAnyObjectByType<Camera>();

        // SerializedObject because the fields are private and serialized, which is how they should
        // stay: nothing outside the component has any business setting them at runtime.
        SerializedObject serialized = new SerializedObject(spit);
        serialized.FindProperty("model").objectReferenceValue = animator != null ? animator.transform : null;
        serialized.FindProperty("muzzle").objectReferenceValue = muzzle;
        serialized.FindProperty("muzzleOffset").vector3Value = MuzzleOffset;
        serialized.FindProperty("projectilePrefab").objectReferenceValue = assets.ProjectilePrefab;
        serialized.FindProperty("muzzleBurst").objectReferenceValue = muzzle.GetComponentInChildren<ParticleSystem>();
        serialized.FindProperty("cameraTransform").objectReferenceValue = camera != null ? camera.transform : null;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        if (animator == null)
            Debug.LogWarning("SpitTestSceneBuilder: the fly has no Animator child, so FlySpit has no model to lunge.");
    }

    // The muzzle is rebuilt rather than reused, so changing MuzzleOffset above and rebuilding moves
    // the mouth instead of leaving the old one behind.
    static Transform RebuildMuzzle(Transform fly, SpitAssets assets)
    {
        Transform existing = fly.Find(MuzzleName);
        if (existing != null)
            Object.DestroyImmediate(existing.gameObject);

        GameObject muzzle = new GameObject(MuzzleName);
        muzzle.transform.SetParent(fly, worldPositionStays: false);
        muzzle.transform.localPosition = MuzzleOffset;
        // Identity rotation, so the burst's cone sprays along the fly's own +Z: its nose.
        muzzle.transform.localRotation = Quaternion.identity;

        GameObject burst = new GameObject(MuzzleBurstName);
        burst.transform.SetParent(muzzle.transform, worldPositionStays: false);
        BuildMuzzleBurst(burst, assets.Droplet);
        return muzzle.transform;
    }

    // ---- Blockout helpers ------------------------------------------------------------------------

    // Not saved as an asset: the material is only referenced by scene objects, so Unity serialises it
    // into SpitTest.unity. Same as the kitchen blockout, and it keeps the project folders clean.
    static Material BlockoutMaterial(string name, Color color)
    {
        Shader shader = Shader.Find(ToonShaderName);
        Material material = new Material(shader) { name = name };
        material.SetColor("_BaseColor", color);
        return material;
    }

    static GameObject Box(GameObject parent, string name, Vector3 position, Quaternion rotation, Vector3 size, Material material)
    {
        return Primitive(PrimitiveType.Cube, parent, name, position, rotation, size, material);
    }

    // Unity's cylinder primitive is 2 units tall, so the scale's y is the can's half-height. The
    // positions above are written as centres, which is where a primitive's pivot is.
    static GameObject Cylinder(GameObject parent, string name, Vector3 position, Quaternion rotation, Vector3 size, Material material)
    {
        return Primitive(PrimitiveType.Cylinder, parent, name, position, rotation, size, material);
    }

    static GameObject Primitive(PrimitiveType type, GameObject parent, string name, Vector3 position,
        Quaternion rotation, Vector3 size, Material material)
    {
        GameObject primitive = GameObject.CreatePrimitive(type);
        primitive.name = name;
        primitive.transform.SetParent(parent.transform, worldPositionStays: true);
        primitive.transform.SetPositionAndRotation(position, rotation);
        primitive.transform.localScale = size;
        primitive.GetComponent<MeshRenderer>().sharedMaterial = material;
        return primitive;
    }
}
