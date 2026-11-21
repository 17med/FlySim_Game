using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

// Escape during a game: the sound levels and a way out.
// It builds its own canvas in code and installs itself when a kitchen scene loads, so neither
// Kitchen nor Kitchen_online needs any UI of its own and a scene played straight from the Editor
// gets the menu too.
// Opening it stops the gameplay input (every controller reads the "Player" action map) and frees the
// cursor. Time only stops in a single player game: in a room the other players keep flying.
public class PauseMenu : MonoBehaviour
{
    const string PlayerActionMap = "Player";
    const int SortingOrder = 900;

    static readonly Color Dim = new Color(0f, 0f, 0f, 0.78f);
    static readonly Color PanelFill = new Color(0.07f, 0.07f, 0.09f, 0.96f);
    static readonly Color PanelRim = new Color(0.85f, 0.2f, 0.2f, 0.55f);
    static readonly Color Ink = new Color(0.93f, 0.93f, 0.95f, 1f);
    static readonly Color InkDim = new Color(0.62f, 0.62f, 0.68f, 1f);
    static readonly Color ButtonFill = new Color(0.15f, 0.15f, 0.18f, 1f);
    static readonly Color LeaveFill = new Color(0.45f, 0.12f, 0.12f, 1f);

    GameObject root;
    Slider masterSlider;
    Slider musicSlider;
    Slider sfxSlider;
    InputActionMap playerActions;
    bool open;
    // Set while the sliders are being filled in from GameSettings, so they do not write the value
    // straight back.
    bool filling;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Install()
    {
        SceneManager.sceneLoaded += (scene, _) =>
        {
            if (scene.name != GameSceneManager.KitchenScene && scene.name != GameSceneManager.KitchenOnlineScene)
            {
                return;
            }
            if (FindAnyObjectByType<PauseMenu>() == null)
            {
                new GameObject(nameof(PauseMenu)).AddComponent<PauseMenu>();
            }
        };
    }

    void Awake()
    {
        playerActions = InputSystem.actions != null ? InputSystem.actions.FindActionMap(PlayerActionMap) : null;
        Build();
        root.SetActive(false);
    }

    void OnDestroy()
    {
        // Leaving the scene with the menu up must not leave the game without input or with time stopped.
        if (open)
        {
            Time.timeScale = 1f;
            playerActions?.Enable();
        }
    }

    void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
        {
            if (open)
            {
                Resume();
            }
            else
            {
                Open();
            }
        }
    }

    public void Open()
    {
        open = true;
        Fill();
        root.SetActive(true);
        playerActions?.Disable();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
        // Stopping time in a room would only stop this player's own screen, networking included.
        if (!IsOnline)
        {
            Time.timeScale = 0f;
        }
    }

    public void Resume()
    {
        open = false;
        root.SetActive(false);
        GameSettings.Save();
        Time.timeScale = 1f;
        playerActions?.Enable();
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    // Out of the game and back to the main menu, leaving the room on the way if there is one.
    public void Leave()
    {
        open = false;
        root.SetActive(false);
        GameSettings.Save();
        Time.timeScale = 1f;
        playerActions?.Enable();
        if (IsOnline)
        {
            NetworkManager.Singleton.Shutdown();
        }
        GameSession.Reset();
        GameSceneManager.Instance.LoadMainMenu();
    }

    static bool IsOnline => NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;

    void Fill()
    {
        filling = true;
        masterSlider.SetValueWithoutNotify(GameSettings.MasterVolume);
        musicSlider.SetValueWithoutNotify(GameSettings.MusicVolume);
        sfxSlider.SetValueWithoutNotify(GameSettings.SfxVolume);
        filling = false;
    }

    // ---- the canvas ----------------------------------------------------------------------------

    void Build()
    {
        root = new GameObject("Pause_Canvas");
        root.transform.SetParent(transform, false);
        Canvas canvas = root.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;
        CanvasScaler scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        root.AddComponent<GraphicRaycaster>();

        Image dim = NewImage("Dim", root.transform, Dim);
        Stretch(dim.rectTransform);

        RectTransform panel = NewImage("Panel", root.transform, PanelFill).rectTransform;
        panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
        panel.pivot = new Vector2(0.5f, 0.5f);
        panel.sizeDelta = new Vector2(560f, 0f);
        var layout = panel.gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(44, 44, 36, 36);
        layout.spacing = 18f;
        layout.childControlHeight = true;
        layout.childControlWidth = true;
        layout.childForceExpandHeight = false;
        var fitter = panel.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        Image rim = NewImage("Panel_Rim", panel, PanelRim);
        Stretch(rim.rectTransform, -3f);
        rim.rectTransform.SetAsFirstSibling();
        rim.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;

        NewLabel("Header", panel, "PAUSED", 46f, Ink, TextAlignmentOptions.Center);
        NewLabel("Caption", panel, "SOUND", 22f, InkDim, TextAlignmentOptions.Left);
        masterSlider = NewSliderRow(panel, "MASTER", value => GameSettings.MasterVolume = value);
        musicSlider = NewSliderRow(panel, "MUSIC", value => GameSettings.MusicVolume = value);
        sfxSlider = NewSliderRow(panel, "EFFECTS", value => GameSettings.SfxVolume = value);
        NewButton(panel, "Resume_Button", "RESUME", ButtonFill, Resume);
        NewButton(panel, "Leave_Button", "LEAVE GAME", LeaveFill, Leave);
    }

    Slider NewSliderRow(Transform parent, string label, UnityEngine.Events.UnityAction<float> set)
    {
        var row = new GameObject($"Row_{label}", typeof(RectTransform));
        row.transform.SetParent(parent, false);
        var rowLayout = row.AddComponent<HorizontalLayoutGroup>();
        rowLayout.spacing = 16f;
        rowLayout.childControlWidth = true;
        rowLayout.childForceExpandWidth = false;
        rowLayout.childAlignment = TextAnchor.MiddleLeft;
        row.AddComponent<LayoutElement>().minHeight = 46f;

        TextMeshProUGUI text = NewLabel("Label", row.transform, label, 24f, InkDim, TextAlignmentOptions.Left);
        text.gameObject.AddComponent<LayoutElement>().preferredWidth = 160f;

        var sliderObject = new GameObject("Slider", typeof(RectTransform));
        sliderObject.transform.SetParent(row.transform, false);
        var element = sliderObject.AddComponent<LayoutElement>();
        element.flexibleWidth = 1f;
        element.minHeight = 22f;

        Image background = NewImage("Background", sliderObject.transform, new Color(1f, 1f, 1f, 0.12f));
        background.rectTransform.anchorMin = new Vector2(0f, 0.5f);
        background.rectTransform.anchorMax = new Vector2(1f, 0.5f);
        background.rectTransform.sizeDelta = new Vector2(0f, 8f);

        // The Slider rewrites its fill's and handle's anchors every frame, so the bar's thickness has
        // to come from the areas around them, not from the fill and handle themselves.
        var fillArea = new GameObject("Fill Area", typeof(RectTransform));
        fillArea.transform.SetParent(sliderObject.transform, false);
        var fillAreaRect = (RectTransform)fillArea.transform;
        fillAreaRect.anchorMin = new Vector2(0f, 0.5f);
        fillAreaRect.anchorMax = new Vector2(1f, 0.5f);
        fillAreaRect.sizeDelta = new Vector2(0f, 8f);
        Image fill = NewImage("Fill", fillArea.transform, new Color(0.85f, 0.25f, 0.25f, 1f));
        Stretch(fill.rectTransform);

        var handleArea = new GameObject("Handle Slide Area", typeof(RectTransform));
        handleArea.transform.SetParent(sliderObject.transform, false);
        // Inset by half a handle, so the handle stays inside the bar at both ends.
        Stretch((RectTransform)handleArea.transform, 10f);
        Image handle = NewImage("Handle", handleArea.transform, Ink);
        handle.rectTransform.sizeDelta = new Vector2(20f, 0f);

        Slider slider = sliderObject.AddComponent<Slider>();
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = 0f;
        slider.maxValue = 1f;
        slider.onValueChanged.AddListener(value =>
        {
            if (!filling)
            {
                set(value);
            }
        });
        return slider;
    }

    void NewButton(Transform parent, string name, string label, Color fill, UnityEngine.Events.UnityAction click)
    {
        Image background = NewImage(name, parent, fill);
        background.gameObject.AddComponent<LayoutElement>().minHeight = 58f;
        Button button = background.gameObject.AddComponent<Button>();
        button.targetGraphic = background;
        button.onClick.AddListener(click);

        TextMeshProUGUI text = NewLabel("Label", background.transform, label, 26f, Ink, TextAlignmentOptions.Center);
        Stretch(text.rectTransform);
    }

    static Image NewImage(string name, Transform parent, Color color)
    {
        var imageObject = new GameObject(name, typeof(RectTransform));
        imageObject.transform.SetParent(parent, false);
        Image image = imageObject.AddComponent<Image>();
        image.color = color;
        return image;
    }

    static TextMeshProUGUI NewLabel(string name, Transform parent, string content, float size, Color color,
                                    TextAlignmentOptions alignment)
    {
        var textObject = new GameObject(name, typeof(RectTransform));
        textObject.transform.SetParent(parent, false);
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.text = content;
        text.fontSize = size;
        text.color = color;
        text.alignment = alignment;
        text.raycastTarget = false;
        return text;
    }

    static void Stretch(RectTransform rect, float inset = 0f)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }
}
