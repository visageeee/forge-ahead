using System;
using System.Collections.Generic;
using System.Linq;

namespace Weland
{
    public partial class Editor
    {
        public void SelectAllGeometry()
        {
            PrepareHistoryNavigation(); Selection.Clear();
            for (int i = 0; i < Level.Endpoints.Count; ++i) Selection.Points.Add((short)i);
            for (int i = 0; i < Level.Lines.Count; ++i) Selection.Lines.Add((short)i);
            for (int i = 0; i < Level.Polygons.Count; ++i) Selection.Polygons.Add((short)i);
            Dirty = RedrawAll = true;
        }

        public void DeselectAllGeometry()
        {
            PrepareHistoryNavigation(); Selection.Clear(); Dirty = RedrawAll = true;
        }

        HashSet<short> HeightSelection()
        {
            var polygons = new HashSet<short>(Selection.Polygons);
            if (Selection.Polygon != -1) polygons.Add(Selection.Polygon);
            if (polygons.Count != 0) return polygons;
            var points = MultipleEndpoints();
            if (Selection.Point != -1) points.Add(Selection.Point);
            if (Selection.Line != -1) points.UnionWith(Level.Lines[Selection.Line].EndpointIndexes);
            var attached = new HashSet<Polygon>(points.SelectMany(i => Level.EndpointPolygons[i]));
            for (int i = 0; i < Level.Polygons.Count; ++i)
                if (attached.Contains(Level.Polygons[i])) polygons.Add((short)i);
            return polygons;
        }

        public short[] SelectedLuaPolygons() => Selection.Polygons.Concat(Selection.Polygon==-1 ? Array.Empty<short>() : new[]{Selection.Polygon}).Distinct().OrderBy(i=>i).ToArray();

        public bool SetSelectedPolygonLua(string code,bool enabled,bool once,out string reason)
        {
            PrepareHistoryNavigation();reason=null;var selected=SelectedLuaPolygons();
            if(selected.Length==0) { reason="Select at least one polygon first.";return false; }
            try { PolygonLua.Validate(code);if(enabled && string.IsNullOrWhiteSpace(code))throw new ArgumentException("Enter Lua code before enabling the trigger."); }
            catch(Exception ex) { reason=ex.Message;return false; }
            if(selected.All(i=>Level.Polygons[i].LuaTriggerCode==code && Level.Polygons[i].LuaTriggerEnabled==enabled && Level.Polygons[i].LuaTriggerOnce==once))return true;
            // Validate serialization and reserved script names before creating history.
            try {
                var trial=new Level();trial.Load(Level.Save().Clone());
                foreach(short i in selected) { var p=trial.Polygons[i];p.LuaTriggerCode=code;p.LuaTriggerEnabled=enabled;p.LuaTriggerOnce=once; }
                var saved=trial.Save().Clone();SetUndo("Edit polygon Lua triggers");Level.Load(saved);
            }
            catch(Exception ex) { reason=ex.Message;return false; }
            Changed=Dirty=RedrawAll=true;return true;
        }

        public bool ShiftSelectedHeights(int amount, bool floor, bool ceiling, out string reason)
        {
            PrepareHistoryNavigation(); reason = null;
            var selected = HeightSelection();
            if (selected.Count == 0) { reason = "Select polygons or their vertices/lines first."; return false; }
            if (!floor && !ceiling) { reason = "Choose Floor, Ceiling, or both."; return false; }
            if (amount == 0) return true;
            foreach (short index in selected)
            {
                var polygon = Level.Polygons[index];
                long f = polygon.FloorHeight + (floor ? (long)amount : 0), c = polygon.CeilingHeight + (ceiling ? (long)amount : 0);
                if (f < short.MinValue || f > short.MaxValue || c < short.MinValue || c > short.MaxValue)
                { reason = "The height shift exceeds the map height limits."; return false; }
                if (f > c) { reason = "The shifted floor would be above its ceiling."; return false; }
            }
            SetUndo("Shift selection heights");
            foreach (short index in selected)
            {
                var polygon = Level.Polygons[index];
                if (floor) polygon.FloorHeight = (short)(polygon.FloorHeight + amount);
                if (ceiling) polygon.CeilingHeight = (short)(polygon.CeilingHeight + amount);
            }
            Changed = Dirty = RedrawAll = true; CommitHistory(); return true;
        }

        public bool MirrorSelectedGeometry(bool horizontal, out string reason)
        {
            PrepareHistoryNavigation(); reason = null;
            BeginMoveTool(); CancelMoveTool();
            if (movePoints.Count < 2) { reason = "Select at least two vertices, a line, or a polygon."; return false; }
            int sum = horizontal
                ? movePoints.Min(i => Level.Endpoints[i].X) + movePoints.Max(i => Level.Endpoints[i].X)
                : movePoints.Min(i => Level.Endpoints[i].Y) + movePoints.Max(i => Level.Endpoints[i].Y);
            bool inRange = true;
            Point Mirror(Point p)
            {
                int x = horizontal ? sum - p.X : p.X, y = horizontal ? p.Y : sum - p.Y;
                if (x < short.MinValue || x > short.MaxValue || y < short.MinValue || y > short.MaxValue) inRange = false;
                return new Point(ClampCoordinate(x), ClampCoordinate(y));
            }
            var before = Level.Save().Clone();
            var trial = new Level(); trial.Load(before.Clone());
            foreach (short i in movePoints) trial.Endpoints[i] = Mirror(Level.Endpoints[i]);
            foreach (var obj in moveObjects)
            {
                var target = trial.Objects[Level.Objects.IndexOf(obj)]; var p = Mirror(new Point(obj.X,obj.Y));
                target.X = p.X; target.Y = p.Y;
                if (obj.Type == ObjectType.Player || obj.Type == ObjectType.Monster)
                    target.Facing = ((horizontal ? 180 - obj.Facing : -obj.Facing) % 360 + 360) % 360;
            }
            foreach (var note in moveNotes)
            {
                var target = trial.Annotations[Level.Annotations.IndexOf(note)]; var p = Mirror(new Point(note.X,note.Y));
                target.X = p.X; target.Y = p.Y;
            }
            if (!inRange) { reason = "The reflection exceeds map coordinate limits."; return false; }
            if (!trial.RepairReflection(Level, movePoints, out reason)) return false;
            var after = trial.Save().Clone();
            if (before.Chunks.Count == after.Chunks.Count && before.Chunks.All(pair =>
                after.Chunks.TryGetValue(pair.Key, out byte[] data) && pair.Value.SequenceEqual(data))) return true;
            SetUndo(horizontal ? "Mirror selection horizontally" : "Flip selection vertically");
            Level.Load(after); Changed = Dirty = RedrawAll = true; CommitHistory(); return true;
        }
    }

    public partial class Level
    {
        static long ReflectionCross(Point a, Point b, Point p) => (long)(b.X-a.X)*(p.Y-a.Y)-(long)(b.Y-a.Y)*(p.X-a.X);
        long ReflectionArea(Polygon p)
        {
            long area = 0;
            for (int i=0;i<p.VertexCount;i++) { var a=Endpoints[p.EndpointIndexes[i]];var b=Endpoints[p.EndpointIndexes[(i+1)%p.VertexCount]];area+=(long)a.X*b.Y-(long)b.X*a.Y; }
            return area;
        }

        public bool RepairReflection(Level original, HashSet<short> moved, out string reason)
        {
            reason = null;
            if (Endpoints.Distinct().Count() != Endpoints.Count)
            { reason = "The reflection would overlap existing vertices."; return false; }
            var affected = new HashSet<short>();
            for (int i=0;i<Polygons.Count;i++)
                if (Polygons[i].EndpointIndexes.Take(Polygons[i].VertexCount).Any(moved.Contains)) affected.Add((short)i);
            foreach (short index in affected)
            {
                var p=Polygons[index];long area=ReflectionArea(p);
                if (area==0) { reason="The reflection would collapse a polygon.";return false; }
                for(int edge=0;edge<p.VertexCount;edge++)for(int vertex=0;vertex<p.VertexCount;vertex++)
                    if (Math.Sign(ReflectionCross(Endpoints[p.EndpointIndexes[edge]],Endpoints[p.EndpointIndexes[(edge+1)%p.VertexCount]],Endpoints[p.EndpointIndexes[vertex]]))*Math.Sign(area)<0)
                    { reason="The reflection would fold an attached polygon. Select its neighbours too.";return false; }
                if(Math.Sign(area)!=Math.Sign(original.ReflectionArea(original.Polygons[index])))
                {
                    var endpoints=(short[])p.EndpointIndexes.Clone();var lines=(short[])p.LineIndexes.Clone();
                    // Line i joins endpoint i to the preceding endpoint.
                    for(int i=0;i<p.VertexCount;i++) {p.EndpointIndexes[i]=endpoints[(p.VertexCount-i)%p.VertexCount];p.LineIndexes[i]=lines[(p.VertexCount+1-i)%p.VertexCount];}
                }
            }
            var affectedLines=new HashSet<short>();
            for(int i=0;i<Lines.Count;i++)if(Lines[i].EndpointIndexes.Any(moved.Contains))affectedLines.Add((short)i);
            foreach(short i in affectedLines)
            {
                var line=Lines[i];Point a=Endpoints[line.EndpointIndexes[0]],b=Endpoints[line.EndpointIndexes[1]];
                if(a.Equals(b)) {reason="The reflection would collapse a line.";return false;}
                for(int j=0;j<Lines.Count;j++)
                {
                    if(j==i || (affectedLines.Contains((short)j)&&j<i))continue;
                    var other=Lines[j];Point c=Endpoints[other.EndpointIndexes[0]],d=Endpoints[other.EndpointIndexes[1]];
                    long abC=ReflectionCross(a,b,c),abD=ReflectionCross(a,b,d),cdA=ReflectionCross(c,d,a),cdB=ReflectionCross(c,d,b);
                    bool crossing=Math.Sign(abC)*Math.Sign(abD)<0 && Math.Sign(cdA)*Math.Sign(cdB)<0;
                    bool overlaps=abC==0 && abD==0 && (a.X!=b.X
                        ? Math.Max(Math.Min(a.X,b.X),Math.Min(c.X,d.X))<Math.Min(Math.Max(a.X,b.X),Math.Max(c.X,d.X))
                        : Math.Max(Math.Min(a.Y,b.Y),Math.Min(c.Y,d.Y))<Math.Min(Math.Max(a.Y,b.Y),Math.Max(c.Y,d.Y)));
                    bool touches=(abC==0 && OnSegment(c,a,b) && !c.Equals(a) && !c.Equals(b)) || (abD==0 && OnSegment(d,a,b) && !d.Equals(a) && !d.Equals(b)) ||
                        (cdA==0 && OnSegment(a,c,d) && !a.Equals(c) && !a.Equals(d)) || (cdB==0 && OnSegment(b,c,d) && !b.Equals(c) && !b.Equals(d));
                    if(crossing || overlaps || touches) {reason="The reflection would cross or overlap existing lines.";return false;}
                }
            }
            bool Separated(Polygon a,Polygon b)
            {
                int sign=Math.Sign(ReflectionArea(a));
                for(int i=0;i<a.VertexCount;i++)
                {
                    Point p=Endpoints[a.EndpointIndexes[i]],q=Endpoints[a.EndpointIndexes[(i+1)%a.VertexCount]];
                    if(b.EndpointIndexes.Take(b.VertexCount).All(v=>Math.Sign(ReflectionCross(p,q,Endpoints[v]))*sign<=0))return true;
                }
                return false;
            }
            foreach(short i in affected)for(int j=0;j<Polygons.Count;j++)
                if(i!=j && !(affected.Contains((short)j)&&j<i) && !Separated(Polygons[i],Polygons[j]) && !Separated(Polygons[j],Polygons[i]))
                {reason="The reflection would overlap another polygon.";return false;}
            // Transfer each wall side with its polygon when reflection reverses
            // winding. Rebuild ownership from the corrected polygon loops.
            var sides=new Dictionary<(short Line,short Polygon),short>();
            var polygonLines=new HashSet<short>(affected.SelectMany(i=>Polygons[i].LineIndexes.Take(Polygons[i].VertexCount)));
            foreach(short i in polygonLines)
            {
                var line=Lines[i];sides[(i,line.ClockwisePolygonOwner)]=line.ClockwisePolygonSideIndex;
                sides[(i,line.CounterclockwisePolygonOwner)]=line.CounterclockwisePolygonSideIndex;
                line.ClockwisePolygonOwner=line.CounterclockwisePolygonOwner=-1;
                line.ClockwisePolygonSideIndex=line.CounterclockwisePolygonSideIndex=-1;
            }
            for(int index=0;index<Polygons.Count;index++)
            {
                var polygon=Polygons[index];
                for(int edge=0;edge<polygon.VertexCount;edge++)
                {
                    short i=polygon.LineIndexes[edge];if(!polygonLines.Contains(i))continue;
                    var line=Lines[i];sides.TryGetValue((i,(short)index),out short side);
                    if(!sides.ContainsKey((i,(short)index)))side=-1;
                    if(line.EndpointIndexes[1]==polygon.EndpointIndexes[edge])
                    {
                        if(line.ClockwisePolygonOwner!=-1){reason="The reflection would invalidate shared polygon edges.";return false;}
                        line.ClockwisePolygonOwner=(short)index;line.ClockwisePolygonSideIndex=side;
                    }
                    else
                    {
                        if(line.CounterclockwisePolygonOwner!=-1){reason="The reflection would invalidate shared polygon edges.";return false;}
                        line.CounterclockwisePolygonOwner=(short)index;line.CounterclockwisePolygonSideIndex=side;
                    }
                }
            }
            UpdatePolygonWallReferences();return true;
        }
    }
}
