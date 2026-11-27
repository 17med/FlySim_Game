using UnityEngine;

// Procedural party dance and calm sit for the main-menu fly. The fly model is not skinned: it is rigid
// mesh pieces in a Transform tree (FLYMAIN thorax, FLYCAB head, FLYCUL abdomen, FLYALA/FLYALA0 wings,
// six FLYPAT legs of 8 nested segments), so every pose is made here by turning those pieces.
// Goes on the MenuFly prefab instance's ROOT. It owns the whole pose: it switches off the two Animators
// below it and sets absolute poses (rest + offset) every frame, never adding to last frame's.
//
// FACING: with the root at identity rotation the fly stands upright along root +Y and faces root +Z
// (head toward +Z, its right side toward +X), whatever the model child was rotated to in the prefab or
// the scene. Turn the root to aim it. The root's origin is the tabletop point under the thorax; the
// fly is kept standing (or sitting) on root y = 0, see PoseBody.
//
// MODEL AXES (checked against "fly (1).fbx"): node-local +Y points DOWN toward the legs, -Z is the
// head, +X is the fly's right. The FBX bind pose is the standing pose with every node unrotated; the
// menu scene overrides and the fly clips (endfly.anim frame 0, FlyWings_Buzz.anim) pose it in flight,
// so the rig is reset to bind before the rest pose is taken. Because every node is then unrotated,
// each node's local axes are the model axes, which is what all the angle signs below rely on.
[DisallowMultipleComponent]
public partial class MenuFlyDance : MonoBehaviour
{
    public enum Mode { Dance, Sit }
    enum LegPair { Front, Middle, Hind }

    // Extra wing spread while buzzing: it swings the wings' inner edges clear of the thorax before the
    // flap lifts them, or the inner edge would dip into the body.
    const float BuzzSpread = 35f;

    // Bind-pose local positions (FBX units) of the nodes the menu scene overrides or the fly clips key,
    // read from "fly (1).fbx". Other nodes keep their positions, which nothing here changes.
    static readonly (string name, Vector3 position)[] BindPositions =
    {
        ("FLYCAB", new Vector3(0.179f, -0.526f, -6.038f)),
        ("FLYCUL", new Vector3(0.032f, 0.123f, 10.844f)),
        ("FLYOJO0", new Vector3(0.124f, -0.772f, -3.048f)),
        ("FLYALA", new Vector3(4.585f, -2.032f, 0.867f)),
        ("FLYALA0", new Vector3(-4.473f, -2.032f, 0.867f)),
        ("FLYPAT01", new Vector3(-1.472f, 2.389f, 4.761f)),
        ("FLYPAT32", new Vector3(1.829f, 2.389f, 4.761f)),
        ("FLYPAT22", new Vector3(-6.226f, 0.794f, 4.216f)),
        ("FLYPAT34", new Vector3(0.900f, 1.229f, 0.325f)),
    };

    // Per leg: the coxa at the body, the knee (top of the tibia, the 3rd segment) and the segment below
    // the knee, where the tarsus starts. Side -1 is the fly's left (model -X).
    static readonly (string root, string knee, string belowKnee, int side, LegPair pair)[] LegNodes =
    {
        ("FLYPAT09", "FLYPAT15", "FLYPAT13", -1, LegPair.Front),
        ("FLYPAT17", "FLYPAT22", "FLYPAT24", -1, LegPair.Middle),
        ("FLYPAT01", "FLYPAT08", "FLYPAT06", -1, LegPair.Hind),
        ("FLYPAT41", "FLYPAT43", "FLYPAT44", 1, LegPair.Front),
        ("FLYPAT33", "FLYPAT35", "FLYPAT36", 1, LegPair.Middle),
        ("FLYPAT32", "FLYPAT26", "FLYPAT27", 1, LegPair.Hind),
    };

    // Where the tibia and the tarsus point, in MODEL axes (x the fly's right, y DOWN, z back), right side
    // (x mirrored for the left). Aims, not turns: AimBlend converts them through the joint's current
    // parent. (As turns in the femur's frame, the hip lift ate the raise's outward lean and the leg went up
    // behind the head; a rigid tarsus kept its standing bend and stuck up like a claw when sitting.)
    // Raised: the whole leg reaches up and out in a V over the head, the tarsus leaning forward a little.
    static readonly Vector3 ArmTibia = new Vector3(0.6f, -1f, -0.25f).normalized;
    static readonly Vector3 ArmTarsus = new Vector3(0.5f, -1f, -0.4f).normalized;
    // Sitting, per pair (front, middle, hind), a fly resting on a surface: the hip lift (sitFemurLift)
    // raises the knees beside the body, each tibia slopes out and down so its drop about matches the
    // knee's height over the belly at a 40 degree lift (so the belly rests on the table), and the tarsus
    // lies flat on it. (Legs folded flat instead splayed out like a starfish in the renders.)
    static readonly Vector3[] TibiaFolds =
    {
        new Vector3(0.55f, 0.45f, -0.55f).normalized,
        new Vector3(0.75f, 0.55f, 0.1f).normalized,
        new Vector3(0.55f, 0.55f, 0.6f).normalized,
    };
    static readonly Vector3[] TarsusFolds =
    {
        new Vector3(0.45f, 0.1f, -1f).normalized,
        new Vector3(0.8f, 0.1f, 0.5f).normalized,
        new Vector3(0.4f, 0.1f, 1f).normalized,
    };

    [Header("Dance: legs")]
    [Tooltip("Degrees a front leg lifts out at the hip when raised; the knee then points the tibia up.")]
    [SerializeField, Range(0f, 80f)] float armLiftAngle = 50f;
    [Tooltip("Degrees the raised front legs sway over the head in the wave bar.")]
    [SerializeField, Range(0f, 45f)] float waveAngle = 25f;
    [Tooltip("Degrees a middle leg lifts out when it kicks.")]
    [SerializeField, Range(0f, 90f)] float kickLiftAngle = 70f;
    [Tooltip("Degrees a middle leg swings forward when it kicks.")]
    [SerializeField, Range(0f, 60f)] float kickSwingAngle = 35f;
    [Tooltip("Degrees a hind leg lifts for a step.")]
    [SerializeField, Range(0f, 60f)] float stepLiftAngle = 35f;

    struct Leg
    {
        public Transform root, knee, belowKnee, tip;
        public Quaternion rootRest, kneeRest, belowKneeRest;
        public Vector3 tibia;    // rest direction of the tibia, in the knee's parent space
        public Vector3 tarsus;   // rest direction of the tarsus (below the knee's child to the tip), in the knee's space
        public float side;
        public LegPair pair;
    }

    Transform model, thorax, head, abdomen, rightWing, leftWing;
    Quaternion headRest, abdomenRest, rightWingRest, leftWingRest, modelRestRotation;
    Vector3 abdomenRestScale, modelRestPosition;
    Leg[] legs;
    MeshFilter[] meshPieces;
    float flyLength;
    // 0 dancing, 1 sitting; eased when applied.
    float sitBlend;
    bool rigReady, rigFailed;

    public Mode CurrentMode => mode;

    public void SetMode(Mode mode) => this.mode = mode;

    public void SetModeImmediate(Mode mode)
    {
        this.mode = mode;
        sitBlend = TargetBlend;
    }

    // Poses the fly as the shared clock reads `time` seconds (Time.time in Play), with any blend finished:
    // the dance phase comes from the clock, not from when the mode was set, so flies stay in step.
    // Works in Edit mode, where nothing else runs; the rig is found and reset the first time.
    public void Evaluate(float time)
    {
        if (!EnsureRig())
            return;
        sitBlend = TargetBlend;
        PoseAt(time);
    }

    float TargetBlend => mode == Mode.Sit ? 1f : 0f;

    void Awake()
    {
        sitBlend = TargetBlend;
        EnsureRig();
    }

    // LateUpdate so nothing that runs in Update (or an Animator someone re-enables) has the last word.
    void LateUpdate()
    {
        if (!EnsureRig())
            return;
        sitBlend = Mathf.MoveTowards(sitBlend, TargetBlend, Time.deltaTime / blendDuration);
        PoseAt(Time.time);
    }

    void PoseAt(float time)
    {
        float sit = Mathf.SmoothStep(0f, 1f, sitBlend);
        float beat = time * beatsPerMinute / 60f + beatOffset;
        FlyPose dance = sit < 1f ? DancePose(beat) : default;
        FlyPose rest = sit > 0f ? SitPose(beat * 60f / beatsPerMinute) : default;
        FlyPose pose = FlyPose.Lerp(dance, rest, sit);
        for (int i = 0; i < legs.Length; i++)
            PoseLeg(legs[i], pose);
        PoseHeadAndAbdomen(pose);
        PoseWings(pose, time);
        PoseBody(pose);
    }

    static Vector3 Mirror(Vector3 rightSide, float side) => new Vector3(rightSide.x * side, rightSide.y, rightSide.z);

    // At the hip, raising, kicking, stepping and sitting all lift the leg about the body's long axis
    // (Z); the sign flips per side so positive is always "tip up". A kick also swings the leg forward
    // about Y; the wave tilts both raised front legs the same way about Z. Then the knee aims the tibia
    // and the next joint aims the tarsus: up for a raised front leg, folded flat for sitting.
    void PoseLeg(in Leg leg, in FlyPose p)
    {
        bool isLeft = leg.side < 0f;
        float raise = leg.pair == LegPair.Front ? (isLeft ? p.frontLeft : p.frontRight) : 0f;
        float kick = leg.pair == LegPair.Middle ? (isLeft ? p.kickLeft : p.kickRight) : 0f;
        float step = leg.pair == LegPair.Hind ? (isLeft ? p.stepLeft : p.stepRight) : 0f;
        float lift = raise * armLiftAngle + kick * kickLiftAngle + step * stepLiftAngle + p.fold * sitFemurLift;
        Quaternion hip = Quaternion.AngleAxis(raise * p.wave * waveAngle, Vector3.forward)
                         * Quaternion.AngleAxis(leg.side * kick * kickSwingAngle, Vector3.up)
                         * Quaternion.AngleAxis(-leg.side * lift, Vector3.forward);
        leg.root.localRotation = hip * leg.rootRest;
        int pair = (int)leg.pair;
        AimBlend(leg.knee, leg.kneeRest, leg.tibia, Mirror(ArmTibia, leg.side), raise, Mirror(TibiaFolds[pair], leg.side), p.fold);
        AimBlend(leg.belowKnee, leg.belowKneeRest, leg.tarsus, Mirror(ArmTarsus, leg.side), raise, Mirror(TarsusFolds[pair], leg.side), p.fold);
    }

    // Turns `node` so that `restDir` (a direction in its parent's frame at rest) points where it would
    // unbent, swung toward `raised` by `raise` and then toward `folded` by `fold`; targets in model axes.
    // Parents are posed first, so node.parent's rotation is this frame's. Relative to the thorax, so the
    // body's own turn (set later, in PoseBody) cancels out.
    void AimBlend(Transform node, Quaternion rest, Vector3 restDir, Vector3 raised, float raise, Vector3 folded, float fold)
    {
        Quaternion parentInModel = Quaternion.Inverse(thorax.rotation) * node.parent.rotation;
        Vector3 aim = parentInModel * restDir;
        if (raise > 0f)
            aim = Vector3.Slerp(aim, raised, raise);
        if (fold > 0f)
            aim = Vector3.Slerp(aim, folded, fold);
        node.localRotation = Quaternion.FromToRotation(restDir, Quaternion.Inverse(parentInModel) * aim) * rest;
    }

    // Model axes: a positive turn about Y swings the head (at -Z) to the fly's left and the abdomen tip
    // (at +Z) to its right; a positive turn about X tips the head down and lifts the abdomen tip.
    void PoseHeadAndAbdomen(in FlyPose p)
    {
        head.localRotation = Quaternion.AngleAxis(p.headTurn, Vector3.up) * Quaternion.AngleAxis(p.nod, Vector3.right) * headRest;
        abdomen.localRotation = Quaternion.AngleAxis(p.abdomenSway, Vector3.up)
                                * Quaternion.AngleAxis(p.abdomenLift, Vector3.right) * abdomenRest;
        // Swells across and up, not along: a breath, not a stretch.
        float swell = 1f + p.breath * breathDepth;
        abdomen.localScale = Vector3.Scale(abdomenRestScale, new Vector3(swell, swell, 1f));
    }

    // Each wing node's origin is the wing root on the thorax side (checked on the mesh), so turning the
    // node hinges the wing in place. Spread opens the V about Y; lift raises the tips about Z, applied
    // after the spread so the inner edge has already swung clear of the thorax.
    void PoseWings(in FlyPose p, float time)
    {
        float flap = p.buzz * wingBuzzAngle * (0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * wingBuzzFrequency * time));
        float spread = p.wingSpread + p.buzz * BuzzSpread;
        float lift = p.wingLift + flap;
        rightWing.localRotation = WingTurn(1f, spread, lift) * rightWingRest;
        leftWing.localRotation = WingTurn(-1f, spread, lift) * leftWingRest;
    }

    static Quaternion WingTurn(float side, float spread, float lift) =>
        Quaternion.AngleAxis(-side * lift, Vector3.forward) * Quaternion.AngleAxis(side * spread, Vector3.up);

    // Roll, pitch and spin turn the whole model about the root's origin, the table point under the
    // thorax, so a sway pivots over the feet; shift slides it sideways (root +X) on top. Then the model is lifted so its lowest point is exactly on
    // the table, plus the hop: whatever the legs do, the fly neither sinks in nor floats.
    void PoseBody(in FlyPose p)
    {
        // Root axes: leaning right (+X) is a negative turn about +Z; nose (+Z) down is positive about +X.
        Quaternion body = Quaternion.AngleAxis(p.spin, Vector3.up)
                          * Quaternion.AngleAxis(-p.roll, Vector3.forward)
                          * Quaternion.AngleAxis(p.pitch, Vector3.right);
        model.localRotation = body * modelRestRotation;
        model.localPosition = body * modelRestPosition + Vector3.right * (p.shift * flyLength);
        model.localPosition += Vector3.up * (p.hop * flyLength - MinAlong(Vector3.up));
    }

    // Lowest extent of all mesh pieces along `axis`, in the root's space. It uses each mesh's bounding
    // box (the meshes are not CPU-readable, isReadable is off in the FBX import), which can reach a
    // little past the real surface of a turned segment: about 1.5% of the fly's length in the sit pose.
    // So the fly may float that much there; it never sinks.
    float MinAlong(Vector3 axis)
    {
        Matrix4x4 toRoot = transform.worldToLocalMatrix;
        float min = float.PositiveInfinity;
        foreach (MeshFilter piece in meshPieces)
        {
            Matrix4x4 m = toRoot * piece.transform.localToWorldMatrix;
            Bounds b = piece.sharedMesh.bounds;
            float reach = Mathf.Abs(Vector3.Dot(axis, m.GetColumn(0))) * b.extents.x
                          + Mathf.Abs(Vector3.Dot(axis, m.GetColumn(1))) * b.extents.y
                          + Mathf.Abs(Vector3.Dot(axis, m.GetColumn(2))) * b.extents.z;
            min = Mathf.Min(min, Vector3.Dot(axis, m.MultiplyPoint3x4(b.center)) - reach);
        }
        return min;
    }

    // Finds, resets and measures the rig once. Fails open: without the fly's nodes the menu still works,
    // the fly just keeps whatever pose it has.
    bool EnsureRig()
    {
        if (rigReady)
            return true;
        if (rigFailed)
            return false;
        if (!FindRig())
        {
            rigFailed = true;
            Debug.LogWarning($"{nameof(MenuFlyDance)} on {name}: the fly model's FLYMAIN, FLYCAB, FLYCUL, FLYALA, FLYALA0, six FLYPAT legs or its meshes were not found below this object, so it will not dance.", this);
            return false;
        }
        StopAnimators();
        ResetToBindPose();
        CaptureRest();
        rigReady = true;
        return true;
    }

    bool FindRig()
    {
        thorax = FindDeep(transform, "FLYMAIN");
        // The model is moved every frame; if it were this object, that would fight the stage placing it.
        if (thorax == null || thorax.parent == null || thorax.parent == transform)
            return false;
        model = thorax.parent;
        head = FindDeep(thorax, "FLYCAB");
        abdomen = FindDeep(thorax, "FLYCUL");
        rightWing = FindDeep(thorax, "FLYALA");
        leftWing = FindDeep(thorax, "FLYALA0");
        legs = new Leg[LegNodes.Length];
        for (int i = 0; i < LegNodes.Length; i++)
        {
            var n = LegNodes[i];
            Transform root = FindDeep(thorax, n.root);
            Transform knee = root != null ? FindDeep(root, n.knee) : null;
            Transform belowKnee = knee != null ? FindDeep(knee, n.belowKnee) : null;
            if (belowKnee == null)
                return false;
            legs[i] = new Leg { root = root, knee = knee, belowKnee = belowKnee, tip = ChainTip(belowKnee), side = n.side, pair = n.pair };
        }
        meshPieces = System.Array.FindAll(model.GetComponentsInChildren<MeshFilter>(), f => f.sharedMesh != null);
        return head != null && abdomen != null && rightWing != null && leftWing != null && meshPieces.Length > 0;
    }

    // The prefab's two Animators (whole-body sway on Fly_Bob_Container, wing buzz on the model) would
    // overwrite this pose every frame, so they are switched off for good. Play only: in Edit mode they
    // do not run, and switching them off there would be saved into the scene.
    void StopAnimators()
    {
        if (!Application.isPlaying)
            return;
        foreach (Animator animator in GetComponentsInChildren<Animator>(true))
            animator.enabled = false;
    }

    // Puts the model back in its FBX bind pose (standing; every node unrotated and unscaled, mesh
    // children at their parent's origin), whatever the scene or a clip left on it. This also makes
    // capturing idempotent: an Edit-mode domain reload re-captures from a posed fly and gets the same rest.
    void ResetToBindPose()
    {
        // Between the root and the model there are only holders for the old clips' sway; zeroed, the
        // root's space is the model parent's space, which PoseBody relies on.
        for (Transform holder = model.parent; holder != transform; holder = holder.parent)
        {
            holder.localPosition = Vector3.zero;
            holder.localRotation = Quaternion.identity;
            holder.localScale = Vector3.one;
        }
        foreach (Transform node in thorax.GetComponentsInChildren<Transform>(true))
        {
            node.localRotation = Quaternion.identity;
            node.localScale = Vector3.one;
            if (node.name.StartsWith("Object_", System.StringComparison.Ordinal))
                node.localPosition = Vector3.zero;
        }
        foreach (var (nodeName, position) in BindPositions)
        {
            Transform node = FindDeep(thorax, nodeName);
            if (node != null)
                node.localPosition = position;
        }
    }

    void CaptureRest()
    {
        headRest = head.localRotation;
        abdomenRest = abdomen.localRotation;
        abdomenRestScale = abdomen.localScale;
        rightWingRest = rightWing.localRotation;
        leftWingRest = leftWing.localRotation;
        for (int i = 0; i < legs.Length; i++)
        {
            legs[i].rootRest = legs[i].root.localRotation;
            legs[i].kneeRest = legs[i].knee.localRotation;
            legs[i].belowKneeRest = legs[i].belowKnee.localRotation;
            legs[i].tibia = (legs[i].kneeRest * legs[i].belowKnee.localPosition).normalized;
            Vector3 tipInKnee = legs[i].knee.InverseTransformPoint(legs[i].tip.position);
            legs[i].tarsus = (tipInKnee - legs[i].belowKnee.localPosition).normalized;
        }
        // Upright and facing root +Z: the model's up is its -Y and its head its -Z, so a half turn about
        // X. The model's scale is kept: the stage sizes the fly with it or with the root.
        modelRestRotation = Quaternion.Euler(180f, 0f, 0f);
        model.localRotation = modelRestRotation;
        model.localPosition = Vector3.zero;
        // Measured standing, after the reset: lengths in root units, so the hops scale with the fly.
        flyLength = -MinAlong(Vector3.back) - MinAlong(Vector3.forward);
        Vector3 thoraxInRoot = transform.InverseTransformPoint(thorax.position);
        modelRestPosition = new Vector3(-thoraxInRoot.x, -MinAlong(Vector3.up), -thoraxInRoot.z);
        model.localPosition = modelRestPosition;
    }

    // The last segment of a leg: each FLYPAT segment has at most one FLYPAT child (the others are its
    // mesh holders).
    static Transform ChainTip(Transform segment)
    {
        foreach (Transform child in segment)
        {
            if (child.name.StartsWith("FLYPAT", System.StringComparison.Ordinal))
                return ChainTip(child);
        }
        return segment;
    }

    static Transform FindDeep(Transform parent, string nodeName)
    {
        foreach (Transform child in parent)
        {
            if (child.name == nodeName)
                return child;
            Transform found = FindDeep(child, nodeName);
            if (found != null)
                return found;
        }
        return null;
    }
}
