using UnityEngine;
using UnityEngine.InputSystem;

// Third-person flight under the OrbitCamera, which circles the fly freely with the mouse in every
// state. The fly steers by a steering direction:
//   Play mode (default) - the steering direction is where the camera looks
//   Free mode (hold Alt) - the steering direction freezes, so the fly keeps its heading and keeps
//                          moving that way while the camera looks around; on release it turns back
// How the fly follows the steering direction depends on its state:
//   flying   - turns toward it on both axes (left/right and up/down)
//   hovering - turns toward it left/right only and eases back to level
//   walking  - on a surface: snaps level the moment it lands, walks flat, turns left/right only;
//              Space takes off, walking off an edge switches to hovering
// In the air the fly has momentum: it eases up to speed, glides to a stop when released, and holding
// Shift (Sprint) boosts its forward speed. Walking has no momentum.
// Put this on an empty parent of the fly model: the model itself carries a 180° X rotation in the
// scene to stand upright, and rotating it directly would overwrite that correction.
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
    // A gliding fly slower than this (units per second) stops completely, so hovering is truly still
    // again: easing alone only approaches zero and would keep making tiny moves forever.
    const float StopSpeed = 0.01f;
    // Int parameter on the fly's Animator; values match MovementState.
    static readonly int StateAnimParameter = Animator.StringToHash("stateanim");
    // Float parameter on the fly's Animator that multiplies the fly state's playback speed (wing beat rate).
    static readonly int WingSpeedAnimParameter = Animator.StringToHash("flyspeed");

    public enum MovementState { Idle = 0, Walking = 1, Flying = 2 }

    // Read by FlyAudio; also what the Animator's "stateanim" is set to.
    public MovementState State { get; private set; }
    // Read by FlyBodyMotion. Zero while walking or hovering still: walking has no momentum.
    public Vector3 FlyVelocity => flyVelocity;

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
    [Tooltip("Seconds the fly takes to reach a higher speed, including speeding into a boost. 0 = instant.")]
    [SerializeField, Range(0f, 1f)] float accelerationTime = 0.25f;
    [Tooltip("Seconds the fly takes to slow down when released. Longer than acceleration so it glides to a stop.")]
    [SerializeField, Range(0f, 1f)] float decelerationTime = 0.4f;
    [Tooltip("How far below its start position the fly looks for ground when Play starts.")]
    [SerializeField] float startGroundCheck = 0.05f;
    [Tooltip("Wing beat playback speed while hovering still. 1 = the fly clip's own rate.")]
    [SerializeField] float hoverWingSpeed = 1f;
    // Kept low: the fly clip already beats 12 times a second, and much faster leaves too few rendered
    // frames per beat at 60 fps, so the flap turns into jitter.
    [Tooltip("Wing beat playback speed at full boost speed; cruising falls in between.")]
    [SerializeField] float boostWingSpeed = 1.5f;

    CharacterController controller;
    InputAction moveAction;
    InputAction upAction;
    InputAction downAction;
    InputAction freeLookAction;
    InputAction boostAction;
    Vector3 flyVelocity;
    // SmoothDamp's running state for flyVelocity.
    Vector3 flyAcceleration;
    float steerYaw;
    float steerPitch;
    bool isGrounded;

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
    }

    // Ground contact is only detected by moving into it, and a hovering fly never moves, so a fly
    // placed on the ground would otherwise start in flying mode. Probe once straight down instead.
    void Start()
    {
        controller.Move(Vector3.down * startGroundCheck);
        isGrounded = (controller.collisionFlags & CollisionFlags.Below) != 0;
        if (!isGrounded)
            controller.Move(Vector3.up * startGroundCheck);
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
    }

    // Faster flight beats the wings faster, so hovering, cruising and boosting each read differently.
    float WingBeatSpeed()
    {
        // Max guards a zero top speed from a misconfigured Inspector: 0/0 would hand the Animator NaN.
        float topSpeed = Mathf.Max(moveSpeed * boostMultiplier, StopSpeed);
        return Mathf.Lerp(hoverWingSpeed, boostWingSpeed, flyVelocity.magnitude / topSpeed);
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
        Vector3 velocity = Quaternion.Euler(0f, steerYaw, 0f) * new Vector3(move.x, 0f, move.y) * walkSpeed
                         + Vector3.down * GroundStickSpeed;
        MoveAndDetectGround(velocity);
        // Landing absorbs all flight momentum, so the next take-off starts from rest.
        flyVelocity = Vector3.zero;
        flyAcceleration = Vector3.zero;
    }

    // Z/S fly along the full steering direction (including up/down); Space/Ctrl are straight up/down.
    // Shift boosts only the Z/Q/S/D part; climbing and diving keep their speed.
    void Fly(Vector2 move, float lift)
    {
        float speed = boostAction.IsPressed() ? moveSpeed * boostMultiplier : moveSpeed;
        Vector3 targetVelocity = Quaternion.Euler(steerPitch, steerYaw, 0f) * new Vector3(move.x, 0f, move.y) * speed
                               + Vector3.up * (lift * verticalSpeed);
        flyVelocity = Accelerate(targetVelocity);
        // Hovering makes no move at all: there is no gravity, and a zero-length move reports no contact.
        if (flyVelocity == Vector3.zero)
            return;
        MoveAndDetectGround(flyVelocity);
    }

    // Eases from the current flight velocity toward the target instead of jumping to it, which is what
    // gives starts, stops and boosts their weight. Slowing down uses the longer time so it reads as a glide.
    Vector3 Accelerate(Vector3 targetVelocity)
    {
        bool isSpeedingUp = targetVelocity.sqrMagnitude >= flyVelocity.sqrMagnitude;
        float smoothTime = isSpeedingUp ? accelerationTime : decelerationTime;
        Vector3 velocity = Vector3.SmoothDamp(flyVelocity, targetVelocity, ref flyAcceleration, smoothTime);
        if (targetVelocity != Vector3.zero || velocity.sqrMagnitude > StopSpeed * StopSpeed)
            return velocity;
        flyAcceleration = Vector3.zero;
        return Vector3.zero;
    }

    void MoveAndDetectGround(Vector3 velocity)
    {
        controller.Move(velocity * Time.deltaTime);
        isGrounded = (controller.collisionFlags & CollisionFlags.Below) != 0;
    }

    // Uses ground contact after this frame's move, so taking off or walking off an edge switches
    // to Flying on the same frame. Hovering counts as Flying: in the air the wings always beat.
    // On the ground, turning in place also walks.
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
