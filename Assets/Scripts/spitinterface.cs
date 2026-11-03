using UnityEngine;
using UnityEngine.UI;

// Shows the fly's spit shots on screen: one Image per shot, swapped between a full and an empty
// sprite. FlySpit calls ShowShots whenever a shot is used or refilled, and PlayEmpty when the fly
// tries to spit with none left.
// Each change is animated in code (no Animator): a used drop bounces and wobbles, a refilled drop
// pops in from nothing, an empty click shakes the whole row and flashes it red, and while a Big
// Shot is charged the drops it will use pulse and glow.
// Only scale and rotation of the drops are animated, never their position, because the row's
// HorizontalLayoutGroup owns where each drop sits. The row shake moves this object instead.
// Unscaled time, so the UI still animates if the game is paused with timeScale 0.
// Put it on the UI object that holds the drop Images, then drag it into FlySpit's Spit Interface slot.
public class spitinterface : MonoBehaviour
{
    enum DropAnimation { None, Use, Fill }

    [Tooltip("One Image per shot, left to right. Give it as many as FlySpit's Max Shots (4).")]
    public Image[] spitImages = new Image[4];
    [Tooltip("Shown on an Image while that shot is available.")]
    public Sprite fullSprite;
    [Tooltip("Shown on an Image once that shot has been used.")]
    public Sprite emptySprite;

    [Header("Shot Used")]
    [SerializeField, Range(0.05f, 1f)] float useDuration = 0.3f;
    [Tooltip("How much bigger the drop gets at the top of the bounce. 0.45 = 45%.")]
    [SerializeField, Range(0f, 1f)] float useBounce = 0.45f;
    [Tooltip("Degrees the drop wobbles side to side.")]
    [SerializeField, Range(0f, 45f)] float useWobble = 20f;

    [Header("Shot Refilled")]
    [SerializeField, Range(0.05f, 1f)] float fillDuration = 0.35f;
    [Tooltip("How far the pop-in grows past full size before settling. 1.7 is a lively default, 0 is none.")]
    [SerializeField, Range(0f, 4f)] float fillOvershoot = 1.7f;

    [Header("Out Of Shots")]
    [SerializeField, Range(0.05f, 1f)] float emptyDuration = 0.3f;
    [Tooltip("How far the row shakes sideways, in UI pixels.")]
    [SerializeField, Range(0f, 40f)] float emptyShakeDistance = 8f;
    [SerializeField] Color emptyFlashColor = new Color(1f, 0.3f, 0.3f);

    [Header("Big Shot Charged")]
    [Tooltip("How fast the drops a Big Shot will use pulse, in pulses per second.")]
    [SerializeField, Range(0.5f, 10f)] float chargePulseRate = 3f;
    [Tooltip("How much bigger the pulsing drops get. 0.2 = 20%.")]
    [SerializeField, Range(0f, 1f)] float chargePulseScale = 0.2f;
    [SerializeField] Color chargeGlowColor = new Color(1f, 0.95f, 0.45f);

    DropAnimation[] animations;
    float[] animationTimes;
    bool[] isFull;
    Color[] restColors;
    RectTransform row;
    Vector2 rowRestPosition;
    // Parked past the end so nothing shakes until PlayEmpty.
    float emptyTime = float.MaxValue;
    // Drops pulsing for a charged Big Shot, from chargeFirst up to but not including chargeEnd; -1 = none.
    int chargeFirst = -1;
    int chargeEnd = -1;
    float chargeTime;

    // The first shotsLeft Images show full, the rest show empty. Drops that changed are animated;
    // the very first call (and any call in the Editor) just sets the sprites.
    public void ShowShots(int shotsLeft)
    {
        bool isFirstShow = EnsureState();
        for (int i = 0; i < spitImages.Length; i++)
        {
            // An empty slot in the array is skipped rather than throwing, so one missing Image does
            // not stop the rest of the row from updating.
            if (spitImages[i] == null)
                continue;
            bool shouldBeFull = i < shotsLeft;
            if (!isFirstShow && shouldBeFull != isFull[i])
                StartAnimation(i, shouldBeFull ? DropAnimation.Fill : DropAnimation.Use);
            isFull[i] = shouldBeFull;
            spitImages[i].sprite = shouldBeFull ? fullSprite : emptySprite;
        }
    }

    // Shakes the row and flashes it red: the fly tried to spit with nothing left.
    public void PlayEmpty()
    {
        EnsureState();
        // Rest position is taken only when a shake starts, so a row moved in the Editor or by layout
        // is not snapped back to an old spot.
        if (emptyTime >= emptyDuration)
            rowRestPosition = row.anchoredPosition;
        emptyTime = 0f;
    }

    // Pulses the drops a Big Shot would use: the last cost full ones, which are the ones it empties.
    // Safe to call every frame; it only restarts when the set of drops changes.
    public void ShowCharge(int shotsLeft, int cost)
    {
        EnsureState();
        int first = Mathf.Max(0, shotsLeft - cost);
        int end = Mathf.Min(shotsLeft, spitImages.Length);
        if (first == chargeFirst && end == chargeEnd)
            return;
        ClearCharge();
        chargeFirst = first;
        chargeEnd = end;
        chargeTime = 0f;
    }

    public void ClearCharge()
    {
        if (chargeFirst < 0)
            return;
        for (int i = chargeFirst; i < chargeEnd; i++)
        {
            if (spitImages[i] == null)
                continue;
            spitImages[i].color = restColors[i];
            if (animations[i] == DropAnimation.None)
                spitImages[i].rectTransform.localScale = Vector3.one;
        }
        chargeFirst = chargeEnd = -1;
    }

    void Update()
    {
        if (animations == null)
            return;
        float deltaTime = Time.unscaledDeltaTime;
        for (int i = 0; i < spitImages.Length; i++)
            AnimateDrop(i, deltaTime);
        AnimateEmpty(deltaTime);
        AnimateCharge(deltaTime);
    }

    // A disabled row would otherwise freeze mid-bounce and stay that way when shown again.
    void OnDisable()
    {
        if (animations == null)
            return;
        for (int i = 0; i < spitImages.Length; i++)
            StopAnimation(i);
        if (emptyTime < emptyDuration)
            EndEmpty();
        ClearCharge();
    }

    // Returns true when the state was just created, i.e. there is no previous display to animate from.
    bool EnsureState()
    {
        if (animations != null && animations.Length == spitImages.Length)
            return false;

        int count = spitImages.Length;
        animations = new DropAnimation[count];
        animationTimes = new float[count];
        isFull = new bool[count];
        restColors = new Color[count];
        for (int i = 0; i < count; i++)
            restColors[i] = spitImages[i] != null ? spitImages[i].color : Color.white;
        row = (RectTransform)transform;
        return true;
    }

    void StartAnimation(int index, DropAnimation animation)
    {
        animations[index] = animation;
        animationTimes[index] = 0f;
    }

    void StopAnimation(int index)
    {
        animations[index] = DropAnimation.None;
        if (spitImages[index] == null)
            return;
        spitImages[index].rectTransform.localScale = Vector3.one;
        spitImages[index].rectTransform.localRotation = Quaternion.identity;
    }

    void AnimateDrop(int index, float deltaTime)
    {
        if (animations[index] == DropAnimation.None || spitImages[index] == null)
            return;

        bool isUse = animations[index] == DropAnimation.Use;
        animationTimes[index] += deltaTime;
        float t = animationTimes[index] / (isUse ? useDuration : fillDuration);
        if (t >= 1f)
        {
            StopAnimation(index);
            return;
        }

        RectTransform drop = spitImages[index].rectTransform;
        if (isUse)
        {
            // Three half-waves that die out: big, small, settle.
            float wave = Mathf.Sin(t * Mathf.PI * 3f) * (1f - t);
            drop.localScale = Vector3.one * (1f + useBounce * wave);
            drop.localRotation = Quaternion.Euler(0f, 0f, useWobble * wave);
        }
        else
        {
            drop.localScale = Vector3.one * EaseOutBack(t, fillOvershoot);
        }
    }

    void AnimateEmpty(float deltaTime)
    {
        if (emptyTime >= emptyDuration)
            return;

        emptyTime += deltaTime;
        float t = emptyTime / emptyDuration;
        if (t >= 1f)
        {
            EndEmpty();
            return;
        }

        float fade = 1f - t;
        row.anchoredPosition = rowRestPosition + Vector2.right * (Mathf.Sin(t * Mathf.PI * 6f) * emptyShakeDistance * fade);
        for (int i = 0; i < spitImages.Length; i++)
        {
            if (spitImages[i] != null)
                spitImages[i].color = Color.Lerp(restColors[i], emptyFlashColor, fade);
        }
    }

    void AnimateCharge(float deltaTime)
    {
        if (chargeFirst < 0)
            return;

        chargeTime += deltaTime;
        // 0 to 1 and back, chargePulseRate times a second.
        float pulse = 0.5f + 0.5f * Mathf.Sin(chargeTime * chargePulseRate * 2f * Mathf.PI);
        for (int i = chargeFirst; i < chargeEnd; i++)
        {
            // A drop still popping in from a refill finishes that first instead of being fought over.
            if (spitImages[i] == null || animations[i] != DropAnimation.None)
                continue;
            spitImages[i].rectTransform.localScale = Vector3.one * (1f + chargePulseScale * pulse);
            spitImages[i].color = Color.Lerp(restColors[i], chargeGlowColor, pulse);
        }
    }

    void EndEmpty()
    {
        emptyTime = float.MaxValue;
        row.anchoredPosition = rowRestPosition;
        for (int i = 0; i < spitImages.Length; i++)
        {
            if (spitImages[i] != null)
                spitImages[i].color = restColors[i];
        }
    }

    // 0 at t = 0, 1 at t = 1, overshooting past 1 on the way by an amount set by overshoot.
    static float EaseOutBack(float t, float overshoot)
    {
        float u = t - 1f;
        return 1f + (overshoot + 1f) * u * u * u + overshoot * u * u;
    }
}
