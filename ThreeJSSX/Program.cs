using ThreeJSSX.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<IsoService>();
builder.Services.AddSingleton<MapGlbExporter>();

var app = builder.Build();

app.UseStaticFiles();
var extractBase = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "maps");
var glbDir = Path.Combine(extractBase, "glb");
Directory.CreateDirectory(glbDir);

app.MapGet("/api/levels", (IsoService iso, ILogger<Program> logger) =>
{
    if (!iso.IsoExists)
        return Results.Problem($"ISO not found at {iso.IsoPath}");

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

app.MapPost("/api/levels/extract", async (IsoService iso) =>
{
    await iso.EnsureExtracted();
    var levels = iso.GetExtractedLevels();
    return Results.Ok(levels);
});

app.MapFallbackToFile("index.html");

app.Run();