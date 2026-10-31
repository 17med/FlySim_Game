using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// The spit's materials and particle systems, and the two prefabs they go into. See
// SpitTestSceneBuilder.cs for the entry point and what the rebuild does and does not touch.
// Every VFX here is texture-free: FlySimulator/StylizedToonParticle builds the droplet's round
// silhouette and its faked sphere normal from the quad's own UV, so there is no sprite sheet to
// author and nothing to keep in sync with the toon look.
// Particle sizes read small because the fly is 0.4 units across: a 0.02 droplet is about the size of
// its eye. Every number below is in those same world units.
public partial class SpitTestSceneBuilder
{
    // Sickly yellow-green, the one colour decision the whole effect hangs off.
    static readonly Color SpitLight = new Color(0.80f, 0.90f, 0.42f);
    static readonly Color SpitDark = new Color(0.52f, 0.66f, 0.22f);

    // The built assets, handed to the scene pass so it can wire the fly up to them.
    class SpitAssets
    {
        public Material Blob;
        public Material Droplet;
        public Material Stain;
        public GameObject SplashPrefab;
        public SpitProjectile ProjectilePrefab;
    }

    static SpitAssets BuildVfxAssets()
    {
        EnsureFolder(VfxFolder);
        SpitAssets assets = new SpitAssets
        {
            Blob = LoadOrCreateMaterial(VfxFolder + "/SpitBlob.mat", ToonShaderName, ConfigureBlobMaterial),
            Droplet = LoadOrCreateMaterial(VfxFolder + "/SpitDroplet.mat", ToonParticleShaderName, ConfigureDropletMaterial),
            Stain = LoadOrCreateMaterial(VfxFolder + "/SpitStain.mat", ToonParticleShaderName, ConfigureStainMaterial),
        };

        // Flushed before anything points at them: a freshly created material only exists in memory
        // until this, and a prefab saved against one that is not in the database yet can end up with
        // an empty material slot even though its GUID was written out.
        AssetDatabase.SaveAssets();

        // Order matters: the projectile holds a reference to the splash.
        assets.SplashPrefab = BuildSplashPrefab(assets);
        assets.ProjectilePrefab = BuildProjectilePrefab(assets);
        return assets;
    }

    // ---- Materials -------------------------------------------------------------------------------

    // The blob in flight is a solid little ball, so it uses the opaque lit shader the rest of the
    // project uses, outline included. Only the particles need the transparent one.
    static void ConfigureBlobMaterial(Material material)
    {
        material.SetColor("_BaseColor", SpitLight);
        material.SetColor("_ShadowColor", new Color(0.30f, 0.42f, 0.22f));
        material.SetColor("_MidColor", new Color(0.66f, 0.78f, 0.34f));
        // Wet: a tight, bright highlight and a strong rim.
        material.SetFloat("_HighlightStrength", 1.8f);
        material.SetFloat("_HighlightSize", 0.55f);
        material.SetFloat("_RimStrength", 1.1f);
        material.SetFloat("_OutlineWidth", 1.2f);
    }

    static void ConfigureDropletMaterial(Material material)
    {
        SetProceduralBlob(material, enabled: true);
        SetAlphaBlended(material);
        material.SetColor("_BaseColor", SpitLight);
        material.SetColor("_ShadowColor", new Color(0.34f, 0.46f, 0.24f));
        // A droplet in the air is a little ball, so it keeps the full faked sphere normal.
        material.SetFloat("_BlobRoundness", 1f);
        material.SetFloat("_SoftEdge", 0.30f);
        material.SetFloat("_FresnelAlpha", 0.5f);
        material.SetFloat("_HighlightStrength", 2.0f);
        material.SetFloat("_RimStrength", 0.9f);
    }

    // The stain lies flat on whatever it hit, so it gets the lobed silhouette: a plain disc reads as
    // a decal, an uneven blot reads as something that splattered.
    static void ConfigureStainMaterial(Material material)
    {
        SetProceduralBlob(material, enabled: true);
        SetAlphaBlended(material);
        material.SetColor("_BaseColor", SpitDark);
        // Kept well off black: a stain is a wet patch on the floor, and the shadow band is what it
        // falls back to in overcast ambient.
        material.SetColor("_ShadowColor", new Color(0.36f, 0.46f, 0.22f));
        // Low roundness: the stain is lit by the surface it is lying on, through the splash root's
        // +Z, rather than shading like a ball turned towards the camera.
        material.SetFloat("_BlobRoundness", 0.25f);
        material.SetFloat("_SoftEdge", 0.34f);
        // Five, not seven: fewer and shallower lobes, which the shader's second harmonic then
        // breaks up into something uneven instead of a cog.
        material.SetFloat("_SplatLobes", 5f);
        material.SetFloat("_SplatLobeDepth", 0.16f);
        material.SetFloat("_FresnelAlpha", 0.25f);
        // A stain is matte compared with a droplet in the air.
        material.SetFloat("_HighlightStrength", 0.7f);
        material.SetFloat("_RimStrength", 0.35f);
    }

    // The toggle drives a shader_feature, so the float and the keyword both have to be set: the float
    // is what the inspector checkbox shows, the keyword is what actually compiles the branch in.
    static void SetProceduralBlob(Material material, bool enabled)
    {
        material.SetFloat("_ProceduralBlob", enabled ? 1f : 0f);
        if (enabled)
            material.EnableKeyword("_PROCEDURALBLOB_ON");
        else
            material.DisableKeyword("_PROCEDURALBLOB_ON");
    }

    // Switch these two to One/One and the same material goes additive, for a glowing acid spit.
    static void SetAlphaBlended(Material material)
    {
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
    }

    // ---- Prefabs ---------------------------------------------------------------------------------

    static GameObject BuildSplashPrefab(SpitAssets assets)
    {
        GameObject root = new GameObject("SpitSplash");
        // SpitProjectile spawns this with LookRotation(surface normal), so the root's +Z points out
        // of the surface and both systems below are authored around that.
        BuildSplashDrops(NewChild(root, "Drops", Vector3.zero), assets.Droplet);
        // Lifted off the surface so the stain does not z-fight with the wall it is lying on.
        BuildSplashStain(NewChild(root, "Stain", new Vector3(0f, 0f, 0.006f)), assets.Stain);
        return SavePrefab(root, VfxFolder + "/SpitSplash.prefab");
    }

    static SpitProjectile BuildProjectilePrefab(SpitAssets assets)
    {
        GameObject root = new GameObject("SpitProjectile");
        SpitProjectile projectile = root.AddComponent<SpitProjectile>();

        // The sphere primitive is 1 unit across, which is what SpitProjectile.ShapeBlob scales from.
        GameObject blob = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        blob.name = "Blob";
        blob.transform.SetParent(root.transform, worldPositionStays: false);
        blob.transform.localScale = Vector3.one * 0.06f;
        // The blob sweeps its own collision with a sphere cast, so a collider would only let it shove
        // the fly's CharacterController around on the way out.
        UnityEngine.Object.DestroyImmediate(blob.GetComponent<Collider>());
        blob.GetComponent<MeshRenderer>().sharedMaterial = assets.Blob;

        ParticleSystem trail = BuildTrail(NewChild(root, "Trail", Vector3.zero), assets.Droplet);

        SerializedObject serialized = new SerializedObject(projectile);
        serialized.FindProperty("blob").objectReferenceValue = blob.transform;
        serialized.FindProperty("trail").objectReferenceValue = trail;
        serialized.FindProperty("splashPrefab").objectReferenceValue = assets.SplashPrefab;
        serialized.ApplyModifiedPropertiesWithoutUndo();

        // The saved asset, not the throwaway instance: FlySpit's field has to point at the prefab.
        return SavePrefab(root, VfxFolder + "/SpitProjectile.prefab").GetComponent<SpitProjectile>();
    }

    // ---- Particle systems ------------------------------------------------------------------------

    // Drops shed along the flight path. World space is the whole point: the drops must stay where
    // they were shed and fall from there, not ride along with the blob.
    static ParticleSystem BuildTrail(GameObject host, Material material)
    {
        ParticleSystem system = host.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = system.main;
        main.loop = true;
        main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.45f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0f, 0.25f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.018f);
        main.startColor = new ParticleSystem.MinMaxGradient(SpitLight, SpitDark);
        main.gravityModifier = 0.5f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 80;

        ParticleSystem.EmissionModule emission = system.emission;
        // Per distance as well as per second, so a fast blob leaves an unbroken streak while one
        // hanging at the top of its arc only dribbles.
        emission.rateOverTime = 18f;
        emission.rateOverDistance = 55f;

        ParticleSystem.ShapeModule shape = system.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.015f;

        FadeOut(system, holdUntil: 0.45f);
        Shrink(system);
        ConfigureRenderer(system, material, ParticleSystemRenderMode.Billboard);
        return system;
    }

    // Fired by FlySpit the moment the blob leaves, so playOnAwake is off: this one lives on the fly
    // for the whole game and must not spray on load.
    static ParticleSystem BuildMuzzleBurst(GameObject host, Material material)
    {
        ParticleSystem system = host.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = system.main;
        main.duration = 0.4f;
        main.loop = false;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.12f, 0.28f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.006f, 0.016f);
        main.startColor = new ParticleSystem.MinMaxGradient(SpitLight, SpitDark);
        main.gravityModifier = 0.4f;
        // World space, so the spray hangs in the air instead of being dragged along by the mouth
        // it came out of.
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 32;

        ParticleSystem.EmissionModule emission = system.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)7) });

        ParticleSystem.ShapeModule shape = system.shape;
        // Cone shapes spray along their own +Z, which the Muzzle object points down the fly's nose.
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 16f;
        shape.radius = 0.008f;

        FadeOut(system, holdUntil: 0.3f);
        Shrink(system);
        ConfigureRenderer(system, material, ParticleSystemRenderMode.Billboard);
        return system;
    }

    static ParticleSystem BuildSplashDrops(GameObject host, Material material)
    {
        ParticleSystem system = host.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = system.main;
        main.duration = 0.6f;
        main.loop = false;
        main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 3f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.008f, 0.024f);
        main.startColor = new ParticleSystem.MinMaxGradient(SpitLight, SpitDark);
        main.gravityModifier = 1.2f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 40;

        ParticleSystem.EmissionModule emission = system.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)16) });

        ParticleSystem.ShapeModule shape = system.shape;
        // A wide cone away from the surface: narrower looks like a jet, wider sprays into the wall.
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 55f;
        shape.radius = 0.01f;

        FadeOut(system, holdUntil: 0.55f);
        Shrink(system);
        ConfigureRenderer(system, material, ParticleSystemRenderMode.Billboard);
        return system;
    }

    // One flat blot left on the surface. Not a billboard: a quad mesh in the system's own local
    // space, so it lies in the surface's plane instead of turning to face the camera.
    static ParticleSystem BuildSplashStain(GameObject host, Material material)
    {
        ParticleSystem system = host.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = system.main;
        main.duration = 0.5f;
        main.loop = false;
        main.playOnAwake = true;
        main.startLifetime = 1.8f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.09f, 0.16f);
        main.startColor = new ParticleSystem.MinMaxGradient(SpitDark);
        main.gravityModifier = 0f;
        // Local, so the stain stays stuck to the spot even though the splash root is not parented
        // to whatever it hit.
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        // Radians in script, unlike the inspector's degrees. A random spin keeps the lobed silhouette
        // from repeating recognisably shot after shot.
        // Explicitly 3D with only Z free: plain startRotation turns a mesh particle about an axis
        // that tips the quad out of the surface, which showed up as stains rendered edge-on.
        main.startRotation3D = true;
        main.startRotationX = 0f;
        main.startRotationY = 0f;
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.maxParticles = 2;

        ParticleSystem.EmissionModule emission = system.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)1) });

        ParticleSystem.ShapeModule shape = system.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.01f;

        ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(Gradient(1f, 0.55f));

        // Splats outward fast, then creeps: the fast part is the impact, the creep is it running.
        ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.25f),
            new Keyframe(0.18f, 1f),
            new Keyframe(1f, 1.08f)));

        ConfigureRenderer(system, material, ParticleSystemRenderMode.Mesh);
        ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
        renderer.mesh = PrimitiveMesh(PrimitiveType.Quad);
        renderer.alignment = ParticleSystemRenderSpace.Local;
        return system;
    }

    // ---- Particle helpers ------------------------------------------------------------------------

    static void FadeOut(ParticleSystem system, float holdUntil)
    {
        ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(Gradient(1f, holdUntil));
    }

    static void Shrink(ParticleSystem system)
    {
        ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.15f));
    }

    // White with an alpha ramp: colorOverLifetime multiplies the start colour, so the colour keys
    // must stay at 1 or they would tint it a second time.
    static Gradient Gradient(float holdAlpha, float holdUntil)
    {
        Gradient gradient = new Gradient();
        gradient.SetKeys(
            new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[]
            {
                new GradientAlphaKey(holdAlpha, 0f),
                new GradientAlphaKey(holdAlpha, holdUntil),
                new GradientAlphaKey(0f, 1f),
            });
        return gradient;
    }

    static void ConfigureRenderer(ParticleSystem system, Material material, ParticleSystemRenderMode mode)
    {
        ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = mode;
        renderer.sharedMaterial = material;
        renderer.alignment = ParticleSystemRenderSpace.View;
        renderer.sortMode = ParticleSystemSortMode.Distance;
        // Droplets are too small to read as shadows and there can be hundreds in the air.
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    // ---- Asset helpers ---------------------------------------------------------------------------

    static Material LoadOrCreateMaterial(string path, string shaderName, Action<Material> configure)
    {
        // Left alone if it already exists, so a rebuild keeps whatever colours have been tweaked in
        // the inspector. Delete the .mat to get the defaults below back.
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
            return existing;

        Shader shader = Shader.Find(shaderName);
        if (shader == null)
            throw new InvalidOperationException($"Shader \"{shaderName}\" not found; it must compile before the VFX can be built.");

        Material material = new Material(shader);
        configure(material);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    static GameObject SavePrefab(GameObject root, string path)
    {
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
        UnityEngine.Object.DestroyImmediate(root);
        if (!success || prefab == null)
            throw new InvalidOperationException($"Could not save the prefab at {path}.");
        return prefab;
    }

    static GameObject NewChild(GameObject parent, string name, Vector3 localPosition)
    {
        GameObject child = new GameObject(name);
        child.transform.SetParent(parent.transform, worldPositionStays: false);
        child.transform.localPosition = localPosition;
        return child;
    }

    // Unity's built-in meshes are not assets that can be loaded by path, so one is borrowed off a
    // throwaway primitive. The mesh itself is shared and outlives the object it came from.
    static Mesh PrimitiveMesh(PrimitiveType type)
    {
        GameObject temporary = GameObject.CreatePrimitive(type);
        Mesh mesh = temporary.GetComponent<MeshFilter>().sharedMesh;
        UnityEngine.Object.DestroyImmediate(temporary);
        return mesh;
    }

    static void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder))
            return;
        string parent = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
    }
}
