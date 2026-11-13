using UnityEngine;
using UnityEngine.SceneManagement;

// Hands the role chosen in the lobby to the Kitchen: the player who took the human role walks in as
// the old man, everyone else flies.
// It hooks the scene load itself instead of living on a GameObject, so the Kitchen needs no setup and
// no reference to the lobby - a scene played straight from the Editor simply never hears from it and
// keeps working the way it always has.
// Both rigs stay in the world; only their controls and camera move. Turning the body the player is
// not using off would delete the other side of the game from the scene.
public static class KitchenRoles
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Install()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Entering the Kitchen from anywhere but a lobby leaves the scene exactly as it was authored.
        if (scene.name != GameSceneManager.KitchenScene || !GameSession.StartedFromLobby)
        {
            return;
        }

        FlyController fly = Object.FindAnyObjectByType<FlyController>(FindObjectsInactive.Include);
        OldPersonFPSController oldPerson = Object.FindAnyObjectByType<OldPersonFPSController>(FindObjectsInactive.Include);
        bool playingHuman = GameSession.LocalRole == PlayerRole.Human;

        if (fly == null || oldPerson == null)
        {
            // Worth saying out loud: the player would otherwise stand in a kitchen with no controls and
            // no idea why.
            Debug.LogWarning($"{nameof(KitchenRoles)}: the Kitchen needs both a {nameof(FlyController)} and an {nameof(OldPersonFPSController)} to hand out roles; it has " +
                             $"{(fly == null ? "no fly" : "a fly")} and {(oldPerson == null ? "no old man" : "an old man")}.");
        }

        Bind(fly != null ? fly.gameObject : null, !playingHuman);
        Bind(oldPerson != null ? oldPerson.gameObject : null, playingHuman);
    }

    // Gives one rig the player's hands and eyes, or takes them away.
    static void Bind(GameObject rig, bool played)
    {
        if (rig == null)
        {
            return;
        }

        // The controllers, the orbit camera and the aiming all read input every frame; two rigs
        // listening at once would have the fly and the old man move together.
        foreach (MonoBehaviour behaviour in rig.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour is FlyController || behaviour is OldPersonFPSController || behaviour is OrbitCamera || behaviour is AimShootsystem)
            {
                behaviour.enabled = played;
            }
        }

        // Two enabled cameras render over each other, and two audio listeners make Unity complain
        // every frame.
        foreach (Camera camera in rig.GetComponentsInChildren<Camera>(true))
        {
            camera.enabled = played;
            AudioListener listener = camera.GetComponent<AudioListener>();
            if (listener != null)
            {
                listener.enabled = played;
            }
        }
    }
}
