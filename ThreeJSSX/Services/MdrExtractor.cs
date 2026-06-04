using SSX_Library.Internal;
using SSX_Library.Internal.Utilities;
using SSXLibrary.FileHandlers.LevelFiles.SSX3PS2;

namespace ThreeJSSX.Services;

/// Walks SSB files using the same chunk loop as SSBHandler.LoadAndExtractSSBFromSBD,
/// but only saves the RefPack-decompressed MDR (ID=2) chunk bytes per prefab.
/// SSX-Library's OBJ exporter flattens tristrip sections; keeping the raw bytes
/// lets MapGlbExporter re-parse them with WorldMDR.LoadData and emit per-section
/// primitives with correct per-section textures.
public class MdrExtractor
{
    private readonly ILogger<MdrExtractor> _logger;

    public MdrExtractor(ILogger<MdrExtractor> logger) { _logger = logger; }

    public void ExtractAll(string ssbDir, string levelsRoot)
    {
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
            string magic = StreamUtil.ReadString(stream, 4);
            int size = StreamUtil.ReadUInt32(stream);
            byte[] data = StreamUtil.ReadBytes(stream, size - 8);
            byte[] decomp = Refpack.Decompress(data);
            accumulator.Write(decomp, 0, decomp.Length);

            if (!string.Equals(magic, "CEND", StringComparison.OrdinalIgnoreCase))
                continue;

            int chunkId = sdb.FindLocationChunk(locationIndex);
            string locationName = sdb.locations[chunkId].Name;
            string mdrDir = Path.Combine(levelsRoot, locationName, "MDR");
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
            }

            accumulator = new MemoryStream();
            locationIndex++;
        }
    }
}
