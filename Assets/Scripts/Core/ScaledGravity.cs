using UnityEngine;

// Makes an object fall at a believable speed in a scaled-up scene. KitchenMap is scaled 45x, so a
// 30 cm bottle is 13.5 m tall to physics and falls its own height in ~1.66 s instead of ~0.25 s,
// which reads as the Moon. Mass cannot fix it, so this adds the missing (multiplier - 1) x gravity
// each physics step. Per object, so scenes at normal scale keep normal gravity.
// Goes on the object with the Rigidbody and follows its Use Gravity, so it stops while the object is
// carried or kinematic. OldPersonPickup reads Multiplier, so its throws match the stronger gravity.
// Tools > Stylized Kitchen > Resize House To World Scale sets every Multiplier in the scene to the
// house's scale, which is what this number means.
[RequireComponent(typeof(Rigidbody))]
public class ScaledGravity : MonoBehaviour
{
    [Tooltip("How many times normal gravity this object falls with. Set it to the scale of the scene it is in: 45 for the Kitchen (KitchenMap's scale). 1 = normal gravity.")]
    [SerializeField, Min(0f)] float multiplier = 45f;

    Rigidbody body;

    public float Multiplier => multiplier;

    void Awake()
    {
        body = GetComponent<Rigidbody>();
        // Falling 45x faster also means 45x further per physics step: a thrown bottle leaves the old
        // man's hand at hundreds of units per second and covers several units in one 0.02 s step,
        // while the house's walls are 4.5 units thick. With Unity's default discrete detection it
        // simply appeared on the other side of them. Sweeping the gap between steps is the fix, and
        // it belongs here because this component is what makes the object fast in the first place.
        if (!body.isKinematic)
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
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
