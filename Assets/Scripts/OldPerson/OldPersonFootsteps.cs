using UnityEngine;

// Loops the old man's footsteps while he walks and stops them when he stands still.
// Reads the Animator's "Speed" rather than OldPersonFPSController, because on another player's copy
// the controller is switched off (OwnerOnlyControls) while NetworkAnimator keeps "Speed" up to date,
// so the fly hears him coming.
// His own steps play flat (2D): the Kitchen scales him up, so his feet are far below his camera and
// a positional sound there would be almost silent to him. Other players hear them from where he is.
[RequireComponent(typeof(AudioSource))]
public class OldPersonFootsteps : MonoBehaviour
{
    // Matches the Walk transition on old_person.controller, so the steps start with the walk animation.
    const float WalkingSpeed = 0.1f;

    [SerializeField] AudioClip footstepSound;
    [SerializeField, Range(0f, 1f)] float volume = 1f;
    [Tooltip("The old person's Animator (with the float parameter \"Speed\"). Found automatically if left empty.")]
    [SerializeField] Animator animator;

    AudioSource source;
    OldPersonFPSController controller;

    void Awake()
    {
        source = GetComponent<AudioSource>();
        source.clip = footstepSound;
        source.loop = true;
        source.playOnAwake = false;
        controller = GetComponentInParent<OldPersonFPSController>();
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
        // Fails open: footsteps are cosmetic, so he walks silently without an Animator to read.
        if (animator == null)
            Debug.LogWarning("OldPersonFootsteps: no Animator found; walking without footstep sounds.", this);
    }

    void Update()
    {
        bool isOwnBody = controller != null && controller.enabled;
        source.spatialBlend = isOwnBody ? 0f : 1f;
        source.volume = GameAudio.Scale(volume);

        bool isWalking = animator != null && animator.GetFloat(OldPersonFPSController.SpeedAnimParameter) > WalkingSpeed;
        if (isWalking == source.isPlaying)
            return;
        if (isWalking)
            source.Play();
        else
            source.Stop();
    }
}
