using System;
using System.Collections.Generic;
using System.Linq;

namespace Weland
{
    public partial class Level
    {
        public bool CutPolygonsWithLine(Point a, Point b, out bool handled, out string reason)
        {
            var trial = new Level(); trial.Load(Save().Clone());
            if (!trial.BuildLineCut(a,b,out handled,out reason)) return false;
            if (handled) Load(trial.Save().Clone());
            return true;
        }

        bool BuildLineCut(Point a, Point b, out bool handled, out string reason)
        {
            handled = false; reason = null;
            if (SamePoint(a,b)) return true;
            var cuts = new List<(short Index, List<Point> First, List<Point> Second)>();
            bool startAttached = false, endAttached = false;
            int count = Polygons.Count;
            for (int index = 0; index < count; ++index)
            {
                var polygon = Polygons[index];
                var ring = new List<Point>(); var hits = new HashSet<Point>();
                for (int edge = 0; edge < polygon.VertexCount; ++edge)
                {
                    Point p = Endpoints[polygon.EndpointIndexes[edge]], q = Endpoints[polygon.EndpointIndexes[(edge+1)%polygon.VertexCount]];
                    ring.Add(p); var added = new List<Point>();
                    foreach (Point end in new[]{a,b}) if (OnSegment(end,p,q)) { hits.Add(end); added.Add(end); }
                    long denominator = ((long)b.X-a.X)*(q.Y-p.Y)-((long)b.Y-a.Y)*(q.X-p.X);
                    if (denominator != 0)
                    {
                        double t = (double)(((long)p.X-a.X)*(q.Y-p.Y)-((long)p.Y-a.Y)*(q.X-p.X))/denominator;
                        double u = (double)(((long)p.X-a.X)*(b.Y-a.Y)-((long)p.Y-a.Y)*(b.X-a.X))/denominator;
                        if (t >= 0 && t <= 1 && u >= 0 && u <= 1)
                        {
                            double x = a.X+t*(b.X-a.X), y = a.Y+t*(b.Y-a.Y);
                            if (Math.Abs(x-Math.Round(x)) > 0.000001 || Math.Abs(y-Math.Round(y)) > 0.000001)
                            { handled = true; reason = "This cut meets an edge between map coordinates. Move the endpoint slightly."; return false; }
                            var hit = new Point((short)Math.Round(x),(short)Math.Round(y)); hits.Add(hit); added.Add(hit);
                        }
                    }
                    foreach (Point point in added.Distinct().OrderBy(v=>Distance(p,v)))
                        if (!SamePoint(point,p) && !SamePoint(point,q)) ring.Add(point);
                }
                startAttached |= hits.Contains(a); endAttached |= hits.Contains(b);
                if (hits.Count < 2)
                {
                    if (ContainsBoxCenter(polygon,a.X,a.Y) && !hits.Contains(a) || ContainsBoxCenter(polygon,b.X,b.Y) && !hits.Contains(b))
                    { handled = true; reason = "A polygon cut must connect two boundary edges or vertices."; return false; }
                    continue;
                }
                if (!ring.Any(p=>ShapeCross(a,b,p)>0) || !ring.Any(p=>ShapeCross(a,b,p)<0)) continue;
                handled = true;
                if (polygon.Concave || hits.Count != 2)
                { reason = "Cut a convex polygon between two boundary points."; return false; }
                Point[] ends = hits.ToArray(); int first = ring.IndexOf(ends[0]), second = ring.IndexOf(ends[1]);
                List<Point> Path(int from, int to) {
                    var part = new List<Point>(); int i = from;
                    while (true) { part.Add(ring[i]); if (i==to) break; i=(i+1)%ring.Count; }
                    // Keep boundary vertices in the geometry; the shape builder reinserts collinear points.
                    bool changed = true;
                    while (changed && part.Count > 3) {
                        changed = false;
                        for (int j=0;j<part.Count;++j) if (ShapeCross(part[(j+part.Count-1)%part.Count],part[j],part[(j+1)%part.Count])==0) {
                            part.RemoveAt(j); changed=true; break;
                        }
                    }
                    return part;
                }
                cuts.Add(((short)index,Path(first,second),Path(second,first)));
            }
            if (cuts.Count > 0 && (!startAttached || !endAttached)) {
                reason = "A polygon cut must start and end on existing boundary edges or vertices."; return false;
            }
            foreach (var cut in cuts)
            {
                if (!RebuildSplitPolygon(cut.Index, original => {
                    foreach (var piece in new[]{cut.First,cut.Second}) {
                        if (!CreatePlainShape(piece,out string error)) return error.Replace("Circle","Cut").Replace("circle","cut");
                        CopyBoxSettings(original,Polygons[Polygons.Count-1]);
                    }
                    return null;
                },out reason)) return false;
            }
            return true;
        }
    }
}
