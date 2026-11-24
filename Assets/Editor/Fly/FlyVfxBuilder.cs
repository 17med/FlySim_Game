using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Builds the effects of the fly being hurt, and of it going fast (Tools > Fly > Build Fly VFX):
//   FlyBlood   - the spray of guts off a slap that the fly survives.
//   FlyGuts    - the same, bigger and wetter, for the slap that kills it.
//   FlyBump    - the puff of dust off a wall the fly flies into.
//   FlyStreaks - the lines that rake past the camera while it boosts.
// FlyFeelWiring is what puts them on the fly; this only makes the assets.
// Texture-free, like the spit VFX it is modelled on: FlySimulator/StylizedToonParticle builds every
// silhouette and every faked sphere normal out of the quad's own UV, so the whole folder is four
// materials and four prefabs and no art.
// The fly's blood is a green-yellow, which is both what an insect's actually is and what reads as
// "bug" rather than "person" against a bright kitchen. It is deliberately close to the spit's palette
// (SpitTestSceneBuilder.Vfx) without matching it: a shade deeper and redder, so a stain on a cupboard
// is not mistaken for a shot that missed.
// Sizes are in world units and read small on purpose: the fly's collider is half a unit across.
public static class FlyVfxBuilder
{
    const string VfxFolder = "Assets/VFX/Fly";
    const string ToonParticleShaderName = "FlySimulator/StylizedToonParticle";

    // The one colour decision the whole effect hangs off, as SpitLight/SpitDark are for the spit.
    static readonly Color GutsLight = new Color(0.72f, 0.78f, 0.26f);
    static readonly Color GutsDark = new Color(0.38f, 0.44f, 0.12f);
    // Kitchen dust: a warm off-white, not grey, or a puff reads as smoke.
    static readonly Color DustLight = new Color(0.86f, 0.83f, 0.74f);
    static readonly Color DustDark = new Color(0.62f, 0.58f, 0.50f);

    [MenuItem("Tools/Fly/Build Fly VFX")]
    public static void Build()
    {
        EnsureFolder(VfxFolder);

        Material drop = LoadOrCreateMaterial(VfxFolder + "/FlyGutsDrop.mat", ConfigureDropMaterial);
        Material stain = LoadOrCreateMaterial(VfxFolder + "/FlyGutsStain.mat", ConfigureStainMaterial);
        Material dust = LoadOrCreateMaterial(VfxFolder + "/FlyDust.mat", ConfigureDustMaterial);
        Material streak = LoadOrCreateMaterial(VfxFolder + "/FlyStreak.mat", ConfigureStreakMaterial);
        // Flushed before any prefab points at them: a prefab saved against a material that is still
        // only in memory can end up with an empty material slot.
        AssetDatabase.SaveAssets();

        BuildBurstPrefab("FlyBlood", drop, stain, scale: 1f);
        BuildBurstPrefab("FlyGuts", drop, stain, scale: 2.2f);
        BuildBumpPrefab(dust);
        BuildStreaksPrefab(streak);

        AssetDatabase.SaveAssets();
        Debug.Log($"Build Fly VFX: wrote FlyBlood, FlyGuts, FlyBump and FlyStreaks to {VfxFolder}. " +
                  "Run Tools > Fly > Wire Fly Feel to put them on the fly.");
    }

    // ---- Materials -------------------------------------------------------------------------------

    // A drop in the air is a little ball, so it keeps the full faked sphere normal and a wet highlight.
    static void ConfigureDropMaterial(Material material)
    {
        SetProceduralBlob(material, enabled: true);
        SetAlphaBlended(material);
        material.SetColor("_BaseColor", GutsLight);
        material.SetColor("_ShadowColor", GutsDark);
        material.SetFloat("_BlobRoundness", 1f);
        material.SetFloat("_SoftEdge", 0.28f);
        material.SetFloat("_FresnelAlpha", 0.55f);
        material.SetFloat("_HighlightStrength", 2.2f);
        material.SetFloat("_RimStrength", 1f);
    }

    // The stain lies flat on whatever it landed on, so it gets the lobed silhouette and is lit by that
    // surface rather than shading like a ball turned toward the camera.
    static void ConfigureStainMaterial(Material material)
    {
        SetProceduralBlob(material, enabled: true);
        SetAlphaBlended(material);
        material.SetColor("_BaseColor", GutsDark);
        // Well off black: the shadow band is what a wet patch falls back to in the room's ambient,
        // and a near-black one turns every stain into a hole in the floor.
        material.SetColor("_ShadowColor", new Color(0.24f, 0.28f, 0.08f));
        material.SetFloat("_BlobRoundness", 0.2f);
        material.SetFloat("_SoftEdge", 0.3f);
        // More and deeper lobes than the spit's stain: guts splatter, spit lands as a blot.
        material.SetFloat("_SplatLobes", 7f);
        material.SetFloat("_SplatLobeDepth", 0.24f);
        material.SetFloat("_FresnelAlpha", 0.3f);
        material.SetFloat("_HighlightStrength", 0.9f);
        material.SetFloat("_RimStrength", 0.4f);
    }

    // Dust is dry, so almost no highlight and no wet rim; the roundness is what keeps it reading as a
    // cloud of specks rather than a flat sprite.
    static void ConfigureDustMaterial(Material material)
    {
        SetProceduralBlob(material, enabled: true);
        SetAlphaBlended(material);
        material.SetColor("_BaseColor", DustLight);
        material.SetColor("_ShadowColor", DustDark);
        material.SetFloat("_BlobRoundness", 0.7f);
        // The softest edge of the four: a hard-edged puff is a bubble.
        material.SetFloat("_SoftEdge", 0.65f);
        material.SetFloat("_FresnelAlpha", 0.1f);
        material.SetFloat("_HighlightStrength", 0.25f);
        material.SetFloat("_RimStrength", 0.2f);
    }

    // The streaks are light rather than matter, so this is the one additive material here: it adds to
    // whatever is behind it instead of covering it, which is what keeps a wall of them from greying
    // the kitchen out at full boost.
    static void ConfigureStreakMaterial(Material material)
    {
        SetProceduralBlob(material, enabled: true);
        SetAdditive(material);
        material.SetColor("_BaseColor", new Color(0.92f, 0.95f, 1f));
        material.SetColor("_ShadowColor", new Color(0.7f, 0.78f, 0.92f));
        material.SetFloat("_BlobRoundness", 0f);
        material.SetFloat("_SoftEdge", 0.9f);
        material.SetFloat("_FresnelAlpha", 0f);
        material.SetFloat("_HighlightStrength", 0f);
        material.SetFloat("_RimStrength", 0f);
    }

    // ---- Prefabs ---------------------------------------------------------------------------------

    // Drops plus a stain, the same two-part shape as the spit's splash, because the same two things
    // happen: something wet leaves the point of impact, and some of it stays there.
    // FlyImpact spawns these with LookRotation along the direction the fly is flung, so the root's +Z
    // is the way the spray goes and both systems below are authored around that.
    static void BuildBurstPrefab(string name, Material drop, Material stain, float scale)
    {
        GameObject root = new GameObject(name);
        BuildBurstDrops(NewChild(root, "Drops"), drop, scale);
        BuildBurstSplat(NewChild(root, "Splat"), stain, scale);
        SavePrefab(root, $"{VfxFolder}/{name}.prefab");
    }

    static void BuildBumpPrefab(Material dust)
    {
        GameObject root = new GameObject("FlyBump");
        // Spawned facing out of the surface, so the puff rolls off the wall rather than into it.
        BuildDustPuff(NewChild(root, "Dust"), dust);
        SavePrefab(root, VfxFolder + "/FlyBump.prefab");
    }

    static void BuildStreaksPrefab(Material streak)
    {
        GameObject root = new GameObject("FlyStreaks");
        BuildStreaks(root, streak);
        SavePrefab(root, VfxFolder + "/FlyStreaks.prefab");
    }

    // ---- Particle systems ------------------------------------------------------------------------

    static void BuildBurstDrops(GameObject host, Material material, float scale)
    {
        ParticleSystem system = host.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = system.main;
        main.duration = 0.6f;
        main.loop = false;
        main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.3f, 0.75f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(1.5f * scale, 5f * scale);
        main.startSize = new ParticleSystem.MinMaxCurve(0.010f * scale, 0.035f * scale);
        main.startColor = new ParticleSystem.MinMaxGradient(GutsLight, GutsDark);
        main.gravityModifier = 1.4f;
        // World space: the spray has to hang in the air where the fly was hit while the fly itself is
        // flung out of it.
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 120;
        // So FlyImpact can scale a whole burst by scaling the object it spawned.
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;

        ParticleSystem.EmissionModule emission = system.emission;
        emission.rateOverTime = 0f;
        // Two bursts, not one: the second a frame later is what makes it read as something bursting
        // rather than as a single ring of dots leaving at once.
        emission.SetBursts(new[]
        {
            new ParticleSystem.Burst(0f, (short)Mathf.RoundToInt(18f * scale)),
            new ParticleSystem.Burst(0.04f, (short)Mathf.RoundToInt(8f * scale)),
        });

        ParticleSystem.ShapeModule shape = system.shape;
        // A wide cone, not a sphere: the guts go the way the blow sent the fly, with enough spread
        // that it is a splatter and not a jet.
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 48f;
        shape.radius = 0.012f * scale;

        FadeOut(system, holdUntil: 0.6f);
        Shrink(system);
        // Stretched along their own travel, so a fast drop is a flick of gut and a slow one a bead.
        ConfigureRenderer(system, material, ParticleSystemRenderMode.Stretch);
        ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
        renderer.lengthScale = 2.2f;
        renderer.velocityScale = 0.04f;
    }

    // The wet mass at the middle of the burst, under the drops that leave it. Not the spit's stain,
    // which lies flat on the wall it was shot at and dries there: a fly is hit in mid-air, so there is
    // no surface for this to lie on, and a quad pinned to a fixed plane in empty space turns edge-on
    // and disappears as the player orbits. It faces the camera instead and is gone in a second or so,
    // which is a blow landing wetly rather than a mark left behind.
    static void BuildBurstSplat(GameObject host, Material material, float scale)
    {
        ParticleSystem system = host.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = system.main;
        main.duration = 0.5f;
        main.loop = false;
        main.playOnAwake = true;
        // Outlives the drops, but not by much: it is the impact, not a stain.
        main.startLifetime = 1.2f;
        main.startSpeed = 0f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.07f * scale, 0.14f * scale);
        main.startColor = new ParticleSystem.MinMaxGradient(GutsDark);
        main.gravityModifier = 0f;
        // World, so it stays where the fly was hit while the fly is flung out of it, like the drops.
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        // Radians in script, unlike the inspector's degrees. A random spin keeps the lobed silhouette
        // from repeating hit after hit. Only Z is turned, which with a view-aligned quad is a spin in
        // the plane of the screen; turning X or Y would tip it away from the camera and show it edge-on.
        main.startRotation3D = true;
        main.startRotationX = 0f;
        main.startRotationY = 0f;
        main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.maxParticles = 4;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;

        ParticleSystem.EmissionModule emission = system.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)(scale > 1.5f ? 3 : 1)) });

        // No emitter shape: the stain is born exactly where the burst was spawned. Scattering it would
        // put some of them behind the surface.
        ParticleSystem.ShapeModule shape = system.shape;
        shape.enabled = false;

        ParticleSystem.ColorOverLifetimeModule color = system.colorOverLifetime;
        color.enabled = true;
        color.color = new ParticleSystem.MinMaxGradient(Gradient(1f, 0.6f));

        // Splats out fast, then creeps: the fast part is the impact, the creep is it running.
        ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(
            new Keyframe(0f, 0.2f),
            new Keyframe(0.15f, 1f),
            new Keyframe(1f, 1.12f)));

        // A quad rather than a billboard, so the material's lobed silhouette has a mesh to cut out of,
        // turned to face the camera rather than pinned to the burst's own plane.
        ConfigureRenderer(system, material, ParticleSystemRenderMode.Mesh);
        ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
        renderer.mesh = PrimitiveMesh(PrimitiveType.Quad);
        renderer.alignment = ParticleSystemRenderSpace.View;
    }

    static void BuildDustPuff(GameObject host, Material material)
    {
        ParticleSystem system = host.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = system.main;
        main.duration = 0.5f;
        main.loop = false;
        main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.09f);
        main.startColor = new ParticleSystem.MinMaxGradient(DustLight, DustDark);
        // Barely falls: dust hangs, which is what tells it apart from the guts.
        main.gravityModifier = 0.08f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 32;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;

        ParticleSystem.EmissionModule emission = system.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)10) });

        ParticleSystem.ShapeModule shape = system.shape;
        shape.shapeType = ParticleSystemShapeType.Cone;
        // Wider than the guts: dust rolls along the wall it came off rather than leaving it.
        shape.angle = 72f;
        shape.radius = 0.02f;

        FadeOut(system, holdUntil: 0.25f);
        // Dust grows as it disperses, the opposite of a drop, so Shrink is not what this one wants.
        ParticleSystem.SizeOverLifetimeModule size = system.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.6f, 1f, 1.8f));
        ConfigureRenderer(system, material, ParticleSystemRenderMode.Billboard);
    }

    // Lines raking past the view. Parented to the fly's camera by FlyFeelWiring and simulated in that
    // camera's own space, so they stream straight back past the player however the fly banks, and
    // FlyRush only has to turn the rate up and down.
    static void BuildStreaks(GameObject host, Material material)
    {
        ParticleSystem system = host.AddComponent<ParticleSystem>();

        ParticleSystem.MainModule main = system.main;
        main.loop = true;
        // Off: the system lives on the camera for the whole match, and FlyRush starts it. A streak
        // storm on the frame the kitchen loads would be the first thing the player saw.
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.35f);
        // Backwards, in the camera's own space: the streaks are the room going past, so they travel
        // the opposite way to the fly.
        main.startSpeed = new ParticleSystem.MinMaxCurve(-26f, -14f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.012f, 0.03f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 0.5f), new Color(0.85f, 0.9f, 1f, 0.2f));
        main.gravityModifier = 0f;
        main.simulationSpace = ParticleSystemSimulationSpace.Local;
        main.maxParticles = 160;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;

        // Set by FlyRush every frame; what is here only decides what the prefab looks like if it is
        // ever played by hand in the editor.
        ParticleSystem.EmissionModule emission = system.emission;
        emission.rateOverTime = 0f;

        ParticleSystem.ShapeModule shape = system.shape;
        // A ring ahead of the camera, hollow in the middle: streaks across the centre of the screen
        // would sit on top of the fly the player is trying to watch.
        shape.shapeType = ParticleSystemShapeType.Donut;
        shape.radius = 1.6f;
        shape.donutRadius = 0.9f;
        shape.position = new Vector3(0f, 0f, 3f);

        FadeOut(system, holdUntil: 0.2f);
        ConfigureRenderer(system, material, ParticleSystemRenderMode.Stretch);
        ParticleSystemRenderer renderer = system.GetComponent<ParticleSystemRenderer>();
        renderer.lengthScale = 6f;
        renderer.velocityScale = 0.08f;
    }

    // ---- Particle helpers ------------------------------------------------------------------------
    // Deliberate near-copies of SpitTestSceneBuilder.Vfx's. They are its private statics, and prying
    // them out into a shared editor utility would mean editing the one VFX builder in the project
    // that already works, for four small methods.

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
        // Specks this small do not read as shadows and there can be hundreds in the air.
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    static void SetProceduralBlob(Material material, bool enabled)
    {
        // The toggle drives a shader_feature, so the float and the keyword both have to be set: the
        // float is what the inspector checkbox shows, the keyword is what compiles the branch in.
        material.SetFloat("_ProceduralBlob", enabled ? 1f : 0f);
        if (enabled)
            material.EnableKeyword("_PROCEDURALBLOB_ON");
        else
            material.DisableKeyword("_PROCEDURALBLOB_ON");
    }

    static void SetAlphaBlended(Material material)
    {
        material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
    }

    static void SetAdditive(Material material)
    {
        material.SetFloat("_SrcBlend", (float)BlendMode.One);
        material.SetFloat("_DstBlend", (float)BlendMode.One);
    }

    // ---- Asset helpers ---------------------------------------------------------------------------

    static Material LoadOrCreateMaterial(string path, Action<Material> configure)
    {
        // Left alone if it already exists, so a rebuild keeps whatever colours have been tweaked in
        // the inspector. Delete the .mat to get the defaults above back.
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null)
            return existing;

        Shader shader = Shader.Find(ToonParticleShaderName);
        if (shader == null)
            throw new InvalidOperationException($"Shader \"{ToonParticleShaderName}\" not found; it must compile before the VFX can be built.");

        Material material = new Material(shader);
        configure(material);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    // Overwrites the prefab every run, unlike the materials: the prefabs are this file's output and
    // the numbers above are the only place they are authored.
    static GameObject SavePrefab(GameObject root, string path)
    {
        GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
        UnityEngine.Object.DestroyImmediate(root);
        if (!success || prefab == null)
            throw new InvalidOperationException($"Could not save the prefab at {path}.");
        return prefab;
    }

    static GameObject NewChild(GameObject parent, string name)
    {
        GameObject child = new GameObject(name);
        child.transform.SetParent(parent.transform, worldPositionStays: false);
        return child;
    }

    // The mesh off a throwaway primitive. Unity has no public handle on the built-in quad.
    static Mesh PrimitiveMesh(PrimitiveType type)
    {
        GameObject temporary = GameObject.CreatePrimitive(type);
        Mesh mesh = temporary.GetComponent<MeshFilter>().sharedMesh;
        UnityEngine.Object.DestroyImmediate(temporary);
        return mesh;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
            return;
        int split = path.LastIndexOf('/');
        string parent = path.Substring(0, split);
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, path.Substring(split + 1));
    }
}
