"""The Chihuahua: a Chihuahua's head on the human test-subject body (jumpsuit takes the player color).
Apple-dome skull, short muzzle with a cream blaze, huge upright ears and big glossy bulging eyes - still deadpan."""
from cf import *
import human

FAWN = (0.82, 0.56, 0.32)
CREAM = (0.97, 0.88, 0.72)
EAR_PINK = (0.95, 0.66, 0.6)

SIZES = human.SIZES


def head(size, hc, hr, z1, body):
    fur = mat("Fur", FAWN, rough=0.6)
    light = mat("FurLight", CREAM, rough=0.6)
    pink = mat("EarInner", EAR_PINK, rough=0.55)
    dark = mat("Dark", (0.05, 0.04, 0.05), rough=0.3)
    eye_m = mat("Pupil", (0.02, 0.02, 0.03), rough=0.05)
    x = hc.x
    hr = hr * 1.06  # the dome is a touch bigger than a human head
    # sits low on a short, slim neck (the large one's long torso top needs it lower still)
    hc = hc + Vector((0, 0.02, -hr * (0.18 if size == "L" else 0.08)))
    radii = (hr * 1.02, hr * 0.96, hr * 0.94)

    def gauss(d, cx, cz, wx, wz):
        return math.exp(-(((d.x - cx) / wx) ** 2 + ((d.z - cz) / wz) ** 2)) if d.y < 0 else 0.0

    def skull_r(d):
        r = 1.0
        r += 0.55 * gauss(d, 0.0, -0.34, 0.27, 0.19)     # short, pointy muzzle
        r -= 0.05 * gauss(d, 0.0, -0.08, 0.25, 0.12)     # the "stop" between muzzle and that apple-dome forehead
        r += 0.05 * gauss(d, 0.0, 0.45, 0.6, 0.25)       # domed forehead
        for s in (-1, 1):
            r += 0.05 * gauss(d, s * 0.45, 0.05, 0.2, 0.2)  # eyes bulge the sockets a little
        if d.z < -0.45:                                   # narrow chin
            r *= 1.0 - 0.18 * min(1.0, (-d.z - 0.45) / 0.4)
        return r

    parts = [sculpted_sphere("Skull", hc, radii, [fur], skull_r, lambda d: 0, segs=56, rings=28),
             cylinder("Neck", (x, 0.0, (z1 - hr * 0.15 + hc.z - hr * 0.6) / 2), hr * 0.29, (hc.z - hr * 0.6) - (z1 - hr * 0.15), fur)]
    # cream muzzle and a blaze up the forehead (clean curved edges, lying on the head)
    parts.append(surface_patch("Muzzle", hc, radii, skull_r, 0.36, 0.3, -0.4, light, out=0.004, rings=14, segs=40))
    parts.append(surface_patch("Blaze", hc, radii, skull_r, 0.09, 0.32, 0.05, light, out=0.002, rings=8, segs=32,
                               shape=lambda th: 1.0 - 0.35 * max(0.0, math.sin(th))))  # narrows toward the top
    nose_p = surface_point(hc, radii, skull_r, Vector((0, -1.0, -0.33)).normalized(), hr * 0.03)
    parts.append(sphere("Nose", nose_p, (hr * 0.2, hr * 0.14, hr * 0.13), dark, segs=16, rings=8))
    # big upright ears, flared out, cupped forward with a pink inside
    for s in (-1, 1):
        base = surface_point(hc, radii, skull_r, Vector((s * 0.6, 0.12, 0.72)).normalized(), -hr * 0.12)
        out = Vector((s * math.sin(math.radians(38)), 0.12, math.cos(math.radians(38)))).normalized()
        L = hr * 1.05
        pts = [base, base + out * L * 0.4 + Vector((0, -hr * 0.03, 0)), base + out * L * 0.78, base + out * L]
        parts.append(tube("Ear", pts, [(hr * 0.06, hr * 0.33), (hr * 0.07, hr * 0.32), (hr * 0.05, hr * 0.16), (hr * 0.03, hr * 0.03)],
                          fur, segs=14, per_span=4))
        inner = [p + Vector((0, -hr * 0.05, 0)) for p in pts]
        inner[0] = inner[0] + out * hr * 0.1
        parts.append(tube("EarInner", inner[:3] + [inner[3] - out * hr * 0.12],
                          [(hr * 0.03, hr * 0.22), (hr * 0.035, hr * 0.22), (hr * 0.03, hr * 0.1), (hr * 0.02, hr * 0.02)],
                          pink, segs=12, per_span=4))
    head_ob = join("Head", parts)
    set_origin(head_ob, hc)
    set_parent(head_ob, body)
    # big, glossy, slightly bulging eyes, set wide (a Chihuahua's best feature) - still just dark and deadpan
    for s, side in ((-1, "L"), (1, "R")):
        d = Vector((s * 0.46, -1.0, 0.06)).normalized()
        ep = surface_point(hc, radii, skull_r, d, -hr * 0.04)
        yaw = math.degrees(math.atan2(d.x, -d.y))
        eye = sphere(f"Eye_{side}", ep, (hr * 0.36, hr * 0.26, hr * 0.38), eye_m, rot=face_rot(yaw, 4), segs=24, rings=12)
        set_parent(eye, head_ob)
    empty("Anchor_Hat", (x, hc.y, hc.z + radii[2] * 1.02), head_ob)
    return head_ob


def build(size="M", tint=(0.12, 0.5, 0.95), x=0.0, name=None):
    paw = mat("Fur", FAWN, rough=0.6)
    return human.build(size, tint=tint, x=x, name=name or f"Chihuahua_{size}", head_builder=head, hand_mat=paw)
