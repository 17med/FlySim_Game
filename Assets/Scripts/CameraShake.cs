using UnityEngine;

// Small camera jolt for game feel: Shake() adds "trauma", which fades out over time; the camera is
// rotated by smooth noise scaled by trauma squared, so small hits are subtle and big ones kick.
// Put this on the Main Camera, next to OrbitCamera. FlySpit finds it there on its own.
// Rotation only, never position: OrbitCamera keeps the camera out of walls, and moving it afterwards
// could push the near plane into one.
// DefaultExecutionOrder runs this LateUpdate after OrbitCamera's (order 0), which sets the camera's
// pose from scratch every frame; the shake is layered on top of that pose and never builds up.
[DefaultExecutionOrder(200)]
public class CameraShake : MonoBehaviour
{
    [Tooltip("Degrees the camera turns at full trauma. Small values are best: the fly is tiny on screen.")]
    [SerializeField, Range(0f, 10f)] float maxAngle = 2f;
    [Tooltip("Roll (tilt) at full trauma, in degrees.")]
    [SerializeField, Range(0f, 10f)] float maxRoll = 1.5f;
    [Tooltip("How fast the shake wobbles. Higher = buzzier.")]
    [SerializeField, Range(1f, 60f)] float frequency = 25f;
    [Tooltip("Trauma lost per second. At 4, one shot (0.6 trauma) is gone in 0.15 s.")]
    [SerializeField, Min(0.1f)] float recoverySpeed = 4f;

    float trauma;
    // Steady minimum shake, asked for every frame (e.g. while a Big Shot is charging) and cleared after
    // each LateUpdate, so it stops the moment the caller stops asking.
    float rumble;
    // A different noise row per axis, so pitch, yaw and roll do not move in step.
    const float PitchSeed = 0f;
    const float YawSeed = 37.1f;
    const float RollSeed = 91.7f;

    // amount: 0 to 1, added to the current shake and capped at 1.
    public void Shake(float amount)
    {
        trauma = Mathf.Clamp01(trauma + amount);
    }

    // amount: 0 to 1. Holds the shake at least this strong for the current frame only.
    public void Rumble(float amount)
    {
        rumble = Mathf.Max(rumble, Mathf.Clamp01(amount));
    }

    void LateUpdate()
    {
        float level = Mathf.Max(trauma, rumble);
        rumble = 0f;
        if (level <= 0f)
            return;

        float strength = level * level;
        float time = Time.time * frequency;
        Quaternion offset = Quaternion.Euler(Noise(PitchSeed, time) * maxAngle * strength,
                                             Noise(YawSeed, time) * maxAngle * strength,
                                             Noise(RollSeed, time) * maxRoll * strength);
        transform.rotation *= offset;
        trauma = Mathf.Max(0f, trauma - recoverySpeed * Time.deltaTime);
    }

    // Smooth random value from -1 to 1.
    static float Noise(float seed, float time)
    {
        return Mathf.PerlinNoise(seed, time) * 2f - 1f;
    }
}
