using System;
using System.Collections.Generic;

namespace Weland
{
    public partial class Level
    {
        static bool SamePoint(Point a, Point b) => a.X == b.X && a.Y == b.Y;

        static bool OnSegment(Point p, Point a, Point b)
        {
            long cross = ((long)p.X - a.X) * (b.Y - a.Y) -
                         ((long)p.Y - a.Y) * (b.X - a.X);
            return cross == 0 && p.X >= Math.Min(a.X, b.X) && p.X <= Math.Max(a.X, b.X) &&
                   p.Y >= Math.Min(a.Y, b.Y) && p.Y <= Math.Max(a.Y, b.Y);
        }

        // Clip the segment to the rectangle, then test the open interior.
        // A segment along an edge or touching only a corner is allowed.
        static bool EntersBox(Point a, Point b, int left, int top, int right, int bottom)
        {
            double low = 0, high = 1;
            int[] starts = { a.X, a.Y };
            int[] deltas = { b.X - a.X, b.Y - a.Y };
            int[] mins = { left, top }, maxs = { right, bottom };
            for (int axis = 0; axis < 2; ++axis)
            {
                if (deltas[axis] == 0)
                {
                    if (starts[axis] <= mins[axis] || starts[axis] >= maxs[axis]) return false;
                }
                else
                {
                    double t0 = (double)(mins[axis] - starts[axis]) / deltas[axis];
                    double t1 = (double)(maxs[axis] - starts[axis]) / deltas[axis];
                    low = Math.Max(low, Math.Min(t0, t1));
                    high = Math.Min(high, Math.Max(t0, t1));
                    if (low >= high) return false;
                }
            }
            return low < high;
        }

        bool ContainsBoxCenter(Polygon polygon, double x, double y)
        {
            bool inside = false;
            for (int i = 0, j = polygon.VertexCount - 1; i < polygon.VertexCount; j = i++)
            {
                Point a = Endpoints[polygon.EndpointIndexes[i]];
                Point b = Endpoints[polygon.EndpointIndexes[j]];
                if ((a.Y > y) != (b.Y > y) &&
                    x < (double)(b.X - a.X) * (y - a.Y) / (b.Y - a.Y) + a.X)
                    inside = !inside;
            }
            return inside;
        }

        List<Point> BoxRing(Point start, Point end)
        {
            short left = Math.Min(start.X, end.X), right = Math.Max(start.X, end.X);
            short top = Math.Min(start.Y, end.Y), bottom = Math.Max(start.Y, end.Y);
            var corners = new[] { new Point(left, top), new Point(right, top),
                                  new Point(right, bottom), new Point(left, bottom) };
            var ring = new List<Point>();
            for (int edge = 0; edge < 4; ++edge)
            {
                Point a = corners[edge], b = corners[(edge + 1) % 4];
                var points = new List<Point> { a };
                // Include all geometry, even when hidden by a view filter.
                foreach (Point p in Endpoints)
                    if (OnSegment(p, a, b) && !SamePoint(p, b) && !points.Contains(p)) points.Add(p);
                points.Sort((p, q) =>
                    (Math.Abs((int)p.X - a.X) + Math.Abs((int)p.Y - a.Y)).CompareTo(
                     Math.Abs((int)q.X - a.X) + Math.Abs((int)q.Y - a.Y)));
                ring.AddRange(points);
            }
            return ring;
        }

        bool CanCreatePlainBox(Point start, Point end, out string reason)
        {
            reason = null;
            int left = Math.Min(start.X, end.X), right = Math.Max(start.X, end.X);
            int top = Math.Min(start.Y, end.Y), bottom = Math.Max(start.Y, end.Y);
            if (left == right || top == bottom)
            {
                reason = "Drag a box with nonzero width and height.";
                return false;
            }
            foreach (Point p in Endpoints)
                if (p.X > left && p.X < right && p.Y > top && p.Y < bottom)
                {
                    reason = "Box contains existing vertices.";
                    return false;
                }
            foreach (Line line in Lines)
                if (EntersBox(Endpoints[line.EndpointIndexes[0]], Endpoints[line.EndpointIndexes[1]], left, top, right, bottom))
                {
                    reason = "Box crosses existing geometry.";
                    return false;
                }
            foreach (Polygon polygon in Polygons)
                if (ContainsBoxCenter(polygon, (left + right) / 2.0, (top + bottom) / 2.0))
                {
                    reason = "Box overlaps an existing polygon.";
                    return false;
                }

            List<Point> ring = BoxRing(start, end);
            if (ring.Count > Polygon.MaxVertexCount)
            {
                reason = "Shared edges would exceed the polygon's eight-vertex limit.";
                return false;
            }
            var extraVertices = new Dictionary<short, int>();
            int splits = 0;
            foreach (Line line in Lines)
            {
                Point a = Endpoints[line.EndpointIndexes[0]], b = Endpoints[line.EndpointIndexes[1]];
                foreach (Point p in ring)
                    if (!SamePoint(p, a) && !SamePoint(p, b) && OnSegment(p, a, b))
                    {
                        ++splits;
                        foreach (short owner in new[] { line.ClockwisePolygonOwner, line.CounterclockwisePolygonOwner })
                            if (owner != -1)
                            {
                                if (!extraVertices.ContainsKey(owner)) extraVertices[owner] = 0;
                                ++extraVertices[owner];
                            }
                    }
            }
            foreach (var entry in extraVertices)
                if (Polygons[entry.Key].VertexCount + entry.Value > Polygon.MaxVertexCount)
                {
                    reason = "Splitting a shared edge would exceed an adjacent polygon's eight-vertex limit.";
                    return false;
                }
            // SplitLine briefly adds two lines before removing the original.
            if (Endpoints.Count + ring.Count > short.MaxValue ||
                Lines.Count + splits + ring.Count + 2 > short.MaxValue ||
                Sides.Count + splits * 4 > short.MaxValue || Polygons.Count >= short.MaxValue)
            {
                reason = "Map geometry limit reached.";
                return false;
            }
            for (int edge = 0; edge < ring.Count; ++edge)
            {
                int matches = 0;
                Point a = ring[edge], b = ring[(edge + 1) % ring.Count];
                foreach (Line line in Lines)
                    if (OnSegment(a, Endpoints[line.EndpointIndexes[0]], Endpoints[line.EndpointIndexes[1]]) &&
                        OnSegment(b, Endpoints[line.EndpointIndexes[0]], Endpoints[line.EndpointIndexes[1]]))
                    {
                        ++matches;
                        if (line.ClockwisePolygonOwner != -1 && line.CounterclockwisePolygonOwner != -1)
                        {
                            reason = "A shared edge already has polygons on both sides.";
                            return false;
                        }
                    }
                if (matches > 1)
                {
                    reason = "Existing boundary lines overlap; repair them before drawing here.";
                    return false;
                }
            }
            return true;
        }

        bool CreatePlainBox(Point start, Point end, out string reason)
        {
            if (!CanCreatePlainBox(start, end, out reason)) return false;
            List<Point> ring = BoxRing(start, end);
            var indexes = new List<short>();
            foreach (Point p in ring)
            {
                short index = -1;
                for (int i = 0; i < Endpoints.Count; ++i)
                    if (SamePoint(p, Endpoints[i])) { index = (short)i; break; }
                if (index == -1) index = NewPoint(p.X, p.Y);
                // A corner may lie partway along a longer shared wall. Reuse
                // this endpoint when splitting, rather than adding a duplicate.
                for (int i = Lines.Count - 1; i >= 0; --i)
                {
                    Line line = Lines[i];
                    Point a = Endpoints[line.EndpointIndexes[0]], b = Endpoints[line.EndpointIndexes[1]];
                    if (!SamePoint(p, a) && !SamePoint(p, b) && OnSegment(p, a, b))
                        SplitLine((short)i, p.X, p.Y, index);
                }
                indexes.Add(index);
            }
            var loop = new List<short>();
            for (int i = 0; i < indexes.Count; ++i)
            {
                short a = indexes[i], b = indexes[(i + 1) % indexes.Count], found = -1;
                for (int j = 0; j < Lines.Count; ++j)
                {
                    Line line = Lines[j];
                    if ((line.EndpointIndexes[0] == a && line.EndpointIndexes[1] == b) ||
                        (line.EndpointIndexes[0] == b && line.EndpointIndexes[1] == a))
                    { found = (short)j; break; }
                }
                loop.Add(found == -1 ? NewLine(a, b) : found);
            }
            bool created = CreatePolygonFromLoop(loop, true);
            if (created) LastCreatedBoxPolygonIndex = (short)(Polygons.Count - 1);
            return created;
        }
    }
}
