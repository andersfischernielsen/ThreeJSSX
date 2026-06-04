using ThreeJSSX.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IsoService>();
builder.Services.AddSingleton<MdrExtractor>();
builder.Services.AddSingleton<MapGlbExporter>();

var app = builder.Build();

app.UseStaticFiles();
var extractBase = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "maps");
var glbDir = Path.Combine(extractBase, "glb");
Directory.CreateDirectory(glbDir);

app.MapGet("/api/levels", async (IsoService iso, ILogger<Program> logger) =>
{
    if (!iso.IsoExists)
        return Results.Problem($"ISO not found at {iso.IsoPath}. Place your SSX3 PS2 ISO at ISO/SSX 3.iso relative to the project root.");

    await iso.EnsureExtracted();
    var levels = iso.GetExtractedLevels();
    return Results.Ok(levels);
});

app.MapGet("/api/levels/{name}/glb", async (string name, IsoService iso, MapGlbExporter exporter) =>
{
    if (!iso.IsoExists)
        return Results.Problem($"ISO not found at {iso.IsoPath}");

    var glbPath = Path.Combine(glbDir, $"{name}.glb");
    if (File.Exists(glbPath))
        return Results.File(File.OpenRead(glbPath), "model/gltf-binary");

    await iso.EnsureExtracted();

    var levelDir = Path.Combine(iso.ExtractDir, "Levels", name);
    if (!Directory.Exists(levelDir))
        return Results.NotFound($"Level '{name}' not found");

    exporter.BuildGlb(levelDir, glbDir);

    if (File.Exists(glbPath))
        return Results.File(File.OpenRead(glbPath), "model/gltf-binary");

    return Results.Problem("Failed to generate map");
});

app.MapGet("/api/levels/{name}/halos", async (string name, IsoService iso) =>
{
    if (!iso.IsoExists)
        return Results.Problem($"ISO not found at {iso.IsoPath}");

    await iso.EnsureExtracted();

    var levelDir = Path.Combine(iso.ExtractDir, "Levels", name);
    if (!Directory.Exists(levelDir))
        return Results.NotFound($"Level '{name}' not found");

    var halos = HaloParser.ParseLevel(levelDir);
    var result = halos.Select(h => new
    {
        pos = new[] { h.Center.X, h.Center.Y, h.Center.Z },
        radius = h.Radius,
        intensity = h.Intensity
    });
    return Results.Ok(result);
});

app.MapPost("/api/levels/extract", async (IsoService iso) =>
{
    await iso.ForceExtract();
    var levels = iso.GetExtractedLevels();
    return Results.Ok(levels);
});

app.MapDelete("/api/levels/{name}/glb", (string name) =>
{
    var glbPath = Path.Combine(glbDir, $"{name}.glb");
    if (File.Exists(glbPath)) File.Delete(glbPath);
    return Results.Ok();
});

app.MapDelete("/api/cache/glb", () =>
{
    foreach (var f in Directory.GetFiles(glbDir, "*.glb"))
        File.Delete(f);
    return Results.Ok(new { deleted = Directory.GetFiles(glbDir, "*.glb").Length });
});

app.MapFallbackToFile("index.html");

app.Run();