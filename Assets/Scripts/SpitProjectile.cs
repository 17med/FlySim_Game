using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

// One blob of spit in flight. Taken from FlySpit's pool, which calls Launch and then forgets about it;
// the blob hands itself back to the pool once its trail has finished (or destroys itself if it was
// never given a pool).
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
    // Smallest shot size Launch accepts; FlySpit's slider already stays well above it.
    const float MinSize = 0.01f;
    // An already-overlapped surface this close to the blob's centre (squared units) means the centre
    // is inside the collider: ClosestPoint hands back the centre itself then.
    const float MinSurfaceDistanceSquared = 1e-10f;
    // Cosine of the widest angle between the blob's flight and the direction to a surface it already
    // overlaps that still counts as flying into it: 80°, so even a shallow shot into a wall splashes,
    // while flying parallel to it (90°) does not.
    const float MinApproachCosine = 0.17f;

    [Tooltip("The sphere mesh that is the blob itself. It gets stretched along the flight path.")]
    [SerializeField] Transform blob;
    [Tooltip("Drops left behind in the air. Detached on impact so the trail does not vanish with the blob.")]
    [SerializeField] ParticleSystem trail;
    [Tooltip("Spawned at the impact point, turned to face the surface. Its own particles play on awake.")]
    [SerializeField] GameObject splashPrefab;
    [Tooltip("Collision radius, and the blob's own size. Keep it close to the blob mesh's radius.")]
    // 0.05 rather than 0.03: at 0.06 across the blob was a seventh of the fly and hard to see in flight.
    [SerializeField] float radius = 0.05f;
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
    [Tooltip("Seconds the blob takes to grow from Launch Scale to full size, so it reads as coming out of the mouth instead of popping into the air. 0 = full size at once.")]
    [SerializeField, Range(0f, 0.3f)] float growTime = 0.06f;
    [Tooltip("Size the blob starts at as it leaves the mouth, as a fraction of its full size.")]
    [SerializeField, Range(0.05f, 1f)] float launchScale = 0.3f;

    // Read by FlySpit off the prefab, to aim the arc so it comes down on the crosshair.
    public float Gravity => gravity;

    readonly RaycastHit[] sweepHits = new RaycastHit[MaxSweepHits];
    IObjectPool<SpitProjectile> pool;
    Transform shooter;
    Vector3 velocity;
    float age;
    bool hasImpacted;
    // After impact: seconds until the trail's last drops are gone and the blob can be reused.
    float trailFadeLeft;
    // This shot's size, from FlySpit's Spit Size: 1 = the sizes set on this prefab.
    float size = 1f;
    // The size the trail's particle settings are currently scaled to. A pooled blob keeps its trail
    // between shots, so each shot rescales from the previous shot's size rather than from the prefab.
    float trailSize = 1f;

    // Collision radius for this shot; the blob mesh is drawn at the same size.
    float ShotRadius => radius * size;

    // Particle buffer for scaling a splash, reused so a hit allocates no list.
    static readonly List<ParticleSystem> splashSystems = new List<ParticleSystem>();

    // Call once, right after creating the blob. Without a pool the blob destroys itself when done.
    public void SetPool(IObjectPool<SpitProjectile> owner)
    {
        pool = owner;
    }

    // Call on every shot, on a blob taken from the pool (inactive) or freshly instantiated. The
    // shooter and everything under it is ignored for the whole flight, not just the first frame: a
    // blob spat straight down would otherwise hit the fly's own collider as it falls past.
    // shotSize scales the blob, its hit radius, its trail drops and its splash; 1 = prefab sizes.
    public void Launch(Vector3 position, Vector3 launchVelocity, Transform shooterRoot, float shotSize)
    {
        velocity = launchVelocity;
        shooter = shooterRoot;
        age = 0f;
        hasImpacted = false;
        // Floored: a zero size would make the trail rescale divide by zero on the next shot.
        size = Mathf.Max(shotSize, MinSize);
        // Moved before activating: the trail emits by distance travelled, so a reused blob that was
        // switched on first and then moved would spray drops along the jump from its last splash.
        Quaternion facing = velocity.sqrMagnitude > 0f ? Quaternion.LookRotation(velocity) : transform.rotation;
        transform.SetPositionAndRotation(position, facing);
        gameObject.SetActive(true);
        if (blob != null)
            blob.gameObject.SetActive(true);
        ShapeBlob();
        RestartTrail();
    }

    void RestartTrail()
    {
        if (trail == null)
            return;
        // Stop-and-clear, then play: a reused blob would otherwise keep last shot's drops, and the
        // emitter would keep its old position as the start of the distance it has travelled.
        trail.Stop(withChildren: true, stopBehavior: ParticleSystemStopBehavior.StopEmittingAndClear);
        if (!Mathf.Approximately(size, trailSize))
        {
            ScaleParticles(trail, size / trailSize);
            trailSize = size;
        }
        trail.Play(withChildren: true);
    }

    // Scales one particle system's drop size, launch speed and emitter radius, so a bigger shot
    // sprays bigger drops from a wider area and farther. Done on the settings, not the transform:
    // these systems use Local scaling mode, which ignores a parent's scale.
    static void ScaleParticles(ParticleSystem system, float factor)
    {
        ParticleSystem.MainModule main = system.main;
        main.startSize = ScaleCurve(main.startSize, factor);
        main.startSpeed = ScaleCurve(main.startSpeed, factor);
        ParticleSystem.ShapeModule shape = system.shape;
        shape.radius *= factor;
    }

    // Scales every mode a MinMaxCurve can be in. Setting startSizeMultiplier instead would only
    // scale the upper value of a "random between two constants" size, which the drops use.
    static ParticleSystem.MinMaxCurve ScaleCurve(ParticleSystem.MinMaxCurve curve, float factor)
    {
        switch (curve.mode)
        {
            case ParticleSystemCurveMode.Constant:
                curve.constant *= factor;
                break;
            case ParticleSystemCurveMode.TwoConstants:
                curve.constantMin *= factor;
                curve.constantMax *= factor;
                break;
            default:
                curve.curveMultiplier *= factor;
                break;
        }
        return curve;
    }

    void Update()
    {
        if (hasImpacted)
        {
            WaitForTrailToFade(Time.deltaTime);
            return;
        }

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
        if (TrySweep(step, out Impact impact))
        {
            // Stop at the surface, not inside it, so the splash sits on the wall rather than behind it.
            transform.position = impact.point + impact.normal * ShotRadius;
            ReportHit(impact);
            Splash(impact.point, impact.normal);
            return;
        }

        transform.position += step;
        transform.forward = velocity.normalized;
        ShapeBlob();
    }

    // What the blob hit this frame: the collider, and the surface point and normal to splash on.
    struct Impact
    {
        public Collider collider;
        public Vector3 point;
        public Vector3 normal;
    }

    // Nearest hit that is not part of the shooter. NonAlloc because this runs every frame on every
    // blob in the air, and the sorted-by-distance order of the results is not guaranteed.
    bool TrySweep(Vector3 step, out Impact impact)
    {
        impact = default;
        float distance = step.magnitude;
        if (distance < MinSweepDistance)
            return false;

        Vector3 direction = step / distance;
        int count = Physics.SphereCastNonAlloc(transform.position, ShotRadius, direction, sweepHits,
            distance, hitLayers, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            RaycastHit candidate = sweepHits[i];
            if (IsShooter(candidate.collider.transform))
                continue;
            // A cast that starts already overlapping a collider reports it at distance 0 with no usable
            // point or normal, and never reports it further along either. Skipping those let a blob
            // spawned against a wall (the fly's mouth is only 0.02 inside its collider's edge) fly
            // through it with no splash. Already touching is the nearest a hit can be, so it wins.
            if (candidate.distance <= 0f)
            {
                if (TryStartOverlapImpact(candidate.collider, direction, out impact))
                    return true;
                continue;
            }
            if (candidate.distance >= nearest)
                continue;
            nearest = candidate.distance;
            impact = new Impact { collider = candidate.collider, point = candidate.point, normal = candidate.normal };
        }
        return nearest < float.PositiveInfinity;
    }

    // A collider the blob already overlaps counts as hit only if the blob is heading into it, so
    // a big blob flying along a wall beside the fly keeps going instead of splatting on that wall.
    bool TryStartOverlapImpact(Collider other, Vector3 direction, out Impact impact)
    {
        Vector3 center = transform.position;
        impact = new Impact { collider = other, point = center, normal = -direction };
        // ClosestPoint does not support non-convex mesh colliders, and the centre already inside the
        // collider means it is too late to find the surface it crossed. Either way the blob is in
        // the thing it hit, so it splashes where it is, facing back the way it came.
        if (other is MeshCollider mesh && !mesh.convex)
            return true;
        Vector3 toSurface = other.ClosestPoint(center) - center;
        if (toSurface.sqrMagnitude < MinSurfaceDistanceSquared)
            return true;

        float approach = Vector3.Dot(toSurface.normalized, direction);
        if (approach < MinApproachCosine)
            return false;
        impact.point = center + toSurface;
        impact.normal = -toSurface.normalized;
        return true;
    }

    bool IsShooter(Transform other)
    {
        return shooter != null && (other == shooter || other.IsChildOf(shooter));
    }

    void ReportHit(Impact impact)
    {
        // GetComponentInParent, not GetComponent: a target's collider is often on a child of the
        // object that actually reacts.
        ISpitHittable target = impact.collider.GetComponentInParent<ISpitHittable>();
        target?.OnSpitHit(new SpitHit
        {
            point = impact.point,
            normal = impact.normal,
            direction = velocity.normalized,
            force = impactForce * velocity.magnitude,
        });
    }

    // Also the end of the blob's flight: the splash is an independent object from here on. The blob
    // itself is hidden but kept until the trail's drops, which are in world space, finish falling;
    // detaching the trail instead (as before pooling) would leave nothing to hand back to the pool.
    void Splash(Vector3 point, Vector3 normal)
    {
        hasImpacted = true;

        if (splashPrefab != null)
        {
            // The splash's particle shapes spray along their own +Z, so +Z is pointed out of the wall.
            Quaternion facing = normal.sqrMagnitude > 0f
                ? Quaternion.LookRotation(normal)
                : Quaternion.identity;
            GameObject splash = Instantiate(splashPrefab, point, facing);
            ScaleSplash(splash);
            Destroy(splash, splashLifetime);
        }

        if (blob != null)
            blob.gameObject.SetActive(false);

        trailFadeLeft = 0f;
        if (trail != null)
        {
            trail.Stop(withChildren: true, stopBehavior: ParticleSystemStopBehavior.StopEmitting);
            trailFadeLeft = trail.main.startLifetime.constantMax + 0.1f;
        }
        if (trailFadeLeft <= 0f)
            Retire();
    }

    // The splash plays on awake, so by the time Instantiate returns its opening burst may already be
    // out at the prefab's size; resizing alone left the impact small. Each system is therefore cleared
    // and restarted after resizing (the same order RestartTrail uses), so the burst fires again at
    // this shot's size. Same frame as Instantiate, so the small version is never drawn.
    void ScaleSplash(GameObject splash)
    {
        if (Mathf.Approximately(size, 1f))
            return;
        splash.GetComponentsInChildren(splashSystems);
        foreach (ParticleSystem system in splashSystems)
        {
            system.Stop(withChildren: false, stopBehavior: ParticleSystemStopBehavior.StopEmittingAndClear);
            ScaleParticles(system, size);
            system.Play(withChildren: false);
        }
    }

    void WaitForTrailToFade(float deltaTime)
    {
        trailFadeLeft -= deltaTime;
        if (trailFadeLeft <= 0f)
            Retire();
    }

    // Called exactly once per shot (Splash only runs once, guarded by hasImpacted): releasing the same
    // blob into the pool twice would hand it out to two shots at once.
    void Retire()
    {
        if (pool != null)
            pool.Release(this);
        else
            Destroy(gameObject);
    }

    // Keeps the mesh at the collision radius and stretches it along the flight: fast spit is a
    // streak, spit at the top of its arc is a ball. Just after launch it is also smaller, growing
    // to full size over Grow Time, so it swells out of the mouth.
    void ShapeBlob()
    {
        if (blob == null)
            return;
        float stretch = Mathf.Clamp(1f + velocity.magnitude * stretchPerSpeed, 1f, maxStretch);
        // Squashing the other two axes by the square root of the stretch keeps the volume constant,
        // so a stretched blob does not look like it grew.
        float squash = 1f / Mathf.Sqrt(stretch);
        // SmoothStep clamps age / growTime to 0..1, so this is 1 from Grow Time on.
        float grow = growTime <= 0f ? 1f : Mathf.Lerp(launchScale, 1f, Mathf.SmoothStep(0f, 1f, age / growTime));
        float diameter = ShotRadius * 2f * grow;
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
