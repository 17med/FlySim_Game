using UnityEngine;
using UnityEngine.InputSystem;

// Sets the Animator int "emotnb" while an emote key is held: 1, 2 or 3 for keys 1, 2, 3, and 0 when
// none is held, so releasing the key ends the emote. If several are held, the lowest number wins.
public class FlyEmotes : MonoBehaviour
{
    // Project-wide actions (InputSystem_Actions); index + 1 is the emote number.
    static readonly string[] EmoteActionNames = { "Player/Emote1", "Player/Emote2", "Player/Emote3" };
    static readonly int EmoteParameter = Animator.StringToHash("emotnb");

    [Tooltip("The fly model's Animator (with the int parameter \"emotnb\"). Found automatically if left empty.")]
    [SerializeField] Animator animator;

    InputAction[] emoteActions;

    void Awake()
    {
        emoteActions = new InputAction[EmoteActionNames.Length];
        // Throws if an action is missing: a key that silently does nothing is worse than a clear error.
        for (int i = 0; i < EmoteActionNames.Length; i++)
            emoteActions[i] = InputSystem.actions.FindAction(EmoteActionNames[i], throwIfNotFound: true);

        if (animator == null)
            animator = GetComponentInChildren<Animator>();
        if (animator == null)
        {
            // Fails open: emotes are cosmetic, so only this component stops; the fly stays controllable.
            Debug.LogWarning("FlyEmotes: no Animator found on the fly model; emotes disabled.", this);
            enabled = false;
        }
    }

    void OnEnable()
    {
        foreach (InputAction action in emoteActions)
            action.Enable();
    }

    void Update()
    {
        animator.SetInteger(EmoteParameter, HeldEmote());
    }

    int HeldEmote()
    {
        for (int i = 0; i < emoteActions.Length; i++)
        {
            if (emoteActions[i].IsPressed())
                return i + 1;
        }
        return 0;
    }
}
