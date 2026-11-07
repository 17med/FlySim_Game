using UnityEngine;

// Hides the OS cursor for as long as this component is enabled, and gives it back when the
// component is disabled or its GameObject is destroyed (scene unload included).
public class HideCursorWhileEnabled : MonoBehaviour
{
    [Tooltip("Also lock the cursor to the centre of the window (FPS style).")]
    [SerializeField] bool lockCursor = false;

    void OnEnable()
    {
        ApplyHidden();
    }

    void OnDisable()
    {
        Cursor.visible = true;
        // Only undo the lock if we applied it, so we don't unlock a cursor that another
        // script (OldPersonFPSController, OrbitCamera) locked on its own.
        if (lockCursor)
        {
            Cursor.lockState = CursorLockMode.None;
        }
    }

    // Alt-tabbing away and back makes the OS show the cursor again, so re-apply on refocus.
    void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus && isActiveAndEnabled)
        {
            ApplyHidden();
        }
    }

    void ApplyHidden()
    {
        Cursor.visible = false;
        if (lockCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
        }
    }
}
