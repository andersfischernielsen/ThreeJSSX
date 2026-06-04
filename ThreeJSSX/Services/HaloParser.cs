using System.Numerics;

namespace ThreeJSSX.Services;

public class HaloParser
{
    public record HaloEntry(
        Vector3 Center,
        float Radius,
        float Intensity);

    public static List<HaloEntry> ParseLevel(string levelDir)
    {
        var results = new List<HaloEntry>();
        var files = Directory.GetFiles(levelDir, "*.bin7");
        foreach (var path in files)
        {
            var entry = ParseFile(path);
            if (entry != null) results.Add(entry);
        }
        return results;
    }

    private static HaloEntry? ParseFile(string path)
    {
        try
        {
            var data = File.ReadAllBytes(path);
            if (data.Length < 80) return null;

            using var ms = new MemoryStream(data);
            using var r = new BinaryReader(ms);

            r.ReadInt32(); // U0
            r.ReadInt32(); // U1

            r.ReadSingle(); // f2
            r.ReadSingle(); // f3
            r.ReadSingle(); // f4
            var intensity = r.ReadSingle(); // f5
            r.ReadSingle(); // f6

            var p1 = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            var p2 = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
            var p3 = new Vector3(r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

            var center = p1;
            var diag = MathF.Max(
                Vector3.Distance(p1, p2),
                MathF.Max(Vector3.Distance(p2, p3), Vector3.Distance(p1, p3)));

            var radius = diag > 0 ? diag * 0.5f : 100f;

            return new HaloEntry(center, radius, Math.Clamp(intensity, 0, 1));
        }
        catch
        {
            return null;
        }
    }
}
