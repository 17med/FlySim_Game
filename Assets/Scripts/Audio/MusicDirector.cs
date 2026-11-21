using UnityEngine;
using UnityEngine.SceneManagement;

// The kitchen's music, which follows the raid rather than just playing under it.
// Three tracks: Food Raid is the match, Fly Chase cuts in while the old man is on top of a fly, and
// Final Minute Panic takes over for the last minute. They cross-fade, so the kitchen never goes quiet
// between them and a chase that lasts two seconds does not restart the music.
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

    // How close the old man has to be to a fly for the chase to be on, in units. The kitchen is built
    // at about 30 units to the metre, so this is a little over an arm's length.
    const float ChaseDistance = 45f;
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
                back.Stop();
            }
        }
        ApplyVolume();
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
        if (match.IsFinalMinute)
        {
            return Cue.Panic;
        }
        return IsChaseOn() ? Cue.Chase : Cue.Raid;
    }

    // True while the old man is within reach of any fly. Held for a few seconds after he backs off,
    // so one step past a fly does not swap the music twice.
    bool IsChaseOn()
    {
        if (Time.time < chaseCheckAt)
        {
            return Time.time < chaseUntil;
        }
        chaseCheckAt = Time.time + ChaseCheckInterval;

        Transform keeper = Guard();
        if (keeper != null)
        {
            foreach (FlyController fly in FindObjectsByType<FlyController>(FindObjectsInactive.Exclude))
            {
                if (Vector3.Distance(keeper.position, fly.transform.position) <= ChaseDistance)
                {
                    chaseUntil = Time.time + ChaseHold;
                    break;
                }
            }
        }
        return Time.time < chaseUntil;
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
        // through a fade picks up from what is actually being heard rather than snapping.
        (front, back) = (back, front);
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
