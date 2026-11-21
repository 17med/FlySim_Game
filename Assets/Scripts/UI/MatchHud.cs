using TMPro;
using UnityEngine;
using UnityEngine.UI;

// What the players need to see while the kitchen is being raided: how long is left, how much couscous
// is left, and what the key under their finger does.
// Built in code and added by MatchDirector, the way PauseMenu builds itself, so neither kitchen scene
// needs any UI of its own. It sorts below the pause menu, which should cover it when it opens.
// Everything it shows comes from MatchDirector; it holds no state of its own beyond its animations.
public class MatchHud : MonoBehaviour
{
    const int SortingOrder = 850;
    // How long the "hold E to eat" line stays up at the start of a match, in seconds.
    const float HintSeconds = 14f;
    // Pulses per second of the clock in the final minute.
    const float AlarmRate = 2f;
    // How often the scene is swept for this machine's own fly while it has not been found yet.
    const float EaterSearchInterval = 0.5f;

    static readonly Color Ink = new Color(0.95f, 0.95f, 0.97f, 1f);
    static readonly Color InkDim = new Color(0.72f, 0.72f, 0.78f, 0.9f);
    static readonly Color Alarm = new Color(1f, 0.33f, 0.28f, 1f);
    static readonly Color BarTrack = new Color(0f, 0f, 0f, 0.45f);
    static readonly Color BarFill = new Color(0.98f, 0.76f, 0.3f, 1f);
    static readonly Color BarLow = new Color(0.95f, 0.38f, 0.25f, 1f);
    static readonly Color Scrim = new Color(0f, 0f, 0f, 0.55f);
    static readonly Color Win = new Color(0.55f, 0.95f, 0.55f, 1f);
    static readonly Color Lose = new Color(1f, 0.5f, 0.45f, 1f);

    MatchDirector match;
    TMP_Text clockText;
    TMP_Text foodText;
    RectTransform clockRect;
    Image foodFill;
    TMP_Text stateText;
    TMP_Text hintText;
    GameObject resultRoot;
    TMP_Text resultText;
    TMP_Text resultNoteText;
    FlyEating localEater;
    float eaterSearchAt;
    float hintLeft = HintSeconds;
    float alarmPhase;
    Vector2 clockRestPosition;

    void Awake()
    {
        match = GetComponent<MatchDirector>();
        Build();
    }

    void Update()
    {
        if (match == null)
        {
            return;
        }
        ShowClock();
        ShowFood();
        ShowState();
        ShowResult();
    }

    // ---- what it says --------------------------------------------------------------------------

    void ShowClock()
    {
        clockText.text = MatchDirector.Clock(match.SecondsLeft);
        if (!match.IsFinalMinute)
        {
            clockText.color = Ink;
            clockRect.anchoredPosition = clockRestPosition;
            clockRect.localScale = Vector3.one;
            return;
        }

        // The last minute: the clock beats, so it is felt rather than read.
        alarmPhase += Time.deltaTime * AlarmRate;
        float beat = 0.5f + 0.5f * Mathf.Sin(alarmPhase * Mathf.PI * 2f);
        clockText.color = Color.Lerp(Ink, Alarm, beat);
        clockRect.localScale = Vector3.one * (1f + 0.06f * beat);
    }

    void ShowFood()
    {
        float left = Mathf.Clamp01(match.FoodLeft);
        foodFill.rectTransform.anchorMax = new Vector2(left, 1f);
        foodFill.color = Color.Lerp(BarLow, BarFill, Mathf.Clamp01(left * 3f));
        foodText.text = $"COUSCOUS  {Mathf.CeilToInt(left * 100f)}%";
    }

    // The line under the bar: what this player should be doing, and why they cannot.
    void ShowState()
    {
        if (hintLeft > 0f)
        {
            hintLeft -= Time.deltaTime;
            hintText.text = GameSession.LocalRole == PlayerRole.Human
                ? "KEEP THE FLIES OFF THE COUSCOUS  ·  STAND OVER IT AND THEY CANNOT EAT"
                : "LAND ON THE COUSCOUS AND HOLD  E  TO EAT IT";
            // Fades out over its last two seconds rather than vanishing mid-read.
            hintText.alpha = Mathf.Clamp01(hintLeft * 0.5f);
        }
        else if (hintText.alpha > 0f)
        {
            hintText.alpha = 0f;
        }

        stateText.text = "";
        if (match.IsOver)
        {
            return;
        }

        FlyEating eater = LocalEater();
        if (eater == null || eater.Dish == null)
        {
            // The human's job is the whole kitchen, so he is told how his own dish is doing instead.
            if (GameSession.LocalRole == PlayerRole.Human && match.FoodLeft < 0.35f)
            {
                stateText.text = "THEY ARE EATING IT";
                stateText.color = Alarm;
            }
            return;
        }

        if (eater.Eating)
        {
            stateText.text = "EATING";
            stateText.color = BarFill;
        }
        else if (eater.BlockedReason != "")
        {
            stateText.text = eater.BlockedReason;
            stateText.color = Alarm;
        }
        else
        {
            stateText.text = "HOLD  E";
            stateText.color = InkDim;
        }
    }

    void ShowResult()
    {
        if (!match.IsOver)
        {
            if (resultRoot.activeSelf)
            {
                resultRoot.SetActive(false);
            }
            return;
        }
        if (resultRoot.activeSelf)
        {
            return;
        }

        bool fliesWon = match.CurrentPhase == MatchDirector.Phase.FliesWon;
        resultText.text = fliesWon ? "THE COUSCOUS IS GONE" : "THE COUSCOUS SURVIVED";
        resultText.color = match.LocalPlayerWon ? Win : Lose;
        resultNoteText.text = match.LocalPlayerWon
            ? fliesWon ? "THE FLIES ATE EVERY LAST GRAIN  ·  YOU WIN" : "YOU KEPT THEM OFF IT TO THE LAST SECOND  ·  YOU WIN"
            : fliesWon ? "THEY CLEARED THE PLATE  ·  YOU LOSE" : "THE CLOCK RAN OUT WITH FOOD STILL ON IT  ·  YOU LOSE";
        resultRoot.SetActive(true);
    }

    // The fly at this machine, if this player is flying one. Kept once found, and looked for again
    // only every so often while there is none: the body is spawned by the host a moment after the
    // scene loads, and sweeping the scene every frame to be told the same thing would be wasteful.
    FlyEating LocalEater()
    {
        if (localEater != null)
        {
            return localEater;
        }
        if (Time.time < eaterSearchAt)
        {
            return null;
        }
        eaterSearchAt = Time.time + EaterSearchInterval;

        foreach (FlyEating eater in FindObjectsByType<FlyEating>(FindObjectsInactive.Exclude))
        {
            // The controller is only enabled on the copy this machine's player is flying, which is the
            // same test FlyAudio and FlyEating use.
            FlyController fly = eater.GetComponent<FlyController>();
            if (fly != null && fly.enabled)
            {
                localEater = eater;
                return localEater;
            }
        }
        return null;
    }

    // ---- the canvas ----------------------------------------------------------------------------

    void Build()
    {
        var canvasObject = new GameObject("Match_Canvas");
        canvasObject.transform.SetParent(transform, false);
        Canvas canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = SortingOrder;
        CanvasScaler scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        // No GraphicRaycaster: nothing here is clickable, and one more raycaster would take clicks
        // away from the pause menu underneath it.
        Transform root = canvasObject.transform;

        clockText = NewLabel("Clock", root, "00:00", 64f, Ink);
        clockRect = clockText.rectTransform;
        TopCentre(clockRect, 0f, 150f, 74f);
        clockRestPosition = clockRect.anchoredPosition;

        Image track = NewImage("Food_Track", root, BarTrack);
        TopCentre(track.rectTransform, -92f, 420f, 18f);
        foodFill = NewImage("Food_Fill", track.transform, BarFill);
        foodFill.rectTransform.anchorMin = Vector2.zero;
        foodFill.rectTransform.anchorMax = Vector2.one;
        foodFill.rectTransform.offsetMin = Vector2.zero;
        foodFill.rectTransform.offsetMax = Vector2.zero;

        foodText = NewLabel("Food_Label", root, "COUSCOUS  100%", 24f, InkDim);
        TopCentre(foodText.rectTransform, -118f, 420f, 30f);

        stateText = NewLabel("State", root, "", 30f, InkDim);
        TopCentre(stateText.rectTransform, -158f, 620f, 36f);

        hintText = NewLabel("Hint", root, "", 26f, InkDim);
        RectTransform hint = hintText.rectTransform;
        hint.anchorMin = hint.anchorMax = new Vector2(0.5f, 0f);
        hint.pivot = new Vector2(0.5f, 0f);
        hint.anchoredPosition = new Vector2(0f, 120f);
        hint.sizeDelta = new Vector2(1100f, 36f);

        BuildResult(root);
    }

    void BuildResult(Transform root)
    {
        resultRoot = new GameObject("Result", typeof(RectTransform));
        resultRoot.transform.SetParent(root, false);
        RectTransform rect = (RectTransform)resultRoot.transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;

        Image scrim = NewImage("Scrim", resultRoot.transform, Scrim);
        scrim.rectTransform.anchorMin = Vector2.zero;
        scrim.rectTransform.anchorMax = Vector2.one;
        scrim.rectTransform.offsetMin = Vector2.zero;
        scrim.rectTransform.offsetMax = Vector2.zero;

        resultText = NewLabel("Headline", resultRoot.transform, "", 78f, Ink);
        Centre(resultText.rectTransform, 40f, 1400f, 96f);
        resultNoteText = NewLabel("Note", resultRoot.transform, "", 30f, InkDim);
        Centre(resultNoteText.rectTransform, -40f, 1400f, 40f);

        resultRoot.SetActive(false);
    }

    static void TopCentre(RectTransform rect, float fromTop, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 1f);
        rect.pivot = new Vector2(0.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, fromTop - 120f);
        rect.sizeDelta = new Vector2(width, height);
    }

    static void Centre(RectTransform rect, float fromMiddle, float width, float height)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = new Vector2(0f, fromMiddle);
        rect.sizeDelta = new Vector2(width, height);
    }

    static Image NewImage(string name, Transform parent, Color color)
    {
        var imageObject = new GameObject(name, typeof(RectTransform));
        imageObject.transform.SetParent(parent, false);
        Image image = imageObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    static TMP_Text NewLabel(string name, Transform parent, string content, float size, Color color)
    {
        var textObject = new GameObject(name, typeof(RectTransform));
        textObject.transform.SetParent(parent, false);
        TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
        text.text = content;
        text.fontSize = size;
        text.color = color;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        return text;
    }
}
