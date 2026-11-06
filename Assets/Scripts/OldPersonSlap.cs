using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// The old person's slap: Attack (left mouse) swings the right arm out to his right and whips it
// across in front of him. Halfway through the whip, everything in a small sphere in front of the
// camera is hit: spit targets (the cans and plates) rock and flash as if spat on, and other objects
// with a Rigidbody are knocked away. The camera jolts, harder on a hit, so the slap is felt landing.
// The swing tilts with the camera, so looking up or down slaps up or down, where the hit sphere is.
// Put this on the old_person root next to OldPersonFPSController. The arm is posed in code on top of
// the Idle/Walk clip (the model has no slap animation), the same way the controller tilts the head.
public class OldPersonSlap : MonoBehaviour
{
    // Project-wide action (InputSystem_Actions): left mouse, gamepad west button.
    const string AttackActionName = "Player/Attack";
    // Swing in degrees around the camera's up axis, through the shoulder (+ = out to his right).
    // Symmetric around 0, so the hand crosses the middle of the screen exactly halfway through the
    // whip, the moment Strike runs: the player sees the hand land where the hit is.
    const float WindUpSweep = 55f;
    const float FollowThroughSweep = -55f;
    // The hand travels right-to-left, so hit objects fly forward and to the left, not straight ahead.
    const float SidewaysPush = 0.5f;

    [Tooltip("The first-person camera; the slap hits where it looks. Found among the children if left empty.")]
    [SerializeField] Camera viewCamera;
    [Tooltip("The old person's Animator (Humanoid rig), used to find the right arm bone. Found automatically if left empty.")]
    [SerializeField] Animator animator;
    [Tooltip("Optional jolt on the view camera. Taken from View Camera if left empty.")]
    [SerializeField] CameraShake cameraShake;
    [Tooltip("Optional sound played where the slap lands. Leave empty for a silent slap.")]
    [SerializeField] AudioClip slapSound;
    [Tooltip("Distance from the camera to the centre of the hit sphere.")]
    [SerializeField] float reach = 0.8f;
    [SerializeField] float hitRadius = 0.35f;
    [Tooltip("Impulse given to what is hit. The default spit hits with about 1.")]
    [SerializeField] float slapForce = 3f;
    [SerializeField] LayerMask hitLayers = ~0;

    [Header("Timing (seconds)")]
    [SerializeField, Min(0.01f)] float windUpTime = 0.12f;
    [SerializeField, Min(0.01f)] float strikeTime = 0.08f;
    [SerializeField, Min(0.01f)] float recoverTime = 0.3f;

    [Header("Camera jolt (0-1)")]
    [SerializeField, Range(0f, 1f)] float hitShake = 0.5f;
    [SerializeField, Range(0f, 1f)] float missShake = 0.1f;

    // One object can have several colliders in the sphere; it is still hit only once per slap.
    readonly HashSet<object> hitThisSlap = new HashSet<object>();
    InputAction attackAction;
    Transform upperArmBone;
    Transform handBone;
    // Seconds since the current slap started; negative while not slapping.
    float slapClock = -1f;
    bool hasStruck;

    void Awake()
    {
        // Throws if the action is missing: a slap button that silently does nothing is worse than a clear error.
        attackAction = InputSystem.actions.FindAction(AttackActionName, throwIfNotFound: true);

        if (viewCamera == null)
            viewCamera = GetComponentInChildren<Camera>();
        if (viewCamera == null)
        {
            // Fails closed: the slap aims where the camera looks, so without one there is nothing to hit.
            Debug.LogError("OldPersonSlap: no camera. Assign View Camera (the first-person camera).", this);
            enabled = false;
            return;
        }

        if (cameraShake == null)
            cameraShake = viewCamera.GetComponent<CameraShake>();
        // Fails open: the jolt is feedback only; the slap still hits without it.
        if (cameraShake == null)
            Debug.LogWarning("OldPersonSlap: no CameraShake on the view camera; slaps will not jolt the view.", this);

        FindArmBone();
    }

    void OnEnable()
    {
        attackAction.Enable();
    }

    void Update()
    {
        if (slapClock < 0f)
        {
            if (attackAction.WasPressedThisFrame())
                StartSlap();
            return;
        }

        slapClock += Time.deltaTime;
        // Halfway through the whip is when the hand is in front of the face.
        if (!hasStruck && slapClock >= windUpTime + strikeTime * 0.5f)
        {
            hasStruck = true;
            Strike();
        }
        // A new slap can only start once this one has finished, so the duration is the cooldown.
        if (slapClock >= windUpTime + strikeTime + recoverTime)
            slapClock = -1f;
    }

    // LateUpdate because the Animator poses the bones between Update and LateUpdate; posing the arm
    // in Update would be overwritten by the Idle/Walk clip the same frame.
    void LateUpdate()
    {
        if (slapClock < 0f || upperArmBone == null || handBone == null)
            return;
        SlapPose(slapClock, out float weight, out float sweep);

        // Aims the shoulder-to-hand line, not the upper arm's own axis: the shoulder sits below and to
        // the right of the eyes, so an arm merely parallel to the view passes the lower-right of the
        // screen. Pointing the hand at the screen centre makes the slap land where the player looks,
        // whatever the elbow bend in the animated pose. World-space rotations, so the rig's bone axes
        // do not matter. The camera is read before CameraShake's LateUpdate (execution order 200)
        // adds its jolt, so the arm does not shake with the view.
        Vector3 shoulder = upperArmBone.position;
        Vector3 currentArm = handBone.position - shoulder;
        Vector3 towardCentre = ScreenCentreTarget(shoulder, currentArm.magnitude) - shoulder;
        Vector3 swungArm = Quaternion.AngleAxis(sweep, viewCamera.transform.up) * towardCentre;
        Quaternion fullSwing = Quaternion.FromToRotation(currentArm, swungArm);
        // Weighted, so the arm rises out of its animated pose and drops back into it smoothly.
        upperArmBone.rotation = Quaternion.Slerp(Quaternion.identity, fullSwing, weight) * upperArmBone.rotation;
    }

    // The point on the line through the middle of the screen that the hand can just reach at full
    // stretch, so the hand ends up dead centre in the player's view rather than short of it or past it.
    Vector3 ScreenCentreTarget(Vector3 shoulder, float armLength)
    {
        Transform view = viewCamera.transform;
        Vector3 toShoulder = shoulder - view.position;
        float along = Vector3.Dot(view.forward, toShoulder);
        float discriminant = along * along - (toShoulder.sqrMagnitude - armLength * armLength);
        // No reachable point (only if the camera is placed far from the shoulder): aim at the closest
        // point on the line instead, which still lines the hand up with the screen centre.
        float distance = discriminant > 0f ? along + Mathf.Sqrt(discriminant) : along;
        return view.position + view.forward * distance;
    }

    void StartSlap()
    {
        slapClock = 0f;
        hasStruck = false;
    }

    // The arm at a moment of the slap: weight is how far it has left its animated pose (0-1), sweep its
    // swing angle. Wind up out to the right, whip across the screen centre, lower back down.
    void SlapPose(float time, out float weight, out float sweep)
    {
        if (time < windUpTime)
        {
            weight = Mathf.SmoothStep(0f, 1f, time / windUpTime);
            sweep = WindUpSweep;
            return;
        }
        time -= windUpTime;
        if (time < strikeTime)
        {
            // Linear, not eased: the whip should be fast all the way through.
            weight = 1f;
            sweep = Mathf.Lerp(WindUpSweep, FollowThroughSweep, time / strikeTime);
            return;
        }
        time -= strikeTime;
        weight = 1f - Mathf.SmoothStep(0f, 1f, time / recoverTime);
        sweep = FollowThroughSweep;
    }

    void Strike()
    {
        Transform view = viewCamera.transform;
        Vector3 center = view.position + view.forward * reach;
        Vector3 direction = (view.forward - view.right * SidewaysPush).normalized;

        hitThisSlap.Clear();
        Collider[] touched = Physics.OverlapSphere(center, hitRadius, hitLayers, QueryTriggerInteraction.Ignore);
        foreach (Collider touchedCollider in touched)
        {
            // Skips the old person's own CharacterController, which the sphere overlaps when looking down.
            if (!touchedCollider.transform.IsChildOf(transform))
                HitCollider(touchedCollider, center, direction);
        }

        bool hasHit = hitThisSlap.Count > 0;
        if (cameraShake != null)
            cameraShake.Shake(hasHit ? hitShake : missShake);
        if (hasHit && slapSound != null)
            AudioSource.PlayClipAtPoint(slapSound, center);
    }

    void HitCollider(Collider touchedCollider, Vector3 center, Vector3 direction)
    {
        // Same lookup as SpitProjectile: a target's collider is often on a child of the target.
        // ISpitHittable is the project's "something that reacts to a hit" contract; a slap reuses it
        // so the cans and plates react without a second, slap-only copy of SpitTarget.
        ISpitHittable target = touchedCollider.GetComponentInParent<ISpitHittable>();
        if (target != null)
        {
            if (hitThisSlap.Add(target))
                target.OnSpitHit(new SpitHit
                {
                    point = ContactPoint(touchedCollider, center),
                    normal = -direction,
                    direction = direction,
                    force = slapForce,
                });
            return;
        }

        // Plain physics objects. A target with a Rigidbody is pushed by its own OnSpitHit above instead.
        Rigidbody body = touchedCollider.attachedRigidbody;
        if (body == null || body.isKinematic || !hitThisSlap.Add(body))
            return;
        body.AddForceAtPosition(direction * slapForce, ContactPoint(touchedCollider, center), ForceMode.Impulse);
    }

    // ClosestPoint only works on primitive and convex mesh colliders; the kitchen's non-convex meshes
    // would log a warning, so those take the sphere's centre instead.
    static Vector3 ContactPoint(Collider touchedCollider, Vector3 center)
    {
        if (touchedCollider is MeshCollider mesh && !mesh.convex)
            return center;
        return touchedCollider.ClosestPoint(center);
    }

    void FindArmBone()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
        // GetBoneTransform only works on a Humanoid rig; old_person.fbx is imported as Humanoid.
        if (animator != null && animator.isHuman)
        {
            upperArmBone = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            handBone = animator.GetBoneTransform(HumanBodyBones.RightHand);
        }
        // Fails open: the arm swing is visual only; the slap still hits without it.
        if (upperArmBone == null || handBone == null)
            Debug.LogWarning("OldPersonSlap: no Humanoid right arm found on the Animator; the slap will hit but the arm will not move.", this);
    }
}
