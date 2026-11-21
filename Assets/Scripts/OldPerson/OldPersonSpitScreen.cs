using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// Green spit splats on the old man's screen, one per face hit (OldPersonSpitNetwork calls AddSplat).
// Each pops in at a random place, size and spin, runs down a little and fades out; past the cap the
// oldest makes room, and a Big Shot throws a bigger splat.
// Goes on the old man's HUD Canvas with a Template Image under it. The template is hidden and every
// splat is a copy of it, so its material and settings carry over.
public class OldPersonSpitScreen : MonoBehaviour
{
    // A splat placed off a canvas this small would not be seen; guards a zero-size canvas at startup.
    const float MinCanvasSize = 1f;

    [Tooltip("Image under the HUD Canvas that every splat is copied from. Hidden at startup.")]
    [SerializeField] Image template;
    [Tooltip("Splat pictures; each splat picks one at random. Leave empty to use the Template's own sprite.")]
    [SerializeField] Sprite[] splatSprites;
    // White by default: the pictures in Assets/ui game/SpitSplat are already green, and a green tint
    // on top would darken them.
    [Tooltip("Tint multiplied onto the splat picture. Its alpha is the splat's opacity before fading.")]
    [SerializeField] Color splatColor = new Color(1f, 1f, 1f, 0.9f);

    [Header("Size and placement")]
    [Tooltip("Width and height of a normal splat, in canvas units.")]
    [SerializeField, Min(1f)] float splatSize = 300f;
    [Tooltip("Normal splats are scaled by a random amount between 1 - this and 1 + this.")]
    [SerializeField, Range(0f, 0.9f)] float sizeVariation = 0.2f;
    [Tooltip("A Big Shot's splat is this many times bigger.")]
    [SerializeField, Min(1f)] float bigShotScale = 2.5f;
    [Tooltip("Share of the screen, from the centre out, that splat centres are placed in.")]
    [SerializeField, Range(0f, 1f)] float spread = 0.8f;

    [Header("Timing (seconds)")]
    [Tooltip("Seconds a splat stays fully visible.")]
    [SerializeField, Min(0f)] float stayTime = 3f;
    [Tooltip("Seconds a splat takes to fade out after Stay Time.")]
    [SerializeField, Min(0.01f)] float fadeTime = 2f;
    [Tooltip("Seconds a splat takes to pop up to full size when it lands.")]
    [SerializeField, Min(0f)] float popTime = 0.08f;
    [Tooltip("Size a splat pops in from, as a share of its full size.")]
    [SerializeField, Range(0.1f, 1f)] float popStartScale = 0.6f;
    [Tooltip("Canvas units per second a splat slides down while on screen.")]
    [SerializeField, Min(0f)] float slideSpeed = 12f;

    [Header("Limit")]
    [Tooltip("Most splats on screen at once. Past this, the oldest is removed to make room.")]
    [SerializeField, Min(1)] int maxSplats = 12;

    struct Splat
    {
        public Image image;
        public Vector2 landedAt;
        public float age;
    }

    // Oldest first, so the cap removes index 0.
    readonly List<Splat> splats = new List<Splat>();
    // Faded splats kept for reuse, so a stream of hits does not create and destroy UI objects.
    readonly Stack<Image> spareImages = new Stack<Image>();

    void Awake()
    {
        if (template == null)
        {
            // Fails open: splats are cosmetic, so only this component stops and the old man plays on.
            Debug.LogWarning($"{nameof(OldPersonSpitScreen)}: no Template Image assigned on {name}; spit will not show on screen.", this);
            enabled = false;
            return;
        }
        template.gameObject.SetActive(false);
    }

    // Puts one splat on screen.
    public void AddSplat(bool isBigShot)
    {
        if (template == null)
            return;
        if (splats.Count >= maxSplats)
            RemoveSplat(0);

        Image image = spareImages.Count > 0 ? spareImages.Pop() : CreateImage();
        image.sprite = PickSprite();
        float size = splatSize * Random.Range(1f - sizeVariation, 1f + sizeVariation) * (isBigShot ? bigShotScale : 1f);
        RectTransform rect = image.rectTransform;
        rect.sizeDelta = new Vector2(size, size);
        rect.localRotation = Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
        // Last sibling: the newest splat lies on top of the older ones and of the rest of the HUD.
        rect.SetAsLastSibling();
        image.gameObject.SetActive(true);

        Splat splat = new Splat { image = image, landedAt = RandomPlace(), age = 0f };
        splats.Add(splat);
        ApplyLook(splat);
    }

    void Update()
    {
        // Backwards, so removing a finished splat does not skip the next one.
        for (int i = splats.Count - 1; i >= 0; i--)
        {
            Splat splat = splats[i];
            splat.age += Time.deltaTime;
            if (splat.age >= stayTime + fadeTime)
            {
                RemoveSplat(i);
                continue;
            }
            splats[i] = splat;
            ApplyLook(splat);
        }
    }

    // Pop-in, slide and fade, all worked out from the splat's age.
    void ApplyLook(Splat splat)
    {
        float pop = popTime <= 0f ? 1f : Mathf.Lerp(popStartScale, 1f, Mathf.SmoothStep(0f, 1f, splat.age / popTime));
        float fade = 1f - Mathf.Clamp01((splat.age - stayTime) / fadeTime);
        RectTransform rect = splat.image.rectTransform;
        rect.localScale = Vector3.one * pop;
        rect.anchoredPosition = splat.landedAt + Vector2.down * (slideSpeed * splat.age);
        Color color = splatColor;
        color.a *= fade;
        splat.image.color = color;
    }

    void RemoveSplat(int index)
    {
        Image image = splats[index].image;
        splats.RemoveAt(index);
        image.gameObject.SetActive(false);
        spareImages.Push(image);
    }

    // A copy of the template, centred on the canvas so positions are measured from the screen's middle.
    Image CreateImage()
    {
        Image image = Instantiate(template, template.transform.parent);
        image.name = "spit splat";
        // A splat must never swallow clicks meant for the HUD underneath it.
        image.raycastTarget = false;
        RectTransform rect = image.rectTransform;
        rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
        return image;
    }

    Sprite PickSprite()
    {
        if (splatSprites == null || splatSprites.Length == 0)
            return template.sprite;
        return splatSprites[Random.Range(0, splatSprites.Length)];
    }

    Vector2 RandomPlace()
    {
        RectTransform canvas = template.transform.parent as RectTransform;
        Vector2 canvasSize = canvas != null ? canvas.rect.size : Vector2.zero;
        Vector2 half = Vector2.Max(canvasSize, Vector2.one * MinCanvasSize) * (0.5f * spread);
        return new Vector2(Random.Range(-half.x, half.x), Random.Range(-half.y, half.y));
    }
}
