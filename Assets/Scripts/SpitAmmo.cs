using System;
using UnityEngine;

// The fly's spit shots: how many it holds, spending them, and getting them back over time.
// A plain class shown inside FlySpit's Inspector (the "Ammo" foldout) rather than its own component,
// so there is nothing extra to add to the fly. It was moved out of FlySpit to keep that file to the
// shooting itself; FlySpit owns when shots are spent and tells the HUD.
[Serializable]
public class SpitAmmo
{
    [Tooltip("Shots the fly holds when full. A normal spit uses one, a Big Shot uses Big Shot Cost.")]
    [SerializeField, Range(1, 20)] int maxShots = 4;
    [Tooltip("Seconds to get one shot back. Shots come back one at a time until full.")]
    [SerializeField, Min(0.05f)] float refillTime = 1.5f;
    [Tooltip("Seconds after the last shot before refilling starts. 0 = refill keeps going while shooting.")]
    [SerializeField, Min(0f)] float refillDelay = 1f;

    int shotsLeft;
    float refillProgress;
    float refillDelayLeft;

    public int ShotsLeft => shotsLeft;
    public int MaxShots => maxShots;
    // How far (0 to 1) the next shot is refilled.
    public float RefillProgress => refillProgress;

    public void Fill()
    {
        shotsLeft = maxShots;
        refillProgress = 0f;
    }

    public bool CanSpend(int cost)
    {
        return shotsLeft >= cost;
    }

    // Takes cost shots and restarts the refill delay. Returns false, spending nothing, if there are
    // not enough.
    public bool TrySpend(int cost)
    {
        if (!CanSpend(cost))
            return false;
        shotsLeft -= cost;
        refillDelayLeft = refillDelay;
        return true;
    }

    // Gives back one shot every Refill Time seconds, once Refill Delay has passed since the last shot.
    // Returns true on the frame a shot comes back, so the caller can update the HUD.
    public bool Tick(float deltaTime)
    {
        if (shotsLeft >= maxShots)
        {
            refillProgress = 0f;
            return false;
        }
        if (refillDelayLeft > 0f)
        {
            refillDelayLeft -= deltaTime;
            return false;
        }

        refillProgress += deltaTime / refillTime;
        if (refillProgress < 1f)
            return false;
        shotsLeft++;
        refillProgress -= 1f;
        return true;
    }
}
