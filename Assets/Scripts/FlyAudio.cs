using UnityEngine;

// Loops the walk or fly sound to match FlyController's state: walking plays the walk sound, flying and
// hovering play the fly sound, idle is silent. An empty sound slot means silence for that state.
[RequireComponent(typeof(FlyController), typeof(AudioSource))]
public class FlyAudio : MonoBehaviour
{
    [SerializeField] AudioClip walkSound;
    [SerializeField, Range(0f, 1f)] float walkVolume = 1f;
    [SerializeField] AudioClip flySound;
    [SerializeField, Range(0f, 1f)] float flyVolume = 1f;

    FlyController fly;
    AudioSource source;

    void Awake()
    {
        fly = GetComponent<FlyController>();
        source = GetComponent<AudioSource>();
        source.loop = true;
        source.playOnAwake = false;
    }

    void Update()
    {
        (AudioClip clip, float volume) = fly.State switch
        {
            FlyController.MovementState.Walking => (walkSound, walkVolume),
            FlyController.MovementState.Flying => (flySound, flyVolume),
            _ => (null, 0f),
        };
        PlayLooping(clip, volume);
    }

    // Restarts only when the clip changes, so a looping sound is not cut off every frame.
    void PlayLooping(AudioClip clip, float volume)
    {
        source.volume = volume;
        if (source.clip == clip && (clip == null || source.isPlaying))
            return;

        source.clip = clip;
        if (clip == null)
            source.Stop();
        else
            source.Play();
    }
}
