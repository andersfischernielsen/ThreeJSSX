using SSXLibrary.JsonFiles.SSX3;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using SharpGLTF.Scenes;
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
        var texturesSrc = Path.Combine(levelDir, "..", "..", "Textures");

        var patchesJson = File.Exists(patchesPath)
            ? PatchesJsonHandler.Load(patchesPath) : new PatchesJsonHandler();
        var instancesJson = File.Exists(instancesPath)
            ? InstanceJsonHandler.Load(instancesPath) : new InstanceJsonHandler();
        var mdrJson = File.Exists(prefabsPath)
            ? MDRJsonHandler.Load(prefabsPath) : new MDRJsonHandler();

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

        _logger.LogInformation("  Patches: {Count}", patchesJson.Patches.Count);
        foreach (var patch in patchesJson.Patches)
        {
            var tessellated = PatchTessellator.Tessellate(patch.Points, patch.UVPoints, subdiv: 12);
            var mat = GetMaterial(materialCache, texCache, patch.TextureRID, patch.Name);
            var mesh = new MeshBuilder<VertexPositionNormal, VertexTexture1>(patch.Name);
            var prim = mesh.UsePrimitive(mat);

            for (int i = 0; i < tessellated.Indices.Count; i += 3)
            {
                var va = tessellated.Vertices[tessellated.Indices[i]];
                var vb = tessellated.Vertices[tessellated.Indices[i + 1]];
                var vc = tessellated.Vertices[tessellated.Indices[i + 2]];

                prim.AddTriangle(
                    (new VertexPositionNormal(new Vector3(va.x, va.y, va.z), new Vector3(va.nx, va.ny, va.nz)), new VertexTexture1(new Vector2(va.u, va.v))),
                    (new VertexPositionNormal(new Vector3(vb.x, vb.y, vb.z), new Vector3(vb.nx, vb.ny, vb.nz)), new VertexTexture1(new Vector2(vb.u, vb.v))),
                    (new VertexPositionNormal(new Vector3(vc.x, vc.y, vc.z), new Vector3(vc.nx, vc.ny, vc.nz)), new VertexTexture1(new Vector2(vc.u, vc.v))));
            }
            scene.AddRigidMesh(mesh, Matrix4x4.Identity);
        }

        _logger.LogInformation("  Instances: {Count}", instancesJson.Instances.Count);
        var modelsDir = Path.Combine(levelDir, "Models");
        foreach (var instance in instancesJson.Instances)
        {
            var mdr = mdrJson.mainModelHeaders.FirstOrDefault(m =>
                m.RID == instance.ModelRID && m.TrackID == instance.ModelTrackID);
            if (mdr.ModelObjects == null) continue;

            foreach (var modelObj in mdr.ModelObjects)
            {
                if (string.IsNullOrEmpty(modelObj.ModelPath)) continue;
                var objPath = Path.Combine(modelsDir, modelObj.ModelPath);
                if (!File.Exists(objPath)) continue;

                try
                {
                    var m = LoadObjToMesh(objPath, defaultMat);
                    if (m == null) continue;

                    var pos = instance.Position != null && instance.Position.Length >= 3
                        ? new Vector3(instance.Position[0], instance.Position[1], instance.Position[2]) : Vector3.Zero;
                    var rot = instance.Rotation != null && instance.Rotation.Length >= 4
                        ? new Quaternion(instance.Rotation[0], instance.Rotation[1], instance.Rotation[2], instance.Rotation[3]) : Quaternion.Identity;
                    var scl = instance.Scale != null && instance.Scale.Length >= 3
                        ? new Vector3(instance.Scale[0], instance.Scale[1], instance.Scale[2]) : Vector3.One;

                    scene.AddRigidMesh(m, Matrix4x4.CreateScale(scl) * Matrix4x4.CreateFromQuaternion(rot) * Matrix4x4.CreateTranslation(pos));
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("  Failed on {Path}: {Msg}", objPath, ex.Message);
                }
            }
        }

        var glbPath = Path.Combine(outputDir, $"{levelName}.glb");
        Directory.CreateDirectory(outputDir);
        var model = scene.ToGltf2();
        model.SaveGLB(glbPath);
        _logger.LogInformation("Saved GLB: {Path}", glbPath);
        return glbPath;
    }

    private MaterialBuilder GetMaterial(Dictionary<int, MaterialBuilder> cache,
        Dictionary<int, string> texCache, int texRid, string name)
    {
        if (cache.TryGetValue(texRid, out var m)) return m;
        var mat = new MaterialBuilder($"{name}_t{texRid}")
            .WithChannelParam(KnownChannel.BaseColor, KnownProperty.RGBA, new Vector4(1, 1, 1, 1));
        if (texCache.TryGetValue(texRid, out var texPath))
        {
            try { mat.WithChannelImage(KnownChannel.BaseColor, texPath); }
            catch { _logger.LogWarning("  Failed to apply texture {Path}", texPath); }
        }
        cache[texRid] = mat;
        return mat;
    }

    private MeshBuilder<VertexPositionNormal, VertexTexture1>? LoadObjToMesh(
        string path, MaterialBuilder mat)
    {
        var name = Path.GetFileNameWithoutExtension(path);
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