# ThreeJSSX

A .NET + Three.js web app that extracts SSX3 PS2 level data and renders terrain maps in the browser.

![EBC3 map](docs/ebc3.png)

## How it works

1. **Mounts** the SSX3 PS2 ISO and extracts `BAM.BIG` (113MB world archive)
2. **Parses** 49 track/zone levels using [SSX-Library](https://github.com/GlitcherOG/SSX-Library) (git submodule)
3. **Tessellates** Bezier terrain patches into smooth triangle meshes with UVs and normals
4. **Places** instanced 3D models (trees, rocks, buildings) at their world transforms
5. **Exports** the assembled level as a self-contained `.glb` file
6. **Renders** the result in the browser via Three.js with orbit controls

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- An SSX3 PS2 ISO (NTSC or PAL) — not included

## Setup (clean clone)

```bash
# 1. Clone including the SSX-Library submodule
git clone --recurse-submodules https://github.com/andersfischern/ThreeJSSX.git
cd ThreeJSSX

# If you already cloned without --recurse-submodules:
git submodule update --init --recursive

# 2. Place your SSX3 PS2 ISO here (exact filename matters):
#    ISO/SSX 3.iso

# 3. Build and run
cd ThreeJSSX
dotnet run
```

Open `http://localhost:5000` in your browser.

**First run:** The app extracts all 49 zones from the ISO automatically when the page loads. This takes ~2 minutes and shows "Extracting level data from ISO…" in the UI. Subsequent runs use the cache and start instantly.

**Per-level GLBs** are generated on first click and cached. Large tracks take 3–6 seconds to generate.

## Re-exporting

### Re-export a single level's GLB

Delete its cached file and click it again in the sidebar:

```bash
rm ThreeJSSX/wwwroot/maps/glb/<LevelName>.glb
```

Or via the API:

```bash
curl -X DELETE http://localhost:5000/api/levels/<LevelName>/glb
```

### Re-export all GLBs

```bash
curl -X DELETE http://localhost:5000/api/cache/glb
```

Then click any level in the sidebar to regenerate on demand.

### Re-extract level data from ISO

If you want to re-parse everything from the ISO (e.g. after updating SSX-Library):

```bash
curl -X POST http://localhost:5000/api/levels/extract
```

This deletes the extracted data and re-runs the full extraction (~2 min). GLBs are unaffected — delete them separately if needed.

## Project structure

```
ThreeJSSX/
├── ThreeJSSX/                  # ASP.NET Core web app
│   ├── Program.cs              # Minimal API endpoints
│   ├── Services/
│   │   ├── IsoService.cs       # ISO mounting, BIG extraction, SSB parsing
│   │   ├── PatchTessellator.cs # Bezier 4×4 → smooth triangle mesh
│   │   └── MapGlbExporter.cs  # Level → GLB assembler (patches + instances + textures)
│   └── wwwroot/
│       ├── index.html          # Three.js viewer with level picker
│       └── maps/glb/           # GLB cache (gitignored, auto-generated)
├── SSX-Library/                # Git submodule — GlitcherOG/SSX-Library
└── ISO/                        # Place SSX 3.iso here (gitignored)
```

## API reference

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET` | `/api/levels` | List all extracted zones (triggers extraction on first call) |
| `GET` | `/api/levels/{name}/glb` | Serve GLB for a zone (generates and caches on first request) |
| `POST` | `/api/levels/extract` | Force full re-extraction from ISO |
| `DELETE` | `/api/levels/{name}/glb` | Delete cached GLB for a level |
| `DELETE` | `/api/cache/glb` | Delete all cached GLBs |

## Controls

**Orbit mode** (default — for framing the starting view):

| Action | Control |
|--------|---------|
| Rotate | Left-drag |
| Zoom | Scroll |
| Pan | Right-drag |

**Fly mode** (click the **Fly mode** button to engage):

| Action | Control |
|--------|---------|
| Move forward / strafe | W / A / S / D |
| Look around | Mouse |
| Move up | Space |
| Move down | C (or Ctrl) |
| Sprint (4× speed) | Shift |
| Return to orbit | Esc |

**Sidebar:**

| Action | Control |
|--------|---------|
| Switch level | Click level name |
| Toggle triggers | Checkbox |
