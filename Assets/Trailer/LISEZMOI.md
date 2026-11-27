# Trailer (intro du jeu)

Le joueur est assis au comptoir du bar et regarde la fin d'un match à la télé, les mains posées sur le comptoir.
Son équipe (FRA) touche le poteau, l'adversaire marque (1-2), coup de sifflet final… et **le personnage pète un câble**.
Le joueur ne contrôle plus que **sa tête** : ses mains (celles du personnage) agissent seules.
1. Les poings se serrent et tremblent, puis tapent sur le comptoir (les verres sautent).
2. Il attrape ce qui traîne devant lui (verre, bouteille) et le jette dans la télé (deux fois si possible) : l'écran explose et le son du match se coupe.
3. Il se lève et balaie le comptoir d'un revers de la main gauche.
4. **À mains nues uniquement** (aucune arme pendant l'intro : armes à feu et batte sont cachées) : il saisit une
   bouteille et la fracasse sur le marbre, enchaîne les coups de poing dans ce qui reste debout, puis abat ses deux
   poings joints sur le comptoir… **coupure au noir** à l'impact.
5. Le Bar est rechargé **intact** et rendu au joueur, à sa position de départ normale : à lui de tout casser.

Pendant le match, le **bar est plein** : clients au comptoir, aux tables, dans le salon, au mange-debout, un barman.
Au sifflet final, tout le monde se tourne vers le personnage ; au premier coup de poing, **ils s'enfuient tous** par la
porte de sortie (elle s'ouvre). Quand le joueur prend la main, il n'y a plus personne. Aucune arme pendant l'intro
(elles sont toutes dans le jeu, à leur place). Le joueur reprend la main **là où la cinématique s'arrête** (un pas en
arrière du tabouret).

Cœur qui s'emballe, souffle, bord de vision rouge (le centre reste dégagé pour voir la casse) et vibrations des manettes
accompagnent la crise. A / X (ou Espace dans l'éditeur) : passer l'intro.

## Le personnage (`TrailerGhost`)
Il joue la **vraie** partie, avec les systèmes de Tom :
- il déplace les manettes virtuelles (le TrackedPoseDriver est coupé pendant l'intro) et pilote les doigts (`ControllerHandPose.ApplyPose`) ;
- il saisit avec XRI (`StartManualInteraction`) : la main se referme sur l'objet comme en jeu ;
- lancers et bouteilles fracassées cassent avec la physique et les règles normales (`Breakable`, `TvBreakable`) ; une
  sécurité casse l'objet s'il a raté. Coups de poing : `Breakable.Shot` sur la cible touchée.

Tout est placé à partir du tabouret choisi : dessus du comptoir trouvé par rayons, verres et bouteilles à portée de main,
Les gestes sont décrits dans `TrailerGhost` (positions en mètres dans le repère du corps : x droite, y haut, z vers le comptoir).

## Les clients (`TrailerCrowd`, `BarClient`)
- **Modèles** : `Assets/Trailer/Clients` (12 clients + barman, ~13 k triangles chacun, textures 1024 / 512 sur Quest),
  générés par `Tools/Blender/generate_clients.py` avec MPFB (MakeHuman pour Blender). Assets **CC0** uniquement
  (voir `Clients/CREDITS.txt`). Pour en refaire : installer MPFB + les packs CC0, modifier la liste `CLIENTS`, puis
  `blender -b --python Tools/Blender/generate_clients.py -- Assets/Trailer/Clients`.
- **Qualité vérifiée à la génération** : squelette calé sur le corps (hanches, coude plié), mâchoire et yeux fixés à la
  tête, et personnage complet (yeux, sourcils, cils, cheveux, textures), sinon il n'est pas exporté. Dans Unity,
  **Fragile > Trailer > Diagnostiquer les clients** anime chacun hors scène (7 animations) et vérifie membres, sens et
  matériaux (rendus et rapport dans `Temp/TrailerDiag`). Les matériaux sont construits par `TrailerClientsImport`.
- **Animations** : `Assets/Trailer/Animations` (Quaternius Universal Animation Library, CC0), retargetées par Unity
  (Humanoid). **Fragile > Trailer > Préparer les clients du bar** choisit les clips par mots-clés (idle, sit, talk,
  sprint…), construit `Clients/Clients.controller` (passe IK pour les regards) et le branche dans la scène Trailer.
- **Places** retrouvées par le nom des meubles du Bar : `Tabouret` (sauf celui du joueur), `Fauteuil`, `Fauteuil lounge`,
  `Canapé`, `Banc`, `Tonneau mange-debout`, `Cible de fléchettes`, barman dans le `Comptoir`. Les plus proches du joueur
  sont remplies d'abord (18 clients par défaut, réglage « Max Clients »).
- **Fuite** : grille du sol (25 cm) + champ de distances depuis la `Porte de sortie` (évite meubles et joueur), chemins
  lissés, séparation entre fuyards, départs en vagues. Le battant (statique dans le Bar) est caché et remplacé par une
  copie qui pivote ; un couloir sombre est posé derrière.
- **Physique** : chaque client a un ragdoll (Rigidbody + CharacterJoint sur 11 os) qui suit l'animation ; en courant ils
  bousculent verres et bouteilles ; touchés par un objet lancé assez vite ou par la batte, ils s'effondrent pour de vrai.

## Pourquoi le Trailer reste synchronisé avec le Bar
- `Assets/Scenes/Trailer.unity` ne contient **qu'un seul objet** (`Trailer`, composant `TrailerDirector`).
  Il n'y a **aucune copie du bar** dedans.
- Au lancement, il charge la **vraie** scène `Bar` en plus (chargement additif). Tout ce que Tom change dans
  `Bar.unity` (meubles, éclairage, casse, sons…) apparaît donc tout seul dans le Trailer.
- Les points d'accroche sont retrouvés **par leur nom** à chaque lancement (`TrailerAnchors`) :
  - le `XR Origin` du bar ;
  - l'objet `Télés` (ou le composant `TvVideo`) et ses renderers `Écran` ;
  - les objets `Tabouret`. Le meilleur tabouret est choisi automatiquement : écran de face, rien devant, à environ 2,6 m.
- Le Trailer **ne modifie jamais** `Bar.unity`. Le bar cassé pendant l'intro est déchargé, puis la scène est rechargée
  telle que Tom l'a enregistrée.
- Tous les fichiers du Trailer sont à part (`Assets/Trailer`, `Assets/Scripts/Trailer`, `Scripts/Editor/TrailerTools.cs`,
  `Scenes/Trailer.unity`) : fusionner `main` dans la branche `trailer` ne crée pas de conflit.

Ce qui casserait le lien : renommer `Télés`, `Écran` ou `Tabouret`, ou supprimer le XR Origin. Dans ce cas, le Trailer
écrit un avertissement et lance directement le Bar. Sans batte ou sans comptoir devant le tabouret, la crise est
raccourcie (coups de poing seulement).

## Récupérer les modifications de Tom
```
git checkout trailer
git pull origin main        # ou : git fetch && git merge origin/main
```
Puis dans Unity : **Fragile > Trailer > Vérifier la synchro avec le Bar**. Le résultat s'affiche dans la Console
(place choisie, nombre d'écrans, avertissements).

## Menus « Fragile > Trailer »
| Menu | Rôle |
|---|---|
| Installer la scène Trailer | crée ou répare `Scenes/Trailer.unity` et la met **en premier** dans le build (Bar juste après) |
| Vérifier la synchro avec le Bar | ouvre le Bar à côté, retrouve les points d'accroche, écrit le rapport, referme le Bar |
| Aperçu de la place du joueur | cadre la vue de scène depuis les yeux du joueur assis (Bar laissé ouvert : **ne pas l'enregistrer**) |
| Jouer le Trailer | ouvre la scène Trailer et lance le mode Play |

## Réglages (objet `Trailer`, composant `TrailerDirector`)
- **View Point** : force la place des yeux (un Transform placé dans la scène Trailer). Vide = choix automatique.
- **Match** : noms et couleurs des équipes.
- **Déroulé** : moments clés (occasion 5 s, but 12 s, sifflet 17 s, noir final 1,5 s…). La crise dure le temps de ses gestes (environ 15 s).
- **Sons** : vides = générés par le code (`TrailerSynth`). On peut y glisser de vrais fichiers (droits vérifiés).

## Le faux match (`FakeMatch`)
Petit stade généré très loin sous la scène (y = −2000, hors de vue), filmé par une caméra « réalisateur télé » dans une
RenderTexture 640×360 affichée sur les 4 écrans. Terrain, foule et panneaux sont des textures générées. Les joueurs sont
des capsules : 4-4-2, passes, pressing et interceptions simples. Les temps forts sont scriptés. Score, chrono
et bandeaux (BUT !, TERMINÉ) en TextMeshPro. Aucune image ni vidéo sous droits.

## À régler après les premiers essais au casque
- Orientation des mains (`PalmRoll`, angles des poings) : dépend de la pose des manettes Quest, à ajuster à l'œil.
- Trajectoires des coups (`BareHands`, `Punch`, `SmashOnCounter`) selon la hauteur réelle du comptoir.
- Pas encore de titre « FRAGILE » sur le noir final.
- Hauteur des clients assis (dépend du clip « assis » choisi) et orientation sur le canapé / les bancs.
- Coût sur Quest : 18 clients ≈ 240 k triangles en plus pendant l'intro (baisser « Max Clients » si besoin).
