# MEMORY — projet « fragile »

Notes pour le prochain agent / développeur. À lire avant de toucher au projet.

## Le projet
Jeu VR Unity : un **bar** où l'on casse des objets (bouteilles, puffs) à la **batte**, avec des **mains réalistes** à la place des manettes.
- **Unity 6000.6.10f1**, URP 17.6, XR Interaction Toolkit 3.6.1, XR Hands 1.9, OpenXR (Meta Quest + Android XR).
- Cible : **Meta Quest** → tout doit rester léger (peu de particules, pas d'ombres sur les débris, shaders simples).
- Scène principale : **`Assets/Scenes/Bar.unity`**. `Menu.unity` = scène de l'utilisateur ; `SampleScene` / `BasicScene` = template, non utilisées.
- Deux développeurs : Tom (Windows) et un collègue (Mac).

## Règles de travail (IMPORTANT)
- **Commits et PR : aucune mention de Claude** (pas de `Co-Authored-By`, pas de « Generated with »). Auteur = l'utilisateur seul.
- Ne commiter / pousser que sur demande. Langue de travail : **français**.
- Les gros fichiers (fbx, png, jpg, asset de maillage…) passent par **Git LFS** (`.gitattributes`). Sans `git lfs pull`, Unity affiche « Missing Prefab ».
- **Ne pas commiter** de modèles à la licence non vérifiée (un essai « main Tripo » a été abandonné et supprimé pour ça).

## Arborescence utile
```
Assets/
  Scenes/Bar.unity                 scène du jeu
  Bar/
    Batte.prefab                   batte (HandGripGrabInteractable + Breaker)
    Materials/ Textures/           pièce (sol parquet généré, murs, plafond), batte
    Models/Comptoir/               comptoir exporté de Blender (+ textures, matériaux URP)
    Models/puff/                   puff (bouteille céramique Clase Azul), cassable
    Models/Bouteilles/<Type>/      6 bouteilles (Whisky, Coloree, Eau, Bleue, Korken, Flasque) :
                                   fbx, prefab cassable+attrapable, *_Eclats.asset, *_Liquide.asset
    Models/Bouteilles/placements_comptoir.json   positions des 20 bouteilles (venant de Blender)
    FX/                            effet de casse (BreakFX.prefab), matériaux particules, sonde de réflexion,
                                   Liquides/ (matériaux de liquide)
    TV/                            télés suspendues (image de match générée, matériaux)
    Vinyle/ Audio/                 platine : touches découpées, étiquette, matériaux ; morceau et vidéo des télés ignorés par Git (droits, dépôt public) : à copier à la main avec leurs .meta
                                   Audio/Verre_Casse.wav = casse des bouteilles (LaSonotheque.fr, ID 0148) ; puffs : céramique générée (SmashSynth)
    Models/Decor/<Nom>/            appareils du bar (caisse, tireuse, machines café/glaçons, frigo vitrine, distributeur) :
                                   modèles de l'équipe (BLENDER_VR/02_Bar), exportés avec l'addon, « place »: false
    Lighting/                      luminaires (maillages, matériaux) + réglages de lightmapping
  Hands/                           mains réalistes générées (maillage + texture peau) ; Detail/ = normal map de pores (CC BY 4.0, voir CREDITS.txt)
  Scenes/Bar/                      lightmaps, probes et sonde calculées (régénérées par « Éclairer le Bar »)
  Shaders/Verre.shader             verre (réfraction via Opaque Texture, absorption, Fresnel, reflets de la sonde)
  Shaders/Liquide.shader           liquide coupé par un plan monde (reste horizontal)
  Scripts/
    Hands/ControllerHandPose.cs    pose des mains : doigts pilotés par gâchette/grip ; prise d'objets sans pénétration
    Grab/GripSurface.cs            surface de prise cylindrique (axe réglable : Z batte, Y bouteille)
    Grab/HandGripGrabInteractable.cs  XRGrabInteractable : cale l'objet dans la main + lancer basé sur le geste réel
    Breakables/Breakable.cs        casse (3 niveaux d'éclats selon la force), relâche les mains avant de se désactiver
    Breakables/Breaker.cs          ce qui casse (la batte) + vibrations
    Breakables/BreakFx.cs          effet de casse en pool (éclats 3D, poussière, liquide)
    Breakables/Debris.cs           morceaux : gros = restent au sol sans physique ni collider ; miettes = disparaissent ; plafond 140
    Breakables/LiquidFill.cs       niveau + ondulation du liquide (ExecuteAlways)
    Breakables/SmashSynth.cs       son de casse généré par code
    Breakables/GlassPane.cs        vitre plane cassable (fissures puis éclats) ; coups transmis par le porteur du Rigidbody
    Breakables/ImpactMarks.cs      impacts de batte sur une surface (marbre du comptoir) : quads Fragile/Impact recyclés (40 max)
    Grab/HingedDoor.cs             porte à charnière saisie par la poignée, « graissée » (élan, rebond, rappel, aimant, poussée par les coups)
    Music/Turntable, PanelButton, PanelKnob, FingerTips   platine vinyle ; bouton poussé au doigt, molette tournée au poignet (réutilisables)
    XR/SimulatorIfNoHeadset.cs     simulateur XR seulement si aucun casque (attend jusqu'à 4 s le démarrage OpenXR)
    Editor/                        outils (menus ci-dessous)
Tools/Blender/fragile_unity_export.py   addon Blender « Fragile - Export vers Unity » (+ README)
```

## Menus éditeur « Fragile > … »
| Menu | Rôle |
|---|---|
| Construire la pièce du Bar | sol 10×10 m (téléportable), murs, plafond, lumière |
| Ajouter le XR Origin au Bar | rig XR (Complete XR Origin Set Up Hands Variant) |
| Générer les mains réalistes | régénère maillage + texture des mains (depuis les modèles XR Hands Android XR, qui doivent être Read/Write) |
| Importer le comptoir | import dédié du comptoir (ancien, remplacé en pratique par l'import Blender générique) |
| Importer les modèles Blender | lit tous les `fragile_export.json` : matériaux URP (verre → shader Fragile/Verre), collider, placement |
| Placer les bouteilles du comptoir | préfabs cassables/attrapables + pose des 20 bouteilles d'après `placements_comptoir.json` |
| Rendre cassable (sélection) | pré-découpe Voronoï (5/12/22 éclats) + Breakable + attrapable |
| Rendre les bouteilles attrapables | GripSurface (axe Y) + HandGripGrabInteractable sur bouteilles et puffs |
| Remplir les bouteilles | volume de liquide + matériau par type de bouteille |
| Ajouter les télés au-dessus du comptoir | 4 télés dos à dos suspendues au centre du comptoir |
| Préparer la vidéo des télés | branche le composant `TvVideo` (objet Télés) sur les 4 écrans : y glisser un fichier vidéo (Assets/Bar/TV/Video) |
| Rendre les télés destructibles | `TvBreakable` sur chaque télé : fissures -> écran mort (éclats, étincelles) -> chute. Les télés ne sont plus « Batching Static » (sinon elles ne peuvent pas tomber) |
| Éclairer le Bar | luminaires + lightmaps + light probes + sonde (environ 1 min de calcul). À relancer si on déplace murs, comptoir ou télés |
| Aménager le décor du Bar | pose les appareils de `Models/Decor` (positions dans `BarDecorBuilder.Spots`), BoxCollider, statiques, émission des écrans/néons (l'addon n'exporte pas l'émission). Frigos : porte `HingedDoor` (saisie par la poignée), vitre séparée et cassable (`GlassPane`), parois en boîtes, 15 bouteilles cassables chacun. Relancer « Éclairer le Bar » après |
| Rendre le marbre marquable | génère la texture d'impacts (`Assets/Bar/FX/Impacts`) et pose `ImpactMarks` sur le comptoir |
| Animer la platine vinyle | branche bouton marche/arrêt, molette de volume, disque, bras, égaliseur et son sur la platine de la scène |

## Pipeline Blender → Unity
1. Blender (`E:\Assets.blend` chez Tom) : panneau N > onglet **Fragile** > *Exporter vers Fragile* (régler « Dossier Assets » sur son clone).
   L'addon : bonne carte UV, textures extraites/converties (webp → png), détection du verre, `fragile_export.json`.
2. Unity : *Fragile > Importer les modèles Blender*. Champ `"place": false` dans le json = ne pas poser automatiquement (cas des bouteilles).

## Pièges connus (déjà rencontrés)
- **Ne jamais utiliser `EditorUtility.CopySerialized` pour mettre à jour un maillage** : les SkinnedMeshRenderer gardent l'ancienne taille → mains invisibles. Utiliser `mesh.Clear()` + API Mesh.
- **NaN en mode édition = crash d'Unity** (rendu). Garde-fous présents dans ControllerHandPose (poids nuls, position invalide).
- Pendant une prise, **la main est enfant de l'objet** : tout objet détruit doit d'abord relâcher les mains (`Breakable.ReleaseHands`).
- Les mains sont des **préfabs FBX modifiés en scène** : ne pas laisser d'overrides d'os (rétablir avec RevertObjectOverride après des tests).
- Le simulateur XR automatique de XRI est **désactivé** (sinon il écrase le suivi du casque) ; c'est `SimulatorIfNoHeadset` qui le lance.
- **Éclairage précalculé** : toutes les lumières sont Baked, sauf la directionnelle (Mixed / Subtractive : ombres et modelé des objets mobiles). Les lumières additionnelles temps réel sont désactivées dans l'URP Performance. Réglages dans `BarLightingBuilder.cs` (ne pas retoucher à la main : relancer le menu écrase).
- Murs et plafond ont **Cast Shadows = Off** (sinon ils bloquent la directionnelle). Les ombres du niveau « Low » étaient coupées (QualitySettings) : activées, distance 4 m, shadow map 2048 ; à recouper si le Quest peine.
- **Opaque Texture** activée (demi-résolution) dans les deux URP assets pour la réfraction du verre ; à couper si les perfs Quest en souffrent (case « Réfraction de la scène » sur les matériaux de verre).
- **Frigo vitrine** : le FBX n'est PAS un export direct du .blend : la porte est séparée (pivot sur la charnière) et les étagères passent de 4 à 2 (bouteilles de 42 cm). Pour le ré-exporter : `Tools/Blender/export_frigo_ouvrable.py` (pas l’addon). Sa vitre utilise `Frigo_Vitre.mat` (URP Lit transparent, posé dans la scène) : `Fragile/Verre` est opaque (recopie l'Opaque Texture) et cacherait les bouteilles en verre derrière.
- Le liquide est rendu en file 2500 (avant le verre transparent) pour être vu à travers la réfraction.
- Physique à **72 Hz** (`TimeManager`) ; le laser de saisie (far casting) est **désactivé** : on n'attrape qu'à la main.
- MCP Unity (CoplayDev unity-mcp v10.3.0, http://127.0.0.1:8080) : se déconnecte souvent après recompilation / redémarrage → `/mcp` > Reconnect, et dans Unity *Window > MCP for Unity* > Start.
- Le `.blend` n'est **pas** dans le dépôt (disque E: de Tom).

## Idées en attente
- Main Tripo (plus détaillée) abandonnée : nécessiterait un vrai rig fait dans Blender + licence vérifiée.
