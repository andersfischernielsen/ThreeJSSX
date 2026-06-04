namespace ThreeJSSX.Services;

public static class PatchTessellator
{
    public static TessellatedPatch Tessellate(float[,] points, float[,] uvCorners, int subdiv = 10)
    {
        var vertices = new List<(float x, float y, float z, float u, float v, float nx, float ny, float nz)>();
        var indices = new List<int>();

        float u0 = uvCorners[0, 0], u1 = uvCorners[1, 0];
        float u2 = uvCorners[2, 0], u3 = uvCorners[3, 0];
        float v0 = uvCorners[0, 1], v1 = uvCorners[1, 1];
        float v2 = uvCorners[2, 1], v3 = uvCorners[3, 1];

        int grid = subdiv + 1;
        float[,,] interpPoints = new float[grid, grid, 3];

        for (int r = 0; r < grid; r++)
        {
            float rFrac = r / (float)subdiv;
            int rLow = (int)(rFrac * 3);
            if (rLow > 2) rLow = 2;
            float rLerp = rFrac * 3 - rLow;

            for (int c = 0; c < grid; c++)
            {
                float cFrac = c / (float)subdiv;
                int cLow = (int)(cFrac * 3);
                if (cLow > 2) cLow = 2;
                float cLerp = cFrac * 3 - cLow;

                int bl = rLow * 4 + cLow;
                int br = rLow * 4 + (cLow + 1);
                int tl = (rLow + 1) * 4 + cLow;
                int tr = (rLow + 1) * 4 + (cLow + 1);

                float x = Bilinear(points[bl, 0], points[br, 0], points[tl, 0], points[tr, 0], rLerp, cLerp);
                float y = Bilinear(points[bl, 1], points[br, 1], points[tl, 1], points[tr, 1], rLerp, cLerp);
                float z = Bilinear(points[bl, 2], points[br, 2], points[tl, 2], points[tr, 2], rLerp, cLerp);

                interpPoints[r, c, 0] = x;
                interpPoints[r, c, 1] = y;
                interpPoints[r, c, 2] = z;
            }
        }

        // compute UVs and per-vertex normals from surface gradients
        for (int r = 0; r < grid; r++)
        {
            float rFrac = r / (float)subdiv;
            for (int c = 0; c < grid; c++)
            {
                float cFrac = c / (float)subdiv;

                float u = Bilinear(u0, u1, u2, u3, rFrac, cFrac);
                float v = Bilinear(v0, v1, v2, v3, rFrac, cFrac);

                // compute normal from central differences
                float x = interpPoints[r, c, 0];
                float y = interpPoints[r, c, 1];
                float z = interpPoints[r, c, 2];

                // gradient in c direction (horizontal across patch)
                int cp = Math.Min(c + 1, grid - 1);
                int cm = Math.Max(c - 1, 0);
                float dxc = interpPoints[r, cp, 0] - interpPoints[r, cm, 0];
                float dyc = interpPoints[r, cp, 1] - interpPoints[r, cm, 1];
                float dzc = interpPoints[r, cp, 2] - interpPoints[r, cm, 2];

                // gradient in r direction (vertical across patch)
                int rp = Math.Min(r + 1, grid - 1);
                int rm = Math.Max(r - 1, 0);
                float dxr = interpPoints[rp, c, 0] - interpPoints[rm, c, 0];
                float dyr = interpPoints[rp, c, 1] - interpPoints[rm, c, 1];
                float dzr = interpPoints[rp, c, 2] - interpPoints[rm, c, 2];

                float nx = dyc * dzr - dzc * dyr;
                float ny = dzc * dxr - dxc * dzr;
                float nz = dxc * dyr - dyc * dxr;

                float len = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
                if (len > 0.0001f) { nx /= len; ny /= len; nz /= len; }

                vertices.Add((x, y, z, u, v, nx, ny, nz));
            }
        }

        for (int r = 0; r < subdiv; r++)
        {
            for (int c = 0; c < subdiv; c++)
            {
                int i0 = r * grid + c;
                int i1 = r * grid + (c + 1);
                int i2 = (r + 1) * grid + c;
                int i3 = (r + 1) * grid + (c + 1);

                indices.Add(i0); indices.Add(i1); indices.Add(i2);
                indices.Add(i1); indices.Add(i3); indices.Add(i2);
            }
        }

        return new TessellatedPatch { Vertices = vertices, Indices = indices };
    }

    private static float Bilinear(float v00, float v01, float v10, float v11, float r, float c)
        => v00 * (1 - r) * (1 - c) + v01 * (1 - r) * c + v10 * r * (1 - c) + v11 * r * c;
}

public class TessellatedPatch
{
    public List<(float x, float y, float z, float u, float v, float nx, float ny, float nz)> Vertices = new();
    public List<int> Indices = new();
}