using SSXLibrary.JsonFiles.SSX3;
using SSXLibrary.FileHandlers.LevelFiles.SSX3PS2.SSBData;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using System.Numerics;

namespace ThreeJSSX.Services;

public class MapGlbExporter
{
    private readonly ILogger<MapGlbExporter> _logger;

    public MapGlbExporter(ILogger<MapGlbExporter> logger)
    {
        _logger = logger;
    }

    public string BuildGlb(string levelDir, string outputDir)
    {
        var levelName = Path.GetFileName(levelDir);
        _logger.LogInformation("Building GLB for level: {Name}", levelName);

        var scene = new SceneBuilder();
        var defaultMat = new MaterialBuilder("default")
            .WithChannelParam(KnownChannel.BaseColor, KnownProperty.RGBA, new Vector4(0.7f, 0.7f, 0.7f, 1));
        var materialCache = new Dictionary<int, MaterialBuilder>();

        var patchesPath = Path.Combine(levelDir, "Patches.json");
        var instancesPath = Path.Combine(levelDir, "Instances.json");
        var prefabsPath = Path.Combine(levelDir, "Prefabs.json");
        var bin0Path = Path.Combine(levelDir, "Bin0.json");
        var texturesSrc = Path.Combine(levelDir, "..", "..", "Textures");
        var lightmapsSrc = Path.Combine(levelDir, "..", "..", "Lightmaps");

        var patchesJson = File.Exists(patchesPath)
            ? PatchesJsonHandler.Load(patchesPath) : new PatchesJsonHandler();
        var instancesJson = File.Exists(instancesPath)
            ? InstanceJsonHandler.Load(instancesPath) : new InstanceJsonHandler();
        var mdrJson = File.Exists(prefabsPath)
            ? MDRJsonHandler.Load(prefabsPath) : new MDRJsonHandler();
        var bin0Json = File.Exists(bin0Path)
            ? Bin0JsonHandler.Load(bin0Path) : new Bin0JsonHandler();

        var texCache = new Dictionary<int, string>();
        if (Directory.Exists(texturesSrc))
        {
            foreach (var texFile in Directory.GetFiles(texturesSrc, "*.png"))
            {
                var name = Path.GetFileNameWithoutExtension(texFile);
                if (int.TryParse(name, out int rid))
                    texCache[rid] = texFile;
            }
        }

        var lightmapCache = new Dictionary<int, string>();
        if (Directory.Exists(lightmapsSrc))
        {
            foreach (var lmFile in Directory.GetFiles(lightmapsSrc, "*.png"))
            {
                var name = Path.GetFileNameWithoutExtension(lmFile);
                if (int.TryParse(name, out int rid))
                    lightmapCache[rid] = lmFile;
            }
        }
        var materialCache2 = new Dictionary<(int tex, int lm), MaterialBuilder>();

        _logger.LogInformation("  Patches: {Count}", patchesJson.Patches.Count);
        foreach (var patch in patchesJson.Patches)
        {
            var tessellated = PatchTessellator.Tessellate(patch.Points, patch.UVPoints, patch.LightMapPoint, subdiv: 12);
            var mat = GetPatchMaterial(materialCache2, texCache, lightmapCache, patch.TextureRID, patch.LightmapRID, patch.Name);
            var mesh = new MeshBuilder<VertexPositionNormal, VertexTexture2>(patch.Name);
            var prim = mesh.UsePrimitive(mat);

            for (int i = 0; i < tessellated.Indices.Count; i += 3)
            {
                var va = tessellated.Vertices[tessellated.Indices[i]];
                var vb = tessellated.Vertices[tessellated.Indices[i + 1]];
                var vc = tessellated.Vertices[tessellated.Indices[i + 2]];

                prim.AddTriangle(
                    (new VertexPositionNormal(new Vector3(va.x, va.y, va.z), new Vector3(va.nx, va.ny, va.nz)), new VertexTexture2(new Vector2(va.u, va.v), new Vector2(va.lu, va.lv))),
                    (new VertexPositionNormal(new Vector3(vb.x, vb.y, vb.z), new Vector3(vb.nx, vb.ny, vb.nz)), new VertexTexture2(new Vector2(vb.u, vb.v), new Vector2(vb.lu, vb.lv))),
                    (new VertexPositionNormal(new Vector3(vc.x, vc.y, vc.z), new Vector3(vc.nx, vc.ny, vc.nz)), new VertexTexture2(new Vector2(vc.u, vc.v), new Vector2(vc.lu, vc.lv))));
            }
            scene.AddRigidMesh(mesh, Matrix4x4.Identity);
        }

        _logger.LogInformation("  Instances: {Count}", instancesJson.Instances.Count);
        var modelsDir = Path.Combine(levelDir, "Models");
        var mdrBinDir = Path.Combine(levelDir, "MDR");
        var triggerMat = new MaterialBuilder("__trigger__")
            .WithChannelParam(KnownChannel.BaseColor, KnownProperty.RGBA, new Vector4(1, 0.2f, 0.8f, 0.35f))
            .WithAlpha(AlphaMode.BLEND)
            .WithDoubleSide(true);

        var prefabMatCache = new Dictionary<(int rid, int track), MaterialBuilder>();
        // Structured-MDR per-prefab cache. Each entry = list of (sectioned mesh, local transform).
        // Built once per prefab, then instanced cheaply for each Instance referencing it.
        var prefabSectionMeshCache = new Dictionary<(int rid, int track),
            List<(MeshBuilder<VertexPositionNormal, VertexTexture1> mesh, Matrix4x4 localMat)>?>();

        int structuredCount = 0, fallbackCount = 0;
        foreach (var instance in instancesJson.Instances)
        {
            var mdr = mdrJson.mainModelHeaders.FirstOrDefault(m =>
                m.RID == instance.ModelRID && m.TrackID == instance.ModelTrackID);
            if (mdr.ModelObjects == null) continue;

            var prefabName = mdr.Name ?? "";
            bool isTrigger = IsTriggerPrefab(prefabName);

            var pos = instance.Position != null && instance.Position.Length >= 3
                ? new Vector3(instance.Position[0], instance.Position[1], instance.Position[2]) : Vector3.Zero;
            var rot = instance.Rotation != null && instance.Rotation.Length >= 4
                ? new Quaternion(instance.Rotation[0], instance.Rotation[1], instance.Rotation[2], instance.Rotation[3]) : Quaternion.Identity;
            var scl = instance.Scale != null && instance.Scale.Length >= 3
                ? new Vector3(instance.Scale[0], instance.Scale[1], instance.Scale[2]) : Vector3.One;
            var instanceMat = Matrix4x4.CreateScale(scl) * Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(pos);

            // Structured path: use raw MDR binary to emit per-section primitives with correct textures.
            // Triggers always use the trigger material via the OBJ fallback below.
            if (!isTrigger)
            {
                var sectionMeshes = GetPrefabSectionMeshes(prefabSectionMeshCache, mdr, mdrBinDir, bin0Json, texCache, defaultMat);
                if (sectionMeshes != null && sectionMeshes.Count > 0)
                {
                    foreach (var (mesh, localMat) in sectionMeshes)
                        scene.AddRigidMesh(mesh, localMat * instanceMat);
                    structuredCount++;
                    continue;
                }
            }

            // OBJ fallback (triggers and any prefabs missing a .bin file)
            var prefabMat = isTrigger
                ? triggerMat
                : ResolvePrefabMaterial(prefabMatCache, mdr, bin0Json, texCache, defaultMat);

            foreach (var modelObj in mdr.ModelObjects)
            {
                if (string.IsNullOrEmpty(modelObj.ModelPath)) continue;
                var objPath = Path.Combine(modelsDir, modelObj.ModelPath);
                if (!File.Exists(objPath)) continue;

                try
                {
                    var m = LoadObjToMesh(objPath, prefabMat, isTrigger ? $"__trigger__{prefabName}" : null);
                    if (m == null) continue;
                    scene.AddRigidMesh(m, instanceMat);
                    fallbackCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("  Failed on {Path}: {Msg}", objPath, ex.Message);
                }
            }
        }
        _logger.LogInformation("  Instances rendered: {Structured} structured, {Fallback} OBJ-fallback",
            structuredCount, fallbackCount);

        var glbPath = Path.Combine(outputDir, $"{levelName}.glb");
        Directory.CreateDirectory(outputDir);
        var model = scene.ToGltf2();
        model.SaveGLB(glbPath);
        _logger.LogInformation("Saved GLB: {Path}", glbPath);
        return glbPath;
    }

    private MaterialBuilder GetPatchMaterial(
        Dictionary<(int, int), MaterialBuilder> cache,
        Dictionary<int, string> texCache,
        Dictionary<int, string> lmCache,
        int texRid, int lmRid, string name)
    {
        var key = (texRid, lmRid);
        if (cache.TryGetValue(key, out var cached)) return cached;

        var mat = new MaterialBuilder($"{name}_t{texRid}_lm{lmRid}")
            .WithChannelParam(KnownChannel.BaseColor, KnownProperty.RGBA, new Vector4(1, 1, 1, 1))
            .WithMetallicRoughness(0f, 1f);

        if (texCache.TryGetValue(texRid, out var texPath))
        {
            try { mat.WithChannelImage(KnownChannel.BaseColor, texPath); ApplyAlphaModeFromTexture(mat, texPath); }
            catch { _logger.LogWarning("  Failed to apply texture {Path}", texPath); }
        }

        if (lmCache.TryGetValue(lmRid, out var lmPath))
        {
            try
            {
                mat.WithChannelImage(KnownChannel.Occlusion, lmPath);
                // lightmap atlas UVs are in the second UV set
                var chan = mat.GetChannel(KnownChannel.Occlusion);
                if (chan != null) chan.UseTexture().CoordinateSet = 1;
            }
            catch { _logger.LogWarning("  Failed to apply lightmap {Path}", lmPath); }
        }

        cache[key] = mat;
        return mat;
    }

    private MaterialBuilder GetMaterial(Dictionary<int, MaterialBuilder> cache,
        Dictionary<int, string> texCache, int texRid, string name)
    {
        if (cache.TryGetValue(texRid, out var m)) return m;
        var mat = new MaterialBuilder($"{name}_t{texRid}")
            .WithChannelParam(KnownChannel.BaseColor, KnownProperty.RGBA, new Vector4(1, 1, 1, 1));
        if (texCache.TryGetValue(texRid, out var texPath))
        {
            try { mat.WithChannelImage(KnownChannel.BaseColor, texPath); ApplyAlphaModeFromTexture(mat, texPath); }
            catch { _logger.LogWarning("  Failed to apply texture {Path}", texPath); }
        }
        cache[texRid] = mat;
        return mat;
    }

    private List<(MeshBuilder<VertexPositionNormal, VertexTexture1> mesh, Matrix4x4 localMat)>? GetPrefabSectionMeshes(
        Dictionary<(int rid, int track), List<(MeshBuilder<VertexPositionNormal, VertexTexture1>, Matrix4x4)>?> cache,
        MDRJsonHandler.MainModelHeader prefab,
        string mdrBinDir,
        Bin0JsonHandler bin0Json,
        Dictionary<int, string> texCache,
        MaterialBuilder fallback)
    {
        var key = (prefab.RID, prefab.TrackID);
        if (cache.TryGetValue(key, out var cached)) return cached;

        var binPath = Path.Combine(mdrBinDir, $"{prefab.TrackID}-{prefab.RID}.bin");
        if (!File.Exists(binPath)) { cache[key] = null; return null; }

        WorldMDR worldMdr;
        try
        {
            worldMdr = new WorldMDR();
            using var stream = File.OpenRead(binPath);
            worldMdr.LoadData(stream);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("  Failed to parse {Path}: {Msg}", binPath, ex.Message);
            cache[key] = null;
            return null;
        }

        // Per-section material lookup: U12 index → Bin0[binIdx] → texture RID
        var u12 = prefab.U12 ?? new List<int>();
        var sectionMats = new MaterialBuilder[Math.Max(1, u12.Count)];
        for (int i = 0; i < u12.Count; i++)
            sectionMats[i] = BuildBin0Material(u12[i] >> 8, bin0Json, texCache, prefab.Name ?? "prefab", fallback);
        if (u12.Count == 0) sectionMats[0] = fallback;

        var result = new List<(MeshBuilder<VertexPositionNormal, VertexTexture1>, Matrix4x4)>();
        for (int moIdx = 0; moIdx < worldMdr.ModelObjects.Count; moIdx++)
        {
            var mo = worldMdr.ModelObjects[moIdx];
            if (mo.U1Offset == 0 || mo.unknownS2.ModelHeaderOffset == null) continue;

            var localMat = mo.MatrixOffset > 0 ? mo.matrix4X4 : Matrix4x4.Identity;

            for (int hdrIdx = 0; hdrIdx < mo.unknownS2.ModelHeaderOffset.Count; hdrIdx++)
            {
                var hdr = mo.unknownS2.ModelHeaderOffset[hdrIdx];
                int matIdx = hdr.U0;
                var mat = (matIdx >= 0 && matIdx < sectionMats.Length) ? sectionMats[matIdx] : fallback;

                var mesh = new MeshBuilder<VertexPositionNormal, VertexTexture1>(
                    $"{prefab.Name ?? "prefab"}_o{moIdx}_s{hdrIdx}");
                var prim = mesh.UsePrimitive(mat);
                int triCount = 0;

                // ModelOffsetHeaders alternate (vertex+UV, normal) pairs
                for (int b = 0; b + 1 < hdr.ModelOffsetHeaders.Count; b += 2)
                {
                    var vuv = hdr.ModelOffsetHeaders[b].modelVandUVData;
                    var norm = hdr.ModelOffsetHeaders[b + 1].modelNormalData;
                    if (vuv.Vertices == null || norm.Normals == null) continue;

                    var faces = worldMdr.GenerateFaces(vuv, norm);
                    foreach (var f in faces)
                    {
                        // SSX-Library writes (1 - V) when serializing OBJ; replicate here.
                        var uv1 = new Vector2(f.UV1.X, 1f - f.UV1.Y);
                        var uv2 = new Vector2(f.UV2.X, 1f - f.UV2.Y);
                        var uv3 = new Vector2(f.UV3.X, 1f - f.UV3.Y);
                        prim.AddTriangle(
                            (new VertexPositionNormal(f.V1, f.Normal1), new VertexTexture1(uv1)),
                            (new VertexPositionNormal(f.V2, f.Normal2), new VertexTexture1(uv2)),
                            (new VertexPositionNormal(f.V3, f.Normal3), new VertexTexture1(uv3)));
                        triCount++;
                    }
                }

                if (triCount > 0) result.Add((mesh, localMat));
            }
        }

        cache[key] = result;
        return result;
    }

    private MaterialBuilder BuildBin0Material(int binIdx, Bin0JsonHandler bin0Json,
        Dictionary<int, string> texCache, string prefabName, MaterialBuilder fallback)
    {
        if (binIdx < 0 || binIdx >= bin0Json.bin0Files.Count) return fallback;
        int texRid = bin0Json.bin0Files[binIdx].U0;
        if (!texCache.TryGetValue(texRid, out var texPath)) return fallback;

        var mat = new MaterialBuilder($"{prefabName}_t{texRid}")
            .WithChannelParam(KnownChannel.BaseColor, KnownProperty.RGBA, new Vector4(1, 1, 1, 1))
            .WithMetallicRoughness(0f, 1f);
        try
        {
            mat.WithChannelImage(KnownChannel.BaseColor, texPath);
            ApplyAlphaModeFromTexture(mat, texPath);
            return mat;
        }
        catch { _logger.LogWarning("  Failed to apply prefab texture {Path}", texPath); return fallback; }
    }

    private MaterialBuilder ResolvePrefabMaterial(
        Dictionary<(int, int), MaterialBuilder> cache,
        MDRJsonHandler.MainModelHeader prefab,
        Bin0JsonHandler bin0Json,
        Dictionary<int, string> texCache,
        MaterialBuilder fallback)
    {
        var key = (prefab.RID, prefab.TrackID);
        if (cache.TryGetValue(key, out var cached)) return cached;

        MaterialBuilder mat = fallback;
        // U12 entries encode (binIdx << 8) | trackId. Use first entry only (Option A).
        if (prefab.U12 != null && prefab.U12.Count > 0)
        {
            int binIdx = prefab.U12[0] >> 8;
            if (binIdx >= 0 && binIdx < bin0Json.bin0Files.Count)
            {
                int texRid = bin0Json.bin0Files[binIdx].U0;
                if (texCache.TryGetValue(texRid, out var texPath))
                {
                    var name = $"{prefab.Name ?? "prefab"}_t{texRid}";
                    var built = new MaterialBuilder(name)
                        .WithChannelParam(KnownChannel.BaseColor, KnownProperty.RGBA, new Vector4(1, 1, 1, 1))
                        .WithMetallicRoughness(0f, 1f);
                    try { built.WithChannelImage(KnownChannel.BaseColor, texPath); mat = built; }
                    catch { _logger.LogWarning("  Failed to apply prefab texture {Path}", texPath); }
                }
            }
        }
        cache[key] = mat;
        return mat;
    }

    // Classify each PNG once: OPAQUE (alpha always 255), MASK (alpha is binary 0/255),
    // BLEND (any partial alpha). SSX3 textures are all RGBA — but most are actually opaque,
    // so the classifier saves us from making everything transparent.
    private static readonly Dictionary<string, AlphaMode> _alphaCache = new();
    private static readonly object _alphaCacheLock = new();

    private static AlphaMode ClassifyTextureAlpha(string pngPath)
    {
        lock (_alphaCacheLock)
        {
            if (_alphaCache.TryGetValue(pngPath, out var cached)) return cached;
        }

        var mode = AlphaMode.OPAQUE;
        try
        {
            using var img = Image.Load<Rgba32>(pngPath);
            bool sawZero = false;
            bool sawPartial = false;
            img.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < accessor.Height && !sawPartial; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < row.Length; x++)
                    {
                        byte a = row[x].A;
                        if (a == 0) sawZero = true;
                        else if (a < 255) { sawPartial = true; break; }
                    }
                }
            });
            if (sawPartial) mode = AlphaMode.BLEND;
            else if (sawZero) mode = AlphaMode.MASK;
        }
        catch { /* unreadable PNG → leave as OPAQUE */ }

        lock (_alphaCacheLock) { _alphaCache[pngPath] = mode; }
        return mode;
    }

    private static void ApplyAlphaModeFromTexture(MaterialBuilder mat, string pngPath)
    {
        var mode = ClassifyTextureAlpha(pngPath);
        if (mode == AlphaMode.MASK) mat.WithAlpha(AlphaMode.MASK, 0.5f);
        else if (mode == AlphaMode.BLEND) mat.WithAlpha(AlphaMode.BLEND);
        // OPAQUE: leave default
    }

    private static bool IsTriggerPrefab(string name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        var n = name.ToLowerInvariant();
        return n.Contains("trigger")
            || n.Contains("reset_plane")
            || n.Contains("_collision")
            || n.Contains("collision_")
            || n.Contains("bcvolume")
            || n.Contains("failvolume");
    }

    private MeshBuilder<VertexPositionNormal, VertexTexture1>? LoadObjToMesh(
        string path, MaterialBuilder mat, string? nameOverride = null)
    {
        var name = nameOverride ?? Path.GetFileNameWithoutExtension(path);
        var mesh = new MeshBuilder<VertexPositionNormal, VertexTexture1>(name);
        var prim = mesh.UsePrimitive(mat);
        var verts = new List<Vector3>();
        var uvs = new List<Vector2>();
        var norms = new List<Vector3>();

        foreach (var line in File.ReadLines(path))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            if (parts[0] == "v" && parts.Length >= 4)
                verts.Add(new Vector3(
                    float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture)));
            else if (parts[0] == "vt" && parts.Length >= 3)
                uvs.Add(new Vector2(
                    float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture)));
            else if (parts[0] == "vn" && parts.Length >= 4)
                norms.Add(new Vector3(
                    float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture),
                    float.Parse(parts[3], System.Globalization.CultureInfo.InvariantCulture)));
            else if (parts[0] == "f" && parts.Length >= 4)
            {
                for (int i = 1; i <= parts.Length - 3; i++)
                {
                    if (!ParseVert(parts[1], out var vi1, out var ti1, out var ni1)) continue;
                    if (!ParseVert(parts[i + 1], out var vi2, out var ti2, out var ni2)) continue;
                    if (!ParseVert(parts[i + 2], out var vi3, out var ti3, out var ni3)) continue;

                    var v1 = verts[vi1]; var v2 = verts[vi2]; var v3 = verts[vi3];
                    var uv1 = ti1 < uvs.Count ? uvs[ti1] : Vector2.Zero;
                    var uv2 = ti2 < uvs.Count ? uvs[ti2] : Vector2.Zero;
                    var uv3 = ti3 < uvs.Count ? uvs[ti3] : Vector2.Zero;
                    var n1 = ni1 < norms.Count ? norms[ni1] : Vector3.Zero;
                    var n2 = ni2 < norms.Count ? norms[ni2] : Vector3.Zero;
                    var n3 = ni3 < norms.Count ? norms[ni3] : Vector3.Zero;

                    prim.AddTriangle(
                        (new VertexPositionNormal(v1, n1), new VertexTexture1(uv1)),
                        (new VertexPositionNormal(v2, n2), new VertexTexture1(uv2)),
                        (new VertexPositionNormal(v3, n3), new VertexTexture1(uv3)));
                }
            }
        }
        return mesh;
    }

    private static bool ParseVert(string s, out int vi, out int ti, out int ni)
    {
        vi = 0; ti = 0; ni = 0;
        var parts = s.Split('/');
        if (!int.TryParse(parts[0], out vi)) return false;
        vi--;
        if (parts.Length >= 2 && !string.IsNullOrEmpty(parts[1])) int.TryParse(parts[1], out ti);
        ti--;
        if (parts.Length >= 3 && !string.IsNullOrEmpty(parts[2])) int.TryParse(parts[2], out ni);
        ni--;
        return true;
    }
}