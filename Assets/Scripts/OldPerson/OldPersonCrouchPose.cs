using UnityEngine;

// Bends the old man down into a crouch on top of whatever Idle/Walk is playing: thighs forward, knees
// back, feet kept flat, hips lowered so the feet stay on the floor, and the back leaning a little over
// the knees. There is no crouch clip for this rig, so the pose is made here, in code.
// How far down he is comes from the Animator's "Crouch" float (0 standing, 1 fully down). The owner's
// OldPersonFPSController writes it from the crouch key, and the prefab's NetworkAnimator carries it to
// every other player's copy, which is why this runs on every copy and is not one of OwnerOnlyControls'
// switched-off components: the flies have to see him duck.
// Goes on the old_person root, next to the Animator's object or above it.
// Runs before OldPersonFPSController's head tilt, OldPersonSlap and OldPersonHoldArm (order 0 and 50):
// those aim the head and the hands at points in the world, so the body has to be lowered first or the
// slap would land a crouch's height above where the player aimed it.
[DefaultExecutionOrder(-50)]
public class OldPersonCrouchPose : MonoBehaviour
{
    public static readonly int CrouchAnimParameter = Animator.StringToHash("Crouch");

    [Tooltip("The old person's Animator (Humanoid rig, with the float parameter \"Crouch\"). Found automatically if left empty.")]
    [SerializeField] Animator animator;
    [Tooltip("Degrees the thighs swing forward when fully crouched.")]
    [SerializeField, Range(0f, 120f)] float thighSwing = 70f;
    [Tooltip("Degrees the knees bend when fully crouched, measured between thigh and shin. More than Thigh Swing, or the shins point forward.")]
    [SerializeField, Range(0f, 160f)] float kneeBend = 110f;
    [Tooltip("Degrees the back leans forward when fully crouched, so his weight sits over his knees instead of falling backwards.")]
    [SerializeField, Range(0f, 60f)] float spineLean = 25f;

    // How far the hips were lowered this frame, in world units. OldPersonFPSController moves the
    // camera down by the same amount, so the view drops exactly as far as the body does.
    public float HipDrop { get; private set; }

    Transform hips;
    Transform spine;
    Transform leftThigh, leftShin, leftFoot;
    Transform rightThigh, rightShin, rightFoot;

    void Awake()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
        // Fails open: without the bones he cannot bend, but he still walks, slaps and crouches his view.
        if (animator == null || !animator.isHuman || !FindBones())
        {
            Debug.LogWarning($"{nameof(OldPersonCrouchPose)}: no Humanoid Animator with hip, leg and foot bones under {name}, so the old man will not bend down when crouching.", this);
            enabled = false;
        }
    }

    bool FindBones()
    {
        hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        spine = animator.GetBoneTransform(HumanBodyBones.Spine);
        leftThigh = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        leftShin = animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);
        leftFoot = animator.GetBoneTransform(HumanBodyBones.LeftFoot);
        rightThigh = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        rightShin = animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);
        rightFoot = animator.GetBoneTransform(HumanBodyBones.RightFoot);
        // The spine is optional (the lean is skipped without it); the legs and hips are the crouch.
        return hips != null && leftThigh != null && leftShin != null && leftFoot != null
               && rightThigh != null && rightShin != null && rightFoot != null;
    }

    // LateUpdate because the Animator poses the bones between Update and LateUpdate; bending them in
    // Update would be overwritten by the clip the same frame. The clip also resets them every frame,
    // which is what stops the bend from adding up.
    void LateUpdate()
    {
        float amount = Mathf.Clamp01(animator.GetFloat(CrouchAnimParameter));
        HipDrop = 0f;
        if (amount <= 0f)
            return;

        float lowestFootBefore = LowestFoot();
        BendLeg(leftThigh, leftShin, leftFoot, amount);
        BendLeg(rightThigh, rightShin, rightFoot, amount);
        // Bending the knees with the hips held still lifts the feet off the floor; lowering the hips by
        // exactly that much puts them back, whatever the rig's leg lengths.
        HipDrop = Mathf.Max(0f, LowestFoot() - lowestFootBefore);
        hips.position -= Vector3.up * HipDrop;
        if (spine != null)
            Rotate(spine, spineLean * amount);
    }

    void BendLeg(Transform thigh, Transform shin, Transform foot, float amount)
    {
        // Kept so the foot can be put back exactly as the clip had it: flat on the floor, not tipped up
        // on its toes by the bend above it.
        Quaternion footRotation = foot.rotation;
        Rotate(thigh, -thighSwing * amount);
        Rotate(shin, kneeBend * amount);
        foot.rotation = footRotation;
    }

    float LowestFoot() => Mathf.Min(leftFoot.position.y, rightFoot.position.y);

    // Around the body's own right axis, on top of the animated pose, so it works whatever way the rig's
    // bone axes point. Positive tips the bone's far end backwards and down for a leg, forwards for the
    // spine - the same sense as the head tilt in OldPersonFPSController.
    void Rotate(Transform bone, float degrees)
    {
        bone.rotation = Quaternion.AngleAxis(degrees, transform.right) * bone.rotation;
    }
}
