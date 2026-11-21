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
    [Tooltip("How many minutes a raid runs for, in whole minutes from 10 to 30. Only the host's value counts; the lobby sends it to the room.")]
    public Slider matchLengthSlider;
    [Tooltip("The row's own label, rewritten to read \"MATCH LENGTH  ·  12 MIN\" so the number is visible while dragging.")]
    public TMP_Text matchLengthLabel;

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
        if (matchLengthSlider != null)
        {
            matchLengthSlider.SetValueWithoutNotify(GameSettings.MatchMinutes);
        }
        ShowMatchLength(GameSettings.MatchMinutes);
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
    void ListenMatchLength()
    {
        if (matchLengthSlider == null)
        {
            return;
        }
        matchLengthSlider.minValue = GameSettings.MinMatchMinutes;
        matchLengthSlider.maxValue = GameSettings.MaxMatchMinutes;
        matchLengthSlider.wholeNumbers = true;
        matchLengthSlider.onValueChanged.AddListener(value =>
        {
            int minutes = Mathf.RoundToInt(value);
            if (!filling)
            {
                GameSettings.MatchMinutes = minutes;
            }
            ShowMatchLength(minutes);
        });
    }

    void ShowMatchLength(int minutes)
    {
        if (matchLengthLabel != null)
        {
            matchLengthLabel.text = $"MATCH LENGTH  ·  {minutes} MIN";
        }
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
