using Unity.Netcode;
using UnityEngine;

// Makes a spawned body playable only on the machine of the player who owns it.
// Every machine holds a copy of every player's body. The controllers, action scripts, cameras, audio
// listeners and HUD all read this machine's input or draw to this machine's screen, so on any copy
// that belongs to somebody else they are switched off and the body is moved by the network instead.
// They are switched off here rather than saved off in the prefab because the same prefabs sit in the
// offline Kitchen, where no network spawn ever happens and they have to keep working as they are.
// The list is filled by Tools > Kitchen Online > Wire Network Players and can be edited in the Inspector.
public class OwnerOnlyControls : NetworkBehaviour
{
    [Tooltip("Switched off on every copy of this body that another player owns: controllers, action scripts, cameras, audio listeners, HUD canvases.")]
    [SerializeField] Behaviour[] ownerOnly = new Behaviour[0];

    // Runs in the same frame the body is instantiated, before any Start, so another player's copy
    // never gets to run its ground probe or build its first-person body.
    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            return;
        }

        // OldPersonFPSController, OrbitCamera and HideCursorWhileEnabled free the cursor in OnDisable.
        // Turning off someone else's copy must not free this player's mouse in the middle of a game.
        CursorLockMode lockState = Cursor.lockState;
        bool cursorVisible = Cursor.visible;

        foreach (Behaviour behaviour in ownerOnly)
        {
            if (behaviour != null)
            {
                behaviour.enabled = false;
            }
        }

        Cursor.lockState = lockState;
        Cursor.visible = cursorVisible;
    }
}
