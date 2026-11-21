using Unity.Netcode;
using UnityEngine;

// The fly being hit, by the old man's slap or by a moving object that runs into it: it is flung and
// stunned (FlyController.Knock) and the hit sound plays.
// Online the fly only moves on its owner's machine while hits are worked out on the old man's, so the
// hit travels over the network; offline it is applied directly.
// Object hits are looked for on every copy but only count where the object is simulated - every other
// copy is kinematic, and kinematic bodies are skipped - so each hit is found exactly once.
// Spit never knocks a fly: blobs move themselves without a Rigidbody.
// Goes on the fly prefab's root, next to its NetworkObject.
public class FlyKnockbackNetwork : NetworkBehaviour
{
    // Room for the kitchen's static colliders too, which the search sphere also returns.
    const int OverlapBufferSize = 64;

    [Tooltip("Played on every hit: flat for the fly hit and the player who hit it, from the fly for everyone else. On a child of the fly's controller, so it moves with the fly. Optional.")]
    [SerializeField] AudioSource hitSound;

    [Header("Hit by objects")]
    [Tooltip("Slowest an object can move, in units per second, and still knock the fly.")]
    [SerializeField, Min(0f)] float minObjectSpeed = 3f;
    [Tooltip("Share of the object's speed the fly is knocked away with.")]
    [SerializeField, Min(0f)] float objectKnockTransfer = 1f;
    [Tooltip("Fastest the fly is knocked away by an object, in units per second. A throw in the scaled-up Kitchen can leave the hand at hundreds of units per second.")]
    [SerializeField, Min(0f)] float maxObjectKnockSpeed = 60f;
    // A thrown object can cover several fly-lengths in one physics step, so rather than waiting for it
    // to overlap the fly, every object within this distance has its next step checked against the fly.
    [Tooltip("How far from the fly objects are checked, in units. Must exceed the distance the fastest object moves in one physics step (speed x Fixed Timestep).")]
    [SerializeField, Min(0f)] float searchRadius = 12f;
    [Tooltip("Seconds after a hit during which objects cannot knock the fly again, so one object is not counted twice.")]
    [SerializeField, Min(0f)] float objectHitCooldown = 0.5f;

    readonly Collider[] nearby = new Collider[OverlapBufferSize];
    FlyController fly;
    CharacterController flyBody;
    float objectHitReadyTime;

    void Awake()
    {
        fly = GetComponentInChildren<FlyController>(true);
        if (fly != null)
            flyBody = fly.GetComponent<CharacterController>();
        // Fails open: the fly still plays normally; it just cannot be knocked.
        if (fly == null || flyBody == null)
            Debug.LogWarning($"{nameof(FlyKnockbackNetwork)}: no {nameof(FlyController)} with a CharacterController under {name}; this fly cannot be knocked.", this);
    }

    // Flings and stuns the fly at this velocity (units per second), on whichever machine owns it.
    // Called on the machine that detected the hit.
    public void Knock(Vector3 velocity)
    {
        if (fly == null)
            return;
        objectHitReadyTime = Time.time + objectHitCooldown;
        PlayHitSound(true);
        if (!IsSpawned || IsOwner)
            fly.Knock(velocity);
        if (IsSpawned)
            KnockRpc(velocity);
    }

    // Everyone may send it: the hit is found on the old man's machine, never the fly owner's.
    [Rpc(SendTo.NotMe, InvokePermission = RpcInvokePermission.Everyone)]
    void KnockRpc(Vector3 velocity)
    {
        if (fly == null)
            return;
        objectHitReadyTime = Time.time + objectHitCooldown;
        PlayHitSound(IsOwner);
        if (IsOwner)
            fly.Knock(velocity);
    }

    void PlayHitSound(bool isFlat)
    {
        if (hitSound == null)
            return;
        hitSound.spatialBlend = isFlat ? 0f : 1f;
        GameAudio.PlaySfx(hitSound);
    }

    void FixedUpdate()
    {
        if (flyBody == null || Time.time < objectHitReadyTime)
            return;
        Vector3 flyCenter = flyBody.bounds.center;
        int count = Physics.OverlapSphereNonAlloc(flyCenter, searchRadius, nearby, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            if (TryGetHitVelocity(nearby[i], flyCenter, out Vector3 objectVelocity))
            {
                Knock(Vector3.ClampMagnitude(objectVelocity * objectKnockTransfer, maxObjectKnockSpeed));
                return;
            }
        }
    }

    // True when the collider belongs to a moving object whose next physics step passes through the fly.
    bool TryGetHitVelocity(Collider candidate, Vector3 flyCenter, out Vector3 velocity)
    {
        velocity = Vector3.zero;
        Rigidbody body = candidate.attachedRigidbody;
        if (body == null || body.isKinematic || body.transform.IsChildOf(transform))
            return false;
        velocity = body.linearVelocity;
        Vector3 start = candidate.bounds.center;
        bool isFastEnough = velocity.sqrMagnitude >= minObjectSpeed * minObjectSpeed;
        // Moving away from the fly: it may still overlap it, but it has already passed.
        if (!isFastEnough || Vector3.Dot(velocity, flyCenter - start) <= 0f)
            return false;

        Vector3 end = start + velocity * Time.fixedDeltaTime;
        float reach = AverageExtent(flyBody.bounds) + AverageExtent(candidate.bounds);
        return DistanceToSegment(flyCenter, start, end) <= reach;
    }

    // Box bounds overstate a round shape's size along the diagonal; the average side is closer.
    static float AverageExtent(Bounds bounds)
    {
        return (bounds.extents.x + bounds.extents.y + bounds.extents.z) / 3f;
    }

    static float DistanceToSegment(Vector3 point, Vector3 start, Vector3 end)
    {
        Vector3 segment = end - start;
        float along = Vector3.Dot(point - start, segment) / Mathf.Max(segment.sqrMagnitude, 1e-6f);
        return Vector3.Distance(point, start + segment * Mathf.Clamp01(along));
    }
}
