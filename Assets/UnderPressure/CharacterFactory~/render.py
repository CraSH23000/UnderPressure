"""Studio render of whatever is in the scene: soft lights, light floor, 3/4 camera that frames everything."""
import math
import sys
import bpy
from mathutils import Vector


def studio(res=(1600, 800), samples=64):
    sc = bpy.context.scene
    sc.render.engine = "CYCLES"
    sc.cycles.device = "CPU"
    sc.cycles.samples = samples
    sc.cycles.use_denoising = True
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.film_transparent = False
    sc.view_settings.view_transform = "Standard"  # closest to how Unity's URP shows these colors

    world = bpy.data.worlds.new("World")
    sc.world = world
    world.use_nodes = True
    bg = world.node_tree.nodes["Background"]
    bg.inputs["Color"].default_value = (0.82, 0.86, 0.9, 1)
    bg.inputs["Strength"].default_value = 0.25

    # floor (sterile lab tile color)
    me = bpy.data.meshes.new("Floor")
    import bmesh
    bm = bmesh.new()
    bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=20)
    bm.to_mesh(me)
    bm.free()
    floor = bpy.data.objects.new("Floor", me)
    sc.collection.objects.link(floor)
    fm = bpy.data.materials.new("FloorMat")
    fm.use_nodes = True
    fm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.78, 0.8, 0.83, 1)
    fm.node_tree.nodes["Principled BSDF"].inputs["Roughness"].default_value = 0.45
    floor.data.materials.append(fm)

    def area(name, loc, energy, size, color=(1, 1, 1)):
        ld = bpy.data.lights.new(name, "AREA")
        ld.energy = energy
        ld.size = size
        ld.color = color
        ob = bpy.data.objects.new(name, ld)
        sc.collection.objects.link(ob)
        ob.location = loc
        d = Vector((0, 0, 0.8)) - Vector(loc)
        ob.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
        return ob

    area("Key", (-3, -4, 5), 260, 4)
    area("Fill", (4, -3, 2.5), 90, 4, (0.85, 0.9, 1.0))
    area("Rim", (0, 4, 4), 220, 3, (1.0, 0.95, 0.9))


def frame_camera(yaw=-18, pitch=12, lens=60, margin=1.12):
    sc = bpy.context.scene
    pts = []
    for ob in sc.objects:
        if ob.type == "MESH" and ob.name != "Floor":
            pts += [ob.matrix_world @ Vector(c) for c in ob.bound_box]
    lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
    hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
    center = (lo + hi) / 2
    cam_d = bpy.data.cameras.new("Cam")
    cam_d.lens = lens
    cam = bpy.data.objects.new("Cam", cam_d)
    sc.collection.objects.link(cam)
    sc.camera = cam
    yaw_r, pitch_r = math.radians(yaw), math.radians(pitch)
    direction = Vector((math.sin(yaw_r) * math.cos(pitch_r), -math.cos(yaw_r) * math.cos(pitch_r), math.sin(pitch_r)))
    # distance so the bounding box fits the frame
    w = (hi - lo).length
    aspect = sc.render.resolution_x / sc.render.resolution_y
    fov = 2 * math.atan(36 / 2 / lens)
    width_needed = max(hi.x - lo.x, (hi.z - lo.z) * aspect) * margin
    dist = (width_needed / 2) / math.tan(fov / 2) + (hi.y - lo.y)
    cam.location = center + direction * dist
    cam.rotation_euler = (center - cam.location).to_track_quat("-Z", "Y").to_euler()


def render(path):
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
