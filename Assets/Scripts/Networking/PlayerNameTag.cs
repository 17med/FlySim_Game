using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

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
// Except between flies: a fly sees the other flies' names through walls and drawn on top of them,
// as it sees their glow (FlyTeammateXRay) and their markers on the minimap, so the flies can work
// together. The old man's view, and a fly looking at the old man, keep the wall check.
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
    // The text shader's depth test (TMP's SDF shaders read ZTest from this), set on the label's own
    // material copy so that only this label is drawn on top of walls.
    const string DepthTestProperty = "unity_GUIZTestMode";
    // How long a label may wait for a camera and a font height before it says so in the log.
    const float SlowStartWarningSeconds = 5f;
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
    [Tooltip("A fly sees the other flies' names through walls, drawn on top of them, as it sees them on the minimap. Names the old man sees, and his own name, keep the wall check.")]
    [SerializeField] bool fliesSeeFliesThroughWalls = true;
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
    // What the label follows: the object carrying the body's CharacterController, which is the part
    // that actually moves. For the old man that is this root, but the fly's root stays at its spawn
    // point while its child Fly_Body (FlyController, NetworkTransform) does the flying, so a label
    // anchored on the root hung where the fly was born and was hidden by the nearest wall.
    Transform anchorBody;
    // How far above Anchor Body's origin the label floats, in world units. Measured once from the
    // body's collider: the fly and the old man are wildly different sizes.
    float labelHeight;
    // How tall one line of the label is at scale 1, measured from the font rather than assumed, so
    // the label keeps its share of the screen whatever font the project ends up with. 0 means the
    // measurement has not come back yet, which is not the same as the label being one unit tall: see
    // TryMeasureUnitHeight.
    float labelUnitHeight;
    Camera viewCamera;
    // Whether this body is a fly, and whether the camera last asked about is a fly's, which together
    // decide whether this name skips the wall check. Both are fixed for a body's and a camera's life.
    bool isFlyBody;
    Camera classifiedCamera;
    bool isFlyView;
    // Null until the depth test is first set, so the first frame always sets it.
    bool? isDrawnOnTop;
    float spawnedAt;
    bool hasWarnedSlowStart;

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            playerName.Value = LobbyManager.LocalPlayerName();
            // No label on your own body, so nothing else here applies.
            return;
        }

        labelHeight = MeasureLabelHeight();
        isFlyBody = GetComponentInChildren<FlyController>(true) != null;
        spawnedAt = Time.unscaledTime;
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
    // Overriding rather than hiding, with base called: NetworkBehaviour has an OnDestroy of its own
    // that releases this component's NetworkVariable and takes it off its NetworkObject, and a plain
    // `void OnDestroy()` here shadowed it - Unity then called this one and Netcode's never ran, so
    // every body that left the kitchen leaked the variable the name travels in.
    public override void OnDestroy()
    {
        DestroyLabel();
        base.OnDestroy();
    }

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
        bool hasCamera = TryGetViewCamera(out Camera view);
        if (!hasCamera || !TryMeasureUnitHeight())
        {
            WarnIfSlowToStart(hasCamera);
            labelTransform.gameObject.SetActive(false);
            return;
        }

        bool isSeenThroughWalls = fliesSeeFliesThroughWalls && isFlyBody && IsFlyView(view);
        SetDrawnOnTop(isSeenThroughWalls);
        Vector3 anchor = anchorBody.position + Vector3.up * labelHeight;
        Vector3 toCamera = view.transform.position - anchor;
        float distance = toCamera.magnitude;
        // Zero distance would hand a zero-length direction to LookRotation; the camera is never
        // inside another player's head, but a body spawning on top of one for a frame is possible.
        bool isHidden = !isSeenThroughWalls && IsBlocked(view.transform, anchor, distance);
        bool isVisible = distance > 0f && distance <= hideBeyond && !isHidden;
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

    // A fly's camera sits under the fly's root, next to its FlyController; the old man's does not.
    // Asked once per camera, since what a camera belongs to never changes.
    bool IsFlyView(Camera view)
    {
        if (view != classifiedCamera)
        {
            classifiedCamera = view;
            isFlyView = view.transform.root.GetComponentInChildren<FlyController>(true) != null;
        }
        return isFlyView;
    }

    // Always passes the depth test when drawn on top, so walls cannot cover the name; otherwise the
    // usual test, so the scenery in front of it does.
    void SetDrawnOnTop(bool isOnTop)
    {
        if (isDrawnOnTop == isOnTop)
        {
            return;
        }
        isDrawnOnTop = isOnTop;
        CompareFunction depthTest = isOnTop ? CompareFunction.Always : CompareFunction.LessEqual;
        label.fontMaterial.SetFloat(DepthTestProperty, (float)depthTest);
    }

    // A label still waiting after a few seconds will never be seen, and nothing else would say so.
    // Logged once per label, naming the step it is stuck on and not the player's name.
    void WarnIfSlowToStart(bool hasCamera)
    {
        if (hasWarnedSlowStart || Time.unscaledTime - spawnedAt < SlowStartWarningSeconds)
        {
            return;
        }
        hasWarnedSlowStart = true;
        string stuckOn = hasCamera ? "the font has not reported a line height" : "no enabled camera was found to face";
        Debug.LogWarning($"{nameof(PlayerNameTag)}: the name over {name} is still not shown after {SlowStartWarningSeconds} s: {stuckOn}.", this);
    }

    // How tall one line of this label is at scale 1. False while the answer is not available yet,
    // which is the whole point of it being a separate step: TextMeshPro reports no height at all
    // until its font atlas has been built, and a height of nothing divided into the size the label
    // wants to be hands back a scale of tens of thousands. The label then either swallows the screen
    // or is culled for being larger than the world, which is a name tag that reads as broken rather
    // than as one frame late. So it is asked for every frame until it answers, and the label is not
    // drawn until it does.
    bool TryMeasureUnitHeight()
    {
        if (labelUnitHeight > 0f)
        {
            return true;
        }
        // The reference line rather than the name: the height has to be the font's full line, with an
        // ascender and a descender, and not whatever this particular name happens to reach.
        string shown = label.text;
        label.text = HeightReferenceText;
        label.ForceMeshUpdate();
        float measured = label.preferredHeight;
        label.text = shown;
        if (measured <= 0f)
        {
            return false;
        }
        labelUnitHeight = measured;
        label.ForceMeshUpdate();
        return true;
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
    // Also picks Anchor Body, since the height is only meaningful relative to the object it is measured from.
    float MeasureLabelHeight()
    {
        Collider body = GetComponentInChildren<CharacterController>();
        if (body == null)
        {
            body = GetComponentInChildren<Collider>();
        }
        // Fails open to the root, which is where the label was anchored before: right for any body
        // whose root is what moves, and no worse than before for one whose root does not.
        anchorBody = body != null ? body.transform : transform;
        float fallback = Mathf.Abs(anchorBody.lossyScale.y) * (1f + heightAboveBody);
        if (body == null)
        {
            return fallback;
        }
        float top = body.bounds.max.y - anchorBody.position.y;
        float height = top + body.bounds.size.y * heightAboveBody;
        // A collider that has not been placed yet reports empty bounds at the origin, which puts the
        // label underneath a body standing anywhere above y = 0 - and the fly's spawn points are all
        // in the air. Better the body's own size than a label at its feet or below the floor.
        return height > 0f ? height : fallback;
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
        // The height is not measured here. It is a property of the font and the size above and never
        // changes once it is known, but on the frame the label is built TextMeshPro has not
        // necessarily built its font atlas yet and reports nothing - so LateUpdate asks for it until
        // it has one, and leaves the label undrawn until then.
        label.text = PendingName;
        if (outlineWidth > 0f)
        {
            // outlineColor and outlineWidth write to fontMaterial, this label's own copy of the
            // material, not to the font asset's shared one, so no other text is outlined with it.
            label.outlineColor = Color.black;
            label.outlineWidth = outlineWidth;
        }
    }
}
