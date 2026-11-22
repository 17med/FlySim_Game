using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

// Puts every player from the lobby into the online Kitchen as the body their role says: the human
// gets the old man, every flyer a fly of their own.
// Only the host spawns, and only once Netcode reports that everyone has finished loading the scene.
// Each body is owned by its player, which is what makes OwnerOnlyControls and the owner-driven
// NetworkTransform hand it over.
// A plain MonoBehaviour: it never sends anything itself, so it needs no NetworkObject of its own.
public class KitchenSpawner : MonoBehaviour
{
    [Tooltip("old_person Variant prefab (needs a NetworkObject and must be in DefaultNetworkPrefabs).")]
    [SerializeField] NetworkObject humanPrefab;
    [Tooltip("fly prefab (needs a NetworkObject and must be in DefaultNetworkPrefabs).")]
    [SerializeField] NetworkObject flyPrefab;
    [SerializeField] Transform humanSpawn;
    [Tooltip("Flies are spread over these in turn. More flies than points: the extra ones stand beside the point.")]
    [SerializeField] Transform[] flySpawns = new Transform[0];
    [Tooltip("Sideways gap between flies that share a spawn point, as a multiple of the fly's own width. Below 1 they are spawned inside each other and shove each other apart.")]
    [SerializeField, Min(1f)] float flySpacing = 1.5f;
    [Tooltip("The kitchen's physics objects. All of them are handed to the human player, the only one who grabs, throws and slaps them.")]
    [SerializeField] NetworkObject[] props = new NetworkObject[0];

    int fliesSpawned;
    // The fly prefab's own width in units, measured once. Nothing to do with the kitchen's scale: it
    // is how far two flies have to stand apart not to overlap.
    float flyWidth;

    void Awake()
    {
        NetworkManager network = NetworkManager.Singleton;
        if (network == null || !network.IsListening)
        {
            // Fails closed: with no session there is nobody to spawn, and a scene of networked bodies
            // played on its own would leave the player looking at nothing with no idea why.
            Debug.LogError($"{nameof(KitchenSpawner)}: '{gameObject.scene.name}' is the online kitchen and has to be started from the lobby " +
                           $"(play {GameSceneManager.MainMenuScene}, CREATE a room, then START). For single player, play '{GameSceneManager.KitchenScene}'.", this);
            enabled = false;
            return;
        }
        if (!network.IsServer || !IsSetUp())
        {
            return;
        }
        network.SceneManager.OnLoadEventCompleted += OnLoadEventCompleted;
    }

    void OnDestroy()
    {
        NetworkManager network = NetworkManager.Singleton;
        if (network != null && network.SceneManager != null)
        {
            network.SceneManager.OnLoadEventCompleted -= OnLoadEventCompleted;
        }
    }

    void OnLoadEventCompleted(string sceneName, LoadSceneMode mode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if (sceneName != gameObject.scene.name)
        {
            return;
        }
        foreach (ulong clientId in clientsCompleted)
        {
            Spawn(clientId);
        }
        foreach (ulong clientId in clientsTimedOut)
        {
            // Fails open: one slow machine should not stop everyone else from playing.
            Debug.LogWarning($"{nameof(KitchenSpawner)}: player {clientId} did not finish loading '{sceneName}' in time and gets no body.", this);
        }
    }

    void Spawn(ulong clientId)
    {
        if (!GameSession.TryGetRole(clientId, out PlayerRole role))
        {
            // Fails open as a flyer: every player who reached the Kitchen came through the lobby, so a
            // missing role is a bug worth logging, but leaving the player bodiless helps nobody.
            Debug.LogWarning($"{nameof(KitchenSpawner)}: no lobby role recorded for player {clientId}; spawning them as a fly.", this);
            role = PlayerRole.Flyer;
        }

        bool isHuman = role == PlayerRole.Human;
        NetworkObject prefab = isHuman ? humanPrefab : flyPrefab;
        Vector3 offset = Vector3.zero;
        Transform point = isHuman ? humanSpawn : NextFlySpawn(out offset);

        NetworkObject body = Instantiate(prefab, point.position + offset, point.rotation);
        // Destroyed with the scene, so going back to the menu does not carry bodies along.
        body.SpawnAsPlayerObject(clientId, destroyWithScene: true);
        if (isHuman)
        {
            GiveProps(clientId);
        }
    }

    // The objects' NetworkTransforms are owner-driven, and only the old man ever touches them, so he
    // owns them all from the start: his grabs and throws run locally and never change hands mid-flight.
    void GiveProps(ulong humanClientId)
    {
        foreach (NetworkObject prop in props)
        {
            if (prop != null && prop.IsSpawned)
            {
                prop.ChangeOwnership(humanClientId);
            }
        }
    }

    Transform NextFlySpawn(out Vector3 offset)
    {
        int index = fliesSpawned++;
        Transform point = flySpawns[index % flySpawns.Length];
        // Measured in fly widths, not in units: the gap was 0.5 units while the fly is 2 units
        // across, so the third and fourth flyers were spawned inside the first two and the
        // CharacterControllers flung them apart the moment the match started.
        offset = point.right * (FlyWidth() * flySpacing * (index / flySpawns.Length));
        return point;
    }

    // The widest the fly's own collider is, in units. Falls back to the gap being counted in units
    // when the prefab has no CharacterController, which is the old behaviour rather than no gap.
    float FlyWidth()
    {
        if (flyWidth > 0f)
        {
            return flyWidth;
        }
        CharacterController body = flyPrefab != null ? flyPrefab.GetComponentInChildren<CharacterController>(true) : null;
        flyWidth = body != null ? body.radius * 2f * Mathf.Abs(body.transform.lossyScale.x) : 1f;
        return flyWidth;
    }

    // Checked once on the host before anyone loads in, so a missing reference is one clear error at
    // the start instead of a NullReferenceException per player.
    bool IsSetUp()
    {
        bool hasFlySpawn = flySpawns.Length > 0 && System.Array.TrueForAll(flySpawns, point => point != null);
        if (humanPrefab != null && flyPrefab != null && humanSpawn != null && hasFlySpawn)
        {
            return true;
        }
        Debug.LogError($"{nameof(KitchenSpawner)}: assign Human Prefab, Fly Prefab, Human Spawn and at least one Fly Spawn " +
                       "(Tools > Kitchen Online > Wire Network Players does it). Nobody will be spawned.", this);
        enabled = false;
        return false;
    }
}
