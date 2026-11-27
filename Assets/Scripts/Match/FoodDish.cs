using System;
using UnityEngine;

// The food the flies are after and the old man is defending: one dish that empties as it is eaten.
// Goes on anything with an IDishFill next to it - the couscous with its CouscousDishController, the
// living room's coffee mug with its CoffeeCup - and drives it. This component owns the rules of
// eating (who is close enough, who is in the way, how fast it goes down) and knows nothing about
// what the food is; the IDishFill owns what a half eaten dish looks like - which garnishes are gone,
// how flat the semolina sits, how low the coffee is - and smooths every change, so a bite here reads
// as the dish sinking rather than as a number changing. That is why there is a second dish in the
// kitchen and not a second copy of this file.
// A plain MonoBehaviour: FoodDishNetwork puts the server in charge of it in a room, and the single
// player Kitchen keeps working with no network at all.
public class FoodDish : MonoBehaviour
{
    // Below this the dish counts as cleared. CouscousDishController hides the last of the semolina at
    // half a percent and CoffeeCup hides the last of the coffee at the same point, so anything under
    // it is an empty plate on screen already.
    public const float EmptyPercent = 0.5f;
    // The old man walks around and is spawned after the dish in a room, so he is looked for again this
    // often rather than once at the start.
    const float GuardSearchInterval = 0.5f;

    [Tooltip("Percent of the dish one fly eats per second of holding Interact. The rate of one fly playing alone: 1.1 clears a full dish in about 90 seconds of eating without a break. With more flies each one eats less (MatchDirector.BiteShare), so this one number suits one to four flies. Give every dish in a scene the same rate: emptying any one dish wins, so a faster dish is the one the human can never leave.")]
    [SerializeField, Range(0.05f, 25f)] float biteRate = 0.6f;

    [Tooltip("How far from the middle of the dish a fly can eat, as a multiple of the dish's own radius. Taken from the model's bounds, so it follows whatever scale the dish is placed at.")]
    [SerializeField, Range(0.5f, 4f)] float biteRangeScale = 1.3f;

    [Tooltip("How close the old man has to stand to stop the flies eating, as a multiple of the dish's own radius, measured flat along the floor from the middle of his body. Within this the dish is guarded and a bite is refused.")]
    [SerializeField, Range(1f, 12f)] float guardRangeScale = 3.5f;

    [Header("Bite")]
    [Tooltip("How far the dish rocks on each bite, in degrees.")]
    [SerializeField, Range(0f, 10f)] float nibbleTilt = 1.6f;
    [Tooltip("Rocks per second while the dish is being eaten.")]
    [SerializeField, Range(0.5f, 12f)] float nibbleRate = 6f;
    [Tooltip("Seconds the rocking takes to die away once nobody is eating.")]
    [SerializeField, Range(0.05f, 2f)] float nibbleFade = 0.35f;

    // Raised whenever the amount left changes, with the new percent. MatchDirector listens for the
    // dish being cleared; the HUD redraws its bar from it.
    public event Action<float> Changed;
    // Raised once, the moment the last of the food goes.
    public event Action Emptied;

    // What is actually holding the food. Found in Awake rather than required by the type, so a mug
    // and a plate can both be a dish.
    IDishFill fill;
    Transform guard;
    // How tall the old man stands, in world units, read from his CharacterController when he is found.
    float guardHeight;
    float guardSearchAt;
    // Bounds radius of the dish in world units, worked out once from its renderers.
    float radius;
    Vector3 biteOffset;
    // 0 to 1: how much of the rocking is showing. Topped up by a bite, fades out on its own.
    float nibble;
    float nibblePhase;
    Quaternion restRotation;

    public float Percent => fill != null ? fill.FillPercent : 0f;

    // 0 to 1, which is what a bar wants.
    public float Fill => Mathf.Clamp01(Percent / 100f);

    // The fill being missing is checked as well as the amount: a dish that cannot be drained is not a
    // dish that has been cleared. Without that test a mis-wired dish would read as an empty plate on
    // its first frame and hand the flies the match before anybody had eaten anything.
    public bool IsEmpty => fill != null && Percent <= EmptyPercent;

    public float BiteRate => biteRate;

    // The middle of the food, a little above the plate: what a fly has to be near to eat.
    public Vector3 BitePoint => transform.TransformPoint(biteOffset);

    public float BiteRange => radius * biteRangeScale;

    public float GuardRange => radius * guardRangeScale;

    // True while the old man is standing over the dish. The flies cannot eat then, which is what makes
    // defending it something the human does by being there rather than only by swatting.
    public bool IsGuarded
    {
        get
        {
            Transform keeper = Guard();
            return keeper != null && IsWithinGuard(keeper.position);
        }
    }

    // Measured flat, not in 3D. His position is his feet and every dish sits on furniture, so in 3D the
    // height of the table alone was more than the guard range: standing right at the table guarded
    // nothing and the flies ate in front of him. Flat, standing at the dish is what guards it - as long
    // as the food is somewhere between his feet and the top of his head, so a dish on the floor above
    // or below is not guarded through the ceiling.
    bool IsWithinGuard(Vector3 feet)
    {
        Vector3 food = BitePoint;
        float height = food.y - feet.y;
        if (height < -GuardRange || height > guardHeight)
        {
            return false;
        }
        Vector2 flat = new Vector2(food.x - feet.x, food.z - feet.z);
        return flat.magnitude <= GuardRange;
    }

    public bool InBiteRange(Vector3 position)
    {
        return Vector3.Distance(position, BitePoint) <= BiteRange;
    }

    void Awake()
    {
        fill = GetComponent<IDishFill>();
        if (fill == null)
        {
            // Fails closed: with nothing to drain this is not food, so it switches itself off rather
            // than sitting in the kitchen as a plate the flies can hover over and never empty. The
            // error names the interface because the fix is to add whichever component implements it -
            // CouscousDishController on a dish, CoffeeCup on a mug.
            Debug.LogError($"{nameof(FoodDish)}: {name} has no {nameof(IDishFill)} next to it, so there is nothing for the flies to eat. " +
                           $"Add the component that holds the food ({nameof(CouscousDishController)} on a couscous, {nameof(CoffeeCup)} on a coffee mug), then run Tools > Match > Wire Match.", this);
            enabled = false;
            return;
        }
        restRotation = transform.localRotation;
        MeasureDish();
    }

    void Update()
    {
        // Unscaled would be wrong here: the dish is part of the world, and the single player pause
        // stops the world.
        nibble = Mathf.Max(0f, nibble - Time.deltaTime / nibbleFade);
        if (nibble <= 0f)
        {
            transform.localRotation = restRotation;
            return;
        }
        nibblePhase += Time.deltaTime * nibbleRate;
        // Two axes at different rates, so the dish wobbles rather than swinging like a pendulum.
        float tilt = nibbleTilt * nibble;
        transform.localRotation = restRotation * Quaternion.Euler(Mathf.Sin(nibblePhase * Mathf.PI * 2f) * tilt,
                                                                  0f,
                                                                  Mathf.Cos(nibblePhase * Mathf.PI * 3f) * tilt * 0.6f);
    }

    // Takes a bite and returns how much was actually eaten, which is less than asked for on the last
    // mouthful and nothing at all once the plate is clear.
    public float Bite(float percent)
    {
        if (fill == null || percent <= 0f)
        {
            return 0f;
        }
        float before = fill.FillPercent;
        float after = Mathf.Max(0f, before - percent);
        if (Mathf.Approximately(before, after))
        {
            return 0f;
        }
        Apply(after);
        Shake();
        return before - after;
    }

    // What the server says is left. Used by FoodDishNetwork on the other players' machines, where
    // nothing is eaten locally.
    public void SetPercent(float percent)
    {
        if (fill == null)
        {
            return;
        }
        bool wasBitten = percent < fill.FillPercent;
        Apply(Mathf.Clamp(percent, 0f, 100f));
        if (wasBitten)
        {
            Shake();
        }
    }

    public void Refill()
    {
        if (fill != null)
        {
            Apply(100f);
        }
    }

    // Starts the rocking, or tops it up while a fly keeps eating.
    public void Shake()
    {
        nibble = 1f;
    }

    void Apply(float percent)
    {
        bool wasEmpty = IsEmpty;
        fill.FillPercent = percent;
        Changed?.Invoke(percent);
        if (!wasEmpty && IsEmpty)
        {
            Emptied?.Invoke();
        }
    }

    // The dish's own size in world units, so the bite and guard ranges mean the same thing whatever
    // scale it was placed at (the kitchen is built at 45 units to the metre, and the coffee mug is a
    // great deal smaller than the couscous).
    void MeasureDish()
    {
        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            // Fails open with the dish's own scale as the unit: a dish nobody can reach would make the
            // match unwinnable for the flies, which is worse than a range that is roughly right.
            radius = transform.lossyScale.x * 0.5f;
            biteOffset = Vector3.zero;
            Debug.LogWarning($"{nameof(FoodDish)}: {name} has no renderers, so its size had to be guessed from its scale. Eating ranges may be off.", this);
            return;
        }

        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++)
        {
            bounds.Encapsulate(renderers[i].bounds);
        }
        radius = Mathf.Max(bounds.extents.x, bounds.extents.z);
        // The top of the food rather than the middle of the plate: a fly eats standing on it.
        biteOffset = transform.InverseTransformPoint(new Vector3(bounds.center.x, bounds.max.y, bounds.center.z));
    }

    // The old man, looked up again every so often: he is spawned after the scene in a room, and a
    // human who leaves is replaced by nobody.
    Transform Guard()
    {
        if (guard != null)
        {
            return guard;
        }
        if (Time.time < guardSearchAt)
        {
            return null;
        }
        guardSearchAt = Time.time + GuardSearchInterval;
        OldPersonFPSController keeper = FindAnyObjectByType<OldPersonFPSController>(FindObjectsInactive.Exclude);
        guard = keeper != null ? keeper.transform : null;
        if (guard != null)
        {
            CharacterController body = guard.GetComponent<CharacterController>();
            // Fails open to the guard range as his height: a guard that still works close to the floor
            // beats a dish that can never be guarded because his height could not be read.
            guardHeight = body != null ? body.height * Mathf.Abs(guard.lossyScale.y) : GuardRange;
        }
        return guard;
    }

    void OnDrawGizmosSelected()
    {
        // Drawn from the serialized fields rather than the measured radius, so the ranges can be seen
        // and adjusted without entering Play mode.
        if (!Application.isPlaying)
        {
            MeasureDish();
        }
        Gizmos.color = new Color(0.4f, 1f, 0.4f, 0.8f);
        Gizmos.DrawWireSphere(BitePoint, BiteRange);
        // The guard is drawn as a flat circle around the food, because it is measured flat: seen from
        // above, the old man's feet have to be inside it.
        Gizmos.color = new Color(1f, 0.4f, 0.3f, 0.5f);
        Gizmos.matrix = Matrix4x4.TRS(BitePoint, Quaternion.identity, new Vector3(1f, 0.001f, 1f));
        Gizmos.DrawWireSphere(Vector3.zero, GuardRange);
        Gizmos.matrix = Matrix4x4.identity;
    }
}
