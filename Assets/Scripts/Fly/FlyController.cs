using UnityEngine;
using UnityEngine.InputSystem;

// Third-person flight under the OrbitCamera. The fly steers toward where the camera looks, or keeps
// its heading while Alt (Free Look) is held. Flying turns on both axes, hovering and walking turn
// left/right only and stay level; Space takes off and walking off an edge starts a hover.
// Flight has momentum (linear acceleration, a glide to a stop, a Shift boost); walking has none.
// The field of view widens with speed, and FlyAiming slows everything while Aim is held.
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
    // Int parameter on the fly's Animator; values match MovementState.
    // Public so FlyAudio can read it on other players' copies, where this component is off.
    public static readonly int StateAnimParameter = Animator.StringToHash("stateanim");
    // Float parameter on the fly's Animator that multiplies the fly state's playback speed (wing beat rate).
    static readonly int WingSpeedAnimParameter = Animator.StringToHash("flyspeed");

    public enum MovementState { Idle = 0, Walking = 1, Flying = 2 }

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
    float steerYaw;
    float steerPitch;
    bool isGrounded;
    Vector3 knockVelocity;
    Vector3 tumbleAxis;
    float stunLeft;

    // True while the fly is knocked and cannot be steered. FlySpit reads it so a stunned fly cannot spit.
    public bool IsStunned => stunLeft > 0f;

    // Flings the fly at this velocity (units per second) and stuns it. Only call it on the owner's
    // copy: on another player's copy this component is off and the network moves the fly.
    public void Knock(Vector3 velocity)
    {
        knockVelocity = velocity;
        flyVelocity = Vector3.zero;
        stunLeft = stunTime;
        isGrounded = false;
        tumbleAxis = Random.onUnitSphere;
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
        knockVelocity += Vector3.down * (knockGravity * Time.deltaTime);
        knockVelocity *= Mathf.Pow(1f - knockAirDrag, Time.deltaTime);

        // Bounces happen inside Move, in OnControllerColliderHit.
        CollisionFlags contacts = controller.Move(knockVelocity * Time.deltaTime);
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
    void OnControllerColliderHit(ControllerColliderHit hit)
    {
        // Only surfaces it is moving into: one it is already leaving was bounced off a moment ago.
        if (!IsStunned || Vector3.Dot(knockVelocity, hit.normal) >= 0f)
            return;
        knockVelocity = Vector3.Reflect(knockVelocity, hit.normal) * knockBounce;
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

    // Widens the view as the fly speeds up. Eased, so starting a boost doesn't snap the view.
    void UpdateFieldOfView()
    {
        if (flyCamera == null)
            return;
        float targetFov = Mathf.Lerp(defaultFov, maxSpeedFov, SpeedFraction()) * FovMultiplier;
        flyCamera.fieldOfView = Mathf.SmoothDamp(flyCamera.fieldOfView, targetFov, ref fovVelocity, fovChangeTime);
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
    }

    // Z/S fly along the full steering direction (including up/down); Space/Ctrl are straight up/down.
    // Shift boosts only the Z/Q/S/D part; climbing and diving keep their speed.
    void Fly(Vector2 move, float lift)
    {
        float speed = boostAction.IsPressed() ? moveSpeed * boostMultiplier : moveSpeed;
        Vector3 targetVelocity = (Quaternion.Euler(steerPitch, steerYaw, 0f) * new Vector3(move.x, 0f, move.y) * speed
                               + Vector3.up * (lift * verticalSpeed)) * SpeedMultiplier;
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
        float maxSpeedChange = moveSpeed / rampTime * Time.deltaTime;
        return Vector3.MoveTowards(flyVelocity, targetVelocity, maxSpeedChange);
    }

    void MoveAndDetectGround(Vector3 velocity)
    {
        controller.Move(velocity * Time.deltaTime);
        isGrounded = (controller.collisionFlags & CollisionFlags.Below) != 0;
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
