// A level is a maze grown from a random seed. The game and the shader compute the same exact distance to its walls.
using System;
using System.Collections.Generic;

class Spawn { public int type; public float x, z; }

// pickups: 1 small medkit, 2 big medkit, 3 bullets, 4 shells, 5 rockets, 6 plasma cells,
//          7 shotgun, 8 rocket launcher, 9 plasma gun, 10 energy drink, 11 arena key card, 12 shield
class Item { public int type; public float x, z; }

class Level
{
    public const float C = 6f;          // cell size
    public const float CEIL = 5.4f;     // ceiling height
    public const float HALF_T = 0.4f;   // half wall thickness

    public static readonly string[] Names = { "Data Catacombs", "GPU Foundry", "Grok's Sanctum" };

    public int Index, N, M, Seed;
    public bool[,] vx; // wall on the line x = i*C next to cell row j   [N+1, M]
    public bool[,] hz; // wall on the line z = j*C next to cell column i [N, M+1]
    public bool[,] seen;               // cells shown on the automap
    public int si, sj, ei, ej;
    public float startYaw, titleX, titleZ, titleYaw;
    public readonly List<Spawn> spawns = new List<Spawn>();
    public readonly List<Item> items = new List<Item>();
    public bool HasRoom;
    public int ri0, rj0, ri1, rj1;      // boss room cells (inclusive)
    int[,] flow;
    int flowI = -1, flowJ = -1;

    // sci-fi doors in front of the boss arena: locked until the key card is found, then they open as you approach
    public readonly List<float[]> Gates = new List<float[]>();   // x, z, orientation (0: the door runs along z)
    readonly Dictionary<int, int> gateEdges = new Dictionary<int, int>();
    readonly bool[] doorClosed = new bool[8];
    bool doorsLocked;
    public bool DoorsLocked { get { return doorsLocked; } set { if (doorsLocked != value) { doorsLocked = value; flowI = -1; } } }
    public void SetDoorClosed(int i, bool c) { doorClosed[i] = c; }
    static int EdgeKey(int i, int j, int d) { return (i * 256 + j) * 4 + d; }

    void AddGate(int i, int j, int d)
    {
        if (d == 0) Gates.Add(new[] { (i + 1) * C, (j + 0.5f) * C, 0f });
        else if (d == 1) Gates.Add(new[] { i * C, (j + 0.5f) * C, 0f });
        else if (d == 2) Gates.Add(new[] { (i + 0.5f) * C, (j + 1) * C, 1f });
        else Gates.Add(new[] { (i + 0.5f) * C, j * C, 1f });
        int k = Gates.Count - 1;
        doorClosed[k] = true;
        gateEdges[EdgeKey(i, j, d)] = k;
        gateEdges[EdgeKey(i + DX[d], j + DZ[d], d ^ 1)] = k;
    }

    public string Name { get { return Names[Index]; } }
    public float ExitX { get { return (ei + 0.5f) * C; } }
    public float ExitZ { get { return (ej + 0.5f) * C; } }

    public static Level Make(int index, int seed)
    {
        var L = new Level();
        L.Index = index;
        L.Seed = seed;
        L.N = L.M = new[] { 11, 13, 15 }[index];
        int N = L.N, M = L.M;
        var r = new Random(seed * 31 + index * 7919);
        L.vx = new bool[N + 1, M];
        L.hz = new bool[N, M + 1];
        L.seen = new bool[N, M];
        for (int i = 0; i <= N; i++) for (int j = 0; j < M; j++) L.vx[i, j] = true;
        for (int i = 0; i < N; i++) for (int j = 0; j <= M; j++) L.hz[i, j] = true;

        // recursive backtracker
        var visited = new bool[N, M];
        var stack = new Stack<int>();
        stack.Push(0); visited[0, 0] = true;
        var nb = new List<int>();
        while (stack.Count > 0)
        {
            int c = stack.Peek(), ci = c % N, cj = c / N;
            nb.Clear();
            for (int d = 0; d < 4; d++)
            {
                int ni = ci + DX[d], nj = cj + DZ[d];
                if (ni >= 0 && nj >= 0 && ni < N && nj < M && !visited[ni, nj]) nb.Add(d);
            }
            if (nb.Count == 0) { stack.Pop(); continue; }
            int dir = nb[r.Next(nb.Count)];
            L.SetWall(ci, cj, dir, false);
            int ti = ci + DX[dir], tj = cj + DZ[dir];
            visited[ti, tj] = true;
            stack.Push(tj * N + ti);
        }
        // a few extra openings so there are loops, not only dead ends
        for (int i = 1; i < N; i++) for (int j = 0; j < M; j++) if (r.NextDouble() < 0.12) L.vx[i, j] = false;
        for (int i = 0; i < N; i++) for (int j = 1; j < M; j++) if (r.NextDouble() < 0.12) L.hz[i, j] = false;

        if (index == 2)
        {
            // open 3x3 arena in the far corner
            L.HasRoom = true;
            L.ri0 = N - 3; L.rj0 = M - 3; L.ri1 = N - 1; L.rj1 = M - 1;
            for (int i = L.ri0 + 1; i <= L.ri1; i++) for (int j = L.rj0; j <= L.rj1; j++) L.vx[i, j] = false;
            for (int i = L.ri0; i <= L.ri1; i++) for (int j = L.rj0 + 1; j <= L.rj1; j++) L.hz[i, j] = false;
        }

        if (index == 2)
        {
            // every way into the arena gets a gate
            for (int i = L.ri0; i <= L.ri1; i++) for (int j = L.rj0; j <= L.rj1; j++)
                for (int d = 0; d < 4; d++)
                {
                    int ni = i + DX[d], nj = j + DZ[d];
                    if (ni < 0 || nj < 0 || ni >= N || nj >= M || L.InRoom(ni, nj) || !L.Open(i, j, d)) continue;
                    L.AddGate(i, j, d);
                }
            L.DoorsLocked = true;
            // with the gates shut, no part of the maze may depend on walking through the arena
            for (bool changed = true; changed; )
            {
                changed = false;
                var dd = L.Bfs(0, 0);
                for (int i = 0; i < N && !changed; i++) for (int j = 0; j < M && !changed; j++)
                {
                    if (L.InRoom(i, j) || dd[i, j] != int.MaxValue) continue;
                    for (int d = 0; d < 4; d++)
                    {
                        int ni = i + DX[d], nj = j + DZ[d];
                        if (ni < 0 || nj < 0 || ni >= N || nj >= M || L.InRoom(ni, nj) || dd[ni, nj] == int.MaxValue) continue;
                        L.SetWall(i, j, d, false); changed = true; break;
                    }
                }
            }
        }

        var dist = L.Bfs(0, 0);
        L.si = 0; L.sj = 0;
        L.startYaw = !L.vx[1, 0] ? (float)(Math.PI / 2) : 0f;
        if (index < 2)
        {
            int best = -1;
            for (int i = 0; i < N; i++) for (int j = 0; j < M; j++)
                if (dist[i, j] > best) { best = dist[i, j]; L.ei = i; L.ej = j; }
        }
        else { L.ei = N - 2; L.ej = M - 2; }

        // drones: 1 seeker (kamikaze), 2 gunner, 3 heavy
        int[] counts = index == 0 ? new[] { 12, 6, 0 } : index == 1 ? new[] { 12, 9, 5 } : new[] { 10, 10, 6 };
        var types = new List<int>();
        for (int t = 0; t < 3; t++) for (int k = 0; k < counts[t]; k++) types.Add(t + 1);
        var cells = new List<int>();
        for (int i = 0; i < N; i++) for (int j = 0; j < M; j++)
        {
            if (dist[i, j] < 3 || (i == L.ei && j == L.ej) || L.InRoom(i, j)) continue;
            cells.Add(j * N + i);
        }
        Shuffle(cells, r);
        for (int k = 0; k < types.Count; k++)
        {
            int c = cells[k % cells.Count];
            L.spawns.Add(new Spawn
            {
                type = types[k],
                x = (c % N + 0.5f) * C + (float)(r.NextDouble() - 0.5) * 2.5f,
                z = (c / N + 0.5f) * C + (float)(r.NextDouble() - 0.5) * 2.5f
            });
        }
        L.PlaceItems(r, dist);
        L.FindLongestView();
        L.Visit(L.si, L.sj);
        return L;
    }

    static readonly int[] DX = { 1, -1, 0, 0 };
    static readonly int[] DZ = { 0, 0, 1, -1 };

    static void Shuffle(List<int> list, Random r)
    {
        for (int k = list.Count - 1; k > 0; k--) { int q = r.Next(k + 1); int t = list[k]; list[k] = list[q]; list[q] = t; }
    }

    void SetWall(int i, int j, int dir, bool v)
    {
        if (dir == 0) vx[i + 1, j] = v;
        else if (dir == 1) vx[i, j] = v;
        else if (dir == 2) hz[i, j + 1] = v;
        else hz[i, j] = v;
    }

    public bool Open(int i, int j, int dir)
    {
        int ni = i + DX[dir], nj = j + DZ[dir];
        if (ni < 0 || nj < 0 || ni >= N || nj >= M) return false;
        if (doorsLocked && gateEdges.ContainsKey(EdgeKey(i, j, dir))) return false;
        if (dir == 0) return !vx[i + 1, j];
        if (dir == 1) return !vx[i, j];
        if (dir == 2) return !hz[i, j + 1];
        return !hz[i, j];
    }

    // moves (i,j) to the open neighbour that is one step closer on a Bfs distance map
    public bool StepDown(int[,] d, ref int i, ref int j)
    {
        for (int k = 0; k < 4; k++)
        {
            if (!Open(i, j, k)) continue;
            int ni = i + DX[k], nj = j + DZ[k];
            if (d[ni, nj] < d[i, j]) { i = ni; j = nj; return true; }
        }
        return false;
    }

    public bool InRoom(int i, int j) { return HasRoom && i >= ri0 && i <= ri1 && j >= rj0 && j <= rj1; }

    // the automap reveals the cell you stand in and everything you can see down straight corridors
    public bool Visit(int i, int j)
    {
        bool changed = false;
        if (!seen[i, j]) { seen[i, j] = true; changed = true; }
        for (int d = 0; d < 4; d++)
        {
            int ci = i, cj = j;
            for (int k = 0; k < 4 && Open(ci, cj, d); k++)
            {
                ci += DX[d]; cj += DZ[d];
                if (!seen[ci, cj]) { seen[ci, cj] = true; changed = true; }
            }
        }
        return changed;
    }

    public int[,] Bfs(int i0, int j0)
    {
        var d = new int[N, M];
        for (int i = 0; i < N; i++) for (int j = 0; j < M; j++) d[i, j] = int.MaxValue;
        var q = new Queue<int>();
        d[i0, j0] = 0; q.Enqueue(j0 * N + i0);
        while (q.Count > 0)
        {
            int c = q.Dequeue(), ci = c % N, cj = c / N;
            for (int k = 0; k < 4; k++)
            {
                if (!Open(ci, cj, k)) continue;
                int ni = ci + DX[k], nj = cj + DZ[k];
                if (d[ni, nj] != int.MaxValue) continue;
                d[ni, nj] = d[ci, cj] + 1;
                q.Enqueue(nj * N + ni);
            }
        }
        return d;
    }

    public void CellOf(float x, float z, out int i, out int j)
    {
        i = Math.Max(0, Math.Min(N - 1, (int)Math.Floor(x / C)));
        j = Math.Max(0, Math.Min(M - 1, (int)Math.Floor(z / C)));
    }

    // next waypoint on the shortest path from (x,z) towards the target cell
    public void Toward(float x, float z, int ti, int tj, out float wx, out float wz)
    {
        if (flowI != ti || flowJ != tj) { flow = Bfs(ti, tj); flowI = ti; flowJ = tj; }
        int i, j; CellOf(x, z, out i, out j);
        int bi = i, bj = j, bd = flow[i, j];
        for (int k = 0; k < 4; k++)
        {
            if (!Open(i, j, k)) continue;
            int ni = i + DX[k], nj = j + DZ[k];
            if (flow[ni, nj] < bd) { bd = flow[ni, nj]; bi = ni; bj = nj; }
        }
        wx = (bi + 0.5f) * C; wz = (bj + 0.5f) * C;
        // go through the middle of the doorway so we do not scrape along the pillars
        if (bi != i)
        {
            float door = Math.Max(i, bi) * C, cz = (j + 0.5f) * C;
            wz = cz;
            wx = Math.Abs(z - cz) > 1.6f && Math.Abs(x - door) > 1.2f ? door - (bi - i) * 1.2f : door + (bi - i) * 1.8f;
        }
        else if (bj != j)
        {
            float door = Math.Max(j, bj) * C, cx = (i + 0.5f) * C;
            wx = cx;
            wz = Math.Abs(x - cx) > 1.6f && Math.Abs(z - door) > 1.2f ? door - (bj - j) * 1.2f : door + (bj - j) * 1.8f;
        }
    }

    // scarce loot, dead ends first so exploring pays off; the level's new weapon lands in the first one
    void PlaceItems(Random r, int[,] dist)
    {
        int[][] loot =
        {
            new[] { 7, 4, 4, 4, 4, 3, 3, 3, 3, 3, 3, 1, 2, 10, 12 },                         // two medkits on levels 1 and 2
            new[] { 8, 5, 5, 5, 4, 4, 4, 4, 3, 3, 3, 3, 3, 3, 1, 2, 10, 10, 12 },
            new[] { 9, 6, 6, 6, 6, 5, 5, 5, 4, 4, 4, 4, 3, 3, 3, 3, 3, 10, 10 },                // medkits and shields: see below
        };
        var ends = new List<int>(); var rest = new List<int>();
        for (int i = 0; i < N; i++) for (int j = 0; j < M; j++)
        {
            if (dist[i, j] < 1 || (i == ei && j == ej) || InRoom(i, j)) continue;
            int opens = 0;
            for (int d = 0; d < 4; d++) if (Open(i, j, d)) opens++;
            (opens == 1 && dist[i, j] >= 3 ? ends : rest).Add(j * N + i);
        }
        Shuffle(ends, r); Shuffle(rest, r);
        ends.AddRange(rest);
        var tl = loot[Index];
        for (int k = 0; k < tl.Length; k++)
        {
            int c = ends[k % ends.Count];
            items.Add(new Item
            {
                type = tl[k],
                x = (c % N + 0.5f) * C + (float)(r.NextDouble() - 0.5) * 1.2f,
                z = (c / N + 0.5f) * C + (float)(r.NextDouble() - 0.5) * 1.2f
            });
        }
        if (HasRoom)
        {
            // supplies for the boss fight: ammo in the corners, five medkits around the arena
            int[] corner = { 5, 6, 5, 6 };
            int[,] cc = { { ri0, rj0 }, { ri1, rj0 }, { ri0, rj1 }, { ri1, rj1 } };
            for (int k = 0; k < 4; k++) items.Add(new Item { type = corner[k], x = (cc[k, 0] + 0.5f) * C, z = (cc[k, 1] + 0.5f) * C });
            // five medkits and two shields scattered over the whole level, the arena included
            var used = new HashSet<int>();
            for (int k = 0; k < tl.Length; k++) used.Add(ends[k % ends.Count]);
            var all = new List<int>();
            for (int i = 0; i < N; i++) for (int j = 0; j < M; j++)
            {
                int c = j * N + i;
                if (used.Contains(c) || (i == ei && j == ej) || (i == si && j == sj)) continue;
                if (dist[i, j] >= 1 || InRoom(i, j)) all.Add(c);
            }
            Shuffle(all, r);
            int[] heal = { 1, 2, 1, 2, 1, 12, 12 };
            for (int k = 0; k < heal.Length && k < all.Count; k++)
                items.Add(new Item
                {
                    type = heal[k],
                    x = (all[k] % N + 0.5f) * C + (float)(r.NextDouble() - 0.5) * 1.2f,
                    z = (all[k] / N + 0.5f) * C + (float)(r.NextDouble() - 0.5) * 1.2f
                });
        }
    }

    // the longest straight corridor makes a nice view for the title screen
    void FindLongestView()
    {
        int best = -1;
        for (int i = 0; i < N; i++) for (int j = 0; j < M; j++)
            for (int d = 0; d < 4; d++)
            {
                int n = 0, ci = i, cj = j;
                while (Open(ci, cj, d)) { ci += DX[d]; cj += DZ[d]; n++; }
                if (n > best)
                {
                    best = n;
                    titleX = (i + 0.5f) * C - DX[d] * 1.5f; titleZ = (j + 0.5f) * C - DZ[d] * 1.5f;
                    titleYaw = (float)Math.Atan2(DX[d], DZ[d]);
                }
            }
    }

    // RGBA texel per lattice point: r = wall from (i,j) towards +z, g = wall towards +x, b = cell (i,j) seen on the automap
    public byte[] WallTexture()
    {
        var t = new byte[(N + 1) * (M + 1) * 4];
        for (int j = 0; j <= M; j++) for (int i = 0; i <= N; i++)
        {
            int o = (j * (N + 1) + i) * 4;
            t[o] = (byte)(j < M && vx[i, j] ? 255 : 0);
            t[o + 1] = (byte)(i < N && hz[i, j] ? 255 : 0);
            t[o + 2] = (byte)(i < N && j < M && seen[i, j] ? 255 : 0);
            t[o + 3] = 255;
        }
        return t;
    }

    // ------------------------------------------------------------------ exact distances, same formula as wallDist in the shader
    static float Rect(float px, float pz, float bx, float bz)
    {
        float dx = Math.Abs(px) - bx, dz = Math.Abs(pz) - bz;
        float ox = Math.Max(dx, 0), oz = Math.Max(dz, 0);
        return (float)Math.Sqrt(ox * ox + oz * oz) + Math.Min(Math.Max(dx, dz), 0);
    }

    bool VWall(int i, int j) { return i >= 0 && i <= N && j >= 0 && j < M && vx[i, j]; }
    bool HWall(int i, int j) { return i >= 0 && i < N && j >= 0 && j <= M && hz[i, j]; }

    public float WallDist(float x, float z)
    {
        int li = (int)Math.Floor(x / C + 0.5f), lj = (int)Math.Floor(z / C + 0.5f);
        float qx = x - li * C, qz = z - lj * C, d = 2.6f;
        if (VWall(li, lj)) d = Math.Min(d, Rect(qx, qz - 3, HALF_T, 3));
        if (VWall(li, lj - 1)) d = Math.Min(d, Rect(qx, qz + 3, HALF_T, 3));
        if (HWall(li, lj)) d = Math.Min(d, Rect(qx - 3, qz, 3, HALF_T));
        if (HWall(li - 1, lj)) d = Math.Min(d, Rect(qx + 3, qz, 3, HALF_T));
        float hw = N * C * 0.5f, hh = M * C * 0.5f;
        return Math.Min(d, -Rect(x - hw, z - hh, hw, hh));
    }

    public bool PillarAt(float lx, float lz)
    {
        if (!HasRoom) return true;
        // no pillars inside the arena
        return !(lx > ri0 * C + 0.1f && lx < (ri1 + 1) * C - 0.1f && lz > rj0 * C + 0.1f && lz < (rj1 + 1) * C - 0.1f);
    }

    // distance to walls and pillars on the ground plane
    public float Dist(float x, float z)
    {
        float d = WallDist(x, z);
        for (int k = 0; k < Gates.Count; k++)
            if (doorClosed[k])
            {
                var g = Gates[k];
                float gx = x - g[0], gz = z - g[1];
                d = Math.Min(d, g[2] < 0.5f ? Rect(gx, gz, 0.12f, 2.8f) : Rect(gx, gz, 2.8f, 0.12f));
            }
        float lx = C * (float)Math.Floor(x / C + 0.5f), lz = C * (float)Math.Floor(z / C + 0.5f);
        if (PillarAt(lx, lz))
        {
            float pd = (float)Math.Sqrt((x - lx) * (x - lx) + (z - lz) * (z - lz)) - 0.8f;
            d = Math.Min(d, pd);
        }
        return d;
    }

    public void Collide(ref float x, ref float z, float r)
    {
        for (int it = 0; it < 4; it++)
        {
            float d = Dist(x, z);
            if (d >= r) return;
            const float e = 0.03f;
            float gx = Dist(x + e, z) - Dist(x - e, z), gz = Dist(x, z + e) - Dist(x, z - e);
            float gl = (float)Math.Sqrt(gx * gx + gz * gz);
            if (gl < 1e-6f) return;
            x += gx / gl * (r - d);
            z += gz / gl * (r - d);
        }
    }

    public bool Los(float x0, float z0, float x1, float z1)
    {
        float dx = x1 - x0, dz = z1 - z0, len = (float)Math.Sqrt(dx * dx + dz * dz);
        if (len < 1e-4f) return true;
        dx /= len; dz /= len;
        float t = 0.3f;
        while (t < len - 0.3f)
        {
            float d = Dist(x0 + dx * t, z0 + dz * t);
            if (d < 0.05f) return false;
            t += Math.Max(d, 0.08f);
        }
        return true;
    }

    // distance along a 3D ray until it hits wall, pillar, floor or ceiling
    public float Ray(float x, float y, float z, float dx, float dy, float dz, float maxT)
    {
        float t = 0;
        for (int i = 0; i < 256 && t < maxT; i++)
        {
            float px = x + dx * t, py = y + dy * t, pz = z + dz * t;
            float d = Math.Min(Dist(px, pz), Math.Min(py, CEIL - py));
            if (d < 0.02f) return t;
            t += Math.Max(d, 0.02f);
        }
        return Math.Min(t, maxT);
    }
}
