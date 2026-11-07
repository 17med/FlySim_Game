using UnityEngine;

/// <summary>
/// Complete Diptox Aerosol Spray Controller for Unity.
/// - Press/Hold Left Mouse Button (or Space, or call StartSpray()/StopSpray() from a UI Button)
/// - Smoothly animates the 3D Nozzle Button ('02_Pressable_Spray_Button') pressing down (-4.5mm)
/// - Emits a realistic volumetric insecticide particle stream from the nozzle orifice (+Z)
/// - Automatically builds and configures a Unity ParticleSystem at runtime if none is assigned!
/// </summary>
public class DiptoxSprayController : MonoBehaviour
{
    [Header("Input Settings")]
    public bool sprayOnLeftClick = true;
    public KeyCode sprayKey = KeyCode.Space;

    [Header("Button Press Animation")]
    public Transform sprayButtonTransform;
    public float buttonPressDepth = 0.0045f; // 4.5 mm downward press
    public float buttonAnimSpeed = 22f;

    [Header("Spray Particle System")]
    public ParticleSystem sprayParticles;
    public Texture2D customParticleTexture; // Optional: assign Diptox_Spray_Particle_Sprite.png
    public AudioSource sprayAudioSource;

    [Header("Gameplay / Insect Hit Detection")]
    public float sprayRange = 2.5f;
    public float sprayRadius = 0.25f;
    public LayerMask hitLayers = ~0;

    private Vector3 _buttonRestLocalPos;
    private bool _hasButton;
    private bool _isSpraying;
    private float _currentPressAmount;

    void Start()
    {
        // 1. Auto-locate the separated 02_Pressable_Spray_Button child if not manually assigned
        if (sprayButtonTransform == null)
        {
            foreach (Transform child in GetComponentsInChildren<Transform>(true))
            {
                if (child.name.Contains("02_Pressable_Spray_Button") || child.name.Contains("Spray_Button"))
                {
                    sprayButtonTransform = child;
                    break;
                }
            }
        }

        if (sprayButtonTransform != null)
        {
            _buttonRestLocalPos = sprayButtonTransform.localPosition;
            _hasButton = true;
        }

        // Hide baked 3D mesh particle puffs if using the real-time Unity ParticleSystem
        foreach (Transform child in GetComponentsInChildren<Transform>(true))
        {
            if (child.name.StartsWith("03_Spray_Mist_Cone") || child.name.StartsWith("04_Spray_Particles_Puff"))
            {
                child.gameObject.SetActive(false);
            }
        }

        // 2. Auto-create a realistic Aerosol Mist ParticleSystem at the nozzle tip if not assigned
        if (sprayParticles == null)
        {
            sprayParticles = CreateAerosolParticleSystem();
        }
        sprayParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
    }

    void Update()
    {
        bool inputActive = (sprayOnLeftClick && Input.GetMouseButton(0)) || Input.GetKey(sprayKey);
        SetSpraying(inputActive);

        // Animate the 3D nozzle button pressing down and springing back up
        float targetPress = _isSpraying ? 1f : 0f;
        _currentPressAmount = Mathf.MoveTowards(_currentPressAmount, targetPress, buttonAnimSpeed * Time.deltaTime);

        if (_hasButton)
        {
            Vector3 offset = new Vector3(0f, -buttonPressDepth * _currentPressAmount, 0f);
            sprayButtonTransform.localPosition = _buttonRestLocalPos + offset;
        }

        if (_isSpraying)
        {
            DetectSprayHits();
        }
    }

    /// <summary>
    /// Can also be hooked up to a Unity UI Button (OnPointerDown -> StartSpray, OnPointerUp -> StopSpray).
    /// </summary>
    public void StartSpray() => SetSpraying(true);
    public void StopSpray()  => SetSpraying(false);

    public void SetSpraying(bool active)
    {
        if (_isSpraying == active) return;
        _isSpraying = active;

        if (_isSpraying)
        {
            if (sprayParticles != null && !sprayParticles.isPlaying)
                sprayParticles.Play();
            if (sprayAudioSource != null && !sprayAudioSource.isPlaying)
                sprayAudioSource.Play();
        }
        else
        {
            if (sprayParticles != null && sprayParticles.isPlaying)
                sprayParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            if (sprayAudioSource != null && sprayAudioSource.isPlaying)
                sprayAudioSource.Stop();
        }
    }

    private void DetectSprayHits()
    {
        Transform origin = sprayParticles != null ? sprayParticles.transform : transform;
        RaycastHit[] hits = Physics.SphereCastAll(origin.position, sprayRadius, origin.forward, sprayRange, hitLayers);
        foreach (var h in hits)
        {
            h.collider.SendMessage("OnHitByDiptox", Time.deltaTime, SendMessageOptions.DontRequireReceiver);
        }
    }

    private ParticleSystem CreateAerosolParticleSystem()
    {
        GameObject psObj = new GameObject("Diptox_Aerosol_ParticleSystem");
        Transform parentTarget = sprayButtonTransform != null ? sprayButtonTransform : transform;
        psObj.transform.SetParent(parentTarget, false);
        // Position right at the front nozzle hole (+Z)
        psObj.transform.localPosition = sprayButtonTransform != null ? new Vector3(0f, 0.0165f, 0.022f) : new Vector3(0f, 0.268f, 0.022f);
        psObj.transform.localRotation = Quaternion.identity; // Shoots along +Z

        ParticleSystem ps = psObj.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.loop = true;
        main.playOnAwake = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.65f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(2.8f, 4.8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.018f, 0.045f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.gravityModifier = 0.04f; // Slight downward drift of mist droplets
        main.maxParticles = 600;

        var emission = ps.emission;
        emission.enabled = true;
        emission.rateOverTime = 220f;

        var shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle = 13.5f;
        shape.radius = 0.0025f;

        // Air drag slowdown (aerosol shoots out fast, then slows down in air)
        var limitVel = ps.limitVelocityOverLifetime;
        limitVel.enabled = true;
        limitVel.limit = 0.6f;
        limitVel.dampen = 0.18f;

        // Mist expands as it travels forward
        var sizeOverLife = ps.sizeOverLifetime;
        sizeOverLife.enabled = true;
        AnimationCurve sizeCurve = new AnimationCurve(
            new Keyframe(0.0f, 0.35f),
            new Keyframe(0.3f, 1.25f),
            new Keyframe(1.0f, 3.20f)
        );
        sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        // Smooth fade-in and fade-out
        var colOverLife = ps.colorOverLifetime;
        colOverLife.enabled = true;
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(new Color(0.96f, 0.98f, 1.0f), 0.0f),
                new GradientColorKey(new Color(0.92f, 0.96f, 1.0f), 1.0f)
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(0.0f, 0.0f),
                new GradientAlphaKey(0.45f, 0.12f),
                new GradientAlphaKey(0.22f, 0.55f),
                new GradientAlphaKey(0.0f, 1.0f)
            }
        );
        colOverLife.color = new ParticleSystem.MinMaxGradient(grad);

        // Turbulent air swirl
        var noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.18f;
        noise.frequency = 1.8f;
        noise.scrollSpeed = 1.2f;

        var renderer = psObj.GetComponent<ParticleSystemRenderer>();
        Shader pShader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (pShader == null) pShader = Shader.Find("Particles/Standard Unlit");
        if (pShader == null) pShader = Shader.Find("Legacy Shaders/Particles/Alpha Blended");
        if (pShader != null)
        {
            Material mat = new Material(pShader);
            if (customParticleTexture != null)
                mat.mainTexture = customParticleTexture;
            renderer.material = mat;
        }

        return ps;
    }
}
