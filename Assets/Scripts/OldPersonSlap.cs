using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// The old person's slap: Attack (left mouse) brings the right hand up at the right edge of the screen,
// then whips it through the middle and out at the bottom left. As the hand crosses the middle,
// everything in a small sphere in front of the camera is hit: spit targets (the cans and plates) rock
// and flash as if spat on, and other objects with a Rigidbody are knocked away. The camera jolts,
// harder on a hit, and a hit briefly freezes the hand (hit-stop), so the slap is felt landing.
// Put this on the old_person root next to OldPersonFPSController. The arm is posed in code on top of
// the Idle/Walk clip (the model has no slap animation), like the controller's head tilt.
// The hand's path is set in camera space, the way first-person weapons are, because what the player
// must see is the hand crossing the screen; the elbow and shoulder just follow it (two-bone IK). The
// path is drawn in the Scene view while this object is selected, for tuning.
public class OldPersonSlap : MonoBehaviour
{
    // Project-wide action (InputSystem_Actions): left mouse, gamepad west button.
    const string AttackActionName = "Player/Attack";
    // The strike follows progress^power along the path: it accelerates into the hit, as a real slap
    // does, instead of moving at one constant speed.
    const float StrikeEasePower = 2f;
    // Share of the recovery the hand keeps travelling past the follow-through before it settles.
    const float OvershootShare = 0.4f;
    // Elbow points down and out to his right (camera space), as in a natural slap.
    const float ElbowOutward = 0.5f;
    // The hand travels right-to-left, so hit objects fly forward and to the left, not straight ahead.
    const float SidewaysPush = 0.5f;
    const int GizmoSegments = 16;

    [Tooltip("The first-person camera; the slap hits where it looks. Found among the children if left empty.")]
    [SerializeField] Camera viewCamera;
    [Tooltip("The old person's Animator (Humanoid rig), used to find the right arm bones. Found automatically if left empty.")]
    [SerializeField] Animator animator;
    [Tooltip("Optional jolt on the view camera. Taken from View Camera if left empty.")]
    [SerializeField] CameraShake cameraShake;
    [Tooltip("Optional sound played where the slap lands. Leave empty for a silent slap.")]
    [SerializeField] AudioClip slapSound;
    [Tooltip("Distance from the camera to the centre of the hit sphere, in metres at the old person's own size (scales with him).")]
    [SerializeField] float reach = 0.8f;
    [Tooltip("Radius of the hit sphere, in metres at the old person's own size (scales with him).")]
    [SerializeField] float hitRadius = 0.35f;
    [Tooltip("Impulse given to what is hit. The default spit hits with about 1.")]
    [SerializeField] float slapForce = 3f;
    [SerializeField] LayerMask hitLayers = ~0;

    [Header("Timing (seconds)")]
    [SerializeField, Min(0.01f)] float windUpTime = 0.12f;
    [SerializeField, Min(0.01f)] float strikeTime = 0.08f;
    [SerializeField, Min(0.01f)] float recoverTime = 0.3f;
    [Tooltip("How long the hand freezes when the slap hits something. 0 turns hit-stop off. Adds to the cooldown on a hit.")]
    [SerializeField, Min(0f)] float hitStopTime = 0.06f;

    // Defaults fit old_person (make_old_person.py): the shoulder is ~0.24 m below and ~0.13 m behind
    // the eye and the arm is ~0.45 m to the wrist, so every point is within reach and in view.
    // Points out of reach are not an error: the arm straightens toward them.
    [Header("Hand path (wrist position, metres from the camera: x right, y up, z forward)")]
    [Tooltip("Wind-up: the hand is raised at the right edge of the screen.")]
    [SerializeField] Vector3 cockedWrist = new Vector3(0.2f, -0.02f, 0.22f);
    [Tooltip("The moment of the hit: the hand is in the middle of the screen.")]
    [SerializeField] Vector3 contactWrist = new Vector3(0.03f, -0.08f, 0.26f);
    [Tooltip("End of the strike: the hand leaves the screen at the bottom left.")]
    [SerializeField] Vector3 followThroughWrist = new Vector3(-0.15f, -0.2f, 0.18f);
    [Tooltip("Wrist bend in degrees: cocked back in the wind-up, snapping through the other way on the strike.")]
    [SerializeField] float wristSnap = 20f;
    [Tooltip("How far the hand keeps travelling after the strike while the arm drops back, as a share of the path.")]
    [SerializeField, Range(0f, 0.5f)] float followThroughOvershoot = 0.2f;

    [Header("Camera jolt (0-1)")]
    [SerializeField, Range(0f, 1f)] float hitShake = 0.5f;
    [SerializeField, Range(0f, 1f)] float missShake = 0.1f;

    // The arm at one moment of the slap: weight is how far it has left its animated pose (0-1); along
    // is the place on the hand path (0 cocked, 0.5 contact, 1 follow-through, more is overshoot);
    // wrist is the wrist bend in degrees.
    struct SlapPose
    {
        public float weight, along, wrist;
    }

    // One object can have several colliders in the sphere; it is still hit only once per slap.
    readonly HashSet<object> hitThisSlap = new HashSet<object>();
    InputAction attackAction;
    Transform upperArmBone;
    Transform lowerArmBone;
    Transform handBone;
    bool hasArm;
    // Seconds since the current slap started; negative while not slapping.
    float slapClock = -1f;
    // Seconds of hit-stop left; the slap clock stands still while this is positive.
    float hitStopClock;
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

        FindArmBones();
    }

    void OnEnable() => attackAction.Enable();

    void Update()
    {
        if (slapClock < 0f)
        {
            if (attackAction.WasPressedThisFrame())
                StartSlap();
            return;
        }
        if (hitStopClock > 0f)
        {
            hitStopClock -= Time.deltaTime;
            return;
        }

        slapClock += Time.deltaTime;
        // The hand passes the contact point halfway along the path; the eased strike gets there here.
        float contact = Mathf.Pow(0.5f, 1f / StrikeEasePower);
        if (!hasStruck && slapClock >= windUpTime + strikeTime * contact)
        {
            hasStruck = true;
            Strike();
        }
        // A new slap can only start once this one has finished, so the duration is the cooldown.
        if (slapClock >= windUpTime + strikeTime + recoverTime)
            slapClock = -1f;
    }

    // LateUpdate because the Animator poses the bones between Update and LateUpdate; posing the arm
    // in Update would be overwritten by the Idle/Walk clip the same frame. The camera is read before
    // CameraShake's LateUpdate (execution order 200) adds its jolt, so the hand does not shake with the view.
    void LateUpdate()
    {
        if (slapClock < 0f || !hasArm)
            return;
        SlapPose pose = EvaluatePose(slapClock);

        // Solved at full strength, then blended from the animated pose in local space, so the arm
        // rises out of the Idle/Walk clip and drops back into it smoothly.
        Quaternion animatedUpper = upperArmBone.localRotation;
        Quaternion animatedLower = lowerArmBone.localRotation;
        ReachArm(WristOnPath(pose.along), ElbowPole());
        upperArmBone.localRotation = Quaternion.Slerp(animatedUpper, upperArmBone.localRotation, pose.weight);
        lowerArmBone.localRotation = Quaternion.Slerp(animatedLower, lowerArmBone.localRotation, pose.weight);

        RotateBone(handBone, pose.wrist * pose.weight, viewCamera.transform.up);
    }

    SlapPose EvaluatePose(float time)
    {
        if (time < windUpTime)
            return WindUpPose(time / windUpTime);
        time -= windUpTime;
        if (time < strikeTime)
            return StrikePose(time / strikeTime);
        return RecoverPose((time - strikeTime) / recoverTime);
    }

    // Rises out of the animated pose into the cocked hand at the right edge of the screen.
    SlapPose WindUpPose(float progress)
    {
        return new SlapPose { weight = Mathf.SmoothStep(0f, 1f, progress), along = 0f, wrist = wristSnap };
    }

    SlapPose StrikePose(float progress)
    {
        return new SlapPose
        {
            weight = 1f,
            along = Mathf.Pow(progress, StrikeEasePower),
            // Eased in and out rather than with the path, so the hand trails the arm and then snaps over.
            wrist = Mathf.Lerp(wristSnap, -wristSnap, Mathf.SmoothStep(0f, 1f, progress)),
        };
    }

    // The hand carries on past the follow-through, slowing down, while the arm drops back into the
    // animated pose; stopping dead at the end of the strike would look like hitting a wall.
    SlapPose RecoverPose(float progress)
    {
        SlapPose pose = StrikePose(1f);
        pose.weight = 1f - Mathf.SmoothStep(0f, 1f, progress);
        float settle = Mathf.Clamp01(progress / OvershootShare);
        pose.along += followThroughOvershoot * (1f - (1f - settle) * (1f - settle));
        return pose;
    }

    // A curve from the cocked point to the follow-through point, bent so that it passes exactly through
    // the contact point halfway: quadratic Bezier, whose midpoint is 1/4 start + 1/2 control + 1/4 end.
    // Past 1 it carries on along the same curve, for the overshoot.
    Vector3 WristOnPath(float along)
    {
        Vector3 control = 2f * contactWrist - 0.5f * (cockedWrist + followThroughWrist);
        float rest = 1f - along;
        Vector3 local = rest * rest * cockedWrist + 2f * rest * along * control + along * along * followThroughWrist;
        return viewCamera.transform.TransformPoint(local);
    }

    Vector3 ElbowPole() => -viewCamera.transform.up + viewCamera.transform.right * ElbowOutward;

    // Two-bone IK: bends the elbow toward the pole so the wrist reaches the target. World-space
    // rotations, so the rig's bone axes do not matter.
    void ReachArm(Vector3 target, Vector3 pole)
    {
        Vector3 shoulder = upperArmBone.position;
        Vector3 toTarget = target - shoulder;
        if (toTarget.sqrMagnitude < 1e-8f)
            return;
        float upperLength = Vector3.Distance(shoulder, lowerArmBone.position);
        float lowerLength = Vector3.Distance(lowerArmBone.position, handBone.position);
        // Clamped to what the arm can span, so an out-of-reach target straightens the arm toward it.
        float distance = Mathf.Clamp(toTarget.magnitude,
            Mathf.Abs(upperLength - lowerLength) + 0.001f, upperLength + lowerLength - 0.001f);
        Vector3 axis = toTarget.normalized;

        // Law of cosines: how far along the shoulder-to-target line the elbow sits, and how far off it.
        float along = (upperLength * upperLength - lowerLength * lowerLength + distance * distance) / (2f * distance);
        float offset = Mathf.Sqrt(Mathf.Max(0f, upperLength * upperLength - along * along));
        Vector3 bend = Vector3.ProjectOnPlane(pole, axis);
        // Pole in line with the arm: keep the elbow on the side it is already on.
        if (bend.sqrMagnitude < 1e-6f)
            bend = Vector3.ProjectOnPlane(lowerArmBone.position - shoulder, axis);
        Vector3 elbow = shoulder + axis * along + bend.normalized * offset;

        AimBone(upperArmBone, lowerArmBone.position, elbow);
        // Read after the upper arm moved, so the forearm is aimed from the elbow's new position.
        AimBone(lowerArmBone, handBone.position, shoulder + axis * distance);
    }

    // Rotates a bone around its own joint so its child moves from where it is to where it is wanted.
    static void AimBone(Transform bone, Vector3 childNow, Vector3 childWanted)
    {
        bone.rotation = Quaternion.FromToRotation(childNow - bone.position, childWanted - bone.position) * bone.rotation;
    }

    // On top of the animated pose, around a world axis, so it works whatever way the bone's local axes point.
    static void RotateBone(Transform bone, float degrees, Vector3 axis)
    {
        bone.rotation = Quaternion.AngleAxis(degrees, axis) * bone.rotation;
    }

    void StartSlap()
    {
        slapClock = 0f;
        hitStopClock = 0f;
        hasStruck = false;
    }

    void Strike()
    {
        Transform view = viewCamera.transform;
        // In the camera's own space, like the hand path: the Kitchen scene scales old_person 30x, and a
        // sphere measured in world units sat inside his head, so the slap hit almost nothing.
        Vector3 center = view.TransformPoint(Vector3.forward * reach);
        float radius = hitRadius * view.lossyScale.x;
        Vector3 direction = (view.forward - view.right * SidewaysPush).normalized;

        hitThisSlap.Clear();
        Collider[] touched = Physics.OverlapSphere(center, radius, hitLayers, QueryTriggerInteraction.Ignore);
        foreach (Collider touchedCollider in touched)
        {
            // Skips the old person's own CharacterController, which the sphere overlaps when looking down.
            if (!touchedCollider.transform.IsChildOf(transform))
                HitCollider(touchedCollider, center, direction);
        }

        bool hasHit = hitThisSlap.Count > 0;
        // Only on a hit: freezing on a miss would feel like the hand struck something invisible.
        if (hasHit)
            hitStopClock = hitStopTime;
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

    void FindArmBones()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
        // GetBoneTransform only works on a Humanoid rig; old_person.fbx is imported as Humanoid.
        if (animator != null && animator.isHuman)
        {
            upperArmBone = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
            lowerArmBone = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
            handBone = animator.GetBoneTransform(HumanBodyBones.RightHand);
        }
        hasArm = upperArmBone != null && lowerArmBone != null && handBone != null;
        // Fails open: the arm pose is visual only; the slap still hits without it.
        if (!hasArm)
            Debug.LogWarning("OldPersonSlap: no Humanoid right arm (upper arm, forearm, hand) found on the Animator; the slap will hit but the arm will not move.", this);
    }

    // Draws the hand path (yellow) with its contact point, and the hit sphere (red), in the Scene view,
    // so the path can be tuned by eye and the hand seen to land where the hit is.
    void OnDrawGizmosSelected()
    {
        if (viewCamera == null)
            return;
        Gizmos.color = Color.yellow;
        Vector3 previous = WristOnPath(0f);
        for (int i = 1; i <= GizmoSegments; i++)
        {
            Vector3 next = WristOnPath(i / (float)GizmoSegments);
            Gizmos.DrawLine(previous, next);
            previous = next;
        }
        Transform view = viewCamera.transform;
        Gizmos.DrawWireSphere(WristOnPath(0.5f), 0.02f * view.lossyScale.x);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(view.TransformPoint(Vector3.forward * reach), hitRadius * view.lossyScale.x);
    }
}
