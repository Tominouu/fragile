"""
Fragile — clients du bar pour le Trailer, générés avec MPFB (MakeHuman pour Blender).

Tous les assets utilisés sont CC0 (packs MakeHuman : system assets, skins01/02, shirts01, pants01, shoes01, hair01,
eyebrows01, eyelashes01, dress01, suits01, skirts01, glasses01, hats01). Voir Assets/Trailer/Clients/CREDITS.txt.

Prérequis : extension MPFB installée dans Blender (≥ 4.2) + ces packs installés (MPFB > Apply assets > Library settings
> Load pack from zip, ou Tools/Blender/README).

Utilisation (depuis la racine du projet) :
    blender -b --python Tools/Blender/generate_clients.py -- Assets/Trailer/Clients [nom1 nom2 ...]

Chaque client : corps (proxy allégé), peau, yeux, sourcils, cils, cheveux, vêtements, rig « mixamo_unity »
(reconnu comme Humanoid par Unity). Exporté en FBX (textures copiées à côté). Budget Quest : ~12 k triangles.
"""
import os
import shutil
import sys
import tempfile
import bpy

from bl_ext.user_default.mpfb.services.humanservice import HumanService
from bl_ext.user_default.mpfb.services.objectservice import ObjectService

TRI_BUDGET = 12000
TEX_MAX = 1024          # textures réduites (Quest, poids du dépôt)
TMP_TEX = tempfile.mkdtemp(prefix="fragile_clients_")

R = dict  # raccourci

# phénotype : gender 0 = femme, 1 = homme ; age 0.5 = 25 ans, 0.75 ≈ 50 ans, 0.9 ≈ 70 ans ; race = mélange.
CLIENTS = [
    R(name="Client_01", gender=1.0, age=0.50, muscle=0.55, weight=0.50, height=0.55, race=(0.0, 1.0, 0.0),
      skin="young_caucasian_male", hair="short02", brows="eyebrow001",
      clothes=["male_casualsuit01", "shoes02"]),
    R(name="Client_02", gender=1.0, age=0.68, muscle=0.60, weight=0.60, height=0.60, race=(1.0, 0.0, 0.0),
      skin="middleage_african_male", hair="short04", brows="eyebrow002",
      clothes=["male_casualsuit03", "shoes01", "jujube_newsboy_cap"]),
    R(name="Client_03", gender=1.0, age=0.72, muscle=0.40, weight=0.80, height=0.45, race=(0.0, 1.0, 0.0),
      skin="middleage_caucasian_male", hair="short01", brows="eyebrow003",
      clothes=["male_casualsuit04", "shoes03"]),
    R(name="Client_04", gender=1.0, age=0.90, muscle=0.35, weight=0.55, height=0.40, race=(0.0, 1.0, 0.0),
      skin="old_caucasian_male", hair="short03", brows="eyebrow004",
      clothes=["male_casualsuit05", "shoes04", "kwnet_at_optical_glasses"]),
    R(name="Client_05", gender=1.0, age=0.48, muscle=0.50, weight=0.40, height=0.50, race=(0.0, 0.0, 1.0),
      skin="young_asian_male", hair="culturalibre_hair_02", brows="eyebrow005",
      clothes=["elvs_crude_t-shirt_male", "cortu_cargo_pants", "shoes06"]),
    R(name="Client_06", gender=1.0, age=0.62, muscle=0.55, weight=0.55, height=0.65, race=(0.0, 1.0, 0.0),
      skin="toigo_light_skin_male_freckles", hair="culturalibre_hair_05", brows="eyebrow006",
      clothes=["toigo_male_suit_tie_and_jacket", "shoes05"]),
    R(name="Client_07", gender=0.0, age=0.48, muscle=0.45, weight=0.45, height=0.50, race=(0.0, 1.0, 0.0),
      skin="young_caucasian_female", hair="long01", brows="eyebrow008",
      clothes=["female_casualsuit01", "toigo_ankle_boots_female"]),
    R(name="Client_08", gender=0.0, age=0.50, muscle=0.50, weight=0.50, height=0.55, race=(1.0, 0.0, 0.0),
      skin="young_african_female", hair="rehmanpolanski_hair_bun_brown", brows="eyebrow009",
      clothes=["joepal_crude_t-shirt_female", "frankyaye_mini_skirt_01", "toigo_stiletto_booties"]),
    R(name="Client_09", gender=0.0, age=0.66, muscle=0.40, weight=0.50, height=0.40, race=(0.0, 0.0, 1.0),
      skin="middleage_asian_female", hair="toigo_inverted_bob_with_bangs", brows="eyebrow010",
      clothes=["female_casualsuit02", "toigo_flats"]),
    R(name="Client_10", gender=0.0, age=0.47, muscle=0.45, weight=0.40, height=0.55, race=(0.0, 1.0, 0.0),
      skin="toigo_light_skin_female_freckles", hair="toigo_blunt_bob", brows="eyebrow011",
      clothes=["toigo_halter_dress_knee_length", "toigo_ballet_flats"]),
    R(name="Client_11", gender=0.0, age=0.70, muscle=0.40, weight=0.65, height=0.45, race=(0.0, 1.0, 0.0),
      skin="middleage_caucasian_female", hair="ponytail01", brows="eyebrow012",
      clothes=["toigo_female_suit", "toigo_ankle_boots_female"]),
    R(name="Client_12", gender=0.0, age=0.88, muscle=0.35, weight=0.55, height=0.35, race=(0.0, 1.0, 0.0),
      skin="old_caucasian_female", hair="toigo_curled_under_bob_with_bangs", brows="eyebrow010",
      clothes=["female_elegantsuit01", "toigo_flats", "kwnet_at_optical_glasses"]),
    # Le barman.
    R(name="Barman", gender=1.0, age=0.56, muscle=0.60, weight=0.50, height=0.60, race=(0.3, 0.7, 0.0),
      skin="young_caucasian_male2", hair="short04", brows="eyebrow007",
      clothes=["namuhekam_male_polo_shirt", "toigo_wool_pants", "toigo_ankle_boots_male"]),
]


def human_info(c):
    gender = c["gender"]
    return {
        "name": c["name"],
        "phenotype": {
            "gender": gender, "age": c["age"], "muscle": c["muscle"], "weight": c["weight"],
            "proportions": 0.5, "height": c["height"], "cupsize": 0.5, "firmness": 0.5,
            "race": {"african": c["race"][0], "caucasian": c["race"][1], "asian": c["race"][2]},
        },
        "rig": "mixamo_unity",
        "eyes": "high-poly.mhclo",
        "eyebrows": c["brows"] + ".mhclo",
        "eyelashes": ("eyelashes01" if gender > 0.5 else "eyelashes02") + ".mhclo",
        "tongue": "", "teeth": "",
        "hair": c["hair"] + ".mhclo",
        "proxy": ("male_generic" if gender > 0.5 else "female_generic") + ".proxy",
        "targets": [],
        "clothes": [x + ".mhclo" for x in c["clothes"]],
        "skin_mhmat": c["skin"] + ".mhmat",
        "skin_material_type": "GAMEENGINE",
        "eyes_material_type": "GAMEENGINE",
        "skin_material_settings": {}, "eyes_material_settings": {},
        "expressions": [], "alternative_materials": {},
    }


SETTINGS = {
    # detailed_helpers DOIT rester vrai : MPFB place chaque os d'après les groupes « joint-… » des aides.
    # Sans eux, le squelette est posé ~90 cm trop bas et les membres s'étirent une fois animés dans Unity.
    "mask_helpers": True, "detailed_helpers": True, "extra_vertex_groups": True, "feet_on_ground": True,
    "scale": 0.1, "subdiv_levels": 0, "load_clothes": True,
    "override_skin_model": "GAMEENGINE", "override_clothes_model": "GAMEENGINE", "override_eyes_model": "GAMEENGINE",
    "override_rig": "PRESET", "material_instances": "NEVER",
}


def clear_scene():
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.armatures, bpy.data.materials, bpy.data.images, bpy.data.node_groups):
        for block in list(coll):
            if block.users == 0:
                coll.remove(block)


def tris(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def apply_modifier(obj, name):
    # En mode batch, les opérateurs n'ont pas de contexte : on le leur donne explicitement.
    with bpy.context.temp_override(object=obj, active_object=obj, selected_objects=[obj], selected_editable_objects=[obj]):
        bpy.ops.object.modifier_apply(modifier=name)


def flatten(obj):
    """Fige formes (morphologie MakeHuman) et masques dans le maillage ; garde seulement le modificateur Armature."""
    if obj.data.shape_keys:
        obj.shape_key_add(name="__mix", from_mix=True)
        keys = obj.data.shape_keys.key_blocks
        for kb in list(keys):
            if kb.name != "__mix":
                obj.shape_key_remove(kb)
        obj.shape_key_remove(obj.data.shape_keys.key_blocks["__mix"])
    for mod in list(obj.modifiers):
        if mod.type == "ARMATURE":
            continue
        try:
            apply_modifier(obj, mod.name)
        except RuntimeError as e:
            print("[Fragile] modificateur non appliqué", obj.name, mod.name, e)
            obj.modifiers.remove(mod)


CAPS = [  # (mots-clés, plafond)
    (("eye", "low-poly", "high-poly", "lash", "brow"), 10 ** 6),
    (("generic", "proxy", "body"), 6500),
    (("shoe", "boot", "flats", "booties", "sneaker"), 1200),
    (("glasses", "cap", "hat", "fedora"), 600),
]
HAIR_CAP, CLOTHES_CAP = 2600, 3000


def cap(name, c):
    n = name.lower()
    if c["hair"].lower() in n:
        return HAIR_CAP
    for keys, value in CAPS:
        if any(k in n for k in keys):
            return value
    return CLOTHES_CAP


def merge_group(obj, src, dst):
    """Reporte les poids d'un os sur un autre (ex. mâchoire -> tête : les animations ne la pilotent pas,
    et Unity la tordait en Humanoid). La bouche reste fermée et en place."""
    gs = obj.vertex_groups.get(src)
    if gs is None:
        return
    gd = obj.vertex_groups.get(dst) or obj.vertex_groups.new(name=dst)
    for v in obj.data.vertices:
        for g in v.groups:
            if g.group == gs.index and g.weight > 0.0:
                gd.add([v.index], g.weight, "ADD")
    obj.vertex_groups.remove(gs)


def decimate(obj, budget):
    n = tris(obj)
    if n <= budget:
        return
    mod = obj.modifiers.new("Allègement", "DECIMATE")
    mod.ratio = max(0.01, budget / n)
    # Avant l'armature, pour l'appliquer seul.
    with bpy.context.temp_override(object=obj, active_object=obj, selected_objects=[obj], selected_editable_objects=[obj]):
        bpy.ops.object.modifier_move_to_index(modifier=mod.name, index=0)
    apply_modifier(obj, mod.name)
    print(f"[Fragile]   allègement {obj.name} : {n} -> {tris(obj)} triangles")


def shrink_textures():
    """Réduit les textures à TEX_MAX et les réenregistre (l'export COPY copie le fichier sur disque)."""
    for img in bpy.data.images:
        if img.source != "FILE" or not img.filepath:
            continue
        w, h = img.size
        name = os.path.basename(bpy.path.abspath(img.filepath))
        dst = os.path.join(TMP_TEX, name)
        if not os.path.exists(dst):
            if max(w, h) > TEX_MAX:
                img.scale(min(w, TEX_MAX), min(h, TEX_MAX))
                img.filepath_raw = dst
                img.file_format = "PNG"
                img.save()
            else:
                shutil.copyfile(bpy.path.abspath(img.filepath), dst)
        img.filepath = dst
        img.reload()


def gather_textures(out_dir, name):
    """Textures de tous les clients dans un seul dossier « Textures » (Unity le cherche à côté des modèles)."""
    fbm = os.path.join(out_dir, name + ".fbm")
    tex = os.path.join(out_dir, "Textures")
    os.makedirs(tex, exist_ok=True)
    if os.path.isdir(fbm):
        for f in os.listdir(fbm):
            shutil.move(os.path.join(fbm, f), os.path.join(tex, f))
        shutil.rmtree(fbm, ignore_errors=True)


def validate(rig, meshes, name):
    """Refuse un client dont le squelette ne colle pas à la peau (sinon, animé dans Unity, les membres « s'étirent »).
    1) Hanches à 45-65 % de la taille. 2) Coude plié à 70° : la peau de la main reste près de l'os de la main."""
    top = max((o.matrix_world @ v.co).z for o in meshes for v in o.data.vertices)
    hips = rig.matrix_world @ rig.data.bones["mixamorig:Hips"].head_local
    ratio = hips.z / max(top, 1e-3)
    if not 0.45 <= ratio <= 0.65:
        raise RuntimeError(f"{name} : squelette décalé (hanches à {hips.z:.2f} m pour {top:.2f} m)")

    pb = rig.pose.bones["mixamorig:LeftForeArm"]
    old = pb.rotation_mode, tuple(pb.rotation_euler)
    pb.rotation_mode = "XYZ"
    pb.rotation_euler = (0.0, 0.0, 1.22)
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get()
    hand = rig.matrix_world @ rig.pose.bones["mixamorig:LeftHand"].head
    worst = 0.0
    for o in meshes:
        gi = o.vertex_groups.get("mixamorig:LeftHand")
        if gi is None:
            continue
        ev = o.evaluated_get(dg).to_mesh()
        for v, ve in zip(o.data.vertices, ev.vertices):
            if any(g.group == gi.index and g.weight > 0.8 for g in v.groups):
                worst = max(worst, ((o.matrix_world @ ve.co) - hand).length)
        o.evaluated_get(dg).to_mesh_clear()
    pb.rotation_euler = old[1]
    pb.rotation_mode = old[0]
    bpy.context.view_layer.update()
    if worst > 0.3:
        raise RuntimeError(f"{name} : la main ne suit pas son os en pose ({worst:.2f} m)")
    for bone in ("mixamorig:Jaw", "mixamorig:LeftEye", "mixamorig:RightEye"):
        if any(o.vertex_groups.get(bone) for o in meshes):
            raise RuntimeError(f"{name} : des sommets dépendent encore de {bone}")
    # Personnage complet : corps, yeux, sourcils, cils, cheveux, et une texture sur chaque matériau.
    parts = " ".join(o.name.lower() for o in meshes)
    for need, label in (("high-poly", "yeux"), ("eyebrow", "sourcils"), ("eyelash", "cils"), (CURRENT_HAIR[0].lower(), "cheveux"), ("generic", "corps")):
        if need not in parts:
            raise RuntimeError(f"{name} : il manque {label}")
    for o in meshes:
        for m in o.data.materials:
            has_tex = m is not None and m.use_nodes and any(n.type == "TEX_IMAGE" and n.image for n in m.node_tree.nodes)
            if not has_tex:
                raise RuntimeError(f"{name} : matériau sans texture sur {o.name}")
    print(f"[Fragile]   squelette OK : hanches à {hips.z:.2f} m ({ratio:.0%} de la taille), main à {worst:.2f} m de son os en pose")


CURRENT_HAIR = [""]


def build(c, out_dir):
    clear_scene()
    CURRENT_HAIR[0] = c["hair"]
    basemesh = HumanService.deserialize_from_dict(human_info(c), dict(SETTINGS))
    rig = basemesh.parent if basemesh.parent and basemesh.parent.type == "ARMATURE" else None
    if rig is None:
        raise RuntimeError("Pas de rig pour " + c["name"])
    rig.name = c["name"]

    proxy = ObjectService.find_object_of_type_amongst_nearest_relatives(basemesh, "Proxymeshes")
    meshes = [o for o in bpy.data.objects if o.type == "MESH"]
    for o in meshes:
        flatten(o)
    # Le maillage de base est entièrement masqué par le proxy (corps allégé) : on l'enlève.
    if proxy is not None:
        bpy.data.objects.remove(basemesh, do_unlink=True)
        meshes = [o for o in meshes if o != basemesh]
    else:
        print("[Fragile] pas de proxy pour", c["name"], ": corps complet")

    # Plafond par pièce (certaines chaussures dépassent 50 k triangles) : le corps et le visage gardent leur détail.
    for o in meshes:
        decimate(o, cap(o.name, c))
    for o in meshes:
        merge_group(o, "mixamorig:Jaw", "mixamorig:Head")
        # Globes oculaires fixés à la tête : l'Humanoid de Unity les révulsait (plus de pupilles visibles).
        merge_group(o, "mixamorig:LeftEye", "mixamorig:Head")
        merge_group(o, "mixamorig:RightEye", "mixamorig:Head")
    total = sum(tris(o) for o in meshes)

    for o in meshes:
        short = o.name.split(".")[-1] if "." in o.name else o.name
        o.name = c["name"] + "_" + short

    validate(rig, meshes, c["name"])
    shrink_textures()

    bpy.ops.object.select_all(action="DESELECT")
    rig.select_set(True)
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = rig
    path = os.path.join(out_dir, c["name"] + ".fbx")
    bpy.ops.export_scene.fbx(
        filepath=path, use_selection=True, object_types={"ARMATURE", "MESH"},
        # MakeHuman regarde vers -Y dans Blender : avec « Z » en avant, il regarde vers +Z (l'avant de Unity).
        apply_scale_options="FBX_SCALE_UNITS", axis_forward="Z", axis_up="Y",
        use_mesh_modifiers=True, mesh_smooth_type="FACE", add_leaf_bones=False, bake_anim=False,
        use_armature_deform_only=True, primary_bone_axis="Y", secondary_bone_axis="X",
        path_mode="COPY", embed_textures=False,
    )
    gather_textures(out_dir, c["name"])
    print(f"[Fragile] {c['name']} : {total} triangles, {len(meshes)} maillages -> {path}")


def main():
    argv = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    out_dir = os.path.abspath(argv[0] if argv else "Assets/Trailer/Clients")
    only = set(argv[1:])
    os.makedirs(out_dir, exist_ok=True)
    for c in CLIENTS:
        if only and c["name"] not in only:
            continue
        try:
            build(c, out_dir)
        except Exception as e:  # un client raté ne bloque pas les autres
            import traceback
            traceback.print_exc()
            print("[Fragile] ÉCHEC", c["name"], e)


main()
