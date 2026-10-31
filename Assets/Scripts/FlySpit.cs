using UnityEngine;
using UnityEngine.InputSystem;

// The fly's weapon: a gob of spit, fired with the Attack action (left click). Put this next to
// FlyController on the fly's parent object, the same one FlyBodyMotion goes on.
// The shot is a three-phase animation rather than an instant spawn, because an instant one reads as
// the blob appearing out of nowhere:
//   windup  - the fly rears back and lifts its nose, gathering the spit
//   thrust  - it lunges forward and snaps its nose down; the blob leaves at the start of this phase
//   recover - it eases back to wherever its normal motion has it
// There is no spit clip in Assets/Animator, so the lunge is procedural, on the model child, exactly
// like FlyBodyMotion's lean and bob. If a trigger parameter named "spit" is ever added to
// fly.controller it is fired too, so a hand-made clip can take over the head and mouth without
// touching this script.
// DefaultExecutionOrder puts this LateUpdate after FlyBodyMotion's (order 0), which rewrites the
// model's pose from scratch every frame; the lunge is then layered on top of the pose it just wrote
// instead of being overwritten by it.
[RequireComponent(typeof(FlyController))]
[DefaultExecutionOrder(100)]
public class FlySpit : MonoBehaviour
{
    // Project-wide action (InputSystem_Actions): left mouse button, gamepad right trigger.
    const string AttackActionName = "Player/Attack";
    // Trigger on the fly's Animator, used only if fly.controller actually declares it.
    const string SpitAnimParameter = "spit";

    [Tooltip("The fly model (the child with the Animator and the 180° X rotation). Found automatically if left empty.")]
    [SerializeField] Transform model;
    [Tooltip("Where the spit leaves from, at the fly's mouth. Muzzle Offset is used instead if this is empty.")]
    [SerializeField] Transform muzzle;
    [Tooltip("Mouth position relative to this object, used when Muzzle is empty. +Z is the fly's forward.")]
    [SerializeField] Vector3 muzzleOffset = new Vector3(0f, 0.02f, 0.18f);
    [Tooltip("Spat on every shot. Needs a SpitProjectile on its root.")]
    [SerializeField] SpitProjectile projectilePrefab;
    [Tooltip("Burst played at the mouth on release. Should not play on awake.")]
    [SerializeField] ParticleSystem muzzleBurst;
    [SerializeField] AudioSource spitSound;

    [Header("Aim")]
    [Tooltip("Launch speed in units per second. The fly is 0.4 units across, so 12 is fast and flat.")]
    [SerializeField] float spitSpeed = 12f;
    [Tooltip("Aim along the camera rather than the fly's own heading. On by default, because FlyController levels the fly out whenever it is not actively moving, so body aim cannot shoot up or down while hovering or standing still. Turn it off for a spit that always leaves the mouth the way the mouth points.")]
    [SerializeField] bool aimAlongCamera = true;
    [Tooltip("The camera, used only when Aim Along Camera is on. Main Camera is used if left empty.")]
    [SerializeField] Transform cameraTransform;
    [Tooltip("How much of the fly's own flight speed the blob keeps. 0 = the shot ignores the fly's motion, 1 = full inheritance.")]
    [SerializeField, Range(0f, 1f)] float velocityInheritance = 0.5f;
    [Tooltip("Random spread in degrees, so a held-down burst does not stack blobs on one line.")]
    [SerializeField, Range(0f, 15f)] float spread = 1.5f;

    [Header("Fire Rate")]
    [Tooltip("Seconds between shots, measured from release to release.")]
    [SerializeField] float cooldown = 0.5f;
    [Tooltip("Keep spitting while the button is held instead of one blob per click.")]
    [SerializeField] bool autoFire;

    [Header("Lunge Animation")]
    [Tooltip("Seconds spent rearing back before the blob leaves.")]
    [SerializeField, Range(0.01f, 0.6f)] float windupTime = 0.12f;
    [Tooltip("Seconds of the forward snap. The blob leaves as this starts.")]
    [SerializeField, Range(0.01f, 0.4f)] float thrustTime = 0.07f;
    [Tooltip("Seconds spent easing back to the normal pose.")]
    [SerializeField, Range(0.01f, 1f)] float recoverTime = 0.22f;
    [Tooltip("How far the body travels between fully back and fully forward, in units. The fly is 0.4 units across.")]
    [SerializeField] float lungeDistance = 0.05f;
    [Tooltip("Degrees the nose lifts on the windup and drops on the thrust.")]
    [SerializeField] float lungeTilt = 14f;

    // Whether a shot is in progress, for anything that wants to hold off while the fly is spitting.
    public bool IsSpitting => spitTime < TotalLungeTime;

    FlyController fly;
    FlyBodyMotion bodyMotion;
    Animator animator;
    InputAction attackAction;
    Vector3 restPosition;
    Quaternion restRotation;
    // Seconds since the current shot started; parked past the end so nothing animates on the first frame.
    float spitTime = float.MaxValue;
    float cooldownLeft;
    bool hasSpitTrigger;

    float TotalLungeTime => windupTime + thrustTime + recoverTime;

    void Awake()
    {
        fly = GetComponent<FlyController>();
        // Throws if the action is missing: a weapon whose button silently does nothing is worse than
        // a clear error.
        attackAction = InputSystem.actions.FindAction(AttackActionName, throwIfNotFound: true);

        if (model == null)
        {
            animator = GetComponentInChildren<Animator>();
            if (animator != null)
                model = animator.transform;
        }
        else
        {
            animator = model.GetComponentInChildren<Animator>();
        }
        // The lunge needs a child to move: moving this object would fight FlyController's steering,
        // which owns this object's position and rotation.
        if (model != null && model != transform)
        {
            restPosition = model.localPosition;
            restRotation = model.localRotation;
        }
        else
        {
            model = null;
            Debug.LogWarning("FlySpit: no fly model child found; assign Model. Spitting without the lunge animation.", this);
        }

        bodyMotion = GetComponent<FlyBodyMotion>();
        hasSpitTrigger = HasTrigger(animator, SpitAnimParameter);

        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
        // Fails open: AimDirection falls back to the fly's own heading, which still shoots, just only
        // ever straight ahead.
        if (aimAlongCamera && cameraTransform == null)
            Debug.LogWarning("FlySpit: Aim Along Camera is on but no camera was found; aiming along the fly's heading instead.", this);

        if (projectilePrefab == null)
        {
            // Fails closed: this component exists only to launch that prefab.
            Debug.LogError("FlySpit: assign Projectile Prefab (the spit blob). Spitting disabled.", this);
            enabled = false;
        }
    }

    void OnEnable()
    {
        attackAction.Enable();
    }

    void Update()
    {
        cooldownLeft -= Time.deltaTime;
        spitTime += Time.deltaTime;

        bool wantsToSpit = autoFire ? attackAction.IsPressed() : attackAction.WasPressedThisFrame();
        if (wantsToSpit && cooldownLeft <= 0f)
            BeginSpit();

        // Released on the frame the windup ends rather than the frame the button was pressed, so the
        // blob appears out of a mouth that has already reared back.
        if (spitTime >= windupTime && spitTime - Time.deltaTime < windupTime)
            Release();
    }

    void BeginSpit()
    {
        spitTime = 0f;
        cooldownLeft = cooldown;
        if (hasSpitTrigger)
            animator.SetTrigger(SpitAnimParameter);
    }

    void Release()
    {
        Vector3 direction = AimDirection();
        // The fly's own speed is added after the spread, so spread stays an angle off the aim rather
        // than an angle off the combined path.
        Vector3 velocity = direction * spitSpeed + fly.FlyVelocity * velocityInheritance;

        SpitProjectile projectile = Instantiate(projectilePrefab, MuzzlePosition(), Quaternion.LookRotation(direction));
        projectile.Launch(velocity, transform);

        if (muzzleBurst != null)
            muzzleBurst.Play();
        if (spitSound != null)
            spitSound.Play();
    }

    Vector3 AimDirection()
    {
        Vector3 forward = aimAlongCamera && cameraTransform != null ? cameraTransform.forward : transform.forward;
        if (spread <= 0f)
            return forward;
        // A random rotation inside a cone of half-angle `spread` around the aim.
        return Quaternion.Euler(Random.Range(-spread, spread), Random.Range(-spread, spread), 0f) * forward;
    }

    Vector3 MuzzlePosition()
    {
        return muzzle != null ? muzzle.position : transform.TransformPoint(muzzleOffset);
    }

    // LateUpdate, after FlyBodyMotion has written the model's pose for this frame.
    void LateUpdate()
    {
        if (model == null)
            return;

        // FlyBodyMotion assigns the pose outright every frame, so while it is running its output is
        // the base to add the lunge to. Without it the model never moves, so the pose from Awake is.
        bool hasLiveBaseMotion = bodyMotion != null && bodyMotion.enabled;
        Vector3 basePosition = hasLiveBaseMotion ? model.localPosition : restPosition;
        Quaternion baseRotation = hasLiveBaseMotion ? model.localRotation : restRotation;

        float lunge = LungeAmount();
        if (lunge == 0f)
        {
            model.localPosition = basePosition;
            model.localRotation = baseRotation;
            return;
        }

        // Pre-multiplying puts the tilt in this object's space, so it is a nose dip along the fly's
        // heading and the model's own 180° X correction is left intact.
        Quaternion tilt = Quaternion.Euler(lunge * lungeTilt, 0f, 0f);
        model.localPosition = basePosition + Vector3.forward * (lunge * lungeDistance);
        model.localRotation = tilt * baseRotation;
    }

    // -1 fully reared back, 0 at rest, +1 fully lunged forward.
    float LungeAmount()
    {
        if (spitTime >= TotalLungeTime)
            return 0f;

        if (spitTime < windupTime)
        {
            // Eases out, so the fly settles into the wound-up pose instead of snapping to it.
            float t = spitTime / windupTime;
            return -Mathf.Sin(t * Mathf.PI * 0.5f);
        }

        float thrustEnd = windupTime + thrustTime;
        if (spitTime < thrustEnd)
        {
            // Linear: the snap should be the one part of the move with no easing on it.
            float t = (spitTime - windupTime) / thrustTime;
            return Mathf.Lerp(-1f, 1f, t);
        }

        float recover = (spitTime - thrustEnd) / recoverTime;
        return Mathf.Cos(recover * Mathf.PI * 0.5f);
    }

    // Setting a trigger the controller does not declare logs an error every shot, so it is checked once.
    static bool HasTrigger(Animator target, string parameterName)
    {
        if (target == null)
            return false;
        foreach (AnimatorControllerParameter parameter in target.parameters)
        {
            if (parameter.type == AnimatorControllerParameterType.Trigger && parameter.name == parameterName)
                return true;
        }
        return false;
    }
}
