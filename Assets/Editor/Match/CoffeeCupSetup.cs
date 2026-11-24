using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

// Stands a mug of coffee on the living room's coffee table (Tools > Match > Place Coffee In Living
// Room), so the flies have a second thing to drink and the old man a second thing to defend.
// A menu step rather than a hand-edited scene because everything about where the mug goes is
// measured rather than typed: the table's top, the mug's own footprint and where the props already on
// the table are. The cup_coffie meshes are modelled at 1 unit = 1 metre and the house is built at 45
// units to the metre, so the one number that would be guessed wrong by hand is the scale.
// Run it with Kitchen open, then again with Kitchen_online open, and run Tools > Match > Wire Match
// afterwards: that is what turns the mug into a networked dish. Running it again is safe - it finds
// the mug it stood there before and moves it rather than making a second one.
// Note that the mug lives under KitchenMap, which Tools > Stylized Kitchen > Build Kitchen Map
// rebuilds from scratch; after a rebuild of the house, run this again.
public static class CoffeeCupSetup
{
    const string MugModelPath = "Assets/cup_coffie/Meshes/FlutedGlassMug.obj";
    const string LiquidModelPath = "Assets/cup_coffie/Meshes/CoffeeLiquid.obj";
    const string CoffeeAlbedoPath = "Assets/cup_coffie/Textures/Coffee_Crema_Albedo.png";
    const string CoffeeNormalPath = "Assets/cup_coffie/Textures/Coffee_Crema_Normal.png";
    // Next to the meshes they belong to, so the mug travels as one folder.
    const string GlassMaterialPath = "Assets/cup_coffie/Meshes/CoffeeMug_Glass.mat";
    const string CoffeeMaterialPath = "Assets/cup_coffie/Meshes/CoffeeMug_Coffee.mat";
    const string LitShaderName = "Universal Render Pipeline/Lit";

    // The house, as KitchenMapBuilder leaves it: KitchenMap > LivingRoom > Seating holds the carpet,
    // the coffee table and the props standing on it.
    const string MapRootName = "KitchenMap";
    const string LivingRoomGroupName = "LivingRoom";
    const string SeatingGroupName = "Seating";
    const string TableName = "SM_SmallTable_01";
    const string TablePropsGroupName = "CoffeeTableProps";

    const string MugObjectName = "CoffeeMug";
    const string GlassObjectName = "FlutedGlassMug";
    const string LiquidObjectName = "CoffeeLiquid";

    // Metres, from Assets/cup_coffie/README.md: the liquid sits a centimetre up inside the glass so
    // its bottom is not coplanar with the mug's base.
    const float LiquidLift = 0.01f;
    // Metres of table top that must stay clear between the mug and whatever is nearest to it. Small,
    // because the table is only about a metre across and already has a book, a candle and crayons on it.
    const float PropClearance = 0.02f;
    // How far above and below the table's top face the surface is looked for.
    const float SurfaceProbe = 0.2f;
    // Candidates per axis across the table top. Odd, so the middle of the table is one of them.
    const int SpotGridSteps = 13;
    // The table is oval and its bounds are a rectangle, so a spot that just fits inside the bounds can
    // still hang over the curve; a quarter of the mug's own width of slack keeps it on the wood.
    const float RimSlack = 1.25f;
    // A mug is about 10 cm tall. Anything outside this is not a mug: it is the 45 units to the metre
    // trap, which places without an error either way.
    const float MinMugMetres = 0.05f;
    const float MaxMugMetres = 0.2f;

    // FoodDish measures its ranges in dish radii, and the mug's radius is under 5 cm: at the couscous's
    // 1.3 a fly would have to be standing in the coffee to drink it (the mug's radius is under 6 cm,
    // handle included, against a dish you could set it down on).
    const float MugBiteRangeScale = 2.5f;
    // A mug is a sip, not a meal: about half a minute of uninterrupted drinking, against nearly three
    // minutes for a full couscous at its default 0.6 percent a second.
    const float MugBiteRate = 3f;
    // The mug stands half a metre up on a table and FoodDish measures guarding from the old man's feet
    // to the coffee, so in mug-radii he is a long way from it even when he is leaning over the table.
    const float MugGuardRangeScale = 12f;

    [MenuItem("Tools/Match/Place Coffee In Living Room")]
    static void PlaceCoffeeFromMenu()
    {
        if (PlaceCoffeeInOpenScene())
        {
            Scene scene = SceneManager.GetActiveScene();
            Debug.Log($"Place Coffee: done. Save {scene.name} (Ctrl+S)." +
                      (scene.name == GameSceneManager.KitchenScene ? $" Now open {GameSceneManager.KitchenOnlineScene} and run it again." : ""));
        }
    }

    // The whole step, on the kitchen that is open. Marks the scene dirty but does not save it, the way
    // Wire Match does, so a hand run can be looked over first; MatchRaidSetup saves it.
    // Fails closed: anything missing stops the run rather than leaving half a mug in the living room.
    public static bool PlaceCoffeeInOpenScene()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != GameSceneManager.KitchenScene && scene.name != GameSceneManager.KitchenOnlineScene)
        {
            Debug.LogError($"Place Coffee: open {GameSceneManager.KitchenScene} or {GameSceneManager.KitchenOnlineScene} first (the open scene is '{scene.name}').");
            return false;
        }

        Shader lit = Shader.Find(LitShaderName);
        if (lit == null)
        {
            // Fails closed: the mug would be magenta, which nobody would read as coffee.
            Debug.LogError($"Place Coffee: shader '{LitShaderName}' not found. Check that URP is the active render pipeline.");
            return false;
        }
        GameObject mugModel = AssetDatabase.LoadAssetAtPath<GameObject>(MugModelPath);
        GameObject liquidModel = AssetDatabase.LoadAssetAtPath<GameObject>(LiquidModelPath);
        if (mugModel == null || liquidModel == null)
        {
            Debug.LogError($"Place Coffee: no mesh at {MugModelPath} or {LiquidModelPath}. Was the cup_coffie folder moved or renamed?");
            return false;
        }
        if (!TryFindTable(scene, out Transform seating, out GameObject table, out Transform tableProps))
        {
            return false;
        }

        Transform mug = EnsureMug(seating, mugModel, liquidModel, lit, out Transform liquid);
        if (mug == null)
        {
            return false;
        }
        if (!StandOnTable(mug, table, tableProps))
        {
            return false;
        }
        WireMugComponents(mug, liquid);

        EditorSceneManager.MarkSceneDirty(scene);
        // The mug is food but not yet the room's food: the NetworkObject and FoodDishNetwork that make
        // the server's mug the one everybody drinks from are Wire Match's job, because Netcode works out
        // a scene object's ids when those components are added through the Editor.
        Debug.Log($"Place Coffee: '{mug.name}' is on the living room table in {scene.name}. Run Tools > Match > Wire Match next - that is what makes it a dish" +
                  (scene.name == GameSceneManager.KitchenOnlineScene ? " and the one mug the whole room shares." : "."), mug);
        return true;
    }

    // ---- where the mug goes --------------------------------------------------------------------

    // The coffee table and the props on it, by the names KitchenMapBuilder gives them. Looked up by
    // name rather than by position: the house is rebuilt from layout metres, but a table that has been
    // nudged by hand afterwards is still the table the mug belongs on.
    static bool TryFindTable(Scene scene, out Transform seating, out GameObject table, out Transform tableProps)
    {
        seating = null;
        table = null;
        tableProps = null;

        GameObject map = scene.GetRootGameObjects().FirstOrDefault(root => root.name == MapRootName);
        Transform livingRoom = map != null ? map.transform.Find(LivingRoomGroupName) : null;
        seating = livingRoom != null ? livingRoom.Find(SeatingGroupName) : null;
        if (seating == null)
        {
            Debug.LogError($"Place Coffee: no {MapRootName}/{LivingRoomGroupName}/{SeatingGroupName} in {scene.name}, so there is no living room to put the mug in. " +
                           "Run Tools > Stylized Kitchen > Build Kitchen Map first.");
            return false;
        }
        Transform found = seating.Find(TableName);
        if (found == null)
        {
            Debug.LogError($"Place Coffee: no '{TableName}' under {seating.name} in {scene.name}; that is the living room's coffee table and the mug has nothing to stand on.");
            return false;
        }
        table = found.gameObject;
        // Fails open: without the props group the mug is only placed in the middle of the table rather
        // than off to one side of what is already there.
        tableProps = seating.Find(TablePropsGroupName);
        if (tableProps == null)
        {
            Debug.LogWarning($"Place Coffee: no '{TablePropsGroupName}' under {seating.name}; the mug cannot be kept clear of the props on the table.");
        }
        return true;
    }

    // Puts the mug's base on the table's measured top, off to one side of the props, with the handle
    // turned away from the middle of the table so it reads as somebody's mug rather than a centrepiece.
    static bool StandOnTable(Transform mug, GameObject table, Transform tableProps)
    {
        if (!TryWorldBounds(table, out Bounds tableBounds))
        {
            Debug.LogError($"Place Coffee: '{table.name}' has no renderers, so its top could not be measured.");
            return false;
        }
        if (!TryWorldBounds(mug.gameObject, out Bounds mugBounds))
        {
            Debug.LogError($"Place Coffee: the mug has no renderers, so its footprint could not be measured. Did {MugModelPath} import?");
            return false;
        }

        float mugRadius = Mathf.Max(mugBounds.extents.x, mugBounds.extents.z);
        Vector3 spot = PickSpotOnTop(table, tableBounds, tableProps, mugRadius);

        // x and z first, then the lift, because the mesh's bottom is only known once it is there: the
        // same two-step PlaceModel uses, and it survives a model whose pivot is not on its base.
        Undo.RecordObject(mug, "Place Coffee");
        mug.position = new Vector3(spot.x, mug.position.y, spot.z);
        TryWorldBounds(mug.gameObject, out mugBounds);
        mug.position += Vector3.up * (spot.y - mugBounds.min.y);
        // The handle is modelled on the mug's +X side.
        Vector3 outward = new Vector3(spot.x - tableBounds.center.x, 0f, spot.z - tableBounds.center.z);
        float handleYaw = outward.sqrMagnitude > 0.0001f ? Mathf.Atan2(-outward.z, outward.x) * Mathf.Rad2Deg : 0f;
        mug.rotation = Quaternion.Euler(0f, handleYaw, 0f);

        TryWorldBounds(mug.gameObject, out mugBounds);
        float scale = KitchenMapBuilder.WorldScale;
        float mugMetres = mugBounds.size.y / scale;
        // Said out loud because the scale is the one thing that goes wrong silently: a mug the size of
        // the room and an invisible speck both place without an error.
        Debug.Log($"Place Coffee: the table top measures {(tableBounds.max.y / scale):0.###} m up and {(tableBounds.size.x / scale):0.###} x {(tableBounds.size.z / scale):0.###} m; " +
                  $"the mug is {mugMetres * 100f:0.#} cm tall and {(mugBounds.size.x / scale) * 100f:0.#} cm across " +
                  $"({mugBounds.size.y:0.##} units at {scale} units to the metre), standing at {spot / scale} in layout metres.", mug);
        if (mugMetres < MinMugMetres || mugMetres > MaxMugMetres)
        {
            // Fails open: the mug is there and can be rescaled by hand, and stopping here would leave
            // the kitchen without its second dish over something that is only a wrong number.
            Debug.LogWarning($"Place Coffee: the mug came out {mugMetres:0.###} m tall, which is not a mug. The meshes are modelled at 1 unit = 1 metre and the house is " +
                             $"built at {scale} units to the metre, so check the import scale on {MugModelPath} and the scale of '{mug.parent.name}'.", mug);
        }
        return true;
    }

    // A spot on the table's top face that the mug fits on and that nothing is already standing on.
    // The table is oval, so a point only counts as on it if it is inside the ellipse its bounds
    // describe; the height comes from a downward ray, so a tray or a cloth on the table is stood on
    // rather than sunk into. Of the spots that are clear of the props, the one nearest the middle of
    // the table wins: that puts the mug beside the book and the candle rather than out on the rim,
    // which is both where somebody would have put it down and the safest place on an oval.
    // Fails open to the middle of the table's top, which is at worst a mug inside a candle rather than
    // no coffee at all.
    static Vector3 PickSpotOnTop(GameObject table, Bounds tableBounds, Transform tableProps, float mugRadius)
    {
        Vector3 middle = new Vector3(tableBounds.center.x, tableBounds.max.y, tableBounds.center.z);
        float marginX = tableBounds.extents.x - mugRadius * RimSlack;
        float marginZ = tableBounds.extents.z - mugRadius * RimSlack;
        if (marginX <= 0f || marginZ <= 0f)
        {
            Debug.LogWarning($"Place Coffee: the mug is as wide as '{table.name}', so it was put in the middle of it. Check the mug's scale.");
            return middle;
        }

        List<Bounds> props = PropFootprints(tableProps);
        float wanted = PropClearance * KitchenMapBuilder.WorldScale;
        // The colliders are where the transforms were when physics last looked, which in the Editor is
        // not after a script has just moved something.
        Physics.SyncTransforms();

        Vector3 nearest = middle;
        float nearestDistance = float.MaxValue;
        Vector3 roomiest = middle;
        float mostRoom = float.NegativeInfinity;
        for (int ix = 0; ix < SpotGridSteps; ix++)
        {
            for (int iz = 0; iz < SpotGridSteps; iz++)
            {
                float u = ix / (SpotGridSteps - 1f) * 2f - 1f;
                float v = iz / (SpotGridSteps - 1f) * 2f - 1f;
                // Inside the oval, with the mug's own width already taken off the radii.
                if (u * u + v * v > 1f)
                {
                    continue;
                }
                var candidate = new Vector3(tableBounds.center.x + u * marginX, SurfaceHeight(table, tableBounds, u * marginX, v * marginZ), tableBounds.center.z + v * marginZ);
                float room = RoomAround(candidate, props) - mugRadius;
                if (room > mostRoom)
                {
                    mostRoom = room;
                    roomiest = candidate;
                }
                float fromMiddle = Vector3.Distance(new Vector3(candidate.x, 0f, candidate.z), new Vector3(middle.x, 0f, middle.z));
                if (room >= wanted && fromMiddle < nearestDistance)
                {
                    nearestDistance = fromMiddle;
                    nearest = candidate;
                }
            }
        }

        if (nearestDistance < float.MaxValue)
        {
            return nearest;
        }
        // Fails open: every spot on the table is within a couple of centimetres of something, so the
        // roomiest one is used and said out loud rather than refusing to put the coffee down.
        Debug.LogWarning($"Place Coffee: nowhere on '{table.name}' leaves {PropClearance:0.###} m around the mug; the roomiest spot has " +
                         $"{(mostRoom / KitchenMapBuilder.WorldScale):0.###} m. The mug may be touching a prop - move it by hand if it looks wrong.");
        return roomiest;
    }

    // Each prop on the table as one world-space box, so a stack of books counts as one obstacle.
    static List<Bounds> PropFootprints(Transform tableProps)
    {
        var footprints = new List<Bounds>();
        if (tableProps == null)
        {
            return footprints;
        }
        foreach (Transform prop in tableProps)
        {
            if (TryWorldBounds(prop.gameObject, out Bounds bounds))
            {
                footprints.Add(bounds);
            }
        }
        return footprints;
    }

    // How much flat table there is around a point before the nearest prop, measured on the floor plane
    // only: the mug stands beside a book, not on top of it.
    static float RoomAround(Vector3 point, List<Bounds> props)
    {
        float room = float.MaxValue;
        foreach (Bounds prop in props)
        {
            Vector3 flat = new Vector3(point.x, prop.center.y, point.z);
            Vector3 nearest = prop.ClosestPoint(flat);
            room = Mathf.Min(room, Vector3.Distance(new Vector3(nearest.x, 0f, nearest.z), new Vector3(point.x, 0f, point.z)));
        }
        return room;
    }

    // The table's own surface at an offset from the middle of its top, from a short ray straight down.
    // Only the table's own colliders count, so neither the mug being placed nor a prop standing on the
    // table answers for it. Fails open to the top of the table's bounds: an oval table with no
    // collider is still a table, and its bounds are its top to within the thickness of a varnish.
    static float SurfaceHeight(GameObject table, Bounds tableBounds, float offsetX, float offsetZ)
    {
        float probe = SurfaceProbe * KitchenMapBuilder.WorldScale;
        var from = new Vector3(tableBounds.center.x + offsetX, tableBounds.max.y + probe, tableBounds.center.z + offsetZ);
        float height = tableBounds.max.y;
        bool found = false;
        foreach (RaycastHit hit in Physics.RaycastAll(from, Vector3.down, probe * 2f, ~0, QueryTriggerInteraction.Ignore))
        {
            if (!hit.collider.transform.IsChildOf(table.transform))
            {
                continue;
            }
            if (!found || hit.point.y > height)
            {
                height = hit.point.y;
                found = true;
            }
        }
        return height;
    }

    // ---- the mug itself ------------------------------------------------------------------------

    // The mug as three objects: an empty root that carries the components and takes the house's scale,
    // the glass, and the coffee inside it. An empty root rather than the glass itself so that running
    // this again can replace either mesh without touching what is wired to the root.
    static Transform EnsureMug(Transform seating, GameObject mugModel, GameObject liquidModel, Shader lit, out Transform liquid)
    {
        liquid = null;
        Transform mug = FindMug(seating);
        if (mug == null)
        {
            var created = new GameObject(MugObjectName);
            Undo.RegisterCreatedObjectUndo(created, "Place Coffee");
            mug = created.transform;
        }
        if (mug.parent != seating)
        {
            Undo.SetTransformParent(mug, seating, "Place Coffee");
        }
        // The meshes are modelled at 1 unit = 1 metre and the house is blown up by WorldScale, so the
        // mug is real size exactly when its world scale is WorldScale. Under KitchenMap that is a local
        // scale of 1; worked out from the parent rather than assumed, so the mug is still right if it
        // is ever hung under something else.
        float parentScale = seating.lossyScale.x;
        if (parentScale <= 0f)
        {
            Debug.LogError($"Place Coffee: '{seating.name}' has a zero scale, so the mug's size cannot be worked out.");
            return null;
        }
        mug.localScale = Vector3.one * (KitchenMapBuilder.WorldScale / parentScale);

        Transform glass = EnsurePart(mug, GlassObjectName, mugModel, Vector3.zero);
        liquid = EnsurePart(mug, LiquidObjectName, liquidModel, Vector3.up * LiquidLift);
        if (glass == null || liquid == null)
        {
            return null;
        }

        Paint(glass, EnsureGlassMaterial(lit));
        Paint(liquid, EnsureCoffeeMaterial(lit));
        // Transparent glass with a solid black shadow under it reads as a tin can.
        foreach (Renderer renderer in glass.GetComponentsInChildren<Renderer>(true))
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }
        // The fly has to be able to stand on the rim to drink, so the glass is solid to land on; the
        // coffee is left without one, because a fly landing on the surface of a drink is the point.
        AddCollidersIfMissing(glass.gameObject);
        return mug;
    }

    // The mug this made last time, wherever it has been dragged to since.
    static Transform FindMug(Transform seating)
    {
        Transform own = seating.Find(MugObjectName);
        if (own != null)
        {
            return own;
        }
        CoffeeCup existing = Object.FindAnyObjectByType<CoffeeCup>(FindObjectsInactive.Include);
        if (existing != null)
        {
            return existing.transform;
        }
        // Named but not yet wired: a run that was interrupted before the components went on.
        return seating.root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == MugObjectName);
    }

    static Transform EnsurePart(Transform mug, string name, GameObject model, Vector3 localPosition)
    {
        Transform part = mug.Find(name);
        if (part == null)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, mug);
            if (instance == null)
            {
                Debug.LogError($"Place Coffee: could not instantiate {model.name} for the mug's {name}.");
                return null;
            }
            instance.name = name;
            Undo.RegisterCreatedObjectUndo(instance, "Place Coffee");
            part = instance.transform;
        }
        part.localPosition = localPosition;
        part.localRotation = Quaternion.identity;
        part.localScale = Vector3.one;
        return part;
    }

    static void Paint(Transform part, Material material)
    {
        foreach (Renderer renderer in part.GetComponentsInChildren<Renderer>(true))
        {
            var slots = new Material[Mathf.Max(1, renderer.sharedMaterials.Length)];
            for (int i = 0; i < slots.Length; i++)
            {
                slots[i] = material;
            }
            renderer.sharedMaterials = slots;
        }
    }

    // Same as KitchenMapBuilder does for a prop with no UCX hull: an exact, non-convex collider per
    // mesh, which is what lets the fly land on the rim rather than on a box over the whole mug.
    static void AddCollidersIfMissing(GameObject part)
    {
        if (part.GetComponentInChildren<Collider>(true) != null)
        {
            return;
        }
        foreach (MeshFilter filter in part.GetComponentsInChildren<MeshFilter>(true))
        {
            filter.gameObject.AddComponent<MeshCollider>().sharedMesh = filter.sharedMesh;
        }
    }

    // ---- the materials -------------------------------------------------------------------------

    // Clear glass, to the numbers in Assets/cup_coffie/README.md. URP decides transparency from a
    // handful of properties and a keyword together, so all of them are set: setting only _Surface
    // leaves an opaque white mug.
    static Material EnsureGlassMaterial(Shader lit)
    {
        Material glass = EnsureMaterial(GlassMaterialPath, lit);
        glass.SetColor("_BaseColor", new Color(0.92f, 0.95f, 0.98f, 0.22f));
        glass.SetFloat("_Smoothness", 0.95f);
        glass.SetFloat("_Metallic", 0f);
        glass.SetFloat("_Surface", 1f);
        glass.SetFloat("_Blend", 0f);
        glass.SetFloat("_AlphaClip", 0f);
        glass.SetFloat("_ZWrite", 0f);
        glass.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        glass.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        glass.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        glass.DisableKeyword("_ALPHATEST_ON");
        glass.SetOverrideTag("RenderType", "Transparent");
        glass.renderQueue = (int)RenderQueue.Transparent;
        EditorUtility.SetDirty(glass);
        return glass;
    }

    static Material EnsureCoffeeMaterial(Shader lit)
    {
        Material coffee = EnsureMaterial(CoffeeMaterialPath, lit);
        coffee.SetColor("_BaseColor", Color.white);
        coffee.SetFloat("_Smoothness", 0.35f);
        coffee.SetFloat("_Metallic", 0f);
        Texture albedo = AssetDatabase.LoadAssetAtPath<Texture>(CoffeeAlbedoPath);
        Texture normal = LoadNormalMap(CoffeeNormalPath);
        if (albedo == null || normal == null)
        {
            // Fails open: plain brown coffee is still coffee.
            Debug.LogWarning($"Place Coffee: no texture at {CoffeeAlbedoPath} or {CoffeeNormalPath}, so the coffee is left untextured.");
            coffee.SetColor("_BaseColor", new Color(0.33f, 0.19f, 0.1f, 1f));
        }
        else
        {
            coffee.SetTexture("_BaseMap", albedo);
            coffee.SetTexture("_BumpMap", normal);
            coffee.EnableKeyword("_NORMALMAP");
        }
        EditorUtility.SetDirty(coffee);
        return coffee;
    }

    // The crema's normal map ships as an ordinary colour texture, which URP would read as a flat blue
    // tint instead of foam, so the importer is corrected once here rather than left for somebody to
    // notice in the inspector.
    static Texture LoadNormalMap(string path)
    {
        if (AssetImporter.GetAtPath(path) is TextureImporter importer && importer.textureType != TextureImporterType.NormalMap)
        {
            importer.textureType = TextureImporterType.NormalMap;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Texture>(path);
    }

    static Material EnsureMaterial(string path, Shader shader)
    {
        Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }
        if (material.shader != shader)
        {
            material.shader = shader;
        }
        return material;
    }

    // ---- what makes it a dish ------------------------------------------------------------------

    // CoffeeCup drains the mug, FoodDish makes it food. The NetworkObject and FoodDishNetwork the
    // online kitchen needs are left to Wire Match, which finds every IDishFill in the scene.
    static void WireMugComponents(Transform mug, Transform liquid)
    {
        CoffeeCup cup = EnsureComponent<CoffeeCup>(mug.gameObject);
        var cupFields = new SerializedObject(cup);
        SetReference(cupFields, "coffeeLiquid", liquid);
        cupFields.ApplyModifiedPropertiesWithoutUndo();

        FoodDish dish = EnsureComponent<FoodDish>(mug.gameObject);
        var dishFields = new SerializedObject(dish);
        SetFloat(dishFields, "biteRangeScale", MugBiteRangeScale);
        SetFloat(dishFields, "biteRate", MugBiteRate);
        SetFloat(dishFields, "guardRangeScale", MugGuardRangeScale);
        dishFields.ApplyModifiedPropertiesWithoutUndo();
    }

    static T EnsureComponent<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : Undo.AddComponent<T>(target);
    }

    // Fails closed: a field renamed without this being updated stops the run loudly instead of
    // leaving a mug that drains at the couscous's rate or a CoffeeCup with no liquid to lower.
    static SerializedProperty Field(SerializedObject fields, string fieldName)
    {
        SerializedProperty property = fields.FindProperty(fieldName);
        if (property == null)
        {
            throw new System.InvalidOperationException(
                $"Place Coffee: {fields.targetObject.GetType().Name} has no serialized field '{fieldName}'. Update {nameof(CoffeeCupSetup)} to match it.");
        }
        return property;
    }

    static void SetReference(SerializedObject fields, string fieldName, Object value)
    {
        Field(fields, fieldName).objectReferenceValue = value;
    }

    static void SetFloat(SerializedObject fields, string fieldName, float value)
    {
        Field(fields, fieldName).floatValue = value;
    }

    static bool TryWorldBounds(GameObject target, out Bounds bounds)
    {
        bounds = new Bounds(target.transform.position, Vector3.zero);
        Renderer[] renderers = target.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return false;
        }
        bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }
        return true;
    }
}
