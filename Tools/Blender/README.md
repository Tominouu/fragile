# Addon Blender « Fragile - Export vers Unity »

Exporte des objets Blender vers le projet Unity en un clic : FBX bien réglé (échelle, axes, modificateurs),
bonne carte UV, textures extraites (même empaquetées dans le .blend) et description des matériaux.

## Installation (une fois)
Blender > Edit > Preferences > Add-ons > flèche en haut à droite > **Install from Disk…** > `fragile_unity_export.py`, puis coche l'addon.

## Utilisation
1. Vue 3D > touche **N** > onglet **Fragile**.
2. Vérifie **Dossier Assets** : le dossier `Assets` de ton clone du projet (sur Mac, il faut le changer).
3. Sélectionne les objets > **Exporter vers Fragile**.
   - *Un modèle par objet* : chaque objet devient un modèle Unity.
   - *Tout en un modèle* : la sélection forme un seul modèle, nommé d'après l'objet actif.
4. Dans Unity : **Fragile > Importer les modèles Blender** (matériaux URP, collider, placement à la position Blender).

Ré-exporter un objet met à jour le modèle et ses matériaux dans Unity sans le dupliquer ni le déplacer.

## Limites
- Matériaux procéduraux (sans texture) : recréés en couleur unie + métal/brillance.
- Verre / transparence : non géré, à régler à la main dans Unity.
- Carte de rugosité : extraite mais pas branchée (URP Lit ne la lit pas seule) ; brillance fixe réglable dans l'Inspector.

## Clients du bar (Trailer) — `generate_clients.py`
Humains réalistes générés avec **MPFB** (MakeHuman pour Blender, extension gratuite de extensions.blender.org).
1. Installer MPFB : Préférences > Get Extensions > « MPFB » (ou `blender -c extension install-file -r user_default --enable add-on-mpfb-vX.zip`).
2. Installer les packs d'assets **CC0** (liste dans `Assets/Trailer/Clients/CREDITS.txt`) : panneau MPFB > Apply assets >
   Library settings > Load pack from zip (zips sur https://static.makehumancommunity.org/assets/assetpacks/index.html).
3. `blender -b --python Tools/Blender/generate_clients.py -- Assets/Trailer/Clients` (ou suivi des noms à refaire).
4. Unity : Fragile > Trailer > Préparer les clients du bar.
Chaque client : proxy de corps allégé, rig « mixamo_unity » (Humanoid), plafonds de triangles par pièce, textures 1024.
