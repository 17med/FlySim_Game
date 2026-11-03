"""
old_person — low-poly elderly man in a sweater vest, Unity-Humanoid skeleton.
Run:  blender --background --python make_old_person.py -- <out_dir>
Produces: <out_dir>/old_person.fbx, <out_dir>/old_person.png, <out_dir>/old_person.blend
"""
import bpy, bmesh, sys, os, math
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT = os.path.abspath(argv[0]) if argv else os.getcwd()
os.makedirs(OUT, exist_ok=True)
NAME = "old_person"

# ---------------------------------------------------------------- clean file
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.context.scene.unit_settings.system = 'METRIC'
bpy.context.scene.unit_settings.scale_length = 1.0

# ---------------------------------------------------------------- palette
# 4x4 atlas, index = row*4 + col (row 0 = bottom of image)
PAL = [
    ("skin",     (0.839, 0.659, 0.545)),
    ("hair",     (0.871, 0.871, 0.847)),
    ("shirt",    (0.533, 0.612, 0.773)),
    ("vest",     (0.929, 0.918, 0.871)),
    ("trousers", (0.302, 0.306, 0.329)),
    ("shoe",     (0.278, 0.180, 0.125)),
    ("dark",     (0.129, 0.118, 0.110)),
    ("skin_sh",  (0.776, 0.588, 0.478)),
    ("vest_sh",  (0.855, 0.839, 0.788)),
    ("shirt_sh", (0.455, 0.529, 0.690)),
    ("eye",      (0.149, 0.157, 0.180)),
    ("sole",     (0.165, 0.129, 0.110)),
    ("lip",      (0.690, 0.498, 0.439)),
    ("trous_sh", (0.247, 0.251, 0.275)),
    ("hair_sh",  (0.776, 0.776, 0.757)),
    ("white",    (0.898, 0.898, 0.886)),
]
CIDX = {n: i for i, (n, _) in enumerate(PAL)}

TEX = 128
CELL = TEX // 4


def build_texture():
    img = bpy.data.images.new(NAME, width=TEX, height=TEX, alpha=True)
    px = [0.0] * (TEX * TEX * 4)
    for y in range(TEX):
        row = y // CELL
        for x in range(TEX):
            col = x // CELL
            r, g, b = PAL[row * 4 + col][1]
            o = (y * TEX + x) * 4
            px[o:o + 4] = [r, g, b, 1.0]
    img.pixels = px
    img.filepath_raw = os.path.join(OUT, NAME + ".png")
    img.file_format = 'PNG'
    img.save()
    return img


def uv_rect(cname):
    i = CIDX[cname]
    col, row = i % 4, i // 4
    u0, u1 = (col + 0.28) / 4.0, (col + 0.72) / 4.0
    v0, v1 = (row + 0.28) / 4.0, (row + 0.72) / 4.0
    return [(u0, v0), (u1, v0), (u1, v1), (u0, v1)]


# ---------------------------------------------------------------- geometry
# A "tube" is a chain of rectangular rings swept along one axis.
#   axis 'Z': section = (z, xmin, xmax, ymin, ymax)
#   axis 'X': section = (x, ymin, ymax, zmin, zmax)
VERTS, FACES, FCOL, PART = [], [], [], []


SIDES = 8          # cross-section resolution: 8 reads as rounded, still low-poly


def ring_pts(axis, a, u0, u1, v0, v1, sides):
    """One elliptical cross-section of `sides` verts, inscribed in the given box."""
    cu, ru = (u0 + u1) * 0.5, (u1 - u0) * 0.5
    cv, rv = (v0 + v1) * 0.5, (v1 - v0) * 0.5
    if sides == 4:                       # keep 4-gons exactly box-shaped
        ru *= math.sqrt(2.0); rv *= math.sqrt(2.0)
    off = math.pi / sides                # flat facet facing each axis
    pts = []
    for k in range(sides):
        ang = off + 2.0 * math.pi * k / sides
        u = cu + ru * math.cos(ang)
        v = cv + rv * math.sin(ang)
        pts.append((u, v, a) if axis == 'Z' else
                   (a, u, v) if axis == 'X' else (u, a, v))
    return pts


def tube(sections, axis, color, part, caps=(True, True), sides=None):
    sides = SIDES if sides is None else sides
    base = len(VERTS)
    for s in sections:
        VERTS.extend(ring_pts(axis, s[0], s[1], s[2], s[3], s[4], sides))
    n = len(sections)
    for i in range(n - 1):
        r0, r1 = base + i * sides, base + (i + 1) * sides
        for k in range(sides):
            k2 = (k + 1) % sides
            FACES.append((r0 + k, r0 + k2, r1 + k2, r1 + k))
            FCOL.append(color); PART.append(part)
    if caps[0]:
        FACES.append(tuple(reversed(range(base, base + sides))))
        FCOL.append(color); PART.append(part)
    if caps[1]:
        t = base + (n - 1) * sides
        FACES.append(tuple(range(t, t + sides)))
        FCOL.append(color); PART.append(part)


def box(x0, x1, y0, y1, z0, z1, color, part):
    tube([(z0, x0, x1, y0, y1), (z1, x0, x1, y0, y1)], 'Z', color, part, sides=4)


# --- measurements (metres, feet z=0, faces -Y, character LEFT = +X)
K = 0.96          # global trim so the finished figure is ~1.75 m
HF = -0.015       # elderly forward head carriage
ARM_Z = 1.400

# head ---------------------------------------------------------------------
tube([
    (1.558, -0.070, 0.070, -0.066 + HF, 0.058 + HF),
    (1.598, -0.082, 0.082, -0.082 + HF, 0.074 + HF),
    (1.655, -0.090, 0.090, -0.090 + HF, 0.082 + HF),
    (1.715, -0.092, 0.092, -0.092 + HF, 0.084 + HF),
    (1.775, -0.086, 0.086, -0.088 + HF, 0.080 + HF),
    (1.812, -0.058, 0.058, -0.062 + HF, 0.054 + HF),
], 'Z', "skin", "head")

# thinning grey hair: cap + band round the back and sides
tube([
    (1.734, -0.095, 0.095, -0.080 + HF, 0.090 + HF),
    (1.766, -0.098, 0.098, -0.092 + HF, 0.092 + HF),
    (1.800, -0.090, 0.090, -0.090 + HF, 0.086 + HF),
    (1.830, -0.058, 0.058, -0.060 + HF, 0.056 + HF),
], 'Z', "hair", "head")
tube([
    (1.618, -0.088, 0.088, 0.022 + HF, 0.086 + HF),
    (1.672, -0.093, 0.093, 0.010 + HF, 0.090 + HF),
    (1.715, -0.097, 0.097, 0.000 + HF, 0.092 + HF),
], 'Z', "hair", "head")

box(0.090, 0.101, 0.000 + HF, 0.038 + HF, 1.628, 1.672, "skin_sh", "head")
box(-0.101, -0.090, 0.000 + HF, 0.038 + HF, 1.628, 1.672, "skin_sh", "head")
box(-0.013, 0.013, -0.114 + HF, -0.088 + HF, 1.662, 1.710, "skin", "head")      # nose
box(0.024, 0.060, -0.098 + HF, -0.088 + HF, 1.680, 1.700, "eye", "head")
box(-0.060, -0.024, -0.098 + HF, -0.088 + HF, 1.680, 1.700, "eye", "head")
box(0.024, 0.060, -0.098 + HF, -0.091 + HF, 1.708, 1.716, "hair", "head")       # white brows
box(-0.060, -0.024, -0.098 + HF, -0.091 + HF, 1.708, 1.716, "hair", "head")
box(-0.026, 0.026, -0.092 + HF, -0.084 + HF, 1.586, 1.596, "lip", "head")

# neck + shirt collar -------------------------------------------------------
tube([
    (1.380, -0.052, 0.052, -0.049, 0.049),
    (1.480, -0.049, 0.049, -0.046 + HF * 0.5, 0.046 + HF * 0.5),
    (1.566, -0.047, 0.047, -0.045 + HF, 0.045 + HF),
], 'Z', "skin", "neck")
tube([
    (1.418, -0.080, 0.080, -0.074, 0.074),
    (1.492, -0.066, 0.066, -0.062, 0.062),
], 'Z', "shirt", "neck", caps=(False, True))

# shoulder yoke - blue shirt above the sleeveless vest ----------------------
tube([
    (1.340, -0.176, 0.176, -0.108, 0.106),
    (1.405, -0.190, 0.190, -0.105, 0.104),
    (1.452, -0.158, 0.158, -0.096, 0.095),
    (1.478, -0.098, 0.098, -0.080, 0.079),
], 'Z', "shirt", "chest")

# sweater vest --------------------------------------------------------------
tube([
    (0.955, -0.172, 0.172, -0.112, 0.110),
    (1.050, -0.175, 0.175, -0.122, 0.116),   # paunch
    (1.160, -0.178, 0.178, -0.124, 0.115),
    (1.265, -0.180, 0.180, -0.117, 0.110),
    (1.348, -0.182, 0.182, -0.110, 0.107),
], 'Z', "vest", "chest")

# shirt showing through the V-neck
tube([
    (1.245, -0.026, 0.026, -0.130, -0.116),
    (1.330, -0.060, 0.060, -0.126, -0.112),
    (1.420, -0.090, 0.090, -0.120, -0.106),
], 'Z', "shirt", "chest", sides=4)

# belt + pelvis -------------------------------------------------------------
tube([
    (0.898, -0.168, 0.168, -0.116, 0.116),
    (0.958, -0.168, 0.168, -0.116, 0.116),
], 'Z', "dark", "pelvis")
tube([
    (0.836, -0.112, 0.112, -0.080, 0.080),
    (0.892, -0.164, 0.164, -0.113, 0.113),
    (0.990, -0.164, 0.164, -0.114, 0.114),
], 'Z', "trousers", "pelvis")

# legs ----------------------------------------------------------------------
for side, sx in (("L", 1.0), ("R", -1.0)):
    cx = 0.090 * sx
    tube([
        (0.962, cx - 0.072, cx + 0.072, -0.098, 0.098),
        (0.880, cx - 0.074, cx + 0.074, -0.100, 0.100),
        (0.700, cx - 0.070, cx + 0.070, -0.094, 0.092),
        (0.500, cx - 0.064, cx + 0.064, -0.086, 0.084),
        (0.280, cx - 0.058, cx + 0.058, -0.076, 0.072),
        (0.118, cx - 0.054, cx + 0.054, -0.068, 0.064),
    ], 'Z', "trousers", "leg" + side)
    tube([
        (0.000, cx - 0.056, cx + 0.056, -0.190, 0.060),
        (0.026, cx - 0.062, cx + 0.062, -0.202, 0.064),
        (0.068, cx - 0.060, cx + 0.060, -0.168, 0.062),
        (0.105, cx - 0.055, cx + 0.055, -0.092, 0.058),
        (0.140, cx - 0.052, cx + 0.052, -0.070, 0.054),
    ], 'Z', "shoe", "shoe" + side)

# arms - T-pose bind -------------------------------------------------------
for side, sx in (("L", 1.0), ("R", -1.0)):
    tube([
        (0.165 * sx, -0.060, 0.060, ARM_Z - 0.066, ARM_Z + 0.066),
        (0.250 * sx, -0.055, 0.055, ARM_Z - 0.058, ARM_Z + 0.058),
        (0.410 * sx, -0.048, 0.048, ARM_Z - 0.050, ARM_Z + 0.050),
        (0.560 * sx, -0.042, 0.042, ARM_Z - 0.044, ARM_Z + 0.044),
        (0.640 * sx, -0.038, 0.038, ARM_Z - 0.040, ARM_Z + 0.040),
    ], 'X', "shirt", "arm" + side)
    tube([
        (0.638 * sx, -0.036, 0.036, ARM_Z - 0.038, ARM_Z + 0.038),
        (0.710 * sx, -0.040, 0.040, ARM_Z - 0.048, ARM_Z + 0.048),
        (0.790 * sx, -0.036, 0.036, ARM_Z - 0.044, ARM_Z + 0.044),
        (0.815 * sx, -0.022, 0.022, ARM_Z - 0.030, ARM_Z + 0.030),
    ], 'X', "skin", "arm" + side)

VERTS[:] = [(x * K, y * K, z * K) for x, y, z in VERTS]

# ---------------------------------------------------------------- build mesh
me = bpy.data.meshes.new(NAME)
me.from_pydata(VERTS, [], FACES)
me.update()
obj = bpy.data.objects.new(NAME + "_body", me)
bpy.context.collection.objects.link(obj)

# UVs -> palette cells
uvl = me.uv_layers.new(name="UVMap")
for fi, poly in enumerate(me.polygons):
    rect = uv_rect(FCOL[fi])
    for k, li in enumerate(poly.loop_indices):
        uvl.data[li].uv = rect[k % 4]

# consistent outward normals, then smooth-shade everything below 55 degrees
bm = bmesh.new(); bm.from_mesh(me)
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
bm.normal_update()
for e in bm.edges:
    if len(e.link_faces) == 2:
        e.smooth = e.link_faces[0].normal.angle(e.link_faces[1].normal) < math.radians(55)
    else:
        e.smooth = False
bm.to_mesh(me); bm.free()
me.update()
for p in me.polygons:
    p.use_smooth = True

# material
img = build_texture()
mat = bpy.data.materials.new(NAME)
mat.use_nodes = True
nt = mat.node_tree
bsdf = next(n for n in nt.nodes if n.type == 'BSDF_PRINCIPLED')
tex = nt.nodes.new('ShaderNodeTexImage')
tex.image = img
tex.interpolation = 'Closest'
tex.location = (-400, 200)
nt.links.new(tex.outputs['Color'], bsdf.inputs['Base Color'])
bsdf.inputs['Roughness'].default_value = 0.78
if 'Specular IOR Level' in bsdf.inputs:
    bsdf.inputs['Specular IOR Level'].default_value = 0.25
me.materials.append(mat)

# ---------------------------------------------------------------- armature
BONES = [
    ("Hips",       (0, 0, 0.935),        (0, 0, 1.050),        None),
    ("Spine",      (0, 0, 1.050),        (0, 0, 1.170),        "Hips"),
    ("Chest",      (0, 0, 1.170),        (0, 0, 1.285),        "Spine"),
    ("UpperChest", (0, 0, 1.285),        (0, -0.006, 1.410),   "Chest"),
    ("Neck",       (0, -0.006, 1.410),   (0, -0.012, 1.558),   "UpperChest"),
    ("Head",       (0, -0.012, 1.558),   (0, -0.022, 1.820),   "Neck"),
]
for _side, _sx in (("Left", 1.0), ("Right", -1.0)):
    BONES += [
        (_side + "Shoulder", (0.035 * _sx, 0, 1.372), (0.165 * _sx, 0, ARM_Z), "UpperChest"),
        (_side + "UpperArm", (0.165 * _sx, 0, ARM_Z), (0.410 * _sx, 0, ARM_Z), _side + "Shoulder"),
        (_side + "LowerArm", (0.410 * _sx, 0, ARM_Z), (0.638 * _sx, 0, ARM_Z), _side + "UpperArm"),
        (_side + "Hand",     (0.638 * _sx, 0, ARM_Z), (0.805 * _sx, 0, ARM_Z), _side + "LowerArm"),
        (_side + "UpperLeg", (0.090 * _sx, 0, 0.912), (0.092 * _sx, 0, 0.500), "Hips"),
        (_side + "LowerLeg", (0.092 * _sx, 0, 0.500), (0.090 * _sx, 0, 0.100), _side + "UpperLeg"),
        (_side + "Foot",     (0.090 * _sx, 0, 0.100), (0.090 * _sx, -0.100, 0.034), _side + "LowerLeg"),
        (_side + "Toes",     (0.090 * _sx, -0.100, 0.034), (0.090 * _sx, -0.185, 0.030), _side + "Foot"),
    ]
BONES = [(n, tuple(c * K for c in h), tuple(c * K for c in t), p) for n, h, t, p in BONES]

arm_data = bpy.data.armatures.new(NAME + "_rig")
rig = bpy.data.objects.new(NAME, arm_data)
bpy.context.collection.objects.link(rig)
bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode='EDIT')
eb = arm_data.edit_bones
for n, h, t, p in BONES:
    b = eb.new(n)
    b.head, b.tail, b.roll = Vector(h), Vector(t), 0.0
    b.use_deform = True
for n, h, t, p in BONES:
    if p:
        eb[n].parent = eb[p]
        eb[n].use_connect = (eb[n].head - eb[p].tail).length < 1e-6
bpy.ops.object.mode_set(mode='OBJECT')

# ---------------------------------------------------------------- skinning
SEG = {n: (Vector(h), Vector(t)) for n, h, t, _ in BONES}
ALLOWED = {
    "head":   ["Head", "Neck"],
    "neck":   ["Neck", "Head", "UpperChest"],
    "chest":  ["UpperChest", "Chest", "Spine", "LeftShoulder", "RightShoulder"],
    "pelvis": ["Hips", "Spine", "LeftUpperLeg", "RightUpperLeg"],
}
for s in ("L", "R"):
    side = "Left" if s == "L" else "Right"
    ALLOWED["arm" + s] = [side + "Shoulder", side + "UpperArm", side + "LowerArm",
                          side + "Hand", "UpperChest"]
    ALLOWED["leg" + s] = [side + "UpperLeg", side + "LowerLeg", side + "Foot", "Hips"]
    ALLOWED["shoe" + s] = [side + "Foot", side + "Toes", side + "LowerLeg"]


def seg_dist(p, a, b):
    ab = b - a
    L = ab.length_squared
    t = 0.0 if L == 0 else max(0.0, min(1.0, (p - a).dot(ab) / L))
    return (p - (a + ab * t)).length


# vertex -> part (a vertex may be shared; first face that uses it wins)
vpart = {}
for fi, f in enumerate(FACES):
    for vi in f:
        vpart.setdefault(vi, PART[fi])

groups = {n: obj.vertex_groups.new(name=n) for n, _, _, _ in BONES}
for v in me.vertices:
    part = vpart.get(v.index, "chest")
    cand = ALLOWED.get(part, [n for n, _, _, _ in BONES])
    d = sorted(((seg_dist(v.co, *SEG[n]) + 1e-5, n) for n in cand))[:3]
    w = [(1.0 / (dd ** 2.3), n) for dd, n in d]
    tot = sum(x for x, _ in w)
    for x, n in w:
        groups[n].add([v.index], x / tot, 'REPLACE')

obj.parent = rig
obj.matrix_parent_inverse = rig.matrix_world.inverted()
mod = obj.modifiers.new("Armature", 'ARMATURE')
mod.object = rig


# ---------------------------------------------------------------- animation
from mathutils import Matrix

bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode='POSE')
for pb in rig.pose.bones:
    pb.rotation_mode = 'QUATERNION'

REST   = {b.name: b.matrix_local.to_3x3() for b in arm_data.bones}
PARENT = {n: pa for n, _, _, pa in BONES}
ORDER  = [n for n, _, _, _ in BONES]            # parents always precede children
I3     = Matrix.Identity(3)
ARM_DOWN = 84.0                                 # degrees to drop a T-posed arm


def rot(axis, deg):
    return Matrix.Rotation(math.radians(deg), 3, axis)


def apply_pose(w):
    """w maps bone -> absolute rotation in armature space; '_hips_loc' is a world offset."""
    for name in ORDER:
        pb = rig.pose.bones[name]
        Rw = w.get(name, I3)
        pa = PARENT[name]
        Rp = w.get(pa, I3) if pa else I3
        B  = REST[name]
        pb.rotation_quaternion = (B.inverted() @ (Rp.inverted() @ Rw) @ B).to_quaternion()
    d = w.get('_hips_loc', Vector((0.0, 0.0, 0.0)))
    rig.pose.bones['Hips'].location = REST['Hips'].inverted() @ d


def arms(w, swing_l, elbow=-20.0, spread=0.0, shoulder=3.0):
    """Hang both arms from the T-pose and swing them."""
    for side, sx, sw in (('Left', 1.0, swing_l), ('Right', -1.0, -swing_l)):
        down = rot('Y', (ARM_DOWN - spread) * sx)
        w[side + 'Shoulder'] = w['UpperChest'] @ rot('Y', shoulder * sx)
        w[side + 'UpperArm'] = w['UpperChest'] @ rot('X', sw) @ down
        w[side + 'LowerArm'] = w['UpperChest'] @ rot('X', sw + elbow) @ down
        w[side + 'Hand']     = w[side + 'LowerArm']


def torso(w, twist=0.0, stoop=7.0):
    """Hips stay level so the feet stay flat; the stoop lives in the spine."""
    w['Hips']       = rot('Z', twist)
    w['Spine']      = w['Hips']       @ rot('Z', -twist * 0.45) @ rot('X', stoop * 0.40)
    w['Chest']      = w['Spine']      @ rot('Z', -twist * 0.35) @ rot('X', stoop * 0.30)
    w['UpperChest'] = w['Chest']      @ rot('Z', -twist * 0.25) @ rot('X', stoop * 0.20)
    w['Neck']       = w['UpperChest'] @ rot('X', -stoop * 0.45)
    w['Head']       = w['Neck']       @ rot('X', -stoop * 0.45)


# --- walk cycle: 8 canonical leg poses (thigh, knee, ankle) over one stride ---
LEG_KEYS = [
    (-20.0,  6.0,   6.0),   # 0.000 heel contact, leg forward
    (-11.0, 14.0,   1.0),   # 0.125 weight down
    (  0.0,  7.0,  -3.0),   # 0.250 mid stance
    ( 11.0,  9.0, -14.0),   # 0.375 heel off
    ( 17.0, 20.0, -12.0),   # 0.500 toe off
    (  6.0, 40.0,   5.0),   # 0.625 swing, knee high
    ( -7.0, 34.0,   7.0),   # 0.750 swing through
    (-18.0, 15.0,   7.0),   # 0.875 reaching forward
]


def leg_at(t):
    n = len(LEG_KEYS)
    t = t % 1.0
    i = int(t * n) % n
    f = (t * n) - int(t * n)
    a, b = LEG_KEYS[i], LEG_KEYS[(i + 1) % n]
    return tuple(a[k] + (b[k] - a[k]) * f for k in range(3))


def walk_pose(t):
    w = {}
    lt, lk, la = leg_at(t)
    rt, rk, ra = leg_at(t + 0.5)
    twist = -5.0 * math.cos(2.0 * math.pi * t)              # pelvis follows the lead leg
    swing = 20.0 * math.cos(2.0 * math.pi * t)              # arms oppose the legs
    bob   = -0.010 - 0.013 * math.cos(4.0 * math.pi * (t - 0.125))

    torso(w, twist=twist, stoop=8.0)
    w['_hips_loc'] = Vector((0.0, 0.0, bob))
    arms(w, swing, elbow=-24.0, spread=9.0)
    for side, th, kn, an in (('Left', lt, lk, la), ('Right', rt, rk, ra)):
        w[side + 'UpperLeg'] = w['Hips'] @ rot('X', th)
        w[side + 'LowerLeg'] = w['Hips'] @ rot('X', th + kn)
        w[side + 'Foot']     = w['Hips'] @ rot('X', th + kn + an)
        w[side + 'Toes']     = w[side + 'Foot']
    return w


def idle_pose(t):
    """Standing breath: slow chest rise, tiny weight shift, head settle."""
    w = {}
    b    = 0.5 - 0.5 * math.cos(2.0 * math.pi * t)          # 0..1 breath
    sway = math.sin(2.0 * math.pi * t)

    torso(w, twist=0.9 * sway, stoop=7.0)
    w['Spine']      = w['Spine']      @ rot('X', -1.1 * b)
    w['Chest']      = w['Chest']      @ rot('X', -1.5 * b)
    w['UpperChest'] = w['UpperChest'] @ rot('X', -1.7 * b)
    w['Neck']       = w['Neck']       @ rot('X',  1.0 * b)
    w['Head']       = w['Head']       @ rot('X',  0.9 * b) @ rot('Z', -0.8 * sway)
    w['_hips_loc']  = Vector((0.0, 0.0, 0.004 * b))

    arms(w, 1.8 * sway, elbow=-18.0, spread=10.0, shoulder=4.0 + 2.5 * b)
    for side in ('Left', 'Right'):
        w[side + 'UpperLeg'] = w['Hips'] @ rot('X', -2.0)
        w[side + 'LowerLeg'] = w['Hips'] @ rot('X',  3.0)
        w[side + 'Foot']     = w['Hips'] @ rot('X', -1.0)
        w[side + 'Toes']     = w[side + 'Foot']
    return w


def bake(name, n_frames, pose_fn):
    """Key every frame from 1..n_frames+1; the last frame repeats the first so it loops."""
    act = bpy.data.actions.new(name)
    if rig.animation_data is None:
        rig.animation_data_create()
    rig.animation_data.action = act
    if hasattr(rig.animation_data, "action_slot"):
        try:
            rig.animation_data.action_slot = act.slots.new(id_type='OBJECT', name=rig.name)
        except Exception as exc:
            print("  slot assign skipped:", exc)
    for i in range(n_frames + 1):
        apply_pose(pose_fn(i / float(n_frames)))
        f = i + 1
        for pb in rig.pose.bones:
            pb.keyframe_insert('rotation_quaternion', frame=f, group=pb.name)
        rig.pose.bones['Hips'].keyframe_insert('location', frame=f, group='Hips')
    act.use_fake_user = True
    print("  action %-6s %d frames" % (name, n_frames + 1))
    return act


bpy.context.scene.render.fps = 24
ACT_IDLE = bake("Idle", 120, idle_pose)    # 5.0 s breathing loop
ACT_WALK = bake("Walk", 36, walk_pose)     # 1.5 s stride loop

# leave the rig on the rest pose so the FBX bind pose stays the T-pose
rig.animation_data.action = None
for pb in rig.pose.bones:
    pb.rotation_quaternion = (1.0, 0.0, 0.0, 0.0)
    pb.location = (0.0, 0.0, 0.0)
bpy.ops.object.mode_set(mode='OBJECT')
bpy.context.scene.frame_start, bpy.context.scene.frame_end = 1, 121

# ---------------------------------------------------------------- export
for o in bpy.context.scene.objects:
    o.select_set(o in (rig, obj))
bpy.context.view_layer.objects.active = rig

fbx = os.path.join(OUT, NAME + ".fbx")
bpy.ops.export_scene.fbx(
    filepath=fbx,
    use_selection=True,
    apply_unit_scale=True,
    global_scale=1.0,
    apply_scale_options='FBX_SCALE_NONE',
    bake_space_transform=False,
    object_types={'ARMATURE', 'MESH'},
    use_mesh_modifiers=False,
    mesh_smooth_type='EDGE',
    add_leaf_bones=False,
    primary_bone_axis='Y',
    secondary_bone_axis='X',
    armature_nodetype='NULL',
    use_armature_deform_only=True,
    bake_anim=True,
    bake_anim_use_all_actions=True,
    bake_anim_use_nla_strips=False,
    bake_anim_force_startend_keying=True,
    bake_anim_step=1.0,
    bake_anim_simplify_factor=0.0,
    path_mode='STRIP',
    axis_forward='-Z',
    axis_up='Y',
)

bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, NAME + ".blend"))

print("TRIS/QUADS:", len(me.polygons), "VERTS:", len(me.vertices),
      "BONES:", len(arm_data.bones))
print("WROTE:", fbx, os.path.getsize(fbx), "bytes")
