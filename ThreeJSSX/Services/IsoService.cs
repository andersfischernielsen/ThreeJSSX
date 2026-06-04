using DiscUtils;
using DiscUtils.Iso9660;
using SSX_Library;

namespace ThreeJSSX.Services;

public class IsoService
{
    private readonly string _isoPath;
    private readonly string _extractDir;
    private readonly ILogger<IsoService> _logger;
    private readonly MdrExtractor _mdrExtractor;

    public IsoService(IConfiguration config, ILogger<IsoService> logger, MdrExtractor mdrExtractor)
    {
        var root = config.GetValue<string>("ExtractDir")
            ?? Path.Combine(Path.GetTempPath(), "ThreeJSSX");
        _extractDir = Path.GetFullPath(root);
        _logger = logger;
        _mdrExtractor = mdrExtractor;

        var isoPath = config.GetValue<string>("IsoPath")
            ?? Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "..", "ISO", "SSX 3.iso"));
        _isoPath = isoPath;
    }

    public string ExtractDir => _extractDir;
    public string IsoPath => _isoPath;
    public bool IsoExists => File.Exists(_isoPath);

    public List<string> GetExtractedLevels()
    {
        var levelsDir = Path.Combine(_extractDir, "Levels");
        if (!Directory.Exists(levelsDir))
            return new();
        return Directory.GetDirectories(levelsDir)
            .Select(Path.GetFileName)
            .Where(n => n != null)
            .Cast<string>()
            .OrderBy(n => n)
            .ToList();
    }

    public async Task ForceExtract()
    {
        var levelsDir = Path.Combine(_extractDir, "Levels");
        if (Directory.Exists(levelsDir))
        {
            _logger.LogInformation("Deleting existing extraction at {Dir}", levelsDir);
            Directory.Delete(_extractDir, recursive: true);
        }
        await EnsureExtracted();
    }

    public async Task EnsureExtracted()
    {
        var bamDir = Path.Combine(_extractDir, "BAM");
        var levelsDir = Path.Combine(_extractDir, "Levels");
        var texturesDir = Path.Combine(_extractDir, "Textures");
        var lightmapsDir = Path.Combine(_extractDir, "Lightmaps");

        if (Directory.Exists(levelsDir))
        {
            _logger.LogInformation("Already extracted, found {Count} levels",
                Directory.GetDirectories(levelsDir).Length);
            EnsureMdrSectionsExtracted(levelsDir);
            return;
        }

        Directory.CreateDirectory(_extractDir);

        _logger.LogInformation("Mounting ISO: {Path}", _isoPath);
        using var cd = new CDReader(File.OpenRead(_isoPath), true);

        _logger.LogInformation("Extracting BAM.BIG from ISO...");
        var worldDir = FindDir(cd.Root, "WORLDS");
        if (worldDir == null)
            throw new FileNotFoundException("WORLDS directory not found on ISO");

        foreach (var f in worldDir.GetFiles())
        {
            var name = StripVersion(f.Name);
            var outPath = Path.Combine(bamDir, name);
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            using var src = cd.OpenFile(f.FullName, FileMode.Open);
            using var dst = File.Create(outPath);
            await src.CopyToAsync(dst);
            _logger.LogInformation("  Extracted {Name} ({Len} bytes)", name, f.Length);
        }

        var bigPath = Path.Combine(bamDir, "BAM.BIG");
        var bigExtract = Path.Combine(bamDir, "extracted");
        _logger.LogInformation("Extracting BAM.BIG archive...");
        BIG.Extract(bigPath, bigExtract);

        var ssbDir = Path.Combine(bigExtract, "data", "worlds");
        var ssbFiles = Directory.GetFiles(ssbDir, "*.ssb");
        _logger.LogInformation("Extracting {Count} SSB files to level directories", ssbFiles.Length);

        foreach (var ssbPath in ssbFiles)
        {
            var handler = new SSXLibrary.FileHandlers.LevelFiles.SSX3PS2.SSBHandler();
            handler.LoadAndExtractSSBFromSBD(ssbPath, _extractDir);
        }

        _logger.LogInformation("Extracting raw MDR chunks for per-section materials...");
        _mdrExtractor.ExtractAll(ssbDir, levelsDir);

        _logger.LogInformation("Extraction complete!");
    }

    private void EnsureMdrSectionsExtracted(string levelsDir)
    {
        // Idempotent backfill: if MDR/ subdirs are missing (e.g. older extraction),
        // re-run only the MDR extractor against the on-disk SSB files.
        var anyLevel = Directory.GetDirectories(levelsDir).FirstOrDefault();
        if (anyLevel != null && Directory.Exists(Path.Combine(anyLevel, "MDR")))
            return;

        var ssbDir = Path.Combine(_extractDir, "BAM", "extracted", "data", "worlds");
        if (!Directory.Exists(ssbDir))
        {
            _logger.LogWarning("Cannot backfill MDR sections — SSB directory missing at {Path}", ssbDir);
            return;
        }
        _logger.LogInformation("Backfilling raw MDR chunks for existing extraction...");
        _mdrExtractor.ExtractAll(ssbDir, levelsDir);
    }

    private static DiscUtils.DiscDirectoryInfo? FindDir(DiscUtils.DiscDirectoryInfo dir, string name)
    {
        foreach (var d in dir.GetDirectories())
            if (string.Equals(d.Name.Split(';')[0], name, StringComparison.OrdinalIgnoreCase))
                return d;
        foreach (var d in dir.GetDirectories())
        {
            var found = FindDir(d, name);
            if (found != null) return found;
        }
        return null;
    }

    private static string StripVersion(string name)
    {
        var semi = name.IndexOf(';');
        return semi >= 0 ? name[..semi] : name;
    }
}