using UnityEngine;
using UnityEngine.InputSystem;

// Third-person orbit camera: the mouse circles it around the target on a full sphere, always looking
// at the target. Goes on the Camera itself, with no CinemachineBrain, which would fight it.
// It also owns that Camera's near clip plane, because the wall check is sized from it (WallCheckRadius).
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
    [Tooltip("Smallest radius of the wall check. The check normally sizes itself to the camera's view (Near Clip Plane and field of view); this is only used if it is bigger.")]
    [SerializeField] float wallCheckRadius = 0.02f;
    // 0.05, not Unity's default 0.3: the fly is 0.4 across, and the wall check has to be as big as
    // the near plane's corners, which at 0.3 is about 0.5 units, too big to fit the kitchen's gaps.
    [Tooltip("Closest distance the camera draws, applied to the Camera when Play starts. Smaller lets the camera get closer to walls without cutting into them.")]
    [SerializeField, Min(0.001f)] float nearClipPlane = 0.05f;
    [SerializeField] LayerMask wallLayers = ~0;
    [Tooltip("Seconds the camera takes to ease back out after a wall stops blocking it. 0 = jump back.")]
    [SerializeField, Range(0f, 1f)] float wallReturnTime = 0.3f;

    // Over-the-shoulder framing, written every frame by FlyAiming; the defaults are the plain orbit.
    // Shoulder Offset is in the camera's own axes and moves the point it orbits, so the fly sits off
    // to one side of the screen centre while aiming.
    public Vector3 ShoulderOffset { get; set; } = Vector3.zero;
    // Multiplies Distance; below 1 pulls the camera in closer behind the shoulder.
    public float DistanceScale { get; set; } = 1f;

    readonly RaycastHit[] wallHits = new RaycastHit[16];
    Camera viewCamera;
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

        viewCamera = GetComponent<Camera>();
        if (viewCamera != null)
            viewCamera.nearClipPlane = nearClipPlane;
        // Fails open: the camera still orbits and avoids walls with Wall Check Radius alone; it just
        // cannot size the check to a view it does not have.
        else
            Debug.LogWarning("OrbitCamera: no Camera on this object; the wall check uses Wall Check Radius only.", this);

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
        // Per frame: the field of view changes with flight speed and aiming, and the view's size with it.
        float checkRadius = WallCheckRadius();
        Vector3 pivot = ShoulderPivot(target.position + targetOffset, rotation, checkRadius);
        Vector3 backward = rotation * Vector3.back;
        FollowClearDistance(ClearDistance(pivot, backward, distance * DistanceScale, checkRadius));
        transform.SetPositionAndRotation(pivot + backward * currentDistance, rotation);
    }

    // The sphere has to hold the camera's whole near plane, whose farthest point is a corner: the
    // camera draws nothing closer than that plane, so a wall inside its reach shows through even
    // without touching the camera.
    float WallCheckRadius()
    {
        if (viewCamera == null || viewCamera.orthographic)
            return wallCheckRadius;
        float near = viewCamera.nearClipPlane;
        float halfHeight = near * Mathf.Tan(viewCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
        float halfWidth = halfHeight * viewCamera.aspect;
        float cornerDistance = Mathf.Sqrt(near * near + halfHeight * halfHeight + halfWidth * halfWidth);
        return Mathf.Max(wallCheckRadius, cornerDistance);
    }

    // The focus moved sideways by Shoulder Offset, but stopped short of any wall in the way, so
    // aiming next to a wall never pushes the camera into it.
    Vector3 ShoulderPivot(Vector3 focus, Quaternion rotation, float checkRadius)
    {
        Vector3 shoulder = rotation * ShoulderOffset;
        float shoulderLength = shoulder.magnitude;
        if (shoulderLength <= 0f)
            return focus;
        Vector3 shoulderDirection = shoulder / shoulderLength;
        return focus + shoulderDirection * ClearDistance(focus, shoulderDirection, shoulderLength, checkRadius);
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

    // Nearest wall within maxDistance along direction. Colliders already overlapping at the start and
    // anything under the target are skipped, so the target never blocks itself - which is why the check
    // radius must stay smaller than the gap the fly's own collider keeps from walls.
    float ClearDistance(Vector3 origin, Vector3 direction, float maxDistance, float checkRadius)
    {
        int hitCount = Physics.SphereCastNonAlloc(origin, checkRadius, direction, wallHits, maxDistance,
                                                  wallLayers, QueryTriggerInteraction.Ignore);
        float nearest = maxDistance;
        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = wallHits[i];
            if (hit.distance > 0f && hit.distance < nearest && !hit.transform.IsChildOf(target.root))
                nearest = hit.distance;
        }
        return nearest;
    }
}
