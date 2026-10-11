using System;
using System.Collections.Generic;

namespace Weland
{
    public partial class Editor
    {
        int circleSides = 8;
        public int CircleSides
        {
            get => circleSides;
            set {
                int sides = Math.Max(3, Math.Min(8, value));
                if (sides != circleSides) { CancelCircle(); circleSides = sides; }
            }
        }
        public bool DrawingCircle { get; private set; }
        public Point CircleStart { get; private set; }
        public List<Point> CircleVertices { get; private set; } = new List<Point>();
        public string CircleMessage { get; private set; }

        public void CancelCircle()
        {
            DrawingCircle = false;
            CircleVertices.Clear();
            CircleTiles.Clear();
            circleTilePlan = null;
            DrawingCircleTessellation = false;
            CircleMessage = null;
            Dirty = RedrawAll = true;
        }

        public void StartCircle(short X, short Y)
        {
            CommitHistory();
            CancelCircle();
            if (!CircleTessellationEnabled) Selection.Clear();
            DrawingCircleTessellation = CircleTessellationEnabled;
            CircleStart = SnapBoxPoint(X, Y);
            DrawingCircle = true;
            UpdateCircle(X, Y, false);
        }

        public void UpdateCircle(short X, short Y, bool circular)
        {
            if (!DrawingCircle) return;
            if (DrawingCircleTessellation) { UpdateCircleTessellation(X, Y); return; }
            Point end = SnapBoxPoint(X, Y);
            int dx = end.X - CircleStart.X, dy = end.Y - CircleStart.Y;
            if (circular)
            {
                int sx = dx < 0 ? -1 : 1, sy = dy < 0 ? -1 : 1;
                int size = Math.Max(Math.Abs(dx), Math.Abs(dy));
                size = Math.Min(size, sx > 0 ? short.MaxValue - CircleStart.X : CircleStart.X - short.MinValue);
                size = Math.Min(size, sy > 0 ? short.MaxValue - CircleStart.Y : CircleStart.Y - short.MinValue);
                dx = sx * size; dy = sy * size;
            }
            double cx = CircleStart.X + dx / 2.0, cy = CircleStart.Y + dy / 2.0;
            double rx = Math.Abs(dx) / 2.0, ry = Math.Abs(dy) / 2.0;
            CircleVertices.Clear();
            for (int i = 0; i < CircleSides; ++i)
            {
                double angle = -Math.PI / 2 + i * 2 * Math.PI / CircleSides;
                Point p = new Point((short)Math.Round(cx + rx * Math.Cos(angle)),
                                    (short)Math.Round(cy + ry * Math.Sin(angle)));
                short near = ClosestPoint(p);
                CircleVertices.Add(near == -1 ? p : Level.Endpoints[near]);
            }
            string reason;
            if (CircleFilled) Level.CanCreateCirclePolygon(CircleVertices, out reason);
            else Level.PlanShapeOutline(new List<List<Point>> { CircleVertices }, out _, out _, out _, out reason);
            CircleMessage = reason;
            Dirty = RedrawAll = true;
        }

        public void FinishCircle(short X, short Y, bool circular)
        {
            if (!DrawingCircle) return;
            if (DrawingCircleTessellation) { FinishCircleTessellation(X, Y); return; }
            UpdateCircle(X, Y, circular);
            DrawingCircle = false;
            if (!CircleFilled)
            {
                FinishShapeOutline(new List<List<Point>> { CircleVertices }, "Draw circle outline", out string outlineReason);
                CircleMessage = outlineReason;
                return;
            }
            if (!Level.CanCreateCirclePolygon(CircleVertices, out string reason)) { CircleMessage = reason; return; }
            SetUndo();
            if (Level.CreateCirclePolygon(CircleVertices, out reason))
            {
                Selection.Polygon = Level.LastCreatedCirclePolygonIndex;
                CircleMessage = null;
                Changed = true;
            }
            else CircleMessage = reason;
            Dirty = RedrawAll = true;
        }
    }

    public partial class Level
    {
        static long ShapeCross(Point a, Point b, Point p) =>
            ((long)b.X - a.X) * (p.Y - a.Y) - ((long)b.Y - a.Y) * (p.X - a.X);

        static bool EntersShape(Point a, Point b, List<Point> corners)
        {
            double low = 0, high = 1;
            for (int i = 0; i < corners.Count; ++i)
            {
                Point p = corners[i], q = corners[(i + 1) % corners.Count];
                double initial = ShapeCross(p, q, a), change = ShapeCross(p, q, b) - initial;
                if (change == 0) { if (initial <= 0) return false; }
                else if (change > 0) low = Math.Max(low, -initial / change);
                else high = Math.Min(high, -initial / change);
                if (low >= high) return false;
            }
            return low < high;
        }

        List<Point> CircleRing(List<Point> corners)
        {
            var ring = new List<Point>();
            for (int i = 0; i < corners.Count; ++i)
            {
                Point a = corners[i], b = corners[(i + 1) % corners.Count];
                var points = new List<Point> { a };
                foreach (Point p in Endpoints)
                    if (OnSegment(p, a, b) && !SamePoint(p, b) && !points.Contains(p)) points.Add(p);
                points.Sort((p, q) =>
                    (((long)p.X-a.X)*(p.X-a.X)+((long)p.Y-a.Y)*(p.Y-a.Y)).CompareTo(
                     ((long)q.X-a.X)*(q.X-a.X)+((long)q.Y-a.Y)*(q.Y-a.Y)));
                ring.AddRange(points);
            }
            return ring;
        }

        public short LastCreatedCirclePolygonIndex { get; private set; } = -1;

        public bool CanCreateCirclePolygon(List<Point> corners, out string reason)
        {
            if (CanCreatePlainShape(corners, out reason)) return true;
            // Invalid or snapped concave rings must not enter the inset planner.
            if (corners.Count < 3 || corners.Count > Polygon.MaxVertexCount) return false;
            for (int i = 0; i < corners.Count; ++i)
                for (int j = 0; j < corners.Count; ++j)
                    if (j != i && j != (i + 1) % corners.Count &&
                        ShapeCross(corners[i], corners[(i + 1) % corners.Count], corners[j]) <= 0) return false;
            short container = ShapeContainer(corners);
            if (container == -1) return false;
            if (!Filter(Polygons[container]))
            { reason = "Circle overlaps geometry hidden by the current view filter."; return false; }
            if (Polygons[container].Type != PolygonType.Normal)
            { reason = "Circle insets currently require a normal polygon."; return false; }
            var trial = new Level(); trial.Load(Save().Clone());
            return trial.SplitInset(container, corners, out reason);
        }

        public bool CreateCirclePolygon(List<Point> corners, out string reason)
        {
            if (CanCreatePlainShape(corners, out reason))
            {
                if (!CreatePlainShape(corners, out reason)) return false;
                LastCreatedCirclePolygonIndex = (short)(Polygons.Count - 1);
                return true;
            }
            short container = ShapeContainer(corners);
            if (container == -1 || !CanCreateCirclePolygon(corners, out reason)) return false;
            if (!SplitInset(container, corners, out reason)) return false;
            LastCreatedCirclePolygonIndex = container;
            return true;
        }

        bool CanCreatePlainShape(List<Point> corners, out string reason)
        {
            reason = null;
            if (corners.Count < 3 || corners.Count > Polygon.MaxVertexCount)
            { reason = "Choose between three and eight sides."; return false; }
            var unique = new HashSet<Point>(corners);
            if (unique.Count != corners.Count)
            { reason = "Drag a larger circle; some vertices coincide."; return false; }
            for (int i = 0; i < corners.Count; ++i)
                if (ShapeCross(corners[i], corners[(i+1)%corners.Count], corners[(i+2)%corners.Count]) <= 0)
                { reason = "Snapping would make this polygon degenerate or concave."; return false; }
            for (int i = 0; i < corners.Count; ++i)
                foreach (Point p in corners)
                    if (ShapeCross(corners[i], corners[(i+1)%corners.Count], p) < 0)
                    { reason = "Snapping would make this polygon intersect itself."; return false; }
            foreach (Point p in Endpoints)
            {
                bool inside = true;
                for (int i = 0; i < corners.Count; ++i)
                    if (ShapeCross(corners[i], corners[(i+1)%corners.Count], p) <= 0) { inside = false; break; }
                if (inside) { reason = "Circle contains existing vertices."; return false; }
            }
            foreach (Line line in Lines)
                if (EntersShape(Endpoints[line.EndpointIndexes[0]], Endpoints[line.EndpointIndexes[1]], corners))
                { reason = "Circle crosses existing geometry."; return false; }
            double cx = 0, cy = 0;
            foreach (Point p in corners) { cx += p.X; cy += p.Y; }
            foreach (Polygon polygon in Polygons)
                if (ContainsBoxCenter(polygon, cx / corners.Count, cy / corners.Count))
                { reason = "Circle overlaps an existing polygon."; return false; }
            List<Point> ring = CircleRing(corners);
            if (ring.Count > Polygon.MaxVertexCount)
            { reason = "Shared edges would exceed the eight-vertex polygon limit."; return false; }
            var additions = new Dictionary<short,int>();
            int splits = 0;
            foreach (Line line in Lines)
            {
                Point a = Endpoints[line.EndpointIndexes[0]], b = Endpoints[line.EndpointIndexes[1]];
                foreach (Point p in ring)
                    if (!SamePoint(p,a) && !SamePoint(p,b) && OnSegment(p,a,b))
                    {
                        ++splits;
                        foreach (short owner in new[] {line.ClockwisePolygonOwner,line.CounterclockwisePolygonOwner})
                            if (owner != -1) { if (!additions.ContainsKey(owner)) additions[owner]=0; ++additions[owner]; }
                    }
            }
            foreach (var entry in additions)
                if (Polygons[entry.Key].VertexCount + entry.Value > Polygon.MaxVertexCount)
                { reason = "A shared-wall split would exceed an adjacent polygon's vertex limit."; return false; }
            if (Endpoints.Count + ring.Count > short.MaxValue || Lines.Count + splits + ring.Count + 2 > short.MaxValue ||
                Sides.Count + splits * 4 > short.MaxValue || Polygons.Count >= short.MaxValue)
            { reason = "Map geometry limit reached."; return false; }
            for (int i = 0; i < ring.Count; ++i)
            {
                int matches = 0;
                foreach (Line line in Lines)
                    if (OnSegment(ring[i],Endpoints[line.EndpointIndexes[0]],Endpoints[line.EndpointIndexes[1]]) &&
                        OnSegment(ring[(i+1)%ring.Count],Endpoints[line.EndpointIndexes[0]],Endpoints[line.EndpointIndexes[1]]))
                    {
                        if (++matches > 1 || (line.ClockwisePolygonOwner != -1 && line.CounterclockwisePolygonOwner != -1))
                        { reason = "Existing boundary lines overlap or already have two polygon owners."; return false; }
                    }
            }
            return true;
        }

        bool CreatePlainShape(List<Point> corners, out string reason)
        {
            if (!CreateShapeBoundary(corners, out var loop, out reason)) return false;
            return CreatePolygonFromLoop(loop,true);
        }

        bool CreateShapeBoundary(List<Point> corners, out List<short> loop, out string reason)
        {
            loop = new List<short>();
            if (!CanCreatePlainShape(corners, out reason)) return false;
            List<Point> ring = CircleRing(corners);
            var indexes = new List<short>();
            foreach (Point p in ring)
            {
                short index = -1;
                for (int i = 0; i < Endpoints.Count; ++i)
                    if (SamePoint(p,Endpoints[i])) { index=(short)i; break; }
                if (index == -1) index=NewPoint(p.X,p.Y);
                for (int i = Lines.Count-1; i >= 0; --i)
                {
                    Line line=Lines[i]; Point a=Endpoints[line.EndpointIndexes[0]], b=Endpoints[line.EndpointIndexes[1]];
                    if (!SamePoint(p,a) && !SamePoint(p,b) && OnSegment(p,a,b)) SplitLine((short)i,p.X,p.Y,index);
                }
                indexes.Add(index);
            }
            for (int i = 0; i < indexes.Count; ++i)
            {
                short a=indexes[i],b=indexes[(i+1)%indexes.Count],found=-1;
                for (int j = 0; j < Lines.Count; ++j)
                {
                    Line line=Lines[j];
                    if ((line.EndpointIndexes[0]==a && line.EndpointIndexes[1]==b) ||
                        (line.EndpointIndexes[0]==b && line.EndpointIndexes[1]==a)) { found=(short)j; break; }
                }
                loop.Add(found == -1 ? NewLine(a,b) : found);
            }
            return true;
        }
    }
}
