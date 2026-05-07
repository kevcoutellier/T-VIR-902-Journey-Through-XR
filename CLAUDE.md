# STOP-IT — Instructions Claude

## Vue d'ensemble du projet

Jeu VR Oculus (Unity URP) où le joueur incarne un papa en VR qui doit empêcher son enfant IA de faire des bêtises. 5 scénarios, tous dans **une seule scène unifiée** représentant la maison entière.

| # | Lieu | Action de l'enfant | Réponse joueur |
|---|---|---|---|
| 1 | Salon | Enfonce une fourchette dans la prise électrique | Bloquer / couvrir la prise |
| 2 | Cuisine | Met le chat dans le micro-ondes | Intercepter avant fermeture de la porte |
| 3 | Escalier | Dévale l'escalier en skateboard | Bloquer la descente |
| 4 | Salle de bain | Boit un produit nettoyant | Échanger la bouteille |
| 5 | Fenêtre | Monte sur le rebord pour attraper un pigeon | Tirer l'enfant en arrière |

**Stack** : Unity 2022+, URP, XR Interaction Toolkit, Oculus SDK, NavMesh AI, MCP-Unity.

---

## Structure du projet

```
STOP-IT/
├── Assets/
│   ├── _Scripts/          ← Scripts C# du jeu (voir liste ci-dessous)
│   ├── _Scenes/
│   │   ├── LivingRoom.unity   ← SCÈNE PRINCIPALE — voir règles ci-dessous
│   │   └── Sandboxes/         ← Scènes de développement isolées (1 par scénario)
│   ├── _Materials/
│   ├── Settings/          ← Assets URP (PC_RPAsset, UniversalRenderPipelineGlobalSettings)
│   ├── XR/ et XRI/        ← Assets XR Interaction Toolkit
│   └── InputSystem_Actions.inputactions
├── Packages/
│   └── com.gamelovers.mcp-unity/   ← MCP-Unity (lire son CLAUDE.md)
└── ProjectSettings/
```

## Scripts existants — toujours réutiliser avant de créer

| Script | Rôle |
|---|---|
| `ChildNPC.cs` | Enfant IA : NavMeshAgent vers HazardZone, animation de marche/bob |
| `HazardZone.cs` | Zone dangereuse : intensité émissive, particules étincelles, son de danger |
| `GameManager.cs` | State machine (Menu → Playing → Success/Fail), score, timer, audio |
| `ScenarioManager.cs` | Gère les 5 scénarios (séquentiel ou sélection) |
| `ScenarioMenu.cs` | Menu VR flottant en World Space, suit le regard, toggle bouton Y |
| `ScenarioIntroCountdown.cs` | Compte à rebours d'intro par scénario |
| `ScenarioSpawner.cs` | Spawn des objets spécifiques à chaque scénario |
| `ScenarioUI.cs` | Mise à jour UI (countdown, success/fail) |
| `PlayerBlocker.cs` | Zones de collision pour bloquer le joueur |
| `DangerVignette.cs` | Overlay rouge plein écran selon proximité de l'enfant au danger |
| `HazardIndicator.cs` | Chevron/flèche animée flottant au-dessus des dangers |
| `ChildGrabber.cs` | Attraper l'enfant (bouton grip / touche E) — succès one-shot avec VFX, compagnon de PlayerBlocker |
| `DesktopTestRig.cs` | Rig clavier/souris pour tester sans HMD (WASD/RMB/LMB/E/M/R/Esc) ; auto-désactivé si XR runtime détecté |
| `CameraShake.cs` | Shake caméra VR (singleton auto-attaché à Camera.main) |
| `XRLocomotionBinder.cs` | Locomotion joystick gauche + CapsuleCast anti-mur avec slide |
| `XRCameraFix.cs` | Correctif tracking XR |

**Outils Editor** : `HouseBuilder.cs`, `StopItBuildTools.cs`, `QuestBuildTools.cs` dans `_Scripts/Editor/`.
`QuestBuildTools.cs` : pipeline one-click Configure / Build / Build & Run (ARM64, IL2CPP, Vulkan, APK dans `Builds/Quest/`).

---

## ⚠️ RÈGLES CRITIQUES — Scène principale

### Règle d'or
> **Ne JAMAIS modifier `STOP-IT/Assets/_Scenes/LivingRoom.unity` (ni son `.meta`) sans avoir d'abord réservé le verrou dans `docs/SCENE_LOCK.md`.**

Un hook automatique (`.claude/hooks/check-scene-lock.ps1`) bloque toute tentative de modification si le verrou n'est pas à ton nom. Voir `docs/TEAM_WORKFLOW.md` pour la procédure complète.

### Fichiers à risque de conflit (toujours vérifier qui travaille dessus)
- `STOP-IT/Assets/_Scenes/LivingRoom.unity` — scène unifiée
- `STOP-IT/Assets/_Scenes/LivingRoom.unity.meta`
- `STOP-IT/ProjectSettings/*.asset` (EditorBuildSettings, GraphicsSettings, ProjectSettings)
- `STOP-IT/Assets/Settings/*.asset` (URP render pipeline assets)

### Zones sûres pour travail parallèle
- `STOP-IT/Assets/_Scripts/*.cs` — chaque fichier est indépendant
- `STOP-IT/Assets/_Materials/` — assets isolés
- `STOP-IT/Assets/_Scenes/Sandboxes/` — scènes sandbox sans conflit
- Prefabs et assets individuels non référencés dans la scène principale

---

## Workflow obligatoire pour le gameplay

```
1. DÉVELOPPER dans la sandbox dédiée au scénario
   → STOP-IT/Assets/_Scenes/Sandboxes/Sandbox_<Scénario>.unity

2. TESTER dans la sandbox jusqu'à validation complète

3. VÉRIFIER le verrou (git pull + lire docs/SCENE_LOCK.md)
   → Si owner: FREE → passer à l'étape 4
   → Si owner: <quelqu'un d'autre> → attendre/communiquer

4. RÉSERVER le verrou
   → Éditer docs/SCENE_LOCK.md (owner, since, feature, expected_release)
   → git commit -m "chore(lock): claim by <nom> for <feature>"
   → git push immédiatement

5. INTÉGRER dans LivingRoom.unity (drag prefabs, copier hiérarchie, etc.)

6. LIBÉRER le verrou
   → Remettre owner: FREE, vider les autres champs
   → git commit -m "chore(lock): release"
   → git push
```

---

## Conventions MCP-Unity

Se référer à `STOP-IT/Packages/com.gamelovers.mcp-unity/CLAUDE.md` pour les conventions complètes. Points essentiels :
- Nommage outils/ressources : `lower_snake_case`
- Tout s'exécute sur le main thread Unity via `EditorCoroutineUtility`
- Serveur WebSocket : `ws://localhost:8090/McpUnity`
- Démarrer le serveur Node : `npm start` depuis `STOP-IT/Packages/com.gamelovers.mcp-unity/Server~/`

---

## Nommage des scènes sandbox

| Scénario | Fichier sandbox |
|---|---|
| Prise électrique (Salon) | `Sandbox_Outlet.unity` |
| Micro-ondes (Cuisine) | `Sandbox_Microwave.unity` |
| Escalier (Skateboard) | `Sandbox_Stairs.unity` |
| Produit nettoyant (SdB) | `Sandbox_Bathroom.unity` |
| Fenêtre / Pigeon | `Sandbox_Window.unity` |
