using UnityEngine;
using UnityEngine.InputSystem;

// Toggles between two sets of GameObjects: only one set is active at a time.
public class Swaping : MonoBehaviour
{
    [Header("Objects active in state 1")]
    public GameObject[] stateOneObjects = new GameObject[2];

    [Header("Objects active in state 2")]
    public GameObject[] stateTwoObjects = new GameObject[2];

    [Tooltip("Which state the scene starts in")]
    public bool startInStateOne = true;

    bool isStateOne;

    void Start()
    {
        ApplyState(startInStateOne);
    }

    void Update()
    {
        // The project uses the new Input System only, so the legacy Input.GetKeyDown would throw.
        // Keyboard.current is null when no keyboard is connected; skip input in that case.
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null)
            return;

        if (keyboard.cKey.wasPressedThisFrame)
            ApplyState(!isStateOne);
    }

    void ApplyState(bool stateOne)
    {
        isStateOne = stateOne;
        SetActive(stateOneObjects, stateOne);
        SetActive(stateTwoObjects, !stateOne);
    }

    static void SetActive(GameObject[] objects, bool active)
    {
        foreach (GameObject obj in objects)
        {
            if (obj != null)
                obj.SetActive(active);
        }
    }
}
