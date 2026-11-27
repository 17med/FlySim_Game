using UnityEngine;

// Marks an object the old person may pick up and throw (OldPersonPickup). Tagging the object
// "Pickable" does the same; use whichever is handier.
// Put it on the object that has the Rigidbody, since the Rigidbody is what gets carried. RequireComponent
// adds one if it is missing; leave it non-kinematic, or the object cannot be carried.
// It also remembers that the object was just thrown, so FlyKnockbackNetwork can tell a throw that
// lands on the fly (one hit off its lives) from an object merely rolling or falling into it (knockback
// only). OldPersonPickup adds this component at the moment of the throw when the object only has the
// tag, which is the case for every prop in the Kitchen scenes.
// The throw state is local and deliberately not networked: the throw, the physics of the flight and
// the hit detection all run on the thrower's machine. The old man owns every prop (KitchenSpawner
// gives them to him when he joins), and every other copy is kinematic under NetworkRigidbody, which
// FlyKnockbackNetwork skips, so no other machine ever needs to ask whether the object was thrown.
[RequireComponent(typeof(Rigidbody))]
public class Pickable : MonoBehaviour
{
    // Time.time until which a hit on the fly still counts as the throw's; 0 when not thrown.
    float thrownUntil;
    // Cleared by the first hit, so one throw costs the fly one life however often it bounces into it.
    bool hasThrowHit;

    // Called by the thrower as the object leaves his hand. A bounce off a wall still counts while the
    // window is open: the old man banking a cup off the cupboard into the fly earned the hit.
    public void MarkThrown(float seconds)
    {
        thrownUntil = Time.time + seconds;
        hasThrowHit = true;
    }

    // Called when the object is picked up again, so carrying it into the fly is never a throw.
    public void ClearThrown()
    {
        thrownUntil = 0f;
        hasThrowHit = false;
    }

    // True at most once per throw, and only inside its window; using it up is the point, so the
    // caller that gets true is the one that reports the hit.
    public bool TryConsumeThrowHit()
    {
        if (!hasThrowHit || Time.time >= thrownUntil)
            return false;
        hasThrowHit = false;
        return true;
    }
}
