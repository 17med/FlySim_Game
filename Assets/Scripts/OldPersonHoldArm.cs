using UnityEngine;

// Poses the old person's right arm for OldPersonPickup: reaching out to grab, carrying the object in
// the hand, and the throw swing. OldPersonPickup decides what happens and when; this only moves the
// arm, and tells it when the hand has arrived (ReachFinished) and when the swing lets go (ConsumeRelease).
// Put it on the old_person root next to OldPersonPickup. Without a Humanoid right arm it does nothing,
// and the pickup works without the hand (objects go straight to the carry spot).
// Reach and carry aim the hand at the object itself, wherever it is, stopping a palm short of its
// surface on the shoulder's side. The throw path is in camera space, like OldPersonSlap's hand path,
// because what matters there is how the hand crosses the screen.
// Order 50: runs its LateUpdate after OldPersonSlap's (0), so a grab started mid-slap takes the arm
// over, and before CameraShake's (200), so the hand does not shake with the view.
[DefaultExecutionOrder(50)]
public class OldPersonHoldArm : MonoBehaviour
{
    enum Phase { None, Reach, Carry, WindUp, Swing, Recover }

    [Tooltip("The first-person camera. Found among the children if left empty.")]
    [SerializeField] Camera viewCamera;
    [Tooltip("The old person's Animator (Humanoid rig), used to find the right arm bones. Found automatically if left empty.")]
    [SerializeField] Animator animator;
    [Tooltip("Wrist to the middle of the palm, metres at his own size. The wrist stops this far short of the object's surface.")]
    [SerializeField, Min(0f)] float palmLength = 0.08f;

    [Header("Timing (seconds)")]
    [Tooltip("Hand going out to the object before it is grabbed.")]
    [SerializeField, Min(0.01f)] float reachTime = 0.25f;
    [Tooltip("Hand drawn back before the throw.")]
    [SerializeField, Min(0.01f)] float windUpTime = 0.12f;
    [Tooltip("Hand whipped forward; the object leaves the hand at the end.")]
    [SerializeField, Min(0.01f)] float swingTime = 0.1f;
    [Tooltip("Arm dropping back into the Idle/Walk pose after a throw or a drop.")]
    [SerializeField, Min(0.01f)] float recoverTime = 0.25f;

    // Defaults fit old_person: the shoulder is ~0.24 m below and ~0.13 m behind the eye and the arm is
    // ~0.45 m to the wrist. Points out of reach are not an error: the arm straightens toward them.
    [Header("Throw path (wrist, metres from the camera: x right, y up, z forward)")]
    [Tooltip("Wind-up: hand drawn back and up at the right edge of the screen.")]
    [SerializeField] Vector3 windUpWrist = new Vector3(0.22f, 0.02f, 0.1f);
    [Tooltip("Release: arm stretched forward, where the object leaves the hand.")]
    [SerializeField] Vector3 releaseWrist = new Vector3(0.08f, -0.02f, 0.4f);

    ArmIK arm;
    Phase phase;
    // Seconds since the current phase started.
    float phaseClock;
    Rigidbody target;
    // Largest half-size of the target, world units: how far its surface is from its centre.
    float targetExtent;
    // The wrist target last frame, in camera space, so a new phase starts where the hand is.
    Vector3 wristLocal;
    Vector3 phaseStartWrist;
    float lastWeight;
    // The weight when the current phase began, so reach and recover start from where the arm is
    // (e.g. grabbing again while the arm is still dropping back) instead of jumping.
    float startWeight;
    bool isReleasePending;

    public bool HasArm => arm != null;
    public bool ReachFinished => phase == Phase.Reach && phaseClock >= reachTime;
    // During the throw the hand leads and the object follows it; the rest of the time it is the other way.
    public bool IsLeading => phase == Phase.WindUp || phase == Phase.Swing;

    // Where the held object's centre belongs while the hand leads: a palm and its half-size beyond the
    // wrist, away from the shoulder. The inverse of GripWrist, so starting the throw causes no jump.
    public Vector3 LeadingObjectCenter
    {
        get
        {
            Vector3 wrist = viewCamera.transform.TransformPoint(wristLocal);
            Vector3 outward = (wrist - arm.Shoulder).normalized;
            return wrist + outward * (targetExtent + palmLength * Scale);
        }
    }

    float Scale => viewCamera.transform.lossyScale.x;

    void Awake()
    {
        if (viewCamera == null)
            viewCamera = GetComponentInChildren<Camera>();
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
        arm = viewCamera != null ? ArmIK.FindRightArm(animator) : null;
        // Fails open: the hand is visual; OldPersonPickup checks HasArm and carries without it.
        if (arm == null)
            Debug.LogWarning("OldPersonHoldArm: no camera or no Humanoid right arm found; objects will be picked up without the hand.", this);
    }

    // The Animator restores the arm on its own next frame; only the phase has to be forgotten.
    void OnDisable()
    {
        phase = Phase.None;
        lastWeight = 0f;
        target = null;
        isReleasePending = false;
    }

    public void Reach(Rigidbody body, float extent)
    {
        target = body;
        targetExtent = extent;
        BeginPhase(Phase.Reach);
    }

    public void Carry() => BeginPhase(Phase.Carry);

    public void Throw() => BeginPhase(Phase.WindUp);

    // Drops the arm back into the animated pose from wherever it is, e.g. when the object is put down.
    // Ignored while already recovering, so letting go at the end of the throw does not restart it.
    public void Release()
    {
        if (phase == Phase.None || phase == Phase.Recover)
            return;
        BeginPhase(Phase.Recover);
    }

    // True once, on the first call after the swing reaches its release point.
    public bool ConsumeRelease()
    {
        bool wasPending = isReleasePending;
        isReleasePending = false;
        return wasPending;
    }

    void BeginPhase(Phase next)
    {
        phase = next;
        phaseClock = 0f;
        phaseStartWrist = wristLocal;
        startWeight = lastWeight;
    }

    // LateUpdate because the Animator poses the bones between Update and LateUpdate; posing the arm in
    // Update would be overwritten by the Idle/Walk clip the same frame.
    void LateUpdate()
    {
        if (phase == Phase.None || arm == null)
            return;
        phaseClock += Time.deltaTime;
        AdvancePhase();
        if (phase == Phase.None)
            return;

        Transform view = viewCamera.transform;
        lastWeight = PhaseWeight();
        wristLocal = PhaseWrist();
        arm.ReachBlended(view.TransformPoint(wristLocal), ArmIK.RightElbowPole(view), lastWeight);
    }

    void AdvancePhase()
    {
        if (phase == Phase.WindUp && phaseClock >= windUpTime)
        {
            wristLocal = windUpWrist;
            BeginPhase(Phase.Swing);
        }
        else if (phase == Phase.Swing && phaseClock >= swingTime)
        {
            wristLocal = releaseWrist;
            isReleasePending = true;
            BeginPhase(Phase.Recover);
        }
        else if (phase == Phase.Recover && phaseClock >= recoverTime)
        {
            phase = Phase.None;
            target = null;
            lastWeight = 0f;
        }
    }

    float PhaseWeight()
    {
        switch (phase)
        {
            case Phase.Reach:
                return Mathf.Lerp(startWeight, 1f, Mathf.SmoothStep(0f, 1f, phaseClock / reachTime));
            case Phase.Recover:
                return startWeight * (1f - Mathf.SmoothStep(0f, 1f, phaseClock / recoverTime));
            default:
                return 1f;
        }
    }

    Vector3 PhaseWrist()
    {
        switch (phase)
        {
            case Phase.Reach:
            case Phase.Carry:
                return GripWrist();
            case Phase.WindUp:
                return Vector3.Lerp(phaseStartWrist, windUpWrist, Mathf.SmoothStep(0f, 1f, phaseClock / windUpTime));
            case Phase.Swing:
                // Squared progress: the hand accelerates into the release, as a real throw does.
                float progress = Mathf.Clamp01(phaseClock / swingTime);
                return Vector3.Lerp(windUpWrist, releaseWrist, progress * progress);
            default:
                return phaseStartWrist;
        }
    }

    // The wrist a palm short of the object's surface, on the line from the shoulder to its centre, in
    // camera space. Keeps the last target if the object has been destroyed.
    Vector3 GripWrist()
    {
        if (target == null)
            return wristLocal;
        Vector3 center = target.worldCenterOfMass;
        Vector3 toCenter = (center - arm.Shoulder).normalized;
        Vector3 wrist = center - toCenter * (targetExtent + palmLength * Scale);
        return viewCamera.transform.InverseTransformPoint(wrist);
    }
}
