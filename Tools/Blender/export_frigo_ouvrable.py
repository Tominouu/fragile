# Export du frigo vitrine avec porte séparée (pivot sur la charnière) et 2 étagères au lieu de 4.
# Usage : blender -b Refrigerateur_Vitrine.blend --python Tools/Blender/export_frigo_ouvrable.py
# Puis dans Unity : Fragile > Aménager le décor du Bar, et Fragile > Éclairer le Bar.
import bpy, bmesh, os, json, importlib.util
from mathutils import Vector
spec = importlib.util.spec_from_file_location("fue", r"C:\Users\Tom\MMI\DEV\fragile\fragile\Tools\Blender\fragile_unity_export.py")
fue = importlib.util.module_from_spec(spec); spec.loader.exec_module(fue)

src = [x for x in bpy.context.scene.objects if x.type == 'MESH'][0]
dg = bpy.context.evaluated_depsgraph_get()
me = src.evaluated_get(dg).to_mesh()
mats = list(me.materials)
HINGE = Vector((-0.3, -0.293, 0.0))   # côté opposé à la poignée (poignée en +X), milieu de l'épaisseur de porte

def parts(bm):
    bm.faces.ensure_lookup_table(); seen = set(); out = []
    for f in bm.faces:
        if f.index in seen: continue
        stack = [f]; grp = []; seen.add(f.index)
        while stack:
            g = stack.pop(); grp.append(g)
            for e in g.edges:
                for h in e.link_faces:
                    if h.index not in seen: seen.add(h.index); stack.append(h)
        out.append(grp)
    return out

def build(kind):
    bm = bmesh.new(); bm.from_mesh(me); bm.transform(src.matrix_world)
    kill = []
    for grp in parts(bm):
        vs = [v.co for g in grp for v in g.verts]
        ymax = max(v.y for v in vs); zmin = min(v.z for v in vs); zmax = max(v.z for v in vs)
        is_door = ymax <= -0.268 and zmin >= 0.07 and zmax <= 1.69 and not (zmin > 1.7)
        is_door = is_door and not (max(v.y for v in vs) > -0.27 and zmin < 0.07)
        if kind == "door":
            if not is_door: kill += grp
            continue
        if is_door: kill += grp; continue
        # Étagères : 4 -> 2, plus hautes (bouteilles de ~42 cm).
        mat = mats[grp[0].material_index].name
        if mat == "M_Grille_Blanche":
            zc = (zmin + zmax) / 2
            if abs(zc - 0.725) < 0.02 or abs(zc - 1.385) < 0.02: kill += grp; continue
            dz = (0.62 - 0.395) if abs(zc - 0.395) < 0.02 else (1.09 - 1.055)
            for v in {v for g in grp for v in g.verts}: v.co.z += dz
    bmesh.ops.delete(bm, geom=list(set(kill)), context='FACES')
    if kind == "door":
        bm.transform(__import__('mathutils').Matrix.Translation(-HINGE))
    m = bpy.data.meshes.new(kind); bm.to_mesh(m); bm.free()
    for mt in mats: m.materials.append(mt)
    return m

body = bpy.data.objects.new("Caisson", build("body"))
door = bpy.data.objects.new("Porte", build("door")); door.location = HINGE
for o in (body, door): bpy.context.scene.collection.objects.link(o)
for v in door.data.vertices: pass
print("[SPLIT] body faces", len(body.data.polygons), "door faces", len(door.data.polygons))
src.hide_set(True); src.hide_render = True
report = []
assets = r"C:\Users\Tom\MMI\DEV\fragile\fragile\Assets"; sub = "Bar/Models/Decor"
fue.export_group(bpy.context, [body, door], "Refrigerateur_Vitrine", assets, sub, False, report)
mf = os.path.join(assets, sub, "Refrigerateur_Vitrine", fue.MANIFEST_NAME)
m = json.load(open(mf, encoding="utf-8")); m["place"] = False
json.dump(m, open(mf, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
print("[EXPORT]", report)
