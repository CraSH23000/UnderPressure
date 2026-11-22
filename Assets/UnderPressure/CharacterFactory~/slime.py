"""The slime: a translucent gumdrop of goo (material 'Slime', tinted with the player color and see-through in game),
two deadpan dot eyes, little goo blobs for hands and a few bubbles suspended inside. No arms or legs: cosmetics
other than the hat float inside the body (the game handles that), and it leaves a trail of its color."""
from cf import *

SIZES = {  # a squat half-dome: a little taller than half its width
    "S": dict(w=0.8, h=0.52, eye=0.07, hand=0.12),
    "M": dict(w=1.02, h=0.66, eye=0.085, hand=0.15),
    "L": dict(w=1.42, h=0.9, eye=0.1, hand=0.2),
}


def build(size="M", tint=(0.35, 0.9, 0.45), x=0.0, name=None):
    p = SIZES[size]
    name = name or f"Slime_{size}"
    goo = mat("Slime", tint, rough=0.08, alpha=0.6)
    eye_m = mat("Pupil", (0.02, 0.02, 0.03), rough=0.05)
    bubble = mat("Bubble", (0.95, 1.0, 0.97), rough=0.05)
    w, h = p["w"], p["h"]
    root = empty(name, (x, 0, 0), size=0.3)

    # gumdrop body: a flared gooey base spreading on the floor, rising to a soft rounded top
    # a half-dome (round top, not a point) whose base spreads out a touch where it sags on the floor
    shape = [(0.0, 0.5, 0.48), (0.025, 0.535, 0.51)]
    for f in (0.08, 0.2, 0.35, 0.5, 0.62, 0.73, 0.82, 0.89, 0.94, 0.975, 0.993, 1.0):
        r = 0.5 * math.sqrt(max(0.0, 1 - f ** 2.2)) * (1.0 + 0.04 * (1 - f))
        shape.append((f, r, r * 0.95))
    prof = [(f * h, rx * w, ry * w) for (f, rx, ry) in shape]
    parts = [loft("Goo", (x, 0, 0.0), prof, goo, segs=48, per_span=3)]
    # a few bubbles suspended in the goo (opaque, so they read through the translucency)
    import random
    rnd = random.Random(5)
    for i in range(6):
        bz = h * rnd.uniform(0.12, 0.55)
        rx, ry, _ = profile_at(prof, bz)
        a = rnd.uniform(0, 2 * math.pi)
        rr = rnd.uniform(0.2, 0.6)
        r = w * rnd.uniform(0.02, 0.045)
        parts.append(sphere("Bubble", (x + math.cos(a) * rx * rr, math.sin(a) * ry * rr, bz), (r * 2,) * 3, bubble, segs=10, rings=6))
    body = join("Body", parts)
    set_parent(body, root)

    # the "head" is just a pivot near the top (so the game can turn/shake the face) carrying the eyes and hat point
    hz = h * 0.62
    head = empty("Head", (x, 0, hz), body)
    for s, side in ((-1, "L"), (1, "R")):
        ez = h * 0.6
        rx, ry, _ = profile_at(prof, ez)
        ex = s * rx * 0.36
        ey = -ry * math.sqrt(max(0.0, 1 - (ex / rx) ** 2)) - 0.002
        yaw = math.degrees(math.atan2(ex / rx, math.sqrt(max(0.0, 1 - (ex / rx) ** 2))))
        e = p["eye"]
        eye = sphere(f"Eye_{side}", (x + ex, ey, ez), (e * 1.6, e * 0.8, e * 2.0), eye_m, rot=face_rot(yaw, 0), segs=16, rings=8)
        set_parent(eye, head)
    empty("Anchor_Hat", (x, 0, h * 0.96), head)

    # little goo blobs for hands, floating at its sides
    for s, side in ((-1, "L"), (1, "R")):
        hs = p["hand"]
        rx, ry, _ = profile_at(prof, h * 0.3)
        hp = (x + s * (rx + hs * 0.35), -0.02, h * 0.3)
        hand = sphere(f"Hand_{side}", hp, (hs, hs * 0.9, hs * 0.95), goo, segs=20, rings=10)
        set_parent(hand, root)
    empty("Anchor_Torso", (x, 0, h * 0.4), body, scale=(w * 0.3, w * 0.3, h * 0.3))
    return root
