# Fusil à pompe (type « 870 ») modélisé par code : aucune question de licence.
# Pièces exportées séparément (Unity les anime) :
#   « Fusil »  : carcasse, canon à bande ventilée, magasin tubulaire, pontet, détente, crosse anglaise en noyer ;
#   « Pompe »  : devant tourné et rainuré + barres d'armement (pivot à sa position avant, recule de 9 cm).
# + une cartouche de calibre 12 (« Cartouche_12 », dossier à part) pour le rechargement.
# Repère Blender : canon vers +Y, haut = +Z, droite (fenêtre d'éjection) = +X, origine sur l'axe du canon
# à l'arrière de la carcasse. Bois : texture Poly Haven CC0 (Tools/Blender/textures).
#
# Usage : blender -b --factory-startup --python Tools/Blender/build_shotgun.py [-- --render chemin.png]
# Puis dans Unity : Fragile > Placer les armes.
import bpy, bmesh, os, sys, json, math, importlib.util
from mathutils import Vector

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("fue", os.path.join(HERE, "fragile_unity_export.py"))
fue = importlib.util.module_from_spec(spec); spec.loader.exec_module(fue)
ASSETS = os.path.abspath(os.path.join(HERE, "..", "..", "Assets"))
SUB = "Bar/Armes/Modeles"
TEX = os.path.join(HERE, "textures")
ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []

bpy.ops.wm.read_factory_settings(use_empty=True)

# ------------------------------------------------------------------ matériaux

def mat(name, color, metallic, roughness):
    m = bpy.data.materials.new(name); m.use_nodes = True
    b = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    b.inputs["Base Color"].default_value = (*color, 1.0)
    b.inputs["Metallic"].default_value = metallic
    b.inputs["Roughness"].default_value = roughness
    return m


def wood_mat(name, tint):
    m = mat(name, (1, 1, 1), 0.0, 0.4)
    nt = m.node_tree; b = next(n for n in nt.nodes if n.type == "BSDF_PRINCIPLED")
    def img(file, colour):
        n = nt.nodes.new("ShaderNodeTexImage"); n.image = bpy.data.images.load(os.path.join(TEX, file))
        if not colour: n.image.colorspace_settings.name = "Non-Color"
        return n
    diff = img("noyer_crosse_diff.jpg", True)
    nt.links.new(diff.outputs["Color"], b.inputs["Base Color"])
    rough = img("noyer_crosse_rough.jpg", False); nt.links.new(rough.outputs["Color"], b.inputs["Roughness"])
    nor = img("noyer_crosse_nor.jpg", False)
    nm = nt.nodes.new("ShaderNodeNormalMap"); nt.links.new(nor.outputs["Color"], nm.inputs["Color"]); nt.links.new(nm.outputs["Normal"], b.inputs["Normal"])
    return m


BLUED = mat("Fusil_Acier", (0.028, 0.03, 0.036), 1.0, 0.3)
DARK = mat("Fusil_Interieur", (0.004, 0.004, 0.004), 0.3, 0.7)
BRASS = mat("Fusil_Laiton", (0.8, 0.58, 0.26), 1.0, 0.25)
RUBBER = mat("Fusil_Caoutchouc", (0.018, 0.018, 0.018), 0.0, 0.8)
WOOD = wood_mat("Fusil_Noyer", (1, 1, 1))

parts = {"Fusil": [], "Pompe": [], "Cartouche": []}

# ------------------------------------------------------------------ outils

def link(o):
    bpy.context.scene.collection.objects.link(o); return o


def finish(o, material, part, bevel=0.0, segs=2, smooth=True, angle=50):
    o.data.materials.append(material)
    if bevel > 0:
        mod = o.modifiers.new("Bevel", "BEVEL"); mod.width = bevel; mod.segments = segs
        mod.limit_method = "ANGLE"; mod.angle_limit = math.radians(angle)
    if smooth:
        for p in o.data.polygons: p.use_smooth = True
        o.modifiers.new("Normales", "WEIGHTED_NORMAL").keep_sharp = True
    parts[part].append(o)
    return o


def profile(name, pts, half_width, material, part="Fusil", bevel=0.003, segs=3, width_fn=None):
    """Plaque découpée selon un profil (y, z) ; demi-épaisseur selon X (fonction de y possible)."""
    me = bpy.data.meshes.new(name); bm = bmesh.new()
    face = bm.faces.new([bm.verts.new((0.0, y, z)) for y, z in pts])
    ext = bmesh.ops.extrude_face_region(bm, geom=[face])
    front = [e for e in ext["geom"] if isinstance(e, bmesh.types.BMVert)]
    back = [v for v in bm.verts if v not in front]
    for v in front: v.co.x = (width_fn(v.co.y) if width_fn else half_width)
    for v in back: v.co.x = -(width_fn(v.co.y) if width_fn else half_width)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me); bm.free()
    return finish(link(bpy.data.objects.new(name, me)), material, part, bevel, segs)


def box(name, center, size, material, part="Fusil", bevel=0.0015):
    bpy.ops.mesh.primitive_cube_add(size=1, location=center)
    o = bpy.context.object; o.name = name; o.scale = size
    bpy.ops.object.transform_apply(scale=True)
    return finish(o, material, part, bevel)


def lathe(name, stations, material, part="Fusil", segs=24, center=(0.0, 0.0), sx=1.0, sz=1.0, cap=True):
    """Pièce de révolution autour d'un axe parallèle à Y : stations = [(y, rayon)]."""
    me = bpy.data.meshes.new(name); bm = bmesh.new()
    rings = []
    for y, r in stations:
        rings.append([bm.verts.new((center[0] + math.cos(a) * r * sx, y, center[1] + math.sin(a) * r * sz))
                      for a in (2 * math.pi * i / segs for i in range(segs))])
    for a, b in zip(rings, rings[1:]):
        for i in range(segs):
            j = (i + 1) % segs
            bm.faces.new((a[i], a[j], b[j], b[i]))
    if cap:
        bm.faces.new(list(reversed(rings[0]))); bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(me); bm.free()
    return finish(link(bpy.data.objects.new(name, me)), material, part, smooth=True)


def tube(name, pts, radius, material, part="Fusil"):
    """Fil rond (pontet, détente) le long d'une polyligne (y, z) dans le plan x = 0."""
    cu = bpy.data.curves.new(name, "CURVE"); cu.dimensions = "3D"
    sp = cu.splines.new("POLY"); sp.points.add(len(pts) - 1)
    for p, (y, z) in zip(sp.points, pts): p.co = (0, y, z, 1)
    cu.bevel_depth = radius; cu.bevel_resolution = 3; cu.use_fill_caps = True
    o = link(bpy.data.objects.new(name, cu))
    bpy.context.view_layer.objects.active = o; o.select_set(True)
    bpy.ops.object.convert(target="MESH"); o.select_set(False)
    return finish(o, material, part, smooth=True)


def smooth_curve(pts, n=6, closed=True):
    """Catmull-Rom : profil adouci à partir de quelques points de contrôle."""
    out = []; m = len(pts)
    rng = range(m) if closed else range(m - 1)
    for i in rng:
        p0, p1, p2, p3 = (Vector(pts[(i + k) % m]) if closed else Vector(pts[max(0, min(m - 1, i + k))]) for k in (-1, 0, 1, 2))
        for s in range(n):
            t = s / n
            out.append(tuple(0.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3)))
    if not closed: out.append(tuple(pts[-1]))
    return out

# ------------------------------------------------------------------ carcasse, détente

receiver = [(-0.070, 0.016), (-0.064, 0.024), (0.170, 0.024), (0.172, 0.016), (0.172, -0.034),
            (0.150, -0.040), (0.020, -0.042), (-0.070, -0.042)]
carcasse = profile("Carcasse", receiver, 0.0168, BLUED, bevel=0.0025)
# Vraies ouvertures : fenêtre d'éjection (côté droit) et trappe de chargement (dessous).
def cut(target, center, size):
    bpy.ops.mesh.primitive_cube_add(size=1, location=center)
    c = bpy.context.object; c.scale = size; c.display_type = "WIRE"
    mod = target.modifiers.new("Ouverture", "BOOLEAN"); mod.operation = "DIFFERENCE"; mod.object = c
    target.modifiers.move(len(target.modifiers) - 1, 0)
    return c
cutters = [cut(carcasse, (0.02, 0.072, 0.003), (0.02, 0.078, 0.022)), cut(carcasse, (0, 0.085, -0.045), (0.02, 0.105, 0.012))]
box("Interieur", (0, 0.075, -0.004), (0.026, 0.13, 0.05), DARK, bevel=0)
box("Culasse", (0.0115, 0.07, 0.003), (0.008, 0.068, 0.016), mat("Fusil_Culasse", (0.5, 0.5, 0.52), 1.0, 0.2), bevel=0.001)
for y in (-0.045, -0.002):
    bpy.ops.mesh.primitive_cylinder_add(vertices=12, radius=0.0022, depth=0.0345, location=(0, y, -0.03), rotation=(0, math.radians(90), 0))
    finish(bpy.context.object, BLUED, "Fusil", smooth=True)                                 # goupilles
box("Platine", (0, -0.025, -0.048), (0.026, 0.095, 0.014), BLUED, bevel=0.002)
tube("Pontet", [(0.02, -0.052), (0.018, -0.07), (0.005, -0.083), (-0.03, -0.085), (-0.05, -0.078), (-0.062, -0.058), (-0.062, -0.052)], 0.0035, BLUED)
tube("Detente", [(-0.012, -0.053), (-0.013, -0.063), (-0.016, -0.072), (-0.021, -0.077)], 0.0028, BLUED)
bpy.ops.mesh.primitive_cylinder_add(vertices=16, radius=0.004, depth=0.03, location=(0, -0.058, -0.049), rotation=(0, math.radians(90), 0))
finish(bpy.context.object, BLUED, "Fusil", smooth=True)                                    # sûreté

# ------------------------------------------------------------------ canon, bande ventilée, magasin

lathe("Canon", [(0.165, 0.0135), (0.20, 0.0135), (0.205, 0.0118), (0.66, 0.0108), (0.668, 0.0108)], BLUED, segs=28)
lathe("Ame", [(0.6672, 0.0091), (0.6675, 0.0091)], DARK, segs=20)
box("Bande", (0, 0.43, 0.0145), (0.007, 0.46, 0.0022), BLUED, bevel=0.0007)
for i in range(12):
    box("Plot", (0, 0.215 + i * 0.039, 0.0118), (0.004, 0.006, 0.0045), BLUED, bevel=0.0005)
bpy.ops.mesh.primitive_uv_sphere_add(radius=0.0023, segments=12, ring_count=8, location=(0, 0.66, 0.0175))
finish(bpy.context.object, BRASS, "Fusil")
lathe("Magasin", [(0.168, 0.0105), (0.56, 0.0105)], BLUED, center=(0.0, -0.025), segs=24)
caps = [(0.56, 0.0122), (0.565, 0.0128)]
for k in range(6):
    y = 0.567 + k * 0.0055; caps += [(y, 0.0128), (y + 0.0015, 0.0121), (y + 0.003, 0.0128)]
caps += [(0.6, 0.0125), (0.604, 0.009)]
lathe("Bouchon", caps, BLUED, center=(0.0, -0.025), segs=24)
box("Collier", (0, 0.548, -0.008), (0.012, 0.012, 0.03), BLUED, bevel=0.002)

# ------------------------------------------------------------------ crosse anglaise (noyer)

stock_ctrl = [(-0.068, 0.017), (-0.10, 0.012), (-0.16, 0.004), (-0.30, -0.010), (-0.418, -0.022), (-0.426, -0.04),
              (-0.432, -0.160), (-0.40, -0.158), (-0.30, -0.128), (-0.21, -0.092), (-0.15, -0.064),
              (-0.11, -0.056), (-0.085, -0.057), (-0.068, -0.05)]
stock = smooth_curve(stock_ctrl, n=5)
def stock_width(y):
    t = min(1.0, max(0.0, (-y - 0.07) / 0.35))
    wrist = 1.0 - math.exp(-((-y - 0.12) / 0.05) ** 2) * 0.25      # poignée amincie
    return (0.0155 + 0.0065 * t) * wrist
profile("Crosse", stock, 0.02, WOOD, bevel=0.009, segs=4, width_fn=stock_width)
profile("Plaque_Couche", [(-0.429, -0.02), (-0.447, -0.022), (-0.452, -0.162), (-0.433, -0.16)], 0.0205, RUBBER, bevel=0.004)

# ------------------------------------------------------------------ pompe (noyer tourné, rainuré)

fore = [(0.20, 0.019), (0.205, 0.024), (0.215, 0.0262)]
for k in range(9):
    y = 0.228 + k * 0.019
    fore += [(y, 0.0262), (y + 0.004, 0.0238), (y + 0.010, 0.0238), (y + 0.014, 0.0262)]
fore += [(0.405, 0.0262), (0.418, 0.024), (0.424, 0.02)]
lathe("Pompe_Bois", fore, WOOD, part="Pompe", segs=28, center=(0.0, -0.019), sx=0.82, sz=1.0)
for x in (-0.0135, 0.0135):
    box("Barre", (x, 0.15, -0.028), (0.0025, 0.13, 0.006), BLUED, part="Pompe", bevel=0.0006)

# ------------------------------------------------------------------ cartouche de calibre 12

lathe("Cartouche_Etui", [(-0.0215, 0.0103), (0.026, 0.0103), (0.029, 0.009), (0.0305, 0.004), (0.031, 0.0)],
      mat("Cartouche_Plastique", (0.55, 0.03, 0.025), 0.0, 0.45), part="Cartouche", segs=20)
lathe("Cartouche_Culot", [(-0.0335, 0.0115), (-0.032, 0.0118), (-0.0305, 0.0118), (-0.030, 0.0108), (-0.0205, 0.0108)],
      BRASS, part="Cartouche", segs=20)

# ------------------------------------------------------------------ export

def join(objs, name, origin, uv_cube=0.0):
    for o in objs:
        bpy.context.view_layer.objects.active = o
        for m in list(o.modifiers): bpy.ops.object.modifier_apply(modifier=m.name)
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs: o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    bpy.ops.object.join()
    o = bpy.context.object; o.name = name
    bpy.context.scene.cursor.location = origin
    bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
    # UV à l'échelle réelle (le fil du bois garde la même taille partout).
    bpy.ops.object.mode_set(mode="EDIT"); bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.cube_project(cube_size=0.35, scale_to_bounds=False)
    bpy.ops.object.mode_set(mode="OBJECT")
    o.select_set(False)
    return o


def export(objs, name):
    report = []
    fue.export_group(bpy.context, objs, name, ASSETS, SUB, False, report)
    mf = os.path.join(ASSETS, SUB, fue.safe_name(name), fue.MANIFEST_NAME)
    m = json.load(open(mf, encoding="utf-8")); m["place"] = False; m["source"] = "Modélisé par Tools/Blender/build_shotgun.py"
    json.dump(m, open(mf, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
    print("[FUSIL]", report, sum(len(o.data.polygons) for o in objs), "faces")


body = join(parts["Fusil"], "Fusil", (0, 0, 0))
for c in cutters: bpy.data.objects.remove(c, do_unlink=True)
pump = join(parts["Pompe"], "Pompe", (0, 0.28, -0.019))
shell = join(parts["Cartouche"], "Cartouche", (0, 0, 0))

if "--render" in ARGS:
    out = ARGS[ARGS.index("--render") + 1]
    scn = bpy.context.scene
    for eng in ("BLENDER_EEVEE_NEXT", "BLENDER_EEVEE", "CYCLES"):
        try: scn.render.engine = eng; break
        except TypeError: pass
    if scn.render.engine == "CYCLES": scn.cycles.samples = 24
    scn.render.resolution_x, scn.render.resolution_y = 1400, 700
    w = bpy.data.worlds.new("W"); scn.world = w; w.use_nodes = True
    w.node_tree.nodes["Background"].inputs[0].default_value = (0.45, 0.45, 0.5, 1); w.node_tree.nodes["Background"].inputs[1].default_value = 1.0
    shell.location = (0.06, 0.05, -0.11)
    for i, (loc, rot, e) in enumerate([((0.6, -0.4, 0.5), (0.9, 0, 0.8), 3.0), ((-0.6, 0.3, 0.4), (1.0, 0, -2.4), 1.5)]):
        l = bpy.data.lights.new(f"L{i}", "AREA"); l.energy = e * 18; l.size = 1.0
        lo = link(bpy.data.objects.new(f"L{i}", l)); lo.location = loc; lo.rotation_euler = rot
    cam = bpy.data.cameras.new("C"); cam.lens = 50
    co = link(bpy.data.objects.new("C", cam)); co.location = (1.95, 0.11, 0.12)
    d = Vector((0, 0.11, -0.04)) - co.location; co.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
    scn.camera = co; scn.render.filepath = out
    bpy.ops.render.render(write_still=True)
    print("[FUSIL] rendu", out)
else:
    export([body, pump], "Fusil_Pompe")
    export([shell], "Cartouche_12")
