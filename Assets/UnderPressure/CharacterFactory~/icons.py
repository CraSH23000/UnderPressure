"""UI icons for Under Pressure: blank input glyphs (the game writes the key name on top) and item icons.

    python icons.py <out_dir>

Renders transparent PNGs with Cycles: key, key_wide, trigger, bumper, face, pill, item_bottle.
"""
import math
import os
import sys

import bpy  # first: makes bmesh importable
import bmesh
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import cf  # noqa: E402

OUT = sys.argv[-1] if len(sys.argv) > 1 and not sys.argv[-1].endswith(".py") else "out/icons"


# ------------------------------------------------------------------ studio

def studio(res, samples=48):
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = samples
    sc.cycles.use_denoising = True
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.film_transparent = True
    sc.render.image_settings.file_format = "PNG"
    sc.render.image_settings.color_mode = "RGBA"
    sc.view_settings.view_transform = "Standard"
    world = bpy.data.worlds.new("World")
    sc.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes["Background"]
    bg.inputs["Color"].default_value = (0.85, 0.9, 0.95, 1)
    bg.inputs["Strength"].default_value = 0.25

    def area(name, loc, energy, size):
        ld = bpy.data.lights.new(name, "AREA")
        ld.energy, ld.size = energy, size
        ob = bpy.data.objects.new(name, ld)
        sc.collection.objects.link(ob)
        ob.location = loc
        ob.rotation_euler = (Vector((0, 0, 0)) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()

    area("Key", (-2.5, -3.5, 5), 170, 3)
    area("Fill", (3, -3, 1.5), 45, 3)
    area("Rim", (0, 4, 3), 140, 3)


def camera(pitch_deg, ortho_w, target=(0, 0, 0)):
    sc = bpy.context.scene
    cd = bpy.data.cameras.new("Cam")
    cd.type = "ORTHO"
    cd.ortho_scale = ortho_w
    cam = bpy.data.objects.new("Cam", cd)
    sc.collection.objects.link(cam)
    sc.camera = cam
    p = math.radians(pitch_deg)
    t = Vector(target)
    cam.location = t + Vector((0, -math.cos(p), math.sin(p))) * 10
    cam.rotation_euler = (t - cam.location).to_track_quat("-Z", "Y").to_euler()


def render(name):
    os.makedirs(OUT, exist_ok=True)
    bpy.context.scene.render.filepath = os.path.join(os.path.abspath(OUT), name + ".png")
    bpy.ops.render.render(write_still=True)
    print("wrote", name)


# ------------------------------------------------------------------ shapes

def rounded_outline(w, h, r_top, r_bot, steps=10):
    """Points of a rounded rectangle in the XZ plane, centred, counter-clockwise."""
    pts = []
    def arc(cx, cz, r, a0, a1):
        for i in range(steps + 1):
            a = math.radians(a0 + (a1 - a0) * i / steps)
            pts.append((cx + math.cos(a) * r, cz + math.sin(a) * r))
    arc(w / 2 - r_bot, -h / 2 + r_bot, r_bot, -90, 0)
    arc(w / 2 - r_top, h / 2 - r_top, r_top, 0, 90)
    arc(-w / 2 + r_top, h / 2 - r_top, r_top, 90, 180)
    arc(-w / 2 + r_bot, -h / 2 + r_bot, r_bot, 180, 270)
    return pts


def slab(name, outline, depth, material, bevel, taper=1.0, segs=5):
    """Extrude a 2D outline (XZ plane) along Y, optionally shrinking the front face (taper), then bevel it."""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    back = [bm.verts.new((x, depth / 2, z)) for x, z in outline]
    front = [bm.verts.new((x * taper, -depth / 2, z * taper)) for x, z in outline]
    n = len(outline)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((back[i], back[j], front[j], front[i]))
    bm.faces.new(front)
    bm.faces.new(list(reversed(back)))
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1e-5)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    ob = cf._new_obj(name, me, material)
    m = ob.modifiers.new("Bevel", "BEVEL")
    m.width = bevel
    m.segments = segs
    m.limit_method = "ANGLE"
    m.angle_limit = math.radians(40)
    for p in ob.data.polygons:
        p.use_smooth = True
    ob.data.shade_auto_smooth() if hasattr(ob.data, "shade_auto_smooth") else None
    return ob


def lay_flat(ob):
    """Slabs are built facing -Y; keys sit with their top facing up (+Z)."""
    ob.rotation_euler = (math.radians(-90), 0, 0)


KEY = (0.8, 0.83, 0.86)
PAD = (0.05, 0.055, 0.065)


def key(name, w):
    cf.reset_scene()
    studio((int(256 * w / 1.0 * 0.88) if w > 1 else 256, 256))
    m = cf.mat("Keycap", KEY, rough=0.45)
    ob = slab("Key", rounded_outline(w, 1.0, 0.2, 0.2), 0.55, m, 0.07, taper=0.84)
    lay_flat(ob)
    ob.location.z = -0.275
    camera(58, (w + 0.24) * 1.0 if w > 1 else 1.24, (0, 0.05, -0.3))
    render(name)


def pad_shape(name, outline, w, h, res):
    cf.reset_scene()
    studio(res)
    m = cf.mat("Pad", PAD, rough=0.32)
    ob = slab("Pad", outline, 0.35, m, 0.09, taper=0.9)
    camera(14, max(w, h * res[0] / res[1]) * 1.18)
    render(name)


def face_button():
    cf.reset_scene()
    studio((256, 256))
    m = cf.mat("Pad", PAD, rough=0.3)
    ob = slab("Face", rounded_outline(1.0, 1.0, 0.5, 0.5, steps=16), 0.35, m, 0.1, taper=0.9)
    camera(14, 1.2)
    render("glyph_face")


def bottle():
    cf.reset_scene()
    studio((256, 256), samples=64)
    water = cf.mat("Water", (0.08, 0.35, 0.95), rough=0.15)
    plastic = cf.mat("Plastic", (0.6, 0.82, 0.95), rough=0.12)
    label = cf.mat("Label", (0.02, 0.15, 0.55), rough=0.4)
    white = cf.mat("Cap", (0.97, 0.97, 0.98), rough=0.35)
    r = 0.3
    # lower half full of water, upper half clear plastic, a soft shoulder up to the neck
    low = cf.loft("Water", (0, 0, 0), [(0.0, r * 0.82, r * 0.82), (0.03, r * 0.97, r * 0.97), (0.12, r, r), (0.3, r * 0.97, r * 0.97),
                                       (0.5, r, r), (0.62, r, r)], water, segs=48)
    up = cf.loft("Upper", (0, 0, 0), [(0.62, r, r), (0.72, r * 0.98, r * 0.98), (0.84, r * 0.85, r * 0.85), (0.94, r * 0.5, r * 0.5),
                                      (0.99, r * 0.4, r * 0.4), (1.02, r * 0.4, r * 0.4)], plastic, segs=48)
    band = cf.cylinder("Label", (0, 0, 0.38), r * 1.03, 0.22, label, segs=48)
    stripe = cf.cylinder("Stripe", (0, 0, 0.38), r * 1.045, 0.05, white, segs=48)
    cap = cf.cylinder("Cap", (0, 0, 1.08), r * 0.47, 0.12, white, segs=40)
    capm = cap.modifiers.new("Bevel", "BEVEL"); capm.width = 0.02; capm.segments = 3
    shine = cf.box("Shine", (-r * 0.55, -r * 0.82, 0.74), (0.05, 0.02, 0.18), cf.mat("Shine", (1, 1, 1), 0.1, emission=(1, 1, 1)), bevel=0.02)
    for ob in (low, up, band, stripe, cap, shine):
        ob.rotation_euler = (0, math.radians(-14), 0)
        ob.location = Vector(ob.location)
    # rotate the group about the bottle's middle
    piv = cf.empty("Piv", (0, 0, 0.55))
    for ob in (low, up, band, stripe, cap, shine):
        ob.rotation_euler = (0, 0, 0)
        cf.set_parent(ob, piv)
    piv.rotation_euler = (0, math.radians(-14), 0)
    camera(12, 1.38, (0, 0, 0.58))
    render("item_bottle")


def backpack():
    cf.reset_scene()
    studio((256, 256), samples=64)
    canvas = cf.mat("Canvas", (0.92, 0.36, 0.04), rough=0.6)
    dark = cf.mat("Dark", (0.32, 0.17, 0.07), rough=0.55)
    strap = cf.mat("Strap", (0.08, 0.08, 0.09), rough=0.5)
    buckle = cf.mat("Buckle", (0.85, 0.85, 0.88), rough=0.3, metal=1.0)
    teal = cf.mat("Fanny", (0.15, 0.7, 0.65), rough=0.5)
    parts = []
    # main body: a soft rounded block
    parts.append(cf.box("Body", (0, 0, 0.45), (0.62, 0.36, 0.78), canvas, bevel=0.12, segs=5))
    # flap over the top, a front pocket, side pockets
    parts.append(cf.box("Flap", (0, -0.03, 0.78), (0.64, 0.4, 0.22), dark, bevel=0.08, segs=4))
    parts.append(cf.box("Pocket", (0, -0.21, 0.3), (0.44, 0.1, 0.3), dark, bevel=0.05, segs=3))
    for s in (-1, 1):
        parts.append(cf.box("Side", (s * 0.33, 0, 0.35), (0.08, 0.26, 0.3), dark, bevel=0.03, segs=3))
        parts.append(cf.box("Strap", (s * 0.15, -0.26, 0.62), (0.05, 0.02, 0.26), strap, bevel=0.01, segs=2))
        parts.append(cf.box("Buckle", (s * 0.15, -0.275, 0.52), (0.07, 0.02, 0.05), buckle, bevel=0.008, segs=2))
    # a little teal fanny pack riding on top (the start of the stack)
    parts.append(cf.box("Fanny", (0, 0.02, 0.97), (0.42, 0.22, 0.14), teal, bevel=0.06, segs=4))
    parts.append(cf.box("Zip", (0, -0.095, 0.98), (0.3, 0.01, 0.015), strap, bevel=0.004, segs=1))
    piv = cf.empty("Piv", (0, 0, 0.55))
    for ob in parts:
        cf.set_parent(ob, piv)
    piv.rotation_euler = (0, math.radians(-8), math.radians(-24))
    camera(12, 1.12, (0, 0, 0.56))
    render("item_backpack")


def group(parts, rot, pivot=(0, 0, 0.5)):
    piv = cf.empty("Piv", pivot)
    for ob in parts:
        cf.set_parent(ob, piv)
    piv.rotation_euler = rot
    return piv


def ant():
    cf.reset_scene()
    studio((256, 256), samples=64)
    black = cf.mat("Chitin", (0.05, 0.045, 0.06), rough=0.25)
    glass = cf.mat("Lens", (0.85, 0.92, 1.0), rough=0.05)
    rim = cf.mat("Rim", (0.85, 0.7, 0.25), rough=0.3, metal=1.0)
    calc = cf.mat("Calc", (0.88, 0.86, 0.8), rough=0.5)
    screen = cf.mat("Screen", (0.55, 0.75, 0.45), rough=0.3, emission=(0.4, 0.7, 0.3))
    key = cf.mat("Keys", (0.2, 0.2, 0.22), rough=0.4)
    parts = []
    parts.append(cf.sphere("Abdomen", (0, 0.32, 0.36), (0.26, 0.34, 0.24), black))
    parts.append(cf.sphere("Thorax", (0, 0.0, 0.42), (0.14, 0.16, 0.14), black))
    parts.append(cf.sphere("Head", (0, -0.2, 0.6), (0.2, 0.18, 0.18), black))
    for sx in (-1, 1):
        parts.append(cf.torus("Glass", (sx * 0.085, -0.37, 0.64), 0.06, 0.012, rim, rot=(90, 0, 0)))
        parts.append(cf.cylinder("Lens", (sx * 0.085, -0.365, 0.64), 0.055, 0.01, glass, rot=(90, 0, 0)))
        parts.append(cf.tube("Antenna", [(sx * 0.06, -0.25, 0.75), (sx * 0.12, -0.3, 0.9), (sx * 0.2, -0.38, 0.96)], [0.012, 0.01, 0.008], black))
        for i, y in enumerate((-0.05, 0.03, 0.11)):
            parts.append(cf.tube("Leg", [(sx * 0.1, y, 0.4), (sx * 0.28, y - 0.02, 0.38), (sx * 0.34, y - 0.06, 0.18)], [0.02, 0.018, 0.015], black))
    # a little calculator held up front
    parts.append(cf.box("Calc", (0, -0.36, 0.38), (0.22, 0.04, 0.28), calc, bevel=0.02))
    parts.append(cf.box("Screen", (0, -0.385, 0.47), (0.16, 0.01, 0.06), screen, bevel=0.005))
    for r in range(3):
        for c in range(3):
            parts.append(cf.box("Key", (-0.06 + c * 0.06, -0.385, 0.4 - r * 0.05), (0.035, 0.01, 0.03), key, bevel=0.005))
    group(parts, (0, 0, math.radians(-28)))
    camera(14, 1.2, (0, 0, 0.5))
    render("item_ant")


def pegleg():
    cf.reset_scene()
    studio((256, 256), samples=64)
    wood = cf.mat("Wood", (0.5, 0.3, 0.13), rough=0.55)
    dark = cf.mat("DarkWood", (0.3, 0.17, 0.07), rough=0.5)
    gold = cf.mat("Gold", (1.0, 0.75, 0.25), rough=0.25, metal=1.0)
    clover = cf.mat("Clover", (0.25, 0.8, 0.3), rough=0.4)
    parts = []
    parts.append(cf.cylinder("Peg", (0, 0, 0.42), 0.045, 0.62, wood, r2=0.07))
    parts.append(cf.cylinder("Foot", (0, 0, 0.1), 0.06, 0.05, dark))
    parts.append(cf.cylinder("Socket", (0, 0, 0.8), 0.11, 0.2, dark, r2=0.17))
    parts.append(cf.torus("Band", (0, 0, 0.72), 0.12, 0.022, gold))
    parts.append(cf.torus("Band2", (0, 0, 0.3), 0.065, 0.016, gold))
    # a four-leaf clover stuck in the cuff
    for i in range(4):
        a = i * math.pi / 2
        parts.append(cf.sphere("Leaf", (math.cos(a) * 0.05 + 0.18, -0.12, 0.95 + math.sin(a) * 0.05), (0.05, 0.015, 0.05), clover))
    group(parts, (0, math.radians(18), 0), (0, 0, 0.5))
    camera(10, 1.15, (0.02, 0, 0.52))
    render("item_pegleg")


def crossing():
    cf.reset_scene()
    studio((256, 256), samples=64)
    red = cf.mat("Red", (0.9, 0.06, 0.06), rough=0.35)
    white = cf.mat("White", (0.97, 0.97, 0.97), rough=0.4)
    pole = cf.mat("Pole", (0.8, 0.8, 0.82), rough=0.25, metal=1.0)
    grip = cf.mat("Grip", (0.1, 0.1, 0.12), rough=0.6)
    parts = []
    # an octagon paddle: white border, red face, STOP in white
    parts.append(cf.cylinder("Rim", (0, 0, 0.66), 0.38, 0.03, white, segs=8, rot=(90, 22.5, 0), smooth=False))
    parts.append(cf.cylinder("Face", (0, -0.018, 0.66), 0.34, 0.012, red, segs=8, rot=(90, 22.5, 0), smooth=False))
    bpy.ops.object.text_add(location=(0, -0.035, 0.6))
    txt = bpy.context.object
    txt.data.body = "STOP"
    txt.data.align_x = "CENTER"
    txt.data.size = 0.19
    txt.data.extrude = 0.006
    txt.rotation_euler = (math.radians(90), 0, 0)
    txt.data.materials.append(white)
    bpy.ops.object.convert(target="MESH")
    parts.append(bpy.context.object)
    parts.append(cf.cylinder("Pole", (0, 0.02, 0.15), 0.025, 0.7, pole))
    parts.append(cf.cylinder("Grip", (0, 0.02, -0.08), 0.035, 0.22, grip))
    group(parts, (0, math.radians(-12), math.radians(-14)), (0, 0, 0.4))
    camera(8, 1.25, (0, 0, 0.42))
    render("item_crossing")


def funnel():
    cf.reset_scene()
    studio((256, 256), samples=64)
    tin = cf.mat("Tin", (0.85, 0.72, 0.3), rough=0.3, metal=0.9)
    rim = cf.mat("TinRim", (0.7, 0.58, 0.22), rough=0.3, metal=0.9)
    drop = cf.mat("Drop", (0.25, 0.6, 1.0), rough=0.1)
    parts = []
    parts.append(cf.cylinder("Cone", (0, 0, 0.62), 0.07, 0.36, tin, r2=0.34))
    parts.append(cf.cylinder("Spout", (0, 0, 0.3), 0.045, 0.3, tin, r2=0.07))
    parts.append(cf.torus("Lip", (0, 0, 0.8), 0.34, 0.025, rim))
    parts.append(cf.sphere("Drop", (0, 0, 0.08), (0.05, 0.05, 0.07), drop))
    group(parts, (math.radians(-22), 0, math.radians(-10)), (0, 0, 0.5))
    camera(12, 1.2, (0, 0, 0.48))
    render("item_funnel")


def filterpiece():
    cf.reset_scene()
    studio((256, 256), samples=64)
    pipe = cf.mat("Pipe", (0.86, 0.88, 0.92), rough=0.25, metal=0.6)
    flange = cf.mat("Flange", (0.55, 0.58, 0.62), rough=0.35, metal=0.8)
    glass = cf.mat("Glass", (0.75, 0.9, 1.0), rough=0.05, alpha=0.35)
    cart = cf.mat("Cartridge", (0.96, 0.96, 0.93), rough=0.5)
    pleat = cf.mat("Pleat", (0.25, 0.58, 0.95), rough=0.4)
    water = cf.mat("Clean", (0.45, 0.8, 1.0), rough=0.1, emission=(0.2, 0.5, 0.9))
    slime = cf.mat("Slime", (0.3, 1.0, 0.35), rough=0.1, emission=(0.2, 0.9, 0.25))
    parts = []
    r = 0.11
    # the T: a bar left-right and a stem toward the camera, meeting at the canister
    parts.append(cf.cylinder("BarL", (-0.32, 0, 0.3), r, 0.4, pipe, rot=(0, 90, 0)))
    parts.append(cf.cylinder("BarR", (0.32, 0, 0.3), r, 0.4, pipe, rot=(0, 90, 0)))
    parts.append(cf.cylinder("Stem", (0, -0.3, 0.3), r, 0.38, pipe, rot=(90, 0, 0)))
    for loc, rot in (((-0.53, 0, 0.3), (0, 90, 0)), ((0.53, 0, 0.3), (0, 90, 0)), ((0, -0.5, 0.3), (90, 0, 0))):
        parts.append(cf.cylinder("Flange", loc, r * 1.35, 0.06, flange, rot=rot))
    # little drips of what comes out: clean water one side, the concentrate the other
    parts.append(cf.sphere("Clean", (0.6, -0.02, 0.16), (0.07, 0.07, 0.1), water))
    parts.append(cf.sphere("Conc", (-0.6, -0.02, 0.16), (0.07, 0.07, 0.1), slime))
    parts.append(cf.cylinder("Glass", (0, 0, 0.45), 0.2, 0.62, glass))
    parts.append(cf.cylinder("Cartridge", (0, 0, 0.45), 0.12, 0.5, cart))
    for i in range(4):
        parts.append(cf.cylinder("Pleat", (0, 0, 0.26 + i * 0.12), 0.125, 0.025, pleat))
    parts.append(cf.cylinder("CapTop", (0, 0, 0.77), 0.22, 0.05, flange))
    parts.append(cf.cylinder("CapBottom", (0, 0, 0.13), 0.22, 0.05, flange))
    parts.append(cf.box("Handle", (0, 0, 0.83), (0.24, 0.06, 0.06), flange, bevel=0.015))
    group(parts, (0, 0, math.radians(-25)), (0, 0, 0.45))
    camera(22, 1.45, (0, 0, 0.42))
    render("item_filter")


def serum():
    """Spider Monkey Serum: a corked lab vial of glowing pink serum, a monkey's tail curled round it."""
    cf.reset_scene()
    studio((256, 256), samples=64)
    glass = cf.mat("Glass", (0.85, 0.92, 1.0), rough=0.05, alpha=0.3)
    pink = cf.mat("Serum", (0.95, 0.25, 0.75), rough=0.15, emission=(0.6, 0.1, 0.45))
    cork = cf.mat("Cork", (0.6, 0.4, 0.2), rough=0.7)
    fur = cf.mat("Fur", (0.45, 0.28, 0.14), rough=0.8)
    label = cf.mat("Label", (0.96, 0.95, 0.9), rough=0.5)
    parts = []
    parts.append(cf.cylinder("Vial", (0, 0, 0.45), 0.2, 0.72, glass))
    parts.append(cf.sphere("Bottom", (0, 0, 0.09), (0.2, 0.2, 0.1), glass))
    parts.append(cf.cylinder("Serum", (0, 0, 0.36), 0.175, 0.5, pink))
    parts.append(cf.sphere("SerumBottom", (0, 0, 0.11), (0.175, 0.175, 0.08), pink))
    parts.append(cf.cylinder("Label", (0, 0, 0.42), 0.205, 0.16, label))
    parts.append(cf.cylinder("Cork", (0, 0, 0.88), 0.16, 0.16, cork, r2=0.19))
    # the tail: spirals up round the vial and curls over at the top
    pts, radii = [], []
    for i in range(13):
        t = i / 12
        a = math.radians(-60 + t * 520)
        r = 0.27 - 0.03 * t
        pts.append((math.cos(a) * r, math.sin(a) * r, 0.12 + t * 0.7))
        radii.append(0.04 - 0.015 * t)
    pts += [(0.05, -0.2, 0.96), (-0.05, -0.15, 1.02), (-0.04, -0.08, 0.95)]
    radii += [0.022, 0.02, 0.018]
    parts.append(cf.tube("Tail", pts, radii, fur))
    group(parts, (0, math.radians(-12), math.radians(-18)), (0, 0, 0.5))
    camera(12, 1.3, (0, 0, 0.52))
    render("item_serum")


def spoon():
    """The Spoon: a plain silver spoon, tilted. There is no pipe."""
    cf.reset_scene()
    studio((256, 256), samples=64)
    steel = cf.mat("Steel", (0.9, 0.92, 0.96), rough=0.28, metal=0.75)
    parts = []
    bowl = cf.sphere("Bowl", (0, 0, 0.74), (0.21, 0.06, 0.27), steel, segs=40, rings=20)
    parts.append(bowl)
    parts.append(cf.tube("Neck", [(0, 0, 0.6), (0, 0.01, 0.42), (0, 0.02, 0.2), (0, 0.0, 0.0)], [0.04, 0.035, 0.045, 0.06], steel, cap=True))
    group(parts, (0, math.radians(-35), 0), (0, 0, 0.5))
    camera(10, 1.05, (0.0, 0, 0.5))
    render("item_spoon")


def badge_new():
    """A NEW! sticker: a gold starburst with a red rim and the word on it (flat, facing the camera)."""
    cf.reset_scene()
    studio((256, 256), samples=48)
    gold = cf.mat("Gold", (1.0, 0.68, 0.02), rough=0.3, emission=(0.35, 0.22, 0.0))
    red = cf.mat("Red", (0.9, 0.1, 0.12), rough=0.4)
    white = cf.mat("White", (1, 1, 1), rough=0.3, emission=(0.6, 0.6, 0.6))
    def star(name, rad_out, rad_in, y, mat, depth):
        pts = []
        n = 14
        for i in range(n * 2):
            a = math.pi / 2 + i * math.pi / n
            r = rad_out if i % 2 == 0 else rad_in
            pts.append((math.cos(a) * r, math.sin(a) * r))
        ob = slab(name, pts, depth, mat, 0.02, segs=2)
        ob.location.y = y
        return ob
    star("Rim", 0.62, 0.5, 0.02, red, 0.06)
    star("Star", 0.55, 0.44, -0.02, gold, 0.06)
    bpy.ops.object.text_add(location=(0, -0.07, -0.1))
    txt = bpy.context.object
    txt.data.body = "NEW!"
    txt.data.align_x = "CENTER"
    txt.data.size = 0.34
    txt.data.extrude = 0.015
    txt.data.bevel_depth = 0.014  # chunkier letters
    txt.rotation_euler = (math.radians(90), 0, math.radians(0))
    txt.data.materials.append(red)
    bpy.ops.object.convert(target="MESH")
    camera(0, 1.36, (0, 0, 0))
    render("badge_new")


if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[-1] == "badge":
        OUT = "out/icons"
        badge_new()
        sys.exit(0)
    if len(sys.argv) > 1 and sys.argv[-1] == "serumspoon":
        OUT = "out/icons"
        serum(); spoon()
        sys.exit(0)
    if len(sys.argv) > 1 and sys.argv[-1] == "filter":
        OUT = "out/icons"
        filterpiece()
        sys.exit(0)
    if len(sys.argv) > 1 and sys.argv[-1] in ("backpack", "new", "crossing"):
        which = sys.argv[-1]
        OUT = "out/icons"
        if which == "backpack":
            backpack()
        elif which == "new":
            ant(); pegleg(); crossing(); funnel()
        if which == "crossing":
            crossing()
        sys.exit(0)
    key("glyph_key", 1.0)
    key("glyph_key_wide", 1.9)
    pad_shape("glyph_trigger", rounded_outline(0.78, 1.0, 0.36, 0.14), 0.78, 1.0, (256, 256))
    pad_shape("glyph_bumper", rounded_outline(1.7, 0.62, 0.3, 0.12), 1.7, 0.62, (384, 192))
    pad_shape("glyph_pill", rounded_outline(1.5, 0.62, 0.31, 0.31), 1.5, 0.62, (384, 192))
    face_button()
    bottle()
    backpack()
    ant(); pegleg(); crossing(); funnel(); filterpiece(); serum(); spoon()
