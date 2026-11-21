using UnityEngine;

// A looping music track that follows the music volume in the settings.
// Put it on the AudioSource that plays the music; Volume is the level the track is mixed at, which
// the player's music setting then scales.
[RequireComponent(typeof(AudioSource))]
public class MusicSource : MonoBehaviour
{
    [Tooltip("The track to loop. The AudioSource's own clip is used if this is left empty.")]
    [SerializeField] AudioClip track;

    [Tooltip("The level this track is mixed at, before the player's music setting.")]
    [SerializeField, Range(0f, 1f)] float volume = 0.55f;

    AudioSource source;

    void Awake()
    {
        source = GetComponent<AudioSource>();
        if (track != null)
        {
            source.clip = track;
        }
        source.loop = true;
        source.spatialBlend = 0f;
        Apply();
    }

    void OnEnable()
    {
        GameSettings.Changed += Apply;
        Apply();
        if (source.clip != null && !source.isPlaying)
        {
            source.Play();
        }
    }

    void OnDisable()
    {
        GameSettings.Changed -= Apply;
    }

    void Apply()
    {
        source.volume = volume * GameSettings.MusicVolume;
    }
}
