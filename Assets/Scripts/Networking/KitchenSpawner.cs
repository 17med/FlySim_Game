using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

// Puts every player from the lobby into the online Kitchen as the body their role says: the human
// gets the old man, every flyer gets a fly of their own.
// Only the host spawns. It waits for Netcode to report that every player has finished loading the
// scene, so nobody is handed a body in a scene they do not have yet. Each body is owned by its player,
// which is what lets OwnerOnlyControls and the owner-driven NetworkTransform give it to them.
// A plain MonoBehaviour rather than a NetworkBehaviour: it never sends anything itself, and an
// in-scene NetworkObject would add a second thing that has to match between host and clients.
public class KitchenSpawner : MonoBehaviour
{
    [Tooltip("old_person Variant prefab (needs a NetworkObject and must be in DefaultNetworkPrefabs).")]
    [SerializeField] NetworkObject humanPrefab;
    [Tooltip("fly prefab (needs a NetworkObject and must be in DefaultNetworkPrefabs).")]
    [SerializeField] NetworkObject flyPrefab;
    [SerializeField] Transform humanSpawn;
    [Tooltip("Flies are spread over these in turn. More flies than points: the extra ones stand beside the point.")]
    [SerializeField] Transform[] flySpawns = new Transform[0];
    [Tooltip("Sideways gap, in metres, between flies that share a spawn point.")]
    [SerializeField] float flySpacing = 0.5f;
    [Tooltip("The kitchen's physics objects. All of them are handed to the human player, the only one who grabs, throws and slaps them.")]
    [SerializeField] NetworkObject[] props = new NetworkObject[0];

    int fliesSpawned;

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

    // The objects' NetworkTransforms are owner-driven, so whoever owns an object simulates it. Only
    // the old man touches them, so he owns them all from the start: his grabs, throws and slaps run
    // on his machine with no delay, and nothing ever has to change hands mid-throw. If he leaves,
    // Netcode gives them back to the host.
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
        offset = point.right * (flySpacing * (index / flySpawns.Length));
        return point;
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
