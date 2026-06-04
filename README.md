# ThreeJSSX — ThreeJSSX

A dotnet web app that extracts SSX3 PS2 level data and renders terrain maps using Three.js.

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
- An SSX3 PS2 ISO placed at `ISO/SSX 3.iso` (not included in this repo)

## Quick start

```bash
# Clone with submodule
git clone --recurse-submodules <repo-url>
cd ThreeJSSX

# Place your SSX3 PS2 ISO at ISO/SSX 3.iso

# Run
cd ThreeJSSX
dotnet run
```

Open `http://localhost:5000` (or the URL shown in the console).

**First run** takes ~2 minutes to extract all 49 zones from the ISO. Subsequent runs use the cache. Each level you click generates its `.glb` (3–6 seconds for large tracks, cached for future visits).

## Project structure

```
ThreeJSSX/
├── ThreeJSSX/          # ASP.NET Core web app
│   ├── Program.cs          # Minimal API: /api/levels, /api/levels/{name}/glb
│   ├── Services/
│   │   ├── IsoService.cs         # ISO mounting, BIG extraction, SSB parsing
│   │   ├── PatchTessellator.cs   # Bezier 4×4 → smooth triangle mesh
│   │   └── MapGlbExporter.cs     # Level → GLB assembler (patches + instances + textures)
│   └── wwwroot/
│       └── index.html            # Three.js viewer with level picker
├── SSX-Library/            # Git submodule (data parsing library)
└── ISO/                    # Place SSX 3.iso here (gitignored)
```

## API endpoints

| Endpoint | Description |
|----------|-------------|
| `GET /api/levels` | List all 49 extracted zones |
| `GET /api/levels/{name}/glb` | Generate and serve GLB for a zone |
| `POST /api/levels/extract` | Force re-extraction from ISO |

## Controls

| Action | Control |
|--------|---------|
| Rotate | Left-click drag |
| Zoom | Scroll wheel |
| Pan | Right-click drag |
| Switch level | Click level name in sidebar |
