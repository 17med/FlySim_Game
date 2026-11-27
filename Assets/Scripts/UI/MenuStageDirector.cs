using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Stages the 3D set behind the main menu and the lobby, in the style of a Dead by Daylight lobby.
// Before a room: one fly - this player - dances on the dining table and the camera looks at it from the front.
// In a room: one fly per flyer sits on the table, the old man stands in the doorway watching it, and
// the camera goes where the local player's role puts it - over the old man's shoulder for the human,
// down at table level facing away from the door for a flyer, who must never see him.
// The state is worked out from LobbyManager every frame instead of being kept up to date from its
// events: a joining client loads MainMenu again during Netcode's scene sync, and a director that only
// listened would come up showing the wrong thing. It also lays the flies out: the four dancers in
// the middle of the table, and in the lobby however many flyers there are, spread evenly about the middle
// of the table, sliding over when one joins or leaves. The references are filled in by
// Tools > Main Menu > Build Lobby Stage.
public class MenuStageDirector : MonoBehaviour
{
    // What the stage shows. Two of these are equal when they would put the same thing on screen.
    public readonly struct StageState : IEquatable<StageState>
    {
        public readonly bool InRoom;
        public readonly bool LocalIsHuman;
        public readonly int FlyerCount;

        StageState(bool inRoom, bool localIsHuman, int flyerCount)
        {
            InRoom = inRoom;
            LocalIsHuman = localIsHuman;
            FlyerCount = flyerCount;
        }

        // No room yet: one fly dances, and it stands for this player. Four dancers before anyone has
        // joined anything read as players who are not there.
        public static StageState Menu => new StageState(false, false, MenuDancers);

        public static StageState Room(bool localIsHuman, int flyerCount) =>
            new StageState(true, localIsHuman, Mathf.Clamp(flyerCount, 0, LobbyManager.MaxFlyers));

        public bool Equals(StageState other) =>
            InRoom == other.InRoom && LocalIsHuman == other.LocalIsHuman && FlyerCount == other.FlyerCount;

        public override bool Equals(object other) => other is StageState state && Equals(state);

        public override int GetHashCode() => HashCode.Combine(InRoom, LocalIsHuman, FlyerCount);
    }

    const int MenuDancers = 1;

    [Header("Camera")]
    [Tooltip("The menu's own camera; it is moved between the three views below.")]
    public Camera stageCamera;
    [Tooltip("Before a room: in front of the dancing fly.")]
    public Transform menuView;
    [Tooltip("In a room, as a flyer: at table level, facing away from the door.")]
    public Transform flyLobbyView;
    [Tooltip("In a room, as the human: over the old man's shoulder, looking at the table.")]
    public Transform humanLobbyView;
    [Tooltip("Seconds the camera takes to move from one view to the next.")]
    public float blendSeconds = 1.4f;

    [Header("Cast")]
    [Tooltip("In the order they take a seat: with N flyers in the room, the first N sit and the rest are hidden.")]
    public MenuFlyDance[] flies = new MenuFlyDance[LobbyManager.MaxFlyers];
    [Tooltip("The old man in the doorway: hidden before a room.")]
    public GameObject oldMan;
    [Tooltip("The layer the old man is on. The fly view leaves it out of the camera, so no camera move can show him.")]
    public int oldManLayer = 6;

    [Header("Seating")]
    [Tooltip("The middle of the line-up on the tabletop, in the space of the flies' parent.")]
    public Vector3 seatCentre;
    [Tooltip("Metres between neighbouring flies dancing before a room.")]
    public float menuSpacing = 0.12f;
    [Tooltip("Metres between neighbouring flies sitting in the lobby. Tighter than the menu's, so four still fit between the lobby's side panels in the fly view.")]
    public float lobbySpacing = 0.10f;
    [Tooltip("How far a full line-up's outer flies stand forward (+z) of its middle.")]
    public float arcDepth = 0.05f;
    [Tooltip("Seconds a fly takes to slide to its new seat when the number of flyers changes.")]
    public float slideSeconds = 0.45f;

    bool hasShownState;
    StageState shownState;
    // The camera blend in progress; no target means the camera is at rest on its view.
    Transform blendTarget;
    Vector3 blendFromPosition;
    Quaternion blendFromRotation;
    float blendElapsed;
    // Per fly, by index in flies: where it is sliding to, and SmoothDamp's running speed.
    Vector3[] seatTargets = Array.Empty<Vector3>();
    Vector3[] slideVelocities = Array.Empty<Vector3>();
    readonly List<int> lineUp = new List<int>();

    void Update()
    {
        if (TryReadState(out StageState state))
        {
            // The first state is snapped to, so a scene loaded straight into a room (a joining
            // client's scene sync) does not open on a camera move or flies blending out of a dance.
            if (!hasShownState)
            {
                ShowImmediate(state);
            }
            else if (!state.Equals(shownState))
            {
                Show(state);
            }
        }
        StepCameraBlend();
        StepSeatSlide();
    }

    // False while the answer is not known yet: connected, or connecting, but the lobby has not
    // spawned or its roster has not arrived. LocalRole says Flyer until then whatever the role really
    // is, so showing the fly view on its word could flash it at the human. What is on screen is held.
    static bool TryReadState(out StageState state)
    {
        LobbyManager lobby = LobbyManager.Instance;
        if (lobby != null && lobby.MemberCount > 0)
        {
            state = StageState.Room(lobby.LocalIsHuman, lobby.FlyerCount);
            return true;
        }
        state = StageState.Menu;
        NetworkManager network = NetworkManager.Singleton;
        return network == null || !network.IsListening;
    }

    // Puts the stage straight into a state with no camera move and no blending: on the first frame,
    // and from Tools > Main Menu > Build Lobby Stage, which uses it in Edit mode for its pictures.
    public void ShowImmediate(StageState state)
    {
        int seated = state.FlyerCount;
        MenuFlyDance.Mode mode = ModeFor(state);
        for (int i = 0; i < flies.Length; i++)
        {
            if (SetFlyVisible(i, i < seated))
            {
                flies[i].SetModeImmediate(mode);
            }
        }
        AssignSeats(state);
        for (int i = 0; i < flies.Length; i++)
        {
            SnapToSeat(i);
        }
        SetOldManVisible(state.InRoom);
        CullOldMan(state);
        SnapCameraTo(ViewFor(state));
        shownState = state;
        hasShownState = true;
    }

    // The same change, played out: the camera glides to its new view, and flies already on the table
    // blend into their new pose and slide to their new seat. A fly that only now appears snaps to its
    // pose and seat, as it was not on screen to move from.
    void Show(StageState state)
    {
        int seated = state.FlyerCount;
        MenuFlyDance.Mode mode = ModeFor(state);
        var wasShowing = new bool[flies.Length];
        for (int i = 0; i < flies.Length; i++)
        {
            wasShowing[i] = flies[i] != null && flies[i].gameObject.activeSelf;
            if (!SetFlyVisible(i, i < seated))
            {
                continue;
            }
            if (wasShowing[i])
            {
                flies[i].SetMode(mode);
            }
            else
            {
                flies[i].SetModeImmediate(mode);
            }
        }
        AssignSeats(state);
        for (int i = 0; i < flies.Length; i++)
        {
            // Appearing flies take their seat at once; the ones already on the table slide over.
            if (flies[i] != null && !wasShowing[i])
            {
                SnapToSeat(i);
            }
        }
        SetOldManVisible(state.InRoom);
        // Ahead of the move, not after it: the camera passes the old man on its way down to the table.
        CullOldMan(state);
        BlendCameraTo(ViewFor(state));
        shownState = state;
    }

    // ---- the cast ------------------------------------------------------------------------------

    static MenuFlyDance.Mode ModeFor(StageState state) =>
        state.InRoom ? MenuFlyDance.Mode.Sit : MenuFlyDance.Mode.Dance;

    // Returns whether the fly is there and now showing, so the caller can set its pose.
    bool SetFlyVisible(int index, bool visible)
    {
        MenuFlyDance fly = flies[index];
        if (fly == null)
        {
            return false;
        }
        if (fly.gameObject.activeSelf != visible)
        {
            fly.gameObject.SetActive(visible);
        }
        return visible;
    }

    void SetOldManVisible(bool visible)
    {
        if (oldMan != null && oldMan.activeSelf != visible)
        {
            oldMan.SetActive(visible);
        }
    }

    // A flyer must not see the old man in the lobby. Hiding him from the camera rather than from the
    // scene keeps him standing for anyone else, and keeps his idle animation running.
    void CullOldMan(StageState state)
    {
        if (stageCamera == null || oldManLayer < 0 || oldManLayer > 31)
        {
            return;
        }
        int bit = 1 << oldManLayer;
        bool isFlyView = state.InRoom && !state.LocalIsHuman;
        stageCamera.cullingMask = isFlyView ? stageCamera.cullingMask & ~bit : stageCamera.cullingMask | bit;
    }

    // ---- the seats -----------------------------------------------------------------------------

    // Where seat `rank` of a line-up of `count` sits from the middle of the line-up: evenly spaced and
    // symmetric about it, the outer ones brought forward along a shallow arc. The arc is shaped for a
    // full line-up, so a shorter one is the middle of the same curve. Public so the builder places the
    // dancers by the same rule the director keeps them to.
    public static Vector3 SeatOffset(int rank, int count, float spacing, float arcDepth)
    {
        float fromMiddle = rank - (count - 1) * 0.5f;
        float arc = fromMiddle / ((LobbyManager.MaxFlyers - 1) * 0.5f);
        return new Vector3(fromMiddle * spacing, 0f, arcDepth * arc * arc);
    }

    // The flies on show are seated left to right in the order they already stand, so nobody walks
    // through a neighbour on the way to a new seat.
    void AssignSeats(StageState state)
    {
        if (seatTargets.Length != flies.Length)
        {
            seatTargets = new Vector3[flies.Length];
            slideVelocities = new Vector3[flies.Length];
        }
        lineUp.Clear();
        for (int i = 0; i < flies.Length && i < state.FlyerCount; i++)
        {
            if (flies[i] != null)
            {
                lineUp.Add(i);
            }
        }
        lineUp.Sort((a, b) => flies[a].transform.localPosition.x.CompareTo(flies[b].transform.localPosition.x));
        float spacing = state.InRoom ? lobbySpacing : menuSpacing;
        for (int rank = 0; rank < lineUp.Count; rank++)
        {
            seatTargets[lineUp[rank]] = seatCentre + SeatOffset(rank, lineUp.Count, spacing, arcDepth);
        }
    }

    void SnapToSeat(int index)
    {
        MenuFlyDance fly = flies[index];
        if (fly == null || !fly.gameObject.activeSelf)
        {
            return;
        }
        fly.transform.localPosition = seatTargets[index];
        slideVelocities[index] = Vector3.zero;
    }

    // Only the root moves: MenuFlyDance poses everything under it and stands the fly on the root.
    void StepSeatSlide()
    {
        for (int i = 0; i < flies.Length && i < seatTargets.Length; i++)
        {
            MenuFlyDance fly = flies[i];
            if (fly == null || !fly.gameObject.activeSelf)
            {
                continue;
            }
            Transform root = fly.transform;
            root.localPosition = Vector3.SmoothDamp(root.localPosition, seatTargets[i], ref slideVelocities[i], slideSeconds, Mathf.Infinity, Time.unscaledDeltaTime);
        }
    }

    // ---- the camera ----------------------------------------------------------------------------

    Transform ViewFor(StageState state)
    {
        if (!state.InRoom)
        {
            return menuView;
        }
        return state.LocalIsHuman ? humanLobbyView : flyLobbyView;
    }

    void SnapCameraTo(Transform view)
    {
        blendTarget = null;
        if (stageCamera == null || view == null)
        {
            return;
        }
        stageCamera.transform.SetPositionAndRotation(view.position, view.rotation);
    }

    // Starts from wherever the camera is, so a change in the middle of a move turns it smoothly.
    void BlendCameraTo(Transform view)
    {
        if (stageCamera == null || view == null)
        {
            blendTarget = null;
            return;
        }
        blendTarget = view;
        blendFromPosition = stageCamera.transform.position;
        blendFromRotation = stageCamera.transform.rotation;
        blendElapsed = 0f;
    }

    // Unscaled: the menu can come back from a match with the game still paused.
    void StepCameraBlend()
    {
        if (blendTarget == null || stageCamera == null)
        {
            return;
        }
        blendElapsed += Time.unscaledDeltaTime;
        float progress = blendSeconds > 0f ? Mathf.Clamp01(blendElapsed / blendSeconds) : 1f;
        // Eased at both ends, so the camera does not lurch into motion or stop dead.
        float eased = Mathf.SmoothStep(0f, 1f, progress);
        stageCamera.transform.SetPositionAndRotation(
            Vector3.Lerp(blendFromPosition, blendTarget.position, eased),
            Quaternion.Slerp(blendFromRotation, blendTarget.rotation, eased));
        if (progress >= 1f)
        {
            blendTarget = null;
        }
    }
}
