using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

// Turns the mouse pointer into a fly for the main menu: the system arrow is hidden and this
// RectTransform chases the pointer wearing the fly icon, buzzing its wings, banking into its turns
// and hovering on the spot when the mouse stops.
// The fly is deliberately a step behind the pointer - that lag is what makes it read as a fly chasing
// the mouse instead of a picture glued to it - but Follow Lag is short enough that the fly still sits
// over whatever is about to be clicked.
// It belongs on its own Canvas above UI_Root with no GraphicRaycaster, so it draws over every panel
// and can never swallow a click; Tools > Main Menu > Build Fly Cursor builds that rig.
// Reads the pointer through the Input System because the project is set to the new input handler only,
// where UnityEngine.Input would throw.
[RequireComponent(typeof(RectTransform))]
public class MenuFlyCursor : MonoBehaviour
{
    [Header("Follow")]
    // The rect the pointer is measured against - the Canvas the fly lives on.
    [SerializeField] RectTransform canvasRect;
    // Seconds of lag behind the pointer. Longer looks more like flight and less like a cursor.
    [SerializeField, Range(0.01f, 0.3f)] float followLag = 0.05f;

    [Header("Hover")]
    // Canvas units the fly drifts while the mouse is still, and how fast it drifts.
    [SerializeField] float hoverAmplitude = 7f;
    [SerializeField] float hoverSpeed = 4.5f;
    // Pointer speed (canvas units a second) under which the fly is treated as hovering.
    [SerializeField] float idleSpeed = 120f;

    [Header("Wings")]
    // Flaps a second, and how far the body squashes with each flap. The squash is on one axis and
    // its opposite on the other, so the fly keeps its area and only ever looks like it is beating.
    // 12 is about the ceiling that still reads as a beat at 60fps; higher strobes instead of flapping.
    [SerializeField] float buzzFrequency = 12f;
    [SerializeField, Range(0f, 0.3f)] float buzzAmount = 0.09f;
    // Small roll on the buzz, at half the flap rate so it does not read as a vibration.
    [SerializeField] float buzzTilt = 4f;

    [Header("Turning")]
    // Above this pointer speed the fly turns to face where it is going; below it it rights itself.
    [SerializeField] float turnSpeedThreshold = 200f;
    [SerializeField] float turnSharpness = 9f;
    [SerializeField] float uprightSharpness = 5f;

    [Header("Click")]
    // The fly tucks in when a button goes down, then springs back.
    [SerializeField, Range(0f, 0.5f)] float clickSquash = 0.22f;
    [SerializeField] float clickRecoverSeconds = 0.16f;

    RectTransform rect;
    Vector2 followPosition;
    Vector2 followVelocity;
    float angle;
    float buzzTime;
    float hoverTime;
    float idleBlend;
    float clickPunch;
    bool flyShown = true;

    void Awake()
    {
        rect = (RectTransform)transform;
        if (canvasRect == null)
        {
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                canvasRect = (RectTransform)canvas.transform;
            }
        }
        // Belt and braces: a cursor that eats its own clicks would make every button dead.
        Graphic graphic = GetComponent<Graphic>();
        if (graphic != null)
        {
            graphic.raycastTarget = false;
        }
    }

    void OnEnable()
    {
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = false;
        // Starts under the pointer instead of flying in from the middle of the screen.
        if (TryReadPointer(out Vector2 pointer))
        {
            followPosition = pointer;
            followVelocity = Vector2.zero;
            rect.anchoredPosition = pointer;
        }
    }

    // Leaving the menu (the Kitchen loading over it, or this rig being switched off) has to give the
    // arrow back, or the player is left with no pointer at all.
    void OnDisable()
    {
        Cursor.visible = true;
    }

    void Update()
    {
        // Unscaled so the fly keeps flying if the menu ever pauses the game behind it.
        float deltaTime = Time.unscaledDeltaTime;
        if (deltaTime <= 0f)
        {
            return;
        }
        // No mouse (pad only, or the device was unplugged): hide rather than freeze mid-screen.
        if (!TryReadPointer(out Vector2 pointer))
        {
            SetVisible(false);
            return;
        }
        SetVisible(true);

        Vector2 previous = followPosition;
        followPosition = Vector2.SmoothDamp(followPosition, pointer, ref followVelocity, followLag, Mathf.Infinity, deltaTime);
        Vector2 travel = (followPosition - previous) / deltaTime;
        float speed = travel.magnitude;

        // Fades the hover drift in as the pointer settles, so it never fights a real mouse move.
        float idleTarget = speed < idleSpeed ? 1f : 0f;
        idleBlend = Mathf.MoveTowards(idleBlend, idleTarget, deltaTime * 3f);
        hoverTime += deltaTime;
        // Different rates on the two axes trace a slow figure of eight instead of a straight bounce.
        Vector2 hover = new Vector2(
            Mathf.Sin(hoverTime * hoverSpeed * 0.7f) * hoverAmplitude * 0.6f,
            Mathf.Sin(hoverTime * hoverSpeed) * hoverAmplitude);
        rect.anchoredPosition = followPosition + hover * idleBlend;

        if (speed > turnSpeedThreshold)
        {
            // The icon is drawn head up, so its forward is +Y and the angle is measured from there.
            float travelAngle = Mathf.Atan2(travel.y, travel.x) * Mathf.Rad2Deg - 90f;
            angle = Mathf.LerpAngle(angle, travelAngle, 1f - Mathf.Exp(-turnSharpness * deltaTime));
        }
        else
        {
            angle = Mathf.LerpAngle(angle, 0f, 1f - Mathf.Exp(-uprightSharpness * deltaTime));
        }

        buzzTime += deltaTime;
        float flap = Mathf.Sin(buzzTime * buzzFrequency * Mathf.PI * 2f);
        rect.localRotation = Quaternion.Euler(0f, 0f, angle + Mathf.Sin(buzzTime * buzzFrequency * Mathf.PI) * buzzTilt);

        if (Mouse.current.leftButton.wasPressedThisFrame || Mouse.current.rightButton.wasPressedThisFrame)
        {
            clickPunch = 1f;
        }
        clickPunch = Mathf.MoveTowards(clickPunch, 0f, deltaTime / Mathf.Max(clickRecoverSeconds, 0.01f));

        float wing = 1f + flap * buzzAmount;
        float punch = 1f - clickPunch * clickSquash;
        rect.localScale = new Vector3(wing * punch, (2f - wing) * punch, 1f);
    }

    bool TryReadPointer(out Vector2 canvasPoint)
    {
        canvasPoint = Vector2.zero;
        if (Mouse.current == null || canvasRect == null)
        {
            return false;
        }
        // Camera is null on purpose: the cursor canvas is Screen Space - Overlay.
        return RectTransformUtility.ScreenPointToLocalPointInRectangle(
            canvasRect, Mouse.current.position.ReadValue(), null, out canvasPoint);
    }

    void SetVisible(bool visible)
    {
        if (flyShown == visible)
        {
            return;
        }
        flyShown = visible;
        // Hides by scale, so the GameObject stays enabled and this Update keeps watching for a mouse.
        rect.localScale = visible ? Vector3.one : Vector3.zero;
        // With no fly to point with, the system arrow has to come back.
        Cursor.visible = !visible;
    }
}
