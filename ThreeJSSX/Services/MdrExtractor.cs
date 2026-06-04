using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SSX_Library.Internal;
using SSX_Library.Internal.Utilities;
using SSX_Library.Internal.Utilities.StreamExtensions;
using SSXLibrary.FileHandlers.LevelFiles.SSX3PS2;
using SSXLibrary.FileHandlers.LevelFiles.SSX3PS2.SSBData;

namespace ThreeJSSX.Services;

/// Walks SSB files using the same chunk loop as SSBHandler.LoadAndExtractSSBFromSBD,
/// but saves per-level data that SSX-Library either discards or globally-collides on:
///   - MDR (ID=2):       raw RefPack-decompressed chunk bytes per prefab (re-parsed in MapGlbExporter)
///   - Textures (ID=9):  PER-LEVEL — SSX-Library writes to one global Textures/{rid}.png and
///                       drops collisions via if-not-exists, causing wrong textures across levels
///                       that share an RID (40+ levels share RIDs 17/23/38/...).
///   - Lightmaps (ID=10): PER-LEVEL — same global-collision issue.
public class MdrExtractor
{
    private readonly ILogger<MdrExtractor> _logger;

    public MdrExtractor(ILogger<MdrExtractor> logger) { _logger = logger; }

    // Tracks which texture/lightmap RIDs we've already written to the global pool during this run.
    // Set is per-ExtractAll so re-runs always overwrite SSX-Library's broken (R↔B swapped) globals.
    private readonly HashSet<int> _texturesWrittenGlobal = new();
    private readonly HashSet<int> _lightmapsWrittenGlobal = new();

    public void ExtractAll(string ssbDir, string levelsRoot)
    {
        _texturesWrittenGlobal.Clear();
        _lightmapsWrittenGlobal.Clear();
        foreach (var ssbPath in Directory.GetFiles(ssbDir, "*.ssb"))
        {
            try { ExtractSsb(ssbPath, levelsRoot); }
            catch (Exception ex)
            {
                _logger.LogWarning("MDR extract failed for {Path}: {Msg}", ssbPath, ex.Message);
            }
        }
    }

    private void ExtractSsb(string ssbPath, string levelsRoot)
    {
        var sdb = new SDBHandler(); sdb.LoadSBD(ssbPath.Replace(".ssb", ".sdb"));
        // PHM/PSM aren't needed for MDR-only extraction (they're for name lookups)

        using var stream = File.OpenRead(ssbPath);
        var accumulator = new MemoryStream();
        int locationIndex = 0;

        while (stream.Position < stream.Length - 1)
        {
            string magic = stream.ReadAsciiWithLength(4, false);
            int size = StreamUtil.ReadUInt32(stream);
            byte[] data = StreamUtil.ReadBytes(stream, size - 8);
            byte[] decomp = Refpack.Decompress(data);
            accumulator.Write(decomp, 0, decomp.Length);

            if (!string.Equals(magic, "CEND", StringComparison.OrdinalIgnoreCase))
                continue;

            int chunkId = sdb.FindLocationChunk(locationIndex);
            string locationName = sdb.locations[chunkId].Name;
            string levelDir = Path.Combine(levelsRoot, locationName);
            string mdrDir = Path.Combine(levelDir, "MDR");
            string texDir = Path.Combine(levelDir, "Textures");
            string lmDir = Path.Combine(levelDir, "Lightmaps");
            // Global pool dirs are siblings of "Levels/" — overwrite SSX-Library's R↔B-swapped output.
            string globalTexDir = Path.GetFullPath(Path.Combine(levelsRoot, "..", "Textures"));
            string globalLmDir = Path.GetFullPath(Path.Combine(levelsRoot, "..", "Lightmaps"));
            Directory.CreateDirectory(mdrDir);

            accumulator.Position = 0;
            while (accumulator.Position < accumulator.Length)
            {
                int id = StreamUtil.ReadUInt8(accumulator);
                int chunkSize = StreamUtil.ReadInt24(accumulator);
                int trackId = StreamUtil.ReadUInt8(accumulator);
                int rid = StreamUtil.ReadInt24(accumulator);
                byte[] chunkData = StreamUtil.ReadBytes(accumulator, chunkSize);

                if (id == 2)
                {
                    var outPath = Path.Combine(mdrDir, $"{trackId}-{rid}.bin");
                    File.WriteAllBytes(outPath, chunkData);
                }
                else if (id == 9)
                {
                    var perLevelPath = Path.Combine(texDir, $"{rid}.png");
                    var globalPath = Path.Combine(globalTexDir, $"{rid}.png");
                    bool wantPerLevel = !File.Exists(perLevelPath);
                    bool wantGlobal = _texturesWrittenGlobal.Add(rid);
                    if (wantPerLevel || wantGlobal)
                    {
                        var bmp = DecodeSshFixed(chunkData, $"texture {rid}");
                        if (bmp != null)
                        {
                            if (wantPerLevel) { Directory.CreateDirectory(texDir); bmp.SaveAsPng(perLevelPath); }
                            if (wantGlobal) { Directory.CreateDirectory(globalTexDir); bmp.SaveAsPng(globalPath); }
                            bmp.Dispose();
                        }
                    }
                }
                else if (id == 10)
                {
                    var perLevelPath = Path.Combine(lmDir, $"{rid:0000}.png");
                    var globalPath = Path.Combine(globalLmDir, $"{rid:0000}.png");
                    bool wantPerLevel = !File.Exists(perLevelPath);
                    bool wantGlobal = _lightmapsWrittenGlobal.Add(rid);
                    if (wantPerLevel || wantGlobal)
                    {
                        var bmp = DecodeSshFixed(chunkData, $"lightmap {rid}");
                        if (bmp != null)
                        {
                            if (wantPerLevel) { Directory.CreateDirectory(lmDir); bmp.SaveAsPng(perLevelPath); }
                            if (wantGlobal) { Directory.CreateDirectory(globalLmDir); bmp.SaveAsPng(globalPath); }
                            bmp.Dispose();
                        }
                    }
                }
            }

            accumulator = new MemoryStream();
            locationIndex++;
        }
    }

    private Image<Rgba32>? DecodeSshFixed(byte[] chunkData, string label)
    {
        try
        {
            var ssh = new WorldSSH();
            using var ms = new MemoryStream(chunkData);
            ssh.Load(ms);
            return ssh.bitmap;
        }
        catch (Exception ex)
        {
            _logger.LogWarning("  Failed to decode {Label}: {Msg}", label, ex.Message);
            return null;
        }
    }
}
