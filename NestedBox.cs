using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Weland
{
    public partial class Level
    {
        public short LastCreatedBoxPolygonIndex { get; private set; } = -1;

        public bool CanOptimizePair(short a,short b,bool ignoreFloorLight=false,bool requireConvexUnion=true)
        {
            if(a<0 || b<0 || a==b)return false;
            var first=Polygons[a];var second=Polygons[b];
            if(first.Type!=PolygonType.Normal || second.Type!=PolygonType.Normal || first.Permutation!=second.Permutation ||
               !string.IsNullOrWhiteSpace(first.LuaTriggerCode) || !string.IsNullOrWhiteSpace(second.LuaTriggerCode))return false;
            byte[] Settings(Polygon source){var copy=new Polygon();CopyBoxSettings(source,copy);if(ignoreFloorLight){copy.FloorLight=0;copy.CeilingLight=0;}using(var stream=new MemoryStream()){copy.Save(new BinaryWriterBE(stream));return stream.ToArray();}}
            if(!Settings(first).SequenceEqual(Settings(second)))return false;
            foreach(var line in Lines.Where(l=>(l.ClockwisePolygonOwner==a && l.CounterclockwisePolygonOwner==b) || (l.ClockwisePolygonOwner==b && l.CounterclockwisePolygonOwner==a))) {
                if(!requireConvexUnion && (line.Flags & (LineFlags.Solid | LineFlags.Landscape | LineFlags.Decorative))!=0)return false;
                foreach(short sideIndex in new[]{line.ClockwisePolygonSideIndex,line.CounterclockwisePolygonSideIndex})
                    if(sideIndex>=0 && (Sides[sideIndex].IsControlPanel || !Sides[sideIndex].Transparent.Texture.IsEmpty()))return false;
            }
            if(!requireConvexUnion)return true;
            var rings=new List<List<Point>>{first.EndpointIndexes.Take(first.VertexCount).Select(v=>Endpoints[v]).ToList(),second.EndpointIndexes.Take(second.VertexCount).Select(v=>Endpoints[v]).ToList()};
            MergeInsetPieces(rings);return rings.Count==1;
        }
        public bool Pathfinder(short a,short b,bool subtract,out string reason)
        {
            reason=null;
            if(a==b || a<0 || b<0 || a>=Polygons.Count || b>=Polygons.Count){reason="Select exactly two polygons.";return false;}
            var settings=Polygons[a];
            if(settings.Type!=PolygonType.Normal || Polygons[b].Type!=PolygonType.Normal){reason="Pathfinder currently requires two normal polygons.";return false;}
            if(!string.IsNullOrWhiteSpace(settings.LuaTriggerCode) || !string.IsNullOrWhiteSpace(Polygons[b].LuaTriggerCode)) {reason="Remove polygon Lua triggers before Pathfinder so actions are not lost or duplicated.";return false;}
            if(Polygons.Any(p=>(p.Type==PolygonType.Teleporter || p.Type==PolygonType.PlatformOnTrigger || p.Type==PolygonType.PlatformOffTrigger) && (p.Permutation==a || p.Permutation==b)) ||
               Sides.Any(s=>s.IsControlPanel && s.IsPlatformSwitch() && (s.ControlPanelPermutation==a || s.ControlPanelPermutation==b))) {reason="A switch or teleporter references one of these polygons. Update that reference before Pathfinder.";return false;}
            var left=new List<Point>();var right=new List<Point>();
            for(int i=0;i<settings.VertexCount;i++)left.Add(Endpoints[settings.EndpointIndexes[i]]);
            for(int i=0;i<Polygons[b].VertexCount;i++)right.Add(Endpoints[Polygons[b].EndpointIndexes[i]]);
            long Area(List<Point> ring){long sum=0;for(int i=0;i<ring.Count;i++){var p=ring[i];var q=ring[(i+1)%ring.Count];sum+=(long)p.X*q.Y-(long)p.Y*q.X;}return sum;}
            if(Area(left)<0)left.Reverse();if(Area(right)<0)right.Reverse();
            List<Point> Clip(List<Point> ring,Point u,Point v,bool inside) {
                var result=new List<Point>();
                for(int i=0;i<ring.Count;i++){
                    Point p=ring[i],q=ring[(i+1)%ring.Count];long cp=ShapeCross(u,v,p),cq=ShapeCross(u,v,q);
                    bool pin=inside?cp>=0:cp<=0,qin=inside?cq>=0:cq<=0;
                    if(pin && (result.Count==0 || !result[result.Count-1].Equals(p)))result.Add(p);
                    if(pin!=qin){double t=(double)cp/(cp-cq);var cut=new Point((short)Math.Round(p.X+t*(q.X-p.X)),(short)Math.Round(p.Y+t*(q.Y-p.Y)));if(result.Count==0 || !result[result.Count-1].Equals(cut))result.Add(cut);}
                }
                if(result.Count>1 && result[0].Equals(result[result.Count-1]))result.RemoveAt(result.Count-1);
                return result;
            }
            var pieces=new List<List<Point>>();var rest=left;
            for(int i=0;i<right.Count && rest.Count>=3;i++){
                var outside=Clip(rest,right[i],right[(i+1)%right.Count],false);
                if(outside.Count>=3 && Area(outside)>0)pieces.Add(outside);
                rest=Clip(rest,right[i],right[(i+1)%right.Count],true);
            }
            if(subtract && (rest.Count<3 || Area(rest)==0)){reason="These polygons have no overlapping area to subtract.";return false;}
            if(!subtract)pieces.Add(right);
            if(pieces.Count==0){reason="The cutter removes the entire target polygon.";return false;}
            // Quantized intersections must be inserted on BOTH sides of a shared
            // edge, including the untouched cutter in a union. Otherwise its
            // straight edge cuts across a rounded intersection in another piece.
            var junctions=pieces.SelectMany(p=>p).Distinct().ToList();
            var conforming=new List<List<Point>>();
            foreach(var piece in pieces) {
                var ring=new List<Point>();
                for(int edge=0;edge<piece.Count;edge++) {
                    var p=piece[edge];var q=piece[(edge+1)%piece.Count];
                    double dx=q.X-p.X,dy=q.Y-p.Y,length2=dx*dx+dy*dy;
                    ring.Add(p);
                    if(length2==0)continue;
                    var between=junctions.Where(v=>!v.Equals(p) && !v.Equals(q))
                        .Select(v=>new {v,t=((v.X-p.X)*dx+(v.Y-p.Y)*dy)/length2,cross=dx*(v.Y-p.Y)-dy*(v.X-p.X)})
                        .Where(v=>v.t>0 && v.t<1 && v.cross*v.cross<=length2*2.25).OrderBy(v=>v.t);
                    foreach(var v in between)if(!ring[ring.Count-1].Equals(v.v))ring.Add(v.v);
                }
                if(!TriangulateInsetRing(ring,conforming)){reason="The intersection is too narrow at map-coordinate precision.";return false;}
            }
            pieces=conforming;
            MergeInsetPieces(pieces);
            var final=new List<List<Point>>();
            foreach(var piece in pieces) {if(piece.Count<=Polygon.MaxVertexCount)final.Add(piece);else for(int i=1;i<piece.Count-1;i++)final.Add(new List<Point>{piece[0],piece[i],piece[i+1]});}
            var walls=new List<BoxWall>();var touched=new HashSet<Line>();
            foreach(var line in Lines){bool cw=line.ClockwisePolygonOwner==a || line.ClockwisePolygonOwner==b;bool ccw=line.CounterclockwisePolygonOwner==a || line.CounterclockwisePolygonOwner==b;if(!cw && !ccw)continue;touched.Add(line);
                foreach(bool clockwise in new[]{true,false}){if(clockwise?!cw:!ccw)continue;short side=clockwise?line.ClockwisePolygonSideIndex:line.CounterclockwisePolygonSideIndex;
                    var wall=new BoxWall{Start=Endpoints[line.EndpointIndexes[0]],End=Endpoints[line.EndpointIndexes[1]],Clockwise=clockwise,Flags=line.Flags};
                    if(side>=0){using(var stream=new MemoryStream()){Sides[side].Save(new BinaryWriterBE(stream));wall.Side=stream.ToArray();}}walls.Add(wall);}}
            var objects=Objects.FindAll(o=>o.PolygonIndex==a || o.PolygonIndex==b);
            var notes=Annotations.FindAll(o=>o.PolygonIndex==a || o.PolygonIndex==b);
            DeletePolygon(Math.Max(a,b));DeletePolygon(Math.Min(a,b));
            for(int i=Lines.Count-1;i>=0;i--)if(touched.Contains(Lines[i]) && Lines[i].ClockwisePolygonOwner<0 && Lines[i].CounterclockwisePolygonOwner<0)DeleteLineIndex((short)i);
            var oldPoints=new HashSet<Point>(left.Concat(right));
            for(int i=Endpoints.Count-1;i>=0;i--)if(oldPoints.Contains(Endpoints[i]) && !Lines.Any(l=>l.EndpointIndexes.Contains((short)i)))DeletePoint((short)i);
            int first=Polygons.Count;
            foreach(var piece in final){if(!CreatePlainShape(piece,out reason))return false;CopyBoxSettings(settings,Polygons[Polygons.Count-1]);}
            foreach(var wall in walls)if(wall.Side!=null)for(int i=0;i<Lines.Count;i++){
                var line=Lines[i];short owner=wall.Clockwise?line.ClockwisePolygonOwner:line.CounterclockwisePolygonOwner;
                if(owner<first || !OnSegment(Endpoints[line.EndpointIndexes[0]],wall.Start,wall.End) || !OnSegment(Endpoints[line.EndpointIndexes[1]],wall.Start,wall.End))continue;
                if((wall.Clockwise?line.ClockwisePolygonSideIndex:line.CounterclockwisePolygonSideIndex)>=0)continue;
                var side=new Side();side.Load(new BinaryReaderBE(new MemoryStream(wall.Side)));side.LineIndex=(short)i;side.PolygonIndex=owner;
                short offset=(short)Distance(wall.Start,Endpoints[line.EndpointIndexes[0]]);side.Primary.X+=offset;side.Secondary.X+=offset;side.Transparent.X+=offset;
                if(wall.Clockwise)line.ClockwisePolygonSideIndex=(short)Sides.Count;else line.CounterclockwisePolygonSideIndex=(short)Sides.Count;Sides.Add(side);
            }
            foreach(var obj in objects){short owner=GetEnclosingPolygon(new Point(obj.X,obj.Y));if(owner>=first){obj.PolygonIndex=owner;Objects.Add(obj);}}
            foreach(var note in notes){short owner=GetEnclosingPolygon(new Point(note.X,note.Y));if(owner>=first){note.PolygonIndex=owner;Annotations.Add(note);}}
            LastCreatedBoxPolygonIndex=(short)first;
            // Refresh derived owner/side links through the existing topology validator.
            var original=new Level();original.Load(Save().Clone());
            return RepairReflection(original,new HashSet<short>(System.Linq.Enumerable.Range(0,Endpoints.Count).Select(i=>(short)i)),out reason);
        }
        bool RectangleBounds(Polygon polygon, out Point min, out Point max)
        {
            min = new Point(short.MaxValue, short.MaxValue);
            max = new Point(short.MinValue, short.MinValue);
            if (polygon.VertexCount < 4 || polygon.Concave) return false;
            for (int i = 0; i < polygon.VertexCount; ++i)
            {
                Point p = Endpoints[polygon.EndpointIndexes[i]];
                min.X = Math.Min(min.X, p.X); min.Y = Math.Min(min.Y, p.Y);
                max.X = Math.Max(max.X, p.X); max.Y = Math.Max(max.Y, p.Y);
            }
            if (min.X == max.X || min.Y == max.Y) return false;
            var corners = new HashSet<Point>();
            for (int i = 0; i < polygon.VertexCount; ++i)
            {
                Point p = Endpoints[polygon.EndpointIndexes[i]];
                Point q = Endpoints[polygon.EndpointIndexes[(i + 1) % polygon.VertexCount]];
                if (p.X != q.X && p.Y != q.Y) return false;
                if (p.X != min.X && p.X != max.X && p.Y != min.Y && p.Y != max.Y) return false;
                if ((p.X == min.X || p.X == max.X) && (p.Y == min.Y || p.Y == max.Y)) corners.Add(p);
            }
            return corners.Count == 4;
        }

        short BoxContainer(Point start, Point end)
        {
            int left = Math.Min(start.X, end.X), right = Math.Max(start.X, end.X);
            int top = Math.Min(start.Y, end.Y), bottom = Math.Max(start.Y, end.Y);
            if (left == right || top == bottom) return -1;
            Point[] corners = { new Point((short)left, (short)top), new Point((short)right, (short)top),
                                new Point((short)right, (short)bottom), new Point((short)left, (short)bottom) };
            for (int i = 0; i < Polygons.Count; ++i)
            {
                Polygon polygon = Polygons[i];
                if (polygon.VertexCount < 3) continue;
                if (RectangleBounds(polygon, out Point min, out Point max) &&
                    left == min.X && right == max.X && top == min.Y && bottom == max.Y) continue;
                bool contains = true;
                foreach (Point p in corners)
                {
                    bool inside = ContainsBoxCenter(polygon, p.X, p.Y);
                    for (int edge = 0; !inside && edge < polygon.VertexCount; ++edge)
                        inside = OnSegment(p, Endpoints[polygon.EndpointIndexes[edge]], Endpoints[polygon.EndpointIndexes[(edge + 1) % polygon.VertexCount]]);
                    if (!inside) { contains = false; break; }
                }
                for (int edge = 0; contains && edge < polygon.VertexCount; ++edge)
                {
                    Point a = Endpoints[polygon.EndpointIndexes[edge]], b = Endpoints[polygon.EndpointIndexes[(edge + 1) % polygon.VertexCount]];
                    if (EntersBox(a, b, left, top, right, bottom)) contains = false;
                }
                if (contains) return (short)i;
            }
            return -1;
        }

        short ShapeContainer(List<Point> corners)
        {
            if (corners.Count < 3) return -1;
            for (int i = 0; i < Polygons.Count; ++i)
            {
                Polygon polygon = Polygons[i];
                if (polygon.VertexCount < 3) continue;
                bool contains = true, equalBoundary = true;
                foreach (Point p in corners)
                {
                    bool boundary = false;
                    for (int e = 0; e < polygon.VertexCount; ++e)
                        if (OnSegment(p, Endpoints[polygon.EndpointIndexes[e]], Endpoints[polygon.EndpointIndexes[(e + 1) % polygon.VertexCount]])) boundary = true;
                    if (!boundary) equalBoundary = false;
                    if (!boundary && !ContainsBoxCenter(polygon, p.X, p.Y)) { contains = false; break; }
                }
                for (int e = 0; contains && e < polygon.VertexCount; ++e)
                {
                    Point a = Endpoints[polygon.EndpointIndexes[e]], b = Endpoints[polygon.EndpointIndexes[(e + 1) % polygon.VertexCount]];
                    if (EntersShape(a, b, corners)) contains = false;
                    bool boundary = false;
                    for (int c = 0; c < corners.Count; ++c)
                        if (OnSegment(a, corners[c], corners[(c + 1) % corners.Count])) boundary = true;
                    if (!boundary) equalBoundary = false;
                }
                if (contains && !equalBoundary) return (short)i;
            }
            return -1;
        }

        public bool CanCreateBox(Point start, Point end, out string reason)
        {
            if (CanCreatePlainBox(start, end, out reason)) return true;
            short container = BoxContainer(start, end);
            if (container == -1) return false;
            if (!Filter(Polygons[container]))
            {
                reason = "Box overlaps geometry hidden by the current view filter.";
                return false;
            }
            if (Polygons[container].Type != PolygonType.Normal)
            {
                reason = "Inset boxes currently require a normal polygon.";
                return false;
            }
            // Run every split and geometry-limit check on a separate level.
            // Failed plans leave the actual geometry and editor undo untouched.
            var trial = new Level();
            trial.Load(Save().Clone());
            return trial.SplitBox(container, start, end, out reason);
        }

        public bool CreateBox(Point start, Point end, out string reason)
        {
            if (CanCreatePlainBox(start, end, out reason)) return CreatePlainBox(start, end, out reason);
            short container = BoxContainer(start, end);
            if (container == -1 || !CanCreateBox(start, end, out reason)) return false;
            return SplitBox(container, start, end, out reason);
        }

        // Triangulate a polygon with a convex inset hole. Bridge only existing
        // vertices, so no fractional intersections or neighbor wall splits are
        // needed. Ear clipping leaves convex pieces with exact integer geometry.
        bool InsetPieces(Polygon polygon, List<Point> inset,
                            out List<List<Point>> triangles, out string reason)
        {
            triangles = new List<List<Point>>();
            reason = "Could not partition this outline into valid polygons.";
            var outer = new List<Point>();
            for (int i = 0; i < polygon.VertexCount; ++i) outer.Add(Endpoints[polygon.EndpointIndexes[i]]);
            long Area(List<Point> ring)
            {
                long area = 0;
                for (int i = 0; i < ring.Count; ++i)
                {
                    Point a = ring[i], b = ring[(i + 1) % ring.Count];
                    area += (long)a.X * b.Y - (long)a.Y * b.X;
                }
                return area;
            }
            if (Area(outer) < 0) outer.Reverse();
            var inner = new List<Point>(inset);
            if (Area(inner) > 0) inner.Reverse();
            bool Blocks(Point a, Point b, Point c, Point d)
            {
                long ac = ShapeCross(a, b, c), ad = ShapeCross(a, b, d);
                long ca = ShapeCross(c, d, a), cb = ShapeCross(c, d, b);
                if (((ac < 0 && ad > 0) || (ac > 0 && ad < 0)) &&
                    ((ca < 0 && cb > 0) || (ca > 0 && cb < 0))) return true;
                if (OnSegment(c, a, b) && !SamePoint(c, a) && !SamePoint(c, b)) return true;
                if (OnSegment(d, a, b) && !SamePoint(d, a) && !SamePoint(d, b)) return true;
                return false;
            }
            bool touching = false;
            foreach (Point p in inner)
                for (int e = 0; e < outer.Count; ++e)
                    if (OnSegment(p, outer[e], outer[(e + 1) % outer.Count])) touching = true;
            foreach (Point p in outer)
                for (int e = 0; e < inner.Count; ++e)
                    if (OnSegment(p, inner[e], inner[(e + 1) % inner.Count])) touching = true;
            if (touching)
            {
                if (!EdgeInsetPieces(outer, inner, triangles)) return false;
                long area = 0;
                foreach (var piece in triangles) area += Area(piece);
                if (area != Area(outer) + Area(inner)) return false;
                MergeInsetPieces(triangles);
                reason = null;
                return true;
            }
            int oi = -1, ii = -1;
            long shortest = long.MaxValue;
            for (int i = 0; i < outer.Count; ++i)
                for (int j = 0; j < inner.Count; ++j)
                {
                    Point a = outer[i], b = inner[j];
                    long distance = ((long)a.X - b.X) * (a.X - b.X) + ((long)a.Y - b.Y) * (a.Y - b.Y);
                    if (distance >= shortest || EntersShape(a, b, inset) ||
                        !ContainsBoxCenter(polygon, (a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0)) continue;
                    bool blocked = false;
                    for (int e = 0; e < outer.Count; ++e)
                        if (Blocks(a, b, outer[e], outer[(e + 1) % outer.Count])) blocked = true;
                    for (int e = 0; e < inner.Count; ++e)
                        if (Blocks(a, b, inner[e], inner[(e + 1) % inner.Count])) blocked = true;
                    if (!blocked) { oi = i; ii = j; shortest = distance; }
                }
            if (oi == -1) return false;
            var ringPoints = new List<Point> { outer[oi] };
            for (int j = 0; j <= inner.Count; ++j) ringPoints.Add(inner[(ii + j) % inner.Count]);
            for (int i = 0; i < outer.Count; ++i) ringPoints.Add(outer[(oi + i) % outer.Count]);
            if (!TriangulateInsetRing(ringPoints, triangles)) return false;
            long total = 0;
            foreach (List<Point> triangle in triangles) total += Area(triangle);
            if (total != Area(outer) + Area(inner)) return false;
            MergeInsetPieces(triangles);
            reason = null;
            return true;
        }

        static bool TriangulateInsetRing(List<Point> ringPoints, List<List<Point>> triangles)
        {
            if (ringPoints.Count < 3) return false;
            while (ringPoints.Count > 3)
            {
                bool clipped = false;
                for (int i = 0; i < ringPoints.Count; ++i)
                {
                    Point a = ringPoints[(i + ringPoints.Count - 1) % ringPoints.Count], b = ringPoints[i],
                          c = ringPoints[(i + 1) % ringPoints.Count];
                    if (ShapeCross(a, b, c) <= 0) continue;
                    bool blocked = false;
                    foreach (Point q in ringPoints)
                    {
                        if (SamePoint(q, a) || SamePoint(q, b) || SamePoint(q, c)) continue;
                        if (ShapeCross(a, b, q) >= 0 && ShapeCross(b, c, q) >= 0 && ShapeCross(c, a, q) >= 0)
                        { blocked = true; break; }
                    }
                    if (blocked) continue;
                    triangles.Add(new List<Point> { a, b, c });
                    ringPoints.RemoveAt(i); clipped = true; break;
                }
                if (!clipped) return false;
            }
            if (ShapeCross(ringPoints[0], ringPoints[1], ringPoints[2]) <= 0) return false;
            triangles.Add(ringPoints);
            return true;
        }

        // For boundary contact, cancel shared perimeter segments and walk the
        // remaining directed edges. The outside region can have several loops
        // (for example, a box spanning the full width of its container).
        static bool EdgeInsetPieces(List<Point> outer, List<Point> inner, List<List<Point>> pieces)
        {
            var vertices = new HashSet<Point>(outer); vertices.UnionWith(inner);
            var edges = new List<(Point A, Point B)>();
            void AddRing(List<Point> ring)
            {
                for (int i = 0; i < ring.Count; ++i)
                {
                    Point a = ring[i], b = ring[(i + 1) % ring.Count];
                    var split = new List<Point>();
                    foreach (Point p in vertices) if (OnSegment(p, a, b)) split.Add(p);
                    split.Sort((p, q) => (((long)p.X-a.X)*(p.X-a.X)+((long)p.Y-a.Y)*(p.Y-a.Y)).CompareTo(
                        ((long)q.X-a.X)*(q.X-a.X)+((long)q.Y-a.Y)*(q.Y-a.Y)));
                    for (int j = 1; j < split.Count; ++j)
                    {
                        var edge = (A: split[j - 1], B: split[j]);
                        int reverse = edges.FindIndex(e => SamePoint(e.A, edge.B) && SamePoint(e.B, edge.A));
                        if (reverse != -1) edges.RemoveAt(reverse); else edges.Add(edge);
                    }
                }
            }
            AddRing(outer); AddRing(inner);
            while (edges.Count > 0)
            {
                var edge = edges[0]; edges.RemoveAt(0);
                Point start = edge.A, previous = edge.A, current = edge.B;
                var ring = new List<Point> { start };
                while (!SamePoint(current, start))
                {
                    ring.Add(current);
                    double incoming = Math.Atan2(previous.Y - current.Y, previous.X - current.X);
                    int next = -1; double bestTurn = double.MaxValue;
                    for (int i = 0; i < edges.Count; ++i)
                    {
                        if (!SamePoint(edges[i].A, current)) continue;
                        double angle = Math.Atan2(edges[i].B.Y - current.Y, edges[i].B.X - current.X);
                        double turn = incoming - angle;
                        while (turn < 0) turn += 2 * Math.PI;
                        if (turn < bestTurn) { next = i; bestTurn = turn; }
                    }
                    if (next == -1) return false;
                    previous = current; current = edges[next].B; edges.RemoveAt(next);
                }
                if (!TriangulateInsetRing(ring, pieces)) return false;
            }
            return true;
        }

        // A union of adjacent pieces is convex exactly when its convex hull
        // has the same area. Plan these merges before creating any map lines,
        // so the triangulation diagonals never become stray geometry.
        void MergeInsetPieces(List<List<Point>> pieces, bool discardUnusedPoints=false, IEnumerable<Point> boundaryPoints=null, bool mergeClusters=false)
        {
            long Area(List<Point> ring)
            {
                long area = 0;
                for (int i = 0; i < ring.Count; ++i)
                {
                    Point a = ring[i], b = ring[(i + 1) % ring.Count];
                    area += (long)a.X * b.Y - (long)a.Y * b.X;
                }
                return area;
            }
            var allPoints = new HashSet<Point>(Endpoints);
            foreach (var piece in pieces) foreach (Point p in piece) allPoints.Add(p);
            List<Point> Hull(List<Point> a, List<Point> b)
            {
                var unique = new HashSet<Point>(a); unique.UnionWith(b);
                var sorted = new List<Point>(unique);
                sorted.Sort((p, q) => p.X != q.X ? p.X.CompareTo(q.X) : p.Y.CompareTo(q.Y));
                var lower = new List<Point>(); var upper = new List<Point>();
                foreach (Point p in sorted)
                {
                    while (lower.Count >= 2 && ShapeCross(lower[lower.Count - 2], lower[lower.Count - 1], p) <= 0)
                        lower.RemoveAt(lower.Count - 1);
                    lower.Add(p);
                }
                for (int i = sorted.Count - 1; i >= 0; --i)
                {
                    Point p = sorted[i];
                    while (upper.Count >= 2 && ShapeCross(upper[upper.Count - 2], upper[upper.Count - 1], p) <= 0)
                        upper.RemoveAt(upper.Count - 1);
                    upper.Add(p);
                }
                lower.RemoveAt(lower.Count - 1); upper.RemoveAt(upper.Count - 1);
                lower.AddRange(upper);
                return lower;
            }
            while (true)
            {
                int bestA = -1, bestB = -1;
                long bestEdge = -1;
                List<Point> bestHull = null;
                for (int i = 0; i < pieces.Count; ++i)
                    for (int j = i + 1; j < pieces.Count; ++j)
                    {
                        List<Point> hull = Hull(pieces[i], pieces[j]);
                        if (hull.Count > Polygon.MaxVertexCount || Area(hull) != Area(pieces[i]) + Area(pieces[j])) continue;
                        // For generated lighting geometry, removed internal corners
                        // need not consume slots on the merged polygon's boundary.
                        if(discardUnusedPoints) {
                            allPoints=new HashSet<Point>(Endpoints);
                            if(boundaryPoints!=null)allPoints.UnionWith(boundaryPoints);
                            for(int k=0;k<pieces.Count;k++)if(k!=i && k!=j)allPoints.UnionWith(pieces[k]);
                            allPoints.UnionWith(hull);
                        }
                        int vertices = 0;
                        for (int e = 0; e < hull.Count; ++e)
                            foreach (Point p in allPoints)
                                if (OnSegment(p, hull[e], hull[(e + 1) % hull.Count]) && !SamePoint(p, hull[(e + 1) % hull.Count])) ++vertices;
                        if (vertices > Polygon.MaxVertexCount) continue;
                        // Prefer removing long diagonals before shorter edges.
                        long shared = 0;
                        foreach (Point p in pieces[i]) foreach (Point q in pieces[i])
                            if (!SamePoint(p, q) && pieces[j].Contains(p) && pieces[j].Contains(q))
                                shared = Math.Max(shared, ((long)p.X - q.X) * (p.X - q.X) + ((long)p.Y - q.Y) * (p.Y - q.Y));
                        if (shared > bestEdge) { bestA = i; bestB = j; bestHull = hull; bestEdge = shared; }
                    }
                if (bestA == -1) {
                    if(!mergeClusters || pieces.Count<3)return;
                    // A convex region can be tiled so that no pair has a convex
                    // union (a pinwheel is one example). Try exact multi-piece
                    // unions before accepting that local minimum.
                    var clusters=new List<List<int>>{Enumerable.Range(0,pieces.Count).ToList()};
                    foreach(var seed in pieces) {
                        var vertices=new HashSet<Point>(seed);
                        clusters.Add(Enumerable.Range(0,pieces.Count).Where(k=>pieces[k].Any(vertices.Contains)).ToList());
                    }
                    bool merged=false;
                    foreach(var cluster in clusters.OrderByDescending(c=>c.Count)) {
                        if(cluster.Count<3)continue;
                        var hull=Hull(cluster.SelectMany(k=>pieces[k]).ToList(),new List<Point>());
                        if(hull.Count>Polygon.MaxVertexCount || Area(hull)!=cluster.Sum(k=>Area(pieces[k])))continue;
                        var required=new HashSet<Point>(Endpoints);
                        if(boundaryPoints!=null)required.UnionWith(boundaryPoints);
                        var members=new HashSet<int>(cluster);
                        for(int k=0;k<pieces.Count;k++)if(!members.Contains(k))required.UnionWith(pieces[k]);
                        required.UnionWith(hull);
                        int count=0;
                        for(int e=0;e<hull.Count;e++)foreach(var point in required)
                            if(OnSegment(point,hull[e],hull[(e+1)%hull.Count]) && !point.Equals(hull[(e+1)%hull.Count]))count++;
                        if(count>Polygon.MaxVertexCount)continue;
                        foreach(int k in cluster.OrderByDescending(k=>k))pieces.RemoveAt(k);
                        pieces.Add(hull);merged=true;break;
                    }
                    if(merged)continue;
                    return;
                }
                pieces[bestA] = bestHull; pieces.RemoveAt(bestB);
            }
        }

        class BoxWall
        {
            public Point Start, End;
            public bool Clockwise;
            public LineFlags Flags;
            public byte[] Side;
        }

        static void CopyBoxSettings(Polygon source, Polygon target)
        {
            target.LuaTriggerCode=source.LuaTriggerCode;target.LuaTriggerEnabled=source.LuaTriggerEnabled;target.LuaTriggerOnce=source.LuaTriggerOnce;
            target.Flags = source.Flags;
            target.FloorHeight = source.FloorHeight; target.CeilingHeight = source.CeilingHeight;
            target.FloorTexture = source.FloorTexture; target.CeilingTexture = source.CeilingTexture;
            target.FloorOrigin = source.FloorOrigin; target.CeilingOrigin = source.CeilingOrigin;
            target.FloorTransferMode = source.FloorTransferMode; target.CeilingTransferMode = source.CeilingTransferMode;
            target.FloorLight = source.FloorLight; target.CeilingLight = source.CeilingLight;
            target.MediaIndex = source.MediaIndex; target.MediaLight = source.MediaLight;
            target.AmbientSound = source.AmbientSound; target.RandomSound = source.RandomSound;
        }

        bool SplitBox(short index, Point start, Point end, out string reason)
        {
            short left = Math.Min(start.X, end.X), right = Math.Max(start.X, end.X);
            short top = Math.Min(start.Y, end.Y), bottom = Math.Max(start.Y, end.Y);
            var inset = new List<Point> { new Point(left, top), new Point(right, top),
                new Point(right, bottom), new Point(left, bottom) };
            return SplitInset(index, inset, out reason);
        }

        bool SplitInset(short index, List<Point> inset, out string reason)
        {
            reason = null;
            Polygon original = Polygons[index];
            bool rectangular = RectangleBounds(original, out Point min, out Point max) && inset.Count == 4;
            short left = short.MaxValue, top = short.MaxValue, right = short.MinValue, bottom = short.MinValue;
            for (int i = 0; i < inset.Count; ++i)
            {
                Point p = inset[i], q = inset[(i + 1) % inset.Count];
                left = Math.Min(left, p.X); right = Math.Max(right, p.X);
                top = Math.Min(top, p.Y); bottom = Math.Max(bottom, p.Y);
                if (p.X != q.X && p.Y != q.Y) rectangular = false;
            }
            List<List<Point>> pieces = null;
            if (!rectangular && !InsetPieces(original, inset, out pieces, out reason)) return false;
            return RebuildSplitPolygon(index, source => {
                string buildReason;
                Point[][] boxes = {
                    new[] { min, new Point(max.X, top) },
                    new[] { new Point(min.X, bottom), max },
                    new[] { new Point(min.X, top), new Point(left, bottom) },
                    new[] { new Point(right, top), new Point(max.X, bottom) },
                    new[] { new Point(left, top), new Point(right, bottom) }
                };
                if (rectangular)
                {
                    foreach (Point[] box in boxes)
                    {
                        if (box[0].X == box[1].X || box[0].Y == box[1].Y) continue;
                        if (!CreatePlainBox(box[0], box[1], out buildReason)) return buildReason;
                        CopyBoxSettings(original, Polygons[Polygons.Count - 1]);
                    }
                }
                else
                {
                    foreach (List<Point> piece in pieces)
                    {
                        if (!CreatePlainShape(piece, out buildReason)) return buildReason;
                        CopyBoxSettings(original, Polygons[Polygons.Count - 1]);
                    }
                    if (!CreatePlainShape(inset, out buildReason)) return buildReason;
                    CopyBoxSettings(original, Polygons[Polygons.Count - 1]);
                }
                return null;
            }, out reason);
        }

        bool RebuildSplitPolygon(short index, Func<Polygon,string> build, out string reason)
        {
            reason = null;
            Polygon original = Polygons[index];
            var walls = new List<BoxWall>();
            // Detach the perimeter without deleting objects, references, or
            // neighboring polygons. Store textured sides before splitting.
            foreach (Line line in Lines)
            {
                bool clockwise = line.ClockwisePolygonOwner == index;
                bool counterclockwise = line.CounterclockwisePolygonOwner == index;
                if (!clockwise && !counterclockwise) continue;
                short sideIndex = clockwise ? line.ClockwisePolygonSideIndex : line.CounterclockwisePolygonSideIndex;
                var wall = new BoxWall { Start = Endpoints[line.EndpointIndexes[0]], End = Endpoints[line.EndpointIndexes[1]],
                    Clockwise = clockwise, Flags = line.Flags };
                if (sideIndex != -1)
                {
                    var stream = new MemoryStream();
                    Sides[sideIndex].Save(new BinaryWriterBE(stream));
                    wall.Side = stream.ToArray();
                }
                walls.Add(wall);
                if (clockwise) line.ClockwisePolygonOwner = -1;
                if (counterclockwise) line.CounterclockwisePolygonOwner = -1;
            }
            for (int i = Sides.Count - 1; i >= 0; --i)
                if (Sides[i].PolygonIndex == index) DeleteSide((short)i);
            foreach (var polygons in EndpointPolygons) polygons.Remove(original);
            // Reserve the original polygon index for the center. Existing
            // teleporter and script references continue to refer to that index.
            Polygons[index] = new Polygon { VertexCount = 0 };
            int first = Polygons.Count;
            reason = build(original);
            if (reason != null) return false;
            short centerIndex = (short)(Polygons.Count - 1);
            Polygons[index] = Polygons[centerIndex];
            Polygons.RemoveAt(centerIndex);
            foreach (Line line in Lines)
            {
                if (line.ClockwisePolygonOwner == centerIndex) line.ClockwisePolygonOwner = index;
                if (line.CounterclockwisePolygonOwner == centerIndex) line.CounterclockwisePolygonOwner = index;
            }
            foreach (Side side in Sides)
                if (side.PolygonIndex == centerIndex) side.PolygonIndex = index;
            foreach (BoxWall wall in walls)
                for (int i = 0; i < Lines.Count; ++i)
                {
                    Line line = Lines[i];
                    Point a = Endpoints[line.EndpointIndexes[0]], b = Endpoints[line.EndpointIndexes[1]];
                    if (!OnSegment(a, wall.Start, wall.End) || !OnSegment(b, wall.Start, wall.End)) continue;
                    line.Flags = wall.Flags;
                    if (wall.Side == null) continue;
                    var side = new Side(); side.Load(new BinaryReaderBE(new MemoryStream(wall.Side)));
                    short offset = (short)Distance(wall.Start, a);
                    if (!side.Primary.Texture.IsEmpty()) side.Primary.X += offset;
                    if (!side.Secondary.Texture.IsEmpty()) side.Secondary.X += offset;
                    if (!side.Transparent.Texture.IsEmpty()) side.Transparent.X += offset;
                    side.LineIndex = (short)i;
                    side.PolygonIndex = wall.Clockwise ? line.ClockwisePolygonOwner : line.CounterclockwisePolygonOwner;
                    if (Sides.Count >= short.MaxValue)
                    {
                        reason = "Inset wall textures would exceed the map's side limit.";
                        return false;
                    }
                    if (wall.Clockwise) line.ClockwisePolygonSideIndex = (short)Sides.Count;
                    else line.CounterclockwisePolygonSideIndex = (short)Sides.Count;
                    Sides.Add(side);
                }
            short PartFor(Point p)
            {
                // Inclusive bounds give objects on an internal edge a stable
                // valid owner, with the retained polygon taking priority.
                bool insideRetained = true;
                for (int e = 0; e < Polygons[index].VertexCount; ++e)
                    if (ShapeCross(Endpoints[Polygons[index].EndpointIndexes[e]],
                        Endpoints[Polygons[index].EndpointIndexes[(e + 1) % Polygons[index].VertexCount]], p) < 0) insideRetained = false;
                if (insideRetained) return index;
                for (int i = first; i < Polygons.Count; ++i)
                {
                    Polygon polygon = Polygons[i];
                    if (ContainsBoxCenter(polygon, p.X, p.Y)) return (short)i;
                    for (int edge = 0; edge < polygon.VertexCount; ++edge)
                        if (OnSegment(p, Endpoints[polygon.EndpointIndexes[edge]],
                            Endpoints[polygon.EndpointIndexes[(edge + 1) % polygon.VertexCount]])) return (short)i;
                }
                return index;
            }
            foreach (MapObject obj in Objects)
                if (obj.PolygonIndex == index) obj.PolygonIndex = PartFor(new Point(obj.X, obj.Y));
            foreach (Annotation note in Annotations)
                if (note.PolygonIndex == index) note.PolygonIndex = PartFor(new Point(note.X, note.Y));
            if (VisualModePolygonIndex == index) VisualModePolygonIndex = PartFor(VisualModePoint);
            for (int i = 0; i < Polygons.Count; ++i)
                for (int edge = 0; edge < Polygons[i].VertexCount; ++edge)
                {
                    Line line = Lines[Polygons[i].LineIndexes[edge]];
                    bool clockwise = line.ClockwisePolygonOwner == i;
                    Polygons[i].AdjacentPolygonIndexes[edge] = clockwise ? line.CounterclockwisePolygonOwner : line.ClockwisePolygonOwner;
                    Polygons[i].SideIndexes[edge] = clockwise ? line.ClockwisePolygonSideIndex : line.CounterclockwisePolygonSideIndex;
                }
            LastCreatedBoxPolygonIndex = index;
            return true;
        }
    }
}
