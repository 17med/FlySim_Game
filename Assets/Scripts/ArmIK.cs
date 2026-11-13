using UnityEngine;

// Two-bone IK for the old person's right arm (shoulder, elbow, wrist), shared by OldPersonSlap and
// OldPersonHoldArm so the solver exists once. The model has no slap or carry animation, so both pose
// the arm in code on top of the Idle/Walk clip, from LateUpdate, after the Animator has posed the bones.
// World-space rotations throughout, so the rig's bone axes do not matter.
public sealed class ArmIK
{
    // Elbow points down and out to his right (camera space), as in a natural slap or reach.
    const float ElbowOutward = 0.5f;

    readonly Transform upperArmBone;
    readonly Transform lowerArmBone;
    readonly Transform handBone;

    ArmIK(Transform upperArm, Transform lowerArm, Transform hand)
    {
        upperArmBone = upperArm;
        lowerArmBone = lowerArm;
        handBone = hand;
    }

    public Vector3 Shoulder => upperArmBone.position;

    // Null when the Animator is missing, not Humanoid, or lacks a right arm bone; callers fail open.
    public static ArmIK FindRightArm(Animator animator)
    {
        // GetBoneTransform only works on a Humanoid rig; old_person.fbx is imported as Humanoid.
        if (animator == null || !animator.isHuman)
            return null;
        Transform upperArm = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        Transform lowerArm = animator.GetBoneTransform(HumanBodyBones.RightLowerArm);
        Transform hand = animator.GetBoneTransform(HumanBodyBones.RightHand);
        if (upperArm == null || lowerArm == null || hand == null)
            return null;
        return new ArmIK(upperArm, lowerArm, hand);
    }

    public static Vector3 RightElbowPole(Transform view) => -view.up + view.right * ElbowOutward;

    // Solved at full strength, then blended from the animated pose in local space, so the arm rises out
    // of the Idle/Walk clip and drops back into it smoothly. Weight 0 = animated pose, 1 = wrist on target.
    public void ReachBlended(Vector3 target, Vector3 pole, float weight)
    {
        Quaternion animatedUpper = upperArmBone.localRotation;
        Quaternion animatedLower = lowerArmBone.localRotation;
        Reach(target, pole);
        upperArmBone.localRotation = Quaternion.Slerp(animatedUpper, upperArmBone.localRotation, weight);
        lowerArmBone.localRotation = Quaternion.Slerp(animatedLower, lowerArmBone.localRotation, weight);
    }

    // On top of the animated pose, around a world axis, so it works whatever way the bone's local axes point.
    public void RotateHand(float degrees, Vector3 axis)
    {
        handBone.rotation = Quaternion.AngleAxis(degrees, axis) * handBone.rotation;
    }

    // Bends the elbow toward the pole so the wrist reaches the target.
    void Reach(Vector3 target, Vector3 pole)
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
}
