using Unity.Netcode;
using UnityEngine;

// Shows a fly's eating on every other player's screen: the head down in the couscous and the chewing.
// The dish itself is not sent from here - FoodDishNetwork owns what is left of it - so this only
// carries whether this fly is eating right now, which is what its copies need to pose and chew.
// A NetworkVariable rather than an RPC per bite: eating is a state that lasts, and a player who joins
// or whose packet went missing then still sees a fly with its head in the food.
public class FlyEatNetwork : NetworkBehaviour
{
    readonly NetworkVariable<bool> eating =
        new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    FlyEating eater;

    void Awake()
    {
        eater = GetComponentInChildren<FlyEating>(true);
        // Fails open: the fly still eats for its own player; only the others miss seeing it.
        if (eater == null)
        {
            Debug.LogWarning($"{nameof(FlyEatNetwork)}: no {nameof(FlyEating)} under {name}; other players will not see this fly eat.", this);
        }
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner && eater != null)
        {
            eater.SetRemoteEating(eating.Value);
        }
        eating.OnValueChanged += OnEatingChanged;
    }

    public override void OnNetworkDespawn()
    {
        eating.OnValueChanged -= OnEatingChanged;
    }

    void Update()
    {
        // Only the owner reports; everyone else is told.
        if (IsOwner && eater != null && eating.Value != eater.Eating)
        {
            eating.Value = eater.Eating;
        }
    }

    void OnEatingChanged(bool previous, bool current)
    {
        if (!IsOwner && eater != null)
        {
            eater.SetRemoteEating(current);
        }
    }
}
