using UnityEngine;

// Walking plays a walk sound, flying and hovering a fly sound, idle is silent. A random clip is
// picked from the state's list whenever the state changes or the last one ends, so short clips do not
// repeat as an obvious loop; an empty list means silence.
// Runs on every copy of the fly. The owner's copy reads FlyController; on another player's, where
// that component is off, the state comes from the Animator's "stateanim" instead.
[RequireComponent(typeof(FlyController), typeof(AudioSource))]
public class FlyAudio : MonoBehaviour
{
    [SerializeField] AudioClip[] walkSounds = new AudioClip[0];
    [SerializeField, Range(0f, 1f)] float walkVolume = 1f;
    [SerializeField] AudioClip[] flySounds = new AudioClip[0];
    [SerializeField, Range(0f, 1f)] float flyVolume = 1f;
    [Tooltip("Pitch of the buzz at full boost speed; hovering stays at 1 and cruising falls in between, so the fly is heard speeding up as well as seen. 1 turns the ramp off.")]
    [SerializeField, Range(0.5f, 2f)] float boostPitch = 1.25f;

    static readonly AudioClip[] Silence = new AudioClip[0];

    FlyController fly;
    Animator animator;
    AudioSource source;
    // The list the current clip came from, so a change of state is noticed.
    AudioClip[] playingSounds;

    void Awake()
    {
        fly = GetComponent<FlyController>();
        animator = GetComponentInChildren<Animator>();
        source = GetComponent<AudioSource>();
        // Not looped: a new random clip is picked when one ends.
        source.loop = false;
        source.playOnAwake = false;
    }

    void Update()
    {
        (AudioClip[] sounds, float volume) = CurrentState() switch
        {
            FlyController.MovementState.Walking => (walkSounds, walkVolume),
            FlyController.MovementState.Flying => (flySounds, flyVolume),
            _ => (Silence, 0f),
        };
        source.volume = GameAudio.Scale(volume);
        // Flat on the owner's copy: the orbit camera sits several units back, where a positional
        // sound would be faint. Other players hear the fly from where it is.
        source.spatialBlend = fly.enabled ? 0f : 1f;
        source.pitch = BuzzPitch();
        Play(sounds);
    }

    // Faster flight buzzes higher, the same way FlyController beats the wings faster. Only on the
    // owner's copy: the fly's speed is not sent over the network, so on another player's it would be
    // whatever it was at the moment that copy's FlyController was switched off - a frozen number that
    // would hold their fly at one wrong pitch for the whole match.
    float BuzzPitch()
    {
        if (!fly.enabled)
            return 1f;
        return Mathf.Lerp(1f, boostPitch, fly.SpeedFraction01);
    }

    FlyController.MovementState CurrentState()
    {
        // Fails open: with no Animator another player's fly is simply silent.
        if (fly.enabled || animator == null)
            return fly.State;
        return (FlyController.MovementState)animator.GetInteger(FlyController.StateAnimParameter);
    }

    // Starts a new random clip only when the state changes or the current clip has ended, so a
    // playing sound is not cut off every frame.
    void Play(AudioClip[] sounds)
    {
        bool isSameState = sounds == playingSounds;
        if (isSameState && (source.isPlaying || sounds.Length == 0))
            return;

        playingSounds = sounds;
        if (sounds.Length == 0)
        {
            source.Stop();
            return;
        }
        source.clip = sounds[Random.Range(0, sounds.Length)];
        source.Play();
    }
}
