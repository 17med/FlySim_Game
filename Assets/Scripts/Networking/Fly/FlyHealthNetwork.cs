using Unity.Netcode;
using UnityEngine;

// Makes one story out of a fly being killed: the hits are counted on the server, the server decides
// the fly is dead, the server picks where it comes back and the server counts the ten seconds.
// It has to work that way because of where the pieces are. The fly only moves on its owner's machine,
// while the slap and the boot that kill it are worked out on the old man's, so the hit has to travel -
// FlyKnockbackNetwork already does exactly that for the knock. And the hits have to be added up
// somewhere that both machines agree on, or the old man's machine and the fly's would each have their
// own idea of whether that was the second slap. The clock is the server's for the reason MatchNetwork
// owns the match clock: two machines counting their own ten seconds do not count the same ten seconds,
// and the fly would reappear twice, or in two places.
// Goes on the fly prefab's root, next to its NetworkObject and FlyKnockbackNetwork.
// Without this component FlyHealth simply runs locally, which is what the single player Kitchen wants.
public class FlyHealthNetwork : NetworkBehaviour
{
    // The fly's life, as the server sees it, read by everyone.
    // Dead is not a variable of its own: Respawn At is the moment in server time the fly is due back,
    // and 0 means it is alive. One number for both keeps death and the clock from ever disagreeing.
    readonly NetworkVariable<int> hits =
        new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    readonly NetworkVariable<double> respawnAt =
        new NetworkVariable<double>(0d, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    // Where the server has decided the fly will reappear. Chosen when it dies rather than when it
    // comes back, so every machine already has the spot in hand the moment the fly is alive again and
    // nobody sees it pop in at the wrong place first.
    readonly NetworkVariable<Vector3> respawnPosition =
        new NetworkVariable<Vector3>(Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    readonly NetworkVariable<Quaternion> respawnRotation =
        new NetworkVariable<Quaternion>(Quaternion.identity, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    FlyHealth health;

    // True while the server says this fly is dead. The same test on every machine, server included.
    bool IsDeadNow => respawnAt.Value != 0d;

    void Awake()
    {
        health = GetComponentInChildren<FlyHealth>(true);
        // Fails open: the fly plays normally, it just cannot be killed. Worth saying out loud, because
        // a fly nobody can swat makes the human's half of the match unwinnable.
        if (health == null)
        {
            Debug.LogWarning($"{nameof(FlyHealthNetwork)}: no {nameof(FlyHealth)} under {name}, so this fly cannot be killed. Run Tools > Match > Wire Fly Lives.", this);
        }
    }

    // One slap. Called on the machine that spotted it, which is the old man's, never the fly owner's.
    public void ReportHit()
    {
        if (health == null)
        {
            return;
        }
        if (!IsSpawned)
        {
            health.TakeHit();
            return;
        }
        ReportHitRpc();
    }

    // The boot: the fly was standing still and is dead outright, however many slaps it had taken.
    public void ReportStomp()
    {
        if (health == null)
        {
            return;
        }
        if (!IsSpawned)
        {
            health.Kill();
            return;
        }
        ReportStompRpc();
    }

    // Everyone may send these, for the same reason FlyKnockbackNetwork lets everyone send KnockRpc:
    // the hit is found on the old man's machine, which does not own the fly.
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    void ReportHitRpc()
    {
        ApplyHits(1);
    }

    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    void ReportStompRpc()
    {
        ApplyHits(health != null ? health.HitsToKill : 1);
    }

    // Server only. Adds the hits up and, on the last one, writes the death everybody then sees.
    void ApplyHits(int count)
    {
        if (!IsServer || health == null || IsDeadNow)
        {
            // A hit on a fly that is already dead is dropped: two players swinging at the same corpse
            // must not shorten or restart its ten seconds.
            return;
        }

        int taken = Mathf.Min(health.HitsToKill, hits.Value + count);
        hits.Value = taken;
        if (taken < health.HitsToKill)
        {
            return;
        }

        // Fails open on its own (it hands back where the fly is standing when nothing is tagged), so
        // the answer is used whether or not it found a spawn point.
        health.TryPickRespawn(out Vector3 position, out Quaternion rotation);
        respawnPosition.Value = position;
        respawnRotation.Value = rotation;
        // Written last, always: this is the one every machine reads as "the fly is dead", so the place
        // it will come back to is already in hand by the time anyone acts on it.
        respawnAt.Value = NetworkManager.ServerTime.Time + health.RespawnSeconds;
    }

    public override void OnNetworkSpawn()
    {
        hits.OnValueChanged += OnHitsChanged;
        respawnAt.OnValueChanged += OnRespawnAtChanged;
        // A player who joins while somebody else's fly is lying dead has to catch up to it; the
        // server's own fly is already in whatever state the server put it in.
        if (!IsServer)
        {
            Apply();
        }
    }

    public override void OnNetworkDespawn()
    {
        hits.OnValueChanged -= OnHitsChanged;
        respawnAt.OnValueChanged -= OnRespawnAtChanged;
    }

    void Update()
    {
        if (!IsSpawned || !IsServer || health == null)
        {
            return;
        }
        if (!IsDeadNow || NetworkManager.ServerTime.Time < respawnAt.Value)
        {
            return;
        }
        // The fly's ten seconds are up. Written last again, so everyone reads the clear flag only once
        // the count is already back to nothing.
        hits.Value = 0;
        respawnAt.Value = 0d;
    }

    void OnHitsChanged(int previous, int current)
    {
        Apply();
    }

    void OnRespawnAtChanged(double previous, double current)
    {
        Apply();
    }

    // Brings this machine's FlyHealth in line with whatever the server currently says, from both
    // variables at once. Reading both every time is what makes the two writes order-proof: a snapshot
    // that applies them the other way round still ends up here with the same pair of values.
    // Runs on the server too, so the host's fly dies down the same path as everybody else's.
    void Apply()
    {
        if (health == null)
        {
            return;
        }
        if (!IsDeadNow && health.IsDead)
        {
            health.RespawnAt(respawnPosition.Value, respawnRotation.Value);
            return;
        }
        health.SetRemoteDead(IsDeadNow, hits.Value);
    }
}
