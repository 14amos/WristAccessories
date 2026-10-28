# Blender: hands (textured) + fitted accessories (flat colours) from test_fit.py's scene json, views of both forearms
import bpy, json, sys, os
from mathutils import Vector
a = sys.argv[sys.argv.index("--") + 1:]; d = json.load(open(a[0])); tex = a[1]; out = a[2]
bpy.ops.wm.read_factory_settings(use_empty=True); sc = bpy.context.scene
def mk(name, pos, tris, mat):
    P = [(p[0], p[2], p[1]) for p in pos]; T = tris[0]
    me = bpy.data.meshes.new(name); me.from_pydata(P, [], [tuple(T[i:i + 3]) for i in range(0, len(T), 3)]); me.update()
    ob = bpy.data.objects.new(name, me); sc.collection.objects.link(ob); me.materials.append(mat); return ob, me
m = bpy.data.materials.new("hands"); m.use_nodes = True; nt = m.node_tree
img = nt.nodes.new("ShaderNodeTexImage"); img.image = bpy.data.images.load(tex); nt.links.new(img.outputs["Color"], nt.nodes["Principled BSDF"].inputs["Base Color"])
ob, me = mk("hands", d["hands"]["pos"], d["hands"]["tris"], m)
uvl = me.uv_layers.new(); uv = d["hands"]["uv"]
for poly in me.polygons:
    for li in poly.loop_indices: uvl.data[li].uv = uv[me.loops[li].vertex_index]
cols = {"rolex": (0.9, 0.7, 0.2), "chainlet": (0.75, 0.75, 0.8), "paracord": (0.2, 0.5, 0.2), "prokill": (0.85, 0.65, 0.3), "dogtag": (0.6, 0.6, 0.6), "chain_gold": (1, 0.8, 0.2), "amulet": (0.9, 0.9, 0.8)}
cent = {"L": [], "R": []}
frames = {}
for e in d["acc"]:
    if e["name"].endswith("_frame"):
        c_, a_, e1_ = (Vector((p[0], p[2], p[1])) for p in e["pos"]); frames[e["name"][0]] = (c_, a_.normalized(), e1_.normalized()); continue
    side, nm = e["name"][0], e["name"].split("_", 1)[1].rsplit("_", 1)[0] if e["name"].count("_") > 1 else e["name"]
    key = next((k for k in cols if k in e["name"]), "rolex")
    mm = bpy.data.materials.new(e["name"]); mm.diffuse_color = (*cols[key], 1); mm.use_nodes = True; mm.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (*cols[key], 1)
    mm.node_tree.nodes["Principled BSDF"].inputs["Metallic"].default_value = 0.6
    texs = {"Hands_Boss": "WORK/wristacc/patches/Hands_Boss_1_d.png", "rolex_LOD0": "tex_item_barter_valuable_rolex_D.png", "Glass": "tex_item_barter_valuable_rolex_Glass_D.png", "prokill": "tex_item_barter_valuable_prokill_D.png", "paracord": "tex_item_barter_other_paracord_D.png"}
    tk = next((v for k, v in texs.items() if k in e["name"]), None)
    if tk and "uv" in e:
        im2 = mm.node_tree.nodes.new("ShaderNodeTexImage"); im2.image = bpy.data.images.load(tk if ":" in tk else "WORK/tattoo/acc/" + tk)
        mm.node_tree.links.new(im2.outputs["Color"], mm.node_tree.nodes["Principled BSDF"].inputs["Base Color"])
    o2, me2 = mk(e["name"], e["pos"], e["tris"], mm); cent[side] += [Vector((p[0], p[2], p[1])) for p in e["pos"][::20]]
    if "uv" in e:
        ul = me2.uv_layers.new()
        for poly in me2.polygons:
            for li in poly.loop_indices: ul.data[li].uv = e["uv"][me2.loops[li].vertex_index]
for nm, (ang, el) in {"sun": ((0.6, 0.2, 0.8), 3.0)}.items():
    s = bpy.data.objects.new(nm, bpy.data.lights.new(nm, "SUN")); s.data.energy = el; sc.collection.objects.link(s); s.rotation_euler = ang
w = bpy.data.worlds.new("w"); w.color = (0.5, 0.5, 0.52); sc.world = w
sc.render.engine = "BLENDER_EEVEE_NEXT" if "BLENDER_EEVEE_NEXT" in [x.identifier for x in bpy.types.RenderSettings.bl_rna.properties["engine"].enum_items] else "BLENDER_EEVEE"
sc.render.resolution_x = 640; sc.render.resolution_y = 480
cam = bpy.data.objects.new("cam", bpy.data.cameras.new("cam")); sc.collection.objects.link(cam); sc.camera = cam; cam.data.clip_start = 0.005
for side in "LR":
    c, axv, e1v = frames[side]; e2v = axv.cross(e1v)
    mk_ = bpy.data.materials.new("mark" + side); mk_.use_nodes = True; mk_.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (1, 0, 0, 1)
    bpy.ops.mesh.primitive_uv_sphere_add(radius=0.008, location=c + axv * 0.10 + e1v * 0.07); bpy.context.object.data.materials.append(mk_)   # red ball = 'back of the hand' side
    tgt = c + axv * 0.10; cam.location = tgt + e1v * 0.42; cam.rotation_euler = (tgt - cam.location).to_track_quat("-Z", "Y").to_euler()
    sc.render.filepath = f"{out}_{side}_wide.png"; bpy.ops.render.render(write_still=True)
    for vn, dv in [("back", e1v), ("side", e2v), ("under", -e1v)]:
        cam.location = c + dv * 0.26 + axv * 0.02; cam.rotation_euler = (c - cam.location).to_track_quat("-Z", "Y").to_euler()
        sc.render.filepath = f"{out}_{side}_{vn}.png"; bpy.ops.render.render(write_still=True)
