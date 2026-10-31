using UnityEngine;

// Something the spit can knock about: the cans and plates on the test range. Put it on an object
// with a collider; the collider may be on a child, since SpitProjectile looks the component up
// through the parents.
// A target reacts in three ways, and all three are there so a hit is unmistakable even when the blob
// itself is off-screen behind the fly: it rocks away from the shot, it flashes, and after enough hits
// it drops out and comes back.
// Objects with a Rigidbody are pushed and left to physics. Without one the rocking is a damped
// spring on the object's own rotation, which is what keeps a target bolted to a shelf from flying off.
public class SpitTarget : MonoBehaviour, ISpitHittable
{
    // Below this the spring has visually settled, and leaving it running forever costs an Update a frame.
    const float RestAngle = 0.05f;
    // Degrees per second below which the spring also counts as settled.
    const float RestSpeed = 0.5f;

    [Tooltip("Hits before the target drops out. 0 = it never breaks and can be hit forever.")]
    [SerializeField] int hitsToBreak = 3;
    [Tooltip("Seconds a broken target stays gone before it comes back at full health.")]
    [SerializeField] float respawnDelay = 2.5f;

    [Header("Reaction")]
    [Tooltip("Degrees it rocks per unit of impact impulse; a square-on hit from the default spit is about 1. Objects with a Rigidbody ignore this and are pushed instead.")]
    [SerializeField] float rockPerForce = 18f;
    [Tooltip("Largest rock, in degrees.")]
    [SerializeField] float maxRock = 35f;
    [Tooltip("Rocks per second as it springs back.")]
    [SerializeField] float rockFrequency = 3.5f;
    [Tooltip("How fast the rocking dies down. 1 = stops dead at the first return, 0 = never stops.")]
    [SerializeField, Range(0.01f, 1f)] float rockDamping = 0.35f;

    [Header("Flash")]
    [Tooltip("Colour the target takes the instant it is hit, fading back to its own over Flash Time.")]
    [SerializeField] Color flashColor = new Color(1f, 0.98f, 0.8f);
    [SerializeField, Range(0.01f, 1f)] float flashTime = 0.18f;

    // Hits taken since the last respawn, for a score display or a puzzle to read.
    public int HitCount { get; private set; }

    static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");

    Rigidbody body;
    // Decided once in Awake, because breaking a target parks its Rigidbody as kinematic: reading
    // isKinematic later would make a pushed target start rocking instead after its first respawn.
    bool usesPhysics;
    Renderer[] renderers;
    Collider[] colliders;
    // One instanced material per renderer, so flashing one can never tint the others.
    Material[] flashMaterials;
    Color[] baseColors;
    Vector3 startPosition;
    Quaternion startRotation;
    // The spring's state: a current angle and its speed, around a fixed axis chosen at the hit.
    Vector3 rockAxis = Vector3.right;
    float rockAngle;
    float rockSpeed;
    float flashLeft;
    float respawnLeft;

    void Awake()
    {
        body = GetComponent<Rigidbody>();
        usesPhysics = body != null && !body.isKinematic;
        renderers = GetComponentsInChildren<Renderer>();
        colliders = GetComponentsInChildren<Collider>();
        startPosition = transform.position;
        startRotation = transform.rotation;

        // .material, not .sharedMaterial: writing to the shared one would tint every target that
        // happens to use the same material asset, and the asset itself on disk.
        flashMaterials = new Material[renderers.Length];
        baseColors = new Color[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            flashMaterials[i] = renderers[i].material;
            baseColors[i] = flashMaterials[i].HasProperty(BaseColorProperty)
                ? flashMaterials[i].GetColor(BaseColorProperty)
                : Color.white;
        }
    }

    // Instanced materials are not owned by the asset database, so nothing else will free them.
    void OnDestroy()
    {
        foreach (Material material in flashMaterials)
        {
            if (material != null)
                Destroy(material);
        }
    }

    public void OnSpitHit(SpitHit hit)
    {
        HitCount++;
        flashLeft = flashTime;

        if (usesPhysics)
        {
            body.AddForceAtPosition(hit.direction * hit.force, hit.point, ForceMode.Impulse);
        }
        else
        {
            // Rock away from the shot: the axis is across the blob's path, so the target tips over
            // backwards rather than twisting around.
            Vector3 axis = Vector3.Cross(Vector3.up, hit.direction);
            rockAxis = axis.sqrMagnitude > 1e-6f ? axis.normalized : Vector3.right;
            rockAngle = Mathf.Clamp(hit.force * rockPerForce, -maxRock, maxRock);
            rockSpeed = 0f;
        }

        if (hitsToBreak > 0 && HitCount >= hitsToBreak)
            Break();
    }

    void Update()
    {
        float deltaTime = Time.deltaTime;
        if (respawnLeft > 0f)
        {
            respawnLeft -= deltaTime;
            if (respawnLeft <= 0f)
                Respawn();
            return;
        }

        UpdateFlash(deltaTime);
        if (!usesPhysics)
            UpdateRock(deltaTime);
    }

    void UpdateFlash(float deltaTime)
    {
        if (flashLeft <= 0f)
            return;
        flashLeft -= deltaTime;
        float amount = Mathf.Clamp01(flashLeft / flashTime);
        for (int i = 0; i < flashMaterials.Length; i++)
        {
            if (flashMaterials[i] != null && flashMaterials[i].HasProperty(BaseColorProperty))
                flashMaterials[i].SetColor(BaseColorProperty, Color.Lerp(baseColors[i], flashColor, amount));
        }
    }

    // A damped harmonic spring: the angle is pulled back towards zero by a force proportional to how
    // far out it is, which is what gives the rock its overshoot instead of a dead slide back.
    void UpdateRock(float deltaTime)
    {
        if (Mathf.Abs(rockAngle) < RestAngle && Mathf.Abs(rockSpeed) < RestSpeed)
        {
            if (rockAngle != 0f)
            {
                rockAngle = 0f;
                transform.rotation = startRotation;
            }
            return;
        }

        float stiffness = rockFrequency * 2f * Mathf.PI;
        rockSpeed -= rockAngle * stiffness * stiffness * deltaTime;
        rockSpeed *= Mathf.Pow(rockDamping, deltaTime);
        rockAngle += rockSpeed * deltaTime;
        transform.rotation = Quaternion.AngleAxis(rockAngle, rockAxis) * startRotation;
    }

    void Break()
    {
        respawnLeft = respawnDelay;
        SetVisible(false);
    }

    void Respawn()
    {
        HitCount = 0;
        rockAngle = 0f;
        rockSpeed = 0f;
        flashLeft = 0f;
        transform.SetPositionAndRotation(startPosition, startRotation);
        for (int i = 0; i < flashMaterials.Length; i++)
        {
            if (flashMaterials[i] != null && flashMaterials[i].HasProperty(BaseColorProperty))
                flashMaterials[i].SetColor(BaseColorProperty, baseColors[i]);
        }
        // Before the velocities: a kinematic body silently drops anything written to them, and the
        // body is still parked as kinematic until SetVisible hands it back to physics.
        SetVisible(true);
        if (usesPhysics)
        {
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
        }
    }

    // Disabling the renderers and colliders rather than the GameObject keeps this Update running, so
    // the respawn timer still ticks.
    void SetVisible(bool visible)
    {
        foreach (Renderer target in renderers)
            target.enabled = visible;
        foreach (Collider target in colliders)
            target.enabled = visible;
        // A broken target is parked as kinematic so it cannot keep falling while it is invisible;
        // respawning restores whatever it started as.
        if (body != null)
            body.isKinematic = visible ? !usesPhysics : true;
    }
}
