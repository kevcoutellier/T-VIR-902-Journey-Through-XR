# Sélection d'assets 3D — STOP-IT (disque externe `D:\`)

> Établi le 2026-06-04 en inventoriant `D:\`. Objectif : ne récupérer **que** ce qui sert
> au projet (maison + 5 scénarios + bébé NPC + chat), dans un **style cohérent** et **compatible Unity URP / Quest**.

---

## ⭐ TL;DR — Ce qu'il faut copier

| Priorité | Pack | Chemin source | Pourquoi |
|---|---|---|---|
| **1** | **POLYGON Town (URP)** | `D:\DL\unity\Polygon\Version URP\Polygon_Town_URP.unitypackage` | Maison moderne complète : murs/fenêtres/portes, **escalier + collision**, cuisine (**micro-ondes**, four, frigo, évier), salle de bain (baignoire, lavabo, WC, douche), mobilier, **fourchette**, **prise (PowerPlug)**, **berceau**, jouets, **personnages enfants + parents** |
| **2** | **POLYGON Office (URP)** | `D:\DL\unity\Polygon\Version URP\Polygon_Office_URP.unitypackage` | Complète Town : **spray nettoyant**, bouteille d'alcool, **prises murales (Socket_Wall)**, câbles, mini-frigo, micro-ondes alt., perso "agent de nettoyage" |
| **3** | **STORY – Wildlands Bundle** | `D:\Perso\USB bureau\Assets VIR\Assets\STORY - Wildlands Bundle...\Tidal Flask - STORY Wildlands Bundle_v1-5.unitypackage` | Déjà dans « Assets VIR ». Extérieur vu par la fenêtre (arbres/buissons/herbe), **bouteilles de pilules** + waterbottles (produit dangereux scénario 4), birdhouse |

> Les versions **URP** sont à privilégier (le projet est URP). Une version "standard" existe aussi
> dans `D:\DL\unity\Polygon\` si besoin.

**À sourcer en externe (absents du disque)** : 🐱 chat, 🐦 pigeon, 👶 vrai bébé/bambin, 🛹 skateboard. Voir §4.

---

## 1. Direction artistique

Le disque contient deux familles stylisées **low-poly flat-shaded** cohérentes entre elles :

- **POLYGON (Synty)** — *moderne* → **colle exactement au brief « maison »** (électroménager, prises, salle de bain moderne…). **Recommandé comme base.**
- **FANTASTIC / STORY (Tidal Flask)** — déjà rangé dans `Assets VIR`, mais **Interior = médiéval/fantasy** (poêle à bois au lieu de micro-ondes, pas d'électricité, pas de lavabo moderne). À garder comme **réserve de props** / alternative fantasy, pas comme base d'une maison contemporaine.

👉 **Décision recommandée : base POLYGON Town + Office** (moderne, cohérent, Quest-friendly, meshes de collision fournis). On garde STORY Wildlands pour l'extérieur/les props dangereux.

---

## 2. Couverture par scénario (fichiers exacts confirmés)

| Scénario | Besoin | Asset trouvé | Pack |
|---|---|---|---|
| **1 — Salon / prise** | Prise électrique | `SM_Prop_Socket_Wall_01/02`, `SM_Prop_Socket_Switch_01`, `SM_Prop_PowerPlug_01` | Office / Town |
| | Fourchette | `SM_Item_Cutlery_Fork_01` (+ `SM_PROP_fork` Wildlands) | Town |
| **2 — Cuisine / micro-ondes** | Micro-ondes | `SM_Prop_Microwave_01` | Town **et** Office |
| | Cuisine complète | `SM_Prop_Kitchen_Counter/CounterSink/Fridge/Extractor/Oven` | Town |
| | 🐱 Chat | **ABSENT** → externe (§4) | — |
| **3 — Escalier / skate** | Escalier + collision | `SM_Bld_House_Interior_Stairs_01/02/03` (+ `_COLLISION`, rampes, côtés) | Town |
| | 🛹 Skateboard | **ABSENT** → externe (§4) | — |
| **4 — SdB / produit ménager** | Spray nettoyant | `SM_Prop_SprayBottle_01` | Office |
| | Bouteille "dangereuse" | `SM_Prop_Alcohol_Bottle_01`, `SM_Prop_Bottle_01`, `SM_PROP_bottle_pills_01/02` | Office / Wildlands |
| | Salle de bain | `SM_Prop_Bath_01/02`, `SM_Prop_BathroomSink_01/02`, `SM_Prop_Toilet_01/02`, `SM_Prop_Shower_01/02` | Town |
| **5 — Fenêtre / pigeon** | Fenêtres (rebord) | `SM_Bld_House_..._Window_01..05`, `..._UpperFloor_Window`, `Roof_Window` | Town |
| | Chambre enfant | `SM_Prop_BabyCot_01`, `SM_Prop_ToyBlock_01/02/03`, `SM_Prop_ToyTrain_01` | Town |
| | Extérieur (arbres…) | `SM_ENV_tree_*`, `SM_ENV_PLANT_*`, `SM_ENV_stone_*` | Wildlands |
| | 🐦 Pigeon | **ABSENT** (seulement BirdBath/birdhouse/birdcage) → externe (§4) | — |

### Personnages disponibles (POLYGON Town)
- **Papa (joueur)** : `Character_Father_01/02` — `Character_Mother_01/02`
- **Enfant NPC** : `Character_Son_01`, `Character_Daughter_01`, `Character_SchoolBoy_01`, `Character_SchoolGirl_01`
  → ce sont des **enfants** (pas des bambins). Utilisables tels quels comme NPC, **ou** mis à l'échelle, **ou** remplacés par un vrai bambin externe (§4).

---

## 3. Maison — couverture mobilier/structure (POLYGON Town + Office)

✅ Présents et confirmés : murs modulaires, portes, fenêtres (stores/rideaux/volets), escaliers (+collision),
canapés (7), fauteuils, chaises (multi), tables (basse/repas/bureau), lits (simple/double/superposé),
berceau, tables de nuit, bibliothèques, étagères, placards, commodes, meubles TV, TV (plat/ancien/mural),
lampes (plafond/sur pied/bureau), cuisine complète (plans, évier, frigo, four, micro-ondes, hotte),
salle de bain complète (baignoire, lavabo, WC, douche, miroir), tapis, prises/interrupteurs, câbles,
gamelles animaux, niche, jouets.

⚠️ Petits manques (à improviser/combiner ou sourcer) : aucun bloquant — la cuisine/SdB sont complètes.

---

## 4. À sourcer en externe (introuvable sur `D:\`)

Garder le **style low-poly Synty** pour la cohérence.

| Élément | Suggestion |
|---|---|
| 🐱 **Chat** (scénario 2) | Asset Store « **Polyperfect — Low Poly Animated Animals** » (contient chat **et** pigeon), ou Sketchfab "low poly cat" |
| 🐦 **Pigeon** (scénario 5) | idem Polyperfect (pigeon inclus), ou Sketchfab "low poly pigeon" |
| 👶 **Bébé/bambin** (NPC) | Synty « **POLYGON Kids** » (bambins/enfants), Sketchfab, **ou** réutiliser `Character_Son_01` de Town mis à l'échelle |
| 🛹 **Skateboard** (scénario 3) | Asset Store / Sketchfab "low poly skateboard" (nombreux gratuits) |

---

## 5. À NE PAS copier (gain de place / inutile)

- ❌ **Tout `D:\Unreal\*` et `D:\DL\unreal\*`** — fichiers `.uasset` **non importables dans Unity** (vérifié : packs Dekogon, Freshcan, Meshingun, Prophaus, Polygon-UE, Maksim Bugrimov, Yarrawah, Bameshi, Graybite, Scans Factory, Sicka… = 0 source FBX exploitable, sauf quelques buissons). **Inutile de les transférer.**
- ❌ Packs **fantasy/médiévaux** non pertinents pour une maison : FANTASTIC Battle/Dungeon/Village, Hivemind (Castle, Dungeon, MedievalTown…), Freshcan Medieval, Witch Village, POLYGON Pirate/Fantasy/SciFi/Spy/Snow/Zombies.
- ❌ Gros environnements **réalistes** hors-sujet et lourds (clasheraient avec le low-poly) : Learters (Egyptian 6 Go, Lighthouse 11 Go, Underwater, RallyPoint), Mammoth Interactive, StylArts, Infinity PBR — **sauf** si vous décidez de basculer en rendu réaliste (alors regarder Mammoth *SubUrban Neighbourhood* et Learters *Will's Room/Dormitory*).

---

## 6. Suppléments optionnels (style assorti)

- **FANTASTIC – Interior Pack** (`Assets VIR`) — réserve de props déco (348 FBX : paintings, rugs, plants, candles, books, jars…) si besoin de remplissage, **en gardant à l'esprit le style médiéval**.
- **POLYGON Icons (URP)** (`Version URP\Polygon_Icons_URP.unitypackage`) — icônes UI pour le menu VR (`ScenarioMenu`).
- **POLYGON Farm (URP)** — fermiers + poulailler (bâtiment), **pas d'animaux loose** dans cette version (ne pas compter dessus pour le chat/pigeon).

---

### Notes techniques
- Les `.unitypackage` POLYGON sont en **double** (`D:\DL\unity\Polygon\` standard **et** `\Version URP\`). Importer **uniquement la version URP**.
- Les 5 packs de `D:\Perso\USB bureau\Assets VIR\Assets\` sont **identiques** à `D:\Assets de base\` (FANTASTIC + STORY). Inutile de copier les deux.
- Workflow d'intégration scène : passer par une **sandbox** puis le **verrou** `docs/SCENE_LOCK.md` (cf. `CLAUDE.md` / `docs/TEAM_WORKFLOW.md`).
