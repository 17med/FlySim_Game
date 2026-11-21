using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Plays sound effects at the level set in the settings. Master volume is the AudioListener's, which
// covers everything at once; this is the separate sound effects level on top of it.
// A source keeps the volume it was given in the prefab: the first time one is played that volume is
// remembered, and the sound effects level is applied over it from then on.
public static class GameAudio
{
    static readonly Dictionary<AudioSource, float> authoredVolumes = new Dictionary<AudioSource, float>();

    // The level a sound effect set to `volume` in the Inspector should actually play at.
    public static float Scale(float volume)
    {
        return volume * GameSettings.SfxVolume;
    }

    public static void PlaySfx(AudioSource source)
    {
        if (source == null)
        {
            return;
        }
        source.volume = Scale(AuthoredVolume(source));
        source.Play();
    }

    public static void PlaySfx(AudioSource source, AudioClip clip)
    {
        if (source == null || clip == null)
        {
            return;
        }
        source.clip = clip;
        PlaySfx(source);
    }

    // A one-shot heard from a place in the world, for sounds with no source of their own.
    public static void PlaySfxAt(AudioClip clip, Vector3 position)
    {
        if (clip == null)
        {
            return;
        }
        AudioSource.PlayClipAtPoint(clip, position, GameSettings.SfxVolume);
    }

    static float AuthoredVolume(AudioSource source)
    {
        if (!authoredVolumes.TryGetValue(source, out float volume))
        {
            volume = source.volume;
            authoredVolumes[source] = volume;
        }
        return volume;
    }

    // The sources of the scene being left are gone; their remembered volumes would never be read again.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void ForgetSourcesOnSceneChange()
    {
        SceneManager.sceneUnloaded += _ => authoredVolumes.Clear();
    }
}
