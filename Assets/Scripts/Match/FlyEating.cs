using UnityEngine;
using UnityEngine.InputSystem;

// What the fly came for: hold Interact over the couscous to eat it. Clearing the dish before the
// clock runs out is how the flies win, so this is the fly's job and the spit is only a way to buy
// time for it.
// A bite needs the fly on or just over the dish, not stunned, and the old man not standing over it -
// which is what makes the human's job standing in the way rather than only swinging.
// Goes next to FlyController. The nibble is procedural: the fly dips its head into the food and
// chews, layered on top of whatever FlyBodyMotion and FlySpit left the model doing, which is why this
// runs after both of them.
// Runs on every copy of the fly. The owner's reads the key; on another player's copy, where
// FlyController is off, FlyEatNetwork sets Eating instead so the nibble and the chewing are seen too.
[RequireComponent(typeof(FlyController))]
[DefaultExecutionOrder(110)]
public class FlyEating : MonoBehaviour
{
    // Project-wide action (InputSystem_Actions): E, gamepad west button.
    const string EatActionName = "Player/Interact";
    // How often the fly looks around for a dish while it is not eating. Every frame would be wasteful
    // and the dishes do not move.
    const float DishSearchInterval = 0.25f;
    // How often a fly's mouthfuls are sent to the server, in seconds. One message per frame would be
    // sixty a second per fly for a number that only has to add up over time, so they are added up here
    // and sent in one. FoodDishNetwork.MaxBiteWindow is what allows a batch this size through.
    public const float BiteSendInterval = 0.1f;

    [Tooltip("The fly model (the child with the Animator and the 180° X rotation). Found automatically if left empty.")]
    [SerializeField] Transform model;

    [Tooltip("How far the fly dips toward the food while eating, in units. The fly is 0.4 units across.")]
    [SerializeField, Range(0f, 0.3f)] float dipDistance = 0.06f;
    [Tooltip("How far the fly tips its head down into the dish, in degrees.")]
    [SerializeField, Range(0f, 60f)] float dipAngle = 28f;
    [Tooltip("Chews per second.")]
    [SerializeField, Range(0.5f, 12f)] float chewRate = 5f;
    [Tooltip("How far each chew moves the fly, as a share of the dip. 0 = a still fly with its head down.")]
    [SerializeField, Range(0f, 1f)] float chewDepth = 0.35f;
    [Tooltip("Seconds the dip takes to fade in when eating starts and out when it stops.")]
    [SerializeField, Range(0.01f, 1f)] float dipFade = 0.15f;

    [Header("Sound")]
    [Tooltip("One is picked at random each time the last finishes, so the chewing does not loop audibly.")]
    [SerializeField] AudioClip[] eatSounds = new AudioClip[0];
    [Tooltip("The level the chewing is mixed at, before the player's sound effects setting.")]
    [SerializeField, Range(0f, 1f)] float eatVolume = 1f;
    [Tooltip("Plays the chewing. A child object with one of its own is made if this is left empty.")]
    [SerializeField] AudioSource eatSource;

    FlyController fly;
    FoodDish dish;
    FoodDishNetwork dishNetwork;
    InputAction eatAction;
    float dishSearchAt;
    // Mouthfuls taken since the last message to the server, and when the next one is due.
    float pendingBite;
    float biteSendAt;
    // 0 to 1: how far into the dip the fly is, so starting and stopping eases instead of snapping.
    float dip;
    float chewPhase;
    // The pose the nibble was added to, and the pose it left on the model. See ApplyNibble.
    bool hasNibblePose;
    Vector3 basePosition;
    Quaternion baseRotation;
    Vector3 nibblePosition;
    Quaternion nibbleRotation;

    // True while this fly is eating. On the owner's copy it is worked out from the key and the dish;
    // on everyone else's, FlyEatNetwork sets it from the owner.
    public bool Eating { get; private set; }

    // The dish this fly is close enough to eat, or null. The HUD reads it to say what the key does.
    public FoodDish Dish => dish;

    // Why the fly cannot eat the dish it is sitting on, for the HUD. Empty while it can.
    public string BlockedReason { get; private set; } = "";

    // Set by FlyEatNetwork on the other players' machines, where no key is read.
    public void SetRemoteEating(bool eating)
    {
        Eating = eating;
    }

    void Awake()
    {
        fly = GetComponent<FlyController>();
        if (model == null)
        {
            Animator animator = GetComponentInChildren<Animator>();
            if (animator != null)
            {
                model = animator.transform;
            }
        }
        // Throws if the action is missing: a key that silently does nothing is worse than a clear error.
        eatAction = InputSystem.actions != null ? InputSystem.actions.FindAction(EatActionName, throwIfNotFound: true) : null;
        if (eatSource == null)
        {
            // On a child of its own, not on this object: FlyAudio already has an AudioSource here and
            // takes it with GetComponent, which would hand it whichever of the two came first.
            var sourceObject = new GameObject("SFX_Eat");
            sourceObject.transform.SetParent(transform, false);
            eatSource = sourceObject.AddComponent<AudioSource>();
        }
        eatSource.playOnAwake = false;
        eatSource.loop = false;
    }

    void OnEnable()
    {
        eatAction?.Enable();
    }

    void OnDisable()
    {
        StopEating();
    }

    void Update()
    {
        // Only the owner's copy decides; FlyController is off on everyone else's, which is the same
        // test FlyAudio uses to tell the two apart.
        if (fly.enabled)
        {
            Eating = WantsToEat();
        }
        if (Eating)
        {
            Swallow();
        }
        Chew();
    }

    // The nibble goes on in LateUpdate, after FlyBodyMotion (and FlySpit's lunge) have posed the model
    // for this frame, and is added to that pose rather than replacing it.
    void LateUpdate()
    {
        if (model == null)
        {
            return;
        }
        dip = Mathf.MoveTowards(dip, Eating ? 1f : 0f, Time.deltaTime / dipFade);
        if (dip <= 0f)
        {
            RestoreBasePose();
            return;
        }

        // 0 at the top of a chew, 1 at the bottom.
        float chew = 0.5f - 0.5f * Mathf.Cos(chewPhase * Mathf.PI * 2f);
        ApplyNibble(dip * (1f - chewDepth + chewDepth * chew));
    }

    // Adds the dip to the model's pose for this frame. On the owner's copy FlySpit re-poses the model
    // every frame before this runs, so the current pose is the base. On another player's copy nothing
    // does (FlySpit is owner-only and the network fly has no FlyBodyMotion), so the model still holds
    // what this left last frame; adding to that again every frame is what spun the other players' flies
    // and left them twisted for the rest of the game. So when the pose is untouched, the last base is
    // reused instead of the nibble being stacked on itself.
    void ApplyNibble(float depth)
    {
        bool isUntouched = hasNibblePose && model.localPosition == nibblePosition && model.localRotation == nibbleRotation;
        if (!isUntouched)
        {
            basePosition = model.localPosition;
            baseRotation = model.localRotation;
        }

        model.localPosition = basePosition + baseRotation * new Vector3(0f, -dipDistance * depth, dipDistance * depth * 0.5f);
        // The model child carries its own 180° X rotation, so the tip is applied in its local space.
        model.localRotation = baseRotation * Quaternion.Euler(dipAngle * depth, 0f, 0f);
        nibblePosition = model.localPosition;
        nibbleRotation = model.localRotation;
        hasNibblePose = true;
    }

    // Takes the dip back off once eating has stopped. Only needed where nothing else re-poses the
    // model; where something does, it already has, and its pose is left alone.
    void RestoreBasePose()
    {
        if (!hasNibblePose || model == null)
        {
            return;
        }
        hasNibblePose = false;
        if (model.localPosition == nibblePosition && model.localRotation == nibbleRotation)
        {
            model.localPosition = basePosition;
            model.localRotation = baseRotation;
        }
    }

    // The key is held, the fly is over a dish with food on it, nothing has knocked it off, and the old
    // man is not standing over the plate.
    bool WantsToEat()
    {
        BlockedReason = "";
        FindDish();
        if (dish == null)
        {
            return false;
        }
        if (fly.IsStunned)
        {
            BlockedReason = "SHAKEN OFF";
            return false;
        }
        if (dish.IsEmpty)
        {
            BlockedReason = "NOTHING LEFT";
            return false;
        }
        if (dish.IsGuarded)
        {
            BlockedReason = "THE OLD MAN IS OVER IT";
            return false;
        }
        return eatAction != null && eatAction.IsPressed();
    }

    // Takes this frame's mouthful. In a room the server takes it, so the dish never goes down twice
    // for one bite; on its own the fly takes it straight off the dish.
    void Swallow()
    {
        if (dish == null)
        {
            return;
        }
        float amount = dish.BiteRate * Time.deltaTime;
        if (dishNetwork == null)
        {
            dish.Bite(amount);
            return;
        }

        // The server's answer is a few frames away; rocking the dish now is what makes the bite feel
        // like it landed when the key went down.
        dish.Shake();
        pendingBite += amount;
        if (Time.time < biteSendAt)
        {
            return;
        }
        biteSendAt = Time.time + BiteSendInterval;
        dishNetwork.RequestBite(pendingBite);
        pendingBite = 0f;
    }

    void Chew()
    {
        if (!Eating)
        {
            if (eatSource.isPlaying)
            {
                eatSource.Stop();
            }
            return;
        }

        chewPhase += Time.deltaTime * chewRate;
        eatSource.volume = GameAudio.Scale(eatVolume);
        // Flat on the owner's copy: the orbit camera sits several units back, where a positional sound
        // would be faint. Other players hear the chewing from where the fly is.
        eatSource.spatialBlend = fly.enabled ? 0f : 1f;
        if (eatSounds.Length > 0 && !eatSource.isPlaying)
        {
            eatSource.clip = eatSounds[Random.Range(0, eatSounds.Length)];
            eatSource.Play();
        }
    }

    void StopEating()
    {
        Eating = false;
        dip = 0f;
        // Disabled mid-bite, LateUpdate will not run again to lift the head back out.
        RestoreBasePose();
        // A mouthful that was never sent is dropped rather than held: it would otherwise arrive as one
        // big bite the next time this fly lands on a dish, possibly a different one.
        pendingBite = 0f;
        if (eatSource != null && eatSource.isPlaying)
        {
            eatSource.Stop();
        }
    }

    // The nearest dish within eating distance, looked up a few times a second. Null once the fly flies
    // away from it, which is what turns the HUD's prompt off.
    void FindDish()
    {
        if (dish != null && dish.InBiteRange(transform.position))
        {
            return;
        }
        if (Time.time < dishSearchAt)
        {
            dish = null;
            return;
        }
        dishSearchAt = Time.time + DishSearchInterval;

        dish = null;
        dishNetwork = null;
        float nearest = float.MaxValue;
        foreach (FoodDish candidate in MatchDirector.Dishes)
        {
            if (candidate == null || !candidate.InBiteRange(transform.position))
            {
                continue;
            }
            float distance = Vector3.Distance(transform.position, candidate.BitePoint);
            if (distance >= nearest)
            {
                continue;
            }
            nearest = distance;
            dish = candidate;
        }
        if (dish != null)
        {
            dishNetwork = dish.GetComponent<FoodDishNetwork>();
        }
    }
}
