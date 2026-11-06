using UnityEngine;

/// <summary>
/// Controls the Tunisian Couscous Dish (Tebsi Kosksi) depletion from 100% full down to 0% empty plate.
/// Attach this script to the root of 'Tunisian_Couscous_All_14_Parts_Separated' or 'Tunisian_Couscous_Animated_100_to_0'.
/// </summary>
[ExecuteAlways]
public class CouscousDishController : MonoBehaviour
{
    [Header("Dish Fill Percentage (100% = Full, 0% = Empty Plate)")]
    [Range(0f, 100f)]
    public float dishPercentage = 100f;

    [Header("Smooth Transition Speed")]
    public bool smoothAnimate = true;
    public float smoothSpeed = 8f;

    private float _currentDisplayedPct = 100f;

    // Each child part name and the percentage threshold at which it is eaten
    private readonly (string prefix, float eatAtPct)[] _garnishRules = new[]
    {
        ("13_Veg_Left", 90f),
        ("12_Veg_Carrot_TopRight", 80f),
        ("11_Veg_Zucchini_FarRight", 80f),
        ("10_Veg_Carrot_Right", 70f),
        ("09_Veg_Zucchini_FrontRight", 70f),
        ("08_Veg_Pumpkin_Red_Front", 60f),
        ("04_Meat_Braised_Front", 60f),
        ("07_Veg_Carrot_FrontLeft", 50f),
        ("06_Veg_Green_Pepper_Felfel", 40f),
        ("03_Meat_Braised_Left", 40f),
        ("02_Meat_Lamb_Shank_Main", 30f),
        ("05_Chickpeas_Cluster", 30f)
    };

    void Update()
    {
        if (Application.isPlaying && smoothAnimate)
        {
            _currentDisplayedPct = Mathf.MoveTowards(_currentDisplayedPct, dishPercentage, smoothSpeed * 10f * Time.deltaTime);
        }
        else
        {
            _currentDisplayedPct = dishPercentage;
        }
        ApplyDishState(_currentDisplayedPct);
    }

    /// <summary>
    /// Call this from your game code when a player eats a portion (e.g. EatPortion(10f)).
    /// </summary>
    public void EatPortion(float amountPercent = 10f)
    {
        dishPercentage = Mathf.Clamp(dishPercentage - amountPercent, 0f, 100f);
    }

    /// <summary>
    /// Refills the dish back to 100%.
    /// </summary>
    public void RefillDish()
    {
        dishPercentage = 100f;
    }

    private void ApplyDishState(float pct)
    {
        foreach (Transform child in transform)
        {
            string cname = child.name;
            if (cname.StartsWith("00_Plate"))
            {
                child.gameObject.SetActive(true);
                child.localScale = Vector3.one;
                continue;
            }

            if (cname.StartsWith("01_Couscous"))
            {
                if (pct <= 0.5f)
                {
                    child.gameObject.SetActive(false);
                }
                else
                {
                    child.gameObject.SetActive(true);
                    float t = Mathf.Clamp01(pct / 100f);
                    float sx = 0.36f + 0.64f * Mathf.Pow(t, 0.45f);
                    float sy = 0.18f + 0.82f * Mathf.Pow(t, 0.70f);
                    child.localScale = new Vector3(sx, sy, sx);
                }
                continue;
            }

            foreach (var rule in _garnishRules)
            {
                if (cname.StartsWith(rule.prefix))
                {
                    bool visible = pct > rule.eatAtPct;
                    child.gameObject.SetActive(visible);
                    if (visible)
                    {
                        float pop = Mathf.Clamp01((pct - rule.eatAtPct) / 8f);
                        child.localScale = Vector3.one * pop;
                    }
                    break;
                }
            }
        }
    }
}
