using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Pool;

// The fly's weapon: a gob of spit, fired with the Attack action (left click), only while aiming
// (Aim held; AimShootsystem on the camera reports it). Put this next to FlyController on the fly's
// parent object, the same one FlyBodyMotion goes on.
// Blobs come from a pool instead of being created and destroyed on every shot, and a few are made
// when Play starts, so firing does not pay for creating objects (and the garbage that leaves).
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
    // Below this horizontal distance (units) a shot is straight up or down and has no arc to correct.
    const float MinArcDistance = 1e-4f;

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
    // 10 rather than 12: at 12 the blob covered half a fly-length per frame at 60 fps and was gone
    // before the eye picked it up leaving the mouth.
    [Tooltip("Launch speed in units per second. The fly is 0.4 units across, so 10 is still fast and fairly flat; lower is easier to follow with the eye.")]
    [SerializeField] float spitSpeed = 10f;
    [Tooltip("Size of the spit: the blob, its hit size, its trail drops and its splash. 1 = the sizes set on the projectile prefab. Can be changed in Play mode; the next shot uses it.")]
    [SerializeField, Range(0.2f, 100f)] float spitSize = 1f;
    [Tooltip("Aim along the camera rather than the fly's own heading. On by default, because FlyController levels the fly out whenever it is not actively moving, so body aim cannot shoot up or down while hovering or standing still. Turn it off for a spit that always leaves the mouth the way the mouth points.")]
    [SerializeField] bool aimAlongCamera = true;
    [Tooltip("The camera, used only when Aim Along Camera is on. Main Camera is used if left empty.")]
    [SerializeField] Transform cameraTransform;
    [Tooltip("How much of the fly's own flight speed the blob keeps. 0 = the shot ignores the fly's motion, 1 = full inheritance.")]
    [SerializeField, Range(0f, 1f)] float velocityInheritance = 0.5f;
    [Tooltip("Random spread in degrees, so a held-down burst does not stack blobs on one line.")]
    [SerializeField, Range(0f, 15f)] float spread = 1.5f;
    [Tooltip("While aiming, how far the crosshair looks for something to hit. Past this the spit flies toward a point this far away.")]
    [SerializeField] float crosshairRange = 30f;
    [Tooltip("Layers the crosshair can land on while aiming.")]
    [SerializeField] LayerMask crosshairLayers = ~0;

    [Header("Fire Rate")]
    [Tooltip("Seconds between shots, measured from release to release.")]
    [SerializeField] float cooldown = 0.5f;

    [Header("Ammo")]
    [SerializeField] SpitAmmo ammo = new SpitAmmo();
    [Tooltip("Played when the fly tries to spit with no shots left. Optional.")]
    [SerializeField] AudioSource emptySound;
    [Tooltip("The on-screen shot drops. Updated every time a shot is used or comes back. Optional.")]
    [SerializeField] spitinterface spitInterface;

    [Header("Game Feel")]
    [Tooltip("Shaken a little on every shot. Taken from the camera (Camera Transform or Main Camera) if left empty. Optional.")]
    [SerializeField] CameraShake cameraShake;
    [Tooltip("How hard each shot shakes the camera, 0 to 1.")]
    [SerializeField, Range(0f, 1f)] float shotShake = 0.6f;

    // Hold the button to charge, let go to fire. Because a hold has to mean something different from
    // a tap, every shot (normal ones too) leaves when the button is let go, not when it is pressed.
    [Header("Big Shot")]
    [Tooltip("Seconds the button must be held for the shot to become a Big Shot.")]
    [SerializeField, Range(0.05f, 2f)] float chargeTime = 0.4f;
    [Tooltip("Shots a Big Shot uses. With fewer left, holding cannot charge and letting go fires a normal shot.")]
    [SerializeField, Range(1, 10)] int bigShotCost = 2;
    [Tooltip("Big Shot size compared to a normal shot: 2 = twice as big, 5 = five times, 6 = six times. Scales the blob, its hit size, its trail and its splash.")]
    [SerializeField, Range(1f, 10f)] float bigShotSizeMultiplier = 2f;
    [Tooltip("The Big Shot's lunge (how far back and forward the fly moves, and how much it tilts), as a multiple of the normal one.")]
    [SerializeField, Range(1f, 3f)] float bigLungeScale = 1.6f;
    [Tooltip("The Big Shot's thrust and recover take this many times longer, so the heavier shot reads as heavier.")]
    [SerializeField, Range(1f, 3f)] float bigLungeSlowdown = 1.5f;
    [Tooltip("How far the fly trembles while fully charged, in units. The fly is 0.4 units across.")]
    [SerializeField, Range(0f, 0.03f)] float chargeTremble = 0.004f;
    [Tooltip("Steady camera rumble while fully charged, 0 to 1.")]
    [SerializeField, Range(0f, 1f)] float chargeRumble = 0.35f;
    [Tooltip("How hard a Big Shot shakes the camera, 0 to 1.")]
    [SerializeField, Range(0f, 1f)] float bigShotShake = 1f;
    [Tooltip("Muzzle burst size on a Big Shot, as a multiple of its normal size. Needs the burst's Scaling Mode on Local or Hierarchy.")]
    [SerializeField, Range(1f, 5f)] float bigBurstScale = 2f;
    [Tooltip("Played on a Big Shot instead of Spit Sound. Spit Sound is used if left empty.")]
    [SerializeField] AudioSource bigSpitSound;

    [Header("Lunge Animation")]
    // Short on purpose: the blob only leaves once the windup ends, so this is the delay between the
    // click and the shot, and 0.12 s of it felt like input lag.
    [Tooltip("Seconds spent rearing back before the blob leaves. This is also the delay between clicking and the shot.")]
    [SerializeField, Range(0.01f, 0.6f)] float windupTime = 0.05f;
    [Tooltip("Seconds of the forward snap. The blob leaves as this starts.")]
    [SerializeField, Range(0.01f, 0.4f)] float thrustTime = 0.07f;
    [Tooltip("Seconds spent easing back to the normal pose.")]
    [SerializeField, Range(0.01f, 1f)] float recoverTime = 0.22f;
    [Tooltip("How far the body travels between fully back and fully forward, in units. The fly is 0.4 units across.")]
    [SerializeField] float lungeDistance = 0.05f;
    [Tooltip("Degrees the nose lifts on the windup and drops on the thrust.")]
    [SerializeField] float lungeTilt = 14f;

    [Header("Pool")]
    [Tooltip("Blobs made when Play starts, so the first shots do not pay for creating them.")]
    [SerializeField, Range(0, 16)] int prewarmCount = 4;
    [Tooltip("Most unused blobs kept for reuse. Extra blobs are destroyed when they finish.")]
    [SerializeField, Range(1, 64)] int maxPooledBlobs = 16;

    // Whether a shot is in progress, for anything that wants to hold off while the fly is spitting.
    public bool IsSpitting => spitTime < TotalLungeTime;
    // Written every frame by AimShootsystem. Spitting only starts while this is true. It also changes
    // the aim: while aiming the camera sits off to one side, so its forward line no longer passes
    // through the fly and spitting along it would land beside the crosshair; aiming at the point under
    // the crosshair instead makes the shot land on it.
    public bool IsAiming { get; set; }
    // For a HUD: shots available now, the full amount, and how far (0 to 1) the next shot is refilled.
    public int ShotsLeft => ammo.ShotsLeft;
    public int MaxShots => ammo.MaxShots;
    public float RefillProgress => ammo.RefillProgress;
    // True while the button has been held long enough, with enough shots, for letting go to fire a Big Shot.
    public bool IsCharged => isHolding && holdTime >= chargeTime && ammo.CanSpend(bigShotCost);

    readonly RaycastHit[] crosshairHits = new RaycastHit[16];
    ObjectPool<SpitProjectile> projectilePool;
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
    // The button went down while a shot was allowed and has not been let go yet.
    bool isHolding;
    float holdTime;
    // The current shot's lunge: its size (1 normal, Big Lunge Scale for a Big Shot), its phase lengths,
    // and how far back the fly already was when it started, so the windup carries on from the charge
    // pose instead of jumping back to rest first.
    float shotLungeScale = 1f;
    float shotThrustTime;
    float shotRecoverTime;
    float windupStartAmount;
    bool isBigShotInProgress;
    Vector3 burstRestScale = Vector3.one;

    float TotalLungeTime => windupTime + shotThrustTime + shotRecoverTime;

    void Awake()
    {
        fly = GetComponent<FlyController>();
        ammo.Fill();
        shotThrustTime = thrustTime;
        shotRecoverTime = recoverTime;
        if (muzzleBurst != null)
            burstRestScale = muzzleBurst.transform.localScale;
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
        // Fails open: with no CameraShake on the camera the fly shoots the same, just without the jolt.
        if (cameraShake == null && cameraTransform != null)
            cameraShake = cameraTransform.GetComponent<CameraShake>();
        // Fails open: AimDirection falls back to the fly's own heading, which still shoots, just only
        // ever straight ahead.
        if (aimAlongCamera && cameraTransform == null)
            Debug.LogWarning("FlySpit: Aim Along Camera is on but no camera was found; aiming along the fly's heading instead.", this);

        if (projectilePrefab == null)
        {
            // Fails closed: this component exists only to launch that prefab.
            Debug.LogError("FlySpit: assign Projectile Prefab (the spit blob). Spitting disabled.", this);
            enabled = false;
            return;
        }

        projectilePool = new ObjectPool<SpitProjectile>(CreateProjectile, actionOnRelease: DeactivateProjectile,
            actionOnDestroy: DestroyProjectile, defaultCapacity: prewarmCount, maxSize: maxPooledBlobs);
    }

    // Creates the blobs up front: taking them all out and putting them back leaves them in the pool.
    void Start()
    {
        UpdateSpitInterface();
        var prewarmed = new List<SpitProjectile>(prewarmCount);
        for (int i = 0; i < prewarmCount; i++)
            prewarmed.Add(projectilePool.Get());
        foreach (SpitProjectile projectile in prewarmed)
            projectilePool.Release(projectile);
    }

    // Unused blobs belong to this fly's pool; blobs still in flight finish and are left inactive.
    void OnDestroy()
    {
        projectilePool?.Clear();
    }

    // Parked inactive until Launch places and activates it, so it never shows or emits at the spot it
    // was created.
    SpitProjectile CreateProjectile()
    {
        SpitProjectile projectile = Instantiate(projectilePrefab);
        projectile.gameObject.SetActive(false);
        projectile.SetPool(projectilePool);
        return projectile;
    }

    static void DeactivateProjectile(SpitProjectile projectile)
    {
        projectile.gameObject.SetActive(false);
    }

    static void DestroyProjectile(SpitProjectile projectile)
    {
        // Null when the scene unloading already destroyed it.
        if (projectile != null)
            Destroy(projectile.gameObject);
    }

    void OnEnable()
    {
        attackAction.Enable();
    }

    void Update()
    {
        cooldownLeft -= Time.deltaTime;
        spitTime += Time.deltaTime;
        if (ammo.Tick(Time.deltaTime))
            UpdateSpitInterface();

        HandleAttackButton();

        // Released on the frame the windup ends rather than the frame the button was let go, so the
        // blob appears out of a mouth that has already reared back. A shot already winding up still
        // leaves if Aim is let go mid-windup: cancelling it would leave the fly rearing back for nothing.
        if (spitTime >= windupTime && spitTime - Time.deltaTime < windupTime)
            Release(isBigShotInProgress);
    }

    // Press starts a hold, holding charges, letting go fires: a Big Shot if charged, otherwise a normal one.
    void HandleAttackButton()
    {
        if (attackAction.WasPressedThisFrame())
            TryStartHold();
        if (!isHolding)
            return;

        // Letting go of Aim while charging cancels the shot and spends nothing.
        if (!IsAiming)
        {
            EndHold();
            return;
        }

        holdTime += Time.deltaTime;
        if (!attackAction.IsPressed())
        {
            // Both read before EndHold, which clears the hold they are measured from.
            bool isBigShot = IsCharged;
            float heldRear = ChargeAmount();
            EndHold();
            BeginSpit(isBigShot, heldRear);
            return;
        }

        if (IsCharged)
            PlayChargeFeedback();
    }

    void TryStartHold()
    {
        if (!IsAiming || cooldownLeft > 0f)
            return;
        if (!ammo.CanSpend(1))
        {
            PlayEmpty();
            return;
        }
        isHolding = true;
        holdTime = 0f;
    }

    void EndHold()
    {
        isHolding = false;
        holdTime = 0f;
        if (spitInterface != null)
            spitInterface.ClearCharge();
    }

    // Runs every frame while fully charged; the camera rumble only lasts the frame it is asked for.
    void PlayChargeFeedback()
    {
        if (cameraShake != null)
            cameraShake.Rumble(chargeRumble);
        if (spitInterface != null)
            spitInterface.ShowCharge(ammo.ShotsLeft, bigShotCost);
    }

    // heldRear: how far back the hold had the fly (ChargeAmount), so the windup carries on from there.
    void BeginSpit(bool isBigShot, float heldRear)
    {
        windupStartAmount = heldRear;
        isBigShotInProgress = isBigShot;
        ammo.TrySpend(isBigShot ? bigShotCost : 1);
        UpdateSpitInterface();
        cooldownLeft = cooldown;

        float slowdown = isBigShot ? bigLungeSlowdown : 1f;
        shotLungeScale = isBigShot ? bigLungeScale : 1f;
        shotThrustTime = thrustTime * slowdown;
        shotRecoverTime = recoverTime * slowdown;
        // A charged fly is already fully reared back, so the Big Shot skips the windup and leaves this
        // frame; a normal shot winds up the rest of the way from wherever the hold left it.
        spitTime = isBigShot ? windupTime : 0f;
        if (hasSpitTrigger)
            animator.SetTrigger(SpitAnimParameter);
    }

    // Uses the cooldown too, so mashing the button while empty clicks once per cooldown.
    void PlayEmpty()
    {
        cooldownLeft = cooldown;
        if (emptySound != null)
            emptySound.Play();
        if (spitInterface != null)
            spitInterface.PlayEmpty();
    }

    // Fails open: with no Spit Interface assigned the fly still shoots and refills, just with no drops
    // on screen.
    void UpdateSpitInterface()
    {
        if (spitInterface != null)
            spitInterface.ShowShots(ammo.ShotsLeft);
    }

    void Release(bool isBigShot)
    {
        Vector3 direction = AimDirection();
        // The fly's own speed is added after the spread, so spread stays an angle off the aim rather
        // than an angle off the combined path.
        Vector3 velocity = direction * spitSpeed + fly.FlyVelocity * velocityInheritance;
        float size = isBigShot ? spitSize * bigShotSizeMultiplier : spitSize;
        projectilePool.Get().Launch(MuzzlePosition(), velocity, transform, size);

        PlayMuzzleBurst(isBigShot ? bigBurstScale : 1f);
        AudioSource sound = isBigShot && bigSpitSound != null ? bigSpitSound : spitSound;
        if (sound != null)
            sound.Play();
        // On release, not on the click: the shot has already been aimed, so the jolt never moves it.
        if (cameraShake != null)
            cameraShake.Shake(isBigShot ? bigShotShake : shotShake);
    }

    // The burst is rescaled before each play; particles from a previous burst still in the air are
    // resized with it, which is too brief to notice at these lifetimes.
    void PlayMuzzleBurst(float scale)
    {
        if (muzzleBurst == null)
            return;
        muzzleBurst.transform.localScale = burstRestScale * scale;
        muzzleBurst.Play();
    }

    Vector3 AimDirection()
    {
        Vector3 forward = AimForward();
        if (spread <= 0f)
            return forward;
        // A random rotation inside a cone of half-angle `spread` around the aim.
        return Quaternion.Euler(Random.Range(-spread, spread), Random.Range(-spread, spread), 0f) * forward;
    }

    Vector3 AimForward()
    {
        if (cameraTransform == null)
            return transform.forward;
        if (!IsAiming)
            return aimAlongCamera ? cameraTransform.forward : transform.forward;

        Vector3 toCrosshair = CrosshairPoint() - MuzzlePosition();
        // A point behind or right at the mouth (something between the camera and the fly) has no
        // usable direction, so the shot falls back to the camera's forward.
        if (Vector3.Dot(toCrosshair, cameraTransform.forward) <= 0f)
            return cameraTransform.forward;
        return ArcDirection(toCrosshair);
    }

    // Launch direction whose falling arc passes through `offset` (from the mouth) at Spit Speed under
    // the blob's own gravity. Aiming straight at the point made the blob land below it, by about a
    // fly-length at 5 units. Exact when Spread and Velocity Inheritance are 0; both are added on top.
    Vector3 ArcDirection(Vector3 offset)
    {
        float gravity = projectilePrefab.Gravity;
        Vector3 horizontal = new Vector3(offset.x, 0f, offset.z);
        float distance = horizontal.magnitude;
        // Straight up or down, no gravity, or no speed: there is no arc to correct.
        if (gravity <= 0f || spitSpeed <= 0f || distance < MinArcDistance)
            return offset.normalized;

        float speedSquared = spitSpeed * spitSpeed;
        float discriminant = speedSquared * speedSquared
                           - gravity * (gravity * distance * distance + 2f * offset.y * speedSquared);
        float angle = discriminant >= 0f
            // The flatter of the two arcs that pass through the point: the one the eye expects.
            ? Mathf.Atan((speedSquared - Mathf.Sqrt(discriminant)) / (gravity * distance))
            // Out of reach: the angle that throws farthest toward the point (45° plus half its elevation).
            : (Mathf.PI * 0.25f) + Mathf.Atan2(offset.y, distance) * 0.5f;
        return horizontal / distance * Mathf.Cos(angle) + Vector3.up * Mathf.Sin(angle);
    }

    // What the screen centre is pointing at: the nearest surface along the camera's forward, or a point
    // Crosshair Range away if there is none. The fly's own colliders are skipped, since the camera
    // looks past it.
    Vector3 CrosshairPoint()
    {
        Vector3 origin = cameraTransform.position;
        Vector3 direction = cameraTransform.forward;
        int hitCount = Physics.RaycastNonAlloc(origin, direction, crosshairHits, crosshairRange,
                                               crosshairLayers, QueryTriggerInteraction.Ignore);
        float nearest = crosshairRange;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = crosshairHits[i];
            if (hit.distance < nearest && !hit.transform.IsChildOf(transform.root))
                nearest = hit.distance;
        }
        return origin + direction * nearest;
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

        // A fully charged fly shakes in place, like it is straining to hold the spit in.
        Vector3 tremble = IsCharged ? Random.insideUnitSphere * chargeTremble : Vector3.zero;
        // Pre-multiplying puts the tilt in this object's space, so it is a nose dip along the fly's
        // heading and the model's own 180° X correction is left intact.
        Quaternion tilt = Quaternion.Euler(lunge * lungeTilt, 0f, 0f);
        model.localPosition = basePosition + Vector3.forward * (lunge * lungeDistance) + tremble;
        model.localRotation = tilt * baseRotation;
    }

    // Negative is reared back, 0 at rest, positive lunged forward. A normal shot goes from -1 to +1;
    // a Big Shot, and the charge before it, goes Big Lunge Scale times as far.
    float LungeAmount()
    {
        if (isHolding)
            return -ChargeAmount();
        if (spitTime >= TotalLungeTime)
            return 0f;

        float scale = shotLungeScale;
        if (spitTime < windupTime)
        {
            // Eases out, so the fly settles into the wound-up pose instead of snapping to it. Starts
            // from the held pose, so a tap that already reared back partway does not jump to rest first.
            float t = spitTime / windupTime;
            return -Mathf.Lerp(windupStartAmount, scale, Mathf.Sin(t * Mathf.PI * 0.5f));
        }

        float thrustEnd = windupTime + shotThrustTime;
        if (spitTime < thrustEnd)
        {
            // Eased in and out. It used to be linear, so the snap was the one unpolished part of the
            // move, but at 0.07 s that is about 4 frames, and starting and stopping at full speed
            // read as a jerk. The windup ends at rest and the recover starts at rest, so easing both
            // ends keeps the whole lunge free of sudden speed changes.
            float t = (spitTime - windupTime) / shotThrustTime;
            return Mathf.Lerp(-scale, scale, Mathf.SmoothStep(0f, 1f, t));
        }

        float recover = (spitTime - thrustEnd) / shotRecoverTime;
        return scale * Mathf.Cos(recover * Mathf.PI * 0.5f);
    }

    // How far back holding the button has the fly: eases up to 1 over Charge Time, or up to Big Lunge
    // Scale when there are enough shots for a Big Shot, so the deeper pose means "this one is big".
    float ChargeAmount()
    {
        if (!isHolding)
            return 0f;
        float fullRear = ammo.CanSpend(bigShotCost) ? bigLungeScale : 1f;
        float t = Mathf.Clamp01(holdTime / chargeTime);
        return fullRear * Mathf.Sin(t * Mathf.PI * 0.5f);
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
