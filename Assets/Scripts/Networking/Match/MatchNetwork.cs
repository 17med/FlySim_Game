using Unity.Netcode;
using UnityEngine;

// Makes one clock and one verdict out of a room full of machines.
// Each player could count ten minutes down on their own, but they would not count the same ten
// minutes: the clock here is a moment in the server's time, which every client already has in step,
// so the last minute starts for everyone at once. The ending is the host's call for the same reason -
// two machines that disagreed about who won would each show their own player a different result.
// Goes on a Match object with a NetworkObject in Kitchen_online (Tools > Match > Wire Match adds it).
// MatchDirector runs the match with or without this; without it, it is a single player kitchen.
[RequireComponent(typeof(NetworkObject))]
public class MatchNetwork : NetworkBehaviour
{
    public static MatchNetwork Instance { get; private set; }

    // The moment the clock runs out, in server time. Server writes, everyone reads, so a player who
    // joins late still sees the right time left rather than a fresh ten minutes.
    readonly NetworkVariable<double> endsAt =
        new NetworkVariable<double>(0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Who won, as a MatchDirector.Phase. Running until the host says otherwise.
    readonly NetworkVariable<int> outcome =
        new NetworkVariable<int>((int)MatchDirector.Phase.Running, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public float SecondsLeft => Mathf.Max(0f, (float)(endsAt.Value - NetworkManager.ServerTime.Time));

    public MatchDirector.Phase Outcome => (MatchDirector.Phase)outcome.Value;

    public override void OnNetworkSpawn()
    {
        Instance = this;
        if (!IsServer)
        {
            return;
        }
        // The length the lobby settled on, which is the host's own settings value. Floored at the
        // shortest match, so a session started without a lobby still gets a full one.
        int minutes = Mathf.Max(GameSession.MatchMinutes, GameSettings.MinMatchMinutes);
        endsAt.Value = NetworkManager.ServerTime.Time + minutes * 60d;
    }

    public override void OnNetworkDespawn()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    // The host's verdict, sent to the room. Only the first one counts: the clock running out on the
    // same frame as the last mouthful must not overwrite the flies' win with the human's.
    public void Declare(MatchDirector.Phase verdict)
    {
        if (IsServer && Outcome == MatchDirector.Phase.Running)
        {
            outcome.Value = (int)verdict;
        }
    }
}
