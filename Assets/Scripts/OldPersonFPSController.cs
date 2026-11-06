using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

// First-person control of the old person: WASD (ZQSD on AZERTY) walks, the mouse looks, Space jumps,
// Shift runs. The mouse turns the whole body left/right and tilts only the camera up/down, so the body
// always faces where the player looks horizontally.
// Put this on the old_person root (the object with the Animator), together with a CharacterController,
// and disable the prefab's CapsuleCollider there: the CharacterController is the collider, and a second
// one on the same body would push against it. The camera goes in as a child at eye height.
// The camera is a field, not Camera.main, so each player in a future multiplayer scene can own its own.
// The body is seen twice. The real body sits on a layer this camera's Culling Mask leaves out (set up
// in the scene), so the Scene view and other cameras see the whole old person. On Play this script adds
// a headless copy on First Person Body Layer, which only this camera sees, so the player sees their own
// arms and legs without the inside of the face filling the view. The model is one mesh, so a layer
// alone cannot hide just the head; the copy drops it by remapping the Head bone (see CreateFirstPersonBody).
[RequireComponent(typeof(CharacterController))]
public class OldPersonFPSController : MonoBehaviour
{
    // Project-wide actions (InputSystem_Actions). Bindings are physical keys, so WASD is ZQSD on AZERTY.
    const string MoveActionName = "Player/Move";
    const string LookActionName = "Player/Look";
    const string JumpActionName = "Player/Jump";
    const string SprintActionName = "Player/Sprint";
    // Small constant push into the ground while standing, so isGrounded stays true every frame
    // instead of flickering, and walking off an edge starts falling immediately.
    const float GroundStickSpeed = 2f;
    // Float parameter on old_person.controller: above 0.1 plays Walk, below plays Idle.
    static readonly int SpeedAnimParameter = Animator.StringToHash("Speed");

    [Tooltip("The first-person camera, a child of this object at eye height.")]
    [SerializeField] Camera viewCamera;
    [Tooltip("The old person's Animator (with the float parameter \"Speed\"). Found automatically if left empty.")]
    [SerializeField] Animator animator;
    [SerializeField] float walkSpeed = 1.5f;
    [SerializeField] float sprintSpeed = 3f;
    [SerializeField] float jumpHeight = 0.6f;
    [SerializeField] float gravity = -9.81f;
    [Tooltip("Same scale as OrbitCamera's Mouse Sensitivity, so both players feel alike.")]
    [SerializeField] float mouseSensitivity = 0.1f;
    [SerializeField, Range(-89f, 0f)] float minPitch = -80f;
    [SerializeField, Range(0f, 89f)] float maxPitch = 80f;

    [Header("Head follows camera")]
    // A real neck cannot bend 80°, so the head only tilts part of the way and the eyes do the rest.
    [Tooltip("Largest up/down tilt (degrees) the neck and head take together. The camera can still look further.")]
    [SerializeField, Range(0f, 89f)] float maxHeadPitch = 60f;
    // Split between two bones so the bend reads as a neck, not a head hinged at the chin.
    [Tooltip("Part of the tilt the neck takes; the head takes the rest.")]
    [SerializeField, Range(0f, 1f)] float neckShare = 0.4f;
    // The eyes sit above and in front of the neck, so a real head tilting down carries them forward
    // and down; a camera that only rotated in place would look down at the chest instead of past it.
    [Tooltip("How far the camera swings with the head around the neck when looking up/down. 0 = camera stays put, 1 = moves like the real head, above 1 = exaggerated.")]
    [SerializeField, Range(0f, 2f)] float cameraFollowsHead = 1f;

    [Header("First-person body")]
    [Tooltip("Layer for the headless body copy. Tick it in View Camera's Culling Mask and untick it in every other camera's.")]
    [SerializeField] string firstPersonBodyLayer = "FirstPersonBody";

    CharacterController controller;
    Transform neckBone;
    Transform headBone;
    InputAction moveAction;
    InputAction lookAction;
    InputAction jumpAction;
    InputAction sprintAction;
    float verticalVelocity;
    float pitch;
    // In this object's local space: where the camera sits when looking straight ahead (as placed in
    // the scene), and the neck point it swings around.
    Vector3 eyePosition;
    Vector3 neckPivot;

    void Awake()
    {
        controller = GetComponent<CharacterController>();
        // Throws if an action is missing: a player that silently ignores a control is worse than a clear error.
        moveAction = InputSystem.actions.FindAction(MoveActionName, throwIfNotFound: true);
        lookAction = InputSystem.actions.FindAction(LookActionName, throwIfNotFound: true);
        jumpAction = InputSystem.actions.FindAction(JumpActionName, throwIfNotFound: true);
        sprintAction = InputSystem.actions.FindAction(SprintActionName, throwIfNotFound: true);

        if (animator == null)
            animator = GetComponentInChildren<Animator>();
        // Fails open: animation is cosmetic, so the old person stays controllable without it.
        if (animator == null)
            Debug.LogWarning("OldPersonFPSController: no Animator found; walking without animation.", this);
        else
        {
            // The real body is on a layer this camera does not draw, so Unity can decide it is invisible
            // and stop animating it (the model's default culling), freezing the walk; it must always animate.
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            FindHeadBones();
        }

        if (viewCamera == null)
            viewCamera = GetComponentInChildren<Camera>();
        if (viewCamera == null)
        {
            // Fails closed: looking and walking are relative to the camera, so without one there is no player.
            Debug.LogError("OldPersonFPSController: no camera. Add a Camera as a child at eye height and assign View Camera.", this);
            enabled = false;
            return;
        }
        pitch = Mathf.Clamp(Mathf.DeltaAngle(0f, viewCamera.transform.localEulerAngles.x), minPitch, maxPitch);
        // Read before the first animated frame, so the neck is at its rest position. The camera must be
        // a direct child of this object for its local position to be in the same space as the pivot.
        eyePosition = viewCamera.transform.localPosition;
        // Fails open: without a neck bone the camera stays at the eye position and only rotates.
        neckPivot = neckBone != null ? transform.InverseTransformPoint(neckBone.position) : eyePosition;
    }

    void Start()
    {
        CreateFirstPersonBody();
    }

    void OnEnable()
    {
        moveAction.Enable();
        lookAction.Enable();
        jumpAction.Enable();
        sprintAction.Enable();
        Cursor.lockState = CursorLockMode.Locked;
    }

    void OnDisable()
    {
        Cursor.lockState = CursorLockMode.None;
    }

    void Update()
    {
        Look(lookAction.ReadValue<Vector2>());
        float horizontalSpeed = Move(moveAction.ReadValue<Vector2>());
        if (animator != null)
            animator.SetFloat(SpeedAnimParameter, horizontalSpeed);
    }

    // LateUpdate because the Animator poses the bones between Update and LateUpdate; tilting them in
    // Update would be overwritten by the Idle/Walk clip the same frame.
    void LateUpdate()
    {
        if (headBone == null)
            return;
        float headPitch = Mathf.Clamp(pitch, -maxHeadPitch, maxHeadPitch);
        if (neckBone != null)
        {
            TiltBone(neckBone, headPitch * neckShare);
            headPitch *= 1f - neckShare;
        }
        TiltBone(headBone, headPitch);
    }

    // Tilts around the body's own right axis (positive = down, like the camera), on top of the
    // animated pose, so it works whatever way the bone's local axes point in the rig.
    void TiltBone(Transform bone, float degrees)
    {
        bone.rotation = Quaternion.AngleAxis(degrees, transform.right) * bone.rotation;
    }

    void FindHeadBones()
    {
        // GetBoneTransform only works on a Humanoid rig; old_person.fbx is imported as Humanoid.
        neckBone = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Neck) : null;
        headBone = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        // Fails open: the head tilt is cosmetic, so the player still walks and looks without it.
        if (headBone == null)
            Debug.LogWarning("OldPersonFPSController: no Head bone on the Animator (rig not Humanoid?); the head will not follow the camera.", this);
    }

    // Adds, for every body mesh, a copy that only this camera sees, skinned to the same bones so it
    // walks, tilts its head and slaps exactly like the real body, but with the Head bone swapped for a
    // shrunken stand-in. The head and hair are weighted to Head (make_old_person.py, ALLOWED["head"]),
    // so they shrink to a point and vanish; the rest of the body is untouched.
    void CreateFirstPersonBody()
    {
        int layer = LayerMask.NameToLayer(firstPersonBodyLayer);
        // Both fail open: the player can still walk, look and slap, they just do not see their own body.
        if (layer < 0)
        {
            Debug.LogWarning($"OldPersonFPSController: no layer named \"{firstPersonBodyLayer}\"; add it in Project Settings > Tags and Layers to see your own body.", this);
            return;
        }
        if (headBone == null)
        {
            Debug.LogWarning("OldPersonFPSController: no Head bone, so the head cannot be removed from the first-person body; not creating it.", this);
            return;
        }

        Transform shrunkenHead = new GameObject("Head (first person, shrunk)").transform;
        shrunkenHead.SetParent(headBone, false);
        // Nearly zero, not zero: an all-zero scale makes the skinning matrix singular, which can turn
        // the normals of partly head-weighted neck vertices into NaN and flicker.
        shrunkenHead.localScale = Vector3.one * 0.0001f;

        foreach (SkinnedMeshRenderer body in GetComponentsInChildren<SkinnedMeshRenderer>())
            CopyWithoutHead(body, layer, shrunkenHead);
    }

    void CopyWithoutHead(SkinnedMeshRenderer original, int layer, Transform shrunkenHead)
    {
        Transform source = original.transform;
        GameObject copyObject = new GameObject(original.name + " (first person)") { layer = layer };
        copyObject.transform.SetParent(source.parent, false);
        copyObject.transform.SetLocalPositionAndRotation(source.localPosition, source.localRotation);
        copyObject.transform.localScale = source.localScale;

        SkinnedMeshRenderer copy = copyObject.AddComponent<SkinnedMeshRenderer>();
        copy.sharedMesh = original.sharedMesh;
        copy.sharedMaterials = original.sharedMaterials;
        copy.rootBone = original.rootBone;
        copy.localBounds = original.localBounds;
        // The real body already casts the shadow; a second one would darken it.
        copy.shadowCastingMode = ShadowCastingMode.Off;

        Transform[] bones = original.bones;
        for (int i = 0; i < bones.Length; i++)
        {
            if (bones[i] == headBone)
                bones[i] = shrunkenHead;
        }
        copy.bones = bones;
    }

    // Mouse left/right turns the body; up/down tilts only the camera, clamped so the view never flips.
    void Look(Vector2 look)
    {
        transform.Rotate(0f, look.x * mouseSensitivity, 0f);
        pitch = Mathf.Clamp(pitch - look.y * mouseSensitivity, minPitch, maxPitch);
        viewCamera.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);

        // Same clamp as the head bones, so the camera stops moving where the head stops tilting and
        // the eyes stay inside the (hidden) head.
        float headPitch = Mathf.Clamp(pitch, -maxHeadPitch, maxHeadPitch) * cameraFollowsHead;
        viewCamera.transform.localPosition = neckPivot + Quaternion.Euler(headPitch, 0f, 0f) * (eyePosition - neckPivot);
    }

    // Walks along the ground relative to where the body faces, with gravity and jumping.
    // Returns the horizontal speed actually moved, for the walk animation.
    float Move(Vector2 move)
    {
        float speed = sprintAction.IsPressed() ? sprintSpeed : walkSpeed;
        // ClampMagnitude: diagonal keys would otherwise walk ~1.4x faster than straight ahead.
        Vector3 horizontal = transform.TransformDirection(new Vector3(move.x, 0f, move.y));
        horizontal = Vector3.ClampMagnitude(horizontal, 1f) * speed;

        UpdateVerticalVelocity();
        Vector3 before = transform.position;
        controller.Move((horizontal + Vector3.up * verticalVelocity) * Time.deltaTime);

        // Measured from the real move, so walking into a wall plays Idle instead of walking in place.
        Vector3 moved = transform.position - before;
        moved.y = 0f;
        return Time.deltaTime > 0f ? moved.magnitude / Time.deltaTime : 0f;
    }

    void UpdateVerticalVelocity()
    {
        if (!controller.isGrounded)
        {
            verticalVelocity += gravity * Time.deltaTime;
            return;
        }
        // Jump speed for the chosen height: v = sqrt(2 * h * g).
        verticalVelocity = jumpAction.WasPressedThisFrame()
            ? Mathf.Sqrt(2f * jumpHeight * -gravity)
            : -GroundStickSpeed;
    }
}
