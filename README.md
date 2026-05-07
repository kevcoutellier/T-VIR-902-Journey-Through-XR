# STOP IT! — Journey Through XR

> Virtual Reality experience — EPITECH T-VIR-902
> An immersive VR experience where the player (adult) must prevent a mischievous child NPC from causing domestic accidents, in increasingly chaotic scenarios.

---

## Concept

The player wears a Meta Quest 3 headset and embodies an adult in a household environment. A child NPC autonomously navigates toward household hazards (electrical outlets, kitchen appliances, windows, etc.). The player must physically intercept the child before each accident occurs — against the clock, with escalating difficulty and a deliberately wacky/cartoon tone.

---

## Project Status

**Current milestone: 5-scenario house playable end-to-end + in-engine desktop test mode + Quest 3 build pipeline.**

| Area | Status |
|---|---|
| 2-floor house built (Living Room, Kitchen, Bathroom, Stairs, Bedroom) | ✅ Done |
| All 5 scenarios wired (outlet / microwave / cleaning product / stairs / window) | ✅ Done |
| XR Origin + OpenXR + Oculus + Joystick locomotion + wall collisions | ✅ Done |
| NavMesh baked, ChildNPC re-targets dynamically + cartoon walk anim | ✅ Done |
| Core scripts (GameManager / ScenarioManager / ChildNPC / HazardZone / UI) | ✅ Done |
| World-space HUD (timer pulse, feedback bounce) + Floating VR menu (Y button) | ✅ Done |
| UX layer (HazardIndicator, DangerVignette, IntroCountdown, CameraShake) | ✅ Done |
| Player hand interception + haptics + auto SphereCollider | ✅ Done |
| **Desktop test mode (WASD/RMB/LMB) — no headset required** | ✅ Done |
| **One-click Quest 3 build pipeline (Tools → STOP IT)** | ✅ Done |
| User testing + feedback iteration | 🔜 Next milestone |
| Audio polish (child laugh, hazard SFX, ambient) | 🔜 Next |
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

## Running the project

### Option A — Desktop test mode (no headset needed) — recommended for iteration

The fastest way to iterate on gameplay. Boots in seconds with keyboard + mouse.

1. Open `Assets/_Scenes/LivingRoom.unity`
2. (one-time) Run **Tools → STOP IT → Bake NavMesh**
3. (one-time) Run **Tools → STOP IT → Add Desktop Test Rig** — adds `DesktopTestRig` to the XR Origin
4. Press **Play**

**Controls:**
| Input | Action |
|---|---|
| `W A S D` / arrows | Walk |
| `Shift` (held) | Sprint |
| Right mouse button + move | Look around |
| Left click | Brief block hand — reflex slap to stop the child |
| `E` (held) | Persistent grab hand — pick up the baby (one-shot save) |
| `R` | Restart current scenario |
| `M` or `Tab` | Toggle scenario menu |
| `Esc` | Release mouse cursor |

`DesktopTestRig` automatically disables itself when an HMD runtime is detected, so it can stay in the scene for headset builds. The XR locomotion binder also gets disabled in this mode.

### Option B — Editor play mode with Mock HMD / Quest Link

1. Steps 1–2 above
2. Either: connect a Quest via Quest Link **or** enable Unity Mock HMD in **Edit → Project Settings → XR Plug-in Management → PC**
3. Press **Play**

**Expected behavior:**
- HUD appears (scenario name, timer, score). Press **Y** (left controller) or **M** (desktop) to toggle the floating menu.
- A 3-2-1-GO countdown plays, then the Child walks toward the hazard.
- The hazard pulses red and emits sparks as the child gets closer; a danger vignette tints the screen.
- Block the child with your hand (or click in desktop mode) → success. Otherwise the child reaches the hazard → fail with camera shake + zap VFX.

### Option C — Build & deploy to Meta Quest 3

One-click pipeline:

1. Run **Tools → STOP IT → Configure Android Settings** once per machine (sets ARM64 / IL2CPP / Vulkan, package id, SDK levels)
2. Connect the Quest via USB, enable developer mode + USB debugging, and accept the host computer prompt inside the headset
3. Run **Tools → STOP IT → Build & Run on Quest** — produces `Builds/Quest/STOP_IT_XR-<timestamp>.apk` and auto-deploys via adb

For a build only (no deploy), use **Tools → STOP IT → Build Quest 3 (.apk)**. Open the resulting folder via **Tools → STOP IT → Open Builds Folder**.

---

## Project Structure

```
STOP-IT/                                       ← Unity project root
├── Assets/
│   ├── _Scenes/
│   │   └── LivingRoom.unity                  ← Single scene with all 5 scenarios
│   ├── _Scripts/
│   │   ├── GameManager.cs                    ← State machine, timer, score, events
│   │   ├── ScenarioManager.cs                ← Scenario sequencing + OnScenarioActivated event
│   │   ├── ChildNPC.cs                       ← NavMeshAgent + cartoon walk + dynamic re-target
│   │   ├── HazardZone.cs                     ← Pulse / sparks / hum / fail VFX
│   │   ├── HazardIndicator.cs                ← Floating arrow above the active hazard
│   │   ├── DangerVignette.cs                 ← Fullscreen red overlay tied to proximity
│   │   ├── CameraShake.cs                    ← Static helper for screen-shake on fail
│   │   ├── ScenarioIntroCountdown.cs         ← 3-2-1-GO before each scenario
│   │   ├── ScenarioUI.cs                     ← World-space HUD bindings
│   │   ├── ScenarioMenu.cs                   ← Floating VR menu (Y button)
│   │   ├── ScenarioSpawner.cs                ← Single-scene fallback spawner
│   │   ├── PlayerBlocker.cs                  ← Hand trigger that intercepts the child (reflex slap)
│   │   ├── ChildGrabber.cs                   ← Hand grab that picks the baby up (deliberate save)
│   │   ├── XRLocomotionBinder.cs             ← Joystick movement + wall collision + menu toggle + grip→grab
│   │   ├── XRCameraFix.cs                    ← Runtime XROrigin.Camera wiring
│   │   ├── DesktopTestRig.cs                 ← Keyboard/mouse mode for editor iteration
│   │   └── Editor/
│   │       ├── HouseBuilder.cs               ← Generates the 5-room house procedurally
│   │       ├── StopItBuildTools.cs           ← Tools > STOP IT menu (scene wiring)
│   │       └── QuestBuildTools.cs            ← Tools > STOP IT menu (Quest 3 build pipeline)
│   ├── _Materials/                           ← Floor / Wall / Furniture / Child / Hazard
│   └── Samples/                              ← XRI 3.4.0 Starter Assets
├── Builds/
│   └── Quest/                                ← Generated APKs land here (gitignored)
├── Packages/
│   ├── manifest.json
│   └── com.gamelovers.mcp-unity/             ← MCP Unity package
└── ProjectSettings/
```

---

## Editor Tools

The project ships with a **Tools → STOP IT** menu that automates common scene setup tasks:

| Menu item | What it does |
|---|---|
| **Build House** | Creates the full 2-floor house (5 rooms, staircase, hazards, spawn points, light) |
| **Bake NavMesh** | Rebuilds the `NavMeshSurface` on the floors |
| **Setup Scene** | Wires `ChildNPC.targetHazard`, `HazardZone.hazardRenderer`, `GameManager.audioSource` |
| **Setup UI** | Creates TMP elements in `ScenarioCanvas` (timer, score, feedback, scenario name) |
| **Setup UX (Indicator + Vignette + Countdown + Shake)** | Adds the full UX layer in one click |
| **Fix XR Camera** | Assigns the camera to `XROrigin.m_Camera`, adds `TrackedPoseDriver` + `AudioListener` |
| **Fix Hand Colliders** | Adds trigger `SphereCollider` (r=0.08) to each `PlayerBlocker` hand |
| **Wire Scenarios** | Auto-fills the `ScenarioManager.scenarios` array from the `HazardZone_*` and `SpawnPlayer_*` / `SpawnChild_*` GameObjects |
| **Create Menu** | Spawns the floating world-space `ScenarioMenu` (toggled with the Y button) |
| **Add Desktop Test Rig** | Attaches `DesktopTestRig` to the XR Origin for keyboard/mouse testing |
| **Configure Android Settings** | Switches PlayerSettings to Quest-friendly values (ARM64 / IL2CPP / Vulkan) |
| **Build Quest 3 (.apk)** | Builds an APK into `Builds/Quest/` (timestamped filename) |
| **Build & Run on Quest** | Builds + deploys + launches via `adb` (Quest must be USB-connected) |
| **Open Builds Folder** | Opens the `Builds/Quest/` folder in the OS file explorer |

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

## Scenarios

| # | Location | Child's Action | Player Response | Status |
|---|---|---|---|---|
| 1 | Living Room | Sticks fork in electrical outlet | Block the child / cover the outlet | ✅ Wired |
| 2 | Kitchen | Puts cat in microwave | Intercept before door closes | ✅ Wired |
| 3 | Bathroom | Drinks cleaning product | Swap the bottle | ✅ Wired |
| 4 | Stairs | Launches on skateboard down the stairs | Block the descent | ✅ Wired |
| 5 | Bedroom (1F) | Climbs window ledge to catch a pigeon | Pull child back | ✅ Wired |

All scenarios share the same loop (timer + intercept + win/fail VFX). Use the floating VR menu (Y button on left controller, or `M` on desktop) to launch any scenario individually, or `>>> JOUER TOUT <<<` to run them all in sequence.

### Two ways to stop the baby

The player has **two complementary verbs** to win a scenario:

- **Block** (reflex slap) — touch the baby with any hand collider. Fast, panicky.
- **Grab** (deliberate save) — press the grip button on either controller (or hold `E` on desktop) when close to the baby. The baby is picked up and the scenario is won immediately.

Each scenario opens with a short verb hint ("ATTRAPE LE BÉBÉ !" / "BLOQUE-LE !" / "PORTE-LE LOIN DE LA FENÊTRE !") to guide first-time players. The hint fades after ~2 seconds; both verbs always work regardless of which one was hinted.

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

## User Testing Protocol

For each test session:

1. **Brief (30s)** — "You're an adult at home. A toddler is about to do something dangerous. Block them with your hands."
2. **Hands-off run** — let the tester open the menu (Y button) and pick a scenario. Don't help.
3. **Capture** — write down everything they say out loud, including silence. Note what they look at first, where they hesitate, and what makes them laugh / wince.
4. **Repeat** — run all 5 scenarios in order on the same person. Watch for fatigue or motion sickness.
5. **Debrief (1–2 min)** — three open questions: *What did you understand the goal was?* / *What surprised you?* / *What did you want to do that the game didn't let you do?*

Collect raw notes per session. After 3+ testers, look for patterns — that's the signal worth fixing for the next iteration.

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
