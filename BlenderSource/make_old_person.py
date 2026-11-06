"""
old_person — low-poly elderly man in a sweater vest, Unity-Humanoid skeleton.
Run:  blender --background --python make_old_person.py -- <out_dir>
Produces: <out_dir>/old_person.fbx + old_person.png, and old_person.blend next
to this script (never inside Assets/, where Unity would import it as a model).
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
# GRID x GRID atlas, index = row*GRID + col (row 0 = bottom of image)
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
    # --- eyes: a warm off-white sclera; pure white reads as plastic
    ("sclera",    (0.925, 0.910, 0.882)),
    ("sclera_sh", (0.800, 0.773, 0.741)),
    ("iris",      (0.396, 0.478, 0.498)),
    ("iris_dk",   (0.231, 0.306, 0.329)),
    ("pupil",     (0.055, 0.059, 0.071)),
    ("glint",     (1.000, 1.000, 0.992)),
    ("lid",       (0.816, 0.631, 0.518)),
    ("socket",    (0.714, 0.525, 0.424)),
]
CIDX = {n: i for i, (n, _) in enumerate(PAL)}

GRID = 8                 # 8x8 cells: room for the eye colours, still tiny
CELL = 32
TEX = GRID * CELL


def build_texture():
    img = bpy.data.images.new(NAME, width=TEX, height=TEX, alpha=True)
    px = [0.0] * (TEX * TEX * 4)
    for y in range(TEX):
        row = y // CELL
        for x in range(TEX):
            col = x // CELL
            i = row * GRID + col
            r, g, b = PAL[i][1] if i < len(PAL) else (0.5, 0.5, 0.5)
            o = (y * TEX + x) * 4
            px[o:o + 4] = [r, g, b, 1.0]
    img.pixels = px
    img.filepath_raw = os.path.join(OUT, NAME + ".png")
    img.file_format = 'PNG'
    img.save()
    return img


def uv_rect(cname):
    i = CIDX[cname]
    col, row = i % GRID, i // GRID
    u0, u1 = (col + 0.28) / GRID, (col + 0.72) / GRID
    v0, v1 = (row + 0.28) / GRID, (row + 0.72) / GRID
    return [(u0, v0), (u1, v0), (u1, v1), (u0, v1)]


# ---------------------------------------------------------------- geometry
# A "tube" is a chain of rectangular rings swept along one axis.
#   axis 'Z': section = (z, xmin, xmax, ymin, ymax)
#   axis 'X': section = (x, ymin, ymax, zmin, zmax)
VERTS, FACES, FCOL, PART = [], [], [], []


SIDES = 14         # cross-section resolution: 14 reads as genuinely round
PATCH = set()      # faces whose winding is authored, not recalculated


def add_face(f, color, part, patch=False):
    if patch:
        PATCH.add(len(FACES))
    FACES.append(f); FCOL.append(color); PART.append(part)


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


def res(sections, k=3):
    """Catmull-Rom resample of a section list, k-1 extra rings per span.
    The hand-written sections stay the silhouette key-frames; this turns the
    straight runs between them into a continuous curve, which is most of what
    makes the body read as smooth rather than stacked."""
    if k <= 1 or len(sections) < 2:
        return [tuple(s) for s in sections]
    S = [list(s) for s in sections]
    P = [S[0]] + S + [S[-1]]
    out = []
    for i in range(len(S) - 1):
        p0, p1, p2, p3 = P[i], P[i + 1], P[i + 2], P[i + 3]
        for j in range(k):
            t = j / k
            t2, t3 = t * t, t * t * t
            out.append(tuple(
                0.5 * (2.0 * b + (-a + c) * t + (2.0 * a - 5.0 * b + 4.0 * c - d) * t2
                       + (-a + 3.0 * b - 3.0 * c + d) * t3)
                for a, b, c, d in zip(p0, p1, p2, p3)))
    out.append(tuple(S[-1]))
    return out


# ------------------------------------------------- shells laid on the face
# Eyes, brows and lips are built as thin shells offset along the head's own
# surface normal. Sitting on the skull's curvature is what keeps them flush
# with the face: a free-standing eyeball sphere bulges off a head this round.
def _front_chain(s):
    """The front half of one head ring, as the mesh actually builds it."""
    cu, ru = (s[1] + s[2]) * 0.5, (s[2] - s[1]) * 0.5
    cv, rv = (s[3] + s[4]) * 0.5, (s[4] - s[3]) * 0.5
    off = math.pi / SIDES
    pts = []
    for k in range(SIDES):
        a = off + 2.0 * math.pi * k / SIDES
        pts.append((cu + ru * math.cos(a), cv + rv * math.sin(a)))
    return sorted(p for p in pts if p[1] < cv), ru, cv, rv


def head_surf(x, z, out=0.0):
    """Point on the front of the head at lateral x / height z, pushed `out`
    along the surface normal. This lands on the *faceted* skin, not on the
    ideal ellipse - the 14-gon sits up to 2 mm inside it, which is more than
    the whole eye is proud of the face, so shells offset from the ellipse
    float visibly. Off the facets, sub-millimetre offsets behave."""
    S = HEAD_RINGS
    if z <= S[0][0]:
        s = S[0]
    elif z >= S[-1][0]:
        s = S[-1]
    else:
        i = max(j for j in range(len(S) - 1) if S[j][0] <= z)
        t = (z - S[i][0]) / (S[i + 1][0] - S[i][0])
        s = tuple(a + (b - a) * t for a, b in zip(S[i], S[i + 1]))
    f, ru, cv, rv = _front_chain(s)
    x = max(f[0][0], min(f[-1][0], x))
    k = max(j for j in range(len(f) - 1) if f[j][0] <= x)
    (u0, v0), (u1, v1) = f[k], f[k + 1]
    y = v0 + (v1 - v0) * ((x - u0) / (u1 - u0) if u1 != u0 else 0.0)
    # offset along the smooth normal so neighbouring facets don't kink apart
    q = max(-0.985, min(0.985, x / ru))
    nx, ny = q / ru, -math.sqrt(1.0 - q * q) / rv
    n = math.hypot(nx, ny)
    return (x + out * nx / n, y + out * ny / n, z)


def face_disc(cx, cz, rx, rz, color, part, out_c, out_r,
              segs=14, rings=3, rim=None):
    """Filled elliptical shell on the face, domed out_c at the centre falling
    to out_r at the rim; `rim` colours the outermost band."""
    base = len(VERTS)
    VERTS.append(head_surf(cx, cz, out_c))
    for i in range(1, rings + 1):
        s = i / rings
        o = out_r + (out_c - out_r) * (1.0 - s * s)
        for j in range(segs):
            a = 2.0 * math.pi * j / segs
            VERTS.append(head_surf(cx + rx * s * math.cos(a),
                                   cz + rz * s * math.sin(a), o))
    for j in range(segs):
        j2 = (j + 1) % segs
        add_face((base, base + 1 + j, base + 1 + j2), color, part, True)
    for i in range(rings - 1):
        c = rim if (rim and i == rings - 2) else color
        r0, r1 = base + 1 + i * segs, base + 1 + (i + 1) * segs
        for j in range(segs):
            j2 = (j + 1) % segs
            add_face((r0 + j, r1 + j, r1 + j2, r0 + j2), c, part, True)


def face_band(x0, x1, lo, hi, color, part, out_lo, out_hi,
              nu=16, nv=3, fade=0.0, sink=-0.0032):
    """Shell on the face between two z(x) curves - eyelids, brows, lips.
    `fade` is the fraction of the width at each end over which the shell
    drops to `sink` and so ducks under the skin: without it every patch
    reads as a rectangular plate stuck on the face. A negative out_hi does
    the same along the far edge."""
    base = len(VERTS)
    for j in range(nv + 1):
        t = j / nv
        prof = out_lo + (out_hi - out_lo) * t
        for i in range(nu + 1):
            u = i / nu
            x = x0 + (x1 - x0) * u
            m = 1.0 if fade <= 0.0 else min(1.0, min(u, 1.0 - u) / fade)
            m = m * m * (3.0 - 2.0 * m)
            z0, z1 = lo(x), hi(x)
            VERTS.append(head_surf(x, z0 + (z1 - z0) * t,
                                   sink + (prof - sink) * m))
    for j in range(nv):
        for i in range(nu):
            a = base + j * (nu + 1) + i
            b = a + (nu + 1)
            add_face((a, a + 1, b + 1, b), color, part, True)


# --- measurements (metres, feet z=0, faces -Y, character LEFT = +X)
K = 0.96          # global trim so the finished figure is ~1.75 m
HF = -0.015       # elderly forward head carriage
ARM_Z = 1.400

# head ---------------------------------------------------------------------
HEAD_PROFILE = [
    (1.558, -0.070, 0.070, -0.066 + HF, 0.058 + HF),
    (1.598, -0.082, 0.082, -0.082 + HF, 0.074 + HF),
    (1.655, -0.090, 0.090, -0.090 + HF, 0.082 + HF),
    (1.715, -0.092, 0.092, -0.092 + HF, 0.084 + HF),
    (1.775, -0.086, 0.086, -0.088 + HF, 0.080 + HF),
    (1.812, -0.058, 0.058, -0.062 + HF, 0.054 + HF),
]
HEAD_RINGS = res(HEAD_PROFILE, 3)
tube(HEAD_RINGS, 'Z', "skin", "head")

# thinning grey hair: cap + band round the back and sides
tube(res([
    (1.728, -0.086, 0.086, -0.074 + HF, 0.082 + HF),   # tucked under the skin
    (1.734, -0.095, 0.095, -0.080 + HF, 0.090 + HF),
    (1.766, -0.098, 0.098, -0.092 + HF, 0.092 + HF),
    (1.800, -0.090, 0.090, -0.090 + HF, 0.086 + HF),
    (1.830, -0.058, 0.058, -0.060 + HF, 0.056 + HF),
], 3), 'Z', "hair", "head")
tube(res([
    (1.608, -0.079, 0.079, 0.028 + HF, 0.078 + HF),    # tucked under the skin
    (1.618, -0.088, 0.088, 0.022 + HF, 0.086 + HF),
    (1.672, -0.093, 0.093, 0.010 + HF, 0.090 + HF),
    (1.715, -0.097, 0.097, 0.000 + HF, 0.092 + HF),
], 3), 'Z', "hair", "head")

# ears
for _sx in (1.0, -1.0):
    tube(res([
        (0.084 * _sx, 0.008 + HF, 0.036 + HF, 1.655, 1.692),
        (0.096 * _sx, 0.002 + HF, 0.042 + HF, 1.648, 1.700),
        (0.103 * _sx, 0.010 + HF, 0.034 + HF, 1.656, 1.690),
    ], 2), 'X', "skin", "head", sides=10)

# nose - a swept profile instead of a block, so the bridge flows into the tip
tube(res([
    (1.648, -0.012, 0.012, -0.104 + HF, -0.086 + HF),
    (1.658, -0.013, 0.013, -0.118 + HF, -0.086 + HF),
    (1.668, -0.0125, 0.0125, -0.127 + HF, -0.086 + HF),
    (1.682, -0.011, 0.011, -0.120 + HF, -0.086 + HF),
    (1.697, -0.0095, 0.0095, -0.109 + HF, -0.086 + HF),
    (1.712, -0.008, 0.008, -0.100 + HF, -0.086 + HF),
], 3), 'Z', "skin", "head", sides=10)

# eyes ---------------------------------------------------------------------
EYE_X, EYE_Z = 0.042, 1.693              # centre of each eye on the face
APW, APU, APL = 0.0195, 0.0088, 0.0082   # aperture half-width, rise, drop


def ap(x, ex, h, over=0.0):
    """Lens-shaped lid edge: `h` tall mid-eye, pointed at the corners."""
    t = abs(x - ex) / APW
    return -over if t >= 1.0 else h * (1.0 - t * t) ** 1.15


def eye(sx):
    ex = EYE_X * sx
    # sclera / iris / pupil / catch-light: concentric shells, each a hair prouder
    face_disc(ex, EYE_Z, 0.0205, 0.0126, "sclera", "head", 0.0013, 0.0004,
              rim="sclera_sh")
    face_disc(ex, EYE_Z - 0.0005, 0.0101, 0.0101, "iris", "head", 0.0018, 0.0015,
              segs=14, rings=3, rim="iris_dk")
    face_disc(ex, EYE_Z - 0.0005, 0.0046, 0.0046, "pupil", "head", 0.0021, 0.0019,
              segs=12, rings=2)
    # one light in the room, so both catch-lights sit on the same side
    face_disc(ex + 0.0044, EYE_Z + 0.0044, 0.0023, 0.0023, "glint", "head",
              0.0024, 0.0023, segs=8, rings=1)
    # lids, drawn over the sclera - they cut the almond aperture
    face_band(ex - 0.0300, ex + 0.0300,
              lambda x: EYE_Z + ap(x, ex, APU),
              lambda x: EYE_Z + 0.0235,
              "skin", "head", 0.0026, -0.0012, nu=22, nv=4, fade=0.13,
              sink=-0.0014)
    face_band(ex - 0.0295, ex + 0.0295,
              lambda x: EYE_Z - ap(x, ex, APL, over=0.0024),
              lambda x: EYE_Z - 0.0245,
              "skin", "head", 0.0024, -0.0012, nu=22, nv=4, fade=0.13,
              sink=-0.0014)
    # lash line: the dark rim that gives the lid an edge
    face_band(ex - APW, ex + APW,
              lambda x: EYE_Z + ap(x, ex, APU),
              lambda x: EYE_Z + ap(x, ex, APU) + 0.0019,
              "eye", "head", 0.0029, 0.0028, nu=18, nv=1, fade=0.12,
              sink=-0.0010)
    # crease above the lid, then the white brow
    face_band(ex - 0.0240, ex + 0.0240,
              lambda x: EYE_Z + 0.0124 + 0.0024 * (1.0 - ((x - ex) / 0.0240) ** 2),
              lambda x: EYE_Z + 0.0150 + 0.0024 * (1.0 - ((x - ex) / 0.0240) ** 2),
              "socket", "head", 0.0030, 0.0031, nu=18, nv=1, fade=0.28,
              sink=-0.0012)
    face_band(ex - 0.0250, ex + 0.0250,
              lambda x: EYE_Z + 0.0156 + 0.0030 * (1.0 - ((x - ex) / 0.0250) ** 2),
              lambda x: EYE_Z + 0.0208 + 0.0038 * (1.0 - ((x - ex) / 0.0250) ** 2),
              "hair", "head", 0.0036, 0.0030, nu=24, nv=3, fade=0.30,
              sink=-0.0016)


eye(1.0)
eye(-1.0)

# mouth
# mouth: two lips meeting on a shared line, each tapering to the corners -
# the same lens trick as the eye aperture, or the lips read as a sticker
MW, ZM = 0.0262, 1.5902


def lens(x, p=0.85):
    return max(0.0, 1.0 - (x / MW) ** 2) ** p


def mouth_line(x):
    return ZM + 0.0032 * (1.0 - (x / MW) ** 2)


face_band(-MW, MW, mouth_line,
          lambda x: mouth_line(x) + 0.0054 * lens(x),
          "lip", "head", 0.0028, 0.0013, nu=22, nv=3, fade=0.10, sink=-0.0014)
face_band(-MW, MW,
          lambda x: mouth_line(x) - 0.0068 * lens(x), mouth_line,
          "lip", "head", 0.0013, 0.0028, nu=22, nv=3, fade=0.10, sink=-0.0014)
face_band(-MW, MW,
          lambda x: mouth_line(x) - 0.0009 * lens(x, 0.45),
          lambda x: mouth_line(x) + 0.0009 * lens(x, 0.45),
          "eye", "head", 0.0032, 0.0032, nu=22, nv=1, fade=0.08, sink=-0.0012)

# neck + shirt collar -------------------------------------------------------
tube(res([
    (1.380, -0.052, 0.052, -0.049, 0.049),
    (1.480, -0.049, 0.049, -0.046 + HF * 0.5, 0.046 + HF * 0.5),
    (1.566, -0.047, 0.047, -0.045 + HF, 0.045 + HF),
], 3), 'Z', "skin", "neck")
tube(res([
    (1.418, -0.080, 0.080, -0.074, 0.074),
    (1.492, -0.066, 0.066, -0.062, 0.062),
], 3), 'Z', "shirt", "neck", caps=(False, True))

# shoulder yoke - blue shirt above the sleeveless vest ----------------------
tube(res([
    (1.340, -0.176, 0.176, -0.108, 0.106),
    (1.405, -0.190, 0.190, -0.105, 0.104),
    (1.452, -0.158, 0.158, -0.096, 0.095),
    (1.478, -0.098, 0.098, -0.080, 0.079),
], 3), 'Z', "shirt", "chest")

# sweater vest --------------------------------------------------------------
tube(res([
    (0.955, -0.172, 0.172, -0.112, 0.110),
    (1.050, -0.175, 0.175, -0.122, 0.116),   # paunch
    (1.160, -0.178, 0.178, -0.124, 0.115),
    (1.265, -0.180, 0.180, -0.117, 0.110),
    (1.348, -0.182, 0.182, -0.110, 0.107),
], 3), 'Z', "vest", "chest")

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
tube(res([
    (0.836, -0.112, 0.112, -0.080, 0.080),
    (0.892, -0.164, 0.164, -0.113, 0.113),
    (0.990, -0.164, 0.164, -0.114, 0.114),
], 3), 'Z', "trousers", "pelvis")

# legs ----------------------------------------------------------------------
for side, sx in (("L", 1.0), ("R", -1.0)):
    cx = 0.090 * sx
    tube(res([
        (0.962, cx - 0.072, cx + 0.072, -0.098, 0.098),
        (0.880, cx - 0.074, cx + 0.074, -0.100, 0.100),
        (0.700, cx - 0.070, cx + 0.070, -0.094, 0.092),
        (0.500, cx - 0.064, cx + 0.064, -0.086, 0.084),
        (0.280, cx - 0.058, cx + 0.058, -0.076, 0.072),
        (0.128, cx - 0.052, cx + 0.052, -0.066, 0.062),
        (0.098, cx - 0.047, cx + 0.047, -0.058, 0.054),
    ], 3), 'Z', "trousers", "leg" + side)
    tube(res([
        (0.000, cx - 0.056, cx + 0.056, -0.190, 0.060),
        (0.026, cx - 0.062, cx + 0.062, -0.202, 0.064),
        (0.068, cx - 0.060, cx + 0.060, -0.168, 0.062),
        (0.105, cx - 0.055, cx + 0.055, -0.092, 0.058),
        (0.140, cx - 0.052, cx + 0.052, -0.070, 0.054),
    ], 2), 'Z', "shoe", "shoe" + side)

# arms - T-pose bind -------------------------------------------------------
for side, sx in (("L", 1.0), ("R", -1.0)):
    tube(res([
        (0.165 * sx, -0.060, 0.060, ARM_Z - 0.066, ARM_Z + 0.066),
        (0.250 * sx, -0.055, 0.055, ARM_Z - 0.058, ARM_Z + 0.058),
        (0.410 * sx, -0.048, 0.048, ARM_Z - 0.050, ARM_Z + 0.050),
        (0.560 * sx, -0.042, 0.042, ARM_Z - 0.044, ARM_Z + 0.044),
        (0.640 * sx, -0.038, 0.038, ARM_Z - 0.040, ARM_Z + 0.040),
    ], 3), 'X', "shirt", "arm" + side)
    tube(res([
        (0.638 * sx, -0.036, 0.036, ARM_Z - 0.038, ARM_Z + 0.038),
        (0.710 * sx, -0.040, 0.040, ARM_Z - 0.048, ARM_Z + 0.048),
        (0.790 * sx, -0.036, 0.036, ARM_Z - 0.044, ARM_Z + 0.044),
        (0.815 * sx, -0.022, 0.022, ARM_Z - 0.030, ARM_Z + 0.030),
    ], 3), 'X', "skin", "arm" + side)

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
bm.faces.ensure_lookup_table()
# recalc only closed shells: a flat open patch has no outside to find, so the
# face shells keep their authored winding and are flipped to face front instead
bmesh.ops.recalc_face_normals(
    bm, faces=[f for i, f in enumerate(bm.faces) if i not in PATCH])
bm.normal_update()
flip = [bm.faces[i] for i in PATCH if bm.faces[i].normal.y > 0.0]
if flip:
    bmesh.ops.reverse_faces(bm, faces=flip)
bm.normal_update()
for e in bm.edges:
    if len(e.link_faces) == 2:
        e.smooth = e.link_faces[0].normal.angle(e.link_faces[1].normal) < math.radians(68)
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

SRC = os.path.dirname(os.path.abspath(__file__))
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(SRC, NAME + ".blend"))

print("TRIS/QUADS:", len(me.polygons), "VERTS:", len(me.vertices),
      "BONES:", len(arm_data.bones))
print("WROTE:", fbx, os.path.getsize(fbx), "bytes")
