using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

// The game the kitchen is playing, and the only thing that decides who won it.
// The flies are after the food and the old man is defending it: clear every dish before the clock
// runs out and the flies win, keep so much as a mouthful on a plate until it does and the human wins.
//
// The length comes from the host's settings (ten minutes at the shortest), handed over by the lobby.
// It installs itself when a kitchen scene loads, the way PauseMenu does, so neither Kitchen nor
// Kitchen_online needs anything in the scene and a scene played straight from the Editor still gets
// a match. MatchNetwork, when the scene has one, makes the host's clock and verdict the room's.
public class MatchDirector : MonoBehaviour
{
    const string PlayerActionMap = "Player";
    // The last stretch, in seconds: the clock turns red and the music changes to match.
    public const float FinalMinuteSeconds = 60f;
    // How long the result stays on screen before everyone is taken back to the menu.
    const float ResultSeconds = 9f;
    // How often the kitchen is swept for dishes while there are none to be found.
    const float DishSearchInterval = 1f;

    public enum Phase { Running, FliesWon, HumanWon }

    public static MatchDirector Instance { get; private set; }

    // Every dish in the kitchen. Kept here rather than searched for per fly: FlyEating asks it a few
    // times a second, once per fly.
    static readonly List<FoodDish> dishes = new List<FoodDish>();
    static float dishSearchAt;

    public static IReadOnlyList<FoodDish> Dishes => dishes;

    public Phase CurrentPhase { get; private set; } = Phase.Running;

    public bool IsOver => CurrentPhase != Phase.Running;

    // Seconds still to play. Zero once the clock has run out.
    public float SecondsLeft { get; private set; }

    // How much of the kitchen's food is still on its plates, 0 to 1. What the HUD's bar shows.
    public float FoodLeft { get; private set; } = 1f;

    public bool IsFinalMinute => !IsOver && SecondsLeft <= FinalMinuteSeconds;

    // True for the player at this machine when the side they are on won.
    public bool LocalPlayerWon =>
        CurrentPhase == Phase.FliesWon ? GameSession.LocalRole == PlayerRole.Flyer
        : CurrentPhase == Phase.HumanWon && GameSession.LocalRole == PlayerRole.Human;

    MatchNetwork network;
    InputActionMap playerActions;
    float resultLeft;
    // The clock this machine counts down on its own. In a room MatchNetwork's is used instead, so the
    // players do not drift apart over ten minutes; this is the fallback for a scene played on its own.
    float localSecondsLeft;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Install()
    {
        SceneManager.sceneLoaded += (scene, _) =>
        {
            if (scene.name != GameSceneManager.KitchenScene && scene.name != GameSceneManager.KitchenOnlineScene)
            {
                dishes.Clear();
                return;
            }
            if (FindAnyObjectByType<MatchDirector>() == null)
            {
                new GameObject(nameof(MatchDirector)).AddComponent<MatchDirector>();
            }
        };
    }

    void Awake()
    {
        Instance = this;
        playerActions = InputSystem.actions != null ? InputSystem.actions.FindActionMap(PlayerActionMap) : null;
        CollectDishes();
        localSecondsLeft = Mathf.Max(GameSession.MatchMinutes, GameSettings.MinMatchMinutes) * 60f;
        SecondsLeft = localSecondsLeft;
        // The HUD reads this director through MatchDirector.Instance, so it needs nothing passed to it.
        gameObject.AddComponent<MatchHud>();
    }

    void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
        // Leaving the scene with the result up must not leave the next one without input.
        if (IsOver)
        {
            playerActions?.Enable();
        }
    }

    void Update()
    {
        // The Match object is spawned by Netcode a moment after the scene loads, so it is picked up
        // here rather than once in Awake.
        if (network == null)
        {
            network = MatchNetwork.Instance;
        }

        if (IsOver)
        {
            CountResultDown();
            return;
        }

        Tick();
        FoodLeft = MeasureFood(out bool allEmpty);
        Judge(allEmpty);
    }

    // How long is left to play. The room's clock when there is one, so everybody's last minute is the
    // same minute; this machine's own otherwise.
    void Tick()
    {
        localSecondsLeft = Mathf.Max(0f, localSecondsLeft - Time.deltaTime);
        SecondsLeft = network != null ? network.SecondsLeft : localSecondsLeft;
    }

    // The share of the kitchen's food still on its plates. A kitchen with no dish at all counts as
    // full, so a scene that has not been wired yet plays out the clock instead of ending at once.
    float MeasureFood(out bool allEmpty)
    {
        CollectDishes();
        if (dishes.Count == 0)
        {
            allEmpty = false;
            return 1f;
        }

        float total = 0f;
        allEmpty = true;
        foreach (FoodDish dish in dishes)
        {
            total += dish.Fill;
            if (!dish.IsEmpty)
            {
                allEmpty = false;
            }
        }
        return total / dishes.Count;
    }

    void Judge(bool allEmpty)
    {
        // In a room only the host judges, and tells everyone: two machines that disagreed about who
        // won would each show their own player a different ending.
        if (network != null && !network.IsServer)
        {
            if (network.Outcome != Phase.Running)
            {
                End(network.Outcome);
            }
            return;
        }

        Phase verdict = allEmpty ? Phase.FliesWon : SecondsLeft <= 0f ? Phase.HumanWon : Phase.Running;
        if (verdict == Phase.Running)
        {
            return;
        }
        network?.Declare(verdict);
        End(verdict);
    }

    void End(Phase verdict)
    {
        CurrentPhase = verdict;
        resultLeft = ResultSeconds;
        // The match is over: nobody steers, nobody eats, and the cursor comes back for the menu that
        // is about to load.
        playerActions?.Disable();
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;
    }

    void CountResultDown()
    {
        resultLeft -= Time.unscaledDeltaTime;
        if (resultLeft > 0f)
        {
            return;
        }
        // Only once: the load takes a frame or two and this runs every frame until the scene changes.
        resultLeft = float.MaxValue;
        LeaveToMenu();
    }

    void LeaveToMenu()
    {
        playerActions?.Enable();
        Time.timeScale = 1f;
        if (NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening)
        {
            NetworkManager.Singleton.Shutdown();
        }
        GameSession.Reset();
        GameSceneManager.Instance.LoadMainMenu();
    }

    // Refreshed rather than found once: the dishes are in the scene from the start, but a reloaded
    // scene brings new ones and a destroyed dish leaves a hole behind.
    // Throttled, because a kitchen with no dish at all would otherwise sweep the whole scene every
    // frame looking for one that is not there.
    static void CollectDishes()
    {
        bool stale = dishes.Count == 0;
        foreach (FoodDish dish in dishes)
        {
            if (dish == null)
            {
                stale = true;
                break;
            }
        }
        if (!stale || Time.unscaledTime < dishSearchAt)
        {
            return;
        }
        dishSearchAt = Time.unscaledTime + DishSearchInterval;
        dishes.Clear();
        dishes.AddRange(FindObjectsByType<FoodDish>(FindObjectsInactive.Exclude));
    }

    // "09:58". Minutes and seconds, which is what a clock on a HUD should read.
    public static string Clock(float seconds)
    {
        int whole = Mathf.Max(0, Mathf.CeilToInt(seconds));
        return $"{whole / 60:00}:{whole % 60:00}";
    }
}
