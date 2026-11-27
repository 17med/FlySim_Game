using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

// The kitchen's music, which follows the raid rather than just playing under it.
// Three tracks: Food Raid is the match, Fly Chase cuts in while the old man is close to a fly, and
// Final Minute Panic takes over for the last minute - or earlier, as soon as a dish is nearly eaten,
// since emptying one dish wins. They cross-fade, so the kitchen never goes quiet between them, and
// each track carries on from where it was left, so coming back from a chase does not replay Food
// Raid's opening. In the last seconds the panic speeds up, like a countdown.
// The chase is this player's own: on a fly's screen it is their fly the old man is close to, on his
// screen any fly. One fly near him used to switch every player's music, even flies in another room.
// The menu keeps its own theme (The Swarm Anthem on Stage_Root, wired by Tools > Main Menu > Wire
// Settings), so this only ever runs in a kitchen.
//
// It installs itself when a kitchen scene loads, the way PauseMenu does, so neither kitchen scene
// needs anything in it. The clips come out of Assets/audio/Music/Resources for the same reason: there
// is nothing in either scene to hang them off, and all three are wanted every match anyway.
public class MusicDirector : MonoBehaviour
{
    // Loaded by name from any Resources folder; these sit in Assets/audio/Music/Resources.
    const string RaidTrack = "Food Raid";
    const string ChaseTrack = "Fly Chase";
    const string PanicTrack = "Final Minute Panic";

    // How close a fly has to be to the middle of the old man's body for the chase to be on, in his own
    // body heights (about 3.5 m for a 1.75 m man). In his height rather than in units, so it follows
    // the house's scale: it used to be 45 units, written for 30 units to the metre and measured from his
    // feet, and at the house's 45 units to the metre a fly at his face was never close enough.
    const float ChaseRangeInBodyHeights = 2f;
    // How long before the end the final minute's music starts speeding up, and how fast it ends up.
    // Pitch also raises the key a little, which is why it stops at 8 percent. 1 turns the speed-up off.
    const float SprintSeconds = 15f;
    const float SprintPitch = 1.08f;
    // Once a chase starts it holds for this long after he backs off, so the music does not flicker
    // between two tracks while he walks past.
    const float ChaseHold = 4f;
    // How often the distance between the old man and the flies is measured. Every frame would be
    // wasteful for something the ear cannot hear change that fast.
    const float ChaseCheckInterval = 0.25f;

    enum Cue { None, Raid, Chase, Panic }

    [Tooltip("Seconds a change of track takes to cross-fade. Longer is smoother; too long and the last minute starts before its music does.")]
    [SerializeField, Range(0.1f, 6f)] float fadeSeconds = 1.6f;

    [Tooltip("The level the kitchen music is mixed at, before the player's music setting.")]
    [SerializeField, Range(0f, 1f)] float volume = 0.5f;

    // Two sources, so one can fade in while the other fades out. Which is which swaps on every change.
    AudioSource front;
    AudioSource back;
    AudioClip raid;
    AudioClip chase;
    AudioClip panic;
    Cue playing = Cue.None;
    // 0 to 1: how far the hand-over from back to front has got.
    float fade = 1f;
    float chaseUntil;
    float chaseCheckAt;
    Transform guard;
    CharacterController guardBody;
    // Where each track was when it was last heard, so a track coming back carries on rather than
    // starting again.
    readonly Dictionary<AudioClip, float> resumeAt = new Dictionary<AudioClip, float>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Install()
    {
        SceneManager.sceneLoaded += (scene, _) =>
        {
            if (scene.name != GameSceneManager.KitchenScene && scene.name != GameSceneManager.KitchenOnlineScene)
            {
                return;
            }
            if (FindAnyObjectByType<MusicDirector>() == null)
            {
                new GameObject(nameof(MusicDirector)).AddComponent<MusicDirector>();
            }
        };
    }

    void Awake()
    {
        raid = Load(RaidTrack);
        chase = Load(ChaseTrack);
        panic = Load(PanicTrack);
        front = NewSource("Music_A");
        back = NewSource("Music_B");
    }

    void OnEnable()
    {
        GameSettings.Changed += ApplyVolume;
    }

    void OnDisable()
    {
        GameSettings.Changed -= ApplyVolume;
    }

    void Update()
    {
        Switch(Wanted());
        // Unscaled: the single player pause stops time, and music that stopped dead with it would
        // have to be restarted rather than resumed.
        if (fade < 1f)
        {
            fade = Mathf.Min(1f, fade + Time.unscaledDeltaTime / fadeSeconds);
            if (fade >= 1f && back.isPlaying)
            {
                Remember(back);
                back.Stop();
            }
        }
        ApplyVolume();
        ApplyPitch();
    }

    // Which track the kitchen wants right now.
    Cue Wanted()
    {
        MatchDirector match = MatchDirector.Instance;
        if (match == null)
        {
            return Cue.Raid;
        }
        // The result banner is quiet on purpose: it is the one moment the players are reading rather
        // than playing.
        if (match.IsOver)
        {
            return Cue.None;
        }
        // A nearly eaten dish is as much a last stand as the last minute: one more dish emptied ends
        // the match. Food never comes back, so once this starts it stays.
        if (match.IsFinalMinute || match.FoodLeft < MatchDirector.FoodDangerShare)
        {
            return Cue.Panic;
        }
        return IsChaseOn() ? Cue.Chase : Cue.Raid;
    }

    // True while the old man is close to this player's fly, or to any fly when this player is the old
    // man. Held for a few seconds after he backs off, so one step past a fly does not swap the music twice.
    bool IsChaseOn()
    {
        if (Time.time < chaseCheckAt)
        {
            return Time.time < chaseUntil;
        }
        chaseCheckAt = Time.time + ChaseCheckInterval;

        Transform keeper = Guard();
        if (keeper != null && IsAnyFlyClose(keeper))
        {
            chaseUntil = Time.time + ChaseHold;
        }
        return Time.time < chaseUntil;
    }

    // From the middle of his body, so a fly at his face counts as close and one at his feet does too.
    bool IsAnyFlyClose(Transform keeper)
    {
        float height = guardBody != null ? guardBody.height * Mathf.Abs(keeper.lossyScale.y) : 0f;
        // Fails open to no chase: without his body's height there is no telling what close means,
        // and the match music playing on is better than a chase that never stops.
        if (height <= 0f)
        {
            return false;
        }
        Vector3 middle = keeper.TransformPoint(guardBody.center);
        float range = height * ChaseRangeInBodyHeights;
        // The old man's own screen hears every fly; a fly's screen only its own.
        bool isLocalHuman = IsThisMachinesPlayer(keeper.GetComponent<OldPersonFPSController>());
        foreach (FlyController fly in FindObjectsByType<FlyController>(FindObjectsInactive.Exclude))
        {
            if (!isLocalHuman && !IsThisMachinesPlayer(fly))
            {
                continue;
            }
            FlyHealth health = fly.GetComponent<FlyHealth>();
            if (health != null && health.IsDead)
            {
                continue;
            }
            if (Vector3.Distance(middle, fly.transform.position) <= range)
            {
                return true;
            }
        }
        return false;
    }

    // Online the body's owner is this player; in the single player Kitchen, where nothing is spawned,
    // the one body whose controls are switched on is. The same test MinimapHud makes.
    static bool IsThisMachinesPlayer(Behaviour controls)
    {
        if (controls == null)
        {
            return false;
        }
        NetworkObject body = controls.GetComponentInParent<NetworkObject>();
        if (body != null && body.IsSpawned)
        {
            return body.IsOwner;
        }
        return controls.isActiveAndEnabled;
    }

    // The old man. Looked up again while there is none: in a room his body is spawned by the host a
    // moment after the scene loads.
    Transform Guard()
    {
        if (guard != null)
        {
            return guard;
        }
        OldPersonFPSController keeper = FindAnyObjectByType<OldPersonFPSController>(FindObjectsInactive.Exclude);
        guard = keeper != null ? keeper.transform : null;
        guardBody = keeper != null ? keeper.GetComponent<CharacterController>() : null;
        return guard;
    }

    // Hands over to a new track, or does nothing when it is already the one playing.
    void Switch(Cue cue)
    {
        if (cue == playing)
        {
            return;
        }
        playing = cue;

        // The track going out becomes the back source and fades from there, so a change half way
        // through a fade picks up from what is actually being heard rather than snapping. The source
        // that was fading out before is cut off to play the new track, so its place is kept first.
        Remember(back);
        (front, back) = (back, front);
        front.pitch = 1f;
        fade = 0f;

        AudioClip clip = cue switch
        {
            Cue.Raid => raid,
            Cue.Chase => chase,
            Cue.Panic => panic,
            _ => null,
        };
        if (clip == null)
        {
            // Cue.None, or a clip that failed to load: the fade still runs, so the kitchen goes quiet
            // instead of cutting out.
            front.Stop();
            return;
        }
        front.clip = clip;
        front.Play();
        // After Play: a compressed clip only takes a position once it is playing.
        if (resumeAt.TryGetValue(clip, out float position) && position < clip.length)
        {
            front.time = position;
        }
    }

    void Remember(AudioSource source)
    {
        if (source.clip != null && source.isPlaying)
        {
            resumeAt[source.clip] = source.time;
        }
    }

    // The final minute's music speeds up over the last seconds; nothing else ever changes speed.
    void ApplyPitch()
    {
        MatchDirector match = MatchDirector.Instance;
        bool isSprint = playing == Cue.Panic && match != null && !match.IsOver && match.SecondsLeft < SprintSeconds;
        front.pitch = isSprint ? Mathf.Lerp(SprintPitch, 1f, match.SecondsLeft / SprintSeconds) : 1f;
    }

    void ApplyVolume()
    {
        float level = volume * GameSettings.MusicVolume;
        front.volume = level * fade;
        back.volume = level * (1f - fade);
    }

    AudioSource NewSource(string name)
    {
        var sourceObject = new GameObject(name);
        sourceObject.transform.SetParent(transform, false);
        AudioSource source = sourceObject.AddComponent<AudioSource>();
        source.loop = true;
        source.playOnAwake = false;
        // Music belongs to the screen, not to a place in the kitchen.
        source.spatialBlend = 0f;
        source.volume = 0f;
        return source;
    }

    static AudioClip Load(string trackName)
    {
        AudioClip clip = Resources.Load<AudioClip>(trackName);
        if (clip == null)
        {
            // Fails open: the other tracks still play, and the kitchen is quiet where this one would be.
            Debug.LogWarning($"{nameof(MusicDirector)}: no music track named \"{trackName}\" in a Resources folder. " +
                             "It belongs in Assets/audio/Music/Resources.");
        }
        return clip;
    }
}
