"""The yeti: a shaggy little snow monster with puppy charm (big head, floppy ears, button nose, oversized paws,
two little fangs). Deadpan dot eyes like the humans. Fur takes the player's color (material 'Tint')."""
from cf import *

FACE = (0.4, 0.45, 0.56)
BONE = (0.93, 0.9, 0.8)

SIZES = {
    # elbow / wrist are offsets from the shoulder (x outward, y forward is negative, z down is negative)
    "S": dict(head_r=0.26, body=(0.44, 0.38, 0.42), body_z=0.3, hip_z=0.13, leg_r=0.06, leg_x=0.09,
              arm_r=0.06, hand=0.13, foot=(0.15, 0.22, 0.09), fur_len=0.11, fur_w=0.035,
              elbow=(0.07, -0.02, -0.13), wrist=(0.1, -0.07, -0.25), head_fwd=0.0, head_drop=0.0, hump=False,
              body_fur=180, head_fur=250),
    "M": dict(head_r=0.3, body=(0.6, 0.5, 0.64), body_z=0.5, hip_z=0.24, leg_r=0.08, leg_x=0.13,
              arm_r=0.08, hand=0.17, foot=(0.19, 0.28, 0.11), fur_len=0.14, fur_w=0.045,
              elbow=(0.1, -0.03, -0.22), wrist=(0.14, -0.1, -0.44), head_fwd=0.03, head_drop=0.04, hump=False,
              body_fur=230, head_fur=260),
    # the big one: hulking shoulders, head hunched forward, arms that nearly drag on the floor
    "L": dict(head_r=0.33, body=(1.0, 0.78, 0.98), body_z=0.86, hip_z=0.38, leg_r=0.13, leg_x=0.22,
              arm_r=0.13, hand=0.27, foot=(0.27, 0.38, 0.15), fur_len=0.2, fur_w=0.065,
              elbow=(0.2, -0.07, -0.36), wrist=(0.27, -0.2, -0.72), head_fwd=0.2, head_drop=0.2, hump=True,
              body_fur=300, head_fur=260),
}


def build(size="S", tint=(0.88, 0.92, 0.98), x=0.0, name=None):
    p = SIZES[size]
    name = name or f"Yeti_{size}"
    fur = mat("Tint", tint, rough=0.85)
    furshade = mat("TintShade", tuple(c * 0.9 for c in tint), rough=0.9)  # slightly darker under-fur for depth
    face = mat("Skin", FACE, rough=0.6)
    dark = mat("Dark", (0.05, 0.05, 0.07), rough=0.3)
    bone = mat("Bone", BONE, rough=0.45)
    eye_m = mat("Pupil", (0.02, 0.02, 0.03), rough=0.1)

    root = empty(name, (x, 0, 0), size=0.3)

    # ---------------------------------------------------------------- body: a pear-shaped furball, hunched a little forward
    bw, bd, bh = p["body"]
    bz = p["body_z"]
    bc = Vector((x, 0.02, bz))
    body_parts = [sphere("Core", bc, (bw, bd, bh), furshade, segs=32, rings=16)]
    body_parts.append(fur_clumps("BodyFur", bc, (bw * 0.98, bd * 0.98, bh * 0.98), fur, p["body_fur"], p["fur_len"], p["fur_w"], seed=3))
    if p["hump"]:  # massive shoulders / back
        hump_c = bc + Vector((0, 0.04, bh * 0.28))
        hump_s = (bw * 1.18, bd * 0.9, bh * 0.55)
        body_parts.append(sphere("Shoulders", hump_c, hump_s, furshade))
        body_parts.append(fur_clumps("ShoulderFur", hump_c, tuple(v * 0.98 for v in hump_s), fur, 120, p["fur_len"], p["fur_w"],
                                     keep=lambda n: n.z > -0.3, seed=5))
    body = join("Body", body_parts)
    set_parent(body, root)

    # ---------------------------------------------------------------- head: big, shaggy, sits low on the body (no neck)
    hr = p["head_r"]
    hz = bz + bh * 0.42 + hr * 0.62 - p["head_drop"]
    hc = Vector((x, -0.02 - p["head_fwd"], hz))
    # one smooth head: the bare face is a gently recessed patch of the same surface, with a muzzle swelling
    # out of it and a soft brow above, so it reads as a face from every angle (not a disc stuck on a ball)
    head_radii = (hr * 1.03, hr * 0.95, hr * 0.93)

    def face_mask(d):
        if d.y > -0.15:
            return 0.0
        e = (d.x / 0.7) ** 2 + ((d.z + 0.1) / 0.64) ** 2
        return max(0.0, min(1.0, (1.0 - e) / 0.25))

    def muzzle(d):
        return math.exp(-((d.x / 0.32) ** 2 + ((d.z + 0.3) / 0.22) ** 2)) if d.y < 0 else 0.0

    def head_r(d):
        m = face_mask(d)
        brow = math.exp(-((d.x / 0.55) ** 2 + ((d.z - 0.3) / 0.1) ** 2)) if d.y < 0 else 0.0
        return 1.0 - 0.04 * m + 0.34 * muzzle(d) + 0.035 * brow

    head_parts = [sculpted_sphere("Skull", hc, head_radii, [furshade], head_r, lambda d: 0, segs=48, rings=24),
                  # the bare face: a clean oval lying on the same surface (fur tufts overlap its edge)
                  surface_patch("Face", hc, head_radii, head_r, 0.66, 0.6, -0.1, face)]
    nose_dir = Vector((0, -1.0, -0.3))
    nose_p = surface_point(hc, head_radii, head_r, nose_dir, -hr * 0.02)
    head_parts.append(sphere("Nose", nose_p, (hr * 0.3, hr * 0.22, hr * 0.2), dark))
    muzzle_c = nose_p + Vector((0, hr * 0.06, -hr * 0.12))
    # two little fangs poking up from an underbite (the monster bit)
    for s in (-1, 1):
        f0 = surface_point(hc, head_radii, head_r, Vector((s * 0.15, -1, -0.5)), -hr * 0.01)
        head_parts.append(tube("Fang", [f0, f0 + Vector((s * hr * 0.015, -hr * 0.04, hr * 0.19))], [hr * 0.06, hr * 0.012], bone,
                               segs=6, per_span=2, cap_rings=(0.7,)))
    # floppy ears hanging out from under the fur, puppy style
    for s in (-1, 1):
        e0 = hc + Vector((s * hr * 0.9, 0.0, hr * 0.25))
        ear = tube("Ear", [e0, e0 + Vector((s * hr * 0.32, -hr * 0.02, -hr * 0.12)), e0 + Vector((s * hr * 0.42, -hr * 0.04, -hr * 0.42))],
                   [(hr * 0.16, hr * 0.07), (hr * 0.17, hr * 0.06), (hr * 0.09, hr * 0.04)], furshade, segs=12)
        head_parts.append(ear)
    # shaggy fur everywhere except the face, plus a fringe hanging over the brow
    head_parts.append(fur_clumps("HeadFur", hc, (hr * 2.04, hr * 1.88, hr * 1.84), fur, p["head_fur"], p["fur_len"] * 0.95, p["fur_w"] * 0.95,
                                 keep=lambda n: face_mask(n) < 0.12 and muzzle(n) < 0.1, seed=7))  # tufts just reach the face edge
    # extra short tufts on the crown so no bald spot shows from above or behind
    head_parts.append(fur_clumps("CrownFur", hc + Vector((0, 0.01, 0)), (hr * 2.0, hr * 1.85, hr * 1.82), fur, 70, p["fur_len"] * 0.7, p["fur_w"],
                                 keep=lambda n: n.z > 0.35 and face_mask(n) < 0.3, droop=0.95, seed=13))
    for i in range(7):
        a = -36 + i * 12
        f0 = face_point(hc, hr * 0.98, a, 46, 0)
        L = hr * (0.26 + 0.06 * ((i * 37) % 3))
        head_parts.append(tube("Fringe", [f0 - Vector((0, -0.01, 0)), f0 + Vector((0, -hr * 0.14, -L * 0.45)), f0 + Vector((math.sin(math.radians(a)) * 0.01, -hr * 0.18, -L))],
                               [p["fur_w"], p["fur_w"] * 0.7, p["fur_w"] * 0.08], fur, segs=5, per_span=2, cap_start=False, cap_rings=()))
    head = join("Head", head_parts)
    set_origin(head, hc)
    set_parent(head, body)

    # deadpan dot eyes, wide-set on the face patch
    for s, side in ((-1, "L"), (1, "R")):
        d = Vector((s * 0.34, -1.0, 0.04)).normalized()
        ep = surface_point(hc, head_radii, head_r, d, hr * 0.0)
        yaw = math.degrees(math.atan2(d.x, -d.y))
        eye = sphere(f"Eye_{side}", ep, (hr * 0.21, hr * 0.12, hr * 0.26), eye_m, rot=face_rot(yaw, 8))
        set_parent(eye, head)

    # ---------------------------------------------------------------- arms: long and furry, ending in big paws
    for s, side in ((-1, "L"), (1, "R")):
        sh = Vector((x + s * bw * (0.4 if not p["hump"] else 0.5), 0.0, bz + bh * (0.22 if not p["hump"] else 0.3)))
        elbow = sh + Vector((s * p["elbow"][0], p["elbow"][1], p["elbow"][2]))
        wrist = sh + Vector((s * p["wrist"][0], p["wrist"][1], p["wrist"][2]))
        k_f = p["fur_len"] / 0.11  # tuft scale
        arm_core = tube("ArmCore", [sh, elbow, wrist], [p["arm_r"], p["arm_r"] * 0.9, p["arm_r"] * 0.85], furshade, segs=14)
        arm_fur = []
        n_tufts = 14 if size == "S" else 20
        for k in range(n_tufts):
            t = k / (n_tufts - 1)
            c = sh.lerp(elbow, t * 2) if t < 0.5 else elbow.lerp(wrist, (t - 0.5) * 2)
            ang = k * 2.4
            n = Vector((math.cos(ang) * s, math.sin(ang) * 0.6 - 0.2, 0.25)).normalized()
            b = c + n * p["arm_r"] * 0.8
            arm_fur.append(tube("ArmTuft", [b - n * 0.01, b + (n * 0.03 + Vector((0, 0, -0.04))) * k_f, b + (n * 0.035 + Vector((0, 0, -0.085))) * k_f],
                                [p["fur_w"], p["fur_w"] * 0.7, p["fur_w"] * 0.08], fur, segs=5, per_span=2, cap_start=False, cap_rings=()))
        arm = join(f"Arm_{side}", [arm_core] + arm_fur)
        set_origin(arm, sh)
        set_parent(arm, root)
        hs = p["hand"]
        hpos = wrist + Vector((s * 0.01, -0.01, -hs * 0.35))
        paw = [sphere("Paw", hpos, (hs, hs * 0.85, hs * 0.9), face, segs=20, rings=10)]
        for k in range(3):  # three stubby claws
            cx = hpos.x + (k - 1) * hs * 0.26
            c0 = Vector((cx, hpos.y - hs * 0.32, hpos.z - hs * 0.25))
            paw.append(tube("Claw", [c0, c0 + Vector((0, -hs * 0.12, -hs * 0.1))], [hs * 0.07, hs * 0.015], bone, segs=6, per_span=2, cap_rings=(0.7,)))
        paw.append(sphere("Wristfluff", wrist + Vector((0, 0, -0.01)), (hs * 0.95, hs * 0.9, hs * 0.5), fur))
        hand = join(f"Hand_{side}", paw)
        set_origin(hand, hpos)
        set_parent(hand, root)

    # ---------------------------------------------------------------- stubby legs and big flat feet
    fx, fy, fz = p["foot"]
    for s, side in ((-1, "L"), (1, "R")):
        hip = Vector((x + s * p["leg_x"], 0.02, p["hip_z"] + 0.04))
        ankle = Vector((hip.x, 0.0, fz * 0.7))
        leg_core = tube("LegCore", [hip, hip.lerp(ankle, 0.5), ankle], [p["leg_r"] * 1.2, p["leg_r"] * 1.1, p["leg_r"]], furshade, segs=14)
        leg_fur = []
        k_f = p["fur_len"] / 0.11
        n_tufts = max(4, int((hip.z - ankle.z) / (0.025 * k_f)))
        for k in range(n_tufts):  # shaggy trousers
            t = k / max(1, n_tufts - 1)
            c = hip.lerp(ankle, t * 0.85)
            ang = k * 2.4
            n = Vector((math.cos(ang), math.sin(ang), 0.2)).normalized()
            b0 = c + n * p["leg_r"]
            leg_fur.append(tube("LegTuft", [b0 - n * 0.01, b0 + (n * 0.025 + Vector((0, 0, -0.04))) * k_f, b0 + (n * 0.03 + Vector((0, 0, -0.085))) * k_f],
                                [p["fur_w"], p["fur_w"] * 0.7, p["fur_w"] * 0.08], fur, segs=5, per_span=2, cap_start=False, cap_rings=()))
        leg = join(f"Leg_{side}", [leg_core] + leg_fur)
        set_origin(leg, hip)
        set_parent(leg, root)
        foot = [sphere("Sole", (hip.x, -fy * 0.15, fz * 0.45), (fx, fy, fz), face)]
        for k in range(3):
            tx = hip.x + (k - 1) * fx * 0.3
            t0 = Vector((tx, -fy * 0.15 - fy * 0.44, fz * 0.35))
            foot.append(sphere("Toe", t0, (fx * 0.3, fx * 0.3, fz * 0.6), face, segs=14, rings=8))
            foot.append(tube("Claw", [t0 + Vector((0, -fx * 0.1, 0)), t0 + Vector((0, -fx * 0.22, -fz * 0.1))], [fx * 0.06, fx * 0.012], bone, segs=6, per_span=2, cap_rings=(0.7,)))
        foot.append(fur_clumps("AnkleFur", (hip.x, -0.01, fz * 0.95), (fx * 0.9, fy * 0.6, fz * 0.6), fur, 16, p["fur_len"] * 0.6, p["fur_w"] * 0.8,
                               keep=lambda n: n.z > -0.2, seed=11 + (s > 0)))
        shoe = join(f"Shoe_{side}", foot)
        set_origin(shoe, (hip.x, 0, 0))
        set_parent(shoe, leg)

    empty("Anchor_Hat", (x, -0.02, hz + hr * 0.9), head)
    # torso cosmetics wrap the outside of the fur, so the anchor's size includes it
    fur_pad = p["fur_len"] * 0.6
    empty("Anchor_Torso", tuple(bc), body, scale=(bw / 2 + fur_pad, bd / 2 + fur_pad, bh / 2 + fur_pad * 0.5))
    return root
