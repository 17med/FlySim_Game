using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// The old person stepping on a fly: while he stands on the ground, a fly that is itself standing on
// something under his feet is killed outright, where a fly in the air survives the first slap. A fly
// pinned between a shoe and the floor has nowhere to go, so this is a certain kill, not another hit.
// Goes on the old_person root next to OldPersonFPSController, whose CharacterController gives the sole
// of his shoe. Nothing here reads input: the stomp happens by standing in the wrong place.
// The kill is worked out only on the machine whose player is this old man and then travels to the fly's
// owner through FlyHealthNetwork, so every machine agrees on the one death. Unlike OldPersonSlap there
// is no swing to replay, so on another player's copy this script simply does nothing - and it decides
// that for itself from the NetworkObject rather than waiting to be told, because nothing switches it
// off: OwnerOnlyControls only disables the components Wire Network Players lists.
// No [RequireComponent]: old_person.prefab is the bare FBX, and its Variant is where the
// CharacterController lives, so requiring one would quietly add a second to the base for the Variant
// to inherit. Awake fails closed instead.
public class OldPersonStomp : MonoBehaviour
{
    // A shoe-sized sphere holds his own capsule, the floor, and the odd fly. Flies are small and there
    // is never a crowd under one foot, so a short buffer is plenty.
    const int FootBufferSize = 16;
    // The ray below a fly passes through the fly's own colliders at most, then the ground.
    const int GroundBufferSize = 4;

    [Tooltip("His own controller; its bottom is the sole of his shoe. Found on this object if left empty.")]
    [SerializeField] CharacterController body;
    [Tooltip("Whether this old man is being played on this machine at all: KitchenRoles switches it off on the rig the player is not playing, and there is no stomping with a rig nobody is standing in. Found on this object if left empty.")]
    [SerializeField] OldPersonFPSController controls;
    [Tooltip("Optional jolt on the view camera when he squashes a fly. Taken from the camera among his children if left empty.")]
    [SerializeField] CameraShake cameraShake;
    [Tooltip("Optional squish played where the fly died. Leave empty for a silent stomp.")]
    [SerializeField] AudioClip squishSound;

    // Every distance here is in metres at the old person's own size and multiplied by his scale when
    // it is used, exactly as OldPersonSlap's reach and hit radius are: the kitchen is built at 45
    // units to the metre and he is scaled to match, so a sphere measured in world units would be
    // either inside his ankle or the size of the room.
    [Tooltip("How wide the stomp reaches around the sole, in metres at his own size (scales with him). About the size of a shoe.")]
    [SerializeField, Min(0f)] float footRadius = 0.25f;
    [Tooltip("How far above the sole the stomp sphere sits, in metres at his own size. It covers that much of the shoe and the rest of its radius below the sole.")]
    [SerializeField, Min(0f)] float footLift = 0.08f;
    [Tooltip("How close the ground must be under a fly for it to count as standing, in metres at his own size. A centimetre is about half a fly, since the fly does not scale with the house.")]
    [SerializeField, Min(0.001f)] float flyGroundClearance = 0.03f;
    [Tooltip("Seconds before the same fly can be stomped again. Longer than the fly's respawn delay on purpose: standing on a spawn point must not kill the same fly over and over as it reappears.")]
    [SerializeField, Min(0f)] float sameFlyCooldown = 12f;
    [Tooltip("What the stomp looks for under his feet.")]
    [SerializeField] LayerMask stompLayers = ~0;
    [Tooltip("What counts as ground under a fly. Everything by default: a fly standing on a dropped pan is as flat as one on the floor.")]
    [SerializeField] LayerMask flyGroundLayers = ~0;

    [Header("Camera jolt (0-1)")]
    [Tooltip("Smaller than a slap's: the foot is far from the eyes and the fly puts up no resistance.")]
    [SerializeField, Range(0f, 1f)] float stompShake = 0.35f;

    readonly Collider[] underfoot = new Collider[FootBufferSize];
    readonly RaycastHit[] belowFly = new RaycastHit[GroundBufferSize];
    // When each fly may be stomped again, by Time.time.
    readonly Dictionary<FlyHealth, float> stompedUntil = new Dictionary<FlyHealth, float>();
    // Reused so forgetting old rows does not allocate every frame.
    readonly List<FlyHealth> expired = new List<FlyHealth>();
    // Null in the single player kitchen, where this machine is the only machine there is.
    NetworkObject player;

    // True on a copy of another player's old man: the player standing on the fly is somewhere else, and
    // their machine does the killing. Not spawned means offline, where every copy is the owner's.
    bool IsMirror => player != null && player.IsSpawned && !player.IsOwner;

    void Awake()
    {
        if (body == null)
            body = GetComponent<CharacterController>();
        if (body == null)
        {
            // Fails closed: the sole of the shoe is where the stomp is measured from, so without the
            // controller there is nothing to measure.
            Debug.LogError($"{nameof(OldPersonStomp)}: no CharacterController on this object; assign Body (the old person's own controller).", this);
            enabled = false;
            return;
        }

        // On the parent, not this object: the NetworkObject sits on the spawned body's root while this
        // may be on the rig below it. Read once - a body's ownership is settled when it spawns.
        player = GetComponentInParent<NetworkObject>();

        if (controls == null)
            controls = GetComponent<OldPersonFPSController>();
        // Fails open: ownership already keeps other players' copies out, so this only costs the single
        // player kitchen the nicety of an unplayed old man not treading on flies.
        if (controls == null)
            Debug.LogWarning($"{nameof(OldPersonStomp)}: no {nameof(OldPersonFPSController)} on this object, so an old man nobody is playing will still stomp flies that land at his feet.", this);

        if (cameraShake == null)
        {
            Camera view = GetComponentInChildren<Camera>();
            if (view != null)
                cameraShake = view.GetComponent<CameraShake>();
        }
        // Fails open: the jolt is feedback only; the fly still dies without it.
        if (cameraShake == null)
            Debug.LogWarning($"{nameof(OldPersonStomp)}: no {nameof(CameraShake)} on the view camera; stomps will not jolt the view.", this);
    }

    void Update()
    {
        // Two different reasons to stand aside: this is somebody else's old man, whose own machine is
        // already doing this, or it is the rig nobody is playing in the single player kitchen, which
        // should not be killing flies by standing still where the level builder left it.
        if (IsMirror || (controls != null && !controls.enabled))
            return;
        // Only while he is standing. Airborne, his feet are knees and shins to a fly on the floor, and
        // a fly he passes over in a jump should get away; the kill lands on the frame he comes down,
        // when isGrounded turns true again. OldPersonFPSController moves him every frame (its constant
        // push into the ground), so isGrounded is this frame's answer, not a stale one.
        if (!body.isGrounded)
            return;

        float scale = BodyScale;
        FootSphere(body, scale, out Vector3 center, out float radius);
        int count = Physics.OverlapSphereNonAlloc(center, radius, underfoot, stompLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
            TryStomp(underfoot[i], scale);
        ForgetExpiredCooldowns();
    }

    void TryStomp(Collider touched, float scale)
    {
        // His own capsule and the first-person body copy are in the sphere every single frame.
        if (touched.transform.IsChildOf(transform))
            return;

        FlyHealth health = touched.GetComponentInParent<FlyHealth>();
        // Everything else down there - the floor, a dropped plate - is simply stood on.
        // A fly already dead is skipped rather than killed twice: the same step keeps finding it for
        // as long as the network takes to tell everyone, and its respawn would reset the hit count.
        if (health == null || health.IsDead)
            return;
        if (stompedUntil.TryGetValue(health, out float readyTime) && Time.time < readyTime)
            return;
        if (!IsFlyStanding(health, touched, scale))
            return;

        // Read before the kill: death switches the fly's CharacterController off, and a disabled
        // collider has no bounds to play the squish at.
        Vector3 where = touched.bounds.center;
        stompedUntil[health] = Time.time + sameFlyCooldown;
        Kill(health);

        if (cameraShake != null)
            cameraShake.Shake(stompShake);
        if (squishSound != null)
            GameAudio.PlaySfxAt(squishSound, where);
    }

    // Online the kill is sent to the server, which owns the fly's death; offline FlyHealthNetwork
    // applies it straight to FlyHealth itself, so this one call covers both kitchens.
    static void Kill(FlyHealth health)
    {
        FlyHealthNetwork networked = health.GetComponentInParent<FlyHealthNetwork>();
        if (networked != null)
        {
            networked.ReportStomp();
            return;
        }
        health.Kill();
    }

    // Whether the fly is standing on something, decided here, on the machine doing the stomping,
    // rather than taken from FlyController.IsGrounded: every copy of a fly but its owner's has its
    // FlyController switched off, and the isGrounded left behind says where the fly was when that copy
    // last ran, which online is never.
    bool IsFlyStanding(FlyHealth health, Collider touched, float scale)
    {
        FlyController fly = health.GetComponent<FlyController>();
        if (fly == null)
            fly = touched.GetComponentInParent<FlyController>();
        // The fast path, on the fly's own machine: its controller is live and worked this out from the
        // move it just made, which is more accurate than any cast from outside.
        if (fly != null && fly.enabled)
            return fly.IsGrounded;

        // The fly's body, not whichever child collider the foot sphere happened to return first.
        Transform flyBody = fly != null ? fly.transform : health.transform;
        CharacterController flyController = fly != null ? fly.GetComponent<CharacterController>() : null;
        Bounds bounds = flyController != null ? flyController.bounds : touched.bounds;

        float clearance = flyGroundClearance * scale;
        // Started a clearance inside the fly and cast twice as far, so a fly resting on the floor does
        // not begin the cast in the surface it is standing on - and a ray from inside a collider does
        // not report that collider, so it leaves the fly itself alone.
        Vector3 start = new Vector3(bounds.center.x, bounds.min.y + clearance, bounds.center.z);
        int count = Physics.RaycastNonAlloc(start, Vector3.down, belowFly, clearance * 2f, flyGroundLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Transform hit = belowFly[i].transform;
            // Its own colliders are not ground, and neither is his shoe: a fly standing on his foot is
            // riding it, not being crushed by it.
            if (!hit.IsChildOf(flyBody) && !hit.IsChildOf(transform))
                return true;
        }
        return false;
    }

    // Where the stomp looks: a ball at the sole of his shoe, in world units.
    void FootSphere(CharacterController standingOn, float scale, out Vector3 center, out float radius)
    {
        Bounds standing = standingOn.bounds;
        center = new Vector3(standing.center.x, standing.min.y + footLift * scale, standing.center.z);
        radius = footRadius * scale;
    }

    // Units per metre at his size, guarded against a zero scale, which would shrink the stomp to a
    // point and leave no hint as to why flies survive being stood on.
    float BodyScale => Mathf.Max(Mathf.Abs(transform.lossyScale.x), 0.0001f);

    // Rows are dropped as they run out: a long match kills a lot of flies, and a row for a fly that
    // has since been destroyed would hold on to it for ever.
    void ForgetExpiredCooldowns()
    {
        if (stompedUntil.Count == 0)
            return;
        expired.Clear();
        foreach (KeyValuePair<FlyHealth, float> row in stompedUntil)
        {
            if (row.Key == null || Time.time >= row.Value)
                expired.Add(row.Key);
        }
        foreach (FlyHealth fly in expired)
            stompedUntil.Remove(fly);
    }

    // Draws the stomp sphere at his feet in the Scene view, so its size can be judged against the shoe
    // rather than guessed from a number in metres.
    void OnDrawGizmosSelected()
    {
        // Resolved into a local, not into the serialized field: writing a field from a gizmo would
        // mark the scene or prefab dirty just for selecting him.
        CharacterController shown = body != null ? body : GetComponent<CharacterController>();
        if (shown == null)
            return;
        FootSphere(shown, BodyScale, out Vector3 center, out float radius);
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(center, radius);
    }
}
