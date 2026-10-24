using UnityEngine;
using UnityEngine.InputSystem;

// Third-person orbit camera: the mouse circles the camera around the target on a sphere (all the way
// round, and over/under it), always looking at the target. Replaces Cinemachine for the fly, whose
// over-the-shoulder Third Person Follow camera could not swing in front of or under it.
// Put this on the real Camera, with no CinemachineBrain on it: both would move the same transform.
public class OrbitCamera : MonoBehaviour
{
    // Project-wide actions (InputSystem_Actions): mouse delta and gamepad right stick.
    const string LookActionName = "Player/Look";

    [SerializeField] Transform target;
    [Tooltip("Point the camera looks at, relative to the target's position.")]
    [SerializeField] Vector3 targetOffset = Vector3.zero;
    [SerializeField] float distance = 0.4f;
    [SerializeField] float mouseSensitivity = 0.1f;
    [Tooltip("Seconds the camera takes to catch up with the mouse. 0 = no smoothing.")]
    [SerializeField, Range(0f, 0.3f)] float smoothing = 0.08f;
    [SerializeField, Range(-89f, 0f)] float minPitch = -80f;
    [SerializeField, Range(0f, 89f)] float maxPitch = 80f;
    [Tooltip("Radius of the wall check. The camera moves closer when something is between it and the target.")]
    [SerializeField] float wallCheckRadius = 0.02f;
    [SerializeField] LayerMask wallLayers = ~0;
    [Tooltip("Seconds the camera takes to ease back out after a wall stops blocking it. 0 = jump back.")]
    [SerializeField, Range(0f, 1f)] float wallReturnTime = 0.3f;

    readonly RaycastHit[] wallHits = new RaycastHit[16];
    InputAction lookAction;
    float currentDistance;
    float distanceVelocity;
    float targetYaw;
    float targetPitch;
    float yaw;
    float pitch;
    float yawVelocity;
    float pitchVelocity;

    void Awake()
    {
        // Throws if the action is missing: a camera that silently ignores the mouse is worse than a clear error.
        lookAction = InputSystem.actions.FindAction(LookActionName, throwIfNotFound: true);
        targetYaw = yaw = transform.eulerAngles.y;
        targetPitch = pitch = Mathf.Clamp(Mathf.DeltaAngle(0f, transform.eulerAngles.x), minPitch, maxPitch);
        currentDistance = distance;

        if (target == null)
        {
            // Fails closed: with nothing to orbit the camera has no position to compute.
            Debug.LogError("OrbitCamera: assign Target (the object to orbit, e.g. the fly).", this);
            enabled = false;
        }
    }

    void OnEnable()
    {
        lookAction.Enable();
        Cursor.lockState = CursorLockMode.Locked;
    }

    void OnDisable()
    {
        Cursor.lockState = CursorLockMode.None;
    }

    // LateUpdate so the target has already moved this frame; following it in Update would jitter.
    void LateUpdate()
    {
        Orbit(lookAction.ReadValue<Vector2>());

        Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
        Vector3 focus = target.position + targetOffset;
        Vector3 backward = rotation * Vector3.back;
        FollowClearDistance(ClearDistance(focus, backward));
        transform.SetPositionAndRotation(focus + backward * currentDistance, rotation);
    }

    // Pulls in instantly so a wall never sits between the camera and the target, but eases back out:
    // jumping back out made the camera pump in and out while objects slid across the line of sight.
    void FollowClearDistance(float clearDistance)
    {
        if (clearDistance < currentDistance)
        {
            currentDistance = clearDistance;
            distanceVelocity = 0f;
            return;
        }
        currentDistance = Mathf.SmoothDamp(currentDistance, clearDistance, ref distanceVelocity, wallReturnTime);
    }

    void Orbit(Vector2 look)
    {
        targetYaw += look.x * mouseSensitivity;
        // Mouse up looks up, which is a negative rotation around X and swings the camera under the target.
        targetPitch = Mathf.Clamp(targetPitch - look.y * mouseSensitivity, minPitch, maxPitch);
        yaw = Mathf.SmoothDampAngle(yaw, targetYaw, ref yawVelocity, smoothing);
        pitch = Mathf.SmoothDamp(pitch, targetPitch, ref pitchVelocity, smoothing);
    }

    // Nearest wall between the target and the camera. Colliders already overlapping at the start
    // (distance 0) and anything in the target's own hierarchy are skipped, so the target never blocks itself.
    float ClearDistance(Vector3 focus, Vector3 backward)
    {
        int hitCount = Physics.SphereCastNonAlloc(focus, wallCheckRadius, backward, wallHits, distance,
                                                  wallLayers, QueryTriggerInteraction.Ignore);
        float nearest = distance;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = wallHits[i];
            if (hit.distance > 0f && hit.distance < nearest && !hit.transform.IsChildOf(target.root))
                nearest = hit.distance;
        }
        return nearest;
    }
}
