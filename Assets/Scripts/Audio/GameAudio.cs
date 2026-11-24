using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

// Plays sound effects at the level set in the settings. Master volume is the AudioListener's, which
// covers everything at once; this is the separate sound effects level on top of it.
// A source keeps the volume it was given in the prefab: the first time one is played that volume is
// remembered, and the sound effects level is applied over it from then on.
public static class GameAudio
{
    // A pitch at or below nothing never finishes playing, and dividing the clip's length by it to
    // work out when to clean up would hand back infinity.
    const float MinPitch = 0.05f;
    // Seconds a one-shot's object is kept past the end of its clip, so rounding never clips the tail.
    const float DestroyGrace = 0.1f;

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

    // The same, at a pitch and a level of its own. PlayClipAtPoint has no pitch, so the throwaway
    // source it would have made is made here instead - with the same defaults, so the two carry the
    // same distance. This is what lets one clip do two jobs: the project ships a hand swinging
    // through the air, and that clip dropped most of an octave is a hand landing on something.
    // `volume` is a share of the sound effects level and cannot go above it: an AudioSource clamps its
    // own volume to 1, so the way to make one sound louder than another is to turn the other down.
    public static void PlaySfxAt(AudioClip clip, Vector3 position, float volume, float pitch)
    {
        if (clip == null)
        {
            return;
        }
        var host = new GameObject($"One shot sfx ({clip.name})");
        host.transform.position = position;
        AudioSource source = host.AddComponent<AudioSource>();
        source.clip = clip;
        source.volume = Scale(volume);
        source.pitch = Mathf.Max(pitch, MinPitch);
        source.spatialBlend = 1f;
        source.Play();
        // Pitch stretches the clip as well as lowering it, so a half-pitch clip runs twice as long;
        // destroying it on the clip's own length would cut the heaviest sounds off halfway.
        Object.Destroy(host, clip.length / source.pitch + DestroyGrace);
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
