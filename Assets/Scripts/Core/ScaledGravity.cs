using UnityEngine;

// Makes an object fall at a believable speed in a scaled-up scene. KitchenMap is scaled 30x, so a
// 30 cm bottle is 9 m tall to physics and falls its own height in ~1.35 s instead of ~0.25 s, which
// reads as the Moon. Mass cannot fix it, so this adds the missing (multiplier - 1) x gravity each
// physics step. Per object, so scenes at normal scale keep normal gravity.
// Goes on the object with the Rigidbody and follows its Use Gravity, so it stops while the object is
// carried or kinematic. OldPersonPickup reads Multiplier, so its throws match the stronger gravity.
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
