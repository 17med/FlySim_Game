using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Drives the Fly HUD. Put it on the HUD Canvas and drag the UI objects into the slots.
/// Call the Set... methods from your fly / game scripts.
/// </summary>
public class FlyHUD : MonoBehaviour
{
    [Header("Health")]
    public Image healthFill;            // bar_fill_health, Image Type = Filled, Horizontal
    public Sprite healthSprite;         // bar_fill_health
    public Sprite healthLowSprite;      // bar_fill_health_low
    [Range(0f, 1f)] public float lowHealthThreshold = 0.3f;
    public TMP_Text healthText;

    [Header("Spit (droplets)")]
    public Image[] spitDrops;           // 5 Images
    public Sprite dropFull;             // drop_full
    public Sprite dropEmpty;            // drop_empty
    public TMP_Text spitText;

    [Header("Wings (stamina)")]
    public Image wingsFill;             // bar_fill_wings, Filled, Horizontal
    public TMP_Text wingsText;

    [Header("Objective (fruit)")]
    public Image[] fruitIcons;          // one Image per fruit
    public Sprite[] fruitDone;          // fruit_*_done, same order
    public Sprite[] fruitTodo;          // fruit_*_todo, same order
    public TMP_Text objectiveCount;

    [Header("Score")]
    public TMP_Text scoreText;
    public TMP_Text comboText;

    [Header("Danger")]
    public GameObject dangerBanner;     // banner_danger + swatter icon + text
    public RectTransform threatArrow;   // threat_arrow, kept on the screen edge
    public TMP_Text threatDistance;
    public float edgePadding = 40f;

    [Header("Abilities")]
    public Image[] cooldownOverlays;    // hex_cooldown_overlay, Filled, Vertical, origin Bottom
    public TMP_Text[] cooldownTexts;

    Camera cam;

    void Awake() { cam = Camera.main; }

    public void SetHealth(float current, float max)
    {
        float t = max > 0 ? Mathf.Clamp01(current / max) : 0f;
        healthFill.fillAmount = t;
        healthFill.sprite = t <= lowHealthThreshold ? healthLowSprite : healthSprite;
        if (healthText) healthText.text = Mathf.CeilToInt(current).ToString();
    }

    public void SetSpit(int charges)
    {
        for (int i = 0; i < spitDrops.Length; i++)
            spitDrops[i].sprite = i < charges ? dropFull : dropEmpty;
        if (spitText) spitText.text = charges + "/" + spitDrops.Length;
    }

    public void SetWings(float current, float max)
    {
        float t = max > 0 ? Mathf.Clamp01(current / max) : 0f;
        wingsFill.fillAmount = t;
        if (wingsText) wingsText.text = Mathf.RoundToInt(t * 100) + "%";
    }

    public void SetFruitDone(int index, bool done)
    {
        if (index < 0 || index >= fruitIcons.Length) return;
        fruitIcons[index].sprite = done ? fruitDone[index] : fruitTodo[index];
        int count = 0;
        for (int i = 0; i < fruitIcons.Length; i++) if (fruitIcons[i].sprite == fruitDone[i]) count++;
        if (objectiveCount) objectiveCount.text = count + "/" + fruitIcons.Length;
    }

    public void SetScore(int score, int combo)
    {
        if (scoreText) scoreText.text = score.ToString("#,0").Replace(",", " ");
        if (comboText)
        {
            comboText.gameObject.SetActive(combo > 1);
            comboText.text = "×" + combo;
        }
    }

    /// <param name="cooldownLeft">seconds left (0 = ready)</param>
    public void SetCooldown(int slot, float cooldownLeft, float cooldownTotal)
    {
        if (slot < 0 || slot >= cooldownOverlays.Length) return;
        bool cooling = cooldownLeft > 0f;
        cooldownOverlays[slot].gameObject.SetActive(cooling);
        cooldownOverlays[slot].fillAmount = cooldownTotal > 0 ? cooldownLeft / cooldownTotal : 0f;
        if (cooldownTexts != null && slot < cooldownTexts.Length && cooldownTexts[slot])
            cooldownTexts[slot].text = cooling ? cooldownLeft.ToString("0.0") : "";
    }

    /// <summary>Pass null to hide the warning.</summary>
    public void SetThreat(Transform threat, Transform fly)
    {
        bool on = threat != null;
        dangerBanner.SetActive(on);
        threatArrow.gameObject.SetActive(on);
        if (!on || cam == null) return;

        Vector3 sp = cam.WorldToScreenPoint(threat.position);
        if (sp.z < 0) sp *= -1f;
        Vector2 center = new Vector2(Screen.width, Screen.height) * 0.5f;
        Vector2 dir = ((Vector2)sp - center).normalized;
        float hw = center.x - edgePadding, hh = center.y - edgePadding;
        float k = Mathf.Min(hw / Mathf.Abs(dir.x + 1e-5f), hh / Mathf.Abs(dir.y + 1e-5f));
        threatArrow.position = center + dir * k;
        threatArrow.rotation = Quaternion.Euler(0, 0, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg);
        if (threatDistance) threatDistance.text = Mathf.RoundToInt(Vector3.Distance(threat.position, fly.position)) + " m";
    }
}
