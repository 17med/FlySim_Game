using Unity.Netcode;
using UnityEngine;

// Shows a fly's spit on every other player's screen.
// The owner fires exactly as in single player (FlySpit); each shot is then sent to everyone else,
// whose copy of this fly launches a blob along the same path with the same burst and sound. Their
// blob cannot hit anything, so a hit happens once, on the shooter's machine.
// The final velocity is sent rather than recomputed because FlySpit adds a random spread.
public class FlySpitNetwork : NetworkBehaviour
{
    FlySpit spit;

    void Awake()
    {
        spit = GetComponentInChildren<FlySpit>(true);
        // Fails open: the fly still flies and spits for its own player; only the others miss the shots.
        if (spit == null)
            Debug.LogWarning($"{nameof(FlySpitNetwork)}: no {nameof(FlySpit)} under {name}; other players will not see this fly spit.", this);
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner && spit != null)
            spit.Fired += OnFired;
    }

    public override void OnNetworkDespawn()
    {
        if (spit != null)
            spit.Fired -= OnFired;
    }

    void OnFired(Vector3 position, Vector3 velocity, float size, bool isBigShot)
    {
        ShowShotRpc(position, velocity, size, isBigShot);
    }

    // Owner only: nobody else may make this fly spit on other screens.
    [Rpc(SendTo.NotOwner, RequireOwnership = true)]
    void ShowShotRpc(Vector3 position, Vector3 velocity, float size, bool isBigShot)
    {
        if (spit != null)
            spit.ShowRemoteShot(position, velocity, size, isBigShot);
    }
}
