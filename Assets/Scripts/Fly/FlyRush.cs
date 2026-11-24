using UnityEngine;

// The streaks that sell a boost. While the fly is flying fast, a particle system rakes lines past the
// orbit camera; they thicken with speed and stop the moment it slows down, so holding Shift reads as
// going somewhere rather than as the same hover with a bigger number behind it.
// Only the fly's own player sees them: they are drawn around their camera, and another player's fly
// does not run FlyController, so its speed is not known here anyway (FlyAudio's buzz pitch is skipped
// for the same reason).
// Goes next to FlyController on the fly's body, with Streaks pointed at a particle system parented to
// the fly's camera - parented there, not to the fly, so the lines rake past the view rather than
// turning with the body as it banks.
[RequireComponent(typeof(FlyController))]
public class FlyRush : MonoBehaviour
{
    [Tooltip("The streak particles, on a child of the fly's camera. Its own emission rate and lifetime are the authored look; this only scales them. Optional - leave empty for no streaks.")]
    [SerializeField] ParticleSystem streaks;
    [Tooltip("Share of the fly's top boost speed it has to pass before any streaks appear, so cruising the kitchen is clean and only a boost streaks.")]
    [SerializeField, Range(0f, 1f)] float startAt = 0.45f;
    [Tooltip("Streaks per second once the fly is at full boost speed. Between Start At and full speed the rate ramps up from nothing to this.")]
    [SerializeField, Min(0f)] float fullRate = 90f;
    [Tooltip("Seconds the rate takes to follow a change in speed, so letting go of Shift thins the streaks out instead of cutting them dead.")]
    [SerializeField, Range(0f, 1f)] float changeTime = 0.15f;

    FlyController fly;
    ParticleSystem.EmissionModule emission;
    float rate;
    float rateVelocity;

    void Awake()
    {
        fly = GetComponent<FlyController>();
        if (streaks == null)
        {
            // Fails open and quietly: the streaks are the most cosmetic thing in the project, and the
            // offline Kitchen's hand-placed fly has no reason to carry them.
            enabled = false;
            return;
        }
        emission = streaks.emission;
        // The system is left playing for the whole game and driven by its rate alone. Stopping and
        // starting it would cut the streaks already in the air every time the fly dipped below the
        // threshold, which is exactly the hard edge Change Time exists to avoid.
        streaks.Play();
    }

    void Update()
    {
        // Another player's copy, where OwnerOnlyControls has switched FlyController off: its speed is
        // whatever it was in the frame that happened, so the streaks would hang at one wrong rate for
        // the rest of the match. Their camera is off too and nobody would see them, but a particle
        // system emitting a hundred and sixty stretched quads a second is not free for being unseen.
        if (!fly.enabled)
        {
            emission.rateOverTime = 0f;
            return;
        }

        // Rescaled so the streaks begin at nothing the moment the fly passes Start At rather than
        // appearing at part strength: at the threshold itself there is no reason for any.
        float past = Mathf.InverseLerp(startAt, 1f, fly.SpeedFraction01);
        float target = past * fullRate;
        rate = Mathf.SmoothDamp(rate, target, ref rateVelocity, changeTime);
        // Under one a second is a streak every few seconds, which reads as a glitch rather than speed.
        emission.rateOverTime = rate < 1f ? 0f : rate;
    }

    void OnDisable()
    {
        if (streaks == null)
            return;
        // Nothing left raking the view while the fly is dead or the scene is being left. The ones
        // already in the air are kept so they finish their own fade.
        emission.rateOverTime = 0f;
        rate = 0f;
        rateVelocity = 0f;
    }
}
