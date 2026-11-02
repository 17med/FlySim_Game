using UnityEngine;
using UnityEngine.InputSystem;

// Hold Aim (right mouse / gamepad left trigger) to aim; let go to stop. While aiming:
//   - the camera slides over the fly's left or right shoulder (picked in the Inspector) and moves closer
//   - the fly moves slower (walking and flying)
//   - the view zooms in
//   - Aim Image (your own crosshair, e.g. a UI Image centred on a Canvas) is shown, and the spit
//     flies to whatever is under the screen centre; let go and the image is hidden again
// Everything blends in and out over Blend Time, so the camera glides to the shoulder instead of snapping.
// Put this on the Camera next to OrbitCamera. It only writes the aim knobs that OrbitCamera,
// FlyController and FlySpit expose; each of those still owns its own behaviour.
public class AimShootsystem : MonoBehaviour
{
    // Project-wide action (InputSystem_Actions): right mouse button, gamepad left trigger.
    const string AimActionName = "Player/Aim";

    public enum ShoulderSide { Right, Left }

    // True while the Aim button is held.
    public bool IsAiming { get; private set; }

    [Tooltip("The fly. Found automatically if left empty.")]
    [SerializeField] FlyController fly;
    [Tooltip("The orbit camera to frame over the shoulder. Taken from this object if left empty.")]
    [SerializeField] OrbitCamera orbitCamera;

    [Header("Shoulder")]
    [Tooltip("Which side of the fly the camera moves to while aiming.")]
    [SerializeField] ShoulderSide shoulderSide = ShoulderSide.Right;
    [Tooltip("How far sideways the camera moves while aiming, in units. The fly is 0.4 units across.")]
    [SerializeField] float shoulderOffset = 0.12f;
    [Tooltip("How far up the camera moves while aiming, in units.")]
    [SerializeField] float shoulderHeight = 0.03f;
    [Tooltip("The camera's orbit distance is multiplied by this while aiming. Below 1 = closer.")]
    [SerializeField, Range(0.2f, 1f)] float aimDistanceScale = 0.7f;
    [Tooltip("Seconds to slide in and out of aim. 0 = instant.")]
    [SerializeField, Range(0f, 0.5f)] float blendTime = 0.12f;

    [Header("Movement and Zoom")]
    [Tooltip("The fly's speed is multiplied by this while aiming. 0.6 = 60% of normal speed.")]
    [SerializeField, Range(0.05f, 1f)] float aimSpeedMultiplier = 0.6f;
    [Tooltip("The field of view is multiplied by this while aiming. 0.75 turns 60° into 45°.")]
    [SerializeField, Range(0.3f, 1f)] float aimFovMultiplier = 0.75f;

    [Header("Crosshair")]
    [Tooltip("Your crosshair object, shown while aiming and hidden otherwise. Centre it on the screen: the spit goes to the screen centre. Must not be this object or one of its parents.")]
    [SerializeField] GameObject aimImage;

    InputAction aimAction;
    FlySpit spit;
    // 0 = not aiming, 1 = fully aimed; moves linearly over Blend Time and is eased when applied.
    float aimWeight;

    void Awake()
    {
        // Throws if the action is missing: an aim button that silently does nothing is worse than a clear error.
        aimAction = InputSystem.actions.FindAction(AimActionName, throwIfNotFound: true);

        if (orbitCamera == null)
            orbitCamera = GetComponent<OrbitCamera>();
        // Fails open: the camera keeps its normal framing, but slowing and crosshair aiming still work.
        if (orbitCamera == null)
            Debug.LogWarning("AimShootsystem: no OrbitCamera on this object; aiming without the shoulder camera.", this);

        if (fly == null)
            fly = FindAnyObjectByType<FlyController>();
        if (fly != null)
            spit = fly.GetComponent<FlySpit>();
        // Fails open: the camera still moves to the shoulder, but nothing slows down or aims at the crosshair.
        if (fly == null)
            Debug.LogWarning("AimShootsystem: no FlyController found; aiming without slowing the fly. Assign Fly.", this);

        // Fails open: hiding this object would switch this script off with it, and nothing could
        // show it again, so it is dropped and aiming carries on without a crosshair.
        if (aimImage != null && transform.IsChildOf(aimImage.transform))
        {
            Debug.LogError("AimShootsystem: Aim Image is this object or one of its parents; hiding it would turn aiming off. Use a separate object, e.g. an Image on a Canvas.", this);
            aimImage = null;
        }
        // Hidden from the first frame: the scene may have been saved with it switched on.
        if (aimImage != null)
            aimImage.SetActive(false);
    }

    void OnEnable()
    {
        aimAction.Enable();
    }

    // Leaves the fly and camera at normal settings, so disabling this mid-aim never strands the fly slowed.
    void OnDisable()
    {
        IsAiming = false;
        aimWeight = 0f;
        ApplyAim(0f);
        ShowAimImage(false);
    }

    void Update()
    {
        IsAiming = aimAction.IsPressed();
        float targetWeight = IsAiming ? 1f : 0f;
        // 0 means instant, as the tooltip says; it would also divide by zero below.
        aimWeight = blendTime <= 0f ? targetWeight
                                    : Mathf.MoveTowards(aimWeight, targetWeight, Time.deltaTime / blendTime);
        ApplyAim(Mathf.SmoothStep(0f, 1f, aimWeight));
        ShowAimImage(IsAiming);
    }

    // Follows the button, not the blend, so the crosshair is there the moment spitting is allowed.
    void ShowAimImage(bool isVisible)
    {
        // Only on a change, so a crosshair someone switched on or off by hand is not fought every frame.
        if (aimImage != null && aimImage.activeSelf != isVisible)
            aimImage.SetActive(isVisible);
    }

    // weight: 0 = normal, 1 = fully aimed.
    void ApplyAim(float weight)
    {
        if (orbitCamera != null)
        {
            float side = shoulderSide == ShoulderSide.Right ? 1f : -1f;
            orbitCamera.ShoulderOffset = new Vector3(side * shoulderOffset, shoulderHeight, 0f) * weight;
            orbitCamera.DistanceScale = Mathf.Lerp(1f, aimDistanceScale, weight);
        }
        if (fly != null)
        {
            fly.SpeedMultiplier = Mathf.Lerp(1f, aimSpeedMultiplier, weight);
            fly.FovMultiplier = Mathf.Lerp(1f, aimFovMultiplier, weight);
        }
        // Not blended: spitting is allowed, and aims at the crosshair point, the moment the button is
        // held; the crosshair aim is correct at any shoulder offset.
        if (spit != null)
            spit.IsAiming = IsAiming;
    }
}
