using UnityEngine;

// One blob of spit in flight. Spawned by FlySpit, which calls Launch and then forgets about it.
// It moves itself instead of using a Rigidbody: at 12 units a second a 0.03 radius blob crosses its
// own width about seven times per frame, so a trigger collider would miss thin geometry outright.
// A sphere cast along this frame's step catches the surface wherever it is in between.
// The blob also stretches along its flight and spins its own squash out on impact, which is what
// sells the weight: a sphere flying at constant size reads as a bead, not a gob of spit.
public class SpitProjectile : MonoBehaviour
{
    // The fly, its model and its collider all sit under the shooter, and the spit leaves from a point
    // inside that volume, so the first thing a cast would hit is always the fly itself. Eight slots is
    // more than enough to step past the shooter's own colliders and reach the first real surface.
    const int MaxSweepHits = 8;
    // Below this length the step's direction is meaningless, so the cast is skipped for the frame.
    const float MinSweepDistance = 1e-5f;

    [Tooltip("The sphere mesh that is the blob itself. It gets stretched along the flight path.")]
    [SerializeField] Transform blob;
    [Tooltip("Drops left behind in the air. Detached on impact so the trail does not vanish with the blob.")]
    [SerializeField] ParticleSystem trail;
    [Tooltip("Spawned at the impact point, turned to face the surface. Its own particles play on awake.")]
    [SerializeField] GameObject splashPrefab;
    [Tooltip("Collision radius, and the blob's own size. Keep it close to the blob mesh's radius.")]
    [SerializeField] float radius = 0.03f;
    [Tooltip("Downward pull, in units per second². Lower than real gravity (9.81) so the arc stays flat enough to aim.")]
    [SerializeField] float gravity = 4f;
    [Tooltip("Seconds before a blob that never hits anything gives up and splashes in mid-air.")]
    [SerializeField] float lifetime = 4f;
    [SerializeField] LayerMask hitLayers = ~0;
    [Tooltip("Extra length per unit per second of speed, as a multiple of the blob's size. 0 = always a sphere.")]
    [SerializeField] float stretchPerSpeed = 0.06f;
    [Tooltip("Longest the blob can stretch, as a multiple of its size.")]
    [SerializeField] float maxStretch = 2.5f;
    [Tooltip("Seconds the splash is kept alive before it is destroyed, long enough for the stain to fade.")]
    [SerializeField] float splashLifetime = 2.5f;
    [Tooltip("Impulse handed to a hit target per unit per second of blob speed, so a blob slowed by its own arc hits softer. At the default 12 unit launch speed this works out to about 1.")]
    [SerializeField] float impactForce = 0.08f;

    readonly RaycastHit[] sweepHits = new RaycastHit[MaxSweepHits];
    Transform shooter;
    Vector3 velocity;
    float age;
    bool hasImpacted;

    // Call once, right after Instantiate. The shooter and everything under it is ignored for the whole
    // flight, not just the first frame: a blob spat straight down would otherwise hit the fly's own
    // collider as it falls past.
    public void Launch(Vector3 launchVelocity, Transform shooterRoot)
    {
        velocity = launchVelocity;
        shooter = shooterRoot;
        age = 0f;
        hasImpacted = false;
        if (velocity.sqrMagnitude > 0f)
            transform.forward = velocity.normalized;
        ShapeBlob();
    }

    void Update()
    {
        if (hasImpacted)
            return;

        float deltaTime = Time.deltaTime;
        age += deltaTime;
        if (age >= lifetime)
        {
            // No surface to lie on, so the splash faces back the way it came and hangs in the air.
            Splash(transform.position, -velocity.normalized);
            return;
        }

        velocity += Vector3.down * (gravity * deltaTime);
        Vector3 step = velocity * deltaTime;
        if (TrySweep(step, out RaycastHit hit))
        {
            // Stop at the surface, not inside it, so the splash sits on the wall rather than behind it.
            transform.position = hit.point + hit.normal * radius;
            ReportHit(hit);
            Splash(hit.point, hit.normal);
            return;
        }

        transform.position += step;
        transform.forward = velocity.normalized;
        ShapeBlob();
    }

    // Nearest hit that is not part of the shooter. NonAlloc because this runs every frame on every
    // blob in the air, and the sorted-by-distance order of the results is not guaranteed.
    bool TrySweep(Vector3 step, out RaycastHit hit)
    {
        hit = default;
        float distance = step.magnitude;
        if (distance < MinSweepDistance)
            return false;

        int count = Physics.SphereCastNonAlloc(transform.position, radius, step / distance, sweepHits,
            distance, hitLayers, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            // A cast that starts already overlapping a collider reports distance 0 and a zero normal,
            // which would put the splash at the blob's own position facing nowhere.
            if (sweepHits[i].distance <= 0f || IsShooter(sweepHits[i].collider.transform))
                continue;
            if (sweepHits[i].distance >= nearest)
                continue;
            nearest = sweepHits[i].distance;
            hit = sweepHits[i];
        }
        return nearest < float.PositiveInfinity;
    }

    bool IsShooter(Transform other)
    {
        return shooter != null && (other == shooter || other.IsChildOf(shooter));
    }

    void ReportHit(RaycastHit hit)
    {
        // GetComponentInParent, not GetComponent: a target's collider is often on a child of the
        // object that actually reacts.
        ISpitHittable target = hit.collider.GetComponentInParent<ISpitHittable>();
        target?.OnSpitHit(new SpitHit
        {
            point = hit.point,
            normal = hit.normal,
            direction = velocity.normalized,
            force = impactForce * velocity.magnitude,
        });
    }

    // Also the end of the blob's life: the splash is an independent object from here on, and so is
    // the trail, which still has drops in the air that should finish falling.
    void Splash(Vector3 point, Vector3 normal)
    {
        hasImpacted = true;

        if (splashPrefab != null)
        {
            // The splash's particle shapes spray along their own +Z, so +Z is pointed out of the wall.
            Quaternion facing = normal.sqrMagnitude > 0f
                ? Quaternion.LookRotation(normal)
                : Quaternion.identity;
            Destroy(Instantiate(splashPrefab, point, facing), splashLifetime);
        }

        if (trail != null)
        {
            trail.transform.SetParent(null, worldPositionStays: true);
            trail.Stop(withChildren: true, stopBehavior: ParticleSystemStopBehavior.StopEmitting);
            Destroy(trail.gameObject, trail.main.startLifetime.constantMax + 0.1f);
        }

        Destroy(gameObject);
    }

    // Keeps the mesh at the collision radius and stretches it along the flight: fast spit is a
    // streak, spit at the top of its arc is a ball.
    void ShapeBlob()
    {
        if (blob == null)
            return;
        float stretch = Mathf.Clamp(1f + velocity.magnitude * stretchPerSpeed, 1f, maxStretch);
        // Squashing the other two axes by the square root of the stretch keeps the volume constant,
        // so a stretched blob does not look like it grew.
        float squash = 1f / Mathf.Sqrt(stretch);
        float diameter = radius * 2f;
        blob.localScale = new Vector3(diameter * squash, diameter * squash, diameter * stretch);
    }
}

// What the spit tells whatever it lands on. Implemented by SpitTarget; implement it on anything else
// that should react to being hit.
public interface ISpitHittable
{
    void OnSpitHit(SpitHit hit);
}

public struct SpitHit
{
    public Vector3 point;
    // The surface's normal, pointing back out at the shooter.
    public Vector3 normal;
    // Which way the blob was travelling, so a push can follow the shot rather than the surface.
    public Vector3 direction;
    public float force;
}
