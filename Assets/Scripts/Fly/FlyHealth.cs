using System;
using System.Collections.Generic;
using UnityEngine;

// How much of a beating one fly takes, and what happens after the last one: two slaps from the old
// man (or one boot on a fly that is standing still) and it is dead, then ten seconds later it is back
// in the kitchen at one of the spawn points the scene has tagged.
// Goes next to FlyController on the fly's body. Death is not the end of the player's match - that is
// the whole point of the respawn - so nothing here destroys or despawns anything: the fly is only
// switched off and hidden, then switched back on exactly as it was found.
// A plain MonoBehaviour: FlyHealthNetwork puts the server in charge of the counting, the dying and
// the clock in a room, and the single player Kitchen keeps working with no network at all. The split
// is the same one FoodDish/FoodDishNetwork and FlyController/FlyKnockbackNetwork already use.
[RequireComponent(typeof(FlyController))]
public class FlyHealth : MonoBehaviour
{
    // The tag the player puts on the empty objects a dead fly comes back at. Also the default of
    // Respawn Tag below, and what Tools > Match > Wire Fly Lives tags the spawn points with.
    public const string DefaultRespawnTag = "FlySpawn";

    [Tooltip("How many hits from the old man kill the fly. Two, so a single lucky swing is not the end of a player's match but a human who connects twice is rewarded.")]
    [SerializeField, Min(1)] int hitsToKill = 2;

    [Tooltip("Seconds a dead fly waits before it comes back, in the server's time. Long enough that dying costs the flies real eating time, short enough that the player is not left watching an empty kitchen.")]
    [SerializeField, Min(0f)] float respawnSeconds = 10f;

    [Tooltip("The tag on the empty objects a dead fly can come back at. One is picked at random. With nothing tagged the fly simply reappears where it died, so an untagged scene is still playable.")]
    [SerializeField] string respawnTag = DefaultRespawnTag;

    [Tooltip("The fly model (the child with the Animator). Its renderers are the ones hidden while the fly is dead. Found automatically if left empty. The camera must not be under it: the dead player still has to see something.")]
    [SerializeField] Transform modelRoot;

    // Raised the moment the fly dies, and again when it comes back, on every machine - the mirrors
    // included, so anything that reacts to a fly dying can hang off these rather than poll IsDead.
    public event Action Died;
    public event Action Respawned;

    public int HitsToKill => hitsToKill;

    public int HitsTaken => hitsTaken;

    public bool IsDead => isDead;

    // 0 while alive, counting down from Respawn Seconds while dead. Online this is the local view of
    // the server's clock (it starts when word of the death arrives, a ping behind the server's own
    // countdown), so it is for showing the player, not for deciding anything: FlyHealthNetwork owns
    // the moment the fly actually comes back, for the same reason MatchNetwork owns the match clock.
    public float RespawnSecondsLeft => isDead ? Mathf.Max(0f, respawnLeft) : 0f;

    // Read by FlyHealthNetwork, which has to tell the server when the fly is due back.
    public float RespawnSeconds => respawnSeconds;

    FlyController fly;
    CharacterController body;
    FlyEating eating;
    FlySpit spit;
    FlyAudio flyAudio;
    AudioSource wingSource;
    FlyAiming[] aiming = new FlyAiming[0];
    Renderer[] modelRenderers = new Renderer[0];

    // What death actually switched off, so the respawn puts back exactly that and nothing more. This
    // is what keeps a respawn from making another player's copy of the fly playable: on a mirror
    // OwnerOnlyControls has already switched FlyController, FlySpit and FlyAiming off, so they were
    // never added to this list and are never switched on again.
    readonly List<Behaviour> switchedOff = new List<Behaviour>();
    readonly List<Renderer> hidden = new List<Renderer>();
    // The CharacterController is a Collider rather than a Behaviour, so it cannot go in the list above.
    bool bodyWasEnabled;

    int hitsTaken;
    bool isDead;
    float respawnLeft;
    // True only when this machine is the one counting the ten seconds, which is the case offline.
    // Online the server flips the fly back on and this stays false, so the two clocks never race.
    bool ownsRespawnClock;
    // So a fly with several spawn points to choose from does not come back at the same one twice.
    Transform lastRespawnPoint;

    void Awake()
    {
        fly = GetComponent<FlyController>();
        body = GetComponent<CharacterController>();
        eating = GetComponent<FlyEating>();
        spit = GetComponent<FlySpit>();
        flyAudio = GetComponent<FlyAudio>();
        // Switching FlyAudio off stops it picking the next clip, but the one already playing keeps
        // going, so the wing beat has to be cut on the source itself.
        if (flyAudio != null)
        {
            wingSource = flyAudio.GetComponent<AudioSource>();
        }
        // The camera the player sees through is a child of the fly's root rather than of this body, so
        // its FlyAiming is a sibling's component and a search down from here would miss it. The fly is
        // a root object in the Kitchen and a root spawned player online, so its own root is the top of it.
        aiming = transform.root.GetComponentsInChildren<FlyAiming>(true);

        if (modelRoot == null)
        {
            Animator animator = GetComponentInChildren<Animator>(true);
            modelRoot = animator != null ? animator.transform : null;
        }
        if (modelRoot != null)
        {
            modelRenderers = modelRoot.GetComponentsInChildren<Renderer>(true);
        }
        // Fails open: a fly that dies without disappearing still stops playing and still comes back,
        // which is the part the match depends on. Only the corpse on screen is wrong.
        if (modelRenderers.Length == 0)
        {
            Debug.LogWarning($"{nameof(FlyHealth)}: no renderers found under {name}'s model, so a dead fly will stay visible. Assign Model Root to the fly model (the child with the Animator).", this);
        }
    }

    void Update()
    {
        if (!isDead)
        {
            return;
        }
        // Not unscaled: the single player pause stops the world, and a fly must not come back while
        // the game is paused.
        respawnLeft -= Time.deltaTime;
        if (ownsRespawnClock && respawnLeft <= 0f)
        {
            TryPickRespawn(out Vector3 position, out Quaternion rotation);
            RespawnAt(position, rotation);
        }
    }

    // One slap. Returns true when this is the hit that killed the fly, so the hitter can react to the
    // kill rather than to every connection. Called on this machine: offline that is the only machine,
    // and online FlyHealthNetwork counts on the server instead and never calls this.
    public bool TakeHit()
    {
        if (isDead)
        {
            return false;
        }
        hitsTaken++;
        if (hitsTaken < hitsToKill)
        {
            return false;
        }
        Die(ownsClock: true);
        return true;
    }

    // Straight to dead, whatever the count was: the old man standing on a fly that is on the ground
    // does not need two goes at it.
    public void Kill()
    {
        if (isDead)
        {
            return;
        }
        hitsTaken = hitsToKill;
        Die(ownsClock: true);
    }

    // What the server says about this fly. Used by FlyHealthNetwork on every machine in a room,
    // including the host's, so there is one path to death however many players are watching. A death
    // that arrives this way does not start a local clock: the server owns when the fly comes back.
    public void SetRemoteDead(bool dead, int hitsTaken)
    {
        this.hitsTaken = Mathf.Clamp(hitsTaken, 0, hitsToKill);
        if (dead && !isDead)
        {
            Die(ownsClock: false);
        }
        else if (!dead && isDead)
        {
            // No position came with it, so the fly comes back where it fell. Only reached if the
            // respawn point went missing; the normal path is RespawnAt.
            Revive();
        }
    }

    // Puts the fly back in the kitchen at this spot and makes it playable again.
    public void RespawnAt(Vector3 position, Quaternion rotation)
    {
        // Moved before it is switched back on, so the fly is never seen for a frame at the place it
        // died. Teleport leaves the CharacterController exactly as it found it (off, at this point),
        // and Revive is what switches it on again.
        if (fly != null)
        {
            fly.Teleport(position, rotation);
        }
        else
        {
            transform.SetPositionAndRotation(position, rotation);
        }
        Revive();
    }

    // Picks one of the scene's tagged spawn points. False means nothing was tagged, in which case the
    // position and rotation handed back are the ones the fly already has - a respawn on the spot -
    // because a kitchen nobody remembered to tag must still be playable.
    public bool TryPickRespawn(out Vector3 position, out Quaternion rotation)
    {
        position = transform.position;
        rotation = transform.rotation;

        GameObject[] points;
        try
        {
            points = GameObject.FindGameObjectsWithTag(respawnTag);
        }
        catch (UnityException)
        {
            // The tag itself does not exist in the project, which is a different mistake from having
            // no object with it, and Unity only says so by throwing.
            Debug.LogWarning($"{nameof(FlyHealth)}: the tag \"{respawnTag}\" is not defined in this project, so {name} will come back where it died. Add it under Project Settings > Tags and Layers, then tag the fly's spawn points with it (Tools > Match > Wire Fly Lives does both).", this);
            return false;
        }

        if (points.Length == 0)
        {
            Debug.LogWarning($"{nameof(FlyHealth)}: nothing in this scene is tagged \"{respawnTag}\", so {name} will come back where it died. Tag a few empty objects with it, in the air where a fly can appear without being inside the furniture (Tools > Match > Wire Fly Lives does it).", this);
            return false;
        }

        int index = UnityEngine.Random.Range(0, points.Length);
        // Coming back at the same corner of the kitchen twice running reads as the respawn being
        // broken, so with somewhere else to go the fly goes somewhere else.
        if (points.Length > 1 && points[index].transform == lastRespawnPoint)
        {
            index = (index + UnityEngine.Random.Range(1, points.Length)) % points.Length;
        }

        Transform point = points[index].transform;
        lastRespawnPoint = point;
        position = point.position;
        rotation = point.rotation;
        return true;
    }

    // Stops the fly being playable and takes it off the screen. Everything switched off here is
    // remembered, so Revive can undo exactly this much.
    void Die(bool ownsClock)
    {
        isDead = true;
        ownsRespawnClock = ownsClock;
        respawnLeft = respawnSeconds;

        // FlyController so it cannot be steered (and so the tumble from the killing slap stops),
        // FlyEating so a dead fly's mouthfuls stop counting for the match, FlySpit and FlyAiming so it
        // cannot shoot back, FlyAudio so the wings go quiet.
        SwitchOff(fly);
        SwitchOff(eating);
        SwitchOff(spit);
        SwitchOff(flyAudio);
        foreach (FlyAiming aim in aiming)
        {
            SwitchOff(aim);
        }
        if (wingSource != null)
        {
            wingSource.Stop();
        }
        // No collider while dead, so the old man cannot slap or stand on a corpse and the fly does not
        // hold a door open where it fell.
        if (body != null && body.enabled)
        {
            bodyWasEnabled = true;
            body.enabled = false;
        }

        // Only the model's renderers: the orbit camera is the fly's own child, and hiding the whole
        // fly would leave the dead player looking at nothing for ten seconds.
        foreach (Renderer modelRenderer in modelRenderers)
        {
            if (modelRenderer != null && modelRenderer.enabled)
            {
                modelRenderer.enabled = false;
                hidden.Add(modelRenderer);
            }
        }

        Died?.Invoke();
    }

    // Puts back what Die switched off, and only that.
    void Revive()
    {
        if (!isDead)
        {
            return;
        }
        isDead = false;
        ownsRespawnClock = false;
        respawnLeft = 0f;
        hitsTaken = 0;

        foreach (Behaviour behaviour in switchedOff)
        {
            if (behaviour != null)
            {
                behaviour.enabled = true;
            }
        }
        switchedOff.Clear();

        foreach (Renderer modelRenderer in hidden)
        {
            if (modelRenderer != null)
            {
                modelRenderer.enabled = true;
            }
        }
        hidden.Clear();

        if (bodyWasEnabled && body != null)
        {
            body.enabled = true;
        }
        bodyWasEnabled = false;

        Respawned?.Invoke();
    }

    // Switches a component off only if it was on, and remembers the ones it switched. A component that
    // was already off - which is every owner-only one on another player's copy of the fly - is left
    // alone and never comes back on, so a respawn cannot hand this machine control of someone else's fly.
    void SwitchOff(Behaviour behaviour)
    {
        if (behaviour == null || !behaviour.enabled)
        {
            return;
        }
        behaviour.enabled = false;
        switchedOff.Add(behaviour);
    }
}
