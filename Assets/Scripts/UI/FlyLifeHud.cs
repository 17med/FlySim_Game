using TMPro;
using UnityEngine;
using UnityEngine.UI;

// Shows the fly player its own life: the health bar empties one step per hit, and while the fly is
// dead the death panel is up with the seconds left until it comes back.
// Everything it shows is read from FlyHealth every frame rather than pushed by events, because online
// the hit count and the death arrive through FlyHealthNetwork's variables on their own schedule, and
// polling one component is the one path that is right offline, on the host and on a client alike.
// Goes on the fly's HUD canvas (Fly_HUD in fly.prefab), with the bar, the panel and its text dragged in.
public class FlyLifeHud : MonoBehaviour
{
    [Tooltip("The fly whose life is shown. Found under this HUD's root (the fly) if left empty.")]
    [SerializeField] FlyHealth health;
    [Tooltip("The health bar. Full while the fly is unhurt, one step lower per hit, empty when dead.")]
    [SerializeField] Slider healthBar;
    [Tooltip("Shown while the fly is dead, hidden otherwise. It is hidden on Play, so it can be left visible while editing.")]
    [SerializeField] GameObject deathPanel;
    [Tooltip("Optional text on the death panel. Whatever it says in the prefab (\"u died\") is kept, and the respawn countdown is put on the line under it.")]
    [SerializeField] TMP_Text deathText;
    [Tooltip("The line under the death text. {0} is the whole seconds left before the fly is back.")]
    [SerializeField] string countdownFormat = "respawn in {0}";

    // The HUD's own canvas. On another player's copy of the fly OwnerOnlyControls switches it off, and
    // the death panel carries a nested canvas of its own, so the panel is only ever shown while this one
    // is on - otherwise a dead mirror could put its death screen over somebody else's game.
    Canvas hudCanvas;
    string deathHeadline;
    int shownSeconds = -1;

    void Awake()
    {
        if (health == null)
        {
            health = transform.root.GetComponentInChildren<FlyHealth>(true);
        }
        hudCanvas = GetComponent<Canvas>();
        deathHeadline = deathText != null ? deathText.text : string.Empty;
        // Fails open: with no FlyHealth there is nothing to show, so the HUD stops updating and the fly
        // plays on. The bar and panel are left as the prefab has them.
        if (health == null)
        {
            Debug.LogWarning($"{nameof(FlyLifeHud)}: no {nameof(FlyHealth)} under {transform.root.name}, so the health bar and death screen will not update. Assign Health.", this);
            enabled = false;
            return;
        }
        SetDeathPanel(false);
    }

    void Update()
    {
        ShowHealth();
        bool showDeath = health.IsDead && (hudCanvas == null || hudCanvas.enabled);
        SetDeathPanel(showDeath);
        if (showDeath)
        {
            ShowCountdown(health.RespawnSecondsLeft);
        }
    }

    void ShowHealth()
    {
        if (healthBar == null)
        {
            return;
        }
        int hitsLeft = Mathf.Max(0, health.HitsToKill - health.HitsTaken);
        float share = health.IsDead ? 0f : (float)hitsLeft / health.HitsToKill;
        // Without notify: the bar is a display, nothing listens to it, and a listener added later must
        // not fire sixty times a second.
        healthBar.SetValueWithoutNotify(Mathf.Lerp(healthBar.minValue, healthBar.maxValue, share));
    }

    void ShowCountdown(float secondsLeft)
    {
        if (deathText == null)
        {
            return;
        }
        // Rounded up so the last second reads "1" rather than "0" while the fly is still dead.
        int seconds = Mathf.CeilToInt(secondsLeft);
        // Only when the number changes: a TMP rebuild every frame is wasted work for a text that ticks once a second.
        if (seconds == shownSeconds)
        {
            return;
        }
        shownSeconds = seconds;
        string countdown = string.Format(countdownFormat, seconds);
        deathText.text = string.IsNullOrEmpty(deathHeadline) ? countdown : deathHeadline + "\n" + countdown;
    }

    void SetDeathPanel(bool visible)
    {
        if (deathPanel == null || deathPanel.activeSelf == visible)
        {
            return;
        }
        deathPanel.SetActive(visible);
        if (!visible)
        {
            shownSeconds = -1;
        }
    }
}
