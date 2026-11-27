using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Puts the server in charge of the dish, so every player sees the same couscous go down.
// The flies eat on their own machine for the feel of it, but what they eat is a request: the server
// checks the fly is really over the dish, that the old man is not standing over it, and that nobody
// is eating faster than a fly can, then takes the bite and tells everyone what is left.
// Goes next to FoodDish on a dish with a NetworkObject (Tools > Match > Wire Match adds both).
// Without this component FoodDish simply runs locally, which is what the single player Kitchen wants.
[RequireComponent(typeof(FoodDish))]
public class FoodDishNetwork : NetworkBehaviour
{
    // Headroom on the rate check, for a client whose messages arrive in bursts. A fly still cannot eat
    // meaningfully faster than Bite Rate over any stretch of time.
    const float RateTolerance = 2f;
    // The longest stretch one message may be paid for, in seconds. A fly sends its mouthfuls a few
    // times a second (FlyEating.BiteSendInterval), and a slow connection can bunch several of those
    // together, so the window has to cover a handful of them; what it must not cover is a client that
    // sat quiet for a minute and then asked for a minute's eating in one go.
    const float MaxBiteWindow = 0.5f;
    // Distance headroom on the range check, in multiples of the bite range: the server sees the fly a
    // little behind where its own player does.
    const float RangeTolerance = 1.4f;

    readonly NetworkVariable<float> percent =
        new NetworkVariable<float>(100f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Server only: when each client's last accepted bite was, in server time, so the rate can be checked.
    readonly Dictionary<ulong, double> lastBiteAt = new Dictionary<ulong, double>();

    FoodDish dish;

    void Awake()
    {
        dish = GetComponent<FoodDish>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            // The dish as the scene authored it is what the room starts from.
            percent.Value = dish.Percent;
        }
        else
        {
            dish.SetPercent(percent.Value);
        }
        percent.OnValueChanged += OnPercentChanged;
    }

    public override void OnNetworkDespawn()
    {
        percent.OnValueChanged -= OnPercentChanged;
    }

    // Called by FlyEating on the eating player's own machine, every frame the fly is eating. The fly
    // does not take the bite itself when this component is present, not even on the host: the dish
    // only ever goes down once the server says so, so one mouthful is never counted twice. The dish
    // rocks locally on the first frame, so the bite still feels immediate while the answer travels.
    public void RequestBite(float amount)
    {
        if (amount > 0f)
        {
            RequestBiteRpc(amount);
        }
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    void RequestBiteRpc(float amount, RpcParams rpcParams = default)
    {
        ulong senderId = rpcParams.Receive.SenderClientId;
        if (!IsAllowed(senderId, amount, out float allowed))
        {
            return;
        }
        float eaten = dish.Bite(allowed);
        if (eaten <= 0f)
        {
            return;
        }
        lastBiteAt[senderId] = NetworkManager.ServerTime.Time;
        percent.Value = dish.Percent;
    }

    // Everything the server checks before a bite counts. A modified client can ask for anything; it
    // still has to have a fly, have it over the dish, and have waited long enough since its last bite.
    bool IsAllowed(ulong senderId, float amount, out float allowed)
    {
        allowed = 0f;
        if (dish.IsEmpty || dish.IsGuarded)
        {
            return false;
        }

        if (!NetworkManager.ConnectedClients.TryGetValue(senderId, out NetworkClient client) || client.PlayerObject == null)
        {
            return false;
        }
        // The fly's controller is what moves, not the spawned root, so the body is measured from it.
        FlyController fly = client.PlayerObject.GetComponentInChildren<FlyController>(true);
        if (fly == null)
        {
            // Not a fly: the old man does not get to eat the couscous he is guarding.
            return false;
        }
        if (Vector3.Distance(fly.transform.position, dish.BitePoint) > dish.BiteRange * RangeTolerance)
        {
            return false;
        }

        double now = NetworkManager.ServerTime.Time;
        bool hasEaten = lastBiteAt.TryGetValue(senderId, out double last);
        // A first bite is worth one message's eating; a later one is worth the time since the last,
        // capped, so the elapsed time since a bite ten seconds ago cannot buy ten seconds of eating.
        float window = hasEaten ? Mathf.Min((float)(now - last), MaxBiteWindow) : FlyEating.BiteSendInterval;
        // The host's own fly count decides the share, so a client cannot eat at the full rate.
        allowed = Mathf.Min(amount, dish.BiteRate * MatchDirector.BiteShare * window * RateTolerance);
        return allowed > 0f;
    }

    void OnPercentChanged(float previous, float current)
    {
        // The server already took the bite on its own dish; everyone else follows it here.
        if (!IsServer)
        {
            dish.SetPercent(current);
        }
    }
}
