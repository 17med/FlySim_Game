using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Hooks the sound effects in Assets/audio/SFX into the fly, the old man and the menu buttons
// (Tools > Audio > Wire Sound Effects). The prefabs are always wired; the buttons only when MainMenu
// is open, because that is where every Button lives.
// Running it again is safe: it reuses the sound objects it made before and rewrites the same clips.
// The menu music and the volume sliders are Tools > Main Menu > Wire Settings.
public static class SoundEffectsWiring
{
    const string FlyPrefabPath = "Assets/objectnetwork/fly.prefab";
    const string HumanPrefabPath = "Assets/objectnetwork/old_person Variant.prefab";
    const string SfxFolder = "Assets/audio/SFX/";

    static readonly string[] CrawlClips = { "Fly/crawl_sound/fly_crawl_1.wav", "Fly/crawl_sound/fly_crawl_2.wav", "Fly/crawl_sound/fly_crawl_3.wav" };
    static readonly string[] BuzzClips = { "Fly/buzz_sound/buzz.wav", "Fly/buzz_sound/buzz2.wav", "Fly/buzz_sound/buzz3.wav" };
    const string SpitClip = "Fly/spit_sound/spitt.wav";
    const string ChargeClip = "Fly/spit_sound/spitt_preload.wav";
    const string FullChargeClip = "Fly/spit_sound/powerup_spitt_preoload.wav";
    const string FootstepsClip = "Human/Footsteps/footsteps.wav";
    const string SwingClip = "Human/whip/human_wave _hand.wav";
    const string ThrowClip = "Human/whip/item_swing.wav";
    const string PickUpClip = "Human/items/pick_up.wav";
    const string DropClip = "Human/items/drop_item.wav";
    const string FadeClip = "Buttons/Fade_out/fade_out.wav";
    const string ClickClip = "Buttons/button_sound_1.wav";
    const string FlyHitClip = "Human/whip/woosh.wav";

    [MenuItem("Tools/Audio/Wire Sound Effects")]
    static void Wire()
    {
        // Fails closed throughout: a missing clip or component stops the run before anything is saved,
        // so a half-wired prefab is never written.
        if (!EditPrefab(FlyPrefabPath, WireFly) || !EditPrefab(HumanPrefabPath, WireHuman))
            return;

        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != GameSceneManager.MainMenuScene)
        {
            Debug.LogWarning($"Wire Sound Effects: fly and old man done. Open {GameSceneManager.MainMenuScene} and run it again to add the button clicks.");
            return;
        }
        if (WireButtons(scene))
            Debug.Log($"Wire Sound Effects: done. Save {scene.name} (Ctrl+S).");
    }

    static bool EditPrefab(string path, Func<GameObject, bool> wire)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(path);
        try
        {
            if (!wire(root))
                return false;
            PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
            if (!saved)
                Debug.LogError($"Wire Sound Effects: could not save {path}.");
            return saved;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static bool WireFly(GameObject root)
    {
        FlyController fly = root.GetComponentInChildren<FlyController>(true);
        FlySpit spit = root.GetComponentInChildren<FlySpit>(true);
        if (!Require(fly, FlyPrefabPath) || !Require(spit, FlyPrefabPath))
            return false;
        AudioClip[] crawl = LoadClips(CrawlClips);
        AudioClip[] buzz = LoadClips(BuzzClips);
        AudioClip shot = LoadClip(SpitClip), charge = LoadClip(ChargeClip), fullCharge = LoadClip(FullChargeClip);
        if (crawl == null || buzz == null || shot == null || charge == null || fullCharge == null)
            return false;

        // RequireComponent adds the AudioSource FlyAudio plays through. FlyAudio and FlySpit switch the
        // fly and spit sounds between flat (own fly) and positional (another player's) themselves.
        FlyAudio flyAudio = EnsureComponent<FlyAudio>(fly.gameObject);
        ConfigureSource(flyAudio.GetComponent<AudioSource>(), null, 1f);
        var flyAudioFields = new SerializedObject(flyAudio);
        SetClips(flyAudioFields.FindProperty("walkSounds"), crawl);
        SetClips(flyAudioFields.FindProperty("flySounds"), buzz);
        flyAudioFields.ApplyModifiedPropertiesWithoutUndo();

        // Big Spit Sound is left empty on purpose: FlySpit falls back to Spit Sound for a Big Shot.
        var spitFields = new SerializedObject(spit);
        spitFields.FindProperty("spitSound").objectReferenceValue = ChildSource(spit.transform, "SFX_Spit", shot, 1f);
        // The charge sounds are only ever heard by the shooter, so they are always flat.
        spitFields.FindProperty("chargeSound").objectReferenceValue = ChildSource(spit.transform, "SFX_SpitCharge", charge, 0f);
        spitFields.FindProperty("fullChargeSound").objectReferenceValue = ChildSource(spit.transform, "SFX_SpitFullCharge", fullCharge, 0f);
        spitFields.ApplyModifiedPropertiesWithoutUndo();
        return WireFlyHit(root, fly) && WireFade(root);
    }

    // The sound of the fly being slapped or hit. On a child of the controller, which is what moves:
    // the prefab root stays where the fly was spawned.
    static bool WireFlyHit(GameObject root, FlyController fly)
    {
        FlyKnockbackNetwork knockback = root.GetComponent<FlyKnockbackNetwork>();
        // Fails open: the other sounds are still worth saving; only the hit sound waits for the script.
        if (knockback == null)
        {
            Debug.LogWarning($"Wire Sound Effects: {FlyPrefabPath} has no {nameof(FlyKnockbackNetwork)} yet. Run Tools > Kitchen Online > Wire Network Players, then this again, to give the fly its hit sound.");
            return true;
        }
        AudioClip hit = LoadClip(FlyHitClip);
        if (hit == null)
            return false;
        SetObject(knockback, "hitSound", ChildSource(fly.transform, "SFX_Hit", hit, 0f));
        return true;
    }

    static bool WireHuman(GameObject root)
    {
        OldPersonFPSController controller = root.GetComponentInChildren<OldPersonFPSController>(true);
        OldPersonSlap slap = root.GetComponentInChildren<OldPersonSlap>(true);
        OldPersonPickup pickup = root.GetComponentInChildren<OldPersonPickup>(true);
        if (!Require(controller, HumanPrefabPath) || !Require(slap, HumanPrefabPath) || !Require(pickup, HumanPrefabPath))
            return false;
        AudioClip steps = LoadClip(FootstepsClip), swing = LoadClip(SwingClip), throwing = LoadClip(ThrowClip);
        AudioClip pickUp = LoadClip(PickUpClip), drop = LoadClip(DropClip);
        if (steps == null || swing == null || throwing == null || pickUp == null || drop == null)
            return false;

        // OldPersonFootsteps switches between flat and positional itself, depending on whose copy it is.
        OldPersonFootsteps footsteps = EnsureComponent<OldPersonFootsteps>(controller.gameObject);
        ConfigureSource(footsteps.GetComponent<AudioSource>(), null, 1f);
        SetObject(footsteps, "footstepSound", steps);
        SetObject(slap, "swingSound", swing);
        var pickupFields = new SerializedObject(pickup);
        pickupFields.FindProperty("pickUpSound").objectReferenceValue = pickUp;
        pickupFields.FindProperty("dropSound").objectReferenceValue = drop;
        pickupFields.FindProperty("throwSound").objectReferenceValue = throwing;
        pickupFields.ApplyModifiedPropertiesWithoutUndo();
        return WireFade(root);
    }

    // The screen fade both bodies play when they appear. Flat (2D): it belongs to the screen, not a place.
    static bool WireFade(GameObject root)
    {
        AudioClip fade = LoadClip(FadeClip);
        if (fade == null)
            return false;
        foreach (ScreenFade black in root.GetComponentsInChildren<ScreenFade>(true))
            SetObject(black, "fadeSound", ChildSource(black.transform, "SFX_Fade", fade, 0f));
        return true;
    }

    static bool WireButtons(Scene scene)
    {
        AudioClip click = LoadClip(ClickClip);
        if (click == null)
            return false;
        int count = 0;
        foreach (GameObject sceneRoot in scene.GetRootGameObjects())
        {
            foreach (Button button in sceneRoot.GetComponentsInChildren<Button>(true))
            {
                ButtonClickSound sound = button.GetComponent<ButtonClickSound>();
                if (sound == null)
                    sound = Undo.AddComponent<ButtonClickSound>(button.gameObject);
                var fields = new SerializedObject(sound);
                fields.FindProperty("clickSound").objectReferenceValue = click;
                fields.ApplyModifiedProperties();
                count++;
            }
        }
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"Wire Sound Effects: click sound on {count} buttons in {scene.name}.");
        return true;
    }

    // A child object holding one AudioSource, found by name on later runs. A child each, because
    // FlySpit plays and stops each sound on its own and FlyAudio uses the AudioSource on its own object.
    static AudioSource ChildSource(Transform parent, string name, AudioClip clip, float spatialBlend)
    {
        Transform child = parent.Find(name);
        if (child == null)
        {
            child = new GameObject(name).transform;
            child.SetParent(parent, false);
        }
        AudioSource source = EnsureComponent<AudioSource>(child.gameObject);
        ConfigureSource(source, clip, spatialBlend);
        return source;
    }

    static void ConfigureSource(AudioSource source, AudioClip clip, float spatialBlend)
    {
        source.clip = clip;
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = spatialBlend;
    }

    static T EnsureComponent<T>(GameObject target) where T : Component
    {
        T component = target.GetComponent<T>();
        return component != null ? component : target.AddComponent<T>();
    }

    static void SetObject(UnityEngine.Object target, string field, UnityEngine.Object value)
    {
        var fields = new SerializedObject(target);
        fields.FindProperty(field).objectReferenceValue = value;
        fields.ApplyModifiedPropertiesWithoutUndo();
    }

    static void SetClips(SerializedProperty list, AudioClip[] clips)
    {
        list.arraySize = clips.Length;
        for (int i = 0; i < clips.Length; i++)
            list.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
    }

    static bool Require<T>(T component, string path) where T : Component
    {
        if (component == null)
            Debug.LogError($"Wire Sound Effects: {path} has no {typeof(T).Name}; nothing was saved for it.");
        return component != null;
    }

    static AudioClip[] LoadClips(string[] paths)
    {
        var clips = new AudioClip[paths.Length];
        for (int i = 0; i < paths.Length; i++)
        {
            clips[i] = LoadClip(paths[i]);
            if (clips[i] == null)
                return null;
        }
        return clips;
    }

    static AudioClip LoadClip(string relativePath)
    {
        string path = SfxFolder + relativePath;
        AudioClip clip = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        if (clip == null)
            Debug.LogError($"Wire Sound Effects: no audio clip at {path}. Was it moved or renamed?");
        return clip;
    }
}
