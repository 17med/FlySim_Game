using TMPro;
using UnityEngine;
using UnityEngine.UI;

// What the players need to see while the kitchen is being raided: how long is left, how much food is
// left, and what the key under their finger does.
// The bar is the emptiest dish in the kitchen, not the couscous alone or an average: emptying any one
// dish wins the match for the flies, so the emptiest is how close they are. It can be either dish,
// so the lines below say "the food" rather than naming one of them.
// The HUD lives in each kitchen scene (Tools > Match > Add HUD To Open Scene makes it once), so its
// texts and bar are placed, sized and coloured by hand in the Editor. This script only fills them in:
// it never moves anything, so whatever layout the scene has is the layout the players see.
// The colours each object has in the scene are its normal colours; the fields below are only the
// colours it changes to (the final minute, a low bar, a win or a loss).
// Everything it shows comes from MatchDirector; it holds no state of its own beyond its animations.
public class MatchHud : MonoBehaviour
{
    // How long the "hold E to eat" line stays up at the start of a match, in seconds.
    const float HintSeconds = 14f;
    // Pulses per second of the clock in the final minute.
    const float AlarmRate = 2f;
    // How much the clock grows on each pulse of the final minute. 0.06 = 6% bigger.
    const float AlarmPulseScale = 0.06f;
    // How often the scene is swept for this machine's own fly while it has not been found yet.
    const float EaterSearchInterval = 0.5f;

    [Header("Timer")]
    [Tooltip("The match clock, \"09:58\".")]
    [SerializeField] TMP_Text clockText;

    [Header("Food Bar")]
    [Tooltip("The coloured part of the bar. Must be a child of the bar's background: it is stretched from the left edge of its parent to show how much food is left.")]
    [SerializeField] Image foodFill;
    [Tooltip("The \"FOOD 100%\" label.")]
    [SerializeField] TMP_Text foodText;

    [Header("Lines Under The Bar")]
    [Tooltip("What this player is doing right now: EATING, HOLD E, or why they cannot eat.")]
    [SerializeField] TMP_Text stateText;
    [Tooltip("The how-to-play line shown for the first seconds of the match.")]
    [SerializeField] TMP_Text hintText;

    [Header("Result Screen")]
    [Tooltip("Shown when the match ends, hidden otherwise. It is hidden on Play, so it can be left visible while editing.")]
    [SerializeField] GameObject resultRoot;
    [SerializeField] TMP_Text resultText;
    [SerializeField] TMP_Text resultNoteText;

    [Header("Colours It Changes To")]
    [SerializeField] Color alarmColor = new Color(1f, 0.33f, 0.28f, 1f);
    [Tooltip("The bar's colour when almost empty. It blends to this from its scene colour over the last third.")]
    [SerializeField] Color foodLowColor = new Color(0.95f, 0.38f, 0.25f, 1f);
    [SerializeField] Color eatingColor = new Color(0.98f, 0.76f, 0.3f, 1f);
    [SerializeField] Color winColor = new Color(0.55f, 0.95f, 0.55f, 1f);
    [SerializeField] Color loseColor = new Color(1f, 0.5f, 0.45f, 1f);

    // The colours the scene gave these objects, which is what they go back to.
    Color clockColor;
    Color foodColor;
    Color stateColor;
    Vector3 clockScale;

    FlyEating localEater;
    float eaterSearchAt;
    float hintLeft = HintSeconds;
    float alarmPhase;

    void Awake()
    {
        // Fails open: a HUD with a missing piece is switched off and the match plays on without it,
        // rather than throwing every frame. Nothing is lost but the display, and the error names the
        // field to fill in.
        if (!HasEveryPiece())
        {
            enabled = false;
            return;
        }
        clockColor = clockText.color;
        foodColor = foodFill.color;
        stateColor = stateText.color;
        clockScale = clockText.rectTransform.localScale;
        resultRoot.SetActive(false);
    }

    void Update()
    {
        MatchDirector match = MatchDirector.Instance;
        if (match == null)
        {
            return;
        }
        ShowClock(match);
        ShowFood(match);
        ShowState(match);
        ShowResult(match);
    }

    bool HasEveryPiece()
    {
        bool hasAll = clockText != null && foodFill != null && foodText != null && stateText != null
                      && hintText != null && resultRoot != null && resultText != null && resultNoteText != null;
        if (!hasAll)
        {
            Debug.LogError($"{nameof(MatchHud)}: one of the fields on {name} is empty, so the HUD is off. " +
                           "Fill it in the Inspector, or delete this object and run Tools > Match > Add HUD To Open Scene.", this);
        }
        return hasAll;
    }

    // ---- what it says --------------------------------------------------------------------------

    void ShowClock(MatchDirector match)
    {
        clockText.text = MatchDirector.Clock(match.SecondsLeft);
        if (!match.IsFinalMinute)
        {
            clockText.color = clockColor;
            clockText.rectTransform.localScale = clockScale;
            return;
        }

        // The last minute: the clock beats, so it is felt rather than read.
        alarmPhase += Time.deltaTime * AlarmRate;
        float beat = 0.5f + 0.5f * Mathf.Sin(alarmPhase * Mathf.PI * 2f);
        clockText.color = Color.Lerp(clockColor, alarmColor, beat);
        clockText.rectTransform.localScale = clockScale * (1f + AlarmPulseScale * beat);
    }

    void ShowFood(MatchDirector match)
    {
        float left = Mathf.Clamp01(match.FoodLeft);
        foodFill.rectTransform.anchorMax = new Vector2(left, foodFill.rectTransform.anchorMax.y);
        foodFill.color = Color.Lerp(foodLowColor, foodColor, Mathf.Clamp01(left * 3f));
        foodText.text = $"FOOD  {Mathf.CeilToInt(left * 100f)}%";
    }

    // The line under the bar: what this player should be doing, and why they cannot.
    void ShowState(MatchDirector match)
    {
        ShowHint();

        stateText.text = "";
        if (match.IsOver)
        {
            return;
        }

        FlyEating eater = LocalEater();
        if (eater == null || eater.Dish == null)
        {
            // The human's job is the whole kitchen, so he is told how his own dish is doing instead.
            if (GameSession.LocalRole == PlayerRole.Human && match.FoodLeft < MatchDirector.FoodDangerShare)
            {
                stateText.text = "THEY ARE EATING IT";
                stateText.color = alarmColor;
            }
            return;
        }

        if (eater.Eating)
        {
            stateText.text = "EATING";
            stateText.color = eatingColor;
        }
        else if (eater.BlockedReason != "")
        {
            stateText.text = eater.BlockedReason;
            stateText.color = alarmColor;
        }
        else
        {
            stateText.text = "HOLD  E";
            stateText.color = stateColor;
        }
    }

    void ShowHint()
    {
        if (hintLeft > 0f)
        {
            hintLeft -= Time.deltaTime;
            hintText.text = GameSession.LocalRole == PlayerRole.Human
                ? "KEEP THE FLIES OFF THE FOOD  ·  STAND OVER IT AND THEY CANNOT EAT"
                : "LAND ON THE FOOD AND HOLD  E  TO EAT IT";
            // Fades out over its last two seconds rather than vanishing mid-read.
            hintText.alpha = Mathf.Clamp01(hintLeft * 0.5f);
        }
        else if (hintText.alpha > 0f)
        {
            hintText.alpha = 0f;
        }
    }

    void ShowResult(MatchDirector match)
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
        resultText.text = fliesWon ? "THE FOOD IS GONE" : "THE FOOD SURVIVED";
        resultText.color = match.LocalPlayerWon ? winColor : loseColor;
        resultNoteText.text = match.LocalPlayerWon
            ? fliesWon ? "THE FLIES LEFT NOTHING BUT EMPTY DISHES  ·  YOU WIN" : "YOU KEPT THEM OFF IT TO THE LAST SECOND  ·  YOU WIN"
            : fliesWon ? "THEY CLEARED EVERY LAST DISH  ·  YOU LOSE" : "THE CLOCK RAN OUT WITH FOOD STILL ON IT  ·  YOU LOSE";
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
}
