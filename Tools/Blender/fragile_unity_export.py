bl_info = {
    "name": "Fragile - Export vers Unity",
    "author": "Fragile",
    "version": (1, 0, 0),
    "blender": (4, 2, 0),
    "location": "Vue 3D > panneau latéral (N) > Fragile",
    "description": "Exporte les objets sélectionnés vers le projet Unity Fragile : FBX bien réglé, bonne carte UV, textures extraites et description des matériaux.",
    "category": "Import-Export",
}

import json
import os
import re

import bpy
from bpy.props import EnumProperty, StringProperty, BoolProperty

MANIFEST_NAME = "fragile_export.json"

# Mots-clés pour reconnaître le rôle d'une texture d'après son nom.
KINDS = [
    ("normal", ("normal", "nor_gl", "nor_dx", "_nor", "nrm")),
    ("roughness", ("rough",)),
    ("metallic", ("metal",)),
    ("baseColor", ("basecolor", "base_color", "diff", "albedo", "color", "col")),
]
SKIP = ("ao", "ambientocclusion", "occlusion", "height", "disp", "emiss", "alpha", "opacity", "bump", "spec")


# ----------------------------------------------------------------------------- utilitaires

def safe_name(name):
    return re.sub(r"[^A-Za-z0-9_\-]+", "_", name).strip("_") or "Objet"


def classify(image_name):
    n = image_name.lower()
    for k, words in KINDS:
        if any(w in n for w in words):
            if k == "baseColor" and any(s in n for s in SKIP):
                continue
            return k
    return None


def images_in(tree, depth=0):
    """Liste de (image, rôle d'après les branchements ou None)."""
    out = []
    for node in tree.nodes:
        if node.type == "TEX_IMAGE" and node.image:
            out.append((node.image, role_from_links(node)))
        elif node.type == "GROUP" and node.node_tree and depth < 5:
            out += images_in(node.node_tree, depth + 1)
    return out


SOCKET_ROLES = {"Base Color": "baseColor", "Roughness": "roughness", "Metallic": "metallic", "Normal": "normal"}


def role_from_links(node, depth=0):
    """Suit les sorties d'un nœud jusqu'au Principled BSDF (à travers mélanges, couleurs, Normal Map…)."""
    if depth > 8:
        return None
    for out in node.outputs:
        for link in out.links:
            to = link.to_node
            if to.type == "BSDF_PRINCIPLED":
                role = SOCKET_ROLES.get(link.to_socket.name)
                if role:
                    return role
            elif to.type == "NORMAL_MAP":
                return "normal"
            elif to.type in ("BUMP", "DISPLACEMENT", "OUTPUT_MATERIAL"):
                continue
            else:
                r = role_from_links(to, depth + 1)
                if r:
                    return r
    return None


def first_mapping_scale(tree, depth=0):
    for node in tree.nodes:
        if node.type == "MAPPING" and not node.inputs["Scale"].is_linked:
            s = node.inputs["Scale"].default_value
            return [round(s[0], 4), round(s[1], 4)]
        if node.type == "GROUP" and node.node_tree and depth < 5:
            r = first_mapping_scale(node.node_tree, depth + 1)
            if r:
                return r
    return None


def guess_constants(mat):
    """Couleur / métal / brillance pour les matériaux sans texture (procéduraux)."""
    color, metallic, smoothness = [0.8, 0.8, 0.8, 1.0], 0.0, 0.5
    tree = mat.node_tree
    bsdf = next((n for n in tree.nodes if n.type == "BSDF_PRINCIPLED"), None)
    if bsdf:
        bc = bsdf.inputs["Base Color"]
        if not bc.is_linked:
            color = list(bc.default_value)
        if not bsdf.inputs["Metallic"].is_linked:
            metallic = bsdf.inputs["Metallic"].default_value
        if not bsdf.inputs["Roughness"].is_linked:
            smoothness = 1.0 - bsdf.inputs["Roughness"].default_value
    else:
        # Matériau en groupe de nœuds : première entrée couleur non branchée.
        for node in tree.nodes:
            if node.type == "GROUP":
                for i in node.inputs:
                    if i.type == "RGBA" and not i.is_linked:
                        color = list(i.default_value)
                        break
                for i in node.inputs:
                    if "rough" in i.name.lower() and i.type == "VALUE" and not i.is_linked:
                        smoothness = 1.0 - i.default_value
                        break
    if any(w in mat.name.lower() for w in ("gold", "or ", "metal", "copper", "cuivre", "steel", "acier", "chrome", "silver", "argent")):
        metallic = 1.0
    # Les couleurs des nœuds Blender sont linéaires ; Unity attend des couleurs sRGB dans l'Inspector.
    color = [linear_to_srgb(c) for c in color[:3]] + [color[3]]
    return color, metallic, max(0.0, min(1.0, smoothness))


def linear_to_srgb(c):
    c = max(0.0, min(1.0, c))
    return 12.92 * c if c <= 0.0031308 else 1.055 * (c ** (1 / 2.4)) - 0.055


UNITY_READS = (b"\x89PNG", b"\xff\xd8")  # PNG, JPEG (+ TGA/PSD/EXR gérés par le cas par défaut)


def write_png_copy(img, path):
    """Réécrit l'image en PNG via Blender (pour les formats qu'Unity ne lit pas : webp, avif…)."""
    w, h = img.size
    if w == 0 or h == 0:
        return False
    tmp = bpy.data.images.new("__fragile_png", w, h, alpha=True)
    try:
        tmp.pixels.foreach_set(_pixels(img))
        tmp.filepath_raw = path
        tmp.file_format = "PNG"
        tmp.save()
        return True
    finally:
        bpy.data.images.remove(tmp)


def _pixels(img):
    import array
    buf = array.array("f", [0.0]) * (img.size[0] * img.size[1] * 4)
    img.pixels.foreach_get(buf)
    return buf


def write_image(img, folder, base):
    """Écrit l'image telle quelle si Unity sait la lire, sinon la convertit en PNG ; renvoie le nom de fichier ou None."""
    data = None
    if img.packed_file:
        data = img.packed_file.data
    else:
        path = bpy.path.abspath(img.filepath)
        if os.path.isfile(path):
            with open(path, "rb") as f:
                data = f.read()
    if data and not (data[:4] == UNITY_READS[0] or data[:2] == UNITY_READS[1] or img.file_format in ("TARGA", "OPEN_EXR", "BMP")):
        # Format inconnu d'Unity (webp, avif…) : conversion en PNG.
        name = base + ".png"
        return name if write_png_copy(img, os.path.join(folder, name)) else None
    if data:
        ext = ".png" if data[:4] == b"\x89PNG" else ".jpg" if data[:2] == b"\xff\xd8" else ".tga" if img.file_format == "TARGA" else ".exr" if img.file_format == "OPEN_EXR" else ".bmp"
        name = base + ext
        with open(os.path.join(folder, name), "wb") as f:
            f.write(data)
        return name
    # Image générée ou introuvable : on l'enregistre depuis Blender.
    try:
        name = base + ".png"
        img.save(filepath=os.path.join(folder, name))
        return name
    except Exception:
        return None


def used_uv_name(obj):
    """Carte UV réellement utilisée par les matériaux : nœud UV Map explicite, sinon celle du rendu."""
    for slot in obj.material_slots:
        m = slot.material
        if m and m.use_nodes:
            for node in m.node_tree.nodes:
                if node.type == "UVMAP" and node.uv_map:
                    return node.uv_map
    for layer in obj.data.uv_layers:
        if layer.active_render:
            return layer.name
    return obj.data.uv_layers[0].name if obj.data.uv_layers else None


# ----------------------------------------------------------------------------- export

def export_group(context, objects, name, assets_dir, subfolder, collider, report):
    folder = os.path.join(assets_dir, subfolder, safe_name(name))
    os.makedirs(folder, exist_ok=True)
    fbx_name = safe_name(name) + ".fbx"

    # Copies temporaires : une seule carte UV (la bonne), pour qu'Unity la lise en premier.
    temps, renamed = [], []
    try:
        for obj in objects:
            tmp = obj.copy()
            tmp.data = obj.data.copy()
            context.scene.collection.objects.link(tmp)
            keep = used_uv_name(obj)
            if keep:
                for layer in [l for l in tmp.data.uv_layers if l.name != keep]:
                    tmp.data.uv_layers.remove(layer)
            # Le FBX garde le nom de l'objet d'origine.
            original = obj.name
            obj.name = original + "__fragile_tmp"
            renamed.append((obj, original))
            tmp.name = original
            temps.append(tmp)

        bpy.ops.object.select_all(action="DESELECT")
        for t in temps:
            t.select_set(True)
        context.view_layer.objects.active = temps[0]
        bpy.ops.export_scene.fbx(
            filepath=os.path.join(folder, fbx_name),
            use_selection=True, object_types={"MESH"}, use_mesh_modifiers=True,
            apply_scale_options="FBX_SCALE_ALL", axis_forward="-Z", axis_up="Y",
            bake_space_transform=True, mesh_smooth_type="FACE", use_tspace=False,
            path_mode="STRIP", embed_textures=False, add_leaf_bones=False, bake_anim=False,
        )
    finally:
        for t in temps:
            data = t.data
            bpy.data.objects.remove(t, do_unlink=True)
            if data.users == 0:
                bpy.data.meshes.remove(data)
        for obj, original in renamed:
            obj.name = original

    # Matériaux : textures extraites + valeurs de secours.
    materials, seen = [], set()
    for obj in objects:
        for slot in obj.material_slots:
            m = slot.material
            if not m or m.name in seen:
                continue
            seen.add(m.name)
            entry = {"name": m.name, "baseColor": "", "normal": "", "roughness": "", "metallicMap": "",
                     "tiling": [1.0, 1.0], "color": [1, 1, 1, 1], "metallic": 0.0, "smoothness": 0.5}
            color, metallic, smoothness = guess_constants(m) if m.use_nodes else ([0.8, 0.8, 0.8, 1], 0.0, 0.5)
            entry.update({"color": color, "metallic": metallic, "smoothness": smoothness})
            if m.use_nodes:
                done = set()
                found = images_in(m.node_tree)
                # Rôle : d'abord les branchements, sinon le nom du fichier.
                found = [(img, role or classify(img.name) or classify(os.path.basename(img.filepath))) for img, role in found]
                # Une seule image non reconnue et pas de couleur : c'est la couleur.
                if not any(k == "baseColor" for _, k in found):
                    unknown = [img for img, k in found if k is None]
                    if len(unknown) == 1:
                        found = [(img, "baseColor" if img is unknown[0] else k) for img, k in found]
                for img, kind in found:
                    if not kind or kind in done:
                        continue
                    key = {"baseColor": "baseColor", "normal": "normal", "roughness": "roughness", "metallic": "metallicMap"}[kind]
                    fname = write_image(img, folder, safe_name(m.name) + "_" + kind)
                    if fname:
                        entry[key] = fname
                        done.add(kind)
                has_bsdf = any(n.type == "BSDF_PRINCIPLED" for n in m.node_tree.nodes)
                if done and not has_bsdf:
                    # Matériau texturé en groupe de nœuds : les devinettes (nom "copper", entrées de groupes)
                    # ne sont pas fiables, on prend des valeurs neutres. Avec un Principled visible, on garde ses réglages.
                    entry["metallic"] = 0.0
                    entry["smoothness"] = 0.5
                if entry["baseColor"]:
                    entry["color"] = [1, 1, 1, 1]  # la texture porte la couleur
                entry["tiling"] = first_mapping_scale(m.node_tree) or [1.0, 1.0]
            materials.append(entry)

    manifest = {"objectName": name, "fbx": fbx_name, "collider": collider, "materials": materials}
    with open(os.path.join(folder, MANIFEST_NAME), "w", encoding="utf-8") as f:
        json.dump(manifest, f, ensure_ascii=False, indent=2)
    report.append(f"{name} : {len(materials)} matériau(x) -> {os.path.join(subfolder, safe_name(name))}")


class FRAGILE_OT_export_unity(bpy.types.Operator):
    bl_idname = "fragile.export_unity"
    bl_label = "Exporter vers Fragile"
    bl_description = "Exporte les objets sélectionnés vers le projet Unity (FBX, textures, matériaux)"
    bl_options = {"REGISTER"}

    def execute(self, context):
        props = context.scene.fragile_export
        assets_dir = bpy.path.abspath(props.assets_dir)
        if not os.path.isdir(assets_dir):
            self.report({"ERROR"}, f"Dossier Assets introuvable : {assets_dir}")
            return {"CANCELLED"}
        meshes = [o for o in context.selected_objects if o.type == "MESH"]
        if not meshes:
            self.report({"ERROR"}, "Sélectionne au moins un objet maillage.")
            return {"CANCELLED"}
        if context.object and context.object.mode != "OBJECT":
            bpy.ops.object.mode_set(mode="OBJECT")

        prev_sel, prev_active = list(context.selected_objects), context.view_layer.objects.active
        report = []
        try:
            if props.mode == "SEPARATE":
                for o in meshes:
                    export_group(context, [o], o.name, assets_dir, props.subfolder, props.collider, report)
            else:
                name = (context.view_layer.objects.active or meshes[0]).name
                export_group(context, meshes, name, assets_dir, props.subfolder, props.collider, report)
        finally:
            bpy.ops.object.select_all(action="DESELECT")
            for o in prev_sel:
                if o.name in bpy.data.objects:
                    o.select_set(True)
            context.view_layer.objects.active = prev_active

        for line in report:
            print("[Fragile]", line)
        self.report({"INFO"}, f"Exporté : {len(report)} modèle(s). Dans Unity : Fragile > Importer les modèles Blender")
        return {"FINISHED"}


class FragileExportProps(bpy.types.PropertyGroup):
    assets_dir: StringProperty(
        name="Dossier Assets",
        description="Dossier Assets du projet Unity",
        subtype="DIR_PATH",
        default=r"C:\Users\Tom\MMI\DEV\fragile\fragile\Assets",
    )
    subfolder: StringProperty(name="Sous-dossier", default="Bar/Models")
    mode: EnumProperty(
        name="Mode",
        items=[("SEPARATE", "Un modèle par objet", "Chaque objet sélectionné devient un modèle Unity"),
               ("COMBINED", "Tout en un modèle", "Toute la sélection dans un seul FBX, nommé d'après l'objet actif")],
        default="SEPARATE",
    )
    collider: BoolProperty(name="Collider", description="Ajouter un collider dans Unity (on ne traverse pas l'objet)", default=True)


class FRAGILE_PT_export(bpy.types.Panel):
    bl_label = "Export Unity"
    bl_idname = "FRAGILE_PT_export"
    bl_space_type = "VIEW_3D"
    bl_region_type = "UI"
    bl_category = "Fragile"

    def draw(self, context):
        props = context.scene.fragile_export
        col = self.layout.column()
        col.prop(props, "assets_dir")
        col.prop(props, "subfolder")
        col.prop(props, "mode", text="")
        col.prop(props, "collider")
        n = len([o for o in context.selected_objects if o.type == "MESH"])
        row = col.row()
        row.scale_y = 1.6
        row.enabled = n > 0
        row.operator(FRAGILE_OT_export_unity.bl_idname, text=f"Exporter vers Fragile ({n})", icon="EXPORT")


classes = (FragileExportProps, FRAGILE_OT_export_unity, FRAGILE_PT_export)


def register():
    for c in classes:
        bpy.utils.register_class(c)
    bpy.types.Scene.fragile_export = bpy.props.PointerProperty(type=FragileExportProps)


def unregister():
    del bpy.types.Scene.fragile_export
    for c in reversed(classes):
        bpy.utils.unregister_class(c)


if __name__ == "__main__":
    register()
