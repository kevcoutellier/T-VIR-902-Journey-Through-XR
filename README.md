# STOP IT! — Journey Through XR

> Virtual Reality experience — EPITECH T-VIR-902
> An immersive VR experience where the player (adult) must prevent a mischievous child NPC from causing domestic accidents, in increasingly chaotic scenarios.

---

## Concept

The player wears a Meta Quest 3 headset and embodies an adult in a household environment. A child NPC autonomously navigates toward household hazards (electrical outlets, kitchen appliances, windows, etc.). The player must physically intercept the child before each accident occurs — against the clock, with escalating difficulty and a deliberately wacky/cartoon tone.

---

## Project Status

**Current milestone: Playable POC — Living Room scenario (scenario #1).**

| Area | Status |
|---|---|
| Living Room scene built (walls, sofa, TV stand, coffee table, outlet) | ✅ Done |
| XR Origin + OpenXR + Mock HMD (editor testing without headset) | ✅ Done |
| NavMesh baked on Floor, ChildNPC navigates to hazard | ✅ Done |
| Core gameplay scripts (GameManager, ChildNPC, HazardZone, UI) | ✅ Done |
| World-space HUD (timer, score, scenario name, feedback) | ✅ Done |
| Player hand blocking (PlayerBlocker trigger) | ✅ Scripts done, hand wiring pending |
| Scenarios 2–5 (kitchen, stairs, bathroom, window) | 🔜 Next |
| Audio (child laugh, hazard SFX, ambient) | 🔜 Next |
| RandomCorp™ mesh swap (final art pass) | ⏳ Blocked on asset drop |

---

## Tech Stack

| Role | Technology | MCP Available |
|---|---|---|
| Game Engine | Unity 6.4 (URP, 6000.4.0f1) | ✅ mcp-unity |
| XR Runtime | OpenXR + Meta Quest 3 | — |
| XR Interactions | XR Interaction Toolkit 3.4.0 | — |
| NPC Pathfinding | Unity AI Navigation 2.0.11 (NavMesh) | — |
| Input | Unity Input System + TrackedPoseDriver | — |
| UI | TextMeshPro (world-space canvas) | — |
| 3D Assets (placeholder) | Unity Primitives → RandomCorp™ swap | ✅ blender-mcp |
| Project Management | Jira (Atlassian) | ✅ Atlassian MCP |
| AI Control | Claude Code + mcp-unity | ✅ |

---

## Prerequisites

- Unity 6.4+ (6000.4.0f1 recommended)
- Node.js v18+
- Meta Quest 3 headset **or** Unity Mock HMD (for editor testing)
- Claude Code with MCP support (optional, for AI-assisted editing)

---

## Unity Project Setup

### 1. Clone this repository

```bash
git clone https://github.com/kevcoutellier/T-VIR-902-Journey-Through-XR.git
cd T-VIR-902-Journey-Through-XR
```

### 2. Open the Unity project

Open **Unity Hub** → **Add project from disk** → select the `STOP-IT/` folder.
Use **Unity 6.4 (6000.4.0f1)** or newer.

### 3. Unity packages (auto-restored)

All packages are defined in `Packages/manifest.json` and will be restored on first open. Key packages:

| Package | Version | Purpose |
|---|---|---|
| XR Interaction Toolkit | 3.4.0 | VR controller interactions |
| XR Plugin Management | latest | Runtime management |
| OpenXR Plugin | latest | Cross-platform XR standard |
| AI Navigation | 2.0.11 | NPC pathfinding (NavMeshSurface) |
| Input System | latest | TrackedPoseDriver + XR bindings |
| TextMeshPro | latest | World-space HUD |
| MCP Unity (CoderGamester) | 1.2.0 | Claude Code bridge |

To add **MCP Unity** manually if missing:
**Window → Package Manager → + → Add package by URL**
```
https://github.com/CoderGamester/mcp-unity.git
```

### 4. Configure XR Plug-in Management

**Edit → Project Settings → XR Plug-in Management**

**PC tab:**
```
✅ OpenXR
✅ Unity Mock HMD   ← for editor testing without headset
```

**Android tab:**
```
✅ OpenXR           ← for Meta Quest 3 deployment
```

**OpenXR sub-menu — Interaction Profiles:**
```
✅ Meta Quest Touch Pro Controller Profile
✅ Oculus Touch Controller Profile
```

### 5. Switch build target to Android (Quest deployment)

**File → Build Settings → Android → Switch Platform**

---

## Running the POC (Living Room scenario)

### Option A — Editor play mode (no headset needed)

1. Open the scene `Assets/_Scenes/LivingRoom.unity`
2. Run **Tools → STOP IT → Bake NavMesh** (if the floor NavMesh is missing)
3. Run **Tools → STOP IT → Fix XR Camera** (ensures the XR Origin camera is wired correctly — see [XR Camera fix](#xr-camera-fix-note) below)
4. Run **Tools → STOP IT → Setup UI** (creates the TMP timer/score/feedback texts in the HUD)
5. Run **Tools → STOP IT → Setup Scene** (auto-wires `targetHazard`, `hazardRenderer`, `audioSource`)
6. Press **Play**

**Expected behavior:**
- HUD appears (scenario name, timer = 30, score = 0/3)
- After a 2-second delay, the Child NPC (blue capsule) walks toward the yellow outlet
- The outlet pulses red when the child is within 1.5 m
- If the child reaches the outlet → fail (flash + fail reported to GameManager)
- If the player intercepts (via `PlayerBlocker` trigger on the hands) → success

### Option B — Meta Quest 3 build

1. Complete steps 1–5 above
2. Connect Quest via USB, enable developer mode
3. **File → Build Settings → Build and Run**

---

## Project Structure

```
STOP-IT/                              ← Unity project root
├── Assets/
│   ├── _Scenes/
│   │   └── LivingRoom.unity         ← Scenario #1 scene
│   ├── _Scripts/
│   │   ├── GameManager.cs           ← State machine, timer, score, events
│   │   ├── ChildNPC.cs              ← NavMeshAgent-driven child behaviour
│   │   ├── HazardZone.cs            ← Hazard detection, pulse, fail trigger
│   │   ├── ScenarioUI.cs            ← World-space HUD bindings
│   │   ├── ScenarioSpawner.cs       ← Sequential scenario lifecycle
│   │   ├── PlayerBlocker.cs         ← Hand trigger that blocks the NPC
│   │   ├── XRCameraFix.cs           ← Runtime XROrigin.Camera wiring
│   │   └── Editor/
│   │       └── StopItBuildTools.cs  ← Tools > STOP IT menu (6 utilities)
│   ├── _Materials/
│   │   ├── Mat_Floor, Mat_Wall, Mat_Furniture
│   │   ├── Mat_Child (blue), Mat_Hazard (yellow)
│   └── Samples/
│       └── XR Interaction Toolkit/3.4.0/Starter Assets/
├── Packages/
│   ├── manifest.json
│   └── com.gamelovers.mcp-unity/    ← MCP Unity package
└── ProjectSettings/
```

---

## Editor Tools

The project ships with a **Tools → STOP IT** menu that automates common scene setup tasks:

| Menu item | What it does |
|---|---|
| **Bake NavMesh** | Rebuilds the `NavMeshSurface` on the Floor |
| **Setup Scene** | Wires `ChildNPC.targetHazard`, `HazardZone.hazardRenderer`, `GameManager.audioSource` |
| **Setup UI** | Creates TMP elements in `ScenarioCanvas` (timer, score, feedback, scenario name) |
| **Fix XR Camera** | Assigns the camera to `XROrigin.m_Camera`, adds `TrackedPoseDriver` + `AudioListener` |
| **Fix Hand Colliders** | Adds trigger `SphereCollider` (r=0.08) to each `PlayerBlocker` hand |

All tools save the scene automatically when run outside Play mode.

---

## XR Camera fix (note)

The XRI Starter Assets prefab `XR Origin (XR Rig)` ships with an **unassigned `m_Camera` field**. At runtime this causes `GravityProvider.Update()` to throw every frame, which silently prevents `ChildNPC.NavMeshAgent` from moving and `GameManager` from ticking the timer.

Two mitigations are in place:
1. **Editor-time** — `Tools → STOP IT → Fix XR Camera` assigns the camera via `SerializedObject` (persisted in the scene).
2. **Runtime-time** — `XRCameraFix.cs` (attached to the `GameManager` GameObject, `[DefaultExecutionOrder(-1000)]`) re-assigns `m_Camera` via reflection before `XROrigin.Awake()` runs. This guarantees the fix survives prefab reverts or fresh clones.

If you ever see `UnassignedReferenceException: m_Camera of XROrigin has not been assigned`, run the editor tool once and make sure `XRCameraFix` is present on `GameManager`.

---

## MCP Unity Configuration (Claude Code, optional)

This allows Claude Code to directly control the Unity Editor (create GameObjects, write scripts, manage scenes, bake NavMesh, run menu items).

### Step 1 — Build the MCP server

The MCP server is bundled inside the Unity package cache. Build it once:

```bash
cd "C:/Users/<you>/Documents/EPITECH/T-VIR-902-Journey-Through-XR/STOP-IT/Library/PackageCache/com.gamelovers.mcp-unity@<hash>/Server~"
npm install
npm run build
```

### Step 2 — Register the MCP server in Claude Code

```bash
claude mcp add --scope user mcp-unity node "C:/.../Server~/build/index.js"
```

### Step 3 — Start the Unity MCP server

In Unity: **Window → MCP Unity** → verify **Status: Server Online** on port `8090`.

### Step 4 — Restart Claude Code

```bash
claude mcp list
```
Expected:
```
mcp-unity: node ... - ✓ Connected
```

---

## Scenarios (Planned)

| # | Location | Child's Action | Player Response | Status |
|---|---|---|---|---|
| 1 | Living Room | Sticks fork in electrical outlet | Block the child / cover the outlet | ✅ POC done |
| 2 | Kitchen | Puts cat in microwave | Intercept before door closes | 🔜 |
| 3 | Stairs | Launches on skateboard down the stairs | Block the descent | 🔜 |
| 4 | Bathroom | Drinks cleaning product | Swap the bottle | 🔜 |
| 5 | Window | Climbs ledge to catch a pigeon | Pull child back | 🔜 |

---

## Asset Policy

Per project brief: **only Unity primitives or RandomCorp™ Media assets** are to be used. No external asset store purchases.

**Current placeholder convention:**
```
Child NPC   → Blue Capsule (scale 0.6, height 2)
Hazard Zone → Yellow Cube (0.15 × 0.2 × 0.05 for outlet)
Environment → Grey Plane + Coloured Cubes (walls, furniture)
```

Assets from the RandomCorp™ Media drop (last week of project) will replace placeholders with a mesh swap — no logic changes required.

---

## Promotional Video

A promotional video focusing on user needs and key features is due at the final deadline alongside the POC.

---

## Team

| Name | Role |
|---|---|
| kevcoutellier | Project Lead / XR Developer |

---

## License

EPITECH internal project — not for public distribution.
