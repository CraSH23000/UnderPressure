"""The human test subject, in three sizes. Big round head, sculpted body, arms and shaped legs."""
from cf import *

SKIN = (0.93, 0.72, 0.58)
HAIR = (0.28, 0.16, 0.09)

# Torso cross-sections from crotch (0) to the base of the neck (1): (z, rx, ry, y_offset), as fractions of the
# half-width / half-depth. Negative y_offset pushes that ring forward (chest out).
SLIM = [(0.0, 0.55, 0.55), (0.07, 0.86, 0.8), (0.25, 0.93, 0.86), (0.45, 0.86, 0.8), (0.68, 0.96, 0.88, -0.04),
        (0.84, 1.0, 0.84, -0.02), (0.94, 0.78, 0.66), (1.0, 0.3, 0.3)]
MUSCLE = [(0.0, 0.5, 0.55), (0.07, 0.72, 0.74), (0.22, 0.76, 0.8), (0.38, 0.72, 0.78), (0.58, 0.95, 0.98, -0.1),
          (0.76, 1.07, 0.95, -0.06), (0.87, 1.02, 0.82), (0.95, 0.66, 0.6), (1.0, 0.28, 0.28)]

SIZES = {
    "S": dict(head_r=0.235, hip_z=0.20, torso=(0.16, 0.46), half=(0.13, 0.10), shape=SLIM, leg_x=0.06, leg_r=0.042,
              arm_r=(0.04, 0.036, 0.032, 0.034, 0.03), hand=0.085, shoe=(0.1, 0.16, 0.075), belt=0.27),
    "M": dict(head_r=0.275, hip_z=0.30, torso=(0.25, 0.69), half=(0.16, 0.12), shape=SLIM, leg_x=0.075, leg_r=0.05,
              arm_r=(0.047, 0.043, 0.037, 0.04, 0.034), hand=0.1, shoe=(0.115, 0.2, 0.085), belt=0.27),
    "L": dict(head_r=0.30, hip_z=0.38, torso=(0.32, 1.13), half=(0.33, 0.24), shape=MUSCLE, leg_x=0.15, leg_r=0.085,
              arm_r=(0.088, 0.1, 0.07, 0.082, 0.06), hand=0.165, shoe=(0.17, 0.27, 0.11), belt=0.3),
}


def build(size="M", tint=(0.12, 0.5, 0.95), x=0.0, name=None, hair_style=False, head_builder=None, hand_mat=None):
    p = SIZES[size]
    name = name or f"Human_{size}"
    skin = mat("Skin", SKIN, rough=0.55, sss=0.15)
    hair = mat("Hair", HAIR, rough=0.6)
    suit = mat("Tint", tint, rough=0.45)
    dark = mat("Dark", (0.06, 0.05, 0.07), rough=0.4)
    white = mat("EyeWhite", (0.97, 0.97, 0.97), rough=0.15)
    pupil = mat("Pupil", (0.03, 0.03, 0.05), rough=0.1)
    shine = mat("Shine", (1, 1, 1), rough=0.05, emission=(1, 1, 1))
    shoe_m = mat("Shoe", (0.16, 0.16, 0.2), rough=0.4)
    sole_m = mat("Sole", (0.92, 0.92, 0.9), rough=0.6)
    accent = mat("Accent", (0.95, 0.95, 0.97), rough=0.5)
    blush = mat("Blush", (1.0, 0.5, 0.55), rough=0.6)

    root = empty(name, (x, 0, 0), size=0.3)

    # ---------------------------------------------------------------- torso: one smooth sculpted shape
    z0, z1 = p["torso"]
    th = z1 - z0
    hw, hd = p["half"]
    prof = [(z * th, rx * hw, ry * hd, (yo * hd)) for (z, rx, ry, *rest) in p["shape"] for yo in [rest[0] if rest else 0.0]]
    parts = [loft("Torso", (x, 0, z0), prof, suit, segs=48)]

    def surface_y(zf, xf=0.0):
        """Front surface y at height fraction zf, x offset fraction xf (of that ring's width)."""
        rx, ry, yo = profile_at(prof, zf * th)
        return yo - ry * math.sqrt(max(0.0, 1 - xf * xf))

    # belt hugging the waist
    bz = p["belt"]
    rx, ry, yo = profile_at(prof, bz * th)
    belt = loft("Belt", (x, yo, z0 + bz * th - th * 0.035),
                [(0, rx * 1.03, ry * 1.04), (th * 0.035, rx * 1.05, ry * 1.06), (th * 0.07, rx * 1.03, ry * 1.04)], dark, segs=48)
    parts.append(belt)
    # buckle
    parts.append(box("Buckle", (x, yo - ry * 1.06 - 0.004, z0 + bz * th), (hw * 0.32, 0.012, th * 0.07), accent, bevel=0.004))
    # zip from collar to belt, following the chest
    zip_pts, zip_r = [], []
    for k in range(8):
        zf = bz + 0.04 + (0.93 - bz - 0.04) * k / 7
        zip_pts.append((x, surface_y(zf) - 0.003, z0 + zf * th))
        zip_r.append(0.006 if size != "L" else 0.01)
    parts.append(tube("Zip", zip_pts, zip_r, dark, segs=8, per_span=3))
    # chest patch
    zf, xf = 0.74, 0.45
    rx, ry, yo = profile_at(prof, zf * th)
    bx = x + xf * rx
    patch = box("Patch", (bx, surface_y(zf, xf) + 0.002, z0 + zf * th), (hw * 0.32, 0.01, th * 0.1), accent, bevel=0.004)
    patch.rotation_euler = (0, 0, math.radians(-35 * xf))
    parts.append(patch)
    # collar
    rx, ry, yo = profile_at(prof, 0.95 * th)
    parts.append(torus("Collar", (x, yo, z0 + 0.95 * th), rx * 0.92, th * 0.025 + 0.006, suit))
    parts[-1].scale = (1.0, ry / rx, 1.0)
    body = join("Body", parts)
    set_parent(body, root)

    # ---------------------------------------------------------------- head (short neck, sits right on the shoulders)
    hr = p["head_r"]
    hz = z1 + hr * 0.86
    hc = Vector((x, 0, hz))
    if head_builder is not None:
        head = head_builder(size, hc, hr, z1, body)  # another species' head on this body (e.g. the Chihuahua)
    else:
        head = human_head(size, hc, hr, z1, body, skin, hair, dark, pupil, hair_style)
    hz = hc.z

    # ---------------------------------------------------------------- arms (pivot at the shoulder) + mitten hands
    ar = p["arm_r"]
    for s, side in ((-1, "L"), (1, "R")):
        rx, ry, yo = profile_at(prof, 0.84 * th)
        sh = Vector((x + s * rx * 0.86, yo, z0 + 0.84 * th))
        length = th * (0.78 if size != "L" else 0.74)
        out = 0.22 if size != "L" else 0.42  # the big guy's arms hang wider, clear of his lats
        elbow = sh + Vector((s * length * out * 0.45, -length * 0.02, -length * 0.5))
        wrist = sh + Vector((s * length * out, -length * 0.14, -length * 0.98))
        mid_up = sh.lerp(elbow, 0.45)
        mid_lo = elbow.lerp(wrist, 0.45)
        arm = tube(f"Arm_{side}", [sh, mid_up, elbow, mid_lo, wrist], list(ar), suit, segs=20)
        set_origin(arm, sh)
        set_parent(arm, root)
        empty(f"Wrist_{side}", wrist, arm)
        hs = p["hand"]
        hpos = wrist + Vector((s * hs * 0.1, -hs * 0.05, -hs * 0.42))
        hm = hand_mat or skin
        mitt = [sphere("Palm", hpos, (hs, hs * 0.85, hs * 1.1), hm),
                sphere("Thumb", (hpos.x - s * hs * 0.38, hpos.y - hs * 0.25, hpos.z + hs * 0.15), (hs * 0.42, hs * 0.4, hs * 0.55), hm,
                       rot=(0, s * 30, 0)),
                cylinder("Cuff", wrist + Vector((0, 0, -hs * 0.02)), ar[-1] * 1.18, hs * 0.22, suit)]
        hand = join(f"Hand_{side}", mitt)
        set_origin(hand, hpos)
        set_parent(hand, root)

    # ---------------------------------------------------------------- legs: thigh, knee, calf, ankle (pivot at the hip)
    sx, sy, sz = p["shoe"]
    lr = p["leg_r"]
    for s, side in ((-1, "L"), (1, "R")):
        hip = Vector((x + s * p["leg_x"], 0, p["hip_z"] + lr * 0.4))
        ankle = Vector((hip.x, 0.0, sz * 0.85))
        knee = hip.lerp(ankle, 0.5) + Vector((0, -lr * 0.25, 0))
        pts = [hip, hip.lerp(knee, 0.5) + Vector((s * lr * 0.08, 0, 0)), knee, knee.lerp(ankle, 0.4) + Vector((0, lr * 0.12, 0)), ankle]
        radii = [lr * 1.3, lr * 1.18, lr * 0.98, lr * 1.08, lr * 0.85]
        leg = join(f"Leg_{side}", [
            tube("LegTube", pts, radii, suit, segs=20),
            cylinder("PantCuff", ankle + Vector((0, 0, lr * 0.25)), lr * 0.98, lr * 0.45, suit),
        ])
        set_origin(leg, hip)
        set_parent(leg, root)
        shoe = join(f"Shoe_{side}", [
            sphere("Upper", (hip.x, -sy * 0.12, sz * 0.62), (sx, sy, sz * 0.95), shoe_m),
            sphere("SoleBox", (hip.x, -sy * 0.12, sz * 0.2), (sx * 1.06, sy * 1.04, sz * 0.42), sole_m),
        ])
        set_origin(shoe, (hip.x, 0, 0))
        set_parent(shoe, leg)

    # ---------------------------------------------------------------- anchors for cosmetics
    rx, ry, yo = profile_at(prof, 0.5 * th)
    empty("Anchor_Torso", (x, yo, z0 + 0.5 * th), body, scale=(hw, hd, th / 2))
    return root


def human_head(size, hc, hr, z1, body, skin, hair, dark, pupil, hair_style):
    """The bald human head with vacant dot eyes (hair and beards are cosmetics)."""
    x, hz = hc.x, hc.z
    head_parts = [sphere("Skull", hc, (hr * 2, hr * 1.92, hr * 1.96), skin, segs=48, rings=24),
                  cylinder("Neck", (x, 0, z1 - 0.02 + hr * 0.12), hr * 0.3, hr * 0.3, skin)]
    # vacant face (PlateUp / Gang Beasts): no nose, mouth, brows or blush - just the two dot eyes below
    for s in (-1, 1):
        head_parts.append(sphere("Ear", (x + s * hr * 0.97, 0.01, hz - hr * 0.05), (hr * 0.16, hr * 0.26, hr * 0.3), skin))
    # hair and beards are cosmetics now (kept here so they can be exported separately later)
    if hair_style and size == "S":
        head_parts.append(dome("HairCap", hc + Vector((0, 0.015, 0.01)), (hr * 2.08, hr * 2.04, hr * 2.04), hair, keep_above=0.3, tilt=24))
        for ang, lean in ((-20, -25), (0, 0), (20, 25)):
            head_parts.append(sphere("Sprout", hc + Vector((math.sin(math.radians(ang)) * hr * 0.25, 0.02, hr * 1.12)),
                                     (hr * 0.12, hr * 0.12, hr * 0.5), hair, rot=(0, lean, 0)))
    elif hair_style and size == "M":
        head_parts.append(dome("HairCap", hc + Vector((0, 0.02, 0.012)), (hr * 2.07, hr * 2.04, hr * 2.04), hair, keep_above=0.22, tilt=26))
        head_parts.append(sphere("Swoop", face_point(hc, hr, -14, 52, -0.01), (hr * 1.1, hr * 0.45, hr * 0.42), hair, rot=(-40, 0, -18)))
        head_parts.append(sphere("Swoop2", face_point(hc, hr, 22, 58, -0.015), (hr * 0.7, hr * 0.35, hr * 0.3), hair, rot=(-45, 0, 25)))
    elif hair_style:
        head_parts.append(dome("HairCap", hc + Vector((0, 0.015, 0.03)), (hr * 2.04, hr * 2.0, hr * 1.9), hair, keep_above=0.45, tilt=18))
        head_parts.append(sphere("Beard", hc + Vector((0, -hr * 0.42, -hr * 0.62)), (hr * 1.45, hr * 0.95, hr * 1.0), hair))
    head = join("Head", head_parts)
    set_origin(head, hc)
    set_parent(head, body)

    # two small, wide-set black dot eyes (the game blinks them by squashing Eye_* and nudges them to look around)
    for s, side in ((-1, "L"), (1, "R")):
        yaw, pitch = s * 25, 0
        ep = face_point(hc, hr, yaw, pitch, -hr * 0.05)
        eye = sphere(f"Eye_{side}", ep, (hr * 0.23, hr * 0.13, hr * 0.29), pupil, rot=face_rot(yaw, pitch))
        set_parent(eye, head)

    empty("Anchor_Hat", (x, 0, hz + hr * 0.86), head)
    return head
