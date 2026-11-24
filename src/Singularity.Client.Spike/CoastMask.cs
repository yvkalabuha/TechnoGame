namespace Singularity.Client.Spike;

/// <summary>Godot-free: which land pixels may carry a coast stroke (G8). Land components (8-connected) smaller than a threshold are islet specks:
/// a 1.2 px stroke would cover them completely and read as a black dot, so they are drawn with their fill only.</summary>
public static class CoastMask
{
    public const int MinTexels = 10;

    /// <summary>true = land pixel of a component of at least <paramref name="minTexels"/> pixels. Longitude wraps; the poles do not.</summary>
    public static bool[] Build(ushort[] regionIds, int w, int h, int minTexels = MinTexels)
    {
        var keep = new bool[regionIds.Length];
        var seen = new bool[regionIds.Length];
        var queue = new int[regionIds.Length];
        for (int start = 0; start < regionIds.Length; start++)
        {
            if (regionIds[start] == 0 || seen[start]) continue;
            int head = 0, tail = 0;
            queue[tail++] = start; seen[start] = true;
            while (head < tail)
            {
                int p = queue[head++], x = p % w, y = p / w;
                for (int dy = -1; dy <= 1; dy++)
                {
                    int ny = y + dy;
                    if (ny < 0 || ny >= h) continue;
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int q = ny * w + (x + dx + w) % w;
                        if (regionIds[q] == 0 || seen[q]) continue;
                        seen[q] = true; queue[tail++] = q;
                    }
                }
            }
            if (tail >= minTexels) for (int i = 0; i < tail; i++) keep[queue[i]] = true;
        }
        return keep;
    }
}
