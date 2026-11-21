using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

// One player's seat in the lobby, as everyone sees it. A NetworkList carries it, so it must be
// unmanaged and comparable: the list uses equality to work out which rows changed.
public struct LobbyMember : INetworkSerializable, IEquatable<LobbyMember>
{
    public ulong ClientId;
    public FixedString32Bytes Name;
    public PlayerRole Role;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref ClientId);
        serializer.SerializeValue(ref Name);
        // Sent as a byte rather than as an enum, so the wire format does not depend on what the
        // compiler picked for the enum's underlying type.
        byte role = (byte)Role;
        serializer.SerializeValue(ref role);
        Role = (PlayerRole)role;
    }

    public bool Equals(LobbyMember other)
    {
        return ClientId == other.ClientId && Role == other.Role && Name.Equals(other.Name);
    }
}

// The room behind the main menu's lobby panel: who is in it, who holds which role, and who may start.
// Lives on a MainMenu scene object with a NetworkObject, so the host has it as soon as the room opens.
// The server owns every decision - roles, swaps, the start - and clients only ask through RPCs, so a
// modified client cannot hand itself the swatter.
// LobbyUI draws what this exposes; LocalNetworkConnector gets the connection up first.
[RequireComponent(typeof(NetworkObject))]
public class LobbyManager : NetworkBehaviour
{
    // The flyer slots the lobby panel has room for (Flyer_Slot_1 to Flyer_Slot_4).
    public const int MaxFlyers = 4;
    // Nobody is left to answer a prompt the other player walked away from, and one unanswered request
    // would block every later swap, so the server gives up on it after this long.
    const float SwapRequestTimeout = 20f;

    public static LobbyManager Instance { get; private set; }

    [Tooltip("Flyers needed before the host can start. One human plus this many flyers is a full match; the lobby panel's Min_Note label states it too.")]
    [SerializeField] int minFlyersToStart = 1;

    // The roster, in join order. Server writes, everyone reads.
    readonly NetworkList<LobbyMember> members = new NetworkList<LobbyMember>();

    // How long the match will run for, in minutes. The host's own settings value: every player has
    // their own, and a room has to agree on one, so the host's is the one the room plays and everyone
    // else is shown it in the room bar. Server writes, everyone reads.
    readonly NetworkVariable<int> matchMinutes =
        new NetworkVariable<int>(GameSettings.DefaultMatchMinutes, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Server only: names that arrived before their sender had a seat. A joining client sends its name
    // from OnNetworkSpawn, while it is still being synchronised, and the server only seats it once
    // OnClientConnectedCallback fires, which can be after that message has been handled. Without this,
    // the name was dropped and the row read "WAITING…" for the whole lobby. Seat takes it from here.
    readonly Dictionary<ulong, FixedString32Bytes> namesBeforeSeat = new Dictionary<ulong, FixedString32Bytes>();

    // Raised on every machine whenever the roster changes, including the first fill on join.
    public event Action RosterChanged;
    // Raised on the machine being asked to swap: who is asking, their name, and whether saying yes
    // makes this player the human.
    public event Action<ulong, string, bool> SwapRequested;
    // Raised on the asking machine once the answer is in, and on the asked machine when the request
    // is withdrawn (the other player left, or it timed out).
    public event Action<bool, string> SwapAnswered;

    // Server only: the one request in flight. A second request is refused until this one is settled,
    // which keeps two players from both being promised the human role.
    bool hasPendingRequest;
    ulong pendingRequesterId;
    ulong pendingTargetId;
    float pendingExpiresAt;

    public int MemberCount => members.Count;

    // The length of the match this room will play, in minutes, as the host has it set.
    public int MatchMinutes => matchMinutes.Value;

    public LobbyMember GetMember(int index) => members[index];

    public int FlyerCount
    {
        get
        {
            int count = 0;
            foreach (LobbyMember member in members)
            {
                if (member.Role == PlayerRole.Flyer)
                {
                    count++;
                }
            }
            return count;
        }
    }

    public bool TryGetHuman(out LobbyMember human)
    {
        foreach (LobbyMember member in members)
        {
            if (member.Role == PlayerRole.Human)
            {
                human = member;
                return true;
            }
        }
        human = default;
        return false;
    }

    public bool TryGetMember(ulong clientId, out LobbyMember found)
    {
        foreach (LobbyMember member in members)
        {
            if (member.ClientId == clientId)
            {
                found = member;
                return true;
            }
        }
        found = default;
        return false;
    }

    // The role of the player sitting at this machine, Flyer until the roster says otherwise.
    public PlayerRole LocalRole
    {
        get
        {
            return TryGetMember(NetworkManager.LocalClientId, out LobbyMember member) ? member.Role : PlayerRole.Flyer;
        }
    }

    public bool LocalIsHuman => LocalRole == PlayerRole.Human;

    // Why the start button is off, for the lobby's status line. Empty reason means it is on.
    public bool CanStart(out string reason)
    {
        if (!IsServer)
        {
            reason = "ONLY THE HOST CAN START";
            return false;
        }
        if (!TryGetHuman(out _))
        {
            reason = "NO HUMAN IN THE KITCHEN";
            return false;
        }
        if (FlyerCount < minFlyersToStart)
        {
            reason = minFlyersToStart == 1 ? "WAITING FOR A FLYER" : $"MINIMUM {minFlyersToStart} FLYERS TO START";
            return false;
        }
        reason = "";
        return true;
    }

    public override void OnNetworkSpawn()
    {
        Instance = this;
        members.OnListChanged += OnMembersChanged;
        // The room bar shows the length, so a change to it has to redraw the panel just like a
        // roster change does.
        matchMinutes.OnValueChanged += OnMatchMinutesChanged;

        if (IsServer)
        {
            matchMinutes.Value = GameSettings.MatchMinutes;
            // The host's own client is already connected by the time this object spawns, so seat
            // everyone who is in before subscribing for the ones still to come.
            foreach (ulong clientId in NetworkManager.ConnectedClientsIds)
            {
                Seat(clientId);
            }
            NetworkManager.OnClientConnectedCallback += Seat;
            NetworkManager.OnClientDisconnectCallback += Unseat;
        }

        // Every machine (the host included) tells the server what to call it. The seat is already
        // there with a placeholder name; this fills it in.
        SubmitNameRpc(LocalPlayerName());
        RosterChanged?.Invoke();
    }

    public override void OnNetworkDespawn()
    {
        members.OnListChanged -= OnMembersChanged;
        matchMinutes.OnValueChanged -= OnMatchMinutesChanged;
        if (IsServer && NetworkManager != null)
        {
            NetworkManager.OnClientConnectedCallback -= Seat;
            NetworkManager.OnClientDisconnectCallback -= Unseat;
        }
        if (Instance == this)
        {
            Instance = null;
        }
    }

    void Update()
    {
        if (!IsServer)
        {
            return;
        }
        // The host can open the settings panel with the room already up, so the length is followed
        // rather than read once: the others see it change as it is dragged.
        if (matchMinutes.Value != GameSettings.MatchMinutes)
        {
            matchMinutes.Value = GameSettings.MatchMinutes;
        }
        if (!hasPendingRequest || Time.time < pendingExpiresAt)
        {
            return;
        }
        // Nobody answered: tell both sides so neither is left looking at a prompt or a spinner.
        ulong requesterId = pendingRequesterId;
        ulong targetId = pendingTargetId;
        ClearPendingRequest();
        AnswerSwapResultRpc(false, "NO ANSWER", RpcTarget.Single(requesterId, RpcTargetUse.Temp));
        WithdrawSwapRpc(RpcTarget.Single(targetId, RpcTargetUse.Temp));
    }

    void OnMembersChanged(NetworkListEvent<LobbyMember> change)
    {
        RosterChanged?.Invoke();
    }

    void OnMatchMinutesChanged(int previous, int current)
    {
        RosterChanged?.Invoke();
    }

    // ---- server side seating -------------------------------------------------------------------

    void Seat(ulong clientId)
    {
        if (!IsServer || TryGetMember(clientId, out _))
        {
            return;
        }

        // The first player in - always the host - takes the human role, which is what the lobby's
        // "ASSIGNED TO THE HOST BY DEFAULT" line promises. Everyone else flies.
        PlayerRole role = TryGetHuman(out _) ? PlayerRole.Flyer : PlayerRole.Human;
        if (role == PlayerRole.Flyer && FlyerCount >= MaxFlyers)
        {
            // Refusing here rather than seating a sixth player: the panel has four flyer slots and a
            // player with no slot would stand in a room they cannot see themselves in.
            NetworkManager.DisconnectClient(clientId, "The kitchen is full.");
            return;
        }

        // The name if it already came in (see namesBeforeSeat); otherwise a placeholder that
        // SubmitNameRpc replaces a moment later, which is what the mock-up's empty slot says.
        FixedString32Bytes name = "WAITING…";
        if (namesBeforeSeat.TryGetValue(clientId, out FixedString32Bytes earlyName))
        {
            name = earlyName;
            namesBeforeSeat.Remove(clientId);
        }
        members.Add(new LobbyMember
        {
            ClientId = clientId,
            Name = name,
            Role = role,
        });
    }

    void Unseat(ulong clientId)
    {
        if (!IsServer)
        {
            return;
        }
        // A player who left before being seated must not hand their name to a later client that is
        // given the same id.
        namesBeforeSeat.Remove(clientId);

        // A request involving whoever just left can never be answered.
        if (hasPendingRequest && (pendingRequesterId == clientId || pendingTargetId == clientId))
        {
            ulong other = pendingRequesterId == clientId ? pendingTargetId : pendingRequesterId;
            bool otherWasAsking = pendingTargetId == clientId;
            ClearPendingRequest();
            if (otherWasAsking)
            {
                AnswerSwapResultRpc(false, "THEY LEFT", RpcTarget.Single(other, RpcTargetUse.Temp));
            }
            else
            {
                WithdrawSwapRpc(RpcTarget.Single(other, RpcTargetUse.Temp));
            }
        }

        for (int i = 0; i < members.Count; i++)
        {
            if (members[i].ClientId != clientId)
            {
                continue;
            }
            bool wasHuman = members[i].Role == PlayerRole.Human;
            members.RemoveAt(i);
            // The kitchen needs its human: the player who has been waiting longest takes over rather
            // than leaving a room that can never start.
            if (wasHuman && members.Count > 0)
            {
                LobbyMember promoted = members[0];
                promoted.Role = PlayerRole.Human;
                members[0] = promoted;
            }
            return;
        }
    }

    // ---- requests from clients -----------------------------------------------------------------

    [Rpc(SendTo.Server)]
    void SubmitNameRpc(FixedString32Bytes name, RpcParams rpcParams = default)
    {
        ulong clientId = rpcParams.Receive.SenderClientId;
        for (int i = 0; i < members.Count; i++)
        {
            if (members[i].ClientId != clientId)
            {
                continue;
            }
            LobbyMember member = members[i];
            member.Name = name;
            members[i] = member;
            return;
        }
        // Not seated yet: kept for Seat rather than dropped.
        namesBeforeSeat[clientId] = name;
    }

    // A flyer asking for the human role (the lobby's REQUEST THIS ROLE button).
    public void RequestHumanRole()
    {
        RequestHumanRoleRpc();
    }

    [Rpc(SendTo.Server)]
    void RequestHumanRoleRpc(RpcParams rpcParams = default)
    {
        ulong requesterId = rpcParams.Receive.SenderClientId;
        if (!TryGetMember(requesterId, out LobbyMember requester) || requester.Role == PlayerRole.Human)
        {
            return;
        }

        // An empty chair needs nobody's permission.
        if (!TryGetHuman(out LobbyMember human))
        {
            SetRole(requesterId, PlayerRole.Human);
            AnswerSwapResultRpc(true, "THE KITCHEN WAS EMPTY", RpcTarget.Single(requesterId, RpcTargetUse.Temp));
            return;
        }

        StartRequest(requesterId, human.ClientId, requester.Name, targetBecomesHuman: false);
    }

    // The human asking a particular flyer to take over (the SWAP button on a flyer's row).
    public void RequestSwapWith(ulong flyerClientId)
    {
        RequestSwapWithRpc(flyerClientId);
    }

    [Rpc(SendTo.Server)]
    void RequestSwapWithRpc(ulong flyerClientId, RpcParams rpcParams = default)
    {
        ulong requesterId = rpcParams.Receive.SenderClientId;
        if (!TryGetMember(requesterId, out LobbyMember requester) || requester.Role != PlayerRole.Human)
        {
            return;
        }
        if (!TryGetMember(flyerClientId, out LobbyMember flyer) || flyer.Role != PlayerRole.Flyer)
        {
            return;
        }

        StartRequest(requesterId, flyerClientId, requester.Name, targetBecomesHuman: true);
    }

    void StartRequest(ulong requesterId, ulong targetId, FixedString32Bytes requesterName, bool targetBecomesHuman)
    {
        if (hasPendingRequest)
        {
            AnswerSwapResultRpc(false, "A SWAP IS ALREADY WAITING", RpcTarget.Single(requesterId, RpcTargetUse.Temp));
            return;
        }

        hasPendingRequest = true;
        pendingRequesterId = requesterId;
        pendingTargetId = targetId;
        pendingExpiresAt = Time.time + SwapRequestTimeout;
        ShowSwapPromptRpc(requesterId, requesterName, targetBecomesHuman, RpcTarget.Single(targetId, RpcTargetUse.Temp));
    }

    // The answer to the prompt, from the player it was shown to.
    public void AnswerSwap(bool accept)
    {
        AnswerSwapRpc(accept);
    }

    [Rpc(SendTo.Server)]
    void AnswerSwapRpc(bool accept, RpcParams rpcParams = default)
    {
        // Only the player actually being asked can answer, and only while the request stands: a late
        // or forged answer must not swap roles behind someone's back.
        if (!hasPendingRequest || rpcParams.Receive.SenderClientId != pendingTargetId)
        {
            return;
        }

        ulong requesterId = pendingRequesterId;
        ulong targetId = pendingTargetId;
        ClearPendingRequest();

        if (!TryGetMember(requesterId, out LobbyMember requester) || !TryGetMember(targetId, out LobbyMember target))
        {
            return;
        }

        if (accept)
        {
            SetRole(requesterId, target.Role);
            SetRole(targetId, requester.Role);
        }

        AnswerSwapResultRpc(accept, target.Name, RpcTarget.Single(requesterId, RpcTargetUse.Temp));
    }

    void ClearPendingRequest()
    {
        hasPendingRequest = false;
        pendingRequesterId = 0;
        pendingTargetId = 0;
        pendingExpiresAt = 0f;
    }

    void SetRole(ulong clientId, PlayerRole role)
    {
        for (int i = 0; i < members.Count; i++)
        {
            if (members[i].ClientId != clientId)
            {
                continue;
            }
            LobbyMember member = members[i];
            member.Role = role;
            members[i] = member;
            return;
        }
    }

    // ---- messages back to one client -----------------------------------------------------------

    [Rpc(SendTo.SpecifiedInParams)]
    void ShowSwapPromptRpc(ulong requesterId, FixedString32Bytes requesterName, bool becomesHuman, RpcParams rpcParams = default)
    {
        SwapRequested?.Invoke(requesterId, requesterName.ToString(), becomesHuman);
    }

    [Rpc(SendTo.SpecifiedInParams)]
    void AnswerSwapResultRpc(bool accepted, FixedString32Bytes otherName, RpcParams rpcParams = default)
    {
        SwapAnswered?.Invoke(accepted, otherName.ToString());
    }

    [Rpc(SendTo.SpecifiedInParams)]
    void WithdrawSwapRpc(RpcParams rpcParams = default)
    {
        // Same event the asker gets: the prompt closes and the lobby says why.
        SwapAnswered?.Invoke(false, "WITHDRAWN");
    }

    // ---- starting the game ---------------------------------------------------------------------

    public void StartGame()
    {
        if (!IsServer || !CanStart(out _))
        {
            return;
        }
        if (!Application.CanStreamedLevelBeLoaded(GameSceneManager.KitchenOnlineScene))
        {
            Debug.LogError($"{nameof(LobbyManager)}: scene '{GameSceneManager.KitchenOnlineScene}' is not in the build scene list, so the game cannot start. Add it in File > Build Profiles > Scene List.");
            return;
        }

        // The host also keeps everyone's role, for KitchenSpawner to hand out bodies by.
        foreach (LobbyMember member in members)
        {
            GameSession.SetRole(member.ClientId, member.Role);
        }
        // Every machine stores its own role and the match length first: once the scene load starts,
        // this lobby is gone and the Kitchen has nothing left to ask.
        HandOverToKitchenRpc(matchMinutes.Value);
        // Netcode's scene manager (not GameSceneManager) loads the Kitchen, because the clients have
        // to be taken along and kept in step with the host.
        NetworkManager.SceneManager.LoadScene(GameSceneManager.KitchenOnlineScene, LoadSceneMode.Single);
    }

    [Rpc(SendTo.Everyone)]
    void HandOverToKitchenRpc(int minutes)
    {
        GameSession.LocalRole = LocalRole;
        GameSession.StartedFromLobby = true;
        // The host's length, not this machine's setting: MatchDirector reads it in the Kitchen.
        GameSession.MatchMinutes = minutes;
        // Gameplay locks the cursor; the menu had it free.
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    // ---- this player's name --------------------------------------------------------------------

    // Shown to everyone else in the room. The name is set in the settings panel; GameSettings falls
    // back to the machine's own user name when nothing has been typed.
    public static FixedString32Bytes LocalPlayerName()
    {
        return new FixedString32Bytes(GameSettings.PlayerName);
    }

    // The roster as plain rows, for UI that would rather not walk a NetworkList.
    public List<LobbyMember> Snapshot(List<LobbyMember> into)
    {
        into.Clear();
        foreach (LobbyMember member in members)
        {
            into.Add(member);
        }
        return into;
    }
}
