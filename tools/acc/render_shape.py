# Blender: grey shaded mesh from 3 axis views (Unity x,y,z -> Blender x,z,y), labelled by view axis
import bpy, json, sys
from mathutils import Vector
a = sys.argv[sys.argv.index("--") + 1:]; d = json.load(open(a[0])); out = a[1]
bpy.ops.wm.read_factory_settings(use_empty=True); sc = bpy.context.scene
pos = [(p[0], p[2], p[1]) for p in d["pos"]]
me = bpy.data.meshes.new("m"); me.from_pydata(pos, [], [tuple(t) for t in d["tris"]]); me.update()
ob = bpy.data.objects.new("o", me); sc.collection.objects.link(ob)
mn = Vector([min(p[i] for p in pos) for i in range(3)]); mx = Vector([max(p[i] for p in pos) for i in range(3)]); c = (mn + mx) / 2; R = (mx - mn).length
sun = bpy.data.objects.new("sun", bpy.data.lights.new("sun", "SUN")); sc.collection.objects.link(sun); sun.rotation_euler = (0.7, 0.3, 0.5)
w = bpy.data.worlds.new("w"); w.color = (0.35, 0.35, 0.38); sc.world = w
sc.render.engine = "BLENDER_EEVEE_NEXT" if "BLENDER_EEVEE_NEXT" in [e.identifier for e in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items] else "BLENDER_EEVEE"
sc.render.resolution_x = sc.render.resolution_y = 360
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam; cam.data.clip_start = 0.0005
for name, dv in [("ux", (1, 0, 0)), ("uy", (0, 0, 1)), ("uz", (0, 1, 0))]:   # looking along Unity +x, +y, +z
    cam.location = c + Vector(dv) * R * 1.6; cam.rotation_euler = (c - cam.location).to_track_quat("-Z", "Y" if name != "uy" else "X").to_euler()
    sc.render.filepath = f"{out}_{name}.png"; bpy.ops.render.render(write_still=True)
