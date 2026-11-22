"""The skeleton: a cartoon walking skeleton in three sizes. Big sculpted skull (deep eye sockets with little glowing
dots for eyes, cheekbones, nose hole, a toothy grin and a separate jaw), ribcage, spine, pelvis, knobbly bones and
bony hands and feet. Bones take the player's color (material 'Tint'); teeth stay 'Bone' white."""
from cf import *

IVORY = (0.93, 0.9, 0.8)

SIZES = {
    "S": dict(head_r=0.25, hip_z=0.25, ankle_z=0.05, pelvis_w=0.19, rib=(0.21, 0.16, 0.17), rib_z=0.42, sh_z=0.505,
              sh_x=0.115, br=0.02, leg_x=0.065, n_ribs=4),
    "M": dict(head_r=0.27, hip_z=0.44, ankle_z=0.07, pelvis_w=0.26, rib=(0.3, 0.22, 0.27), rib_z=0.7, sh_z=0.83,
              sh_x=0.17, br=0.026, leg_x=0.085, n_ribs=5),
    "L": dict(head_r=0.29, hip_z=0.6, ankle_z=0.1, pelvis_w=0.34, rib=(0.62, 0.42, 0.48), rib_z=1.02, sh_z=1.24,
              sh_x=0.34, br=0.05, leg_x=0.15, n_ribs=6),
}


def bone(name, a, b, r, material, knob_a=1.9, knob_b=1.9, waist=0.72):
    """A cartoon bone from a to b: a narrow shaft with round knobs on both ends."""
    a, b = Vector(a), Vector(b)
    shaft = tube(name, [a, a.lerp(b, 0.5), b], [r, r * waist, r], material, segs=12, per_span=4)
    parts = [shaft]
    side = (b - a).cross(Vector((0, 0, 1)))
    if side.length < 1e-4:
        side = Vector((1, 0, 0))
    side.normalize()
    for p, k in ((a, knob_a), (b, knob_b)):
        if k <= 0:
            continue
        # double knob (like a real bone end), side by side
        for s in (-1, 1):
            parts.append(sphere(name + "Knob", p + side * s * r * 0.55, (r * k,) * 3, material, segs=14, rings=8))
    return parts


def build(size="M", tint=IVORY, x=0.0, name=None):
    p = SIZES[size]
    name = name or f"Skeleton_{size}"
    bone_m = mat("Tint", tint, rough=0.55)
    teeth_m = mat("Bone", (0.97, 0.95, 0.88), rough=0.35)
    dark = mat("Dark", (0.05, 0.04, 0.06), rough=0.8)
    glow = mat("Glow", (0.85, 1.0, 0.95), rough=0.2, emission=(0.6, 1.0, 0.9))
    br = p["br"]

    root = empty(name, (x, 0, 0), size=0.3)

    # ---------------------------------------------------------------- torso: spine, ribcage, sternum, clavicles, pelvis
    rw, rd, rh = p["rib"]
    rz = p["rib_z"]
    sh_z, sh_x = p["sh_z"], p["sh_x"]
    hip_z, pw = p["hip_z"], p["pelvis_w"]
    parts = []
    # spine: a column of vertebrae from the pelvis up to the base of the skull
    spine_y = rd * 0.42
    top = sh_z + br * 2.5
    n_v = 11
    for i in range(n_v):
        t = i / (n_v - 1)
        z = hip_z + 0.03 + (top - hip_z - 0.03) * t
        y = spine_y * (1 - 0.35 * math.sin(math.pi * t)) if t < 0.85 else spine_y * 0.6
        w = br * (2.6 - 0.8 * t)
        parts.append(sphere("Vertebra", (x, y, z), (w, w * 0.85, w * 0.62), bone_m, segs=12, rings=6))
        parts.append(sphere("Process", (x, y + w * 0.6, z - w * 0.1), (w * 0.35, w * 0.7, w * 0.35), bone_m, segs=8, rings=5))
    # ribs: pairs of curved bones from the spine around to the sternum (the lower ones float free)
    n = p["n_ribs"]
    top_rib = rz + rh * 0.42
    for i in range(n):
        t = i / max(1, n - 1)
        z0 = top_rib - rh * 0.8 * t
        wk = 0.78 + 0.32 * math.sin(math.pi * min(1.0, t * 0.85 + 0.15))  # widest in the middle
        end = math.pi * (0.9 if i < n - 2 else 0.7)
        for s in (-1, 1):
            pts = []
            for k in range(9):
                ph = 0.12 + (end - 0.12) * k / 8
                pts.append((x + s * rw / 2 * wk * math.sin(ph), spine_y * 0.9 * math.cos(ph) - (rd / 2) * (1 - math.cos(ph)) * 0.05,
                            z0 - rh * 0.12 * (1 - math.cos(ph)) / 2))
            parts.append(tube("Rib", pts, [br * 0.62] * 9, bone_m, segs=10, per_span=2))
    sy = -rd * 0.46
    parts.append(tube("Sternum", [(x, sy, top_rib + br), (x, sy - br * 0.2, top_rib - rh * 0.2), (x, sy - br * 0.1, top_rib - rh * 0.45),
                                  (x, sy + br * 0.3, top_rib - rh * 0.62)],
                      [(br * 0.7, br * 2.4), (br * 0.75, br * 2.0), (br * 0.7, br * 1.7), (br * 0.5, br * 0.8)], bone_m, segs=14, per_span=3))
    for s in (-1, 1):  # clavicles
        parts.append(tube("Clavicle", [(x + s * br * 1.5, -rd * 0.45, top_rib + br), (x + s * sh_x * 0.55, -rd * 0.3, top_rib + br * 2.2),
                                       (x + s * sh_x * 0.95, -0.0, sh_z + br * 0.5)], [br * 0.8, br * 0.7, br * 0.9], bone_m, segs=10, per_span=3))
    # pelvis: two flared hip wings around a sacrum, joined by a bony arch at the front
    for s in (-1, 1):
        # hip wings: curved bony blades sweeping out from the sacrum and round to the front
        wing = [(x + s * pw * 0.12, spine_y * 0.6, hip_z + pw * 0.08), (x + s * pw * 0.4, spine_y * 0.35, hip_z + pw * 0.3),
                (x + s * pw * 0.52, -pw * 0.04, hip_z + pw * 0.26), (x + s * pw * 0.38, -pw * 0.2, hip_z + pw * 0.08)]
        parts.append(tube("HipWing", wing, [(br * 0.9, pw * 0.1), (br * 1.0, pw * 0.16), (br * 1.0, pw * 0.13), (br * 0.9, br * 1.2)], bone_m,
                          segs=14, per_span=4))
        parts.append(sphere("HipSocket", (x + s * p["leg_x"] * 1.05, 0, hip_z), (br * 2.4,) * 3, bone_m, segs=12, rings=8))
    parts.append(tube("Sacrum", [(x, spine_y * 0.75, hip_z + pw * 0.28), (x, spine_y * 0.85, hip_z + pw * 0.08), (x, spine_y * 0.65, hip_z - pw * 0.08)],
                      [(br * 1.2, pw * 0.15), (br * 1.2, pw * 0.1), (br * 0.9, br)], bone_m, segs=12, per_span=3))
    parts.append(tube("PubicArch", [(x - p["leg_x"] * 0.95, -pw * 0.1, hip_z - br * 0.5), (x, -pw * 0.22, hip_z - br * 2.0),
                                    (x + p["leg_x"] * 0.95, -pw * 0.1, hip_z - br * 0.5)], [(br * 0.9, br * 1.5)] * 3, bone_m, segs=12, per_span=4))
    body = join("Body", parts)
    set_parent(body, root)

    # ---------------------------------------------------------------- skull: one sculpted shape
    hr = p["head_r"]
    hz = top + hr * 0.78
    hc = Vector((x, -0.01, hz))
    radii = (hr * 1.0, hr * 0.96, hr * 0.96)

    def gauss(d, cx, cz, wx, wz):
        return math.exp(-(((d.x - cx) / wx) ** 2 + ((d.z - cz) / wz) ** 2)) if d.y < 0 else 0.0

    def skull_r(d):
        r = 1.0
        for s in (-1, 1):
            r -= 0.24 * gauss(d, s * 0.37, 0.0, 0.21, 0.22)   # big, deep eye sockets
            r += 0.08 * gauss(d, s * 0.56, -0.32, 0.17, 0.13)  # cheekbones
        r += 0.1 * gauss(d, 0.0, -0.62, 0.42, 0.26)            # upper jaw pushes forward to hold the teeth
        r -= 0.12 * gauss(d, 0.0, -0.24, 0.09, 0.11)           # nose hole
        r += 0.04 * gauss(d, 0.0, 0.42, 0.55, 0.12)            # brow ridge
        # the sides pinch in under the cheekbones, so the face tapers to the teeth
        if d.z < -0.35:
            r *= 1.0 - 0.32 * min(1.0, (-d.z - 0.35) / 0.45) * min(1.0, abs(d.x) / 0.6)
        return r

    head_parts = [sculpted_sphere("Skull", hc, radii, [bone_m], skull_r, lambda d: 0, segs=56, rings=28)]
    # dark sockets and nose hole lie on that surface
    for s in (-1, 1):
        head_parts.append(surface_patch("Socket", hc, radii, skull_r, 0.2, 0.21, 0.0, dark, out=hr * 0.03, x0=s * 0.37, rings=8, segs=32))
    head_parts.append(surface_patch("NoseHole", hc, radii, skull_r, 0.08, 0.1, -0.24, dark, out=hr * 0.025, rings=5, segs=24,
                                    shape=lambda th: 1.0 + 0.35 * math.sin(th) - 0.25 * abs(math.cos(2 * th)) * (math.sin(th) > 0)))
    # upper teeth along the front of the upper jaw
    n_t = 6
    for i in range(n_t):
        a = (i - (n_t - 1) / 2) * 13
        d = Vector((math.sin(math.radians(a)) * 0.55, -1.0, -0.62)).normalized()
        tp = surface_point(hc, radii, skull_r, d, -hr * 0.03)
        head_parts.append(box("Tooth", tp, (hr * 0.12, hr * 0.1, hr * 0.17), teeth_m, bevel=hr * 0.03, rot=(0, 0, a * 0.8), segs=2))
    # lower jaw: a slim U of bone tucked right under the upper teeth, rising back to hinges under the cheekbones
    tf = surface_point(hc, radii, skull_r, Vector((0, -1.0, -0.62)).normalized(), 0)
    front = tf + Vector((0, hr * 0.05, -hr * 0.2))
    jaw_pts = []
    for s_ in (-1, 1):
        pass
    hingeL = Vector((x - hr * 0.6, hc.y + hr * 0.08, hz - hr * 0.42))
    hingeR = Vector((x + hr * 0.6, hc.y + hr * 0.08, hz - hr * 0.42))
    cornerL = Vector((x - hr * 0.45, front.y + hr * 0.3, front.z + hr * 0.02))
    cornerR = Vector((x + hr * 0.45, front.y + hr * 0.3, front.z + hr * 0.02))
    frontL = Vector((x - hr * 0.22, front.y + hr * 0.04, front.z))
    frontR = Vector((x + hr * 0.22, front.y + hr * 0.04, front.z))
    jaw_pts = [hingeL, cornerL, frontL, front + Vector((0, -hr * 0.02, -hr * 0.02)), frontR, cornerR, hingeR]
    th, ht = hr * 0.065, hr * 0.12  # thin bone, taller than it is thick
    head_parts.append(tube("Jaw", jaw_pts, [(th * 0.8, ht * 0.8), (th, ht), (th, ht), (th * 1.1, ht * 1.05), (th, ht), (th, ht), (th * 0.8, ht * 0.8)],
                           bone_m, segs=14, per_span=4))
    for i in range(n_t - 1):
        t = i / (n_t - 2)
        a = frontL.lerp(frontR, t)
        a.y = front.y + hr * 0.02 - math.sin(math.pi * t) * hr * 0.04
        tp = a + Vector((0, 0, ht * 0.95))
        head_parts.append(box("Tooth", tp, (hr * 0.09, hr * 0.08, hr * 0.12), teeth_m, bevel=hr * 0.025, segs=2))
    head = join("Head", head_parts)
    set_origin(head, hc)
    set_parent(head, body)

    # little glowing dots deep in the sockets are the eyes (the game blinks / moves them)
    for s, side in ((-1, "L"), (1, "R")):
        d = Vector((s * 0.37, -1.0, 0.0)).normalized()
        ep = surface_point(hc, radii, skull_r, d, hr * 0.04)
        eye = sphere(f"Eye_{side}", ep, (hr * 0.08, hr * 0.05, hr * 0.09), glow, segs=14, rings=8)
        set_parent(eye, head)

    # ---------------------------------------------------------------- arms: humerus, then radius + ulna side by side
    arm_len = (sh_z - hip_z) * 1.12
    for s, side in ((-1, "L"), (1, "R")):
        sh = Vector((x + s * sh_x, 0.0, sh_z))
        elbow = sh + Vector((s * arm_len * 0.08, 0.0, -arm_len * 0.5))
        wrist = sh + Vector((s * arm_len * 0.13, -arm_len * 0.1, -arm_len * 0.97))
        arm = bone("Humerus", sh, elbow, br * 1.1, bone_m, knob_a=2.0, knob_b=1.6)
        for o in (-1, 1):
            off = Vector((o * br * 0.65, 0, 0))
            arm += bone("Forearm", elbow + off, wrist + off, br * 0.62, bone_m, knob_a=0, knob_b=1.5)
        arm_ob = join(f"Arm_{side}", arm)
        set_origin(arm_ob, sh)
        set_parent(arm_ob, root)
        # bony hand, palm facing the body (so carried things sit between the palms): wrist bones, four metacarpals
        # fanning front to back, jointed fingers, and the thumb at the FRONT of the hand
        hs = br * 5.0
        palm_c = wrist + Vector((0, -hs * 0.05, -hs * 0.45))
        hand = [sphere("Carpals", wrist + Vector((0, -hs * 0.02, -hs * 0.1)), (br * 1.7, br * 2.6, br * 1.8), bone_m, segs=12, rings=6)]
        for f in range(4):
            fy = (f - 1.5) * hs * 0.2 + hs * 0.06  # f = 0 is the index finger, next to the thumb at the front
            base = wrist + Vector((0, fy * 0.4, -hs * 0.12))
            knuck = wrist + Vector((-s * hs * 0.02, fy, -hs * 0.62))
            mid = knuck + Vector((-s * hs * 0.05, fy * 0.12, -hs * 0.3))
            tip = mid + Vector((-s * hs * 0.08, fy * 0.05, -hs * 0.2))
            hand.append(tube("Metacarpal", [base, knuck], [br * 0.5, br * 0.55], bone_m, segs=8, per_span=2))
            hand.append(sphere("Knuckle", knuck, (br * 1.05,) * 3, bone_m, segs=10, rings=6))
            hand.append(tube("Finger", [knuck, mid, tip], [br * 0.5, br * 0.45, br * 0.4], bone_m, segs=8, per_span=2))
            hand.append(sphere("Joint", mid, (br * 0.95,) * 3, bone_m, segs=10, rings=6))
        tb = wrist + Vector((-s * hs * 0.04, -hs * 0.3, -hs * 0.18))
        hand.append(tube("Thumb", [tb, tb + Vector((-s * hs * 0.1, -hs * 0.14, -hs * 0.18)), tb + Vector((-s * hs * 0.18, -hs * 0.18, -hs * 0.34))],
                         [br * 0.55, br * 0.5, br * 0.42], bone_m, segs=8, per_span=2))
        hand_ob = join(f"Hand_{side}", hand)
        set_origin(hand_ob, palm_c)
        set_parent(hand_ob, root)

    # ---------------------------------------------------------------- legs: femur, kneecap, tibia + fibula; bony feet
    for s, side in ((-1, "L"), (1, "R")):
        hip = Vector((x + s * p["leg_x"], 0.0, hip_z))
        ankle = Vector((hip.x, 0.0, p["ankle_z"] + br * 1.5))
        knee = hip.lerp(ankle, 0.5) + Vector((0, -br * 0.5, 0))
        leg = bone("Femur", hip, knee, br * 1.3, bone_m, knob_a=1.5, knob_b=1.7)
        leg.append(sphere("Kneecap", knee + Vector((0, -br * 1.6, br * 0.5)), (br * 2.0, br * 1.2, br * 2.2), bone_m, segs=12, rings=8))
        leg += bone("Tibia", knee + Vector((-s * br * 0.3, 0, 0)), ankle, br * 0.95, bone_m, knob_a=0, knob_b=1.6)
        leg += bone("Fibula", knee + Vector((s * br * 1.3, br * 0.3, -br * 1.5)), ankle + Vector((s * br * 1.0, br * 0.2, br * 0.8)), br * 0.5, bone_m, knob_a=1.2, knob_b=0)
        leg_ob = join(f"Leg_{side}", leg)
        set_origin(leg_ob, hip)
        set_parent(leg_ob, root)
        fl = br * 8.0  # foot length
        foot = [sphere("Heel", (hip.x, br * 1.2, br * 1.5), (br * 2.4, br * 2.8, br * 2.4), bone_m, segs=12, rings=8),
                sphere("Talus", (hip.x, -br * 0.6, br * 2.4), (br * 2.2, br * 2.2, br * 1.6), bone_m, segs=12, rings=8)]
        for t in range(3):
            tx = hip.x + (t - 1) * br * 1.4
            m0 = Vector((hip.x + (t - 1) * br * 0.7, -br * 1.2, br * 2.0))
            m1 = Vector((tx, -fl * 0.42, br * 1.0))
            foot.append(tube("Metatarsal", [m0, m1], [br * 0.6, br * 0.55], bone_m, segs=8, per_span=2))
            foot.append(sphere("Ball", m1, (br * 1.0,) * 3, bone_m, segs=10, rings=6))
            foot.append(tube("Toe", [m1, m1 + Vector((0, -fl * 0.16, -br * 0.2))], [br * 0.55, br * 0.5], bone_m, segs=8, per_span=2))
        foot_ob = join(f"Shoe_{side}", foot)
        set_origin(foot_ob, (hip.x, 0, 0))
        set_parent(foot_ob, leg_ob)

    empty("Anchor_Hat", (x, hc.y, hz + radii[2] * 0.98), head)
    empty("Anchor_Torso", (x, 0, rz), body, scale=(rw / 2, rd / 2, rh / 2))
    return root
