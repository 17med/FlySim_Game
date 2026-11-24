using UnityEngine;

// The second thing on the menu: a mug of coffee in the living room that the flies drink dry.
// Goes on the mug's root, next to FoodDish, with the CoffeeLiquid child assigned below
// (Tools > Match > Place Coffee In Living Room builds and wires one).
// All this does is show how much is left: the liquid mesh has its pivot at the bottom of the mug
// (see Assets/cup_coffie/README.md), so scaling it down on Y drops the surface the way a mug being
// drunk does, and the last millimetre is hidden rather than left as a film of coffee on the glass.
// FoodDish drives it through IDishFill and owns everything else - who may drink, how fast, and when
// the mug counts as cleared - so the mug and the couscous are the same kind of thing to the match.
// The change is smoothed the way CouscousDishController smooths the semolina sinking: a sip arrives
// as one small number from the server, and a level that jumped to it would read as a flicker rather
// than as the coffee going down.
// Not [ExecuteAlways], unlike CouscousDishController: this one writes the liquid's scale, and
// driving that in the Editor would save a half drunk mug into the scene and leave the next run
// remembering a drained mug as a full one.
public class CoffeeCup : MonoBehaviour, IDishFill
{
    // The liquid is never scaled flat to nothing: a zero scale makes Unity complain about a
    // degenerate transform, and the renderers are off by then anyway.
    const float MinLiquidHeight = 0.001f;

    [Tooltip("The CoffeeLiquid child, whose pivot sits at the bottom of the mug. Scaled down on Y as the coffee goes. Without it the mug still drains as a number, it just does not show.")]
    [SerializeField] Transform coffeeLiquid;

    [Tooltip("How much coffee is in the mug, 100 = full. Scrub it to see the level; the match drives it through FoodDish.")]
    [SerializeField, Range(0f, 100f)] float coffeePercentage = 100f;

    [Header("Smooth Transition")]
    [Tooltip("Slides the level toward the amount left instead of snapping to it, so one fly's sip reads as the coffee dropping.")]
    [SerializeField] bool smoothAnimate = true;
    [Tooltip("How fast the shown level catches up, in percent of the mug per second divided by ten (8 = 80%/s). The same units as CouscousDishController's, so the mug and the dish sink at the same speed.")]
    [SerializeField, Range(0.5f, 40f)] float smoothSpeed = 8f;

    // The scale the mug was authored with, which is what a full mug looks like.
    Vector3 fullScale = Vector3.one;
    Renderer[] liquidRenderers = new Renderer[0];
    // What is on screen right now, which trails coffeePercentage while a sip is being shown.
    float shownPercent = 100f;

    // IDishFill: what FoodDish reads and writes. Clamped here rather than trusted, because the
    // server's number arrives over the network and a bad one would scale the liquid inside out.
    public float FillPercent
    {
        get => coffeePercentage;
        set => coffeePercentage = Mathf.Clamp(value, 0f, 100f);
    }

    void Awake()
    {
        if (coffeeLiquid == null)
        {
            // Fails open: the mug keeps working as a dish and the flies can still drain it, they just
            // cannot see how far they have got. Cosmetic, so a warning rather than a stop.
            Debug.LogWarning($"{nameof(CoffeeCup)}: {name} has no coffee liquid assigned, so the level will not go down on screen. " +
                             "Assign the CoffeeLiquid child in the Inspector, or run Tools > Match > Place Coffee In Living Room.", this);
        }
        else
        {
            fullScale = coffeeLiquid.localScale;
            liquidRenderers = coffeeLiquid.GetComponentsInChildren<Renderer>(true);
        }
        // Whatever the scene authored is where the level starts: no slide up from empty on the first
        // frame of the match.
        shownPercent = coffeePercentage;
        ShowLevel(shownPercent);
    }

    void Update()
    {
        // Scaled time, like the dish: the mug is part of the world, and the single player pause stops
        // the world.
        shownPercent = smoothAnimate
            ? Mathf.MoveTowards(shownPercent, coffeePercentage, smoothSpeed * 10f * Time.deltaTime)
            : coffeePercentage;
        ShowLevel(shownPercent);
    }

    void ShowLevel(float percent)
    {
        if (coffeeLiquid == null)
        {
            return;
        }
        float level = Mathf.Clamp01(percent / 100f);
        coffeeLiquid.localScale = new Vector3(fullScale.x,
                                              Mathf.Max(MinLiquidHeight, fullScale.y * level),
                                              fullScale.z);

        // The same threshold FoodDish calls an empty plate, so the coffee disappears on exactly the
        // frame the mug stops being worth landing on.
        bool visible = percent > FoodDish.EmptyPercent;
        foreach (Renderer liquid in liquidRenderers)
        {
            if (liquid != null && liquid.enabled != visible)
            {
                liquid.enabled = visible;
            }
        }
    }
}
