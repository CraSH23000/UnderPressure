"""
Character Factory: small helpers for building cartoon characters from primitives in Blender (bpy).

Conventions (Blender space): Z is up, the character faces -Y (Blender's "front" view), 1 unit = 1 metre.
Every character is a tree of named parts so Unity can keep animating them procedurally (squash, blink,
look-at, floating hands, walking legs). Material names are the contract with the game:
  Tint     - tinted with the player's chosen color (Spray Booth)
  Skin, Hair, EyeWhite, Pupil, Shine, Dark, Shoe, Sole, Accent ... - fixed colors
"""
import math
import bpy  # must come first: it makes bmesh/mathutils importable
import bmesh
from mathutils import Vector, Euler, Matrix

_mats = {}


def reset_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    _mats.clear()


def mat(name, color, rough=0.5, metal=0.0, emission=None, alpha=1.0, sss=0.0):
    """Principled material, cached by name (the name is what the game reads)."""
    if name in _mats:
        return _mats[name]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    b = m.node_tree.nodes.get("Principled BSDF")
    b.inputs["Base Color"].default_value = (*color, 1.0)
    b.inputs["Roughness"].default_value = rough
    b.inputs["Metallic"].default_value = metal
    if sss > 0:
        b.inputs["Subsurface Weight"].default_value = sss
        b.inputs["Subsurface Radius"].default_value = (0.3, 0.12, 0.08)
    if emission is not None:
        b.inputs["Emission Color"].default_value = (*emission, 1.0)
        b.inputs["Emission Strength"].default_value = 1.0
    if alpha < 1.0:
        b.inputs["Alpha"].default_value = alpha
        b.inputs["Transmission Weight"].default_value = 0.6
        m.surface_render_method = "BLENDED"
    m.diffuse_color = (*color, alpha)
    _mats[name] = m
    return m


def _new_obj(name, me, material):
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    if material is not None:
        ob.data.materials.append(material)
    return ob


def _smooth(ob):
    for p in ob.data.polygons:
        p.use_smooth = True


def sphere(name, loc, size, material, segs=24, rings=12, rot=(0, 0, 0), smooth=True):
    """Ellipsoid with full diameters 'size' (x, y, z)."""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=segs, v_segments=rings, radius=0.5)
    bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
    bm.to_mesh(me)
    bm.free()
    ob = _new_obj(name, me, material)
    ob.location = loc
    ob.rotation_euler = Euler([math.radians(a) for a in rot])
    if smooth:
        _smooth(ob)
    return ob


def cylinder(name, loc, radius, depth, material, segs=32, rot=(0, 0, 0), smooth=True, r2=None):
    """Cylinder (or cone/frustum if r2 is given) along local Z, centred on loc."""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segs,
                          radius1=radius, radius2=radius if r2 is None else r2, depth=depth)
    bm.to_mesh(me)
    bm.free()
    ob = _new_obj(name, me, material)
    ob.location = loc
    ob.rotation_euler = Euler([math.radians(a) for a in rot])
    if smooth:
        _smooth(ob)
        bevel_edges_sharp(ob)
    return ob


def box(name, loc, size, material, bevel=0.02, rot=(0, 0, 0), segs=3):
    """Rounded box (bevel modifier, applied at export)."""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
    bm.to_mesh(me)
    bm.free()
    ob = _new_obj(name, me, material)
    ob.location = loc
    ob.rotation_euler = Euler([math.radians(a) for a in rot])
    if bevel > 0:
        mod = ob.modifiers.new("Bevel", "BEVEL")
        mod.width = bevel
        mod.segments = segs
        mod.limit_method = "NONE"
        mod.harden_normals = False
    _smooth(ob)
    return ob


def bevel_edges_sharp(ob):
    """Keep cylinder caps crisp while the sides stay smooth."""
    mod = ob.modifiers.new("Smooth", "WEIGHTED_NORMAL") if False else None
    for p in ob.data.polygons:
        # flat caps: polygons whose normal is near +/-Z in local space
        p.use_smooth = abs(p.normal.z) < 0.9


def torus(name, loc, major, minor, material, rot=(0, 0, 0), segs=40, minor_segs=14, arc=360.0):
    """Torus (or a partial arc of one, e.g. a smile) lying in local XY."""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    arc_r = math.radians(arc)
    rings = []
    n = segs if arc >= 360 else max(4, int(segs * arc / 360.0))
    closed = arc >= 360
    count = n if closed else n + 1
    for i in range(count):
        a = arc_r * i / n - (0 if closed else arc_r / 2) - math.pi / 2
        c = Vector((math.cos(a) * major, math.sin(a) * major, 0))
        out = Vector((math.cos(a), math.sin(a), 0))
        ring = []
        for j in range(minor_segs):
            b = 2 * math.pi * j / minor_segs
            p = c + out * math.cos(b) * minor + Vector((0, 0, 1)) * math.sin(b) * minor
            ring.append(bm.verts.new(p))
        rings.append(ring)
    for i in range(len(rings) - (0 if closed else 1)):
        r1, r2 = rings[i], rings[(i + 1) % len(rings)]
        for j in range(minor_segs):
            bm.faces.new((r1[j], r1[(j + 1) % minor_segs], r2[(j + 1) % minor_segs], r2[j]))
    if not closed:  # round end caps
        for ring, flip in ((rings[0], True), (rings[-1], False)):
            f = bm.faces.new(ring if not flip else list(reversed(ring)))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    ob = _new_obj(name, me, material)
    ob.location = loc
    ob.rotation_euler = Euler([math.radians(a) for a in rot])
    _smooth(ob)
    return ob


def dome(name, loc, size, material, keep_above=0.0, tilt=0.0, segs=40, rings=20):
    """An ellipsoid cut by a plane: keeps the part above local z = keep_above (fraction of the half-height),
    with the cut tilted around X by 'tilt' degrees (positive = lower at the back). Good for hair caps."""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=segs, v_segments=rings, radius=0.5)
    bmesh.ops.scale(bm, vec=Vector(size), verts=bm.verts)
    n = Vector((0, 0, 1))
    n.rotate(Euler((math.radians(-tilt), 0, 0)))
    co = Vector((0, 0, keep_above * size[2] * 0.5))
    geom = bm.verts[:] + bm.edges[:] + bm.faces[:]
    bmesh.ops.bisect_plane(bm, geom=geom, plane_co=co, plane_no=n, clear_inner=True)
    # give the cut edge a little thickness by filling it
    edges = [e for e in bm.edges if e.is_boundary]
    if edges:
        bmesh.ops.holes_fill(bm, edges=edges, sides=0)
    bm.to_mesh(me)
    bm.free()
    ob = _new_obj(name, me, material)
    ob.location = loc
    _smooth(ob)
    return ob


def empty(name, loc, parent=None, size=0.08, scale=(1, 1, 1)):
    ob = bpy.data.objects.new(name, None)
    ob.empty_display_type = "PLAIN_AXES"
    ob.empty_display_size = size
    bpy.context.scene.collection.objects.link(ob)
    ob.location = loc
    ob.scale = scale
    if parent is not None:
        set_parent(ob, parent)
    return ob


def set_parent(child, parent):
    """Parent while keeping the child's world transform."""
    bpy.context.view_layer.update()
    mw = child.matrix_world.copy()
    child.parent = parent
    child.matrix_parent_inverse = parent.matrix_world.inverted()
    child.matrix_world = mw


def join(name, objs):
    """Merge several mesh objects into one (materials are kept as slots). Applies their modifiers first."""
    objs = [o for o in objs if o is not None]
    apply_modifiers(objs)
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    ob = bpy.context.view_layer.objects.active
    ob.name = name
    ob.data.name = name
    return ob


def apply_modifiers(objs):
    dg = bpy.context.evaluated_depsgraph_get()
    for o in objs:
        if o.type != "MESH" or not o.modifiers:
            continue
        ev = o.evaluated_get(dg)
        me = bpy.data.meshes.new_from_object(ev)
        old = o.data
        o.modifiers.clear()
        o.data = me
        bpy.data.meshes.remove(old)


def set_origin(ob, point):
    """Move an object's origin (pivot) to a world-space point without moving its geometry."""
    bpy.context.view_layer.update()
    mw = ob.matrix_world.copy()
    local = mw.inverted() @ Vector(point)
    ob.data.transform(Matrix.Translation(-local))
    ob.matrix_world = mw @ Matrix.Translation(local)


def face_point(center, radius, yaw_deg, pitch_deg, out=0.0):
    """A point on a sphere's surface (front = -Y). yaw: +right, pitch: +up."""
    yaw, pitch = math.radians(yaw_deg), math.radians(pitch_deg)
    d = Vector((math.sin(yaw) * math.cos(pitch), -math.cos(yaw) * math.cos(pitch), math.sin(pitch)))
    return Vector(center) + d * (radius + out)


def face_rot(yaw_deg, pitch_deg):
    """Rotation (degrees, XYZ) that turns a part's local -Y to face out of the sphere at that spot."""
    return (-pitch_deg, 0.0, yaw_deg)


# ------------------------------------------------------------------------------------------------ organic shapes

def _catmull(points, values, per_span=6):
    """Smoothly resample a list of points (Vectors) and matching scalar/tuple values."""
    n = len(points)
    out_p, out_v = [], []
    def get(lst, i):
        return lst[max(0, min(n - 1, i))]
    for i in range(n - 1):
        p0, p1, p2, p3 = get(points, i - 1), get(points, i), get(points, i + 1), get(points, i + 2)
        v0, v1, v2, v3 = get(values, i - 1), get(values, i), get(values, i + 1), get(values, i + 2)
        for k in range(per_span):
            t = k / per_span
            t2, t3 = t * t, t * t * t
            def cr(a, b, c, d):
                return 0.5 * ((2 * b) + (-a + c) * t + (2 * a - 5 * b + 4 * c - d) * t2 + (-a + 3 * b - 3 * c + d) * t3)
            out_p.append(cr(p0, p1, p2, p3))
            if isinstance(v1, tuple):
                out_v.append(tuple(cr(v0[j], v1[j], v2[j], v3[j]) for j in range(len(v1))))
            else:
                out_v.append(cr(v0, v1, v2, v3))
    out_p.append(points[-1])
    out_v.append(values[-1])
    return out_p, out_v


def tube(name, points, radii, material, segs=24, per_span=6, cap=True, cap_start=True, cap_rings=(0.38, 0.7, 0.92, 0.995)):
    """A smooth limb: a tube through 'points' with a radius per point (radii may be (rx, ry) tuples for
    flattened cross-sections). Ends are rounded off like a capsule."""
    pts = [Vector(p) for p in points]
    rs = [r if isinstance(r, tuple) else (r, r) for r in radii]
    pts, rs = _catmull(pts, rs, per_span)
    if cap:  # add hemispherical end rings
        for end in ((0, 1) if cap_start else (1,)):
            c = pts[0] if end == 0 else pts[-1]
            t = (pts[0] - pts[1]).normalized() if end == 0 else (pts[-1] - pts[-2]).normalized()
            rx, ry = rs[0] if end == 0 else rs[-1]
            extra_p, extra_r = [], []
            for f in cap_rings:
                ang = math.asin(f)
                extra_p.append(c + t * math.sin(ang) * min(rx, ry))
                extra_r.append((rx * math.cos(ang), ry * math.cos(ang)))
            if end == 0:
                pts = list(reversed(extra_p)) + pts
                rs = list(reversed(extra_r)) + rs
            else:
                pts = pts + extra_p
                rs = rs + extra_r
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    tangents = []
    for i in range(len(pts)):
        a = pts[max(0, i - 1)]
        b = pts[min(len(pts) - 1, i + 1)]
        tangents.append((b - a).normalized())
    ref = Vector((0, 0, 1)) if abs(tangents[0].z) < 0.9 else Vector((1, 0, 0))
    side = tangents[0].cross(ref).normalized()
    rings = []
    for i, (c, t) in enumerate(zip(pts, tangents)):
        side = (side - t * side.dot(t)).normalized()  # parallel transport: no twisting
        up = t.cross(side).normalized()
        rx, ry = rs[i]
        rings.append([bm.verts.new(c + side * math.cos(2 * math.pi * j / segs) * rx + up * math.sin(2 * math.pi * j / segs) * ry)
                      for j in range(segs)])
    for i in range(len(rings) - 1):
        for j in range(segs):
            bm.faces.new((rings[i][j], rings[i][(j + 1) % segs], rings[i + 1][(j + 1) % segs], rings[i + 1][j]))
    if cap:
        ends = [(rings[-1], pts[-1])]
        if cap_start:
            ends.append((rings[0], pts[0] + (pts[0] - pts[1]).normalized() * 0.0005))
        for ring, tip in ends:
            pole = bm.verts.new(tip)
            for j in range(segs):
                bm.faces.new((ring[j], ring[(j + 1) % segs], pole))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    ob = _new_obj(name, me, material)
    _smooth(ob)
    return ob


def loft(name, center, profile, material, segs=40, per_span=5):
    """A smooth body of revolution-ish shape: 'profile' is a list of (z, rx, ry, y_offset) rings from bottom to
    top (z in world units relative to 'center'). Closed at both ends."""
    pts = [Vector((0, p[3] if len(p) > 3 else 0.0, p[0])) for p in profile]
    vals = [(p[1], p[2]) for p in profile]
    pts, vals = _catmull(pts, vals, per_span)
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    rings = []
    for c, (rx, ry) in zip(pts, vals):
        rings.append([bm.verts.new(Vector(center) + c + Vector((math.sin(2 * math.pi * j / segs) * rx, -math.cos(2 * math.pi * j / segs) * ry, 0)))
                      for j in range(segs)])
    for i in range(len(rings) - 1):
        for j in range(segs):
            bm.faces.new((rings[i][j], rings[i][(j + 1) % segs], rings[i + 1][(j + 1) % segs], rings[i + 1][j]))
    bot = bm.verts.new(Vector(center) + pts[0])
    top = bm.verts.new(Vector(center) + pts[-1])
    for j in range(segs):
        bm.faces.new((rings[0][(j + 1) % segs], rings[0][j], bot))
        bm.faces.new((rings[-1][j], rings[-1][(j + 1) % segs], top))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    ob = _new_obj(name, me, material)
    _smooth(ob)
    return ob


def profile_at(profile, z):
    """(rx, ry, y_offset) of a loft profile at height z (linear between rings; good enough for placing details)."""
    for a, b in zip(profile, profile[1:]):
        if a[0] <= z <= b[0]:
            t = (z - a[0]) / max(1e-6, b[0] - a[0])
            ya = a[3] if len(a) > 3 else 0.0
            yb = b[3] if len(b) > 3 else 0.0
            return (a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t, ya + (yb - ya) * t)
    last = profile[-1]
    return (last[1], last[2], last[3] if len(last) > 3 else 0.0)


def fur_clumps(name, center, size, material, count, length, width, keep=None, droop=0.8, seed=1, jitter=0.25):
    """Shaggy fur: 'count' tapered clumps sprouting from an ellipsoid (center, full size), each bending
    downward like hanging hair. keep(normal) -> bool filters where clumps grow (e.g. not on the face)."""
    import random
    rnd = random.Random(seed)
    c = Vector(center)
    hx, hy, hz = size[0] / 2, size[1] / 2, size[2] / 2
    objs = []
    golden = math.pi * (3 - math.sqrt(5))
    n_total = int(count * 3)
    cands = []
    for i in range(n_total):
        z = 1 - 2 * (i + 0.5) / n_total
        r = math.sqrt(1 - z * z)
        th = golden * i
        n = Vector((math.cos(th) * r, math.sin(th) * r, z))
        n += Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-1, 1))) * jitter * 0.3
        n.normalize()
        if keep is None or keep(n):
            cands.append(n)
    step = max(1.0, len(cands) / max(1, count))  # spread evenly over the whole allowed area
    picked = [cands[int(k * step)] for k in range(min(count, len(cands))) if int(k * step) < len(cands)]
    for i, n in enumerate(picked):
        base = c + Vector((n.x * hx, n.y * hy, n.z * hz))
        L = length * rnd.uniform(0.7, 1.25)
        w = width * rnd.uniform(0.75, 1.2)
        dirv = (n * (1 - droop) + Vector((0, 0, -1)) * droop + n * 0.35).normalized()
        if n.z < -0.6:
            dirv = (n + Vector((0, 0, -0.3))).normalized()
        p0 = base - n * w * 0.8
        p1 = base + dirv * L * 0.45 + n * L * 0.12
        p2 = base + dirv * L
        objs.append(tube(f"{name}_{i}", [p0, p1, p2], [w, w * 0.72, w * 0.08], material, segs=6, per_span=2,
                         cap_start=False, cap_rings=()))
    if not objs:
        return None
    return join(name, objs)


def sculpted_sphere(name, center, radii, materials, radius_fn, material_fn, segs=56, rings=28):
    """An ellipsoid whose surface is pushed in/out smoothly: radius_fn(dir) -> multiplier, where dir is the unit
    direction from the centre (front = -Y). material_fn(dir) -> index into 'materials' for each face, so a face
    patch can be part of the same smooth surface instead of a separate stuck-on shape."""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=segs, v_segments=rings, radius=1.0)
    for v in bm.verts:
        d = v.co.normalized()
        v.co = Vector((d.x * radii[0], d.y * radii[1], d.z * radii[2])) * radius_fn(d)
    bm.faces.ensure_lookup_table()
    for f in bm.faces:
        c = f.calc_center_median()
        f.material_index = material_fn(Vector((c.x / radii[0], c.y / radii[1], c.z / radii[2])).normalized())
    bm.to_mesh(me)
    bm.free()
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    for m in materials:
        ob.data.materials.append(m)
    ob.location = center
    _smooth(ob)
    return ob


def surface_point(center, radii, radius_fn, d, out=0.0):
    """Point on a sculpted_sphere's surface in direction d (plus 'out' metres along the direction)."""
    d = Vector(d).normalized()
    p = Vector((d.x * radii[0], d.y * radii[1], d.z * radii[2])) * radius_fn(d)
    return Vector(center) + p + d * out


def surface_patch(name, center, radii, radius_fn, a, b, z0, material, out=0.004, rings=16, segs=48, x0=0.0, shape=None):
    """A smooth elliptical patch lying on a sculpted_sphere's front: directions (x, z) inside the ellipse
    x^2/a^2 + (z - z0)^2/b^2 <= 1 (front = -Y). Gives a clean curved edge (no stair-steps) for faces."""
    me = bpy.data.meshes.new(name)
    bm = bmesh.new()

    def pt(r, th):
        k = shape(th) if shape is not None else 1.0  # optional outline shaping (e.g. a heart-shaped nose hole)
        dx, dz = x0 + r * k * a * math.cos(th), z0 + r * k * b * math.sin(th)
        dy = -math.sqrt(max(0.0, 1 - dx * dx - dz * dz))
        d = Vector((dx, dy, dz))
        return surface_point((0, 0, 0), radii, radius_fn, d, out)

    centre = bm.verts.new(pt(0.0, 0.0))
    prev = None
    for i in range(1, rings + 1):
        r = i / rings
        ring = [bm.verts.new(pt(r, 2 * math.pi * j / segs)) for j in range(segs)]
        if prev is None:
            for j in range(segs):
                bm.faces.new((centre, ring[(j + 1) % segs], ring[j]))
        else:
            for j in range(segs):
                bm.faces.new((prev[j], prev[(j + 1) % segs], ring[(j + 1) % segs], ring[j]))
        prev = ring
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    # make sure it faces outward (front)
    bm.faces.ensure_lookup_table()
    if bm.faces[0].normal.y > 0:
        bmesh.ops.reverse_faces(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    ob = _new_obj(name, me, material)
    ob.location = center
    _smooth(ob)
    return ob
