# Équipe d'agents STOP-IT

Équipe locale de sous-agents Claude Code dédiée au jeu VR **STOP-IT** (Unity 6 URP · OpenXR · Meta Quest 3). Point d'entrée unique : l'**orchestrateur**, qui dispatche vers 5 spécialistes.

## Comment l'utiliser

- **Point d'entrée** : laisse Claude router automatiquement (les `description:` déclenchent la sélection), ou force-le :
  `Agent(subagent_type='stopit-orchestrator')` avec ta demande.
- L'orchestrateur lit l'état du repo (git + verrou), découpe et délègue via `Agent(subagent_type='stopit-…')`.
- Tu peux aussi appeler un spécialiste directement si le domaine est évident.

## Les agents

| Agent | Domaine | MCP / Skills | Écrit la scène ? |
|-------|---------|--------------|:---:|
| `stopit-orchestrator` | Routage, découpage, suivi Jira, synthèse — **point d'entrée** | Agent, Skill, **Atlassian/Jira**, git (lecture) | non |
| `stopit-gameplay` | IA enfant, scénarios, hazards, state machine, UI gameplay | mcp-unity, `unity-xr` | non (sandbox) |
| `stopit-xr` | Interactions VR, locomotion, grab, confort, OpenXR, menu VR | mcp-unity, `unity-xr` | non (sandbox) |
| `stopit-scene` | **Intégration `LivingRoom.unity`** + gestion du verrou | mcp-unity (scène + menus), git | **oui (seul)** |
| `stopit-art` | Matériaux URP, **modélisation Blender**, modèles Synty, habillage, budget visuel | **blender-mcp**, mcp-unity, `unity-xr` | non (assets/sandbox) |
| `stopit-build` | Build Quest (QuestBuildTools), tests, profiling, adb | mcp-unity (tests/menus), bash | non |

## Matrice de routage rapide

| La demande parle de… | Agent |
|----------------------|-------|
| enfant qui se met en danger, scénario, score, spawn, countdown, hazard | `stopit-gameplay` |
| manette, grab, se déplacer, vignette, nausée, menu VR, OpenXR, test sans casque | `stopit-xr` |
| « mettre dans la scène », `LivingRoom`, placer/parenter, verrou | `stopit-scene` |
| matériau, texture, modèle 3D, Synty, look, draw calls | `stopit-art` |
| build, APK, Quest, tests, FPS, adb, perf | `stopit-build` |
| ambigu / multi-domaines | `stopit-orchestrator` |

## Règles structurantes (rappel)

1. **Sandbox-first** — gameplay/xr/art développent en sandbox ; seul `stopit-scene` intègre dans `LivingRoom.unity`.
2. **Verrou de scène** — `docs/SCENE_LOCK.md` + hook `.claude/hooks/check-scene-lock.ps1` ; `stopit-scene` fait claim → push → intégrer → release → push.
3. **Budget Quest 3** — 72 FPS · < 100 draw calls · < 750K tris/frame · textures ≤ 2K ASTC.
4. **Réutiliser avant de créer** — 23 scripts + 7 outils Editor existants.

## MCP utilisés (cf. `README.md` racine)

| MCP | Sert à | Agent(s) | Prérequis |
|-----|--------|----------|-----------|
| **mcp-unity** (`ws://localhost:8090/McpUnity`) | Piloter l'éditeur Unity : scène, GameObjects, matériaux, tests, menus `Tools → STOP IT` | tous les `stopit-*` Unity | `npm start` dans `…/com.gamelovers.mcp-unity/Server~/`, Unity ouvert |
| **blender-mcp** | Modéliser/exporter les meshes (chat, pigeon, bambin, skateboard ; pass primitives → meshes) | `stopit-art` | Blender ouvert + addon blender-mcp actif |
| **Atlassian / Jira** | Suivi de projet : tickets, sprint, transitions | `stopit-orchestrator` | MCP Atlassian connecté |

Sans MCP, les agents restent opérationnels sur le code C# (Read/Write/Edit/Grep) et le git.

## Restriction d'outils

- `stopit-gameplay`, `stopit-xr`, `stopit-scene`, `stopit-build` ont une **allowlist `tools:`** (préfixe `mcp__mcp-unity__*`, portable).
- `stopit-orchestrator` et `stopit-art` n'ont **pas** de `tools:` figé (ils héritent de tout) car leurs MCP — **Jira** et **blender-mcp** — ont des identifiants d'outils non portables/non garantis ici ; le scoping se fait par leur prompt, et le **hook de verrou protège la scène quoi qu'il arrive**.

## Modèle

Les agents **héritent du modèle de la session** (aucun `model:` figé). Pour réduire les coûts, tu peux ajouter `model: sonnet` au frontmatter d'un agent d'exécution.
