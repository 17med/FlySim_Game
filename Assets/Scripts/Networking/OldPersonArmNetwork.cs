using Unity.Netcode;
using UnityEngine;

// Shows the old man's arm on every other player's screen: the slap swing, and reaching for, carrying
// and throwing an object.
// The arm is posed in code, not by an animation clip, so NetworkAnimator cannot carry it. Instead the
// owner sends each thing the arm is told to do, and the other copies replay it on their own
// OldPersonHoldArm / OldPersonSlap, whose timed parts (swing, recover) then run by themselves.
// The objects themselves move through their own NetworkTransform; this only moves the arm. On other
// copies the slap is a mirror (OldPersonSlap.IsMirror): it swings but never hits, so a slap lands once.
public class OldPersonArmNetwork : NetworkBehaviour
{
    OldPersonHoldArm holdArm;
    OldPersonSlap slap;

    void Awake()
    {
        holdArm = GetComponentInChildren<OldPersonHoldArm>(true);
        slap = GetComponentInChildren<OldPersonSlap>(true);
    }

    // Runs before the first Update, so another player's copy never reads this machine's mouse.
    public override void OnNetworkSpawn()
    {
        if (slap != null)
            slap.IsMirror = !IsOwner;
        if (!IsOwner)
            return;
        if (holdArm != null)
            holdArm.Commanded += OnArmCommanded;
        if (slap != null)
            slap.Slapped += OnSlapped;
    }

    public override void OnNetworkDespawn()
    {
        if (holdArm != null)
            holdArm.Commanded -= OnArmCommanded;
        if (slap != null)
            slap.Slapped -= OnSlapped;
    }

    void OnSlapped()
    {
        ShowSlapRpc();
    }

    void OnArmCommanded(OldPersonHoldArm.Command command, Rigidbody body, float extent)
    {
        NetworkObjectReference target = default;
        if (command == OldPersonHoldArm.Command.Reach)
        {
            NetworkObject prop = body != null ? body.GetComponentInParent<NetworkObject>() : null;
            if (prop == null)
            {
                // Fails open: the grab still works and the object still moves for everyone; the other
                // players only miss the hand going out to it.
                Debug.LogWarning($"{nameof(OldPersonArmNetwork)}: '{(body != null ? body.name : "null")}' has no NetworkObject, so other players will not see the reach.", this);
                return;
            }
            target = prop;
        }
        ArmCommandRpc(command, target, extent);
    }

    // Owner only, like every RPC here: nobody else may move this old man's arm.
    [Rpc(SendTo.NotOwner, RequireOwnership = true)]
    void ShowSlapRpc()
    {
        if (slap != null)
            slap.ShowRemoteSlap();
    }

    [Rpc(SendTo.NotOwner, RequireOwnership = true)]
    void ArmCommandRpc(OldPersonHoldArm.Command command, NetworkObjectReference target, float extent)
    {
        if (holdArm == null)
            return;
        switch (command)
        {
            case OldPersonHoldArm.Command.Reach:
                if (target.TryGet(out NetworkObject prop) && prop.TryGetComponent(out Rigidbody body))
                    holdArm.Reach(body, extent);
                break;
            case OldPersonHoldArm.Command.Carry:
                holdArm.Carry();
                break;
            case OldPersonHoldArm.Command.Throw:
                holdArm.Throw();
                break;
            case OldPersonHoldArm.Command.Release:
                holdArm.Release();
                break;
        }
    }
}
