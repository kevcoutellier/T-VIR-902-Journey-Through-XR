# Animation du ChildNPC — Guide d'intégration

> **Statut :** l'Animator Controller `STOP-IT/Assets/_Animation/ChildNPC.controller`
> est **écrit à la main hors-ligne** (MCP-Unity / Blender non connectés). Il est
> **structurellement valide** (fileIDs vérifiés) mais **n'a pas encore été ouvert
> dans l'éditeur Unity**. Première action requise : l'ouvrir une fois dans Unity
> pour confirmer l'import (voir §0).
>
> **GUID du controller :** `5c0ffee0a11ce0de57013c0117011500`
> (référençable par `stopit-scene` sans ouvrir l'asset).

---

## Contexte — le pont Animator de `ChildNPC.cs`

`ChildNPC.cs` expose déjà un pont Animator **purement additif** :

```csharp
[SerializeField] private Animator animator; // section "Animator (optional)"
```

piloté par hash (noms = **contrat strict**, à respecter au caractère près) :

| Paramètre     | Type    | Piloté par | Quand                                                    |
|---------------|---------|------------|---------------------------------------------------------|
| `Speed`       | float   | `SetFloat` | chaque frame, vitesse normalisée 0..1 (`AnimateWalk`)   |
| `Electrocute` | trigger | `SetTrigger` | l'enfant atteint la prise (`PlayReaction(Electrocuted)`) |
| `React`       | trigger | `SetTrigger` | réactions stub (`SkateWipeout` / `Stumble`)             |

Le controller **expose exactement ces 3 paramètres avec ces noms/types**. Tant
qu'aucun clip n'est posé dans les états (`m_Motion: {fileID: 0}`), le controller
est **inerte** : le procédural reste l'unique moteur visuel → **aucune régression**.

Si le champ `animator` est laissé vide dans l'inspecteur, `Awake()` fait un
`GetComponentInChildren<Animator>(true)` → il suffit donc qu'un `Animator` avec ce
controller existe **sous** le ChildNPC pour qu'il se branche automatiquement.

### Structure du controller (déjà en place)

- **1 layer** « Base Layer » (poids 1), 1 state machine.
- **4 états**, tous à `m_Motion` **vide** (slots à remplir) :
  - `Idle` (**état par défaut**)
  - `Walk`
  - `Electrocute`
  - `React`
- **Transitions :**
  - `Idle → Walk` : `Speed Greater 0.1`, sans Exit Time (durée 0.1 s)
  - `Walk → Idle` : `Speed Less 0.1`, sans Exit Time (durée 0.15 s)
  - `AnyState → Electrocute` : trigger `Electrocute`, sans Exit Time, `CanTransitionToSelf = false`
  - `Electrocute → Idle` : Exit Time 0.9 (retour auto)
  - `AnyState → React` : trigger `React`, sans Exit Time, `CanTransitionToSelf = false`
  - `React → Idle` : Exit Time 0.9 (retour auto)

---

## 0. Valider l'import du controller (à faire en premier dans Unity)

1. Ouvrir le projet dans Unity 6 (6000.4.0f1).
2. Dans le Project window, ouvrir `Assets/_Animation/ChildNPC.controller` (double-clic
   → fenêtre **Animator**).
3. Vérifier : aucune erreur console à l'import, les 4 états visibles, les flèches de
   transition présentes, et l'onglet **Parameters** liste `Speed` (float),
   `Electrocute` (trigger), `React` (trigger).
4. Si Unity propose un ré-enregistrement (« upgrade »), accepter : il ré-écrira le YAML
   au format exact de la version installée — sans changer la logique.

> En cas d'erreur d'import imprévue : supprimer le `.controller` + `.meta` et
> recréer le controller via **Create ▸ Animator Controller** dans Unity, puis
> re-saisir manuellement les 3 paramètres et les 6 transitions ci-dessus. Conserver
> le **même GUID** dans le `.meta` pour ne pas casser une éventuelle référence de
> `stopit-scene`.

---

## 1. Rig du mesh enfant

Source : `STOP-IT/Assets/PolygonTown/Models/Characters/Characters.fbx`
(POLYGON Town — déjà dans le repo ; cohérent avec la DA Synty low-poly).

1. Sélectionner `Characters.fbx` → Inspector → onglet **Model**.
2. Choisir le **mesh enfant le plus petit** du set. S'il n'y a pas de proportions
   « bambin », prendre le plus petit personnage puis **scaler à hauteur bambin**
   (~0.85–0.95 m) sur le `MeshHolder` (voir §3), **pas** sur le root du NPC (le root
   est piloté par le NavMeshAgent).
3. Onglet **Rig** :
   - Squelette humanoïde → **Animation Type = Humanoid**, **Avatar Definition =
     Create From This Model**, puis **Apply** → ouvrir **Configure…** et vérifier le
     mapping (hips, spine, bras, jambes en vert).
   - Squelette non standard → **Animation Type = Generic** (Root node = la racine du
     skin). Les clips devront alors être Generic (pas de retarget Humanoid).
4. **Apply**.

> Humanoid est fortement recommandé : il permet de réutiliser des clips Mixamo par
> retarget. Generic impose des clips faits pour ce squelette précis.

---

## 2. Poser les clips dans les 4 états

Clips conseillés (Mixamo de préférence, en **Humanoid** pour le retarget) :

| État          | Clip                                  | Réglage             |
|---------------|---------------------------------------|---------------------|
| `Idle`        | idle léger (respiration / balancement) | **Loop Time = on**  |
| `Walk`        | marche bambin                          | **Loop Time = on**  |
| `Electrocute` | one-shot ~0.6 s (secousse / choc)      | Loop Time = off     |
| `React`       | réaction générique courte (trébuche)   | Loop Time = off     |

> **Cale temporelle :** la durée `Electrocute` (~0.6 s) doit matcher le *beat* de
> `HazardZone` et la durée procédurale `electrocuteDuration` (0.6 s par défaut dans
> `ChildNPC.cs`) pour que la secousse du clip et le flash rouge/shake soient synchro.

**Importer un clip :**
1. Glisser le FBX/clip Mixamo dans `Assets/_Animation/` (ou un sous-dossier).
2. Inspector du FBX → **Rig** → Animation Type = **Humanoid**, **Avatar Definition =
   Copy From Other Avatar** → pointer l'avatar du `Characters.fbx` du §1 → **Apply**.
3. Onglet **Animation** : régler Loop Time selon le tableau, éventuellement *trim* la
   plage de frames → **Apply**.

**Glisser un clip sur un état (fenêtre Animator) :**
1. Project : ouvrir le FBX, déplier pour révéler le **clip** (triangle blanc).
2. Animator (controller ouvert) : cliquer l'état cible (`Idle`, `Walk`, …).
3. Inspector de l'état → champ **Motion** → glisser le clip dedans (ou cliquer le
   petit cercle et le sélectionner). Le champ **Motion** vide (`None`) passe au clip.
4. Répéter pour les 4 états.

> Dès qu'un état a un Motion non vide, **ce state-là** s'anime. On peut commencer par
> `Idle` + `Walk` seulement ; `Electrocute`/`React` peuvent rester vides un temps —
> les triggers seront alors no-op visuels (le procédural couvre déjà l'électrocution).

---

## 3. Câblage scène — rôle `stopit-scene`, SOUS VERROU, différé

> ⚠️ Touche au prefab/à la hiérarchie du ChildNPC dans `LivingRoom.unity`.
> **À exécuter par `stopit-scene` après réservation du verrou** (`docs/SCENE_LOCK.md`,
> procédure `docs/TEAM_WORKFLOW.md`). À faire idéalement sur le **prefab** du ChildNPC
> pour propager à toutes les zones.

1. Sous le GameObject `ChildNPC`, repérer (ou laisser `Awake` créer) l'enfant
   **`MeshHolder`** : c'est lui qui porte le visuel (le root reste au NavMeshAgent).
2. Y poser le **SkinnedMeshRenderer** du mesh enfant riggé (instancier le personnage
   sous `MeshHolder`, supprimer tout placeholder capsule). Garder le pivot au sol,
   échelle ajustée à hauteur bambin sur `MeshHolder`.
3. Sur ce GameObject riggé (celui qui porte l'`Avatar`), ajouter un composant
   **Animator** :
   - **Controller** = `Assets/_Animation/ChildNPC.controller`
     (GUID `5c0ffee0a11ce0de57013c0117011500`)
   - **Avatar** = l'avatar généré au §1
   - **Apply Root Motion = OFF** (le déplacement vient du NavMeshAgent ; le root motion
     se battrait avec lui).
   - **Update Mode = Normal**, **Culling Mode = Cull Update Transforms**.
4. Renseigner le champ **`animator`** du composant `ChildNPC` (section *Animator
   (optional)*) avec cet Animator.
   - Optionnel : si l'Animator est bien **sous** le ChildNPC, on peut laisser le champ
     vide → `Awake()` le retrouve via `GetComponentInChildren<Animator>(true)`.
   - Le renseigner explicitement reste préférable (robustesse, et si plusieurs Animators
     coexistent).
5. Lancer en Play : marcher l'enfant → `Speed` doit varier dans l'onglet **Parameters**
   de l'Animator (la blend Idle↔Walk se déclenche dès qu'un clip est posé). Toucher la
   prise → le trigger `Electrocute` s'allume brièvement.

---

## 4. Budget Quest 3 (à respecter)

- **Compression des clips** : Anim Compression = *Optimal* (ou *Keyframe Reduction*),
  tolérances par défaut ; couper les courbes scale inutiles.
- **Pas d'IK runtime coûteux** (pas de Foot IK / pas de couche IK Pass) — inutile pour
  un toddler vu de loin en VR.
- **1 seul material par skinned mesh** si possible (atlas Synty partagé), smoothness
  basse / métallique nul (look cartoon URP), GPU instancing sur le material partagé.
- **Culling Mode = Cull Update Transforms** pour suspendre l'évaluation hors écran.
- Triangles bas (low-poly Synty) → reste dans le budget global < 750K tris/frame,
  < 100 draw calls.

---

## 5. Garde-fou — non-régression

Tant que les 4 états ont `m_Motion` **vide** (état actuel du controller livré) :
- l'Animator **ne produit aucune pose** ;
- `ChildNPC.cs` continue d'animer le `MeshHolder` **par procédural** (waddle, bob,
  électrocution, bounce) exactement comme avant ;
- les `SetFloat`/`SetTrigger` partent dans le vide → **zéro effet de bord**.

On peut donc référencer le controller dès maintenant et **ajouter les clips
progressivement**, un état à la fois, sans jamais casser le jeu.

---

## Récap des fichiers livrés (tranche hors-ligne)

| Fichier | Rôle |
|---|---|
| `STOP-IT/Assets/_Animation/ChildNPC.controller` | AnimatorController hand-authored (3 params, 4 états vides, 6 transitions) |
| `STOP-IT/Assets/_Animation/ChildNPC.controller.meta` | `.meta` NativeFormatImporter, GUID `5c0ffee0a11ce0de57013c0117011500` |
| `docs/ANIMATION_CHILD.md` | ce guide |
