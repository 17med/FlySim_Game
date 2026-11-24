using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace CoffeeAssets
{
    /// <summary>
    /// Animates the coffee liquid level from 0% drunk (full) to 100% drunk (empty).
    /// Attach this to the FlutedGlassMug parent GameObject in Unity.
    /// </summary>
    public class CoffeeDrinkController : MonoBehaviour
    {
        [Header("References")]
        [Tooltip("Assign the CoffeeLiquid child GameObject (pivot at y=0, placed at y=0.01 inside the mug).")]
        public Transform coffeeLiquidTransform;

        [Header("Drink State (0 = Full, 100 = Empty)")]
        [Range(0f, 100f)]
        public float drinkProgress = 0f;

        [Header("Animation Settings")]
        [Tooltip("Total duration in seconds when calling PlayDrinkAnimation().")]
        public float defaultDrinkDuration = 4.0f;
        public AnimationCurve drinkCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 100f);
        public bool hideMeshAt100Percent = true;

        [Header("Events")]
        public UnityEvent onFinishedDrinking;

        private Vector3 initialScale = Vector3.one;
        private Renderer[] liquidRenderers;
        private Coroutine activeRoutine;

        private void Awake()
        {
            if (coffeeLiquidTransform != null)
            {
                initialScale = coffeeLiquidTransform.localScale;
                liquidRenderers = coffeeLiquidTransform.GetComponentsInChildren<Renderer>();
            }
            ApplyProgress(drinkProgress);
        }

        private void OnValidate()
        {
            if (coffeeLiquidTransform != null && Application.isPlaying)
            {
                ApplyProgress(drinkProgress);
            }
            else if (coffeeLiquidTransform != null)
            {
                float fill = Mathf.Clamp01(1f - (drinkProgress / 100f));
                coffeeLiquidTransform.localScale = new Vector3(1f, Mathf.Max(0.001f, fill), 1f);
            }
        }

        /// <summary>
        /// Updates the coffee mesh scale based on 0..100 drink progress.
        /// </summary>
        public void ApplyProgress(float progress0To100)
        {
            drinkProgress = Mathf.Clamp(progress0To100, 0f, 100f);
            if (coffeeLiquidTransform == null) return;

            float fillRatio = 1f - (drinkProgress / 100f);
            coffeeLiquidTransform.localScale = new Vector3(
                initialScale.x,
                initialScale.y * Mathf.Max(0.001f, fillRatio),
                initialScale.z
            );

            if (hideMeshAt100Percent && liquidRenderers != null)
            {
                bool visible = drinkProgress < 99.5f;
                foreach (var r in liquidRenderers)
                {
                    if (r != null) r.enabled = visible;
                }
            }
        }

        /// <summary>
        /// Starts a smooth 0 -> 100 drinking animation.
        /// </summary>
        public void PlayDrinkAnimation()
        {
            PlayDrinkAnimation(defaultDrinkDuration);
        }

        public void PlayDrinkAnimation(float duration)
        {
            if (activeRoutine != null) StopCoroutine(activeRoutine);
            activeRoutine = StartCoroutine(AnimateDrinkRoutine(duration));
        }

        /// <summary>
        /// Refills the coffee back to 0% drunk.
        /// </summary>
        public void ResetCoffee()
        {
            if (activeRoutine != null) StopCoroutine(activeRoutine);
            ApplyProgress(0f);
        }

        private IEnumerator AnimateDrinkRoutine(float duration)
        {
            float elapsed = 0f;
            float startProgress = drinkProgress;
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, duration));
                float target = Mathf.Lerp(startProgress, 100f, drinkCurve.Evaluate(t) / 100f);
                ApplyProgress(target);
                yield return null;
            }
            ApplyProgress(100f);
            onFinishedDrinking?.Invoke();
            activeRoutine = null;
        }
    }
}
