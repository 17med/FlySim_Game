using System.Collections;
using UnityEngine;
using UnityEngine.UI;

// Builds a full-screen black overlay canvas and fades it out each time this component is enabled.
public class Black : MonoBehaviour
{
    [Tooltip("Seconds the screen stays fully black before fading")]
    public float holdTime = 0.5f;

    [Tooltip("Seconds the fade from black to clear takes")]
    public float fadeDuration = 1.5f;

    [Tooltip("Higher draws on top of other canvases")]
    public int sortingOrder = 1000;

    GameObject overlayCanvas;
    Image blackImage;
    Coroutine fadeRoutine;

    void Awake()
    {
        CreateOverlay();
    }

    void OnEnable()
    {
        if (fadeRoutine != null)
            StopCoroutine(fadeRoutine);
        fadeRoutine = StartCoroutine(FadeOut());
    }

    void OnDisable()
    {
        // Coroutines stop when the component is disabled; hide the overlay so the screen isn't left black.
        if (overlayCanvas != null)
            overlayCanvas.SetActive(false);
    }

    void OnDestroy()
    {
        if (overlayCanvas != null)
            Destroy(overlayCanvas);
    }

    void CreateOverlay()
    {
        overlayCanvas = new GameObject("BlackFadeCanvas");
        Canvas canvas = overlayCanvas.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        GameObject imageObject = new GameObject("Black");
        imageObject.transform.SetParent(overlayCanvas.transform, false);
        blackImage = imageObject.AddComponent<Image>();
        blackImage.color = Color.black;
        // Don't block clicks on UI underneath while fading.
        blackImage.raycastTarget = false;

        RectTransform rect = blackImage.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    IEnumerator FadeOut()
    {
        overlayCanvas.SetActive(true);
        SetAlpha(1f);

        yield return new WaitForSeconds(holdTime);

        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            SetAlpha(1f - Mathf.Clamp01(elapsed / fadeDuration));
            yield return null;
        }

        SetAlpha(0f);
        overlayCanvas.SetActive(false);
        fadeRoutine = null;
    }

    void SetAlpha(float alpha)
    {
        Color color = blackImage.color;
        color.a = alpha;
        blackImage.color = color;
    }
}
