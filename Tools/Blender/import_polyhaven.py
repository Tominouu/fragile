# Importe des modèles Poly Haven (licence CC0 : libres, sans attribution) dans le projet Unity.
# Pour chaque identifiant : téléchargement du glTF 1k (+ textures), import dans Blender, export avec l'addon
# « Fragile - Export vers Unity » vers Assets/Bar/Models/Mobilier/<id> (« place »: false : posé par Fragile > Meubler le Bar).
#
# Usage : blender -b --factory-startup --python Tools/Blender/import_polyhaven.py -- <id> [<id> ...]
# Puis dans Unity : Fragile > Meubler le Bar, puis Fragile > Éclairer le Bar.
import bpy, sys, os, json, tempfile, urllib.request, importlib.util

HERE = os.path.dirname(os.path.abspath(__file__))
spec = importlib.util.spec_from_file_location("fue", os.path.join(HERE, "fragile_unity_export.py"))
fue = importlib.util.module_from_spec(spec); spec.loader.exec_module(fue)
ASSETS = os.path.abspath(os.path.join(HERE, "..", "..", "Assets"))
SUB = "Bar/Models/Mobilier"
UA = {"User-Agent": "fragile-asset-import"}


def fetch(url, path=None):
    req = urllib.request.Request(url, headers=UA)
    with urllib.request.urlopen(req, timeout=60) as r:
        data = r.read()
    if path is None: return data
    os.makedirs(os.path.dirname(path), exist_ok=True)
    with open(path, "wb") as f: f.write(data)


def import_one(asset_id, workdir):
    info = json.loads(fetch(f"https://api.polyhaven.com/files/{asset_id}"))
    g = info["gltf"]["1k"]["gltf"]
    folder = os.path.join(workdir, asset_id)
    main = os.path.join(folder, os.path.basename(g["url"]))
    fetch(g["url"], main)
    for rel, inc in g.get("include", {}).items():
        fetch(inc["url"], os.path.join(folder, rel))

    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=main)
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    # Transformations appliquées : l'objet exporté est à l'origine, à l'échelle réelle.
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes: o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
    bpy.ops.object.transform_apply(location=False, rotation=True, scale=True)

    report = []
    fue.export_group(bpy.context, meshes, asset_id, ASSETS, SUB, False, report)
    mf = os.path.join(ASSETS, SUB, fue.safe_name(asset_id), fue.MANIFEST_NAME)
    m = json.load(open(mf, encoding="utf-8")); m["place"] = False; m["source"] = f"https://polyhaven.com/a/{asset_id} (CC0)"
    json.dump(m, open(mf, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
    print("[POLYHAVEN]", asset_id, report)


ids = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
with tempfile.TemporaryDirectory(prefix="polyhaven_") as work:
    for i in ids:
        try: import_one(i, work)
        except Exception as e: print("[POLYHAVEN] ÉCHEC", i, e)
