using Unity.Netcode;
using UnityEngine;

// Lets the flies see each other through walls: on a fly player's machine every other fly gets a
// glow (FlySimulator/FlyTeammateXRay) drawn only where something hides it, so a teammate behind a
// cupboard shows as a light-blue shape where they are.
// The old man never gets it. His machine never adds the glow, for the same reason his minimap never
// shows the flies: hunting them would be no work at all.
// Done by giving each of the fly model's renderers one more material, on this machine only. Nothing
// is sent over the network and the prefab is never changed, so who sees the glow is decided entirely
// by whose screen it is. A dead fly's renderers are switched off by FlyHealth, which takes the glow
// with them, and a respawned fly glows again.
// Goes on the fly's root next to its NetworkObject (Tools > Kitchen Online > Wire Network Players
// adds it and its material). Does nothing in the offline Kitchen, where nothing is spawned.
public class FlyTeammateXRay : NetworkBehaviour
{
    [Tooltip("The glow: a material using FlySimulator/FlyTeammateXRay. Tools > Kitchen Online > Wire Network Players sets it.")]
    [SerializeField] Material xRayMaterial;

    // Off until this body is spawned as somebody else's fly: Update only has work to do then.
    void Awake()
    {
        enabled = false;
    }

    // Never on this player's own body: they cannot be hidden from themselves.
    public override void OnNetworkSpawn()
    {
        enabled = !IsOwner;
    }

    // Asked every frame until this player's own body exists, because the bodies are spawned one by one
    // and another fly can arrive before this player's own. Then decided once.
    void Update()
    {
        NetworkObject localPlayer = NetworkManager != null ? NetworkManager.SpawnManager.GetLocalPlayerObject() : null;
        if (localPlayer == null)
        {
            return;
        }
        enabled = false;
        if (localPlayer.GetComponentInChildren<FlyController>(true) != null)
        {
            AddGlow();
        }
    }

    void AddGlow()
    {
        if (xRayMaterial == null)
        {
            // Fails open: the match plays on without the glow - nothing is lost but seeing the other
            // flies through walls - and the warning names the fix.
            Debug.LogWarning($"{nameof(FlyTeammateXRay)}: {name} has no glow material, so this fly is not shown through walls. Run Tools > Kitchen Online > Wire Network Players.", this);
            return;
        }
        // Meshes only: any particle effect or trail on the body is not part of its shape and is left alone.
        foreach (Renderer modelRenderer in GetComponentsInChildren<Renderer>(true))
        {
            if (modelRenderer is MeshRenderer || modelRenderer is SkinnedMeshRenderer)
            {
                AddMaterial(modelRenderer);
            }
        }
    }

    // A material past the mesh's last submesh draws that submesh again, which for a mesh of one submesh
    // is the whole mesh (a mesh of several glows on its last part only). Shared materials, so no
    // per-fly copies are made.
    void AddMaterial(Renderer modelRenderer)
    {
        Material[] current = modelRenderer.sharedMaterials;
        var withGlow = new Material[current.Length + 1];
        current.CopyTo(withGlow, 0);
        withGlow[current.Length] = xRayMaterial;
        modelRenderer.sharedMaterials = withGlow;
    }
}
