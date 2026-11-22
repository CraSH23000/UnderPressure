"""SHARC: an upright, chubby shark standing on stubby legs, with its tail flopped on the floor behind it.
Countershaded (top takes the player color, belly and chin white), a wide deadpan grin full of little triangle teeth,
beady dot eyes, gill slits, a dorsal fin and pectoral-fin arms."""
from cf import *

WHITE = (0.95, 0.95, 0.93)

SIZES = {
    "S": dict(head_r=0.27, body=(0.5, 0.46, 0.46), body_z=0.3, hip_z=0.13, leg_r=0.05, leg_x=0.09, foot=(0.12, 0.17, 0.07), k=0.8),
    "M": dict(head_r=0.31, body=(0.62, 0.56, 0.62), body_z=0.44, hip_z=0.19, leg_r=0.06, leg_x=0.11, foot=(0.14, 0.2, 0.08), k=1.0),
    "L": dict(head_r=0.35, body=(0.98, 0.8, 0.96), body_z=0.7, hip_z=0.28, leg_r=0.1, leg_x=0.2, foot=(0.22, 0.3, 0.11), k=1.45),
}


def build(size="M", tint=(0.4, 0.55, 0.68), x=0.0, name=None):
    p = SIZES[size]
    name = name or f"Sharc_{size}"
    k = p["k"]
    skin = mat("Tint", tint, rough=0.45)
    belly = mat("Belly", WHITE, rough=0.5)
    dark = mat("Dark", (0.12, 0.03, 0.05), rough=0.6)
    teeth = mat("Bone", (0.98, 0.97, 0.93), rough=0.3)
    eye_m = mat("Pupil", (0.02, 0.02, 0.03), rough=0.05)

    root = empty(name, (x, 0, 0), size=0.3)

    # ---------------------------------------------------------------- body: a plump pear with a white belly
    bw, bd, bh = p["body"]
    bz = p["body_z"]
    bc = Vector((x, 0.0, bz))
    b_radii = (bw / 2, bd / 2, bh / 2)

    # a fish-shaped body: deepest at the chest, tapering down and curving back into the tail (not a ball)
    zb = p["hip_z"] - 0.02 * k
    zt = bz + bh * 0.5
    H = zt - zb
    shape = [(0.0, 0.26, 0.34, 0.5), (0.14, 0.58, 0.6, 0.28), (0.34, 0.86, 0.82, 0.08), (0.58, 0.98, 0.9, -0.02),
             (0.8, 0.92, 0.84, 0.0), (1.0, 0.66, 0.62, 0.03)]
    prof = [(f * H, rx * bw / 2, ry * bd / 2, yo * bd / 2) for (f, rx, ry, yo) in shape]
    parts = [loft("Core", (x, 0.0, zb), prof, skin, segs=40, per_span=5)]
    # white underside: a slightly forward copy of the front, so the countershading has a clean curved edge
    bprof = [(z, rx * 0.72, ry, yo - ry * 0.055) for (z, rx, ry, yo) in prof[1:-1]]
    parts.append(loft("Belly", (x, 0.0, zb), bprof, belly, segs=40, per_span=5))
    tail_root = Vector((x, prof[0][3] + bd * 0.12, zb + H * 0.06))
    # tail: a thick stalk flopped on the floor behind, ending in a crescent fin
    t0 = tail_root + Vector((0, -bd * 0.12, H * 0.1))
    t1 = Vector((x, bd * 0.62, p["hip_z"] * 0.8))
    t2 = Vector((x, bd * 0.62 + 0.2 * k, 0.07 * k))
    parts.append(tube("Tail", [t0, t1, t2], [0.13 * k, 0.09 * k, 0.05 * k], skin, segs=16, per_span=4))
    for lobe, (dy, dz, ln) in (("TailUpper", (0.14, 0.24, 1.0)), ("TailLower", (0.12, -0.02, 0.55))):
        tip = t2 + Vector((0, dy * k * ln, dz * k * ln))
        parts.append(tube(lobe, [t2, t2.lerp(tip, 0.5) + Vector((0, 0.02 * k, 0)), tip],
                          [(0.022 * k, 0.07 * k), (0.018 * k, 0.06 * k), (0.008 * k, 0.012 * k)], skin, segs=12, per_span=4))
    body = join("Body", parts)
    set_parent(body, root)

    # ---------------------------------------------------------------- head: rounded snout, wide grin, white chin
    hr = p["head_r"]
    hz = bz + bh * 0.38 + hr * 0.62
    hc = Vector((x, -0.03 * k, hz))
    radii = (hr * 0.95, hr * 1.18, hr * 0.9)

    def gauss(d, cx, cz, wx, wz):
        return math.exp(-(((d.x - cx) / wx) ** 2 + ((d.z - cz) / wz) ** 2)) if d.y < 0 else 0.0

    def head_r(d):
        r = 1.0
        r += 0.22 * gauss(d, 0.0, 0.02, 0.42, 0.36)      # rounded, slightly pointed snout pushes forward
        r -= 0.05 * gauss(d, 0.0, -0.4, 0.7, 0.06)       # the mouth line is a slight groove
        if d.z < -0.3:                                     # flatter underside
            r *= 1.0 - 0.15 * min(1.0, (-d.z - 0.3) / 0.6)
        return r

    hp = [sculpted_sphere("Skull", hc, radii, [skin], head_r, lambda d: 0, segs=56, rings=28),
          surface_patch("Chin", hc, radii, head_r, 0.62, 0.32, -0.7, belly, out=0.003, rings=10, segs=40)]
    # the grin: a wide shallow crescent, with a row of little triangle teeth hanging from the top lip
    hp.append(surface_patch("Mouth", hc, radii, head_r, 0.5, 0.07, -0.42, dark, out=0.004, rings=6, segs=48,
                            shape=lambda th: 1.0 - 0.45 * max(0.0, math.sin(th)) * (1 - abs(math.cos(th)))))
    n_t = 9
    for i in range(n_t):
        u = (i - (n_t - 1) / 2) / ((n_t - 1) / 2)
        d = Vector((u * 0.44, -1.0, -0.39 + 0.05 * u * u)).normalized()
        tp = surface_point(hc, radii, head_r, d, hr * 0.0)
        hp.append(cylinder("Tooth", tp + Vector((0, 0, -hr * 0.035)), hr * 0.035, hr * 0.07, teeth, segs=8, r2=0.0, rot=(180, 0, 0)))
    # dorsal fin on top of the head: rises straight up, then sweeps back
    fb = surface_point(hc, radii, head_r, Vector((0, 0.35, 0.94)).normalized(), -hr * 0.12)
    fin = [fb, fb + Vector((0, 0.0, 0.12 * k)), fb + Vector((0, 0.08 * k, 0.22 * k)), fb + Vector((0, 0.18 * k, 0.29 * k))]
    hp.append(tube("Dorsal", fin, [(0.14 * k, 0.03 * k), (0.12 * k, 0.026 * k), (0.06 * k, 0.018 * k), (0.012 * k, 0.008 * k)], skin,
                   segs=14, per_span=4))
    # gill slits behind the eyes
    for s in (-1, 1):
        for g in range(3):
            d = Vector((s * 1.0, -0.15 + g * 0.16, -0.22)).normalized()
            gp = surface_point(hc, radii, head_r, d, -hr * 0.005)
            hp.append(tube("Gill", [gp + Vector((0, 0, hr * 0.12)), gp, gp - Vector((0, -hr * 0.02, hr * 0.1))],
                           [hr * 0.018, hr * 0.022, hr * 0.016], dark, segs=8, per_span=2))
    head = join("Head", hp)
    set_origin(head, hc)
    set_parent(head, body)

    # beady, deadpan dot eyes on the sides of the snout
    for s, side in ((-1, "L"), (1, "R")):
        d = Vector((s * 0.62, -1.0, 0.16)).normalized()
        ep = surface_point(hc, radii, head_r, d, -hr * 0.02)
        yaw = math.degrees(math.atan2(d.x, -d.y))
        eye = sphere(f"Eye_{side}", ep, (hr * 0.17, hr * 0.12, hr * 0.2), eye_m, rot=face_rot(yaw, 6), segs=18, rings=10)
        set_parent(eye, head)

    # ---------------------------------------------------------------- pectoral-fin arms and little fin hands
    for s, side in ((-1, "L"), (1, "R")):
        rx_, ry_, yo_ = profile_at(prof, (bz + bh * 0.18) - zb)
        sh = Vector((x + s * rx_ * 0.9, yo_ - 0.02 * k, bz + bh * 0.18))
        mid = sh + Vector((s * 0.07 * k, -0.02 * k, -0.09 * k))
        wrist = sh + Vector((s * 0.1 * k, -0.06 * k, -0.19 * k))
        arm = tube(f"Arm_{side}", [sh, mid, wrist], [(0.05 * k, 0.085 * k), (0.035 * k, 0.07 * k), (0.03 * k, 0.045 * k)], skin, segs=14, per_span=3)
        set_origin(arm, sh)
        set_parent(arm, root)
        hs = 0.09 * k
        hpos = wrist + Vector((s * 0.01 * k, -0.01 * k, -hs * 0.4))
        hand = join(f"Hand_{side}", [sphere("FinTip", hpos, (hs * 0.75, hs * 1.0, hs * 0.85), skin, segs=16, rings=8),
                                     sphere("FinUnder", hpos + Vector((0, -hs * 0.05, -hs * 0.12)), (hs * 0.6, hs * 0.8, hs * 0.55), belly, segs=14, rings=8)])
        set_origin(hand, hpos)
        set_parent(hand, root)

    # ---------------------------------------------------------------- stubby legs, round feet with white soles
    fx, fy, fz = p["foot"]
    for s, side in ((-1, "L"), (1, "R")):
        hip = Vector((x + s * p["leg_x"], -0.02 * k, p["hip_z"] + 0.04 * k))
        ankle = Vector((hip.x, -0.02 * k, fz * 0.75))
        leg = tube(f"Leg_{side}", [hip, hip.lerp(ankle, 0.5), ankle], [p["leg_r"] * 1.15, p["leg_r"], p["leg_r"] * 0.9], skin, segs=14, per_span=3)
        set_origin(leg, hip)
        set_parent(leg, root)
        shoe = join(f"Shoe_{side}", [
            sphere("Foot", (hip.x, -fy * 0.2, fz * 0.55), (fx, fy, fz), skin, segs=20, rings=10),
            sphere("Sole", (hip.x, -fy * 0.2, fz * 0.18), (fx * 1.02, fy * 1.02, fz * 0.38), belly, segs=20, rings=8),
        ])
        set_origin(shoe, (hip.x, 0, 0))
        set_parent(shoe, leg)

    empty("Anchor_Hat", (x, hc.y, hz + radii[2] * 0.98), head)
    empty("Anchor_Torso", tuple(bc), body, scale=(bw / 2, bd / 2, bh / 2))
    return root
