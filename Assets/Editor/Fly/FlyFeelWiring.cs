using System;
using UnityEditor;
using UnityEngine;

// Puts the fly's and the old man's feel together (Tools > Fly > Wire Fly Feel): the new feedback
// components, the effects FlyVfxBuilder made, the sounds that go with them, and the handful of tuned
// numbers that turn a boost into a boost and a slap into a blow.
// Run Tools > Fly > Build Fly VFX first; the effect prefabs have to exist before anything can point
// at them. Running this again is safe: it reuses the sound objects and the streak child it made
// before and rewrites the same values.
//
// What it tunes, and why those numbers:
//   Fly      - Shift is worth pressing (x3 rather than x2, and it lifts as well as pushes), the fly
//              answers the stick sooner (acceleration 0.1 s, turn 540 deg/s), and a boost kicks the
//              view instead of easing it open.
//   Old man  - the slap hits harder and jolts more, and its landing is a heavier sound than its swing
//              instead of the same clip played twice.
//   Bumps    - the speeds above decide what counts as hitting a wall hard, so Full Bump Speed is set
//              from the fly's new top speed rather than left at the script's default.
//
// Sound effects come out of the same Assets/audio/SFX folder SoundEffectsWiring uses, and the two do
// not overlap: that one wires movement, spit, footsteps and buttons, this one wires getting hurt.
// Four of the clips there were not used by anything before this; three of them are used here, pitched
// down, because what the project was missing was not clips but weight. See GameAudio.PlaySfxAt.
public static class FlyFeelWiring
{
    const string FlyPrefabPath = "Assets/objectnetwork/fly.prefab";
    const string HumanPrefabPath = "Assets/objectnetwork/old_person Variant.prefab";
    const string SfxFolder = "Assets/audio/SFX/";
    const string VfxFolder = "Assets/VFX/Fly/";
    // Found by name on later runs, so a second run does not stack a second set of them. Nothing under
    // the fly's Animator is touched: those names are addressed by the animation clips.
    const string BumpSourceName = "SFX_Bump";
    const string HurtSourceName = "SFX_Hurt";
    const string StreaksName = "FlyStreaks";

    // A knock on something hard. Pitched down by FlyImpact in proportion to how fast the fly hit it.
    const string BumpClip = "Human/items/drop_item.wav";
    // The body blow layered under the fly's own hit sound. A different clip from the old man's
    // landing on purpose: the same one on both would double up rather than thicken.
    const string HitClip = "Human/whip/item_swing.wav";
    // Wet. Dropped half an octave by FlyImpact, the spit is the squelch of a fly coming apart.
    const string DeathClip = "Fly/spit_sound/spitt.wav";
    // The old man's slap landing, kept apart from his swing (Human/whip/human_wave _hand.wav, which
    // SoundEffectsWiring puts on Swing Sound and which was also on Slap Sound until now).
    const string SlapClip = "Human/whip/woosh.wav";
    // His one unused clip. A grunt on a quarter of his swings.
    const string EffortClip = "Human/fury/fury.wav";

    [MenuItem("Tools/Fly/Wire Fly Feel")]
    public static void Wire()
    {
        // Fails closed throughout: a missing clip, effect or component stops the run before anything
        // is saved, so a half-wired prefab is never written.
        if (!EditPrefab(FlyPrefabPath, WireFly))
            return;
        if (!EditPrefab(HumanPrefabPath, WireHuman))
            return;
        AssetDatabase.SaveAssets();
        Debug.Log("Wire Fly Feel: done. The fly bumps, bleeds and streaks; the old man's slap lands harder.");
    }

    // ---- the fly ---------------------------------------------------------------------------------

    static bool WireFly(GameObject root)
    {
        FlyController fly = root.GetComponentInChildren<FlyController>(true);
        if (!Require(fly, FlyPrefabPath))
            return false;

        AudioClip bump = LoadClip(BumpClip), hit = LoadClip(HitClip), death = LoadClip(DeathClip);
        GameObject bumpEffect = LoadPrefab("FlyBump"), blood = LoadPrefab("FlyBlood"), guts = LoadPrefab("FlyGuts");
        GameObject streaks = LoadPrefab("FlyStreaks");
        if (bump == null || hit == null || death == null)
            return false;
        if (bumpEffect == null || blood == null || guts == null || streaks == null)
            return false;

        TuneFly(fly);
        return WireFlyImpact(fly, bump, hit, death, bumpEffect, blood, guts)
               && WireFlyRush(root, fly, streaks)
               && TuneFlyAudio(fly);
    }

    // Faster, sharper, and with a boost that reads as one.
    static void TuneFly(FlyController fly)
    {
        var fields = new SerializedObject(fly);
        // Three, not two: at two, Shift was close enough to cruising that players did not believe it
        // was doing anything. Move Speed is left where it is, so cruising the kitchen is unchanged and
        // only the boost got faster.
        fields.FindProperty("boostMultiplier").floatValue = 3f;
        // Climbing and diving get the whole boost too. Shift used to leave them at their own speed,
        // which made a boosted climb slower than a boosted dash and read as the boost cutting out
        // whenever the fly went up.
        fields.FindProperty("boostLiftShare").floatValue = 1f;
        // Down from 0.25. A quarter of a second to come up to speed is a long time to hold a button
        // and feel nothing; a tenth is still momentum, not a snap.
        fields.FindProperty("accelerationTime").floatValue = 0.1f;
        // Still longer than the acceleration, so the fly keeps gliding to a stop rather than braking.
        fields.FindProperty("decelerationTime").floatValue = 0.3f;
        // Up from 360. The fly turns toward the camera half again as fast, so flicking the mouse round
        // turns the fly rather than leaving it trailing behind the view.
        fields.FindProperty("turnSpeed").floatValue = 540f;
        // The kick on the press. Twelve degrees over a Default Fov of 60 is felt without the kitchen
        // visibly warping.
        fields.FindProperty("boostFovPunch").floatValue = 12f;
        // Bumps. Eight units a second is about a third of cruising speed: drifting into a cupboard is
        // silent, flying into one is not.
        fields.FindProperty("bumpMinSpeed").floatValue = 8f;
        fields.FindProperty("bumpAbsorb").floatValue = 0.75f;
        fields.FindProperty("bumpCooldown").floatValue = 0.12f;
        fields.ApplyModifiedPropertiesWithoutUndo();
    }

    static bool WireFlyImpact(FlyController fly, AudioClip bump, AudioClip hit, AudioClip death,
                              GameObject bumpEffect, GameObject blood, GameObject guts)
    {
        FlyImpact impact = EnsureComponent<FlyImpact>(fly.gameObject);
        // Two sources, not one: a slapped fly bounces off the floor while the blow is still ringing,
        // and one source would have the bump cut the blow off. They are children of the controller,
        // which is what moves - the prefab root stays where the fly was spawned.
        AudioSource bumpSource = ChildSource(fly.transform, BumpSourceName);
        AudioSource hurtSource = ChildSource(fly.transform, HurtSourceName);

        var fields = new SerializedObject(impact);
        fields.FindProperty("bumpSource").objectReferenceValue = bumpSource;
        SetClips(fields.FindProperty("bumpSounds"), bump);
        fields.FindProperty("bumpEffect").objectReferenceValue = bumpEffect;
        // Three times Move Speed is the fly's new top boost speed, so a bump at full tilt is as loud
        // and as big as a bump gets, and anything slower is scaled down from it.
        fields.FindProperty("fullBumpSpeed").floatValue = 70f;

        fields.FindProperty("hurtSource").objectReferenceValue = hurtSource;
        SetClips(fields.FindProperty("hitSounds"), hit);
        fields.FindProperty("bloodEffect").objectReferenceValue = blood;

        SetClips(fields.FindProperty("deathSounds"), death);
        fields.FindProperty("deathEffect").objectReferenceValue = guts;
        // Half an octave down: the spit clip at its own pitch is a fly spitting, and the same clip
        // slowed is a fly bursting.
        fields.FindProperty("deathPitch").floatValue = 0.55f;
        // Longer than the guts stain's own 2.4 second life plus its fade, or the burst is destroyed
        // from under the stain halfway through it drying.
        fields.FindProperty("effectLifetime").floatValue = 3.5f;
        fields.ApplyModifiedPropertiesWithoutUndo();
        return true;
    }

    // The streaks go on the camera, not the fly: parented to the fly they would turn with the body as
    // it banked, and the whole point of them is that they rake straight past the view.
    static bool WireFlyRush(GameObject root, FlyController fly, GameObject streaksPrefab)
    {
        Camera view = FlyCamera(root);
        if (view == null)
        {
            Debug.LogError($"Wire Fly Feel: no Camera under {FlyPrefabPath}, so the boost streaks have nowhere to live. Nothing was saved.");
            return false;
        }

        Transform existing = view.transform.Find(StreaksName);
        if (existing != null)
        {
            // Replaced rather than reused, so a rebuilt FlyStreaks.prefab actually reaches the fly.
            UnityEngine.Object.DestroyImmediate(existing.gameObject);
        }
        // A nested prefab instance, so editing FlyStreaks.prefab later updates the fly without a
        // second run of this.
        var streaks = (GameObject)PrefabUtility.InstantiatePrefab(streaksPrefab);
        streaks.name = StreaksName;
        streaks.transform.SetParent(view.transform, worldPositionStays: false);

        ParticleSystem system = streaks.GetComponent<ParticleSystem>();
        if (!Require(system, VfxFolder + StreaksName + ".prefab"))
            return false;

        FlyRush rush = EnsureComponent<FlyRush>(fly.gameObject);
        var fields = new SerializedObject(rush);
        fields.FindProperty("streaks").objectReferenceValue = system;
        // Not until the fly is nearly half-way to its top speed, which it only reaches on Shift: a
        // cruising fly leaves the kitchen clear to look at.
        fields.FindProperty("startAt").floatValue = 0.45f;
        fields.FindProperty("fullRate").floatValue = 90f;
        fields.ApplyModifiedPropertiesWithoutUndo();
        return true;
    }

    static bool TuneFlyAudio(FlyController fly)
    {
        FlyAudio audio = fly.GetComponent<FlyAudio>();
        // Fails open: the rest of the feel is still worth saving, and SoundEffectsWiring is what adds
        // FlyAudio in the first place.
        if (audio == null)
        {
            Debug.LogWarning($"Wire Fly Feel: {FlyPrefabPath} has no {nameof(FlyAudio)} yet, so the buzz will not rise with speed. Run Tools > Audio > Wire Sound Effects, then this again.");
            return true;
        }
        var fields = new SerializedObject(audio);
        // A third up at full boost. Further and the buzz starts sounding like a mosquito.
        fields.FindProperty("boostPitch").floatValue = 1.3f;
        fields.ApplyModifiedPropertiesWithoutUndo();
        return true;
    }

    // The camera the player sees through. OrbitCamera is the thing that marks it out, because the fly
    // model carries no camera of its own and a plain search would be a guess.
    static Camera FlyCamera(GameObject root)
    {
        OrbitCamera orbit = root.GetComponentInChildren<OrbitCamera>(true);
        if (orbit != null)
        {
            Camera onOrbit = orbit.GetComponent<Camera>();
            if (onOrbit != null)
                return onOrbit;
        }
        return root.GetComponentInChildren<Camera>(true);
    }

    // ---- the old man -----------------------------------------------------------------------------

    static bool WireHuman(GameObject root)
    {
        OldPersonSlap slap = root.GetComponentInChildren<OldPersonSlap>(true);
        if (!Require(slap, HumanPrefabPath))
            return false;
        AudioClip landing = LoadClip(SlapClip), effort = LoadClip(EffortClip);
        if (landing == null || effort == null)
            return false;

        var fields = new SerializedObject(slap);
        // Was the same clip as Swing Sound, so a slap that connected sounded exactly like one that
        // missed, only twice.
        fields.FindProperty("slapSound").objectReferenceValue = landing;
        fields.FindProperty("slapPitch").floatValue = 0.6f;
        // The landing at the full sound effects level and the swing well under it, which is the only
        // way round: an AudioSource clamps its volume to 1, so the blow cannot be pushed above the
        // swing and the swing has to come down to it.
        fields.FindProperty("slapVolume").floatValue = 1f;
        fields.FindProperty("swingVolume").floatValue = 0.65f;
        fields.FindProperty("effortSound").objectReferenceValue = effort;
        fields.FindProperty("effortChance").floatValue = 0.25f;
        // Up from 0.5. The jolt is most of what tells the player through a first-person camera that
        // the hand found something.
        fields.FindProperty("hitShake").floatValue = 0.8f;
        // Up from 0.06. The freeze on contact is the other half of it; much longer and the arm reads
        // as stuck rather than as having met resistance.
        fields.FindProperty("hitStopTime").floatValue = 0.09f;
        // Harder, and more of it upward, so a slapped fly is thrown across the kitchen in an arc
        // instead of skidding away along the counter.
        fields.FindProperty("flyKnockSpeed").floatValue = 65f;
        fields.FindProperty("flyKnockLift").floatValue = 28f;
        fields.ApplyModifiedPropertiesWithoutUndo();
        return true;
    }

    // ---- helpers ---------------------------------------------------------------------------------
    // Deliberately the same shapes as SoundEffectsWiring's, so the two wiring scripts read alike.

    static bool EditPrefab(string path, Func<GameObject, bool> wire)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        if (root == null)
        {
            Debug.LogError($"Wire Fly Feel: no prefab at {path}. Was it moved or renamed?");
            return false;
        }
        try
        {
            if (!wire(root))
                return false;
            PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
            if (!saved)
                Debug.LogError($"Wire Fly Feel: could not save {path}.");
            return saved;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // A child holding one AudioSource, found by name on later runs. FlyImpact sets the volume, the
    // pitch and the spatial blend itself on every sound it plays, so none of them is authored here.
    static AudioSource ChildSource(Transform parent, string name)
    {
        Transform child = parent.Find(name);
        if (child == null)
        {
            child = new GameObject(name).transform;
            child.SetParent(parent, worldPositionStays: false);
        }
        AudioSource source = EnsureComponent<AudioSource>(child.gameObject);
        source.playOnAwake = false;
        source.loop = false;
        return source;
    }

    static T EnsureComponent<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : target.AddComponent<T>();
    }

    static void SetClips(SerializedProperty list, params AudioClip[] clips)
    {
        list.arraySize = clips.Length;
        for (int i = 0; i < clips.Length; i++)
            list.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
    }

    static bool Require<T>(T component, string path) where T : Component
    {
        if (component == null)
            Debug.LogError($"Wire Fly Feel: {path} has no {typeof(T).Name}; nothing was saved for it.");
        return component != null;
    }

    static AudioClip LoadClip(string relativePath)
    {
        string path = SfxFolder + relativePath;
        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        if (clip == null)
            Debug.LogError($"Wire Fly Feel: no audio clip at {path}. Was it moved or renamed?");
        return clip;
    }

    static GameObject LoadPrefab(string name)
    {
        string path = VfxFolder + name + ".prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
            Debug.LogError($"Wire Fly Feel: no prefab at {path}. Run Tools > Fly > Build Fly VFX first.");
        return prefab;
    }
}
