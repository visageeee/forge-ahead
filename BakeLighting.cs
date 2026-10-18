using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace Weland
{
    public sealed class LightingPiece
    {
        public List<Point> Points;
        public bool Shadow;
    }

    public partial class Level
    {
        public short[] LastLightingRegionScope;
        public bool RebuildLightingRegions(ref HashSet<short> scope,out int removed,out string reason,Dictionary<short,List<LightingPiece>> lightingPlans=null,short bright=-1,short dark=-1)
        {
            removed=0;reason=null;
            // Capture before deleting any region: deleting a neighbour temporarily
            // marks shared lines solid and can change the surviving side records.
            var originalWalls=new List<BoxWall>();
            foreach(var line in Lines)for(int direction=0;direction<2;direction++) {
                short owner=direction==0?line.ClockwisePolygonOwner:line.CounterclockwisePolygonOwner;
                if(!scope.Contains(owner))continue;
                short index=direction==0?line.ClockwisePolygonSideIndex:line.CounterclockwisePolygonSideIndex;
                var wall=new BoxWall{Start=Endpoints[line.EndpointIndexes[0]],End=Endpoints[line.EndpointIndexes[1]],Clockwise=direction==0,Flags=line.Flags};
                if(index>=0){using(var stream=new MemoryStream()){Sides[index].Save(new BinaryWriterBE(stream));wall.Side=stream.ToArray();}}
                originalWalls.Add(wall);
            }
            var plansByPolygon=lightingPlans?.ToDictionary(pair=>Polygons[pair.Key],pair=>pair.Value);
            var groups=new List<List<Polygon>>();var visited=new HashSet<short>();
            foreach(short start in scope.OrderBy(i=>i)) {
                if(!visited.Add(start))continue;
                var ids=new HashSet<short>{start};var queue=new Queue<short>();queue.Enqueue(start);
                while(queue.Count>0) {
                    short current=queue.Dequeue();
                    foreach(var line in Lines) {
                        short other=line.ClockwisePolygonOwner==current ? line.CounterclockwisePolygonOwner : line.CounterclockwisePolygonOwner==current ? line.ClockwisePolygonOwner : (short)-1;
                        if(other<0 || !scope.Contains(other) || visited.Contains(other) || !CanOptimizePair(current,other,true,false))continue;
                        bool crossesProtectedBoundary=Lines.Any(edge=>
                            ((edge.ClockwisePolygonOwner==other && ids.Contains(edge.CounterclockwisePolygonOwner)) ||
                             (edge.CounterclockwisePolygonOwner==other && ids.Contains(edge.ClockwisePolygonOwner))) &&
                            !CanOptimizePair(edge.ClockwisePolygonOwner,edge.CounterclockwisePolygonOwner,true,false));
                        if(crossesProtectedBoundary)continue;
                        visited.Add(other);ids.Add(other);queue.Enqueue(other);
                    }
                }
                if(ids.Count>1 || lightingPlans!=null)groups.Add(ids.Select(i=>Polygons[i]).ToList());
            }
            foreach(var group in groups) {
                var ids=new HashSet<short>(group.Select(p=>(short)Polygons.IndexOf(p)));
                // Numeric built-in destinations must keep their original polygon.
                if(Polygons.Any(p=>(p.Type==PolygonType.Teleporter || p.Type==PolygonType.PlatformOnTrigger || p.Type==PolygonType.PlatformOffTrigger) && ids.Contains(p.Permutation)) ||
                   Sides.Any(s=>s.IsControlPanel && s.IsPlatformSwitch() && ids.Contains(s.ControlPanelPermutation))) {
                    if(plansByPolygon!=null)foreach(var protectedPolygon in group) {
                        double x=protectedPolygon.EndpointIndexes.Take(protectedPolygon.VertexCount).Average(i=>(double)Endpoints[i].X);
                        double y=protectedPolygon.EndpointIndexes.Take(protectedPolygon.VertexCount).Average(i=>(double)Endpoints[i].Y);
                        var sample=new Point((short)Math.Round(x),(short)Math.Round(y));
                        var piece=plansByPolygon[protectedPolygon].FirstOrDefault(p=>Enumerable.Range(0,p.Points.Count).All(e=>ShapeCross(p.Points[e],p.Points[(e+1)%p.Points.Count],sample)>=0));
                        if(piece!=null)protectedPolygon.FloorLight=piece.Shadow?dark:bright;
                    }
                    continue;
                }
                if(group.Count==1 && (group[0].Type!=PolygonType.Normal || !string.IsNullOrWhiteSpace(group[0].LuaTriggerCode))) {
                    if(lightingPlans!=null)group[0].FloorLight=plansByPolygon[group[0]][0].Shadow?dark:bright;
                    continue;
                }
                var planned=lightingPlans==null ? null : group.SelectMany(p=>plansByPolygon[p]).ToList();
                var edges=new List<(Point A,Point B)>();
                foreach(short id in ids) {
                    var p=Polygons[id];
                    for(int e=0;e<p.VertexCount;e++) {
                        var line=Lines[p.LineIndexes[e]];
                        if(ids.Contains(line.ClockwisePolygonOwner) && ids.Contains(line.CounterclockwisePolygonOwner))continue;
                        edges.Add((Endpoints[p.EndpointIndexes[(e+p.VertexCount-1)%p.VertexCount]],Endpoints[p.EndpointIndexes[e]]));
                    }
                }
                var loops=new List<List<Point>>();
                while(edges.Count>0) {
                    var edge=edges[0];edges.RemoveAt(0);var loop=new List<Point>{edge.A};var cursor=edge.B;
                    while(!cursor.Equals(loop[0])) {
                        loop.Add(cursor);int next=edges.FindIndex(e=>e.A.Equals(cursor));
                        if(next<0 || loop.Count>Endpoints.Count){reason="A lighting region has an open or ambiguous boundary.";return false;}
                        cursor=edges[next].B;edges.RemoveAt(next);
                    }
                    loops.Add(loop);
                }
                var outers=loops.Where(p=>LightArea(p)>0).ToList();
                if(outers.Count!=1){reason="A lighting region has an unsupported touching boundary. No changes applied.";return false;}
                double expected=loops.Sum(p=>LightArea(p));
                double originalArea=group.Sum(p=>LightArea(p.EndpointIndexes.Take(p.VertexCount).Select(i=>Endpoints[i]).ToList()));
                if(Math.Abs(expected-originalArea)>.01){reason=$"Lighting region starting at polygon {ids.Min()}: boundary area disagrees with original floors.";return false;}
                var pieces=new List<List<Point>>();
                if(planned!=null) {
                    // Shadow clipping already produced a complete region partition.
                    // Do not triangulate a second, unused bridged outline here:
                    // weakly-simple hole bridges can fail ear clipping even though
                    // the actual shadow pieces are valid.
                    pieces=planned.Select(p=>p.Points).ToList();
                    double tolerance=loops.Sum(loop=>loop.Select((p,i)=>Distance(p,loop[(i+1)%loop.Count])).Sum());
                    if(Math.Abs(pieces.Sum(p=>LightArea(p))-expected)>tolerance){reason=$"Lighting region starting at polygon {ids.Min()}: shadow pieces do not cover the original floor area within coordinate rounding tolerance.";return false;}
                }
                else {
                    foreach(var loop in loops) {
                        bool changed=true;
                        while(changed && loop.Count>3){changed=false;for(int i=0;i<loop.Count;i++)if(ShapeCross(loop[(i+loop.Count-1)%loop.Count],loop[i],loop[(i+1)%loop.Count])==0){loop.RemoveAt(i);changed=true;break;}}
                    }
                    var ring=new List<Point>(outers[0]);var holes=loops.Where(p=>LightArea(p)<0).ToList();
                    bool InsideRegion(double x,double y)=>group.Any(p=>ContainsBoxCenter(p,x,y));
                    bool Blocks(Point a,Point b,Point c,Point d) {
                        long ac=ShapeCross(a,b,c),ad=ShapeCross(a,b,d),ca=ShapeCross(c,d,a),cb=ShapeCross(c,d,b);
                        if(Math.Sign(ac)*Math.Sign(ad)<0 && Math.Sign(ca)*Math.Sign(cb)<0)return true;
                        return (OnSegment(c,a,b) && !c.Equals(a) && !c.Equals(b)) || (OnSegment(d,a,b) && !d.Equals(a) && !d.Equals(b));
                    }
                    foreach(var hole in holes) {
                        int outerIndex=-1,holeIndex=-1;double best=double.MaxValue;
                        for(int i=0;i<ring.Count;i++)for(int j=0;j<hole.Count;j++) {
                            Point a=ring[i],b=hole[j];double length=(double)(a.X-b.X)*(a.X-b.X)+(double)(a.Y-b.Y)*(a.Y-b.Y);
                            if(length>=best || !InsideRegion((a.X+b.X)/2.0,(a.Y+b.Y)/2.0))continue;
                            bool blocked=false;
                            foreach(var boundary in loops.Concat(new[]{ring}))for(int e=0;e<boundary.Count;e++)if(Blocks(a,b,boundary[e],boundary[(e+1)%boundary.Count]))blocked=true;
                            if(!blocked){best=length;outerIndex=i;holeIndex=j;}
                        }
                        if(outerIndex<0){reason="Cannot connect a hole in the lighting region safely.";return false;}
                        var joined=new List<Point>{ring[outerIndex]};
                        for(int j=0;j<=hole.Count;j++)joined.Add(hole[(holeIndex+j)%hole.Count]);
                        for(int i=0;i<ring.Count;i++)joined.Add(ring[(outerIndex+i)%ring.Count]);ring=joined;
                    }
                    if(!TriangulateInsetRing(ring,pieces) || Math.Abs(pieces.Sum(p=>LightArea(p))-expected)>.01){reason="Could not reconstruct a lighting region without changing its area.";return false;}
                }
                var settings=group[0];var walls=new List<BoxWall>();var touched=new HashSet<Line>();
                foreach(var line in Lines) {
                    bool cw=ids.Contains(line.ClockwisePolygonOwner),ccw=ids.Contains(line.CounterclockwisePolygonOwner);
                    if(!cw && !ccw)continue;touched.Add(line);
                    if(cw && ccw)continue;
                    short side=cw?line.ClockwisePolygonSideIndex:line.CounterclockwisePolygonSideIndex;
                    var wall=new BoxWall{Start=Endpoints[line.EndpointIndexes[0]],End=Endpoints[line.EndpointIndexes[1]],Clockwise=cw,Flags=line.Flags};
                    if(side>=0){using(var stream=new MemoryStream()){Sides[side].Save(new BinaryWriterBE(stream));wall.Side=stream.ToArray();}}walls.Add(wall);
                }
                int internalCount=touched.Count(l=>ids.Contains(l.ClockwisePolygonOwner) && ids.Contains(l.CounterclockwisePolygonOwner));
                var objects=Objects.Where(o=>ids.Contains(o.PolygonIndex)).ToList();var notes=Annotations.Where(o=>ids.Contains(o.PolygonIndex)).ToList();
                var oldPoints=new HashSet<Point>(group.SelectMany(p=>p.EndpointIndexes.Take(p.VertexCount).Select(v=>Endpoints[v])));
                foreach(short id in ids.OrderByDescending(i=>i))DeletePolygon(id);
                scope=new HashSet<short>(scope.Where(i=>!ids.Contains(i)).Select(i=>(short)(i-ids.Count(d=>d<i))));
                for(int i=Lines.Count-1;i>=0;i--)if(touched.Contains(Lines[i]) && Lines[i].ClockwisePolygonOwner<0 && Lines[i].CounterclockwisePolygonOwner<0)DeleteLineIndex((short)i);
                // Keep texture segment boundaries even when their old lines vanish.
                // Otherwise a new wall can span several saved sides and match none.
                var wallPoints=new HashSet<Point>(walls.Where(w=>w.Side!=null).SelectMany(w=>new[]{w.Start,w.End}));
                for(int i=Endpoints.Count-1;i>=0;i--)if(oldPoints.Contains(Endpoints[i]) && !wallPoints.Contains(Endpoints[i]) && !Lines.Any(l=>l.EndpointIndexes.Contains((short)i)))DeletePoint((short)i);
                if(planned==null)MergeInsetPieces(pieces,true);
                else {planned=SimplifyLightingPieces(planned,true);pieces=planned.Select(p=>p.Points).ToList();}
                foreach(var piece in pieces) {
                    bool changed=true;
                    while(changed && piece.Count>3){changed=false;for(int e=0;e<piece.Count;e++)if(ShapeCross(piece[(e+piece.Count-1)%piece.Count],piece[e],piece[(e+1)%piece.Count])==0){piece.RemoveAt(e);changed=true;break;}}
                }
                int first=Polygons.Count;
                for(int k=0;k<pieces.Count;k++){if(!CreatePlainShape(pieces[k],out reason))return false;var created=Polygons[Polygons.Count-1];CopyBoxSettings(settings,created);if(planned!=null)created.FloorLight=planned[k].Shadow?dark:bright;scope.Add((short)(Polygons.Count-1));}
                foreach(var wall in walls)for(int i=0;i<Lines.Count;i++) {
                    var line=Lines[i];Point a=Endpoints[line.EndpointIndexes[0]],b=Endpoints[line.EndpointIndexes[1]];
                    if(!OnSegment(a,wall.Start,wall.End) || !OnSegment(b,wall.Start,wall.End))continue;
                    bool forward=(long)(b.X-a.X)*(wall.End.X-wall.Start.X)+(long)(b.Y-a.Y)*(wall.End.Y-wall.Start.Y)>0;
                    bool clockwise=forward?wall.Clockwise:!wall.Clockwise;
                    short owner=clockwise?line.ClockwisePolygonOwner:line.CounterclockwisePolygonOwner;
                    if(owner<first)continue;
                    line.Flags=wall.Flags;if(wall.Side==null)continue;
                    var side=new Side();side.Load(new BinaryReaderBE(new MemoryStream(wall.Side)));side.LineIndex=(short)i;side.PolygonIndex=owner;
                    Point oldOrigin=wall.Clockwise?wall.Start:wall.End,newOrigin=clockwise?a:b;
                    short offset=(short)Distance(oldOrigin,newOrigin);side.Primary.X+=offset;side.Secondary.X+=offset;side.Transparent.X+=offset;
                    if(clockwise)line.ClockwisePolygonSideIndex=(short)Sides.Count;else line.CounterclockwisePolygonSideIndex=(short)Sides.Count;Sides.Add(side);
                }
                foreach(var obj in objects){short owner=GetEnclosingPolygon(new Point(obj.X,obj.Y));if(owner<first){reason="Region reconstruction could not place an object.";return false;}obj.PolygonIndex=owner;Objects.Add(obj);}
                foreach(var note in notes){short owner=GetEnclosingPolygon(new Point(note.X,note.Y));if(owner<first){reason="Region reconstruction could not place an annotation.";return false;}note.PolygonIndex=owner;Annotations.Add(note);}
                removed+=internalCount;
            }
            bool OnOriginalWall(Point p,Point a,Point b) {
                double dx=b.X-a.X,dy=b.Y-a.Y,length=dx*dx+dy*dy;
                double dot=(p.X-a.X)*dx+(p.Y-a.Y)*dy,cross=ShapeCross(a,b,p);
                return length>0 && dot>=0 && dot<=length && cross*cross<=length;
            }
            for(int i=0;i<Lines.Count;i++) {
                var line=Lines[i];Point a=Endpoints[line.EndpointIndexes[0]],b=Endpoints[line.EndpointIndexes[1]];
                bool touched=scope.Contains(line.ClockwisePolygonOwner)||scope.Contains(line.CounterclockwisePolygonOwner);
                if(!touched)continue;
                // New shadow partitions are portals, never collision walls.
                if(line.ClockwisePolygonOwner>=0 && line.CounterclockwisePolygonOwner>=0)line.Flags=LineFlags.Transparent;
                foreach(var wall in originalWalls) {
                    if(!OnOriginalWall(a,wall.Start,wall.End)||!OnOriginalWall(b,wall.Start,wall.End))continue;
                    bool forward=(long)(b.X-a.X)*(wall.End.X-wall.Start.X)+(long)(b.Y-a.Y)*(wall.End.Y-wall.Start.Y)>0;
                    bool clockwise=forward?wall.Clockwise:!wall.Clockwise;
                    short owner=clockwise?line.ClockwisePolygonOwner:line.CounterclockwisePolygonOwner;
                    if(!scope.Contains(owner))continue;
                    line.Flags=wall.Flags;
                    if(wall.Side==null)continue;
                    var side=new Side();side.Load(new BinaryReaderBE(new MemoryStream(wall.Side)));side.LineIndex=(short)i;side.PolygonIndex=owner;
                    short offset=(short)Distance(wall.Clockwise?wall.Start:wall.End,clockwise?a:b);
                    side.Primary.X+=offset;side.Secondary.X+=offset;side.Transparent.X+=offset;
                    short existing=clockwise?line.ClockwisePolygonSideIndex:line.CounterclockwisePolygonSideIndex;
                    if(existing>=0)Sides[existing]=side;
                    else {existing=(short)Sides.Count;Sides.Add(side);}
                    if(clockwise)line.ClockwisePolygonSideIndex=existing;else line.CounterclockwisePolygonSideIndex=existing;
                    FixSideType(side);
                }
            }
            var before=new Level();before.Load(Save().Clone());
            return RepairReflection(before,new HashSet<short>(Enumerable.Range(0,Endpoints.Count).Select(i=>(short)i)),out reason);
        }

        // Engine/visual-mode maps can pair vertex i with its outgoing edge.
        // Editing operations pair it with the incoming edge. Match by endpoints,
        // carrying side/adjacency data with the line; never change winding.
        public bool NormalizeLightingEdgeOrder(out string reason)
        {
            reason=null;
            for(int id=0;id<Polygons.Count;id++) {
                var p=Polygons[id];var lines=(short[])p.LineIndexes.Clone();
                var sides=(short[])p.SideIndexes.Clone();var adjacent=(short[])p.AdjacentPolygonIndexes.Clone();
                for(int e=0;e<p.VertexCount;e++) {
                    short a=p.EndpointIndexes[(e+p.VertexCount-1)%p.VertexCount],b=p.EndpointIndexes[e];int match=-1;
                    for(int j=0;j<p.VertexCount;j++) {
                        if(lines[j]<0 || lines[j]>=Lines.Count)continue;
                        var l=Lines[lines[j]];
                        if((l.EndpointIndexes[0]==a && l.EndpointIndexes[1]==b) || (l.EndpointIndexes[0]==b && l.EndpointIndexes[1]==a)){match=j;break;}
                    }
                    if(match<0){reason=$"Polygon {id}: no boundary line connects vertices {a} and {b}.";return false;}
                    p.LineIndexes[e]=lines[match];p.SideIndexes[e]=sides[match];p.AdjacentPolygonIndexes[e]=adjacent[match];
                }
            }
            return true;
        }

        struct LightPoint
        {
            public double X,Y;
            public LightPoint(double x,double y) { X=x;Y=y; }
        }
        static double LightArea(List<Point> p) {
            double a=0;for(int i=0;i<p.Count;i++)a+=(double)p[i].X*p[(i+1)%p.Count].Y-(double)p[i].Y*p[(i+1)%p.Count].X;return a/2;
        }
        static List<Point> LightClip(List<Point> ring,LightPoint a,LightPoint b,bool inside) {
            var result=new List<Point>();
            for(int i=0;i<ring.Count;i++) {
                var p=ring[i];var q=ring[(i+1)%ring.Count];
                double cp=(b.X-a.X)*(p.Y-a.Y)-(b.Y-a.Y)*(p.X-a.X),cq=(b.X-a.X)*(q.Y-a.Y)-(b.Y-a.Y)*(q.X-a.X);
                bool pin=inside?cp>=0:cp<=0,qin=inside?cq>=0:cq<=0;
                if(pin)result.Add(p);
                if(pin!=qin){double t=cp/(cp-cq);result.Add(new Point((short)Math.Round(p.X+t*(q.X-p.X)),(short)Math.Round(p.Y+t*(q.Y-p.Y))));}
            }
            result=result.Distinct().ToList();
            bool changed=true;while(changed && result.Count>=3){changed=false;for(int i=0;i<result.Count;i++)if(ShapeCross(result[(i+result.Count-1)%result.Count],result[i],result[(i+1)%result.Count])==0){result.RemoveAt(i);changed=true;break;}}
            return result;
        }

        // Approximate the shadow on an exact integer-lattice subdivision. Only the
        // lighting boundary is approximate: the original floor coverage stays exact.
        List<LightingPiece> ApproximateFloorLight(List<Point> ring,Point source,double sourceZ,double floor,int allowance,double minimumArea,int refinementLimit)
        {
            bool Shadow(double x,double y) {
                double dx=x-source.X,dy=y-source.Y;
                foreach(var edge in Lines) {
                    int cw=edge.ClockwisePolygonOwner,ccw=edge.CounterclockwisePolygonOwner;
                    if(cw<0 && ccw<0)continue;
                    double top=cw<0 || ccw<0 ? sourceZ : Math.Max(Polygons[cw].FloorHeight,Polygons[ccw].FloorHeight);
                    if(top<=floor)continue;
                    var a=Endpoints[edge.EndpointIndexes[0]];var b=Endpoints[edge.EndpointIndexes[1]];
                    double ex=b.X-a.X,ey=b.Y-a.Y,den=dx*ey-dy*ex;
                    if(Math.Abs(den)<1e-9)continue;
                    double ax=a.X-source.X,ay=a.Y-source.Y;
                    double t=(ax*ey-ay*ex)/den,u=(ax*dy-ay*dx)/den;
                    if(t>1e-8 && t<1-1e-8 && u>=0 && u<=1 && top>sourceZ+t*(floor-sourceZ)+1e-8)return true;
                }
                return false;
            }
            if(refinementLimit==1)return new List<LightingPiece>{new LightingPiece{Points=ring,Shadow=Shadow(ring.Average(v=>(double)v.X),ring.Average(v=>(double)v.Y))}};
            var triangles=new List<List<Point>>();
            if(!TriangulateInsetRing(ring,triangles))return null;
            int Gcd(int a,int b){while(b!=0){int t=a%b;a=b;b=t;}return a;}
            // Refine broad pieces first. At most 64 pieces per receiving polygon.
            int cap=Math.Min(refinementLimit,Math.Max(triangles.Count,allowance));
            for(int pass=0;pass<6 && triangles.Count<cap;pass++) {
                int count=triangles.Count;
                for(int i=0;i<count && triangles.Count<cap;i++) {
                    var p=triangles[i];double x=p.Average(v=>(double)v.X),y=p.Average(v=>(double)v.Y);bool dark=Shadow(x,y);
                    bool mixed=p.Any(v=>Shadow(x*.02+v.X*.98,y*.02+v.Y*.98)!=dark);
                    if(!mixed && pass>=2)continue;
                    int best=-1,gcd=0;double length=-1;
                    for(int e=0;e<3;e++) {
                        var a=p[e];var b=p[(e+1)%3];int g=Gcd(Math.Abs(b.X-a.X),Math.Abs(b.Y-a.Y));
                        double l=(double)(b.X-a.X)*(b.X-a.X)+(double)(b.Y-a.Y)*(b.Y-a.Y);
                        if(g>1 && l>length){best=e;gcd=g;length=l;}
                    }
                    if(best<0)continue;
                    var start=p[best];var end=p[(best+1)%3];var other=p[(best+2)%3];int step=gcd/2;
                    var mid=new Point((short)(start.X+(end.X-start.X)/gcd*step),(short)(start.Y+(end.Y-start.Y)/gcd*step));
                    var first=new List<Point>{start,mid,other};var second=new List<Point>{mid,end,other};
                    if(LightArea(first)<minimumArea || LightArea(second)<minimumArea)continue;
                    triangles[i]=first;triangles.Add(second);
                }
            }
            var result=new List<LightingPiece>();
            foreach(var p in triangles)result.Add(new LightingPiece{Points=p,Shadow=Shadow(p.Average(v=>(double)v.X),p.Average(v=>(double)v.Y))});
            return SimplifyLightingPieces(result);
        }

        List<LightingPiece> SimplifyLightingPieces(List<LightingPiece> pieces,bool reconstruct=false)
        {
            var lit=pieces.Where(p=>!p.Shadow).Select(p=>p.Points).ToList();
            var dark=pieces.Where(p=>p.Shadow).Select(p=>p.Points).ToList();
            int previous;
            do {
                previous=lit.Count+dark.Count;
                MergeInsetPieces(lit,true,dark.SelectMany(p=>p).ToArray(),true);
                MergeInsetPieces(dark,true,lit.SelectMany(p=>p).ToArray(),true);
            } while(lit.Count+dark.Count<previous);
            if(reconstruct) {
                lit=ReconstructLightingShade(lit,dark);
                dark=ReconstructLightingShade(dark,lit);
            }
            return lit.Select(p=>new LightingPiece{Points=p,Shadow=false})
                .Concat(dark.Select(p=>new LightingPiece{Points=p,Shadow=true})).ToList();
        }

        // Throw away same-shade internal edges and repartition from the union
        // boundary. Merging alone cannot change a bad diagonal or a concave tiling.
        List<List<Point>> ReconstructLightingShade(List<List<Point>> source,List<List<Point>> other)
        {
            if(source.Count<2)return source;
            var points=new HashSet<Point>(source.SelectMany(p=>p));
            points.UnionWith(other.SelectMany(p=>p));
            // Adjacent clipped pieces can round the same straight boundary to
            // opposite sides of a coordinate. Weld only within one map unit;
            // map boundary vertices stay protected and final topology is checked.
            bool NearSegment(Point p,Point a,Point b) {
                double dx=b.X-a.X,dy=b.Y-a.Y,length=dx*dx+dy*dy;
                double dot=(p.X-a.X)*dx+(p.Y-a.Y)*dy,cross=ShapeCross(a,b,p);
                return dot>=0 && dot<=length && cross*cross<=length;
            }
            var edges=new HashSet<(Point A,Point B)>();
            foreach(var piece in source)for(int e=0;e<piece.Count;e++) {
                Point a=piece[e],b=piece[(e+1)%piece.Count];
                var split=points.Where(p=>NearSegment(p,a,b)).OrderBy(p=>(long)(p.X-a.X)*(p.X-a.X)+(long)(p.Y-a.Y)*(p.Y-a.Y)).ToList();
                for(int i=1;i<split.Count;i++) {
                    var edge=(A:split[i-1],B:split[i]);
                    if(!edges.Remove((edge.B,edge.A)) && !edges.Add(edge))return source;
                }
            }
            // Ambiguous point contacts keep the existing valid partition.
            if(edges.GroupBy(e=>e.A).Any(g=>g.Count()!=1))return source;
            var loops=new List<List<Point>>();
            while(edges.Count>0) {
                var first=edges.First();edges.Remove(first);
                var ring=new List<Point>{first.A};var cursor=first.B;
                while(!cursor.Equals(first.A)) {
                    ring.Add(cursor);var next=edges.Where(e=>e.A.Equals(cursor)).ToList();
                    if(next.Count!=1)return source;
                    edges.Remove(next[0]);cursor=next[0].B;
                }
                bool changed=true;
                while(changed && ring.Count>3){changed=false;for(int i=0;i<ring.Count;i++)if((ShapeCross(ring[(i+ring.Count-1)%ring.Count],ring[i],ring[(i+1)%ring.Count])==0 || (!Endpoints.Contains(ring[i]) && NearSegment(ring[i],ring[(i+ring.Count-1)%ring.Count],ring[(i+1)%ring.Count])))){ring.RemoveAt(i);changed=true;break;}}
                loops.Add(ring);
            }
            bool Inside(List<Point> ring,Point p) {
                bool inside=false;
                for(int i=0,j=ring.Count-1;i<ring.Count;j=i++) {
                    Point a=ring[i],b=ring[j];
                    if((a.Y>p.Y)!=(b.Y>p.Y) && p.X<(double)(b.X-a.X)*(p.Y-a.Y)/(b.Y-a.Y)+a.X)inside=!inside;
                }
                return inside;
            }
            bool Blocks(Point a,Point b,Point c,Point d) {
                long ac=ShapeCross(a,b,c),ad=ShapeCross(a,b,d),ca=ShapeCross(c,d,a),cb=ShapeCross(c,d,b);
                return Math.Sign(ac)*Math.Sign(ad)<0 && Math.Sign(ca)*Math.Sign(cb)<0 ||
                    OnSegment(c,a,b) && !c.Equals(a) && !c.Equals(b) || OnSegment(d,a,b) && !d.Equals(a) && !d.Equals(b);
            }
            var result=new List<List<Point>>();
            foreach(var outer in loops.Where(p=>LightArea(p)>0)) {
                var ring=new List<Point>(outer);
                foreach(var hole in loops.Where(p=>LightArea(p)<0 && Inside(outer,p[0]))) {
                    int oi=-1,hi=-1;double best=double.MaxValue;
                    for(int i=0;i<ring.Count;i++)for(int j=0;j<hole.Count;j++) {
                        Point a=ring[i],b=hole[j];double length=Distance(a,b);
                        if(length>=best)continue;
                        double x=(a.X+b.X)/2.0,y=(a.Y+b.Y)/2.0;
                        if(!source.Any(p=>Enumerable.Range(0,p.Count).All(e=>
                            (p[(e+1)%p.Count].X-p[e].X)*(y-p[e].Y)-(p[(e+1)%p.Count].Y-p[e].Y)*(x-p[e].X)>=0)))continue;
                        if(loops.Concat(new[]{ring}).Any(p=>Enumerable.Range(0,p.Count).Any(e=>Blocks(a,b,p[e],p[(e+1)%p.Count]))))continue;
                        best=length;oi=i;hi=j;
                    }
                    if(oi<0)return source;
                    var joined=new List<Point>{ring[oi]};
                    for(int j=0;j<=hole.Count;j++)joined.Add(hole[(hi+j)%hole.Count]);
                    for(int i=0;i<ring.Count;i++)joined.Add(ring[(oi+i)%ring.Count]);ring=joined;
                }
                if(!TriangulateInsetRing(ring,result))return source;
            }
            if(result.Count==0 || Math.Abs(result.Sum(p=>LightArea(p))-source.Sum(p=>LightArea(p)))>loops.Sum(p=>p.Select((v,i)=>Distance(v,p[(i+1)%p.Count])).Sum()))return source;
            MergeInsetPieces(result,true,other.SelectMany(p=>p).ToArray(),true);
            var required=new HashSet<Point>(Endpoints);
            required.UnionWith(other.SelectMany(p=>p));required.UnionWith(result.SelectMany(p=>p));
            foreach(var piece in result) {
                int count=0;
                for(int e=0;e<piece.Count;e++)foreach(var point in required)
                    if(OnSegment(point,piece[e],piece[(e+1)%piece.Count]) && !point.Equals(piece[(e+1)%piece.Count]))count++;
                if(count>Polygon.MaxVertexCount)return source;
            }
            // An equal-count reconstruction may still repair rounded seams.
            return result.Count<=source.Count ? result : source;
        }

        // Display-only shadow masks. These may overlap and need no map polygons.
        public List<LightingPiece> PreviewFloorLighting(short[] selected,Point source,double sourceZ,out string reason)
        {
            var result=new List<LightingPiece>();reason=null;
            short owner=GetEnclosingPolygon(source);
            if(owner<0 || sourceZ<=Polygons[owner].FloorHeight || sourceZ>=Polygons[owner].CeilingHeight){reason="Place the light between the floor and ceiling inside the map.";return result;}
            if(selected.Length==0){reason="Select receiving floors first.";return result;}
            foreach(short id in selected) {
                if(id<0 || id>=Polygons.Count)continue;
                var polygon=Polygons[id];
                var ring=polygon.EndpointIndexes.Take(polygon.VertexCount).Select(v=>Endpoints[v]).ToList();
                if(LightArea(ring)<0)ring.Reverse();
                int start=Enumerable.Range(0,ring.Count).OrderBy(i=>ring[i].X).ThenBy(i=>ring[i].Y).First();
                ring=Enumerable.Range(0,ring.Count).Select(i=>ring[(start+i)%ring.Count]).ToList();
                // A horizontal top surface cannot receive direct light from below.
                result.Add(new LightingPiece{Points=ring,Shadow=sourceZ<=polygon.FloorHeight});
                if(sourceZ<=polygon.FloorHeight)continue;
                foreach(var edge in Lines) {
                    int cw=edge.ClockwisePolygonOwner,ccw=edge.CounterclockwisePolygonOwner;
                    if(cw<0 && ccw<0)continue;
                    double top=cw<0 || ccw<0 ? sourceZ : Math.Max(Polygons[cw].FloorHeight,Polygons[ccw].FloorHeight);
                    if(top<=polygon.FloorHeight || (cw>=0 && ccw>=0 && Polygons[cw].FloorHeight==Polygons[ccw].FloorHeight))continue;
                    var a=Endpoints[edge.EndpointIndexes[0]];var b=Endpoints[edge.EndpointIndexes[1]];
                    double factor=top>=sourceZ ? 1000000 : (sourceZ-polygon.FloorHeight)/(sourceZ-top);
                    var mask=new List<LightPoint>{new LightPoint(a.X,a.Y),new LightPoint(b.X,b.Y),new LightPoint(source.X+(b.X-source.X)*factor,source.Y+(b.Y-source.Y)*factor),new LightPoint(source.X+(a.X-source.X)*factor,source.Y+(a.Y-source.Y)*factor)};
                    double area=0;for(int k=0;k<4;k++)area+=mask[k].X*mask[(k+1)%4].Y-mask[k].Y*mask[(k+1)%4].X;
                    if(Math.Abs(area)<.01)continue;if(area<0)mask.Reverse();
                    var clipped=ring;
                    for(int k=0;k<4 && clipped.Count>=3;k++)clipped=LightClip(clipped,mask[k],mask[(k+1)%4],true);
                    if(clipped.Count>=3 && LightArea(clipped)>0)result.Add(new LightingPiece{Points=clipped,Shadow=true});
                }
            }
            return result;
        }

        void BakeSurfaceLighting(HashSet<short> receivers,Point source,double sourceZ,short bright,short dark)
        {
            var wallLights=new Dictionary<int,short>{{0,bright},{15,dark}};
            short BlendedLight(int blocked) {
                if(wallLights.TryGetValue(blocked,out short existing))return existing;
                double intensity=Lights[bright].PrimaryActive.Intensity+(Lights[dark].PrimaryActive.Intensity-Lights[bright].PrimaryActive.Intensity)*blocked/15.0;
                var light=new Light();light.PrimaryActive=light.SecondaryActive=light.BecomingActive=light.PrimaryInactive=light.SecondaryInactive=light.BecomingInactive=new Light.Function(LightingFunction.Constant,30,0,intensity,0);
                short index=(short)Lights.Count;Lights.Add(light);wallLights.Add(blocked,index);return index;
            }
            bool Blocked(double x,double y,double z,Line ignored=null) {
                double dx=x-source.X,dy=y-source.Y;
                foreach(var blocker in Lines) {
                    if(ReferenceEquals(blocker,ignored))continue;
                    int cw=blocker.ClockwisePolygonOwner,ccw=blocker.CounterclockwisePolygonOwner;
                    if(cw<0 && ccw<0)continue;
                    var u=Endpoints[blocker.EndpointIndexes[0]];var v=Endpoints[blocker.EndpointIndexes[1]];
                    double ex=v.X-u.X,ey=v.Y-u.Y,den=dx*ey-dy*ex;
                    if(Math.Abs(den)<1e-9)continue;
                    double ax=u.X-source.X,ay=u.Y-source.Y;
                    double t=(ax*ey-ay*ex)/den,s=(ax*dy-ay*dx)/den;
                    if(t<=1e-8 || t>=1-1e-8 || s<0 || s>1)continue;
                    if(cw<0 || ccw<0)return true;
                    double height=sourceZ+t*(z-sourceZ);
                    if(height<=Math.Max(Polygons[cw].FloorHeight,Polygons[ccw].FloorHeight) ||
                       height>=Math.Min(Polygons[cw].CeilingHeight,Polygons[ccw].CeilingHeight))return true;
                }
                return false;
            }
            foreach(short id in receivers) {
                var polygon=Polygons[id];
                if(sourceZ>=polygon.CeilingHeight){polygon.CeilingLight=dark;continue;}
                var ring=polygon.EndpointIndexes.Take(polygon.VertexCount).Select(i=>Endpoints[i]).ToList();
                double cx=ring.Average(p=>(double)p.X),cy=ring.Average(p=>(double)p.Y);
                double total=0,occluded=0;
                for(int i=0;i<ring.Count;i++) {
                    Point a=ring[i],b=ring[(i+1)%ring.Count];
                    double area=Math.Abs((a.X-cx)*(b.Y-cy)-(a.Y-cy)*(b.X-cx));
                    // Three interior samples per fan triangle, weighted by its area.
                    foreach(var weights in new[]{new[]{2.0/3,1.0/6,1.0/6},new[]{1.0/6,2.0/3,1.0/6},new[]{1.0/6,1.0/6,2.0/3}}) {
                        double x=cx*weights[0]+a.X*weights[1]+b.X*weights[2];
                        double y=cy*weights[0]+a.Y*weights[1]+b.Y*weights[2];
                        total+=area;
                        if(Blocked(x,y,polygon.CeilingHeight))occluded+=area;
                    }
                }
                polygon.CeilingLight=BlendedLight(total>0?(int)Math.Round(15*occluded/total):15);
            }
            foreach(var side in Sides) {
                if(!receivers.Contains(side.PolygonIndex) || side.LineIndex<0 || side.LineIndex>=Lines.Count)continue;
                var polygon=Polygons[side.PolygonIndex];var line=Lines[side.LineIndex];
                var a=Endpoints[line.EndpointIndexes[0]];var b=Endpoints[line.EndpointIndexes[1]];
                double x=(a.X+b.X)/2.0,y=(a.Y+b.Y)/2.0;
                double centerX=polygon.EndpointIndexes.Take(polygon.VertexCount).Average(i=>(double)Endpoints[i].X);
                double centerY=polygon.EndpointIndexes.Take(polygon.VertexCount).Average(i=>(double)Endpoints[i].Y);
                double interior=(b.X-a.X)*(centerY-a.Y)-(b.Y-a.Y)*(centerX-a.X);
                double facing=(b.X-a.X)*(source.Y-a.Y)-(b.Y-a.Y)*(source.X-a.X);
                short LightAt(double z) {
                    if(interior*facing<=0)return dark;
                    return Blocked(x,y,z,line)?dark:bright;
                }
                short opposite=line.ClockwisePolygonOwner==side.PolygonIndex ? line.CounterclockwisePolygonOwner : line.ClockwisePolygonOwner;
                double primaryLow=polygon.FloorHeight,primaryHigh=polygon.CeilingHeight;
                double secondaryLow=primaryLow,secondaryHigh=primaryHigh,transparentLow=primaryLow,transparentHigh=primaryHigh;
                if(opposite>=0) {
                    var other=Polygons[opposite];
                    if(side.Type==SideType.High || side.Type==SideType.Split)primaryLow=Math.Max(polygon.FloorHeight,other.CeilingHeight);
                    if(side.Type==SideType.Low)primaryHigh=Math.Min(polygon.CeilingHeight,other.FloorHeight);
                    secondaryHigh=Math.Min(polygon.CeilingHeight,other.FloorHeight);
                    transparentLow=Math.Max(polygon.FloorHeight,other.FloorHeight);transparentHigh=Math.Min(polygon.CeilingHeight,other.CeilingHeight);
                }
                short SurfaceLight(double low,double high) {
                    if(high<=low)return LightAt((low+high)/2);
                    int blocked=0;
                    foreach(double along in new[]{.1,.5,.9}) {
                        x=a.X+(b.X-a.X)*along;y=a.Y+(b.Y-a.Y)*along;
                        foreach(double up in new[]{.02,.25,.5,.75,.98})if(LightAt(low+(high-low)*up)==dark)blocked++;
                    }
                    return BlendedLight(blocked);
                }
                side.PrimaryLightsourceIndex=SurfaceLight(primaryLow,primaryHigh);
                side.SecondaryLightsourceIndex=SurfaceLight(secondaryLow,secondaryHigh);
                side.TransparentLightsourceIndex=SurfaceLight(transparentLow,transparentHigh);
            }
        }

        // Operates on a disposable clone. Callers commit only after all splits validate.
        public bool BakeFloorLighting(short[] selected,Point source,double sourceZ,double lit,double shade,int budget,double minimumArea,
            out List<LightingPiece> preview,out string reason,bool approximate=false,int refinementLimit=64,bool rebuildRegions=false)
        {
            preview=new List<LightingPiece>();reason=null;
            if(selected.Length==0){reason="Select the floor polygons to light first.";return false;}
            if(budget<1 || budget>short.MaxValue || minimumArea<1 || double.IsNaN(sourceZ) || lit<0 || lit>1 || shade<0 || shade>1){reason="Invalid lighting settings.";return false;}
            short sourcePolygon=GetEnclosingPolygon(source);
            if(sourcePolygon<0 || sourceZ<=Polygons[sourcePolygon].FloorHeight || sourceZ>=Polygons[sourcePolygon].CeilingHeight){reason="Place the light inside a polygon, with its absolute height between floor and ceiling.";return false;}
            if(selected.Any(i=>i<0 || i>=Polygons.Count)){reason="The lighting scope contains an invalid polygon ID.";return false;}
            if(!NormalizeLightingEdgeOrder(out reason))return false;
            var plans=new Dictionary<short,List<LightingPiece>>();
            foreach(short id in selected) {
                var polygon=Polygons[id];
                var ring=polygon.EndpointIndexes.Take(polygon.VertexCount).Select(v=>Endpoints[v]).ToList();
                if(LightArea(ring)<0)ring.Reverse();
                int start=Enumerable.Range(0,ring.Count).OrderBy(i=>ring[i].X).ThenBy(i=>ring[i].Y).First();
                ring=Enumerable.Range(0,ring.Count).Select(i=>ring[(start+i)%ring.Count]).ToList();
                if(polygon.Type!=PolygonType.Normal || !string.IsNullOrWhiteSpace(polygon.LuaTriggerCode)) {
                    // Preserve platform/trigger identity and behavior: light these
                    // surfaces without splitting or duplicating the polygon.
                    var protectedPiece=sourceZ<=polygon.FloorHeight
                        ? new LightingPiece{Points=ring,Shadow=true}
                        : ApproximateFloorLight(ring,source,sourceZ,polygon.FloorHeight,1,minimumArea,1)[0];
                    plans.Add(id,new List<LightingPiece>{protectedPiece});preview.Add(protectedPiece);
                    if(preview.Count>budget){reason="Lighting exceeds the polygon budget.";return false;}
                    continue;
                }
                if(sourceZ<=polygon.FloorHeight) {
                    var unlit=new LightingPiece{Points=ring,Shadow=true};
                    plans.Add(id,new List<LightingPiece>{unlit});preview.Add(unlit);
                    if(preview.Count>budget){reason="Lighting exceeds the polygon budget.";return false;}
                    continue;
                }
                if(approximate) {
                    int allowance=(budget-preview.Count)/(selected.Length-plans.Count);
                    var coarse=ApproximateFloorLight(ring,source,sourceZ,polygon.FloorHeight,allowance,minimumArea,refinementLimit);
                    if(coarse==null || coarse.Count>allowance){reason="Not enough polygon budget for approximate shadows. Increase the budget or select fewer floors.";return false;}
                    plans.Add(id,coarse);preview.AddRange(coarse);continue;
                }
                var litPieces=new List<List<Point>>{ring};var darkPieces=new List<List<Point>>();
                foreach(var edge in Lines) {
                    int cw=edge.ClockwisePolygonOwner,ccw=edge.CounterclockwisePolygonOwner;
                    if(cw<0 && ccw<0)continue;
                    double top=cw<0 || ccw<0 ? sourceZ : Math.Max(Polygons[cw].FloorHeight,Polygons[ccw].FloorHeight);
                    if(top<=polygon.FloorHeight)continue;
                    if(cw>=0 && ccw>=0 && Polygons[cw].FloorHeight==Polygons[ccw].FloorHeight)continue;
                    var a=Endpoints[edge.EndpointIndexes[0]];var b=Endpoints[edge.EndpointIndexes[1]];
                    double factor=top>=sourceZ ? 1000000 : (sourceZ-polygon.FloorHeight)/(sourceZ-top);
                    var shadow=new List<LightPoint>{new LightPoint(a.X,a.Y),new LightPoint(b.X,b.Y),new LightPoint(source.X+(b.X-source.X)*factor,source.Y+(b.Y-source.Y)*factor),new LightPoint(source.X+(a.X-source.X)*factor,source.Y+(a.Y-source.Y)*factor)};
                    double area=0;for(int k=0;k<4;k++)area+=shadow[k].X*shadow[(k+1)%4].Y-shadow[k].Y*shadow[(k+1)%4].X;
                    if(Math.Abs(area)<0.01)continue;if(area<0)shadow.Reverse();
                    var next=new List<List<Point>>();
                    foreach(var piece in litPieces) {
                        // Skip disjoint shadows without introducing redundant cuts.
                        var intersection=piece;
                        for(int k=0;k<4 && intersection.Count>=3;k++)intersection=LightClip(intersection,shadow[k],shadow[(k+1)%4],true);
                        if(intersection.Count<3 || LightArea(intersection)<1){next.Add(piece);continue;}
                        var rest=piece;
                        for(int k=0;k<4 && rest.Count>=3;k++){
                            var outside=LightClip(rest,shadow[k],shadow[(k+1)%4],false);
                            if(outside.Count>=3 && LightArea(outside)>0)next.Add(outside);
                            rest=LightClip(rest,shadow[k],shadow[(k+1)%4],true);
                        }
                        if(rest.Count>=3 && LightArea(rest)>0)darkPieces.Add(rest);
                    }
                    litPieces=next;
                    if(litPieces.Count+darkPieces.Count+preview.Count>budget){reason="Lighting exceeds the polygon budget. Select a smaller area.";return false;}
                }
                var simplified=SimplifyLightingPieces(litPieces.Select(p=>new LightingPiece{Points=p,Shadow=false})
                    .Concat(darkPieces.Select(p=>new LightingPiece{Points=p,Shadow=true})).ToList());
                litPieces=simplified.Where(p=>!p.Shadow).Select(p=>p.Points).ToList();
                darkPieces=simplified.Where(p=>p.Shadow).Select(p=>p.Points).ToList();
                var pieces=new List<LightingPiece>();
                foreach(bool dark in new[]{false,true})foreach(var p in dark?darkPieces:litPieces){
                    if(p.Count>8 || LightArea(p)<minimumArea){reason=$"Polygon {id}: a shadow piece has {p.Count} corners and area {LightArea(p):0.##}; minimum area is {minimumArea:0.##}.";return false;}
                    pieces.Add(new LightingPiece{Points=p,Shadow=dark});
                }
                double total=pieces.Sum(p=>LightArea(p.Points));
                // Rounding an intersection to an integer coordinate introduces a
                // sub-unit displacement. Bound the area error by a one-unit strip
                // around the original perimeter; topology checks still run below.
                double roundingAreaBudget=ring.Select((p,i)=>Distance(p,ring[(i+1)%ring.Count])).Sum();
                if(Math.Abs(total-LightArea(ring))>roundingAreaBudget){reason=$"Polygon {id}: shadow intersections cannot be represented exactly at map-coordinate precision.";return false;}
                plans.Add(id,pieces);preview.AddRange(pieces);
                if(preview.Count>budget){reason="Lighting exceeds the polygon budget.";return false;}
            }
            if(Lights.Count>short.MaxValue-16){reason="Too many lights in the map.";return false;}
            short brightId=(short)Lights.Count,darkId=(short)(Lights.Count+1);
            Light Constant(double intensity){var l=new Light();l.PrimaryActive=l.SecondaryActive=l.BecomingActive=l.PrimaryInactive=l.SecondaryInactive=l.BecomingInactive=new Light.Function(LightingFunction.Constant,30,0,intensity,0);return l;}
            Lights.Add(Constant(lit));Lights.Add(Constant(shade));
            if(rebuildRegions) {
                var receivers=new HashSet<short>(selected);
                if(!RebuildLightingRegions(ref receivers,out _,out reason,plans,brightId,darkId))return false;
                if(receivers.Count>budget){reason="Rebuilt lighting exceeds the polygon budget.";return false;}
                BakeSurfaceLighting(receivers,source,sourceZ,brightId,darkId);
                LastLightingRegionScope=receivers.OrderBy(i=>i).ToArray();
                return true;
            }
            int firstNewPolygon=Polygons.Count;
            foreach(var plan in plans) {
                if(plan.Value.Count==1){Polygons[plan.Key].FloorLight=plan.Value[0].Shadow?darkId:brightId;continue;}
                if(!RebuildSplitPolygon(plan.Key,original=>{
                    foreach(var piece in plan.Value){if(!CreatePlainShape(piece.Points,out string failure))return failure;var p=Polygons[Polygons.Count-1];CopyBoxSettings(original,p);p.FloorLight=piece.Shadow?darkId:brightId;}return null;
                },out reason)){reason=$"Polygon {plan.Key}, building shadow pieces: {reason}";return false;}
            }
            if(Polygons.Count!=firstNewPolygon) {
                var before=new Level();before.Load(Save().Clone());
                if(!RepairReflection(before,new HashSet<short>(Enumerable.Range(0,Endpoints.Count).Select(i=>(short)i)),out reason))return false;
            }
            var wallReceivers=new HashSet<short>(selected);
            for(int i=firstNewPolygon;i<Polygons.Count;i++)wallReceivers.Add((short)i);
            BakeSurfaceLighting(wallReceivers,source,sourceZ,brightId,darkId);
            return true;
        }
    }

    public partial class Editor
    {
        Level lightingSourceLevel;
        Wadfile.DirectoryEntry lightingOriginal,lightingLastResult;
        short[] lightingOriginalScope,lightingLastScope;
        bool lightingWholeMap;
        static bool SameLightingSnapshot(Wadfile.DirectoryEntry a,Wadfile.DirectoryEntry b) =>
            a!=null && b!=null && a.Chunks.Count==b.Chunks.Count &&
            a.Chunks.All(pair=>b.Chunks.TryGetValue(pair.Key,out var bytes) && pair.Value.SequenceEqual(bytes));
        public bool PlacingBakeLight;
        public bool BakeWholeMap=true;
        public bool RebuildLightingGeometry;
        public bool AllowCoarseRebuild;
        short[] LightingReceivers() => BakeWholeMap
            ? Enumerable.Range(0,Level.Polygons.Count).Select(i=>(short)i).ToArray()
            : SelectedLuaPolygons();
        bool bakeClickConsumed;
        bool draggingBakeLight;
        public double BakePreviewHeight=768;
        public string BakePreviewMessage;
        public string BakeResultMessage;
        public void UpdateLightingPreview() {
            BakePreviewMessage=HasBakeLight ? null : "Click and drag inside the map to place the light.";
            LightingPreview=HasBakeLight ? Level.PreviewFloorLighting(LightingReceivers(),BakeLightPosition,BakePreviewHeight,out BakePreviewMessage) : new List<LightingPiece>();
            Dirty=RedrawAll=true;
        }
        public Point BakeLightPosition;
        public bool HasBakeLight;
        public List<LightingPiece> LightingPreview=new List<LightingPiece>();
        public void ClearLightingPreview(){LightingPreview.Clear();Dirty=RedrawAll=true;}
        public bool BakeSelectedLighting(double height,double lit,double shade,int budget,double area,bool apply,out string reason) {
            if(!apply) {BakePreviewHeight=height;UpdateLightingPreview();reason=BakePreviewMessage ?? "Live shadow preview. Bake will attempt to build matching geometry.";return HasBakeLight && BakePreviewMessage==null;}
            PrepareHistoryNavigation();ClearLightingPreview();
            if(!HasBakeLight){reason="Click inside the map with the Bake lighting tool to place the light.";return false;}
            var selected=LightingReceivers();
            var currentSnapshot=Level.Save().Clone();
            bool replacePrevious=ReferenceEquals(lightingSourceLevel,Level) && lightingWholeMap==BakeWholeMap &&
                (BakeWholeMap || (lightingLastScope!=null && selected.SequenceEqual(lightingLastScope))) &&
                SameLightingSnapshot(currentSnapshot,lightingLastResult);
            var sourceSnapshot=replacePrevious ? lightingOriginal.Clone() : currentSnapshot;
            if(replacePrevious)selected=(short[])lightingOriginalScope.Clone();
            var originalScope=(short[])selected.Clone();
            if(selected.Length==0){reason="Select the receiving polygons first.";return false;}
            if(BakeWholeMap)budget=Math.Max(budget,selected.Length);
            var basis=new Level();basis.Load(sourceSnapshot.Clone());
            if(!basis.NormalizeLightingEdgeOrder(out reason))return false;
            string rebuildNotice="";
            bool rebuild=RebuildLightingGeometry;
            if(rebuild && (basis.Polygons.Any(p=>!string.IsNullOrWhiteSpace(p.LuaTriggerCode)) ||
                basis.Chunks.ContainsKey(Wadfile.Chunk("LUAS")) || basis.Chunks.ContainsKey(Wadfile.Chunk("term")))) {
                rebuild=false;rebuildNotice=" Geometry rebuilding skipped: this map contains scripts or terminals that may reference polygon IDs.";
            }

            if(budget<selected.Length){reason=$"The polygon budget ({budget}) is smaller than the selection ({selected.Length}). Increase it to at least {selected.Length}.";return false;}
            int baseCount=basis.Polygons.Count;
            var trial=new Level();trial.Load(basis.Save().Clone());
            bool approximate=false;int usedRefinement=64;string exactFailure=null,lastRetryFailure=null;
            if(!trial.BakeFloorLighting(selected,BakeLightPosition,height,lit,shade,budget,area,out var preview,out reason,false,64,rebuild)) {
                if(RebuildLightingGeometry && !AllowCoarseRebuild) {
                    reason="Rebuild stopped: "+reason+" No changes applied. The remaining fallback uses coarse sampling triangles; enable Allow coarse approximation only if that result is acceptable.";
                    return false;
                }
                // Discard any partial exact construction before retrying.
                exactFailure=reason;approximate=true;
                bool built=false;
                // Retry from the untouched map, reducing detail before adding too
                // many junctions to a shared boundary. Never relax topology checks.
                foreach(int refinement in new[]{64,32,16,8,4,1}) {
                    trial=new Level();trial.Load(basis.Save().Clone());
                    if(trial.BakeFloorLighting(selected,BakeLightPosition,height,lit,shade,budget,area,out preview,out reason,true,refinement,rebuild)) {built=true;usedRefinement=refinement;break;}
                    lastRetryFailure=$"{refinement}-piece approximation: {reason}";
                    // Retry all geometry failures: budget, thin fragments, rounded
                    // intersections and crossing edges can all improve with less detail.
                    // Each attempt starts from the original, never a partial bake.
                }
                if(!built){reason=$"No safe bake could be built. {reason ?? exactFailure ?? "Unknown geometry error."} No changes were applied.";return false;}
            }
            string detail="";
            if(approximate) {
                string cause=exactFailure!=null && exactFailure.Contains("map-coordinate precision")
                    ? "shadow edges required coordinate rounding"
                    : exactFailure!=null && (exactFailure.Contains("vertex") || exactFailure.Contains("eight-"))
                        ? "shared edges reached the vertex limit"
                        : exactFailure!=null && exactFailure.Contains("budget")
                            ? "full shadow detail exceeded the polygon budget"
                            : exactFailure ?? "No geometry diagnostic was returned";
                detail=(usedRefinement==1 || trial.Polygons.Count==baseCount
                    ? " Simplified: one light level per selected floor; no new shadow boundaries."
                    : " Applied approximate shadow geometry.")+" Fallback reason: "+cause+".";
                if(lastRetryFailure!=null)detail+=" Last rejected attempt: "+lastRetryFailure;
            }
            int generated=trial.Polygons.Count-baseCount;
            var finalScope=new HashSet<short>(rebuild ? trial.LastLightingRegionScope : selected);
            if(!rebuild)for(int i=baseCount;i<trial.Polygons.Count;i++)finalScope.Add((short)i);
            SetUndo("Bake lighting");Level.Load(trial.Save().Clone());Selection.Clear();Selection.Polygons.UnionWith(finalScope);
            Changed=Dirty=RedrawAll=true;CommitHistory();reason=$"Bake complete: {selected.Length} receiving polygons, {Math.Max(0,generated)} added and {Math.Max(0,-generated)} removed polygons. Floors, ceilings and existing walls shaded. Undo restores the original map."+detail+rebuildNotice;
            lightingSourceLevel=Level;lightingOriginal=sourceSnapshot.Clone();lightingLastResult=Level.Save().Clone();
            lightingOriginalScope=originalScope;lightingLastScope=finalScope.OrderBy(i=>i).ToArray();lightingWholeMap=BakeWholeMap;
            if(replacePrevious)reason+=" Replaced the previous bake from the original geometry; lit and shadowed regions recalculated.";
            if(rebuild)reason+=$" Region reconstruction replaced the original internal geometry; {finalScope.Count} resulting polygons.";
            return true;
        }
    }
}
