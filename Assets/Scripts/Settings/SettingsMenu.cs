using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// The settings panel in the main menu: name, screen and sound, all reading and writing GameSettings.
// Lives on an object that stays active (UI_Root) rather than on the panel itself, so Escape can open
// a panel that is currently hidden.
// The references are filled in by Tools > Main Menu > Wire Settings.
public class SettingsMenu : MonoBehaviour
{
    [Header("The panel")]
    [Tooltip("Shown and hidden by the SETTINGS button and by Escape (L9_Modal).")]
    public GameObject modalRoot;
    public Button openButton;
    [Tooltip("CLOSE and BACK: either one hides the panel.")]
    public Button[] closeButtons = new Button[0];

    [Header("Player")]
    public TMP_InputField nameInput;

    [Header("Screen")]
    public Toggle fullscreenToggle;
    public TMP_Dropdown resolutionDropdown;

    [Header("Sound")]
    public Slider masterSlider;
    public Slider musicSlider;
    public Slider sfxSlider;

    [Header("Match")]
    [Tooltip("How many minutes a raid runs for, picked from a short list (MatchLengthChoices). Only the host's value counts; the lobby sends it to the room.")]
    public TMP_Dropdown matchLengthDropdown;
    [Tooltip("The row's own label. The dropdown shows the minutes, so this only names the setting.")]
    public TMP_Text matchLengthLabel;

    // A pick from a handful of round numbers instead of a slider: nobody plans a 13 minute raid, and
    // a dropdown can be set in one click without dragging to an exact number. Values outside
    // GameSettings' range are left out, so the list cannot offer a length the game would clamp.
    static readonly int[] MatchLengthChoices = { 5, 10, 15, 20, 25, 30 };
    int[] matchLengths = new int[0];

    Vector2Int[] resolutions = new Vector2Int[0];
    // Set while the controls are being filled in from GameSettings, so their change callbacks do not
    // write the value straight back and fight the player's drag.
    bool filling;

    void Start()
    {
        // Fails loudly: a half-wired panel otherwise looks like a panel whose sliders simply do nothing.
        if (modalRoot == null || masterSlider == null || musicSlider == null || sfxSlider == null
            || nameInput == null || fullscreenToggle == null || resolutionDropdown == null)
        {
            Debug.LogWarning($"{nameof(SettingsMenu)}: some widgets are not assigned, so those settings do nothing. " +
                             "Run Tools > Main Menu > Wire Settings with MainMenu open, then save the scene.", this);
        }

        resolutions = GameSettings.SupportedResolutions();
        FillResolutionOptions();
        // Before Fill, which selects the saved length among these options.
        FillMatchLengthOptions();
        Fill();

        Bind(openButton, Open);
        foreach (Button closeButton in closeButtons ?? new Button[0])
        {
            Bind(closeButton, Close);
        }

        if (nameInput != null)
        {
            nameInput.characterLimit = GameSettings.MaxNameLength;
            nameInput.onEndEdit.AddListener(SetName);
            nameInput.onDeselect.AddListener(SetName);
        }
        Listen(masterSlider, value => GameSettings.MasterVolume = value);
        Listen(musicSlider, value => GameSettings.MusicVolume = value);
        Listen(sfxSlider, value => GameSettings.SfxVolume = value);
        ListenMatchLength();
        if (fullscreenToggle != null)
        {
            fullscreenToggle.onValueChanged.AddListener(value =>
            {
                if (!filling)
                {
                    GameSettings.Fullscreen = value;
                }
            });
        }
        if (resolutionDropdown != null)
        {
            resolutionDropdown.onValueChanged.AddListener(SetResolution);
        }
    }

    void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || !keyboard.escapeKey.wasPressedThisFrame || modalRoot == null)
        {
            return;
        }
        if (modalRoot.activeSelf)
        {
            Close();
        }
        else
        {
            Open();
        }
    }

    public void Open()
    {
        if (modalRoot == null)
        {
            return;
        }
        Fill();
        modalRoot.SetActive(true);
    }

    public void Close()
    {
        if (modalRoot != null)
        {
            modalRoot.SetActive(false);
        }
        GameSettings.Save();
    }

    // Every control shows what is saved. Called again each time the panel opens, because the pause
    // menu in a game changes the same sound levels.
    void Fill()
    {
        filling = true;
        if (nameInput != null)
        {
            nameInput.text = GameSettings.PlayerName;
        }
        if (masterSlider != null)
        {
            masterSlider.SetValueWithoutNotify(GameSettings.MasterVolume);
        }
        if (musicSlider != null)
        {
            musicSlider.SetValueWithoutNotify(GameSettings.MusicVolume);
        }
        if (sfxSlider != null)
        {
            sfxSlider.SetValueWithoutNotify(GameSettings.SfxVolume);
        }
        if (fullscreenToggle != null)
        {
            fullscreenToggle.SetIsOnWithoutNotify(GameSettings.Fullscreen);
        }
        if (matchLengthDropdown != null)
        {
            matchLengthDropdown.SetValueWithoutNotify(NearestMatchLengthIndex(GameSettings.MatchMinutes));
            matchLengthDropdown.RefreshShownValue();
        }
        if (resolutionDropdown != null)
        {
            resolutionDropdown.SetValueWithoutNotify(CurrentResolutionIndex());
            resolutionDropdown.RefreshShownValue();
        }
        filling = false;
    }

    void FillResolutionOptions()
    {
        if (resolutionDropdown == null)
        {
            return;
        }
        resolutionDropdown.ClearOptions();
        var labels = new System.Collections.Generic.List<string>(resolutions.Length);
        foreach (Vector2Int size in resolutions)
        {
            labels.Add($"{size.x} x {size.y}");
        }
        resolutionDropdown.AddOptions(labels);
    }

    int CurrentResolutionIndex()
    {
        Vector2Int current = GameSettings.Resolution;
        for (int i = 0; i < resolutions.Length; i++)
        {
            if (resolutions[i] == current)
            {
                return i;
            }
        }
        return 0;
    }

    void SetResolution(int index)
    {
        if (filling || index < 0 || index >= resolutions.Length)
        {
            return;
        }
        GameSettings.Resolution = resolutions[index];
    }

    void SetName(string typed)
    {
        if (filling)
        {
            return;
        }
        GameSettings.PlayerName = typed;
        // Shows what was actually kept: trimmed, upper case and cut to length.
        nameInput.SetTextWithoutNotify(GameSettings.PlayerName);
    }

    // Its own binding rather than Listen's: this slider counts whole minutes from 10 to 30 instead
    // of running 0 to 1, and the row's label has to show the number as it is dragged.
    void FillMatchLengthOptions()
    {
        if (matchLengthLabel != null)
        {
            matchLengthLabel.text = "MATCH LENGTH";
        }
        matchLengths = System.Array.FindAll(MatchLengthChoices,
            minutes => minutes >= GameSettings.MinMatchMinutes && minutes <= GameSettings.MaxMatchMinutes);
        if (matchLengthDropdown == null)
        {
            return;
        }
        matchLengthDropdown.ClearOptions();
        matchLengthDropdown.AddOptions(System.Array.ConvertAll(matchLengths, minutes => $"{minutes} MIN").ToList());
    }

    void ListenMatchLength()
    {
        if (matchLengthDropdown == null)
        {
            return;
        }
        matchLengthDropdown.onValueChanged.AddListener(index =>
        {
            if (!filling && index >= 0 && index < matchLengths.Length)
            {
                GameSettings.MatchMinutes = matchLengths[index];
            }
        });
    }

    // The saved length may be one the list does not offer (an older build saved any whole minute), so
    // the closest choice is shown; the saved value itself only changes when the player picks one.
    int NearestMatchLengthIndex(int minutes)
    {
        int best = 0;
        for (int i = 1; i < matchLengths.Length; i++)
        {
            if (Mathf.Abs(matchLengths[i] - minutes) < Mathf.Abs(matchLengths[best] - minutes))
            {
                best = i;
            }
        }
        return best;
    }

    void Listen(Slider slider, UnityEngine.Events.UnityAction<float> set)
    {
        if (slider == null)
        {
            return;
        }
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.onValueChanged.AddListener(value =>
        {
            if (!filling)
            {
                set(value);
            }
        });
    }

    static void Bind(Button button, UnityEngine.Events.UnityAction click)
    {
        if (button != null)
        {
            button.onClick.AddListener(click);
        }
    }
}
