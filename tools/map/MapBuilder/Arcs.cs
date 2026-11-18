using NetTopologySuite.Algorithm;
using NetTopologySuite.Geometries;

namespace Singularity.MapBuilder;

internal sealed class Arc
{
    public int Left = -1, Right = -1;        // province index left and right of the travel direction; -1 = outside the polygons
    public List<(int Lat, int Lon)> V = new(); // micro-degrees
    public double LengthKm;
}

/// <summary>Shared arcs (ADR-0011 §5): every boundary vertex chain is stored once with its left and right province.</summary>
internal sealed class Topology
{
    public List<Arc> Arcs = new();
    /// <summary>Per province: rings (largest area first); a ring is a list of (arc index + 1) with the sign giving the travel direction.</summary>
    public List<List<List<int>>> Rings = new();

    private struct Seg { public long A, B; public int L, R; public int Arc; public bool Canon; }

    private static long Key(int lat, int lon) => ((long)lat << 32) + (long)((uint)lon + 0x80000000u);   // lexicographic (lat, lon) order
    private static (int Lat, int Lon) Unkey(long k) => ((int)(k >> 32), unchecked((int)((uint)(k & 0xFFFFFFFFL) - 0x80000000u)));

    public static Topology Build(IReadOnlyList<Geometry> provinces)
    {
        var segs = new List<Seg>();
        var segIndex = new Dictionary<(long, long), int>();
        var ringsOfProvince = new List<List<long[]>>();   // oriented vertex keys, closed ring without repeated last vertex
        for (int p = 0; p < provinces.Count; p++)
        {
            var list = new List<long[]>();
            foreach (var poly in Sph.Polygons(provinces[p]))
            {
                list.Add(Oriented(poly.ExteriorRing.Coordinates, true));
                for (int h = 0; h < poly.NumInteriorRings; h++) list.Add(Oriented(poly.GetInteriorRingN(h).Coordinates, false));
            }
            ringsOfProvince.Add(list);
            foreach (var ring in list)
                for (int i = 0; i < ring.Length; i++)
                {
                    long u = ring[i], v = ring[(i + 1) % ring.Length];
                    if (u == v) continue;
                    var key = u < v ? (u, v) : (v, u);
                    if (!segIndex.TryGetValue(key, out int si))
                    {
                        si = segs.Count;
                        segIndex[key] = si;
                        segs.Add(new Seg { A = key.Item1, B = key.Item2, L = -1, R = -1 });
                    }
                    var s = segs[si];
                    if (u < v) s.L = p; else s.R = p;   // province p is on the left of u->v
                    segs[si] = s;
                }
        }

        var incident = new Dictionary<long, List<int>>();
        for (int i = 0; i < segs.Count; i++)
        {
            foreach (var v in new[] { segs[i].A, segs[i].B })
            {
                if (!incident.TryGetValue(v, out var l)) incident[v] = l = new List<int>();
                l.Add(i);
            }
        }

        (int Left, int Right) Sides(int s, bool fwd) => fwd ? (segs[s].L, segs[s].R) : (segs[s].R, segs[s].L);
        long From(int s, bool fwd) => fwd ? segs[s].A : segs[s].B;
        long To(int s, bool fwd) => fwd ? segs[s].B : segs[s].A;

        (int, bool)? Next(int s, bool fwd)
        {
            long at = To(s, fwd);
            var inc = incident[at];
            if (inc.Count != 2) return null;
            int o = inc[0] == s ? inc[1] : inc[0];
            bool ofwd = segs[o].A == at;
            return Sides(o, ofwd) == Sides(s, fwd) ? (o, ofwd) : null;
        }
        (int, bool)? Prev(int s, bool fwd)
        {
            long at = From(s, fwd);
            var inc = incident[at];
            if (inc.Count != 2) return null;
            int o = inc[0] == s ? inc[1] : inc[0];
            bool ofwd = segs[o].B == at;
            return Sides(o, ofwd) == Sides(s, fwd) ? (o, ofwd) : null;
        }

        var arcs = new List<Arc>();
        var arcSegs = new List<List<(int S, bool Fwd)>>();
        var visited = new bool[segs.Count];
        for (int s0 = 0; s0 < segs.Count; s0++)
        {
            if (visited[s0]) continue;
            (int S, bool F) start = (s0, true);
            while (true)
            {
                var pr = Prev(start.S, start.F);
                if (pr == null || pr.Value.Item1 == s0) break;
                start = pr.Value;
            }
            var chain = new List<(int S, bool Fwd)>();
            (int S, bool F) cur = start;
            while (true)
            {
                visited[cur.S] = true;
                chain.Add((cur.S, cur.F));
                var n = Next(cur.S, cur.F);
                if (n == null || visited[n.Value.Item1]) break;
                cur = n.Value;
            }
            var (l, r) = Sides(chain[0].S, chain[0].Fwd);
            var verts = new List<long> { From(chain[0].S, chain[0].Fwd) };
            foreach (var c in chain) verts.Add(To(c.S, c.Fwd));
            var segList = chain.ToList();
            bool loop = verts[0] == verts[^1];
            if (loop)
            {
                // Start at the smallest vertex; travel towards the smaller neighbour.
                verts.RemoveAt(verts.Count - 1);
                int m = verts.IndexOf(verts.Min());
                verts = verts.Skip(m).Concat(verts.Take(m)).ToList();
                segList = segList.Skip(m).Concat(segList.Take(m)).ToList();
                verts.Add(verts[0]);
                if (verts[^2] < verts[1])
                {
                    verts.Reverse();
                    segList.Reverse();
                    segList = segList.Select(x => (x.S, !x.Fwd)).ToList();
                    (l, r) = (r, l);
                }
            }
            else if (verts[^1] < verts[0])
            {
                verts.Reverse();
                segList.Reverse();
                segList = segList.Select(x => (x.S, !x.Fwd)).ToList();
                (l, r) = (r, l);
            }
            var arc = new Arc { Left = l, Right = r };
            double km = 0;
            for (int i = 0; i < verts.Count; i++)
            {
                var (lat, lon) = Unkey(verts[i]);
                arc.V.Add((lat, lon));
                if (i > 0) km += Sph.DistKm(arc.V[i - 1].Lon / 1e6, arc.V[i - 1].Lat / 1e6, lon / 1e6, lat / 1e6);
            }
            arc.LengthKm = km;
            arcs.Add(arc);
            arcSegs.Add(segList);
        }

        var order = Enumerable.Range(0, arcs.Count).OrderBy(i => Math.Min(arcs[i].Left, arcs[i].Right))
            .ThenBy(i => Math.Max(arcs[i].Left, arcs[i].Right)).ThenBy(i => arcs[i].V[0].Lat).ThenBy(i => arcs[i].V[0].Lon)
            .ThenBy(i => arcs[i].V[^1].Lat).ThenBy(i => arcs[i].V[^1].Lon).ThenBy(i => arcs[i].V.Count).ToList();
        var rank = new int[arcs.Count];
        for (int i = 0; i < order.Count; i++) rank[order[i]] = i;
        var res = new Topology();
        foreach (var i in order) res.Arcs.Add(arcs[i]);
        for (int a = 0; a < arcs.Count; a++)
            foreach (var (s, fwd) in arcSegs[a])
            {
                var sg = segs[s]; sg.Arc = rank[a]; sg.Canon = fwd; segs[s] = sg;   // Canon: arc travels A->B here
            }

        for (int p = 0; p < provinces.Count; p++)
        {
            var rings = new List<(double Area, long First, List<int> Refs)>();
            foreach (var ring in ringsOfProvince[p])
            {
                var seq = new List<int>();
                for (int i = 0; i < ring.Length; i++)
                {
                    long u = ring[i], v = ring[(i + 1) % ring.Length];
                    if (u == v) continue;
                    int si = segIndex[u < v ? (u, v) : (v, u)];
                    bool ringCanon = u < v;
                    seq.Add((segs[si].Arc + 1) * (segs[si].Canon == ringCanon ? 1 : -1));
                }
                int cut = 0;
                for (int i = 0; i < seq.Count; i++) if (seq[i] != seq[(i + seq.Count - 1) % seq.Count]) { cut = i; break; }
                seq = seq.Skip(cut).Concat(seq.Take(cut)).ToList();
                var refs = new List<int>();
                foreach (var x in seq) if (refs.Count == 0 || refs[^1] != x) refs.Add(x);
                var cs = ring.Select(k => Unkey(k)).Select(t => new Coordinate(t.Lon / 1e6, t.Lat / 1e6)).ToList();
                cs.Add(cs[0]);
                rings.Add((Sph.RingArea(cs.ToArray()), ring[0], refs));
            }
            res.Rings.Add(rings.OrderByDescending(x => x.Area).ThenBy(x => x.First).Select(x => x.Refs).ToList());
        }
        return res;
    }

    /// <summary>Shell counter-clockwise, hole clockwise; vertices as sortable keys, last duplicate dropped.</summary>
    private static long[] Oriented(Coordinate[] cs, bool shell)
    {
        bool ccw = Orientation.IsCCW(cs);
        var list = cs.Take(cs.Length - 1).Select(c => Key(Sph.Micro(c.Y), Sph.Micro(c.X))).ToList();
        if (ccw != shell) list.Reverse();
        return list.ToArray();
    }

    /// <summary>Rebuilds a province ring from its arc references (used by tests and the validator).</summary>
    public List<Coordinate> RingCoordinates(List<int> refs)
    {
        var cs = new List<Coordinate>();
        foreach (var r in refs)
        {
            var arc = Arcs[Math.Abs(r) - 1];
            var vs = r > 0 ? arc.V : Enumerable.Reverse(arc.V).ToList();
            foreach (var v in vs.Skip(cs.Count == 0 ? 0 : 1)) cs.Add(new Coordinate(v.Lon / 1e6, v.Lat / 1e6));
        }
        return cs;
    }
}
