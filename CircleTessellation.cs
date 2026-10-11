using System;
using System.Collections.Generic;
using System.Linq;

namespace Weland
{
    public static class CircleTessellation
    {
        public const int MaxTiles = 256;

        // Round the lattice dimensions once, rather than each tile independently.
        // Neighbouring tiles therefore use exactly the same integer vertices.
        public static bool TryBuild(Point origin, Point end, int sides, int edgeSize,
            out List<List<Point>> tiles, out string reason)
            => TryBuild(origin, end, sides, edgeSize, out tiles, out _, out reason);

        public static bool TryBuild(Point origin, Point end, int sides, int edgeSize,
            out List<List<Point>> tiles, out List<int> stairSteps, out string reason)
        {
            tiles = new List<List<Point>>(); stairSteps = new List<int>(); reason = null;
            if (sides != 3 && sides != 4 && sides != 6 && sides != 8)
            { reason = "Tessellation needs 3, 4, 6, or 8 sides; octagons include square fillers."; return false; }
            if (edgeSize < 16 || edgeSize > 8192)
            { reason = "Choose an edge length between 1/64 and 8 world units."; return false; }
            int half = (int)Math.Round(edgeSize / 2.0);
            int height = (int)Math.Round(half * Math.Sqrt(3));
            int cut = (int)Math.Round(edgeSize / Math.Sqrt(2));
            int pitch = edgeSize + 2 * cut;
            int stepX = sides == 6 ? 3 * half : sides == 8 ? pitch : sides == 3 ? 2 * half : edgeSize;
            int stepY = sides == 6 ? 2 * height : sides == 8 ? pitch : sides == 3 ? height : edgeSize;
            int colEnd = (int)Math.Floor((end.X - origin.X) / (double)stepX);
            int rowEnd = (int)Math.Floor((end.Y - origin.Y) / (double)stepY);
            int firstCol = Math.Min(0, colEnd), lastCol = Math.Max(0, colEnd);
            int firstRow = Math.Min(0, rowEnd), lastRow = Math.Max(0, rowEnd);
            long cols = lastCol - firstCol + 1, rows = lastRow - firstRow + 1;
            long count = cols * rows * (sides == 3 ? 2 : 1);
            if (sides == 8) count += (cols - 1) * (rows - 1);
            if (count > MaxTiles)
            { reason = "Drag a smaller patch or increase the size (up to 256 tiles per drag)."; return false; }
            bool alongX = Math.Abs(end.X - origin.X) >= Math.Abs(end.Y - origin.Y);
            var result = new List<List<Point>>();
            var steps = new List<int>();
            int currentStep = 0;
            bool Add(int x, int y, params int[] offsets)
            {
                var ring = new List<Point>();
                for (int i = 0; i < offsets.Length; i += 2)
                {
                    int px = x + offsets[i], py = y + offsets[i + 1];
                    if (px < short.MinValue || px > short.MaxValue || py < short.MinValue || py > short.MaxValue)
                        return false;
                    ring.Add(new Point((short)px, (short)py));
                }
                result.Add(ring); steps.Add(currentStep); return true;
            }
            for (int row = firstRow; row <= lastRow; ++row)
                for (int col = firstCol; col <= lastCol; ++col)
                {
                    currentStep = Math.Abs(alongX ? col : row);
                    int x = origin.X + col * stepX, y = origin.Y + row * stepY;
                    bool added;
                    if (sides == 3)
                    {
                        x += (row & 1) * half;
                        added = Add(x, y, 0,0, 2*half,0, half,height) &&
                            Add(x, y, 2*half,0, 3*half,height, half,height);
                    }
                    else if (sides == 4)
                        added = Add(x, y, 0,0, edgeSize,0, edgeSize,edgeSize, 0,edgeSize);
                    else if (sides == 6)
                    {
                        x -= half; y += (col & 1) * height;
                        added = Add(x, y, half,0, 3*half,0, 4*half,height,
                            3*half,2*height, half,2*height, 0,height);
                    }
                    else
                    {
                        x -= cut;
                        added = Add(x, y, cut,0, cut+edgeSize,0, pitch,cut, pitch,cut+edgeSize,
                            cut+edgeSize,pitch, cut,pitch, 0,cut+edgeSize, 0,cut);
                        if (added && col < lastCol && row < lastRow)
                        {
                            // Fillers sit between lattice steps; use the higher
                            // step index of their neighbouring rows/columns.
                            currentStep = alongX ? Math.Max(Math.Abs(col), Math.Abs(col+1)) : Math.Max(Math.Abs(row), Math.Abs(row+1));
                            added = Add(x+pitch, y+pitch, 0,-cut, cut,0, 0,cut, -cut,0);
                        }
                    }
                    if (!added)
                    { reason = "The patch extends beyond the map coordinate limits."; return false; }
                }
            tiles = result; stairSteps = steps; return true;
        }
    }

    public partial class Level
    {
        public bool PlanCircleTessellation(List<List<Point>> tiles, out Level plan,
            out List<short> selected, out int added, out string reason)
        {
            plan = null; selected = new List<short>(); added = 0; reason = null;
            if (tiles.Count == 0 || tiles.Count > CircleTessellation.MaxTiles)
            { reason = "Choose a patch of 1–256 tiles."; return false; }
            var trial = new Level(); trial.Load(Save().Clone());
            foreach (var corners in tiles)
            {
                var ring = trial.CircleRing(corners);
                var points = new HashSet<Point>(ring);
                short existing = -1;
                for (int i = 0; i < trial.Polygons.Count; ++i)
                {
                    Polygon p = trial.Polygons[i];
                    if (p.VertexCount == ring.Count && points.SetEquals(p.EndpointIndexes.Take(p.VertexCount).Select(index => trial.Endpoints[index])))
                    { existing = (short)i; break; }
                }
                if (existing != -1)
                {
                    if (!Filter(trial.Polygons[existing]))
                    { reason = "A matching tile is hidden by the current view filter."; return false; }
                    selected.Add(existing); continue;
                }
                // Tessellation adds geometry in empty space, without insetting each
                // cell separately (which could divide an existing polygon repeatedly).
                if (!trial.CreatePlainShape(corners, out reason))
                { reason = "Patch blocked by existing geometry: " + reason; return false; }
                selected.Add((short)(trial.Polygons.Count - 1)); ++added;
            }
            plan = trial; return true;
        }
    }

    public partial class Editor
    {
        bool circleTessellation;
        int circleTileSize = 1024;
        public bool CircleTessellationEnabled
        {
            get => circleTessellation;
            set { if (circleTessellation != value) { CancelCircle(); circleTessellation = value; } }
        }
        public int CircleTileSize
        {
            get => circleTileSize;
            set {
                int size = Math.Max(16, Math.Min(8192, value));
                if (size != circleTileSize) { CancelCircle(); circleTileSize = size; }
            }
        }
        public bool DrawingCircleTessellation { get; private set; }
        public List<List<Point>> CircleTiles { get; private set; } = new List<List<Point>>();
        Level circleTilePlan;
        List<short> circleTileSelection;
        int circleTilesAdded;

        void UpdateCircleTessellation(short x, short y)
        {
            circleTilePlan = null;
            CircleTessellation.TryBuild(CircleStart, new Point(x,y), CircleSides, CircleTileSize,
                out var tiles, out var stairSteps, out string reason);
            CircleTiles = tiles;
            if (reason == null)
            {
                if (CircleFilled)
                    Level.PlanCircleTessellation(tiles, out circleTilePlan, out circleTileSelection,
                        out circleTilesAdded, out reason);
                else
                {
                    Level.PlanShapeOutline(tiles, out circleTilePlan, out circleTileSelection,
                        out bool changed, out reason);
                    circleTilesAdded = changed ? 1 : 0;
                }
            }
            if (circleTilePlan != null && CircleFilled && CircleStairs)
            {
                if (!ApplyStairHeights(circleTilePlan, circleTileSelection, stairSteps,
                    CircleStairStart, CircleStairStep, CircleStairCeiling, CircleCeilingFollows, out bool changed, out reason)) circleTilePlan = null;
                else if (changed) circleTilesAdded = Math.Max(1, circleTilesAdded);
            }
            CircleMessage = reason;
            Dirty = RedrawAll = true;
        }

        void FinishCircleTessellation(short x, short y)
        {
            UpdateCircleTessellation(x,y);
            DrawingCircle = false;
            if (circleTilePlan == null) return;
            if (circleTilesAdded > 0)
            {
                SetUndo(CircleFilled ? (CircleStairs ? "Tessellate stairs" : "Tessellate polygons") : "Tessellate outlines");
                Level.Load(circleTilePlan.Save().Clone());
                Changed = true;
            }
            Selection.Clear();
            foreach (short index in circleTileSelection)
                if (CircleFilled) Selection.Polygons.Add(index);
                else Selection.Lines.Add(index);
            CircleMessage = null;
            circleTilePlan = null;
            Dirty = RedrawAll = true;
        }
    }
}
