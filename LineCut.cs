using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace Weland
{
    public partial class Level
    {
        public bool CutPolygonsWithLine(Point a, Point b, out bool handled, out string reason, bool splitAll = false)
        {
            var trial = new Level(); trial.Load(Save().Clone());
            if (!trial.BuildLineCut(a,b,out handled,out reason,splitAll)) return false;
            if (handled) Load(trial.Save().Clone());
            return true;
        }

        bool BuildLineCut(Point a, Point b, out bool handled, out string reason, bool splitAll)
        {
            handled = false; reason = null;
            if (SamePoint(a,b)) return true;
            var cutPath = splitAll ? GatherSplitLinePath(a,b) : null;
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
                            var hit = SnapCutIntersection(a,b,p,q); hits.Add(hit); added.Add(hit);
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
                // Rounded endpoint attachments can make the unsplit ring slightly
                // concave. CreatePlainShape validates convexity of both final pieces.
                if (hits.Count != 2)
                { reason = "Cut a convex polygon between two boundary points."; return false; }
                Point[] ends = hits.ToArray();
                // If precision snapping lands on a boundary chord, follow that
                // boundary instead of trying to create a collapsed sliver.
                if (!ring.Any(p=>ShapeCross(ends[0],ends[1],p)>0) || !ring.Any(p=>ShapeCross(ends[0],ends[1],p)<0)) continue;
                int first = ring.IndexOf(ends[0]), second = ring.IndexOf(ends[1]);
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
            if (cuts.Count > 0 && !splitAll && (!startAttached || !endAttached)) {
                reason = "A polygon cut must start and end on existing boundary edges or vertices."; return false;
            }
            if (!PrepareRoundedCutEdges(a,b,new HashSet<short>(cuts.Select(c=>c.Index)),out reason)) return false;
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
            if (splitAll)
            {
                handled = true;
                if (!BuildSplitLinePath(cutPath,out reason)) return false;
            }
            return true;
        }

        // Round the actual intersection, not the sparse integer lattice of
        // the original edge. A shared edge is rebuilt through this same point.
        static Point SnapCutIntersection(Point a, Point b, Point p, Point q)
        {
            if (p.X > q.X || (p.X == q.X && p.Y > q.Y)) { var swap=p; p=q; q=swap; }
            long dx=q.X-p.X,dy=q.Y-p.Y;
            long denominator=((long)b.X-a.X)*dy-((long)b.Y-a.Y)*dx;
            long numerator=((long)p.X-a.X)*(b.Y-a.Y)-((long)p.Y-a.Y)*(b.X-a.X);
            if (denominator<0) { denominator=-denominator; numerator=-numerator; }
            numerator=Math.Max(0,Math.Min(denominator,numerator));
            // Translate from short.MinValue so exact rational half-up rounding
            // is consistent for negative coordinates and reversed drags too.
            short Round(long origin,long delta) => (short)(((origin-short.MinValue)*denominator+delta*numerator+denominator/2)/denominator+short.MinValue);
            return new Point(Round(p.X,dx),Round(p.Y,dy));
        }

        bool PrepareRoundedCutEdges(Point a,Point b,HashSet<short> cutPolygons,out string reason)
        {
            reason=null;
            var replacements=new List<(Line Line,Point Hit)>();
            foreach(var line in Lines)
            {
                Point p=Endpoints[line.EndpointIndexes[0]],q=Endpoints[line.EndpointIndexes[1]];
                long denominator=((long)b.X-a.X)*(q.Y-p.Y)-((long)b.Y-a.Y)*(q.X-p.X);
                if(denominator==0)continue;
                double t=(double)(((long)p.X-a.X)*(q.Y-p.Y)-((long)p.Y-a.Y)*(q.X-p.X))/denominator;
                double u=(double)(((long)p.X-a.X)*(b.Y-a.Y)-((long)p.Y-a.Y)*(b.X-a.X))/denominator;
                if(t<0 || t>1 || u<0 || u>1)continue;
                Point hit=SnapCutIntersection(a,b,p,q);
                if(OnSegment(hit,p,q))continue;
                if((line.ClockwisePolygonOwner!=-1 && !cutPolygons.Contains(line.ClockwisePolygonOwner)) ||
                   (line.CounterclockwisePolygonOwner!=-1 && !cutPolygons.Contains(line.CounterclockwisePolygonOwner)))
                { reason="This rounded crossing would alter an uncut polygon. Extend the cut through its boundaries.";return false; }
                replacements.Add((line,hit));
            }
            // Polygon rings are rebuilt below, so split the perimeter here
            // without temporarily inserting a ninth vertex into fixed arrays.
            foreach(var replacement in replacements)
            {
                var line=replacement.Line;Point hit=replacement.Hit;
                int found=Endpoints.IndexOf(hit);
                if((found==-1 && Endpoints.Count>=short.MaxValue) || Lines.Count>=short.MaxValue || Sides.Count>short.MaxValue-2)
                { reason="This cut exceeds map geometry limits.";return false; }
                short vertex=found==-1 ? NewPoint(hit.X,hit.Y) : (short)found;
                short start=line.EndpointIndexes[0],end=line.EndpointIndexes[1];
                short index=NewLine(vertex,end);var tail=Lines[index];tail.Flags=line.Flags;
                tail.ClockwisePolygonOwner=line.ClockwisePolygonOwner;tail.CounterclockwisePolygonOwner=line.CounterclockwisePolygonOwner;
                short CloneSide(short old)
                {
                    if(old==-1)return -1;
                    using(var stream=new MemoryStream())
                    {
                        Sides[old].Save(new BinaryWriterBE(stream));stream.Position=0;
                        var side=new Side();side.Load(new BinaryReaderBE(stream));side.LineIndex=index;
                        short offset=(short)Distance(Endpoints[start],hit);
                        if(!side.Primary.Texture.IsEmpty())side.Primary.X+=offset;
                        if(!side.Secondary.Texture.IsEmpty())side.Secondary.X+=offset;
                        if(!side.Transparent.Texture.IsEmpty())side.Transparent.X+=offset;
                        short result=(short)Sides.Count;Sides.Add(side);return result;
                    }
                }
                tail.ClockwisePolygonSideIndex=CloneSide(line.ClockwisePolygonSideIndex);
                tail.CounterclockwisePolygonSideIndex=CloneSide(line.CounterclockwisePolygonSideIndex);
                EndpointLines[end].Remove(line);line.EndpointIndexes[1]=vertex;EndpointLines[vertex].Add(line);
            }
            return true;
        }

        List<Point> GatherSplitLinePath(Point a, Point b)
        {
            var path = new HashSet<Point> { a, b };
            foreach (Point point in Endpoints) if (OnSegment(point,a,b)) path.Add(point);
            foreach (Line line in Lines)
            {
                Point p = Endpoints[line.EndpointIndexes[0]], q = Endpoints[line.EndpointIndexes[1]];
                long denominator = ((long)b.X-a.X)*(q.Y-p.Y)-((long)b.Y-a.Y)*(q.X-p.X);
                if (denominator == 0) continue;
                double t = (double)(((long)p.X-a.X)*(q.Y-p.Y)-((long)p.Y-a.Y)*(q.X-p.X))/denominator;
                double u = (double)(((long)p.X-a.X)*(b.Y-a.Y)-((long)p.Y-a.Y)*(b.X-a.X))/denominator;
                if (t >= 0 && t <= 1 && u >= 0 && u <= 1) path.Add(SnapCutIntersection(a,b,p,q));
            }
            // Capture the path before splitting polygons: recomputing intersections
            // afterwards would cross the newly adjusted chords a second time.
            return path.OrderBy(p => (long)(p.X-a.X)*(b.X-a.X)+(long)(p.Y-a.Y)*(b.Y-a.Y))
                .ThenBy(p=>p.X).ThenBy(p=>p.Y).ToList();
        }

        bool BuildSplitLinePath(List<Point> path, out string reason)
        {
            reason = null;
            // A snapped segment can pass an existing vertex which was slightly
            // off the drawn line. Include it so the result has no T-junctions.
            var complete = new List<Point>();
            for (int i=1;i<path.Count;i++)
            {
                Point a=path[i-1],b=path[i];
                foreach (Point point in Endpoints.Where(p=>OnSegment(p,a,b)).Concat(new[]{a,b}).Distinct()
                    .OrderBy(p=>(long)(p.X-a.X)*(b.X-a.X)+(long)(p.Y-a.Y)*(b.Y-a.Y)))
                    if (complete.Count==0 || !SamePoint(complete[complete.Count-1],point)) complete.Add(point);
            }
            var vertices = new List<short>();
            foreach (Point point in complete)
            {
                int existing = Endpoints.IndexOf(point);
                if (existing == -1 && Endpoints.Count >= short.MaxValue)
                { reason = "This path exceeds map vertex limits."; return false; }
                short vertex = existing == -1 ? NewPoint(point.X,point.Y) : (short)existing;
                vertices.Add(vertex);
                for (int index = Lines.Count-1; index >= 0; --index)
                {
                    var line = Lines[index];
                    if (line.EndpointIndexes.Contains(vertex) || !OnSegment(point,Endpoints[line.EndpointIndexes[0]],Endpoints[line.EndpointIndexes[1]])) continue;
                    if ((line.ClockwisePolygonOwner != -1 && Polygons[line.ClockwisePolygonOwner].VertexCount >= Polygon.MaxVertexCount) ||
                        (line.CounterclockwisePolygonOwner != -1 && Polygons[line.CounterclockwisePolygonOwner].VertexCount >= Polygon.MaxVertexCount))
                    { reason = "This path would exceed a polygon's eight-vertex limit."; return false; }
                    if (Lines.Count > short.MaxValue-2 || Sides.Count > short.MaxValue-2)
                    { reason = "This path exceeds map line or wall limits."; return false; }
                    SplitLine((short)index,point.X,point.Y,vertex);
                }
            }
            for (int i=1; i<vertices.Count; ++i)
            {
                short p=vertices[i-1],q=vertices[i];
                if (Lines.Any(line => (line.EndpointIndexes[0]==p && line.EndpointIndexes[1]==q) || (line.EndpointIndexes[0]==q && line.EndpointIndexes[1]==p))) continue;
                if (Lines.Count >= short.MaxValue) { reason = "This path exceeds map line limits."; return false; }
                Point a=Endpoints[p],b=Endpoints[q];
                foreach (var line in Lines)
                {
                    Point c=Endpoints[line.EndpointIndexes[0]],d=Endpoints[line.EndpointIndexes[1]];
                    long abC=ShapeCross(a,b,c),abD=ShapeCross(a,b,d),cdA=ShapeCross(c,d,a),cdB=ShapeCross(c,d,b);
                    if ((Math.Sign(abC)*Math.Sign(abD)<0 && Math.Sign(cdA)*Math.Sign(cdB)<0) ||
                        (cdA==0 && OnSegment(a,c,d) && !SamePoint(a,c) && !SamePoint(a,d)) ||
                        (cdB==0 && OnSegment(b,c,d) && !SamePoint(b,c) && !SamePoint(b,d)))
                    { reason="Snapping cannot connect this path without crossing existing geometry. Try a nearby cut."; return false; }
                }
                if (Polygons.Any(polygon => EntersShape(a,b,polygon.EndpointIndexes.Take(polygon.VertexCount).Select(v=>Endpoints[v]).ToList())))
                { reason="Snapping would leave an unsplit polygon on the path. Try a nearby cut."; return false; }
                NewLine(p,q);
            }
            return true;
        }
    }
}
