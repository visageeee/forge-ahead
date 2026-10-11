using System;
using System.Collections.Generic;
using System.Linq;

namespace Weland
{
    public static class RingGeometry
    {
        public const int MaxSides = 128;

        public const int MaxInnerRings = 16;

        public static bool TryBuild(Point start, Point end, int sides, int innerPercent,
            out List<List<Point>> sectors, out string reason) =>
            TryBuild(start, end, sides, innerPercent > 0 ? 1 : 0, innerPercent, out sectors, out reason);

        public static bool TryBuild(Point start, Point end, int sides, int innerCount, int innerPercent,
            out List<List<Point>> sectors, out string reason)
        {
            sectors = new List<List<Point>>(); reason = null;
            if (sides < 3 || sides > MaxSides || innerCount < 0 || innerCount > MaxInnerRings ||
                (innerCount > 0 && (innerPercent < 1 || innerPercent > 95)))
            { reason = "Choose 3–128 sides, up to 16 inner rings, and an inner size of 1–95 percent."; return false; }
            int bands = Math.Max(1, innerCount);
            if (sides * bands > CircleTessellation.MaxTiles)
            { reason = "Reduce sides or inner rings (up to 256 sectors per drag)."; return false; }
            double cx = (start.X + end.X) / 2.0, cy = (start.Y + end.Y) / 2.0;
            double rx = Math.Abs(end.X - start.X) / 2.0, ry = Math.Abs(end.Y - start.Y) / 2.0;
            if (rx < 1 || ry < 1) { reason = "Drag a larger ring."; return false; }
            Point center = new Point((short)Math.Round(cx), (short)Math.Round(cy));
            var rings = new List<List<Point>>();
            for (int band = 0; band <= innerCount; ++band)
            {
                double factor = innerCount == 0 ? 1 : 1 - band * (1 - innerPercent / 100.0) / innerCount;
                var ring = new List<Point>();
                for (int i = 0; i < sides; ++i)
                {
                    double angle = -Math.PI / 2 + i * Math.PI * 2 / sides;
                    ring.Add(new Point((short)Math.Round(cx + rx * Math.Cos(angle) * factor),
                        (short)Math.Round(cy + ry * Math.Sin(angle) * factor)));
                }
                rings.Add(ring);
            }
            var vertices = rings.SelectMany(r => r).ToList();
            if (innerCount == 0) vertices.Add(center);
            if (vertices.Distinct().Count() != vertices.Count)
            { reason = "Ring vertices are too close: enlarge the ring or reduce sides/inner rings."; return false; }
            for (int band = 0; band < bands; ++band)
                for (int i = 0; i < sides; ++i)
                {
                    int next = (i + 1) % sides;
                    var outer = rings[band];
                    var sector = innerCount > 0
                        ? new List<Point> { outer[i], outer[next], rings[band+1][next], rings[band+1][i] }
                        : new List<Point> { outer[i], outer[next], center };
                    for (int j = 0; j < sector.Count; ++j)
                        for (int k = 0; k < sector.Count; ++k)
                        {
                            if (k == j || k == (j + 1) % sector.Count) continue;
                            Point a = sector[j], b = sector[(j + 1) % sector.Count], p = sector[k];
                            long cross = (long)(b.X-a.X)*(p.Y-a.Y) - (long)(b.Y-a.Y)*(p.X-a.X);
                            if (cross <= 0)
                            { reason = "Ring sectors are too narrow: enlarge the ring or change its inner size/count."; return false; }
                        }
                    sectors.Add(sector);
                }
            return true;
        }
    }

    public partial class Editor
    {
        int ringSides = 16, ringInnerPercent = 50, ringInnerCount = 1;
        bool ringHasInner = true, ringFilled = true;
        public int RingSides { get => ringSides; set { int n = Math.Max(3, Math.Min(RingGeometry.MaxSides, value)); if (n != ringSides) { CancelRing(); ringSides = n; } } }
        public int RingInnerPercent { get => ringInnerPercent; set { int n = Math.Max(1, Math.Min(95, value)); if (n != ringInnerPercent) { CancelRing(); ringInnerPercent = n; } } }
        public bool RingHasInner { get => ringHasInner; set { if (ringHasInner != value) { CancelRing(); ringHasInner = value; } } }
        public int RingInnerCount { get => ringInnerCount; set { int n = Math.Max(1, Math.Min(RingGeometry.MaxInnerRings, value)); if (n != ringInnerCount) { CancelRing(); ringInnerCount = n; } } }
        public bool RingFilled { get => ringFilled; set { if (ringFilled != value) { CancelRing(); ringFilled = value; } } }
        public bool DrawingRing { get; private set; }
        public Point RingStart { get; private set; }
        public List<List<Point>> RingSectors { get; private set; } = new List<List<Point>>();
        public string RingMessage { get; private set; }

        public void CancelRing()
        {
            DrawingRing = false; RingSectors.Clear(); RingMessage = null;
            Dirty = RedrawAll = true;
        }

        public void StartRing(short x, short y)
        {
            CommitHistory(); CancelRing();
            RingStart = SnapBoxPoint(x, y); DrawingRing = true;
            UpdateRing(x, y, false);
        }

        public void UpdateRing(short x, short y, bool circular)
        {
            if (!DrawingRing) return;
            Point end = SnapBoxPoint(x, y);
            if (circular)
            {
                int dx = end.X - RingStart.X, dy = end.Y - RingStart.Y;
                int sx = dx < 0 ? -1 : 1, sy = dy < 0 ? -1 : 1;
                int size = Math.Max(Math.Abs(dx), Math.Abs(dy));
                size = Math.Min(size, sx > 0 ? short.MaxValue - RingStart.X : RingStart.X - short.MinValue);
                size = Math.Min(size, sy > 0 ? short.MaxValue - RingStart.Y : RingStart.Y - short.MinValue);
                end = new Point((short)(RingStart.X + sx * size), (short)(RingStart.Y + sy * size));
            }
            // Preview only the small generated sector set. Plan against the map
            // on release so a large map does not slow down every mouse event.
            RingGeometry.TryBuild(RingStart, end, RingSides, RingHasInner ? RingInnerCount : 0, RingInnerPercent,
                out var sectors, out string reason);
            RingSectors = reason == null ? sectors : new List<List<Point>>();
            RingMessage = reason; Dirty = RedrawAll = true;
        }

        public void FinishRing(short x, short y, bool circular)
        {
            if (!DrawingRing) return;
            UpdateRing(x, y, circular); DrawingRing = false;
            if (RingMessage != null) return;
            if (!RingFilled)
            {
                FinishShapeOutline(RingSectors, "Draw ring outline", out string outlineReason);
                RingMessage = outlineReason; return;
            }
            if (!Level.PlanCircleTessellation(RingSectors, out var plan, out var selected, out int added, out string reason))
            { RingMessage = reason; return; }
            if (RingStairs)
            {
                var steps = Enumerable.Range(0, selected.Count).Select(i => i % RingSides).ToList();
                if (!ApplyStairHeights(plan, selected, steps, RingStairStart, RingStairStep, RingStairCeiling, RingCeilingFollows,
                    out bool changed, out reason)) { RingMessage = reason; return; }
                if (changed) added = Math.Max(1, added);
            }
            if (added > 0)
            {
                SetUndo(RingStairs ? "Draw ring stairs" : "Draw ring"); Level.Load(plan.Save().Clone()); Changed = true;
            }
            Selection.Clear(); foreach (short index in selected) Selection.Polygons.Add(index);
            RingMessage = null; Dirty = RedrawAll = true;
        }
    }
}
