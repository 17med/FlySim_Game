using UnityEngine;

// Makes an object fall at a believable speed in a scaled-up scene. The Kitchen's KitchenMap is scaled
// 30x, so a 30 cm bottle is 9 m tall in physics terms; under normal gravity it falls its own height in
// ~1.35 s instead of ~0.25 s and looks like it is on the Moon. Mass cannot fix that: gravity accelerates
// every mass alike. This adds the missing (multiplier - 1) x gravity each physics step, so the object
// falls as if gravity were multiplier x stronger.
// Per object rather than in Project Settings, so scenes at normal scale keep normal gravity.
// Put it on the object with the Rigidbody. It follows the Rigidbody's own Use Gravity, so it stops while
// OldPersonPickup carries the object (which turns gravity off) and while the body is kinematic.
// OldPersonPickup reads Multiplier, so its throw speed and aim arc match the stronger gravity.
[RequireComponent(typeof(Rigidbody))]
public class ScaledGravity : MonoBehaviour
{
    [Tooltip("How many times normal gravity this object falls with. Set it to the scale of the scene it is in: 30 for the Kitchen (KitchenMap's scale). 1 = normal gravity.")]
    [SerializeField, Min(0f)] float multiplier = 30f;

    Rigidbody body;

    public float Multiplier => multiplier;

    void Awake()
    {
        body = GetComponent<Rigidbody>();
    }

    void FixedUpdate()
    {
        if (!body.useGravity || body.isKinematic)
            return;
        // Acceleration, not force, so it is the same for every mass, like gravity itself.
        body.AddForce(Physics.gravity * (multiplier - 1f), ForceMode.Acceleration);
    }

    // Gravity multiplier for any Rigidbody: its ScaledGravity's, or 1 without one.
    public static float MultiplierOf(Rigidbody target)
    {
        ScaledGravity scaled = target.GetComponent<ScaledGravity>();
        return scaled != null ? scaled.multiplier : 1f;
    }
}
