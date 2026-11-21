using System;
using UnityEngine;

// Everything the player can change in the settings panel, saved in PlayerPrefs and applied the
// moment it is set. Static, so the main menu, the in-game pause menu and the lobby all read the
// same values without any scene setup.
public static class GameSettings
{
    public const int MaxNameLength = 12;

    const string MasterVolumeKey = "Settings.MasterVolume";
    const string MusicVolumeKey = "Settings.MusicVolume";
    const string SfxVolumeKey = "Settings.SfxVolume";
    const string FullscreenKey = "Settings.Fullscreen";
    const string ScreenWidthKey = "Settings.ScreenWidth";
    const string ScreenHeightKey = "Settings.ScreenHeight";
    // The lobby shipped reading this key, so saved names survive the update.
    const string PlayerNameKey = "PlayerName";

    const float DefaultVolume = 0.8f;

    // Raised after any setting changes, so open sliders and playing sounds follow along.
    public static event Action Changed;

    static float masterVolume = DefaultVolume;
    static float musicVolume = DefaultVolume;
    static float sfxVolume = DefaultVolume;
    static bool fullscreen = true;
    static Vector2Int resolution;
    static string playerName = "";
    static bool loaded;

    public static float MasterVolume
    {
        get { Load(); return masterVolume; }
        set { Load(); masterVolume = Mathf.Clamp01(value); PlayerPrefs.SetFloat(MasterVolumeKey, masterVolume); Apply(); }
    }

    public static float MusicVolume
    {
        get { Load(); return musicVolume; }
        set { Load(); musicVolume = Mathf.Clamp01(value); PlayerPrefs.SetFloat(MusicVolumeKey, musicVolume); Apply(); }
    }

    public static float SfxVolume
    {
        get { Load(); return sfxVolume; }
        set { Load(); sfxVolume = Mathf.Clamp01(value); PlayerPrefs.SetFloat(SfxVolumeKey, sfxVolume); Apply(); }
    }

    public static bool Fullscreen
    {
        get { Load(); return fullscreen; }
        set { Load(); fullscreen = value; PlayerPrefs.SetInt(FullscreenKey, value ? 1 : 0); ApplyScreen(); Apply(); }
    }

    public static Vector2Int Resolution
    {
        get { Load(); return resolution; }
        set
        {
            Load();
            resolution = value;
            PlayerPrefs.SetInt(ScreenWidthKey, value.x);
            PlayerPrefs.SetInt(ScreenHeightKey, value.y);
            ApplyScreen();
            Apply();
        }
    }

    // Shown to the other players in the lobby. Always something readable: a name the player typed,
    // otherwise the machine's own user name.
    public static string PlayerName
    {
        get
        {
            Load();
            return string.IsNullOrWhiteSpace(playerName) ? DefaultPlayerName() : playerName;
        }
        set
        {
            Load();
            playerName = Clean(value);
            PlayerPrefs.SetString(PlayerNameKey, playerName);
            Apply();
        }
    }

    // Trimmed, upper case and short enough for a lobby row and for FixedString32Bytes.
    public static string Clean(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "";
        }
        name = name.Trim().ToUpperInvariant();
        // Trimmed again after the cut: "A VERY LONG NAME" would otherwise end on a space.
        return name.Length > MaxNameLength ? name.Substring(0, MaxNameLength).TrimEnd() : name;
    }

    public static string DefaultPlayerName()
    {
        string name = Clean(Environment.UserName);
        if (name.Length == 0)
        {
            name = Clean(SystemInfo.deviceName);
        }
        return name.Length == 0 ? "FLYER" : name;
    }

    // The resolutions the dropdown offers: every distinct size the screen supports, largest first,
    // with the current one guaranteed to be in the list.
    public static Vector2Int[] SupportedResolutions()
    {
        var sizes = new System.Collections.Generic.List<Vector2Int> { new Vector2Int(Screen.width, Screen.height) };
        foreach (UnityEngine.Resolution supported in Screen.resolutions)
        {
            var size = new Vector2Int(supported.width, supported.height);
            if (!sizes.Contains(size))
            {
                sizes.Add(size);
            }
        }
        sizes.Sort((a, b) => b.x * b.y - a.x * a.y);
        return sizes.ToArray();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void LoadOnStart()
    {
        Load();
        ApplyScreen();
        // Removed first, so a play with domain reload turned off does not stack the handler up.
        Application.quitting -= Save;
        Application.quitting += Save;
    }

    static void Load()
    {
        if (loaded)
        {
            return;
        }
        // Set first: the property getters below call back into Load.
        loaded = true;
        masterVolume = PlayerPrefs.GetFloat(MasterVolumeKey, DefaultVolume);
        musicVolume = PlayerPrefs.GetFloat(MusicVolumeKey, DefaultVolume);
        sfxVolume = PlayerPrefs.GetFloat(SfxVolumeKey, DefaultVolume);
        fullscreen = PlayerPrefs.GetInt(FullscreenKey, Screen.fullScreen ? 1 : 0) == 1;
        resolution = new Vector2Int(PlayerPrefs.GetInt(ScreenWidthKey, Screen.width),
                                    PlayerPrefs.GetInt(ScreenHeightKey, Screen.height));
        playerName = Clean(PlayerPrefs.GetString(PlayerNameKey, ""));
        AudioListener.volume = masterVolume;
    }

    // Writes the settings to disk. PlayerPrefs.Set only changes them in memory, and saving on every
    // frame of a slider drag would hit the disk a hundred times, so this is called when a settings
    // panel closes and when the game quits.
    public static void Save()
    {
        PlayerPrefs.Save();
    }

    static void Apply()
    {
        AudioListener.volume = masterVolume;
        Changed?.Invoke();
    }

    static void ApplyScreen()
    {
#if UNITY_EDITOR
        // The Game view is sized by the Editor, not by the player's setting.
        return;
#else
        if (resolution.x <= 0 || resolution.y <= 0)
        {
            return;
        }
        FullScreenMode mode = fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;
        if (Screen.width != resolution.x || Screen.height != resolution.y || Screen.fullScreenMode != mode)
        {
            Screen.SetResolution(resolution.x, resolution.y, mode);
        }
#endif
    }
}
