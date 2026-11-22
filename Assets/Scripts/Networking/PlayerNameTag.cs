using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// The player's name on a label floating over their body, so the flyers can tell each other apart in
// the kitchen and know which fly is whose.
// Goes on a spawned body's root next to its NetworkObject (Tools > Kitchen Online > Wire Network
// Players adds it to both bodies). The owner publishes the name it typed in Settings; every other
// machine draws it. The name travels in a NetworkVariable rather than coming from the lobby, because
// the lobby roster is gone by the time the Kitchen loads and only the host ever knew the whole of it.
// Never drawn on the owner's own body: the player knows their own name, and the label would hang in
// the middle of their own view. Nothing happens in the offline Kitchen either, where the bodies are
// placed by hand and never spawned, so single player stays label-free.
// The label is built in code as a root object of its own, not as a child of the body, because the
// bodies are scaled (the old man 45x, the fly's body 2x) and a child would inherit that scale. It
// holds a constant share of the screen however far away it is, turns to face the player, and is
// hidden by walls, so a name can only be read while its owner can actually be seen.
public class PlayerNameTag : NetworkBehaviour
{
    // Any size would do: the label's drawn height at scale 1 is measured once and divided out, so
    // this only decides how much resolution the glyphs are rendered with.
    const float LabelFontSize = 10f;
    // Measured against a line with an ascender and a descender, so the height is the font's full
    // line and not whatever the current name happens to reach.
    const string HeightReferenceText = "Ag";
    // Shown until the owner's name arrives, a frame or two after the body spawns. Better than an
    // empty label that pops into existence once the name lands.
    const string PendingName = "…";
    // Room for the colliders a look at a body can pass through: the house shell, a counter and the
    // props on it. Only whether anything is in the way matters, so overflowing is harmless.
    const int WallBufferSize = 16;

    [Tooltip("How high above the top of the body the label floats, as a share of the body's own height. 0.25 = a quarter of a body above the head.")]
    [SerializeField, Min(0f)] float heightAboveBody = 0.25f;
    [Tooltip("Share of the screen's height the label fills. It is held at this size however far away the body is, so a name across the house reads as easily as one up close.")]
    [SerializeField, Range(0.005f, 0.2f)] float screenShare = 0.028f;
    [Tooltip("Beyond this distance, in units, the label is not drawn. The house is about 540 units across at 45 units per metre.")]
    [SerializeField, Min(0f)] float hideBeyond = 600f;
    [Tooltip("Hide the label when something solid stands between the player and the body, so names cannot be read through walls.")]
    [SerializeField] bool hideBehindWalls = true;
    [Tooltip("What counts as a wall for the check above.")]
    [SerializeField] LayerMask wallLayers = ~0;
    [SerializeField] Color textColor = new Color(1f, 0.95f, 0.75f, 1f);
    [Tooltip("Width of the black outline that keeps the name readable against a light wall. 0 turns it off.")]
    [SerializeField, Range(0f, 0.5f)] float outlineWidth = 0.2f;

    // Owner writes, everyone reads: the player's own machine is the only one that knows what they
    // called themselves.
    readonly NetworkVariable<FixedString32Bytes> playerName =
        new NetworkVariable<FixedString32Bytes>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    readonly RaycastHit[] wallHits = new RaycastHit[WallBufferSize];
    // Null on the owner's own body, which never gets a label.
    TMP_Text label;
    // The label's own object, kept out of the body's hierarchy so it does not inherit its scale.
    Transform labelTransform;
    // How far above this object's origin the label floats, in world units. Measured once from the
    // body's collider: the fly and the old man are wildly different sizes.
    float labelHeight;
    // How tall one line of the label is at scale 1, measured from the font rather than assumed, so
    // the label keeps its share of the screen whatever font the project ends up with.
    float labelUnitHeight = 1f;
    Camera viewCamera;

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            playerName.Value = LobbyManager.LocalPlayerName();
            // No label on your own body, so nothing else here applies.
            return;
        }

        labelHeight = MeasureLabelHeight();
        BuildLabel();
        playerName.OnValueChanged += OnNameChanged;
        ShowName(playerName.Value);
    }

    public override void OnNetworkDespawn()
    {
        playerName.OnValueChanged -= OnNameChanged;
        DestroyLabel();
    }

    // Leaving the scene destroys the body without despawning it first, and the label is not a child
    // of the body, so it would be left floating in the next scene.
    void OnDestroy() => DestroyLabel();

    void DestroyLabel()
    {
        if (labelTransform == null)
        {
            return;
        }
        Destroy(labelTransform.gameObject);
        labelTransform = null;
        label = null;
    }

    void OnNameChanged(FixedString32Bytes previous, FixedString32Bytes current) => ShowName(current);

    void ShowName(FixedString32Bytes name)
    {
        if (label == null)
        {
            return;
        }
        string typed = name.ToString();
        label.text = string.IsNullOrWhiteSpace(typed) ? PendingName : typed;
    }

    // LateUpdate so the body and the player's camera have both finished moving this frame; following
    // them in Update would leave the label a frame behind and make it swim.
    void LateUpdate()
    {
        if (labelTransform == null)
        {
            return;
        }
        if (!TryGetViewCamera(out Camera view))
        {
            labelTransform.gameObject.SetActive(false);
            return;
        }

        Vector3 anchor = transform.position + Vector3.up * labelHeight;
        Vector3 toCamera = view.transform.position - anchor;
        float distance = toCamera.magnitude;
        // Zero distance would hand a zero-length direction to LookRotation; the camera is never
        // inside another player's head, but a body spawning on top of one for a frame is possible.
        bool isVisible = distance > 0f && distance <= hideBeyond && !IsBlocked(view.transform, anchor, distance);
        labelTransform.gameObject.SetActive(isVisible);
        if (!isVisible)
        {
            return;
        }

        labelTransform.position = anchor;
        // Faces the camera's position rather than copying its rotation, so a label off to the side of
        // the screen turns towards the player instead of staying edge-on.
        labelTransform.rotation = Quaternion.LookRotation(-toCamera / distance, view.transform.up);
        labelTransform.localScale = Vector3.one * (WorldHeightAt(view, distance) / labelUnitHeight);
    }

    // The world height the label needs to fill Screen Share of the view from this far away. Read from
    // the camera every frame, because the fly's field of view widens with its speed.
    float WorldHeightAt(Camera view, float distance)
    {
        if (view.orthographic)
        {
            return view.orthographicSize * 2f * screenShare;
        }
        float viewHeightHere = 2f * distance * Mathf.Tan(view.fieldOfView * 0.5f * Mathf.Deg2Rad);
        return viewHeightHere * screenShare;
    }

    bool IsBlocked(Transform view, Vector3 anchor, float distance)
    {
        if (!hideBehindWalls)
        {
            return false;
        }
        Vector3 eye = view.position;
        // Two bodies are skipped, not one. The labelled body, because the anchor sits above its head
        // and the line to it passes through it whenever the player looks up from below. And the
        // player's own body, because the fly's camera orbits less than a body's width behind it and
        // the old man's sits inside his head, so every name in the house would have read as blocked
        // by the player's own shoulders.
        Transform self = view.root;
        int hitCount = Physics.RaycastNonAlloc(eye, (anchor - eye) / distance, wallHits, distance, wallLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hitCount; i++)
        {
            Transform hit = wallHits[i].transform;
            if (!hit.IsChildOf(transform) && !hit.IsChildOf(self))
            {
                return true;
            }
        }
        return false;
    }

    // This machine's own camera, the one the label has to face. It belongs to this player's own body,
    // which is spawned at the same moment as everyone else's, so it is looked up until it is found
    // rather than once in OnNetworkSpawn.
    // Camera.main is only the first choice, not the only one: neither player camera is tagged
    // MainCamera, so in the kitchen it is always null and the labels would never have appeared.
    // Camera.allCameras holds only enabled cameras, and OwnerOnlyControls switches off every body's
    // camera but this player's own, so what is left is the view this machine is drawing.
    bool TryGetViewCamera(out Camera view)
    {
        if (viewCamera != null && viewCamera.isActiveAndEnabled)
        {
            view = viewCamera;
            return true;
        }
        viewCamera = Camera.main != null ? Camera.main : FrontmostCamera();
        view = viewCamera;
        return view != null;
    }

    // The enabled camera drawn last, which is the one the player ends up looking at when a scene has
    // a second camera over the first.
    static Camera FrontmostCamera()
    {
        Camera frontmost = null;
        foreach (Camera candidate in Camera.allCameras)
        {
            if (frontmost == null || candidate.depth > frontmost.depth)
            {
                frontmost = candidate;
            }
        }
        return frontmost;
    }

    // The top of the body plus the gap, in world units. The CharacterController is asked for first:
    // the old man also carries a disabled CapsuleCollider, whose bounds are not where he stands.
    // Falls back to the body's scale when there is no collider at all, so the label still appears
    // somewhere sensible instead of inside the model.
    float MeasureLabelHeight()
    {
        Collider body = GetComponentInChildren<CharacterController>();
        if (body == null)
        {
            body = GetComponentInChildren<Collider>();
        }
        if (body == null)
        {
            return Mathf.Abs(transform.lossyScale.y) * (1f + heightAboveBody);
        }
        float top = body.bounds.max.y - transform.position.y;
        return top + body.bounds.size.y * heightAboveBody;
    }

    void BuildLabel()
    {
        var labelObject = new GameObject($"{name} name tag");
        labelTransform = labelObject.transform;

        label = labelObject.AddComponent<TextMeshPro>();
        label.fontSize = LabelFontSize;
        label.alignment = TextAlignmentOptions.Center;
        label.color = textColor;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        // Without it a long name is clipped to the default text rect instead of simply being wider.
        label.overflowMode = TextOverflowModes.Overflow;
        // Measured here, before the name arrives, because it is a property of the font and the size
        // above and never changes afterwards. Guarded against zero: the first frame can report
        // nothing if the font atlas is still loading, and dividing the scale by it would make the
        // label either vanish or swallow the screen.
        label.text = HeightReferenceText;
        label.ForceMeshUpdate();
        labelUnitHeight = Mathf.Max(label.preferredHeight, 0.0001f);
        if (outlineWidth > 0f)
        {
            // outlineColor and outlineWidth write to fontMaterial, this label's own copy of the
            // material, not to the font asset's shared one, so no other text is outlined with it.
            label.outlineColor = Color.black;
            label.outlineWidth = outlineWidth;
        }
    }
}
