using UnityEngine;

// Marks an object the old person may pick up and throw (OldPersonPickup). Tagging the object
// "Pickable" does the same; use whichever is handier.
// Put it on the object that has the Rigidbody, since the Rigidbody is what gets carried. RequireComponent
// adds one if it is missing; leave it non-kinematic, or the object cannot be carried.
[RequireComponent(typeof(Rigidbody))]
public class Pickable : MonoBehaviour
{
}
