# Workflow de collaboration — STOP-IT (3 devs)

## Contexte

La maison entière (5 scénarios) est dans une seule scène Unity :
`STOP-IT/Assets/_Scenes/LivingRoom.unity`

Les fichiers `.unity` sont du YAML binaire semi-lisible : **un merge conflict sur cette scène est pratiquement ingérable**. Pour éviter ça, on suit le workflow ci-dessous sans exception.

---

## Setup initial (à faire une seule fois par membre)

### 1. Configurer votre identité git
```powershell
git config user.name "VotreNom"   # ex: "Paulin", "Kevin", "Troisième"
git config user.email "votre@email.com"
```

Le nom git est utilisé par le hook pour identifier qui a réservé le verrou. Vérifiez que votre `user.name` est cohérent dans tout le repo :
```powershell
git config user.name   # doit afficher votre prénom
```

### 2. Vérifier que le hook est actif
Le fichier `.claude/settings.json` configure le hook automatiquement pour toutes les sessions Claude Code sur ce projet. Aucune action supplémentaire n'est nécessaire — Claude le charge automatiquement.

### 3. Créer votre scène sandbox si elle n'existe pas encore
Depuis Unity Editor :
- `File → New Scene` (choisir le template approprié)
- Sauvegarder dans `STOP-IT/Assets/_Scenes/Sandboxes/Sandbox_<VotreScénario>.unity`
- Commit + push

---

## Règles fondamentales

| Règle | Explication |
|---|---|
| **Sandbox d'abord** | Tout nouveau gameplay se développe dans la scène sandbox du scénario, jamais directement dans LivingRoom.unity |
| **Verrou avant intégration** | On ne touche LivingRoom.unity qu'après avoir réservé le verrou dans `docs/SCENE_LOCK.md` |
| **Push immédiat du verrou** | Dès qu'on claim le verrou, on push — les coéquipiers font un `git pull` et voient le verrou actif |
| **Scripts en parallèle OK** | On peut modifier les fichiers `.cs` dans `_Scripts/` sans coordination |
| **Libérer vite** | On libère le verrou le plus vite possible après l'intégration (pas de réservation "au cas où") |

---

## Workflow complet par feature

### Phase 1 — Développement (sandbox, sans coordination)

```
git checkout -b feat/<scenario>-<feature>
```

Travailler uniquement sur :
- `STOP-IT/Assets/_Scripts/` — écrire ou modifier les scripts
- `STOP-IT/Assets/_Scenes/Sandboxes/Sandbox_<Scénario>.unity` — tester dans la sandbox
- `STOP-IT/Assets/_Materials/` — materials, textures
- Assets isolés (prefabs, audio, etc.)

Itérer librement. Commits réguliers sur la branche feature.

### Phase 2 — Validation sandbox

Tester le scénario complet dans la sandbox :
- [ ] Comportement enfant IA correct (ChildNPC + HazardZone)
- [ ] Interaction joueur fonctionnelle
- [ ] Pas de régression sur les scripts partagés (GameManager, ScenarioManager)
- [ ] Performance VR acceptable (pas de chute de framerate)

Ne pas passer à la Phase 3 tant que la sandbox n'est pas validée.

### Phase 3 — Réservation du verrou

**Étape 3a — Vérifier que le verrou est libre**
```powershell
git pull origin main
cat docs/SCENE_LOCK.md   # vérifier owner: FREE
```
Si `owner` n'est pas `FREE`, contacter le coéquipier concerné sur Discord et attendre.

**Étape 3b — Réserver le verrou**

Éditer `docs/SCENE_LOCK.md` :
```yaml
---
owner: VotreNom          # ex: Paulin
since: 2026-05-07 14:30
feature: Sandbox_Outlet integration
expected_release: 2026-05-07 16:00
---
```

Committer et pusher **immédiatement** :
```powershell
git add docs/SCENE_LOCK.md
git commit -m "chore(lock): claim by Paulin for Outlet integration"
git push origin feat/<scenario>-<feature>
# ou git push origin main si déjà sur main
```

Prévenir les coéquipiers sur Discord : _"J'ai pris le verrou LivingRoom pour [feature], release vers [heure]"_.

### Phase 4 — Intégration dans la scène principale

```powershell
git pull origin main   # récupérer les dernières modifs avant de toucher la scène
```

Ouvrir `LivingRoom.unity` dans Unity Editor et intégrer :
- Copier la hiérarchie de GameObjects testés dans la sandbox
- Vérifier les références (scripts, prefabs, materials)
- Tester rapidement en Play Mode dans la scène unifiée

### Phase 5 — Commit, merge et libération

```powershell
git add STOP-IT/Assets/_Scenes/LivingRoom.unity
git add STOP-IT/Assets/_Scenes/LivingRoom.unity.meta
git commit -m "feat(<scenario>): integrate <feature> into main house scene"
```

**Libérer immédiatement le verrou** :

Remettre `docs/SCENE_LOCK.md` à l'état libre :
```yaml
---
owner: FREE
since: 
feature: 
expected_release: 
---
```

```powershell
git add docs/SCENE_LOCK.md
git commit -m "chore(lock): release"
git push
```

Prévenir les coéquipiers sur Discord : _"Verrou libéré ✓"_.

Ouvrir la PR vers `main` si on était sur une branche feature.

---

## Gestion des branches

```
main
├── feat/outlet-scenario     ← 1 branche par scénario ou feature
├── feat/stairs-blocking
├── feat/bathroom-swap
└── feat/window-pigeon
```

- Les branches feature ne touchent **jamais** `LivingRoom.unity` (sauf si on suit le workflow de verrou)
- L'intégration dans `LivingRoom.unity` se fait sur `main` de préférence, ou en dernier commit de la branche avant merge
- Avant de merger une PR, s'assurer que la scène principale est à jour (`git pull main`)

---

## Scènes sandbox — Convention de nommage

| Scénario | Fichier |
|---|---|
| Prise électrique (Salon) | `Sandbox_Outlet.unity` |
| Micro-ondes (Cuisine) | `Sandbox_Microwave.unity` |
| Escalier / Skateboard | `Sandbox_Stairs.unity` |
| Produit nettoyant (SdB) | `Sandbox_Bathroom.unity` |
| Fenêtre / Pigeon | `Sandbox_Window.unity` |

Emplacement : `STOP-IT/Assets/_Scenes/Sandboxes/`

Chaque sandbox contient le minimum nécessaire pour tester le scénario (XR Origin, enfant NPC, zone dangereuse) sans copier la maison entière.

---

## Timeout et situations d'urgence

### Quelqu'un a oublié de libérer le verrou

1. Vérifier depuis quand le verrou est actif (`since:` dans SCENE_LOCK.md)
2. Si > 4 heures sans réponse Discord → timeout acceptable
3. Vérifier le dernier commit sur `docs/SCENE_LOCK.md` pour confirmer qui a claimé :
   ```powershell
   git log --oneline docs/SCENE_LOCK.md
   ```
4. Contacter le membre, et si vraiment injoignable, libérer le verrou manuellement avec un commit explicite :
   ```
   chore(lock): force-release (timeout, claimed by Kevin since 10h00)
   ```

### Bypass d'urgence du hook

Si vous devez absolument modifier `LivingRoom.unity` sans avoir pris le verrou (urgence critique, bug bloquant en demo) :
```powershell
$env:HOOK_BYPASS = "1"
# Faire la modification via Claude
$env:HOOK_BYPASS = $null
```

**À utiliser avec discernement.** Documenter la raison dans le commit message.

### Deux personnes veulent intégrer en même temps

1. Celui qui a pushé le verrou en premier a priorité
2. Le deuxième attend la libération
3. Avant d'intégrer, toujours faire `git pull` pour avoir la version la plus récente de la scène
4. Si une intégration précédente a changé des GameObjects que vous utilisiez, vérifier que vos références sont toujours valides

---

## Checklist rapide avant de toucher LivingRoom.unity

- [ ] `git pull` récent (moins de 5 minutes)
- [ ] `docs/SCENE_LOCK.md` affiche `owner: FREE`
- [ ] Sandbox validée et testée
- [ ] J'ai édité SCENE_LOCK.md avec mon nom et pushé
- [ ] J'ai prévenu l'équipe sur Discord
