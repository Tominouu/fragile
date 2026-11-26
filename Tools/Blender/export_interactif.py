# Export des appareils du bar avec leurs pièces mobiles séparées (pivot au bon endroit) :
#   Refrigerateur_Vitrine : Porte (charnière) ; étagères 4 -> 2 (bouteilles de 42 cm)
#   Caisse_Enregistreuse  : 20 touches, tiroir-caisse (créé ici : façade, bac, billets, pièces) + ouverture sombre
#   Tireuse_Biere         : 2 leviers
#   Machine_Cafe          : 2 boutons
#   Machine_Glacons       : trappe (charnière en bas, poignée en haut) + ouverture sombre
#   Distributeur_Boissons : 9 touches du clavier
# Usage : blender -b <Modele>.blend --python Tools/Blender/export_interactif.py
#         (le nom du fichier .blend choisit les règles ; le .blend n'est pas modifié)
# Puis dans Unity : Fragile > Aménager le décor du Bar, puis Fragile > Éclairer le Bar.
import bpy, bmesh, os, json, importlib.util
from mathutils import Vector, Matrix

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("fue", os.path.join(HERE, "fragile_unity_export.py"))
fue = importlib.util.module_from_spec(spec); spec.loader.exec_module(fue)
ASSETS = os.path.abspath(os.path.join(HERE, "..", "..", "Assets"))
SUB = "Bar/Models/Decor"

src = [x for x in bpy.context.scene.objects if x.type == 'MESH'][0]
MODEL = os.path.splitext(os.path.basename(bpy.data.filepath))[0]
dg = bpy.context.evaluated_depsgraph_get()
me = src.evaluated_get(dg).to_mesh()
mats = list(me.materials)


def extra_mat(name, color):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = next(n for n in m.node_tree.nodes if n.type == "BSDF_PRINCIPLED")
    bsdf.inputs["Base Color"].default_value = color
    bsdf.inputs["Roughness"].default_value = 0.8
    if m not in mats: mats.append(m)
    return mats.index(m)


class Part:
    def __init__(self, faces):
        self.faces = faces
        vs = [v.co for f in faces for v in f.verts]
        self.min = Vector((min(v.x for v in vs), min(v.y for v in vs), min(v.z for v in vs)))
        self.max = Vector((max(v.x for v in vs), max(v.y for v in vs), max(v.z for v in vs)))
        self.center = (self.min + self.max) / 2
        self.mat = mats[faces[0].material_index].name


def parts_of(bm):
    bm.faces.ensure_lookup_table(); seen = set(); out = []
    for f in bm.faces:
        if f.index in seen: continue
        stack = [f]; grp = []; seen.add(f.index)
        while stack:
            g = stack.pop(); grp.append(g)
            for e in g.edges:
                for h in e.link_faces:
                    if h.index not in seen: seen.add(h.index); stack.append(h)
        out.append(Part(grp))
    return out


def box(bm, lo, hi, mat_index):
    """Pavé axe-aligné (coordonnées monde Blender)."""
    res = bmesh.ops.create_cube(bm, size=1.0)
    c = (Vector(lo) + Vector(hi)) / 2; s = Vector(hi) - Vector(lo)
    for v in res["verts"]:
        v.co = Vector((c.x + v.co.x * s.x, c.y + v.co.y * s.y, c.z + v.co.z * s.z))
    for f in {f for v in res["verts"] for f in v.link_faces}: f.material_index = mat_index


# ---------------------------------------------------------------- règles par modèle
# name(part) -> nom de l'objet (None = reste dans « Corps ») ; pivots : nom -> point (sinon centre des pièces)

def rules_fridge():
    def name(p):
        if p.max.y <= -0.268 and p.min.z >= 0.07 and p.max.z <= 1.69: return "Porte"
        return None
    def mutate(p, bm):
        if p.mat != "M_Grille_Blanche": return True
        zc = p.center.z
        if abs(zc - 0.725) < 0.02 or abs(zc - 1.385) < 0.02: return False       # étagère supprimée
        dz = (0.62 - 0.395) if abs(zc - 0.395) < 0.02 else (1.09 - 1.055)
        for v in {v for f in p.faces for v in f.verts}: v.co.z += dz
        return True
    return name, {"Porte": Vector((-0.3, -0.293, 0.0))}, mutate, None


def rules_register():
    keys = []
    def name(p):
        if p.mat in ("M_Touches", "M_Touches_Rouges"):
            if p.mat == "M_Touches_Rouges": return "Touche_Total"
            row = round((p.center.y + 0.164) / 0.036); col = round((p.center.x + 0.12) / 0.045)
            return f"Touche_{row}_{col}"
        if p.mat == "M_Chrome" and p.max.y < -0.21: return "Tiroir"                 # poignée du tiroir
        return None
    def extra(objs):
        shade = extra_mat("M_Ombre", (0.01, 0.01, 0.01, 1)); bills = extra_mat("M_Billets", (0.25, 0.42, 0.22, 1))
        grey = mats.index(next(m for m in mats if m.name == "M_Plastique_Gris")); chrome = mats.index(next(m for m in mats if m.name == "M_Chrome"))
        d = objs.setdefault("Tiroir", bmesh.new())
        box(d, (-0.19, -0.221, 0.012), (0.19, -0.214, 0.072), grey)                 # façade
        box(d, (-0.18, -0.214, 0.014), (0.18, 0.10, 0.018), grey)                    # fond du bac
        for x in (-0.18, 0.176): box(d, (x, -0.214, 0.018), (x + 0.004, 0.10, 0.065), grey)
        box(d, (-0.18, 0.096, 0.018), (0.18, 0.10, 0.065), grey)
        for x in (-0.092, -0.002, 0.088): box(d, (x, -0.214, 0.018), (x + 0.004, 0.10, 0.05), grey)
        for x0 in (-0.17, -0.08): box(d, (x0, -0.15, 0.018), (x0 + 0.07, 0.05, 0.026), bills)   # billets
        for i, x0 in enumerate((0.01, 0.035, 0.1, 0.125)):                           # piles de pièces
            box(d, (x0, -0.16 + i * 0.03, 0.018), (x0 + 0.02, -0.14 + i * 0.03, 0.03), chrome)
        c = objs.setdefault("Corps", bmesh.new())
        box(c, (-0.185, -0.2142, 0.015), (0.185, -0.2141, 0.07), shade)              # ouverture (cachée tiroir fermé)
    return name, {"Tiroir": Vector((0.0, -0.214, 0.0))}, None, extra


def rules_tap():
    def name(p):
        if p.mat in ("M_Poignee_Noire", "M_Laiton"): return "Levier_D" if p.center.x > 0 else "Levier_G"
        return None
    return name, {"Levier_D": Vector((0.1, -0.082, 0.36)), "Levier_G": Vector((-0.1, -0.082, 0.36))}, None, None


def rules_coffee():
    def name(p):
        if p.mat == "M_Plastique_Noir" and p.min.z > 0.39: return "Bouton_D" if p.center.x > 0 else "Bouton_G"
        return None
    return name, {}, None, None


def rules_ice():
    def name(p):
        if p.mat == "M_Inox_Brosse" and p.max.y < -0.26 and p.min.z > 0.46: return "Trappe"
        if p.mat == "M_Chrome" and p.min.y < -0.29 and 0.735 < p.min.z < 0.75: return "Trappe"
        return None
    def extra(objs):
        shade = extra_mat("M_Ombre", (0.01, 0.01, 0.01, 1))
        box(objs.setdefault("Corps", bmesh.new()), (-0.2, -0.2756, 0.48), (0.2, -0.2755, 0.76), shade)
    # Charnière en bas (poignée en haut) : la trappe bascule vers l'avant.
    return name, {"Trappe": Vector((0.0, -0.287, 0.47))}, None, extra


def rules_vending():
    def name(p):
        if p.mat == "M_Inox_Brosse" and 0.26 < p.min.x and p.max.x < 0.38 and 1.19 < p.min.z and p.max.z < 1.32:
            row = round((1.30 - p.center.z) / 0.045); col = round((p.center.x - 0.28) / 0.04)
            return f"Touche_{row * 3 + col + 1}"
        return None
    return name, {}, None, None


RULES = {"Refrigerateur_Vitrine": rules_fridge, "Caisse_Enregistreuse": rules_register, "Tireuse_Biere": rules_tap,
         "Machine_Cafe": rules_coffee, "Machine_Glacons": rules_ice, "Distributeur_Boissons": rules_vending}
name_of, pivots, mutate, extra = RULES[MODEL]()

# ---------------------------------------------------------------- découpe
bm = bmesh.new(); bm.from_mesh(me); bm.transform(src.matrix_world)
groups = {}
for p in parts_of(bm):
    if mutate is not None and not mutate(p, bm): continue
    groups.setdefault(name_of(p) or "Corps", []).append(p)

objs = {}
for n, ps in groups.items():
    keep = {f for p in ps for f in p.faces}
    b = bm.copy()
    b.faces.ensure_lookup_table()
    idx = {f.index for f in keep}
    bmesh.ops.delete(b, geom=[f for f in b.faces if f.index not in idx], context='FACES')
    objs[n] = b
if extra is not None: extra(objs)

made = []
for n, b in objs.items():
    if n in pivots: piv = pivots[n]
    elif n == "Corps": piv = Vector((0, 0, 0))
    else:
        vs = [v.co for v in b.verts]
        piv = Vector(((min(v.x for v in vs) + max(v.x for v in vs)) / 2, (min(v.y for v in vs) + max(v.y for v in vs)) / 2, (min(v.z for v in vs) + max(v.z for v in vs)) / 2))
    b.transform(Matrix.Translation(-piv))
    m = bpy.data.meshes.new(n); b.to_mesh(m); b.free()
    for mt in mats: m.materials.append(mt)
    o = bpy.data.objects.new(n, m); o.location = piv
    bpy.context.scene.collection.objects.link(o); made.append(o)
bm.free()
src.hide_set(True); src.hide_render = True

report = []
fue.export_group(bpy.context, made, MODEL, ASSETS, SUB, False, report)
mf = os.path.join(ASSETS, SUB, MODEL, fue.MANIFEST_NAME)
m = json.load(open(mf, encoding="utf-8")); m["place"] = False
json.dump(m, open(mf, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
print("[EXPORT]", MODEL, sorted(o.name for o in made), report)
