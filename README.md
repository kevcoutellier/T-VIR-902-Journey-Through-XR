# STOP IT! — Journey Through XR

> Augmented Reality experience — EPITECH T-VIR-902
> An immersive VR experience where the player (adult) must prevent a mischievous child NPC from causing domestic accidents, in increasingly chaotic scenarios.

---

## Concept

The player wears a Meta Quest 3 headset and embodies an adult in a household environment. A child NPC autonomously navigates toward household hazards (electrical outlets, kitchen appliances, windows, etc.). The player must physically intercept the child before each accident occurs — against the clock, with escalating difficulty and a deliberately wacky/cartoon tone.

---

## Tech Stack

| Role | Technology | MCP Available |
|---|---|---|
| Game Engine | Unity 6.4 (URP) | ✅ mcp-unity |
| XR Runtime | OpenXR + Meta Quest 3 | — |
| XR Interactions | XR Interaction Toolkit 3.4.0 | — |
| NPC Pathfinding | Unity AI Navigation (NavMesh) | — |
| 3D Assets (placeholder) | Unity Primitives → Blender | ✅ blender-mcp |
| Project Management | Jira (Atlassian) | ✅ Atlassian MCP |
| AI Control | Claude Code + mcp-unity | ✅ |

---

## Prerequisites

- Unity 6.4+ (6000.4.0f1 recommended)
- Node.js v18+
- Meta Quest 3 headset (or Unity Mock HMD for editor testing)
- Claude Code with MCP support

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

### 3. Install Unity packages (Package Manager)

All packages are saved in `Packages/manifest.json` and will be restored automatically. Key packages:

| Package | Version | Purpose |
|---|---|---|
| XR Interaction Toolkit | 3.4.0 | VR controller interactions |
| XR Plugin Management | latest | Runtime management |
| OpenXR Plugin | latest | Cross-platform XR standard |
| AI Navigation | latest | NPC pathfinding (NavMesh) |
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

**Edit → Project Settings → XR Plug-in Management → OpenXR**
Add Interaction Profiles:
```
✅ Meta Quest Touch Pro Controller Profile
✅ Oculus Touch Controller Profile
```

### 5. Switch build target to Android

**File → Build Settings → Android → Switch Platform**

---

## MCP Unity Configuration (Claude Code)

This allows Claude Code to directly control the Unity Editor (create objects, write scripts, manage scenes).

### Step 1 — Build the MCP server

The MCP server is bundled inside the Unity package cache. Build it once:

```bash
cd "C:/Users/<you>/STOP-IT/Library/PackageCache/com.gamelovers.mcp-unity@72c005fa0ae2/Server~"
npm install
npm run build
```

### Step 2 — Register the MCP server in Claude Code

```bash
claude mcp add --scope user mcp-unity node "C:/Users/<you>/STOP-IT/Library/PackageCache/com.gamelovers.mcp-unity@72c005fa0ae2/Server~/build/index.js"
```

> Replace `<you>` with your Windows username.

### Step 3 — Start the Unity MCP server

In Unity: **Window → MCP Unity**
Verify **Status: Server Online** on port `8090`.

### Step 4 — Restart Claude Code

On next startup, run:
```bash
claude mcp list
```
Expected output:
```
mcp-unity: node ... - ✓ Connected
```

---

## Project Structure

```
STOP-IT/                        ← Unity project root
├── Assets/
│   ├── _Scenes/                ← Game scenes
│   ├── _Scripts/               ← C# game logic
│   ├── _Prefabs/               ← Reusable GameObjects
│   ├── _Materials/             ← Materials & colors
│   ├── _Audio/                 ← Sound effects
│   └── Samples/
│       └── XR Interaction Toolkit/
│           └── 3.4.0/
│               └── Starter Assets/
├── Packages/
│   └── manifest.json
└── ProjectSettings/
```

---

## Scenarios (Planned)

| # | Location | Child's Action | Player Response |
|---|---|---|---|
| 1 | Living Room | Sticks fork in electrical outlet | Grab the fork / cover the outlet |
| 2 | Kitchen | Puts cat in microwave | Intercept before door closes |
| 3 | Stairs | Launches on skateboard down the stairs | Block the descent |
| 4 | Bathroom | Drinks cleaning product | Swap the bottle |
| 5 | Window | Climbs ledge to catch a pigeon | Pull child back |

---

## Asset Policy

Per project brief: **only Unity primitives or RandomCorp™ Media assets** are to be used. No external asset store purchases.

**Placeholder convention:**
```
Child NPC   → Red Capsule (small scale)
Adult/Player→ Blue Capsule (large scale)
Hazard Zone → Yellow Sphere/Cube
Environment → Grey Plane + White Cubes
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
