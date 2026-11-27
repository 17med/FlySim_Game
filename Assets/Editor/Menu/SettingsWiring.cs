using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Finishes the settings panel the main menu was designed with (Tools > Main Menu > Wire Settings).
// The panel was drawn with a resolution, a fullscreen and two volume rows and no script behind them.
// This adds the sound effects row and the name box, puts SettingsMenu on UI_Root with every widget
// wired to it, gives QUIT its click, and hooks the menu music up to the music volume.
// A menu step rather than a hand edit to MainMenu.unity because the scene is usually open with
// unsaved changes. Running it again is safe: it reuses the rows and components it made before.
public static class SettingsWiring
{
    const string MusicTrackPath = "Assets/audio/Music/The Swarm Anthem.mp3";

    const string UiRootName = "UI_Root";
    const string ModalName = "L9_Modal";
    const string PanelName = "Settings_Panel";
    const string MusicRowName = "Row_MusicVolume";
    const string SfxRowName = "Row_SfxVolume";
    const string NameRowName = "Row_PlayerName";
    const string MatchRowName = "Row_MatchLength";
    const string MatchDropdownName = "Match_Dropdown";
    const string ResolutionDropdownName = "Resolution_Dropdown";
    const string SfxSliderName = "Sfx_Slider";
    const string NameInputName = "Name_Input";
    const string DividerName = "Divider";
    const string MusicObjectName = "Stage_Root";

    [MenuItem("Tools/Main Menu/Wire Settings")]
    internal static void Wire()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.name != GameSceneManager.MainMenuScene)
        {
            Debug.LogError($"Wire Settings: open {GameSceneManager.MainMenuScene} first (the open scene is '{scene.name}').");
            return;
        }

        GameObject panel = Find(scene, PanelName);
        GameObject uiRoot = Find(scene, UiRootName);
        GameObject modal = Find(scene, ModalName);
        GameObject musicRow = Find(scene, MusicRowName);
        if (panel == null || uiRoot == null || modal == null || musicRow == null)
        {
            return;
        }

        Slider sfxSlider = EnsureSfxRow(panel, musicRow);
        TMP_InputField nameInput = EnsureNameRow(scene, panel);
        TMP_Dropdown matchDropdown = EnsureMatchRow(scene, panel, musicRow, out TMP_Text matchLabel);
        if (sfxSlider == null || nameInput == null || matchDropdown == null)
        {
            return;
        }

        if (!WireMenu(scene, uiRoot, modal, sfxSlider, nameInput, matchDropdown, matchLabel) || !WireQuit(scene))
        {
            return;
        }
        WireMusic(scene);

        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"Wire Settings: {nameof(SettingsMenu)} is on {UiRootName}, the panel has its sound effects, name and match length rows, and the menu music follows the music volume. Save {scene.name} (Ctrl+S).");
    }

    // ---- the rows the panel was missing --------------------------------------------------------

    // A copy of the music volume row, with its own label and slider, sitting right below it.
    static Slider EnsureSfxRow(GameObject panel, GameObject musicRow)
    {
        GameObject row = ChildIn(panel, SfxRowName);
        if (row == null)
        {
            row = Object.Instantiate(musicRow, panel.transform);
            row.name = SfxRowName;
            Undo.RegisterCreatedObjectUndo(row, "Wire Settings");
            row.transform.SetSiblingIndex(musicRow.transform.GetSiblingIndex() + 1);
            GrowPanel(panel, row);
        }

        TMP_Text label = row.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
        {
            Undo.RecordObject(label, "Wire Settings");
            label.gameObject.name = $"{SfxRowName}_Label";
            label.text = "EFFECTS VOLUME";
        }
        Slider slider = row.GetComponentInChildren<Slider>(true);
        if (slider == null)
        {
            Debug.LogError($"Wire Settings: the copy of {MusicRowName} has no Slider, so there is nothing to set the sound effects volume with.");
            return null;
        }
        slider.gameObject.name = SfxSliderName;
        return slider;
    }

    // How long a raid runs for: another copy of a volume row, whose slider is swapped for a copy of the
    // resolution dropdown, so the pick looks like the panel's other dropdown. SettingsMenu fills in the
    // choices (5 to 30 minutes in fives); the row's label only names the setting.
    static TMP_Dropdown EnsureMatchRow(Scene scene, GameObject panel, GameObject musicRow, out TMP_Text label)
    {
        label = null;
        GameObject row = ChildIn(panel, MatchRowName);
        if (row == null)
        {
            row = Object.Instantiate(musicRow, panel.transform);
            row.name = MatchRowName;
            Undo.RegisterCreatedObjectUndo(row, "Wire Settings");
            // Last of the rows: it is about the game rather than about this machine.
            row.transform.SetAsLastSibling();
            GrowPanel(panel, row);
        }

        label = row.GetComponentInChildren<TMP_Text>(true);
        if (label != null)
        {
            Undo.RecordObject(label, "Wire Settings");
            label.gameObject.name = $"{MatchRowName}_Label";
            label.text = "MATCH LENGTH";
        }
        TMP_Dropdown existing = row.GetComponentInChildren<TMP_Dropdown>(true);
        return existing != null ? existing : ReplaceSliderWithDropdown(scene, row);
    }

    // Fails closed: without the slider's place to fill or a dropdown to copy, the row would have no
    // control at all, which is worse than reporting it and leaving the row as it was.
    static TMP_Dropdown ReplaceSliderWithDropdown(Scene scene, GameObject row)
    {
        Slider slider = row.GetComponentInChildren<Slider>(true);
        TMP_Dropdown template = ComponentIn<TMP_Dropdown>(scene, ResolutionDropdownName);
        if (slider == null || template == null)
        {
            Debug.LogError($"Wire Settings: {MatchRowName} needs its slider and the panel's {ResolutionDropdownName} to make the match length dropdown; nothing was changed in that row.");
            return null;
        }
        var sliderRect = (RectTransform)slider.transform;
        GameObject copy = Object.Instantiate(template.gameObject, sliderRect.parent);
        copy.name = MatchDropdownName;
        Undo.RegisterCreatedObjectUndo(copy, "Wire Settings");
        var rect = (RectTransform)copy.transform;
        // Exactly where the slider was, so the row keeps its layout.
        rect.anchorMin = sliderRect.anchorMin;
        rect.anchorMax = sliderRect.anchorMax;
        rect.pivot = sliderRect.pivot;
        rect.anchoredPosition = sliderRect.anchoredPosition;
        rect.sizeDelta = sliderRect.sizeDelta;
        rect.SetSiblingIndex(sliderRect.GetSiblingIndex());
        var dropdown = copy.GetComponent<TMP_Dropdown>();
        // The copy carries the resolution dropdown's listeners set up in the scene, if any; SettingsMenu
        // adds its own at runtime.
        dropdown.onValueChanged.RemoveAllListeners();
        Undo.DestroyObjectImmediate(slider.gameObject);
        return dropdown;
    }

    // The name the lobby shows the other players. The row is a copy of a volume row with its slider
    // swapped for a copy of the action bar's address box, so the box matches the menu's own style.
    static TMP_InputField EnsureNameRow(Scene scene, GameObject panel)
    {
        GameObject row = ChildIn(panel, NameRowName);
        if (row == null)
        {
            GameObject musicRow = Find(scene, MusicRowName);
            GameObject addressInput = Find(scene, "Address_Input");
            if (musicRow == null || addressInput == null)
            {
                return null;
            }

            row = Object.Instantiate(musicRow, panel.transform);
            row.name = NameRowName;
            Undo.RegisterCreatedObjectUndo(row, "Wire Settings");
            // Straight under the header, because it is the one setting that is not about the machine.
            GameObject divider = ChildIn(panel, DividerName);
            row.transform.SetSiblingIndex(divider != null ? divider.transform.GetSiblingIndex() + 1 : 1);

            Slider slider = row.GetComponentInChildren<Slider>(true);
            if (slider != null)
            {
                Undo.DestroyObjectImmediate(slider.gameObject);
            }

            GameObject box = Object.Instantiate(addressInput, row.transform);
            box.name = NameInputName;
            Undo.RegisterCreatedObjectUndo(box, "Wire Settings");
            LayoutElement element = box.GetComponent<LayoutElement>();
            if (element != null)
            {
                Undo.RecordObject(element, "Wire Settings");
                element.preferredWidth = 280f;
                element.flexibleWidth = 0f;
            }
            GrowPanel(panel, row);
        }

        TMP_Text label = row.transform.GetChild(0).GetComponentInChildren<TMP_Text>(true);
        if (label != null)
        {
            Undo.RecordObject(label, "Wire Settings");
            label.gameObject.name = $"{NameRowName}_Label";
            label.text = "NAME";
        }

        TMP_InputField input = row.GetComponentInChildren<TMP_InputField>(true);
        if (input == null)
        {
            Debug.LogError($"Wire Settings: the {NameRowName} row has no TMP_InputField, so there is nothing to type a name in.");
            return null;
        }
        Undo.RecordObject(input, "Wire Settings");
        input.gameObject.name = NameInputName;
        input.characterLimit = GameSettings.MaxNameLength;
        input.text = "";
        if (input.placeholder is TMP_Text placeholder)
        {
            Undo.RecordObject(placeholder, "Wire Settings");
            placeholder.text = GameSettings.DefaultPlayerName();
        }
        return input;
    }

    // The panel is a fixed size, so a row added to its layout group would be pushed out of the bottom.
    static void GrowPanel(GameObject panel, GameObject row)
    {
        var panelRect = (RectTransform)panel.transform;
        LayoutElement rowElement = row.GetComponent<LayoutElement>();
        float rowHeight = rowElement != null && rowElement.preferredHeight > 0f ? rowElement.preferredHeight : 40f;
        VerticalLayoutGroup layout = panel.GetComponent<VerticalLayoutGroup>();
        float spacing = layout != null ? layout.spacing : 0f;
        Undo.RecordObject(panelRect, "Wire Settings");
        panelRect.sizeDelta += new Vector2(0f, rowHeight + spacing);
    }

    // ---- the script behind the panel -----------------------------------------------------------

    static bool WireMenu(Scene scene, GameObject uiRoot, GameObject modal, Slider sfxSlider, TMP_InputField nameInput,
                         TMP_Dropdown matchDropdown, TMP_Text matchLabel)
    {
        SettingsMenu menu = uiRoot.GetComponent<SettingsMenu>();
        if (menu == null)
        {
            menu = Undo.AddComponent<SettingsMenu>(uiRoot);
        }
        Undo.RecordObject(menu, "Wire Settings");

        menu.modalRoot = modal;
        menu.openButton = ButtonIn(scene, "Settings_Button");
        // The dimmed backdrop closes the panel too, which is what a click outside it should do.
        menu.closeButtons = new[] { ButtonIn(scene, "Close_Button"), ButtonIn(scene, "Back_Button"), ButtonIn(scene, "Dimmer") };
        menu.nameInput = nameInput;
        menu.fullscreenToggle = ComponentIn<Toggle>(scene, "Fullscreen_Toggle");
        menu.resolutionDropdown = ComponentIn<TMP_Dropdown>(scene, "Resolution_Dropdown");
        menu.masterSlider = ComponentIn<Slider>(scene, "Master_Slider");
        menu.musicSlider = ComponentIn<Slider>(scene, "Music_Slider");
        menu.sfxSlider = sfxSlider;
        menu.matchLengthDropdown = matchDropdown;
        menu.matchLengthLabel = matchLabel;

        if (menu.openButton == null || menu.masterSlider == null || menu.musicSlider == null)
        {
            Debug.LogError("Wire Settings: the panel is missing Settings_Button, Master_Slider or Music_Slider; nothing was wired.");
            return false;
        }

        // The buttons showed and hid the panel with a SetActive click set up in the scene. SettingsMenu
        // does that itself now, and fills the controls in on the way, so the old clicks have to go or
        // the panel would open before its sliders say what is saved.
        ClearPersistentClicks(menu.openButton);
        foreach (Button closeButton in menu.closeButtons)
        {
            ClearPersistentClicks(closeButton);
        }
        // Escape opens the panel, so the modal must start hidden, not hidden-by-accident.
        if (modal.activeSelf)
        {
            Undo.RecordObject(modal, "Wire Settings");
            modal.SetActive(false);
        }
        EditorUtility.SetDirty(menu);
        return true;
    }

    static bool WireQuit(Scene scene)
    {
        Button quit = ButtonIn(scene, "Quit_Button");
        if (quit == null)
        {
            return true;
        }
        GameSceneManager manager = Object.FindAnyObjectByType<GameSceneManager>(FindObjectsInactive.Include);
        if (manager == null)
        {
            Debug.LogWarning($"Wire Settings: no {nameof(GameSceneManager)} in the scene, so QUIT was left alone. Run Tools > Main Menu > Wire Scene Flow first.");
            return true;
        }
        Undo.RecordObject(quit, "Wire Settings");
        ClearPersistentClicks(quit);
        UnityEventTools.AddVoidPersistentListener(quit.onClick, manager.QuitGame);
        return true;
    }

    // ---- the music -----------------------------------------------------------------------------

    static void WireMusic(Scene scene)
    {
        GameObject stage = Find(scene, MusicObjectName);
        if (stage == null)
        {
            return;
        }
        AudioSource source = stage.GetComponent<AudioSource>();
        if (source == null)
        {
            Debug.LogWarning($"Wire Settings: {MusicObjectName} has no AudioSource, so the menu music was left alone.");
            return;
        }
        AudioClip track = AssetDatabase.LoadAssetAtPath<AudioClip>(MusicTrackPath);
        if (track == null)
        {
            Debug.LogError($"Wire Settings: no audio clip at {MusicTrackPath}. Was it moved or renamed?");
            return;
        }

        Undo.RecordObject(source, "Wire Settings");
        source.clip = track;
        source.loop = true;
        // MusicSource starts it and owns its volume from here on.
        source.playOnAwake = false;
        source.spatialBlend = 0f;

        MusicSource music = stage.GetComponent<MusicSource>();
        if (music == null)
        {
            music = Undo.AddComponent<MusicSource>(stage);
        }
        var fields = new SerializedObject(music);
        fields.FindProperty("track").objectReferenceValue = track;
        fields.ApplyModifiedProperties();
    }

    // ---- finding things ------------------------------------------------------------------------

    // Walks the scene instead of GameObject.Find, so the hidden settings panel is searched too.
    static GameObject Find(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                if (transform.name == name)
                {
                    return transform.gameObject;
                }
            }
        }
        Debug.LogError($"Wire Settings: no GameObject named {name} in {scene.name}.");
        return null;
    }

    static GameObject ChildIn(GameObject parent, string name)
    {
        Transform child = parent.transform.Find(name);
        return child != null ? child.gameObject : null;
    }

    static T ComponentIn<T>(Scene scene, string name) where T : Component
    {
        GameObject found = Find(scene, name);
        if (found == null)
        {
            return null;
        }
        T component = found.GetComponent<T>();
        if (component == null)
        {
            Debug.LogError($"Wire Settings: {name} has no {typeof(T).Name}.");
        }
        return component;
    }

    static Button ButtonIn(Scene scene, string name)
    {
        return ComponentIn<Button>(scene, name);
    }

    static void ClearPersistentClicks(Button button)
    {
        if (button == null)
        {
            return;
        }
        Undo.RecordObject(button, "Wire Settings");
        for (int i = button.onClick.GetPersistentEventCount() - 1; i >= 0; i--)
        {
            UnityEventTools.RemovePersistentListener(button.onClick, i);
        }
    }
}
