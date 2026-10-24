using UnityEngine;

// Procedural body motion on top of the animated wings, in the air only: the fly leans into turns,
// dips its nose when speeding up (and lifts it when braking), and bobs gently while hovering.
// Put this next to FlyController. It moves the model child, never this object: this object's
// rotation is the steering heading, and the model carries a 180° X rotation to stand upright, so the
// motion is layered on top of the model's starting pose instead of overwriting it.
[RequireComponent(typeof(FlyController))]
public class FlyBodyMotion : MonoBehaviour
{
    // Hover bob fades out completely at this flight speed (units per second): a moving fly does not bob.
    const float BobFadeSpeed = 1f;
    // Rotations smaller than this (degrees) count as no turn; the rotation axis is meaningless at zero.
    const float MinTurnAngle = 0.001f;

    [Tooltip("The fly model (the child with the Animator and the 180° X rotation). Found automatically if left empty.")]
    [SerializeField] Transform model;
    [Tooltip("Degrees of lean per degree per second of turning.")]
    [SerializeField] float bankPerTurnRate = 0.1f;
    [Tooltip("Largest lean into a turn, in degrees.")]
    [SerializeField] float maxBank = 15f;
    [Tooltip("Degrees of nose dip per unit per second² of forward acceleration. Braking lifts the nose.")]
    [SerializeField] float tiltPerAcceleration = 0.5f;
    [Tooltip("Largest nose dip or lift, in degrees.")]
    [SerializeField] float maxTilt = 15f;
    [Tooltip("How high the hovering fly bobs, in units.")]
    [SerializeField] float bobHeight = 0.03f;
    [Tooltip("Hover bobs per second.")]
    [SerializeField] float bobFrequency = 0.8f;
    [Tooltip("Seconds the lean and tilt take to settle, and to fade in on take-off and out on landing.")]
    [SerializeField, Range(0.01f, 1f)] float smoothTime = 0.15f;

    FlyController fly;
    Vector3 restPosition;
    Quaternion restRotation;
    Quaternion lastRotation;
    Vector3 lastVelocity;
    // 0 on the ground, 1 in the air; scales all motion so it fades in and out instead of popping.
    float airborne;
    float bank;
    float tilt;
    float bobPhase;
    // SmoothDamp's running state for the values above.
    float airborneRate;
    float bankRate;
    float tiltRate;

    void Awake()
    {
        fly = GetComponent<FlyController>();
        if (model == null)
        {
            Animator animator = GetComponentInChildren<Animator>();
            if (animator != null)
                model = animator.transform;
        }
        if (model == null || model == transform)
        {
            // Fails open: body motion is cosmetic, so only this component stops; the fly stays controllable.
            // Moving this object itself would fight FlyController's steering, hence the second check.
            Debug.LogWarning("FlyBodyMotion: no fly model child found; assign Model. Body motion disabled.", this);
            enabled = false;
            return;
        }
        restPosition = model.localPosition;
        restRotation = model.localRotation;
        lastRotation = transform.rotation;
    }

    // LateUpdate so it reads the heading and velocity after FlyController has moved this frame.
    void LateUpdate()
    {
        float deltaTime = Time.deltaTime;
        // Paused: there is no motion to measure, and dividing by zero would poison the pose.
        if (deltaTime <= 0f)
            return;

        bool isAirborne = fly.State == FlyController.MovementState.Flying;
        airborne = Mathf.SmoothDamp(airborne, isAirborne ? 1f : 0f, ref airborneRate, smoothTime);
        UpdateLean(isAirborne, deltaTime);
        float bob = BobOffset(fly.FlyVelocity.magnitude, deltaTime);

        Quaternion lean = Quaternion.Euler(tilt * airborne, 0f, bank * airborne);
        // Rotating the rest position too pivots the model around this object's centre, not its own origin.
        model.localPosition = lean * restPosition + Vector3.up * (bob * airborne);
        model.localRotation = lean * restRotation;
    }

    // Measured in the air only: landing zeroes the flight velocity in one frame, which would read as
    // a huge braking spike, and turning in place on the ground should not lean.
    void UpdateLean(bool isAirborne, float deltaTime)
    {
        float turnRate = TurnRate(deltaTime);
        Vector3 velocity = fly.FlyVelocity;
        float forwardAcceleration = Vector3.Dot(velocity - lastVelocity, transform.forward) / deltaTime;
        lastVelocity = velocity;

        // Turning right is positive around up; leaning right is negative around Z. Nose down is positive around X.
        float targetBank = isAirborne ? -Mathf.Clamp(turnRate * bankPerTurnRate, -maxBank, maxBank) : 0f;
        float targetTilt = isAirborne ? Mathf.Clamp(forwardAcceleration * tiltPerAcceleration, -maxTilt, maxTilt) : 0f;
        bank = Mathf.SmoothDamp(bank, targetBank, ref bankRate, smoothTime);
        tilt = Mathf.SmoothDamp(tilt, targetTilt, ref tiltRate, smoothTime);
    }

    // Degrees per second around the fly's own up axis. Taken from the rotation change rather than the yaw
    // angle, which flips wildly when the fly points straight up or down.
    float TurnRate(float deltaTime)
    {
        Quaternion delta = transform.rotation * Quaternion.Inverse(lastRotation);
        lastRotation = transform.rotation;
        delta.ToAngleAxis(out float angle, out Vector3 axis);
        if (angle > 180f)
            angle -= 360f;
        if (Mathf.Abs(angle) < MinTurnAngle)
            return 0f;
        return angle * Vector3.Dot(axis, transform.up) / deltaTime;
    }

    // Rises from the rest height and back, never below it, so a fly hovering just above a surface
    // does not dip into it.
    float BobOffset(float speed, float deltaTime)
    {
        bobPhase = (bobPhase + bobFrequency * deltaTime) % 1f;
        float stillness = 1f - Mathf.Clamp01(speed / BobFadeSpeed);
        return (0.5f - 0.5f * Mathf.Cos(bobPhase * 2f * Mathf.PI)) * bobHeight * stillness;
    }
}
