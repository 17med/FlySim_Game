using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// The old person picks things up and throws them. Interact (E) picks up the object he looks at, within
// reach, and carries it in front of him, low on the right; E again puts it down. Only marked objects
// can be picked up: a non-kinematic Rigidbody whose GameObject has a Pickable component or the tag
// "Pickable" (add the tag in Tags & Layers if you use it). With OldPersonHoldArm on the same object
// the right hand does it: it reaches out and grabs, carries the object, and swings to throw it; the
// object is grabbed when the hand arrives and flies when the swing ends. While he
// holds something, Attack (left mouse) throws it instead of slapping: OldPersonSlap asks
// ClaimsAttackButton before it slaps. Holding Aim (right mouse) while carrying zooms the view, shows
// the crosshair and draws the throw's arc up to where it will land. The throw goes toward whatever is
// under the screen centre, aimed or not.
// Put this on the old_person root next to OldPersonFPSController and OldPersonSlap.
// Distances are in the camera's own space, like OldPersonSlap's: the Kitchen scene scales old_person
// 30x, and lengths in world units would be far too short there.
public class OldPersonPickup : MonoBehaviour
{
    // Project-wide actions (InputSystem_Actions): E / gamepad north, left mouse / gamepad west,
    // right mouse / gamepad left trigger.
    const string InteractActionName = "Player/Interact";
    const string AttackActionName = "Player/Attack";
    const string AimActionName = "Player/Aim";
    const string PickableTag = "Pickable";
    // Hits past this many in one cast are ignored; a cast from the eye rarely touches more.
    const int RaycastBufferSize = 16;

    [Tooltip("The first-person camera; he picks up and throws where it looks. Found among the children if left empty.")]
    [SerializeField] Camera viewCamera;
    [SerializeField] LayerMask hitLayers = ~0;
    [Tooltip("Optional. Poses the right arm to grab, carry and throw. Found on this object if left empty; without it objects go straight to the carry spot.")]
    [SerializeField] OldPersonHoldArm holdArm;

    [Header("Pick up (metres at the old person's own size; they scale with him)")]
    [SerializeField] float reach = 1.2f;
    [Tooltip("Thickness of the grab cast, so small objects do not need pixel-perfect aim.")]
    [SerializeField] float grabRadius = 0.1f;
    [Tooltip("Heavier Rigidbodies cannot be picked up.")]
    [SerializeField, Min(0f)] float maxMass = 20f;

    [Header("Carry")]
    // Close enough that the hand (shoulder ~0.24 m below and ~0.13 m behind the eye, arm ~0.45 m)
    // reaches the object's surface; further out, the arm straightens and the hand stops short.
    [Tooltip("Where the held object sits, metres from the camera: x right, y up, z forward. Big objects sit further out.")]
    [SerializeField] Vector3 holdOffset = new Vector3(0.2f, -0.12f, 0.35f);
    [Tooltip("How quickly the object catches up with the hold point. Higher = stiffer.")]
    [SerializeField, Min(0.1f)] float followSpeed = 15f;
    [Tooltip("If the object gets this far from the hold point (stuck behind a wall), he lets go. Metres at his own size.")]
    [SerializeField, Min(0.01f)] float dropDistance = 1f;

    [Header("Throw")]
    [Tooltip("Launch speed in m/s at the old person's own size, the same for every mass.")]
    [SerializeField, Min(0f)] float throwSpeed = 8f;
    [Tooltip("Seconds after letting go before the object can bump into the old person again, so it does not hit him on the way out.")]
    [SerializeField, Min(0f)] float ownCollisionDelay = 0.3f;

    [Header("Aim (hold Aim while carrying)")]
    [Tooltip("The field of view is multiplied by this while aiming. 0.75 turns 60° into 45°.")]
    [SerializeField, Range(0.3f, 1f)] float aimFovMultiplier = 0.75f;
    [Tooltip("Seconds to zoom in and out. 0 = instant.")]
    [SerializeField, Range(0f, 0.5f)] float blendTime = 0.12f;
    [Tooltip("Your crosshair object (e.g. a UI Image centred on a Canvas), shown only while aiming. Must not be this object or one of its parents.")]
    [SerializeField] GameObject aimImage;
    [Tooltip("Optional line for the throw arc. Left empty, one is created on Play with Arc Color and Arc Width.")]
    [SerializeField] LineRenderer trajectoryLine;
    [SerializeField] Color arcColor = new Color(1f, 1f, 1f, 0.8f);
    [Tooltip("Arc width in metres at the old person's own size.")]
    [SerializeField, Min(0.001f)] float arcWidth = 0.015f;
    [Tooltip("How many seconds of flight the arc shows at most. It stops earlier where the throw lands.")]
    [SerializeField, Range(0.2f, 5f)] float maxArcTime = 2f;
    [Tooltip("How far the aim looks for what is under the crosshair, in metres at his own size.")]
    [SerializeField, Min(1f)] float aimRange = 50f;

    readonly RaycastHit[] raycastBuffer = new RaycastHit[RaycastBufferSize];
    readonly List<Vector3> arcPoints = new List<Vector3>();
    InputAction interactAction;
    InputAction attackAction;
    InputAction aimAction;
    CharacterController ownCollider;
    Material createdArcMaterial;
    float baseFov;
    // 0 = not aiming, 1 = fully zoomed; moves linearly over Blend Time and is eased when applied.
    float aimWeight;

    // Kept apart from heldBody != null: a held object destroyed by something else makes heldBody
    // compare equal to null, and this flag is how Update notices and cleans up.
    bool isHolding;
    Rigidbody heldBody;
    Collider[] heldColliders;
    bool heldUsedGravity;
    RigidbodyInterpolation heldInterpolation;
    // How much further out than Hold Offset (camera space) the object sits, so a big one does not fill the view.
    float heldExtraDistance;
    int throwFrame = -1;
    // The hand is on its way to this object; it is picked up when the hand arrives.
    bool isReaching;
    Rigidbody reachingBody;
    // The throw swing is playing; the object leaves the hand when it ends.
    bool isThrowing;
    // The last released object's colliders, and when they may collide with the old person again.
    Collider[] releasedColliders;
    float collisionRestoreTime;

    // True while he reaches for or holds something, and on the frame he throws it, so the throwing
    // click does not also start a slap whichever of the two scripts runs first that frame, and no slap
    // fights the grab for the arm.
    public bool ClaimsAttackButton => isHolding || isReaching || throwFrame == Time.frameCount;

    bool UsesHand => holdArm != null && holdArm.HasArm;

    float Scale => viewCamera.transform.lossyScale.x;

    void Awake()
    {
        // Throws if an action is missing: a button that silently does nothing is worse than a clear error.
        interactAction = InputSystem.actions.FindAction(InteractActionName, throwIfNotFound: true);
        attackAction = InputSystem.actions.FindAction(AttackActionName, throwIfNotFound: true);
        aimAction = InputSystem.actions.FindAction(AimActionName, throwIfNotFound: true);

        if (viewCamera == null)
            viewCamera = GetComponentInChildren<Camera>();
        if (viewCamera == null)
        {
            // Fails closed: picking up and throwing aim where the camera looks, so without one there is nothing to do.
            Debug.LogError("OldPersonPickup: no camera. Assign View Camera (the first-person camera).", this);
            enabled = false;
            return;
        }
        baseFov = viewCamera.fieldOfView;
        // The CharacterController's sweep would bump into the carried object and stop him walking forward.
        ownCollider = GetComponent<CharacterController>();
        if (holdArm == null)
            holdArm = GetComponent<OldPersonHoldArm>();

        // Fails open: hiding this object would switch this script off with it, and nothing could show
        // it again, so it is dropped and aiming carries on without a crosshair.
        if (aimImage != null && transform.IsChildOf(aimImage.transform))
        {
            Debug.LogError("OldPersonPickup: Aim Image is this object or one of its parents; hiding it would turn this script off. Use a separate object, e.g. an Image on a Canvas.", this);
            aimImage = null;
        }
        if (aimImage != null)
            aimImage.SetActive(false);
        if (trajectoryLine == null)
            trajectoryLine = CreateTrajectoryLine();
        if (trajectoryLine != null)
            trajectoryLine.enabled = false;
    }

    void OnEnable()
    {
        interactAction.Enable();
        attackAction.Enable();
        aimAction.Enable();
    }

    // Swapping to the fly turns the old person off: he lets go, and the zoom and crosshair go away,
    // instead of leaving an object floating and the view stuck zoomed in.
    void OnDisable()
    {
        if (viewCamera == null)
            return;
        CancelReach();
        Release();
        RestoreOwnCollisions();
        aimWeight = 0f;
        viewCamera.fieldOfView = baseFov;
        HideAim();
    }

    void OnDestroy()
    {
        if (createdArcMaterial != null)
            Destroy(createdArcMaterial);
    }

    void Update()
    {
        if (releasedColliders != null && Time.time >= collisionRestoreTime)
            RestoreOwnCollisions();
        // Destroyed, or parked as kinematic (a SpitTarget breaking): it can no longer be carried.
        if (isHolding && (heldBody == null || heldBody.isKinematic))
            Release();
        if (isReaching)
            UpdateReach();
        if (isThrowing && holdArm.ConsumeRelease())
            Throw();

        // WasPressedThisFrame, not WasPerformedThisFrame: Interact has a Hold interaction in the
        // asset, and pressed reacts to the key going down, so a tap on E is enough.
        if (interactAction.WasPressedThisFrame())
            OnInteractPressed();
        else if (isHolding && !isThrowing && attackAction.WasPressedThisFrame())
            StartThrow();
        UpdateAim(isHolding && aimAction.IsPressed());
    }

    // E puts down what he holds, cancels a reach, or starts one. Ignored mid-throw: the swing is committed.
    void OnInteractPressed()
    {
        if (isThrowing)
            return;
        if (isHolding)
            Release();
        else if (isReaching)
            CancelReach();
        else
            TryPickUp();
    }

    // The object is grabbed only once the hand reaches it; until then it is left to physics.
    void UpdateReach()
    {
        // Destroyed, or parked as kinematic (a SpitTarget breaking) on the way.
        if (reachingBody == null || reachingBody.isKinematic)
        {
            CancelReach();
            return;
        }
        if (!holdArm.ReachFinished)
            return;
        Rigidbody body = reachingBody;
        isReaching = false;
        reachingBody = null;
        PickUp(body);
        holdArm.Carry();
    }

    void CancelReach()
    {
        if (!isReaching)
            return;
        isReaching = false;
        reachingBody = null;
        holdArm.Release();
    }

    void StartThrow()
    {
        if (!UsesHand)
        {
            Throw();
            return;
        }
        isThrowing = true;
        holdArm.Throw();
    }

    // The object is moved through its velocity rather than its position, so it still collides with
    // the world while carried instead of passing through walls.
    void FixedUpdate()
    {
        if (!isHolding || heldBody == null)
            return;
        // During the throw swing the hand leads and the object keeps up with it exactly (one physics
        // step); otherwise the hand follows the object, which eases toward the hold point.
        bool followsHand = isThrowing && holdArm.IsLeading;
        Vector3 goal = followsHand ? holdArm.LeadingObjectCenter : HoldPoint();
        Vector3 toGoal = goal - heldBody.worldCenterOfMass;
        if (toGoal.magnitude > dropDistance * Scale)
        {
            Release();
            return;
        }
        heldBody.linearVelocity = followsHand ? toGoal / Time.fixedDeltaTime : toGoal * followSpeed;
        heldBody.angularVelocity = Vector3.zero;
    }

    void TryPickUp()
    {
        Transform view = viewCamera.transform;
        // The first thing in the way decides: a wall in front of a cup means no cup.
        if (!TryFindHit(view.position, view.forward, reach * Scale, grabRadius * Scale, out RaycastHit hit))
            return;
        Rigidbody body = hit.rigidbody;
        if (body == null || body.isKinematic || body.mass > maxMass || !IsMarkedPickable(body.gameObject))
            return;
        if (!UsesHand)
        {
            PickUp(body);
            return;
        }
        isReaching = true;
        reachingBody = body;
        holdArm.Reach(body, LargestExtent(body.GetComponentsInChildren<Collider>()));
    }

    // Checked on the Rigidbody's own GameObject, not its parents: a Pickable tray must not make every
    // cup standing on it pickable too.
    static bool IsMarkedPickable(GameObject candidate)
    {
        // A string compare, not CompareTag: CompareTag logs an error on every press while the
        // "Pickable" tag is not yet defined in Tags & Layers, and the Pickable component alone must
        // work without that tag.
        return candidate.GetComponent<Pickable>() != null || candidate.tag == PickableTag;
    }

    void PickUp(Rigidbody body)
    {
        // Settled first, so a pending restore cannot switch collisions back on while this one is held.
        RestoreOwnCollisions();
        isHolding = true;
        heldBody = body;
        heldColliders = body.GetComponentsInChildren<Collider>();
        heldUsedGravity = body.useGravity;
        heldInterpolation = body.interpolation;
        body.useGravity = false;
        // The camera moves every rendered frame; without interpolation the object would jitter against it.
        body.interpolation = RigidbodyInterpolation.Interpolate;
        heldExtraDistance = LargestExtent(heldColliders) / Scale;
        SetCollidesWithOwnBody(heldColliders, false);
    }

    // Lets go where the object is, keeping the motion it has; Throw sets a launch speed on top.
    void Release()
    {
        if (!isHolding)
            return;
        isThrowing = false;
        // Drops the arm back into the animated pose; at the end of a throw it is already doing so.
        if (UsesHand)
            holdArm.Release();
        if (heldBody != null)
        {
            heldBody.useGravity = heldUsedGravity;
            heldBody.interpolation = heldInterpolation;
        }
        // Collisions with him come back after a delay, not at once: an object still touching him
        // would be shoved out of his capsule.
        releasedColliders = heldColliders;
        collisionRestoreTime = Time.time + ownCollisionDelay;
        isHolding = false;
        heldBody = null;
        heldColliders = null;
    }

    void Throw()
    {
        Rigidbody body = heldBody;
        Vector3 launchVelocity = ThrowVelocity();
        Release();
        throwFrame = Time.frameCount;
        body.linearVelocity = launchVelocity;
    }

    Vector3 ThrowVelocity()
    {
        Vector3 start = heldBody.worldCenterOfMass;
        Vector3 direction = (AimTarget() - start).normalized;
        // A target closer than the hold point (a wall right in front) would send it backwards.
        if (Vector3.Dot(direction, viewCamera.transform.forward) <= 0f)
            direction = viewCamera.transform.forward;
        // Range is speed squared over gravity, so speed x sqrt(scale x gravity multiplier) keeps the arc in
        // proportion to his size: 30x bigger in the Kitchen, he throws 30x further, whether the object
        // falls with normal gravity or with ScaledGravity's stronger one.
        return direction * (throwSpeed * Mathf.Sqrt(Scale * ScaledGravity.MultiplierOf(heldBody)));
    }

    // What is under the screen centre, or a far point straight ahead when nothing is.
    Vector3 AimTarget()
    {
        Transform view = viewCamera.transform;
        float range = aimRange * Scale;
        if (TryFindHit(view.position, view.forward, range, 0f, out RaycastHit hit))
            return hit.point;
        return view.position + view.forward * range;
    }

    Vector3 HoldPoint()
    {
        return viewCamera.transform.TransformPoint(holdOffset + Vector3.forward * heldExtraDistance);
    }

    void UpdateAim(bool isAiming)
    {
        float previousWeight = aimWeight;
        float step = blendTime > 0f ? Time.deltaTime / blendTime : 1f;
        aimWeight = Mathf.MoveTowards(aimWeight, isAiming ? 1f : 0f, step);
        // Written only while it changes, so the field of view is left alone the rest of the time.
        if (aimWeight != previousWeight)
            viewCamera.fieldOfView = baseFov * Mathf.Lerp(1f, aimFovMultiplier, Mathf.SmoothStep(0f, 1f, aimWeight));

        if (!isAiming)
        {
            HideAim();
            return;
        }
        if (aimImage != null)
            aimImage.SetActive(true);
        DrawArc();
    }

    void HideAim()
    {
        if (aimImage != null)
            aimImage.SetActive(false);
        if (trajectoryLine != null)
            trajectoryLine.enabled = false;
    }

    // Steps the throw the way the physics engine will (same time step, gravity and damping) and stops
    // at the first thing it would hit, so the arc ends where the object lands.
    void DrawArc()
    {
        if (trajectoryLine == null)
            return;
        Vector3 position = heldBody.worldCenterOfMass;
        Vector3 velocity = ThrowVelocity();
        // Includes ScaledGravity's extra pull, so the arc ends where the object really lands.
        Vector3 gravity = heldUsedGravity ? Physics.gravity * ScaledGravity.MultiplierOf(heldBody) : Vector3.zero;
        float damping = heldBody.linearDamping;
        float step = Time.fixedDeltaTime;
        int stepCount = Mathf.CeilToInt(maxArcTime / step);

        arcPoints.Clear();
        arcPoints.Add(position);
        for (int i = 0; i < stepCount; i++)
        {
            velocity += gravity * step;
            velocity *= Mathf.Clamp01(1f - damping * step);
            Vector3 move = velocity * step;
            float length = move.magnitude;
            if (length < 1e-6f)
                break;
            if (TryFindHit(position, move / length, length, 0f, out RaycastHit hit))
            {
                arcPoints.Add(hit.point);
                break;
            }
            position += move;
            arcPoints.Add(position);
        }

        trajectoryLine.widthMultiplier = arcWidth * Scale;
        trajectoryLine.positionCount = arcPoints.Count;
        for (int i = 0; i < arcPoints.Count; i++)
            trajectoryLine.SetPosition(i, arcPoints[i]);
        trajectoryLine.enabled = true;
    }

    // The closest hit along the cast that is neither the old person himself (the camera starts inside
    // his CharacterController) nor the object he is holding. A radius of 0 is a plain ray.
    bool TryFindHit(Vector3 origin, Vector3 direction, float distance, float radius, out RaycastHit closest)
    {
        int count = radius > 0f
            ? Physics.SphereCastNonAlloc(origin, radius, direction, raycastBuffer, distance, hitLayers, QueryTriggerInteraction.Ignore)
            : Physics.RaycastNonAlloc(origin, direction, raycastBuffer, distance, hitLayers, QueryTriggerInteraction.Ignore);
        closest = default;
        bool hasHit = false;
        for (int i = 0; i < count; i++)
        {
            RaycastHit hit = raycastBuffer[i];
            if (IsOwnOrHeld(hit.collider) || (hasHit && hit.distance >= closest.distance))
                continue;
            closest = hit;
            hasHit = true;
        }
        return hasHit;
    }

    bool IsOwnOrHeld(Collider touched)
    {
        return touched.transform.IsChildOf(transform) || (isHolding && touched.attachedRigidbody == heldBody);
    }

    void RestoreOwnCollisions()
    {
        if (releasedColliders == null)
            return;
        SetCollidesWithOwnBody(releasedColliders, true);
        releasedColliders = null;
    }

    void SetCollidesWithOwnBody(Collider[] colliders, bool collides)
    {
        if (ownCollider == null)
            return;
        foreach (Collider other in colliders)
        {
            // Skips colliders destroyed since the object was picked up.
            if (other != null)
                Physics.IgnoreCollision(ownCollider, other, !collides);
        }
    }

    static float LargestExtent(Collider[] colliders)
    {
        float largest = 0f;
        foreach (Collider other in colliders)
        {
            Vector3 extents = other.bounds.extents;
            largest = Mathf.Max(largest, extents.x, extents.y, extents.z);
        }
        return largest;
    }

    LineRenderer CreateTrajectoryLine()
    {
        // Sprites/Default is one of Unity's always-included shaders, so it exists in builds too, and
        // URP draws it as an unlit pass.
        Shader shader = Shader.Find("Sprites/Default");
        if (shader == null)
        {
            // Fails open: the arc is a visual aid only; aiming and throwing still work without it.
            Debug.LogWarning("OldPersonPickup: shader Sprites/Default not found; no throw arc. Assign Trajectory Line to show one.", this);
            return null;
        }
        GameObject lineObject = new GameObject("Throw Arc");
        lineObject.transform.SetParent(transform, false);
        LineRenderer line = lineObject.AddComponent<LineRenderer>();
        createdArcMaterial = new Material(shader);
        line.sharedMaterial = createdArcMaterial;
        line.useWorldSpace = true;
        line.startColor = arcColor;
        line.endColor = arcColor;
        line.numCapVertices = 2;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        return line;
    }
}
