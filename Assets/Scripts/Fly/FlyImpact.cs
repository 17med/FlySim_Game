using UnityEngine;

// Everything the player sees and hears when the fly takes a knock, in one place: flying into the
// kitchen, being slapped, and dying.
//   - A bump (FlyController.Bumped) puffs dust off the surface, thuds, and jolts the view. How hard
//     all three land is read from the speed the fly went in at, so clipping a cupboard and flying
//     into a wall at full boost do not sound the same.
//   - A hit (FlyKnockbackNetwork.Knocked) sprays the fly's guts along the direction it is flung,
//     plays the slap landing, and kicks the view hard.
//   - Death (FlyHealth.Died) is the same thing, bigger, with nothing left behind to steer.
// Goes next to FlyController on the fly's body, which is the object that actually moves; the
// knockback and the health live on the prefab root above it and are found from here.
// Runs on every copy of the fly, so a slap is seen and heard wherever it is watched from. Bumps are
// the exception: FlyController is only enabled on the fly's own machine, so only that player gets the
// dust off their own walls - which is the right side of the trade, because the alternative is an
// RPC per cupboard.
[RequireComponent(typeof(FlyController))]
public class FlyImpact : MonoBehaviour
{
    // Pitch is dropped below 1 for the heavier sounds, which is what turns the swing clips the project
    // ships into something that reads as a landed blow. Floored well above 0: a pitch at or below 0
    // never finishes playing.
    const float MinPitch = 0.05f;

    [Header("Flying into things")]
    [Tooltip("Where the bump thud is played from. On a child of this object, so it moves with the fly. Optional - leave empty for silent bumps.")]
    [SerializeField] AudioSource bumpSource;
    [Tooltip("One is picked at random per bump, so hitting the same wall twice does not sound like a loop.")]
    [SerializeField] AudioClip[] bumpSounds = new AudioClip[0];
    [SerializeField, Range(0f, 1f)] float bumpVolume = 0.9f;
    [Tooltip("Pitch of the thud at Full Bump Speed. A gentle knock is pitched up from here toward 1, so a hard hit sounds heavier than a scuff.")]
    [SerializeField, Range(MinPitch, 2f)] float bumpPitch = 0.8f;
    [Tooltip("Optional puff of dust, spawned on the surface facing out of it.")]
    [SerializeField] GameObject bumpEffect;
    [Tooltip("Speed into a surface, in units per second, that counts as hitting it as hard as it gets: the loudest thud, the biggest puff, the strongest jolt. Around the fly's top boost speed.")]
    [SerializeField, Min(1f)] float fullBumpSpeed = 50f;
    [Tooltip("View jolt at Full Bump Speed. A slow bump gets proportionally less.")]
    [SerializeField, Range(0f, 1f)] float bumpShake = 0.3f;
    [Tooltip("Smallest a puff is scaled to, at the slowest bump that still counts. 1 = every bump puffs at the prefab's own size.")]
    [SerializeField, Range(0.1f, 1f)] float minBumpEffectScale = 0.45f;

    [Header("Being slapped")]
    [Tooltip("Where the landed blow and the death are played from. On a child of this object. Optional.")]
    [SerializeField] AudioSource hurtSource;
    [Tooltip("The slap landing on the fly. One is picked at random.")]
    [SerializeField] AudioClip[] hitSounds = new AudioClip[0];
    [SerializeField, Range(0f, 1f)] float hitVolume = 1f;
    [Tooltip("Pitch of the blow. Below 1 on purpose: dropping a hand-swing clip an octave is what makes it read as a hit landing on something rather than as the hand moving.")]
    [SerializeField, Range(MinPitch, 2f)] float hitPitch = 0.7f;
    [Tooltip("The fly's guts, sprayed the way it is flung. Optional.")]
    [SerializeField] GameObject bloodEffect;
    [Tooltip("View kick when the fly is hit.")]
    [SerializeField, Range(0f, 1f)] float hitShake = 0.8f;

    [Header("Dying")]
    [Tooltip("The bigger burst for the hit that kills. Optional - the hit burst above is used instead when this is empty.")]
    [SerializeField] GameObject deathEffect;
    [Tooltip("Played on the hit that kills, over the blow itself.")]
    [SerializeField] AudioClip[] deathSounds = new AudioClip[0];
    [SerializeField, Range(0f, 1f)] float deathVolume = 1f;
    [SerializeField, Range(MinPitch, 2f)] float deathPitch = 0.85f;
    [SerializeField, Range(0f, 1f)] float deathShake = 1f;

    [Header("Both")]
    [Tooltip("Seconds a spawned effect is kept before it is destroyed. Long enough for the slowest part of it - the stain - to fade out, or it vanishes mid-fade.")]
    [SerializeField, Min(0.1f)] float effectLifetime = 3.5f;

    FlyController fly;
    CharacterController body;
    FlyHealth health;
    FlyKnockbackNetwork knockback;
    CameraShake shake;
    // The direction of the last knock, kept so the death burst sprays the way the killing blow went
    // rather than straight up. FlyHealth.Died arrives a moment after the knock that caused it, and in
    // a room it arrives from the server with no direction attached at all.
    Vector3 lastHitDirection = Vector3.up;

    // True on this machine's own fly. FlyController is in OwnerOnlyControls' list, so it being enabled
    // is the same test FlyAudio uses to tell its own fly from a copy of somebody else's: only the
    // player whose fly it is gets the view jolt, and only they hear it flat rather than from over there.
    bool IsOwnFly => fly != null && fly.enabled;

    void Awake()
    {
        fly = GetComponent<FlyController>();
        body = GetComponent<CharacterController>();
        health = GetComponent<FlyHealth>();
        knockback = GetComponentInParent<FlyKnockbackNetwork>();
        // The camera is the fly root's own child rather than this body's, so a search down from here
        // would miss it - the same reason FlyHealth looks for FlyAiming from the root.
        shake = transform.root.GetComponentInChildren<CameraShake>(true);
    }

    void OnEnable()
    {
        fly.Bumped += OnBumped;
        if (knockback != null)
            knockback.Knocked += OnKnocked;
        if (health != null)
            health.Died += OnDied;
    }

    // Nothing switches this component off in normal play, and FlyHealth must not be made to: it
    // switches its list off before it raises Died, so a FlyImpact in that list would unsubscribe
    // itself a line before the death it exists to show. This is here for leaving the scene.
    void OnDisable()
    {
        fly.Bumped -= OnBumped;
        if (knockback != null)
            knockback.Knocked -= OnKnocked;
        if (health != null)
            health.Died -= OnDied;
    }

    void OnBumped(FlyController.Bump bump)
    {
        // 0 at the slowest bump that counts, 1 at Full Bump Speed. Everything below reads off it, so
        // one number tunes how hard a bump of a given speed lands.
        float force = Mathf.Clamp01(bump.speed / fullBumpSpeed);
        Play(bumpSource, bumpSounds, bumpVolume * force, Mathf.Lerp(1f, bumpPitch, force));
        // Out of the surface, which is where dust off a wall goes.
        Spawn(bumpEffect, bump.point, bump.normal, Mathf.Lerp(minBumpEffectScale, 1f, force));
        Jolt(bumpShake * force);
    }

    void OnKnocked(Vector3 velocity)
    {
        lastHitDirection = velocity.sqrMagnitude > 0f ? velocity.normalized : Vector3.up;
        Play(hurtSource, hitSounds, hitVolume, hitPitch);
        Spawn(bloodEffect, Center(), lastHitDirection, 1f);
        Jolt(hitShake);
    }

    // The hit that killed it. Online this arrives from the server a ping after the knock, and offline
    // in the same frame as it, so the blow's own sound and burst have already played: this is the
    // second, bigger helping on top, not a replacement for them.
    void OnDied()
    {
        Play(hurtSource, deathSounds, deathVolume, deathPitch);
        Spawn(deathEffect != null ? deathEffect : bloodEffect, Center(), lastHitDirection, 1f);
        Jolt(deathShake);
    }

    // The middle of the fly rather than its feet, which is where a blow lands and where its insides
    // are. Falls back to the object's own position when there is no collider to measure.
    Vector3 Center()
    {
        return body != null ? body.bounds.center : transform.position;
    }

    void Play(AudioSource source, AudioClip[] clips, float volume, float pitch)
    {
        if (source == null || clips.Length == 0 || volume <= 0f)
            return;
        source.pitch = Mathf.Max(pitch, MinPitch);
        // Flat for the player whose fly it is, positional for everyone else, the same split
        // FlyKnockbackNetwork makes for the hit sound: the orbit camera sits several units back, where
        // a positional sound would be faint on the one machine that most needs to hear it.
        source.spatialBlend = IsOwnFly ? 0f : 1f;
        source.volume = GameAudio.Scale(volume);
        source.clip = clips[Random.Range(0, clips.Length)];
        source.Play();
    }

    // The effect prefabs face along their own +Z, like the spit's splash, so +Z is pointed down the
    // direction the effect should spray.
    void Spawn(GameObject prefab, Vector3 position, Vector3 forward, float scale)
    {
        if (prefab == null)
            return;
        // LookRotation hands back identity and logs for a zero-length direction.
        Quaternion facing = forward.sqrMagnitude > 0f ? Quaternion.LookRotation(forward) : Quaternion.identity;
        GameObject effect = Instantiate(prefab, position, facing);
        // Not parented to the fly: the burst has to stay where the fly was hit while the fly is flung
        // out of it, and a dead fly's body is moved to its respawn point before the guts have faded.
        effect.transform.localScale = prefab.transform.localScale * scale;
        Destroy(effect, effectLifetime);
    }

    // Only this machine's own fly jolts the view. Someone else's being slapped across the kitchen is
    // their problem, and OwnerOnlyControls leaves their CameraShake running on a switched-off camera.
    void Jolt(float amount)
    {
        if (shake != null && amount > 0f && IsOwnFly)
            shake.Shake(amount);
    }
}
