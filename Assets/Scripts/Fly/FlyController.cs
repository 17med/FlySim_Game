using System;
using UnityEngine;
using UnityEngine.InputSystem;

// Third-person flight under the OrbitCamera. The fly steers toward where the camera looks, or keeps
// its heading while Alt (Free Look) is held. Flying turns on both axes, hovering and walking turn
// left/right only and stay level; Space takes off and walking off an edge starts a hover.
// Flight has momentum (linear acceleration, a glide to a stop, a Shift boost); walking has none.
// The field of view widens with speed, and FlyAiming slows everything while Aim is held.
// Flying into a surface is a bump: the fly loses the speed it was carrying into it and says so through
// Bumped, which FlyImpact turns into a thud and a puff of dust.
// Goes on an empty parent of the fly model, whose own 180° X rotation must not be overwritten.
[RequireComponent(typeof(CharacterController))]
public class FlyController : MonoBehaviour
{
    // Project-wide actions (InputSystem_Actions). Bindings are physical keys, so WASD is ZQSD on AZERTY.
    const string MoveActionName = "Player/Move";
    const string UpActionName = "Player/Jump";
    const string DownActionName = "Player/Crouch";
    const string FreeLookActionName = "Player/FreeLook";
    const string BoostActionName = "Player/Sprint";
    // Small constant push into the surface while walking, so ground contact is re-detected every frame
    // and walking off an edge is noticed immediately.
    const float GroundStickSpeed = 0.5f;
    // Turning faster than this (degrees per second) on the ground plays the walk animation, so turning
    // in place shows the legs moving; slower drift from camera smoothing does not count.
    const float TurnInPlaceAnimSpeed = 30f;
    // Floor for the top flight speed (units per second), so a zero speed set in the Inspector never
    // divides by zero.
    const float MinTopSpeed = 0.01f;
    // No single sweep of the collider moves it further than this share of its own radius. See MoveInSteps.
    const float MaxSweepShare = 0.5f;
    // Ceiling on the sweeps one frame may be split into, so a frame hitch or a breakpoint cannot turn
    // one Move into hundreds of them.
    const int MaxSweeps = 12;
    // Int parameter on the fly's Animator; values match MovementState.
    // Public so FlyAudio can read it on other players' copies, where this component is off.
    public static readonly int StateAnimParameter = Animator.StringToHash("stateanim");
    // Float parameter on the fly's Animator that multiplies the fly state's playback speed (wing beat rate).
    static readonly int WingSpeedAnimParameter = Animator.StringToHash("flyspeed");

    public enum MovementState { Idle = 0, Walking = 1, Flying = 2 }

    // One knock of the fly against a surface, handed to whatever is listening to Bumped. Speed is how
    // fast the fly was going into the surface, not its total speed: brushing along a wall at full
    // tilt is not a bump, flying straight into it is.
    public struct Bump
    {
        public Vector3 point;
        public Vector3 normal;
        public float speed;
        public Collider surface;
    }

    // Raised when the fly flies into something hard enough to be worth hearing, and on every bounce
    // of a knocked fly. Only on the owner's copy: on another player's this component is off and the
    // network moves the fly, so that copy's bumps are its owner's, replayed by FlyKnockbackNetwork.
    public event Action<Bump> Bumped;

    // Read by FlyAudio on the owner's copy; also what the Animator's "stateanim" is set to.
    public MovementState State { get; private set; }
    // Read by FlyBodyMotion. Zero while walking or hovering still: walking has no momentum.
    public Vector3 FlyVelocity => flyVelocity;
    // Written every frame by FlyAiming; 1 = normal. Speed Multiplier slows every kind of movement and
    // Fov Multiplier zooms the view in, so aiming never has to fight this script over the Camera.
    public float SpeedMultiplier { get; set; } = 1f;
    public float FovMultiplier { get; set; } = 1f;

    [Tooltip("The camera the player sees through (the one with OrbitCamera). Main Camera is used if left empty.")]
    [SerializeField] Transform cameraTransform;
    [Tooltip("The fly model's Animator (with the int parameter \"stateanim\"). Found automatically if left empty.")]
    [SerializeField] Animator animator;
    [SerializeField] float moveSpeed = 3f;
    [SerializeField] float walkSpeed = 1f;
    [SerializeField] float verticalSpeed = 2f;
    [Tooltip("Degrees per second the fly turns toward the camera's direction.")]
    [SerializeField] float turnSpeed = 360f;
    [Tooltip("Forward flying speed is multiplied by this while Shift (Sprint) is held.")]
    [SerializeField] float boostMultiplier = 2f;
    [Tooltip("How much of the boost climbing and diving get too, so Shift speeds up going up as well as going forward. 0 = Shift only boosts the Z/Q/S/D part, 1 = it boosts the lift just as much.")]
    [SerializeField, Range(0f, 1f)] float boostLiftShare = 1f;
    [Tooltip("Seconds the fly takes to reach Move Speed from rest. Speeding into a boost continues at the same rate. 0 = instant.")]
    [SerializeField, Range(0f, 1f)] float accelerationTime = 0.25f;
    [Tooltip("Seconds the fly takes to stop from Move Speed when released. Longer than acceleration so it glides to a stop.")]
    [SerializeField, Range(0f, 1f)] float decelerationTime = 0.4f;
    [Tooltip("How far below its start position the fly looks for ground when Play starts.")]
    [SerializeField] float startGroundCheck = 0.05f;
    [Tooltip("Wing beat playback speed while hovering still. 1 = the fly clip's own rate.")]
    [SerializeField] float hoverWingSpeed = 1f;
    // Kept low: the fly clip already beats 12 times a second, and much faster leaves too few rendered
    // frames per beat at 60 fps, so the flap turns into jitter.
    [Tooltip("Wing beat playback speed at full boost speed; cruising falls in between.")]
    [SerializeField] float boostWingSpeed = 1.5f;

    [Header("Camera")]
    [Tooltip("The camera whose field of view widens with speed. Taken from Camera Transform if left empty.")]
    [SerializeField] Camera flyCamera;
    [Tooltip("Field of view while walking or hovering still. Applied when Play starts.")]
    [SerializeField, Range(1f, 179f)] float defaultFov = 60f;
    [Tooltip("Field of view at full boost speed; cruising falls in between.")]
    [SerializeField, Range(1f, 179f)] float maxSpeedFov = 75f;
    [Tooltip("Seconds the field of view takes to follow a change in speed. 0 = instant.")]
    [SerializeField, Range(0f, 1f)] float fovChangeTime = 0.2f;
    [Tooltip("Extra degrees of field of view punched in the instant Shift is pressed, on top of the widening that speed itself brings, so a boost kicks rather than creeping in. It fades over Fov Change Time. 0 turns the kick off.")]
    [SerializeField, Min(0f)] float boostFovPunch = 10f;

    // Flying into a cupboard. The CharacterController stops the fly on its own, but left to itself it
    // then slides along the surface at the speed it arrived with, in silence, which reads as skating.
    [Header("Bumps")]
    [Tooltip("Slowest the fly can be going into a surface, in units per second, and still bump it. Below this it settles and slides exactly as it always did, so landings and crawling into a wall stay quiet.")]
    [SerializeField, Min(0f)] float bumpMinSpeed = 8f;
    [Tooltip("Share of the speed it was carrying into the surface that the fly loses on a bump. 1 = it stops dead against the wall, 0 = it keeps all of it and only the sound plays.")]
    [SerializeField, Range(0f, 1f)] float bumpAbsorb = 0.75f;
    [Tooltip("Seconds before another bump is reported. One Move can touch a corner with several colliders, and a knocked fly bounces many times a second; without this the thud would stack into a rattle.")]
    [SerializeField, Min(0f)] float bumpCooldown = 0.12f;

    // Slapped or hit (FlyKnockbackNetwork calls Knock): flung like a loose object, tumbling and
    // bouncing, and unsteerable until the stun wears off.
    [Header("Knocked")]
    [Tooltip("Seconds the fly cannot be steered (or spit) after a hit.")]
    [SerializeField, Min(0f)] float stunTime = 1f;
    // Stronger than real gravity: the Kitchen is scaled up (its falling objects use ScaledGravity
    // around 10x), and a fly falling at 9.81 units/s² there would drift down like a feather.
    [Tooltip("Downward pull while knocked, in units per second².")]
    [SerializeField, Min(0f)] float knockGravity = 60f;
    [Tooltip("Share of its speed the knocked fly loses to the air each second. Higher = it stops sooner.")]
    [SerializeField, Range(0f, 0.99f)] float knockAirDrag = 0.5f;
    [Tooltip("Share of its speed kept when the knocked fly bounces off a wall, floor or ceiling.")]
    [SerializeField, Range(0f, 1f)] float knockBounce = 0.4f;
    [Tooltip("Degrees per second the knocked fly spins while in the air.")]
    [SerializeField, Min(0f)] float tumbleSpeed = 720f;

    CharacterController controller;
    InputAction moveAction;
    InputAction upAction;
    InputAction downAction;
    InputAction freeLookAction;
    InputAction boostAction;
    Vector3 flyVelocity;
    // SmoothDamp's running state for the camera's field of view.
    float fovVelocity;
    // Degrees of the boost kick still to fade out of the field of view.
    float fovPunchLeft;
    float steerYaw;
    float steerPitch;
    bool isGrounded;
    bool isBoosting;
    Vector3 knockVelocity;
    Vector3 tumbleAxis;
    float stunLeft;
    float bumpReadyTime;

    // True while the fly is knocked and cannot be steered. FlySpit reads it so a stunned fly cannot spit.
    public bool IsStunned => stunLeft > 0f;

    // True while Shift is held and the fly is actually flying on it. Read by FlyRush, which turns it
    // into the wind and the streaks that sell the speed.
    public bool IsBoosting => isBoosting;

    // 0 while hovering still, 1 at full boost speed. Also read by FlyRush.
    public float SpeedFraction01 => Mathf.Clamp01(SpeedFraction());

    // True while the fly is standing on something. Only meaningful on the owner's copy: on another
    // player's this component is off and never updates it, which is why OldPersonStomp works the fly's
    // ground contact out from the geometry instead and only reads this as a shortcut.
    public bool IsGrounded => isGrounded;

    // Puts the fly somewhere else rather than moving it there: a respawn. A CharacterController keeps
    // its own idea of where it is and would overwrite a plain transform.position write on its next
    // Move, so it is switched off around the write - and put back exactly as it was found, because a
    // dead fly's controller is off and coming back must not switch it on ahead of FlyHealth.
    // Everything that was carrying the fly along is cleared, so it arrives at rest rather than still
    // flying, or still tumbling from the slap that killed it, and facing where the spawn point faces.
    public void Teleport(Vector3 position, Quaternion rotation)
    {
        if (controller != null)
        {
            bool wasEnabled = controller.enabled;
            controller.enabled = false;
            transform.SetPositionAndRotation(position, rotation);
            controller.enabled = wasEnabled;
        }
        else
        {
            transform.SetPositionAndRotation(position, rotation);
        }

        flyVelocity = Vector3.zero;
        knockVelocity = Vector3.zero;
        stunLeft = 0f;
        isBoosting = false;
        fovPunchLeft = 0f;
        // Otherwise the first frame back turns the fly straight off the spawn point's heading toward
        // wherever the camera happened to be looking when it died.
        steerYaw = rotation.eulerAngles.y;
        // eulerAngles comes back in 0 to 360, and steering pitch is signed either side of level.
        steerPitch = Mathf.DeltaAngle(0f, rotation.eulerAngles.x);
    }

    // Flings the fly at this velocity (units per second) and stuns it. Only call it on the owner's
    // copy: on another player's copy this component is off and the network moves the fly.
    public void Knock(Vector3 velocity)
    {
        knockVelocity = velocity;
        flyVelocity = Vector3.zero;
        stunLeft = stunTime;
        isGrounded = false;
        isBoosting = false;
        // Qualified: System is in scope here for Action, and both namespaces have a Random.
        tumbleAxis = UnityEngine.Random.onUnitSphere;
        // So the first wall the flung fly meets is heard, however recently it last bumped one.
        bumpReadyTime = 0f;
    }

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        // Throws if an action is missing: flying with a silently dead control is worse than a clear error.
        moveAction = InputSystem.actions.FindAction(MoveActionName, throwIfNotFound: true);
        upAction = InputSystem.actions.FindAction(UpActionName, throwIfNotFound: true);
        downAction = InputSystem.actions.FindAction(DownActionName, throwIfNotFound: true);
        freeLookAction = InputSystem.actions.FindAction(FreeLookActionName, throwIfNotFound: true);
        boostAction = InputSystem.actions.FindAction(BoostActionName, throwIfNotFound: true);
        steerYaw = transform.eulerAngles.y;

        if (animator == null)
            animator = GetComponentInChildren<Animator>();
        // Fails open: animation is cosmetic, so the fly stays controllable without it.
        if (animator == null)
            Debug.LogWarning("FlyController: no Animator found on the fly model; flying without animation.", this);

        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
        if (cameraTransform == null)
        {
            // Fails closed: every movement is relative to the camera, so without one the controls are meaningless.
            Debug.LogError("FlyController: no camera found. Assign Camera Transform or tag your camera MainCamera.", this);
            enabled = false;
        }

        if (flyCamera == null && cameraTransform != null)
            flyCamera = cameraTransform.GetComponent<Camera>();
        // Fails open: the field of view change is cosmetic, so the fly stays controllable without it.
        if (flyCamera == null)
            Debug.LogWarning("FlyController: no Camera found on Camera Transform; field of view stays fixed.", this);
    }

    // Ground contact is only detected by moving into it, and a hovering fly never moves, so a fly
    // placed on the ground would otherwise start in flying mode. Probe once straight down instead.
    void Start()
    {
        controller.Move(Vector3.down * startGroundCheck);
        isGrounded = (controller.collisionFlags & CollisionFlags.Below) != 0;
        if (!isGrounded)
            controller.Move(Vector3.up * startGroundCheck);

        if (flyCamera != null)
            flyCamera.fieldOfView = defaultFov;
    }

    void OnEnable()
    {
        moveAction.Enable();
        upAction.Enable();
        downAction.Enable();
        freeLookAction.Enable();
        boostAction.Enable();
    }

    void Update()
    {
        bool isFreeMode = freeLookAction.IsPressed();
        if (!isFreeMode)
            SteerTowardCamera();
        if (IsStunned)
        {
            MoveKnocked();
            return;
        }

        Vector2 move = moveAction.ReadValue<Vector2>();
        float lift = (upAction.IsPressed() ? 1f : 0f) - (downAction.IsPressed() ? 1f : 0f);
        bool wasGrounded = isGrounded;
        bool isWalking = isGrounded && lift <= 0f;
        if (isWalking)
            Walk(move);
        else
            Fly(move, lift);

        bool isFlying = !isWalking && (move != Vector2.zero || lift != 0f);
        bool isTurning = Rotate(isFlying, isGrounded && !wasGrounded);
        State = CurrentState(move, isTurning);
        if (animator != null)
        {
            animator.SetInteger(StateAnimParameter, (int)State);
            animator.SetFloat(WingSpeedAnimParameter, WingBeatSpeed());
        }
        UpdateFieldOfView();
    }

    // Moves like a thrown object - gravity, drag, a bounce on contact, a spin in the air - and
    // ignores input. The speed it ends with carries on as flight momentum.
    void MoveKnocked()
    {
        stunLeft -= Time.deltaTime;
        isBoosting = false;
        knockVelocity += Vector3.down * (knockGravity * Time.deltaTime);
        knockVelocity *= Mathf.Pow(1f - knockAirDrag, Time.deltaTime);

        // Bounces happen inside Move, in OnControllerColliderHit.
        CollisionFlags contacts = MoveInSteps(knockVelocity * Time.deltaTime);
        isGrounded = (contacts & CollisionFlags.Below) != 0;
        if (!isGrounded)
            transform.Rotate(tumbleAxis, tumbleSpeed * Time.deltaTime, Space.World);

        // Held at Flying for the whole stun: following ground contact would flip "stateanim" on every
        // bounce and restart the Animator's landing and take-off transitions over and over.
        State = MovementState.Flying;
        if (animator != null)
            animator.SetInteger(StateAnimParameter, (int)State);
        if (!IsStunned)
            flyVelocity = isGrounded ? Vector3.zero : knockVelocity;
    }

    // Called by the CharacterController during Move for every surface it touches. While knocked the
    // fly bounces off it, losing speed each time, so it comes to rest instead of skating along the floor.
    // Flying, it bumps: the speed it was carrying into the surface is absorbed, so hitting a cupboard
    // stops the fly against it rather than letting it grind along at full tilt.
    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        if (IsStunned)
        {
            // Only surfaces it is moving into: one it is already leaving was bounced off a moment ago.
            float intoSurface = -Vector3.Dot(knockVelocity, hit.normal);
            if (intoSurface <= 0f)
                return;
            ReportBump(hit, intoSurface);
            knockVelocity = Vector3.Reflect(knockVelocity, hit.normal) * knockBounce;
            return;
        }

        // Below the bump speed nothing is touched, so settling onto a surface, walking into a skirting
        // board and crawling along a wall all behave exactly as they did before bumps existed.
        float intoWall = -Vector3.Dot(flyVelocity, hit.normal);
        if (intoWall < bumpMinSpeed)
            return;
        ReportBump(hit, intoWall);
        flyVelocity += hit.normal * (intoWall * bumpAbsorb);
    }

    // One bump, at most one every Bump Cooldown. The cooldown is the whole reason this is not just an
    // event invocation: a single Move can report a corner's two colliders, and a knocked fly bounces
    // several times a second, so the thud would otherwise stack into a rattle.
    void ReportBump(ControllerColliderHit hit, float speed)
    {
        if (speed < bumpMinSpeed || Time.time < bumpReadyTime)
            return;
        bumpReadyTime = Time.time + bumpCooldown;
        Bumped?.Invoke(new Bump { point = hit.point, normal = hit.normal, speed = speed, surface = hit.collider });
    }

    // 0 while hovering still, 1 at full boost speed. Used for the wing beat and the field of view.
    float SpeedFraction()
    {
        // Max guards a zero top speed from a misconfigured Inspector: 0/0 would hand NaN downstream.
        float topSpeed = Mathf.Max(moveSpeed * boostMultiplier, MinTopSpeed);
        return flyVelocity.magnitude / topSpeed;
    }

    // Faster flight beats the wings faster, so hovering, cruising and boosting each read differently.
    float WingBeatSpeed()
    {
        return Mathf.Lerp(hoverWingSpeed, boostWingSpeed, SpeedFraction());
    }

    // Widens the view as the fly speeds up. Eased, so starting a boost doesn't snap the view - except
    // for the kick, which is the one part meant to be felt: pressing Shift adds Boost Fov Punch
    // degrees at once and they fade back out over Fov Change Time, so the boost reads as a shove
    // rather than as the view slowly opening while the fly accelerates.
    void UpdateFieldOfView()
    {
        if (flyCamera == null)
            return;
        float targetFov = Mathf.Lerp(defaultFov, maxSpeedFov, SpeedFraction()) * FovMultiplier + fovPunchLeft;
        // Camera.fieldOfView rejects anything outside this, and the punch on top of a high Max Speed
        // Fov can reach it.
        targetFov = Mathf.Clamp(targetFov, 1f, 179f);
        flyCamera.fieldOfView = Mathf.SmoothDamp(flyCamera.fieldOfView, targetFov, ref fovVelocity, fovChangeTime);
        // Gone in Fov Change Time whatever the punch was set to, so the kick and the easing that
        // follows it are always the same length.
        float fadeRate = boostFovPunch / Mathf.Max(fovChangeTime, Time.deltaTime);
        fovPunchLeft = Mathf.MoveTowards(fovPunchLeft, 0f, fadeRate * Time.deltaTime);
    }

    // Play mode only. In Free mode the last value is kept, which is what holds the fly's heading.
    void SteerTowardCamera()
    {
        Vector3 forward = cameraTransform.forward;
        steerYaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        // Looking up is a negative rotation around X in Unity.
        steerPitch = -Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
    }

    // Walks flat along the surface in the steering direction; steering pitch is ignored.
    void Walk(Vector2 move)
    {
        // The ground stick push is not slowed: it keeps ground contact, it is not movement.
        Vector3 velocity = Quaternion.Euler(0f, steerYaw, 0f) * new Vector3(move.x, 0f, move.y) * (walkSpeed * SpeedMultiplier)
                         + Vector3.down * GroundStickSpeed;
        MoveAndDetectGround(velocity);
        // Landing absorbs all flight momentum, so the next take-off starts from rest.
        flyVelocity = Vector3.zero;
        isBoosting = false;
    }

    // Z/S fly along the full steering direction (including up/down); Space/Ctrl are straight up/down.
    // Shift boosts the Z/Q/S/D part, and as much of the climb and the dive as Boost Lift Share asks for.
    void Fly(Vector2 move, float lift)
    {
        bool wantsBoost = boostAction.IsPressed();
        float speed = wantsBoost ? moveSpeed * boostMultiplier : moveSpeed;
        // Lerped rather than multiplied by the share, so share 0 leaves the lift at its own speed
        // (how Shift used to work) and share 1 boosts it by the full multiplier.
        float lifted = verticalSpeed * (wantsBoost ? Mathf.Lerp(1f, boostMultiplier, boostLiftShare) : 1f);
        Vector3 targetVelocity = (Quaternion.Euler(steerPitch, steerYaw, 0f) * new Vector3(move.x, 0f, move.y) * speed
                               + Vector3.up * (lift * lifted)) * SpeedMultiplier;
        // On the press, not for as long as Shift is down: the kick belongs to starting the boost.
        if (wantsBoost && !isBoosting)
            fovPunchLeft = boostFovPunch;
        isBoosting = wantsBoost;
        flyVelocity = Accelerate(targetVelocity);
        // Hovering makes no move at all: there is no gravity, and a zero-length move reports no contact.
        if (flyVelocity == Vector3.zero)
            return;
        MoveAndDetectGround(flyVelocity);
    }

    // Changes the flight velocity at a constant rate, so starts, stops and boosts read as a steady
    // push rather than an ease. Slowing down uses the longer time, which reads as a glide.
    Vector3 Accelerate(Vector3 targetVelocity)
    {
        bool isSpeedingUp = targetVelocity.sqrMagnitude >= flyVelocity.sqrMagnitude;
        float rampTime = isSpeedingUp ? accelerationTime : decelerationTime;
        // 0 means instant, as the Inspector tooltips say; it would also divide by zero below.
        if (rampTime <= 0f)
            return targetVelocity;
        // Against the speed actually being asked for, not against Move Speed: dividing the cruising
        // speed by the ramp time meant a boost, which is twice as fast, took twice as long to come up
        // to, so the one move that should feel like a kick was the softest one the fly had.
        float rampSpeed = Mathf.Max(moveSpeed, targetVelocity.magnitude);
        float maxSpeedChange = rampSpeed / rampTime * Time.deltaTime;
        return Vector3.MoveTowards(flyVelocity, targetVelocity, maxSpeedChange);
    }

    void MoveAndDetectGround(Vector3 velocity)
    {
        CollisionFlags contacts = MoveInSteps(velocity * Time.deltaTime);
        isGrounded = (contacts & CollisionFlags.Below) != 0;
    }

    // One Move, split so no single sweep is longer than half the collider's own radius.
    // A CharacterController sweeps its capsule along the whole delta at once, and a delta longer than
    // the thing in its way can finish on the far side of it: the fly's collider is half a unit across
    // and a boost carries it sixty units a second, so a single dropped frame is a hand's width of
    // kitchen covered in one sweep - which is how a fast fly left the house through a cupboard door or
    // a wall. Splitting it also keeps the knocked fly's bounces honest, because each sweep gets its
    // own OnControllerColliderHit instead of one call for the whole flight.
    // Normal flight is one sweep, so this costs nothing until the fly is actually moving that fast.
    CollisionFlags MoveInSteps(Vector3 delta)
    {
        float radius = Mathf.Abs(controller.radius * transform.lossyScale.x);
        float maxSweep = Mathf.Max(radius, MinTopSpeed) * MaxSweepShare;
        int sweeps = Mathf.Clamp(Mathf.CeilToInt(delta.magnitude / maxSweep), 1, MaxSweeps);
        Vector3 step = delta / sweeps;
        CollisionFlags contacts = CollisionFlags.None;
        for (int i = 0; i < sweeps; i++)
        {
            // The last sweep's flags, not every sweep's or-ed together: this has to mean the same
            // thing as one big Move's, which is what the fly is touching once it has got there.
            // Or-ing them would leave a fly that clipped the floor on the way up reading as grounded.
            contacts = controller.Move(step);
        }
        return contacts;
    }

    // Uses ground contact after this frame's move, so taking off or walking off an edge switches to
    // Flying at once. Hovering counts as Flying, and turning in place on the ground counts as walking.
    MovementState CurrentState(Vector2 move, bool isTurning)
    {
        if (!isGrounded)
            return MovementState.Flying;
        return move != Vector2.zero || isTurning ? MovementState.Walking : MovementState.Idle;
    }

    // Flying: turns toward the steering direction on both axes (left/right and up/down).
    // Walking or hovering: turns toward it left/right only and stays level.
    // Returns whether the fly turned noticeably this frame.
    bool Rotate(bool isFlying, bool hasJustLanded)
    {
        // Snap level the moment it lands so it reads as standing, not hovering.
        if (hasJustLanded)
            transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);

        float pitch = isFlying ? steerPitch : 0f;
        Quaternion target = Quaternion.Euler(pitch, steerYaw, 0f);
        Quaternion before = transform.rotation;
        transform.rotation = Quaternion.RotateTowards(before, target, turnSpeed * Time.deltaTime);
        return Quaternion.Angle(before, transform.rotation) > TurnInPlaceAnimSpeed * Time.deltaTime;
    }
}
