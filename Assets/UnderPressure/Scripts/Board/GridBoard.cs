using System.Collections.Generic;
using UnityEngine;

namespace UnderPressure
{
    /// <summary>The tile grid: occupancy, coordinates, and connectivity to the spout.</summary>
    public class GridBoard : MonoBehaviour
    {
        public int Cols { get; private set; }
        public int Rows { get; private set; }
        public float S { get; private set; }
        /// <summary>World position of the centre of cell (0,0).</summary>
        public Vector3 Origin { get; private set; }
        public Spout Spout { get; private set; }

        PipePiece[,] cells;

        public struct Delta
        {
            public List<PipePiece> newlyConnected;    // ordered by distance from the spout
            public List<PipePiece> newlyDisconnected;
        }

        public void Build(GameConfig cfg, Vector3 origin)
        {
            Cols = cfg.columns; Rows = cfg.rows; S = cfg.tileSize; Origin = origin;
            cells = new PipePiece[Cols, Rows];

            var a = Mats.Lit(Theme.TileA, 0.5f);
            var b = Mats.Lit(Theme.TileB, 0.5f);
            var tiles = Prims.Empty("Tiles", transform, Vector3.zero).transform;
            Vector3 min = CellCenter(Vector2Int.zero) - new Vector3(S, 0, S) * 0.5f;
            float w = Cols * S, d = Rows * S;
            // dark base so the gaps between tiles read as panel seams
            Prims.Make(PrimitiveType.Cube, tiles, min + new Vector3(w * 0.5f, -0.005f, d * 0.5f), new Vector3(w, 0.02f, d), Mats.Lit(Theme.BoardBase, 0.3f), null, "BoardBase");
            for (int x = 0; x < Cols; x++)
            for (int y = 0; y < Rows; y++)
                Prims.Make(PrimitiveType.Cube, tiles, CellCenter(new Vector2Int(x, y)),
                    new Vector3(S * 0.95f, 0.04f, S * 0.95f), ((x + y) & 1) == 0 ? a : b, null, $"Tile_{x}_{y}");

            // hazard-striped border (no colliders)
            var black = Mats.Lit(Theme.Ink, 0.3f);
            var yellow = Mats.Lit(Theme.Hazard, 0.4f);
            const float seg = 0.4f, bw = 0.22f;
            int i = 0;
            for (float x = -bw; x < w + bw - 0.01f; x += seg, i++)
            {
                var m = (i & 1) == 0 ? black : yellow;
                Prims.Make(PrimitiveType.Cube, tiles, min + new Vector3(x + seg * 0.5f, 0.01f, -bw * 0.5f), new Vector3(seg, 0.03f, bw), m, null, "Border");
                Prims.Make(PrimitiveType.Cube, tiles, min + new Vector3(x + seg * 0.5f, 0.01f, d + bw * 0.5f), new Vector3(seg, 0.03f, bw), m, null, "Border");
            }
            i = 0;
            for (float z = 0f; z < d - 0.01f; z += seg, i++)
            {
                var m = (i & 1) == 0 ? black : yellow;
                Prims.Make(PrimitiveType.Cube, tiles, min + new Vector3(-bw * 0.5f, 0.01f, z + seg * 0.5f), new Vector3(bw, 0.03f, seg), m, null, "Border");
                Prims.Make(PrimitiveType.Cube, tiles, min + new Vector3(w + bw * 0.5f, 0.01f, z + seg * 0.5f), new Vector3(bw, 0.03f, seg), m, null, "Border");
            }

            var sc = new Vector2Int(Mathf.Clamp(cfg.spoutCell.x, 0, Cols - 1), Mathf.Clamp(cfg.spoutCell.y, 0, Rows - 1));
            Spout = Spout.Create(transform, sc, cfg.spoutDirection, CellCenter(sc), S);
        }

        // ------------------------------------------------------------------ coordinates

        public Vector3 CellCenter(Vector2Int c) => Origin + new Vector3(c.x * S, 0f, c.y * S);

        public Vector2Int WorldToCell(Vector3 p) =>
            new Vector2Int(Mathf.RoundToInt((p.x - Origin.x) / S), Mathf.RoundToInt((p.z - Origin.z) / S));

        public bool InBounds(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < Cols && c.y < Rows;

        public Vector3 Min => Origin - new Vector3(S, 0, S) * 0.5f;
        public Vector3 Max => Origin + new Vector3((Cols - 0.5f) * S, 0, (Rows - 0.5f) * S);

        // ------------------------------------------------------------------ occupancy

        public PipePiece Get(Vector2Int c) => InBounds(c) ? cells[c.x, c.y] : null;
        public bool IsSpout(Vector2Int c) => Spout != null && c == Spout.Cell;
        public bool IsFree(Vector2Int c) => InBounds(c) && !IsSpout(c) && cells[c.x, c.y] == null;

        public void Place(PipePiece p, Vector2Int c)
        {
            cells[c.x, c.y] = p;
            p.SetCell(c);
        }

        public PipePiece Remove(Vector2Int c)
        {
            var p = Get(c);
            if (p != null) cells[c.x, c.y] = null;
            return p;
        }

        public IEnumerable<PipePiece> AllPlaced()
        {
            for (int x = 0; x < Cols; x++)
            for (int y = 0; y < Rows; y++)
                if (cells[x, y] != null) yield return cells[x, y];
        }

        // ------------------------------------------------------------------ connectivity

        /// <summary>Which placed pieces are structurally connected to the spout (latched), and which just changed.</summary>
        public Delta Recompute(bool animate, float baseDelay = 0f)
        {
            var depth = new Dictionary<PipePiece, int>();
            var queue = new Queue<(Vector2Int cell, Dir from, int d)>();
            queue.Enqueue((Spout.Cell + DirUtil.Offset(Spout.Out), DirUtil.Opposite(Spout.Out), 0));
            while (queue.Count > 0)
            {
                var (cell, from, d) = queue.Dequeue();
                var p = Get(cell);
                if (p == null || depth.ContainsKey(p) || !p.HasOpening(from)) continue;
                depth[p] = d;
                foreach (var o in p.WorldOpenings())
                    queue.Enqueue((cell + DirUtil.Offset(o), DirUtil.Opposite(o), d + 1));
            }

            var delta = new Delta { newlyConnected = new List<PipePiece>(), newlyDisconnected = new List<PipePiece>() };
            foreach (var p in AllPlaced())
            {
                bool now = depth.ContainsKey(p);
                if (now && !p.Connected) delta.newlyConnected.Add(p);
                if (!now && p.Connected) delta.newlyDisconnected.Add(p);
            }
            delta.newlyConnected.Sort((x, y) => depth[x].CompareTo(depth[y]));

            foreach (var p in AllPlaced())
            {
                var joined = new HashSet<Dir>();
                foreach (var o in p.WorldOpenings())
                {
                    var n = p.Cell + DirUtil.Offset(o);
                    if (IsSpout(n) && Spout.Out == DirUtil.Opposite(o)) joined.Add(o);
                    else
                    {
                        var np = Get(n);
                        if (np != null && np.HasOpening(DirUtil.Opposite(o))) joined.Add(o);
                    }
                }
                int rank = delta.newlyConnected.IndexOf(p);
                float delay = baseDelay + (rank > 0 ? rank * 0.08f : 0f);
                p.ApplyConnection(depth.ContainsKey(p), joined, delay, animate);
            }

            var first = Get(Spout.Cell + DirUtil.Offset(Spout.Out));
            Spout.SetClamp(first != null && depth.ContainsKey(first), animate);
            return delta;
        }

        public Rect CellRect(Vector2Int c)
        {
            var center = CellCenter(c);
            return new Rect(center.x - S * 0.5f, center.z - S * 0.5f, S, S);
        }
    }
}
