using Unity.Netcode;
using UnityEngine;

// The old man being spat in the face: a blob that lands close to his head puts a green splat on his
// screen (OldPersonSpitScreen). Body hits do nothing.
// Spit hits are worked out only on the shooting fly's machine (SpitProjectile.HitsTargets), but the
// splat belongs on the old man's own screen, so the hit is sent to his owner. Offline (nothing
// spawned) it is shown directly.
// Put it on the old man prefab's root, next to its NetworkObject; the blob finds it through
// GetComponentInParent from his CharacterController.
public class OldPersonSpitNetwork : NetworkBehaviour, ISpitHittable
{
    [Tooltip("How close to his head bone a blob must land to count as a face hit, in metres at the old person's own size (scales with him).")]
    [SerializeField, Min(0f)] float faceRadius = 0.35f;

    Transform head;
    OldPersonSpitScreen screen;

    void Awake()
    {
        head = FindHead();
        // Fails open: splats are cosmetic, so without a head to aim at the game simply has none.
        if (head == null)
            Debug.LogWarning($"{nameof(OldPersonSpitNetwork)}: no Humanoid Head bone under {name}; spit in the face will not splat the screen.", this);

        // Inactive included: the Canvas's object may be switched off on other players' copies.
        screen = GetComponentInChildren<OldPersonSpitScreen>(true);
        // Fails open, for the same reason. Only the owner's copy uses it, but every copy warns so a
        // missing component is noticed on whichever machine is being tested.
        if (screen == null)
            Debug.LogWarning($"{nameof(OldPersonSpitNetwork)}: no {nameof(OldPersonSpitScreen)} under {name}; spit in the face will not splat the screen.", this);
    }

    // Runs on the shooting fly's machine, where the blob hit.
    public void OnSpitHit(SpitHit hit)
    {
        if (!IsFaceHit(hit.point))
            return;
        if (!IsSpawned || IsOwner)
            ShowSplat(hit.isBigShot);
        else
            SplatRpc(hit.isBigShot);
    }

    // Everyone may send it: the hit is found on the fly's machine, never the old man's.
    [Rpc(SendTo.Owner, InvokePermission = RpcInvokePermission.Everyone)]
    void SplatRpc(bool isBigShot)
    {
        ShowSplat(isBigShot);
    }

    void ShowSplat(bool isBigShot)
    {
        if (screen != null)
            screen.AddSplat(isBigShot);
    }

    bool IsFaceHit(Vector3 point)
    {
        if (head == null)
            return false;
        float radius = FaceRadiusInWorld();
        return (point - head.position).sqrMagnitude <= radius * radius;
    }

    float FaceRadiusInWorld()
    {
        return faceRadius * transform.lossyScale.x;
    }

    Transform FindHead()
    {
        Animator animator = GetComponentInChildren<Animator>();
        return animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
    }

    // Shows the face zone in the Scene view, so Face Radius can be tuned against the model.
    void OnDrawGizmosSelected()
    {
        Transform headBone = FindHead();
        if (headBone == null)
            return;
        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(headBone.position, FaceRadiusInWorld());
    }
}
