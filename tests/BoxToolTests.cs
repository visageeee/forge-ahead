using System;
using System.Linq;
using Weland;

namespace Weland
{
    // The editor settings normally live on the GTK application entry point.
    public static class Weland
    {
        public static Settings Settings = new Settings();
    }
}

class BoxToolTests
{
    static Point P(int x, int y) => new Point((short)x, (short)y);
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    static void Box(Level level, int x0, int y0, int x1, int y1)
    {
        Check(level.CreateBox(P(x0, y0), P(x1, y1), out string reason), reason);
        Validate(level);
    }
    static string Snapshot(Level level) => string.Join("|", level.Save().Chunks.OrderBy(p => p.Key)
        .Select(p => p.Key + ":" + Convert.ToBase64String(p.Value)));
    static string CanonicalSnapshot(Level level)
    {
        var loaded = new Level(); loaded.Load(level.Save().Clone());
        return Snapshot(loaded);
    }
    static void Reject(Level level, int x0, int y0, int x1, int y1)
    {
        string before = Snapshot(level);
        Check(!level.CreateBox(P(x0, y0), P(x1, y1), out string reason), "Invalid box accepted");
        Check(!string.IsNullOrEmpty(reason), "Missing rejection explanation");
        Check(Snapshot(level) == before, "Rejected box changed the map");
    }
    static void Validate(Level level)
    {
        Check(level.Endpoints.Distinct().Count() == level.Endpoints.Count, "Duplicate vertex");
        var edges = level.Lines.Select(l => string.Join(",", l.EndpointIndexes.OrderBy(i => i)));
        Check(edges.Distinct().Count() == level.Lines.Count, "Duplicate line");
        for (int i = 0; i < level.Polygons.Count; ++i)
        {
            Polygon polygon = level.Polygons[i];
            Check(polygon.VertexCount >= 3 && polygon.VertexCount <= 8, "Bad vertex count");
            Check(!polygon.Concave, "Box or neighbor became concave");
            for (int j = 0; j < polygon.VertexCount; ++j)
            {
                short point = polygon.EndpointIndexes[j];
                Line line = level.Lines[polygon.LineIndexes[j]];
                Check(line.EndpointIndexes.Contains(point), "Polygon endpoint does not match line");
                Check(line.EndpointIndexes.Contains(polygon.EndpointIndexes[(j + polygon.VertexCount - 1) % polygon.VertexCount]), "Broken polygon ring");
                Check(line.ClockwisePolygonOwner == i || line.CounterclockwisePolygonOwner == i, "Missing line owner");
                Check(level.EndpointPolygons[point].Contains(polygon), "Missing endpoint polygon cache");
            }
        }
        foreach (Line line in level.Lines)
            foreach (short point in line.EndpointIndexes)
                Check(level.EndpointLines[point].Contains(line), "Missing endpoint line cache");
    }
    static Editor Editor(Level level) => new Editor {
        Level = level, Grid = new Grid { Visible = false }, Selection = new Selection(), Scale = 1,
        DefaultSnapDistance = 8, Tool = Tool.Box
    };

    static void Main()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        int passed = 0;
        void Test(string name, Action action) { action(); Console.WriteLine("PASS " + name); ++passed; }
        Test("all drag directions", () => {
            foreach (var pair in new[] { new[] {0,0,100,100}, new[] {100,100,0,0}, new[] {100,0,0,100}, new[] {0,100,100,0} }) {
                var level = new Level(); Box(level, pair[0],pair[1],pair[2],pair[3]);
                Check(level.Endpoints.Count == 4 && level.Lines.Count == 4, "Bad box counts");
            }
        });
        Test("full shared edge", () => {
            var level = new Level(); Box(level,0,0,100,100); Box(level,100,0,200,100);
            Check(level.Endpoints.Count == 6 && level.Lines.Count == 7, "Shared geometry duplicated");
            Check(level.Lines.Count(l => l.ClockwisePolygonOwner >= 0 && l.CounterclockwisePolygonOwner >= 0) == 1, "No shared owner");
        });
        Test("partial shared edge and neighboring settings", () => {
            var level = new Level(); Box(level,0,0,100,100);
            level.Polygons[0].FloorHeight = 123; level.Polygons[0].CeilingLight = 5;
            Box(level,100,25,200,75);
            Check(level.Polygons[0].VertexCount == 6, "Wall did not split twice");
            Check(level.Polygons[1].FloorHeight == 123 && level.Polygons[1].CeilingLight == 5, "Settings not inherited");
            var loaded = new Level(); loaded.Load(level.Save().Clone()); Validate(loaded);
        });
        Test("splits on both wall orientations", () => {
            foreach (var pair in new[] { new[]{-100,25,0,75}, new[]{25,-100,75,0}, new[]{25,100,75,200} }) {
                var level = new Level(); Box(level,0,0,100,100); Box(level,pair[0],pair[1],pair[2],pair[3]);
            }
        });
        Test("shared boundary containing extra vertex", () => {
            var level = new Level(); Box(level,0,0,100,100);
            var wall = level.Lines.FindIndex(l => l.EndpointIndexes.All(i => level.Endpoints[i].X == 100));
            level.SplitLine((short)wall,100,50); Box(level,100,0,200,100);
            Check(level.Polygons[1].VertexCount == 5, "Intermediate endpoint omitted");
        });
        Test("reuse orphan vertex when splitting a wall", () => {
            var level = new Level(); Box(level,0,0,100,100); level.NewPoint(100,50);
            Box(level,100,0,200,100);
        });
        Test("overlap and containment rejected without mutations", () => {
            var level = new Level(); Box(level,0,0,100,100);
            Reject(level,0,0,100,100); Reject(level,50,50,150,150);
            Reject(level,-10,-10,110,110);
            Reject(level,0,0,0,100);
        });
        Test("crossing stray lines rejected", () => {
            foreach (var ends in new[] {new[]{-50,50,150,50}, new[]{-50,-50,150,150},new[]{50,50,150,150}}) {
                var level = new Level(); level.NewLine(level.NewPoint((short)ends[0],(short)ends[1]),level.NewPoint((short)ends[2],(short)ends[3]));
                Reject(level,0,0,100,100);
            }
        });
        Test("boundary lines reused and exterior corner contact split", () => {
            var level = new Level(); level.NewLine(level.NewPoint(-50,0),level.NewPoint(150,0));
            Box(level,0,0,100,100);
            Check(level.Lines.Count == 6, "Partial existing line not reused");
            level = new Level(); level.NewLine(level.NewPoint(-50,50),level.NewPoint(50,-50)); Box(level,0,0,100,100);
        });
        Test("hidden geometry still blocks overlap", () => {
            var level = new Level(); Box(level,0,0,100,100);
            Level.Filter = p => false;
            try { Reject(level,10,10,90,90); Box(level,100,0,200,100); }
            finally { Level.Filter = p => true; }
        });
        Test("eight vertex limits checked before splitting", () => {
            var level = new Level(); Box(level,0,0,100,100);
            Box(level,100,10,200,20); Box(level,100,30,200,40);
            Check(level.Polygons[0].VertexCount == 8, "Wrong adjacent vertex count");
            Reject(level,100,50,200,60);
            level = new Level();
            for (short y = 10; y <= 50; y += 10) level.NewPoint(0,y);
            Reject(level,0,0,100,100);
        });
        Test("drag preview, snapping, undo and redo", () => {
            var level = new Level(); Box(level,0,0,100,100); string before = Snapshot(level);
            var editor = Editor(level); editor.ButtonPress(103,3,EditorModifiers.None);
            editor.Motion(200,100,EditorModifiers.None);
            Check(editor.BoxStart.Equals(P(100,0)), "Start did not snap");
            Check(Snapshot(level) == before, "Preview changed geometry");
            editor.ButtonRelease(200,100,EditorModifiers.None); Validate(level);
            string after = CanonicalSnapshot(level); editor.Undo();
            Check(level.Polygons.Count == 1 && level.Endpoints.Count == 4 && level.Lines.Count == 4, "Undo failed");
            Validate(level);
            editor.Undo(); Check(CanonicalSnapshot(level) == after, "Redo failed");
        });
        Test("grid and derived-corner snapping", () => {
            var level = new Level(); level.NewPoint(200,0);
            var editor = Editor(level); editor.StartBox(0,0); editor.FinishBox(204,100);
            Check(editor.BoxEnd.X == 200, "Derived corner did not snap"); Validate(level);
            level = new Level(); editor = Editor(level);
            editor.Grid.Visible = true; editor.Grid.Resolution = 100; editor.Grid.UseCustomGrid = false;
            editor.StartBox(2,3); editor.FinishBox(197,201);
            Check(editor.BoxStart.Equals(P(0,0)) && editor.BoxEnd.Equals(P(200,200)), "Grid snapping failed");
        });
        Test("cancel and failed drag preserve existing undo", () => {
            var level = new Level(); var editor = Editor(level);
            editor.StartBox(0,0); editor.FinishBox(100,100);
            string before = Snapshot(level);
            editor.StartBox(100,0); editor.UpdateBox(200,100); editor.CancelBox();
            Check(Snapshot(level) == before && !editor.DrawingBox, "Cancel changed geometry");
            editor.StartBox(50,50); editor.FinishBox(150,150);
            Check(Snapshot(level) == before && editor.BoxMessage != null, "Failed drag changed map");
            editor.Undo(); Check(level.Polygons.Count == 0 && level.Endpoints.Count == 0, "Failed drag replaced undo");
        });
        Test("partial wall split is one undo action", () => {
            var level = new Level(); Box(level,0,0,100,100);
            var editor = Editor(level); editor.StartBox(100,25); editor.FinishBox(200,75);
            Validate(level); Check(level.Polygons[0].VertexCount == 6, "Missing wall splits");
            string after = CanonicalSnapshot(level); editor.Undo(); Validate(level);
            Check(level.Polygons.Count == 1 && level.Polygons[0].VertexCount == 4 && level.Lines.Count == 4, "Undo left split geometry behind");
            editor.Undo(); Validate(level); Check(CanonicalSnapshot(level) == after, "Split redo failed");
        });
        Test("textured wall split preserves remaining sides", () => {
            var level = new Level(); Box(level,0,0,100,100);
            int wall = level.Lines.FindIndex(l => l.EndpointIndexes.All(i => level.Endpoints[i].X == 100));
            var side = new Side(); side.Primary.Texture = (ShapeDescriptor)1; side.Primary.X = 11;
            side.LineIndex = (short)wall; side.PolygonIndex = 0;
            level.Sides.Add(side); level.Lines[wall].ClockwisePolygonSideIndex = 0;
            Box(level,100,25,200,75);
            Check(level.Sides.Count == 3, "Wall sides lost during splitting");
            var loaded = new Level(); loaded.Load(level.Save().Clone()); Validate(loaded);
        });
        Test("large coordinate ranges and narrow boxes", () => {
            var level = new Level(); Box(level,-30000,-30000,30000,30000);
            level = new Level(); Box(level,0,0,1,100); Reject(level,0,1,1,99);
        });
        Test("selection box in both drag directions", () => {
            foreach (bool reverse in new[] {false,true}) {
                var level = new Level(); Box(level,0,0,100,100); Box(level,200,0,300,100);
                var editor = Editor(level); editor.Tool = Tool.Select; string before = Snapshot(level);
                editor.ButtonPress((short)(reverse?120:-20),(short)(reverse?120:-20),EditorModifiers.None);
                editor.Motion((short)(reverse?-20:120),(short)(reverse?-20:120),EditorModifiers.None);
                Check(editor.DrawingSelectionBox, "Missing selection preview");
                editor.ButtonRelease((short)(reverse?-20:120),(short)(reverse?-20:120),EditorModifiers.None);
                Check(editor.Selection.Points.Count == 4 && editor.Selection.Lines.Count == 4, "Wrong enclosed selection");
                Check(Snapshot(level) == before && !editor.Changed && !editor.DrawingSelectionBox, "Selection changed map");
            }
        });
        Test("only completely enclosed lines selected", () => {
            var level = new Level(); level.NewLine(level.NewPoint(0,0),level.NewPoint(100,0));
            var editor = Editor(level); editor.Tool = Tool.Select;
            editor.ButtonPress(-20,-20,EditorModifiers.None); editor.ButtonRelease(50,20,EditorModifiers.None);
            Check(editor.Selection.Points.SetEquals(new short[]{0}) && editor.Selection.Lines.Count == 0, "Crossing line selected");
        });
        Test("shift extends a box and toggles items", () => {
            var level = new Level(); Box(level,0,0,100,100); Box(level,200,0,300,100);
            var editor = Editor(level); editor.Tool = Tool.Select;
            editor.ButtonPress(-20,-20,EditorModifiers.None); editor.ButtonRelease(120,120,EditorModifiers.None);
            editor.ButtonPress(180,-20,EditorModifiers.Shift); editor.ButtonRelease(320,120,EditorModifiers.Shift);
            Check(editor.Selection.Points.Count == 8 && editor.Selection.Lines.Count == 8, "Shift selection replaced old group");
            editor.ButtonPress(0,0,EditorModifiers.Shift); editor.ButtonRelease(0,0,EditorModifiers.Shift);
            Check(editor.Selection.Points.Count == 7, "Shift click did not toggle vertex");
            editor.ButtonPress(50,0,EditorModifiers.Shift); editor.ButtonRelease(50,0,EditorModifiers.Shift);
            Check(editor.Selection.Lines.Count == 7, "Shift click did not toggle line");
        });
        Test("group drag moves shared endpoints exactly once and undoes", () => {
            var level = new Level(); Box(level,0,0,100,100);
            var editor = Editor(level); editor.Tool = Tool.Select;
            editor.ButtonPress(-20,-20,EditorModifiers.None); editor.ButtonRelease(120,120,EditorModifiers.None);
            string before = CanonicalSnapshot(level);
            editor.ButtonPress(0,0,EditorModifiers.None); editor.Motion(20,30,EditorModifiers.None);
            editor.Motion(25,35,EditorModifiers.None); editor.ButtonRelease(25,35,EditorModifiers.None);
            Check(level.Endpoints.Contains(P(25,35)) && level.Endpoints.Contains(P(125,135)), "Group moved unevenly or more than once");
            editor.Undo(); Check(CanonicalSnapshot(level) == before && editor.Selection.Points.Count == 4, "Group undo lost geometry or selection");
            editor.Undo(); Check(level.Endpoints.Contains(P(25,35)), "Group redo failed");
        });
        Test("group nudge and line-only dragging", () => {
            var level = new Level(); Box(level,0,0,100,100);
            var editor = Editor(level); editor.Tool = Tool.Select;
            editor.ButtonPress(50,0,EditorModifiers.Shift); editor.ButtonRelease(50,0,EditorModifiers.Shift);
            Check(editor.Selection.Lines.Count == 1 && editor.Selection.Points.Count == 0, "Line-only selection failed");
            editor.ButtonPress(50,0,EditorModifiers.None); editor.Motion(50,20,EditorModifiers.None); editor.ButtonRelease(50,20,EditorModifiers.None);
            Check(level.Endpoints.Contains(P(0,20)) && level.Endpoints.Contains(P(100,20)), "Line group drag failed");
            editor.NudgeSelected(10,0); Check(level.Endpoints.Contains(P(10,20)), "Group nudge failed");
        });
        Test("group delete survives endpoint and line renumbering", () => {
            var level = new Level(); Box(level,0,0,100,100); Box(level,200,0,300,100);
            var editor = Editor(level); editor.Tool = Tool.Select;
            editor.ButtonPress(-20,-20,EditorModifiers.None); editor.ButtonRelease(120,120,EditorModifiers.None);
            editor.DeleteSelected(); Validate(level);
            Check(level.Polygons.Count == 1 && level.Endpoints.Count == 4 && !editor.Selection.HasMultipleGeometry, "Group deletion removed unrelated geometry");
            editor.Undo(); Validate(level); Check(level.Polygons.Count == 2 && editor.Selection.Points.Count == 4, "Deletion undo lost selection");
        });
        Test("cancel selection box restores earlier group", () => {
            var level = new Level(); Box(level,0,0,100,100); Box(level,200,0,300,100);
            var editor = Editor(level); editor.Tool = Tool.Select;
            editor.ButtonPress(-20,-20,EditorModifiers.None); editor.ButtonRelease(120,120,EditorModifiers.None);
            editor.ButtonPress(180,-20,EditorModifiers.None); editor.Motion(320,120,EditorModifiers.None);
            editor.CancelSelectionBox(); Check(editor.Selection.Points.SetEquals(new short[]{0,1,2,3}), "Cancel lost previous group");
            var copy = new Selection(); copy.CopyFrom(editor.Selection); editor.Selection.Clear();
            Check(copy.Points.Count == 4, "Selection snapshot shared sets");
        });
        Test("selection respects view filters", () => {
            var level = new Level(); Box(level,0,0,100,100);
            var editor = Editor(level); editor.Tool = Tool.Select;
            Level.Filter = p => false; Level.FilterPoints = true;
            try {
                editor.ButtonPress(-20,-20,EditorModifiers.None); editor.ButtonRelease(120,120,EditorModifiers.None);
                Check(!editor.Selection.HasMultipleGeometry, "Hidden geometry selected");
            } finally { Level.Filter = p => true; Level.FilterPoints = false; }
        });
        Test("nested box creates five covering polygons without duplicate geometry", () => {
            var level = new Level(); Box(level,0,0,1000,1000); Box(level,200,300,700,800);
            Check(level.Polygons.Count == 5 && level.Endpoints.Count == 12 && level.Lines.Count == 16, "Incorrect inset partition counts");
            Check(level.LastCreatedBoxPolygonIndex == 0 && level.Polygons[0].VertexCount == 4, "Center did not retain original index");
            long area = 0;
            foreach (Polygon polygon in level.Polygons) {
                long twice = 0;
                for(int i=0;i<polygon.VertexCount;i++) {
                    Point a=level.Endpoints[polygon.EndpointIndexes[i]], b=level.Endpoints[polygon.EndpointIndexes[(i+1)%polygon.VertexCount]];
                    twice += (long)a.X*b.Y-(long)a.Y*b.X;
                }
                area += Math.Abs(twice);
            }
            Check(area == 2000000, "Inset left a gap or overlap");
            var loaded = new Level(); loaded.Load(level.Save().Clone()); Validate(loaded);
        });
        Test("nested box retains settings and reassigns objects and notes", () => {
            var level = new Level(); Box(level,0,0,1000,1000);
            Polygon original=level.Polygons[0]; original.FloorHeight=123; original.CeilingLight=5;
            original.FloorOrigin=P(21,42); original.MediaIndex=2; original.AmbientSound=3;
            foreach(Point p in new[]{P(400,400),P(500,100),P(500,900),P(100,500),P(900,500)}) {
                level.NewObject(p,0); level.Annotations.Add(new Annotation{X=p.X,Y=p.Y,PolygonIndex=0,Text="keep"});
            }
            Box(level,200,300,700,800);
            Check(level.Objects.Count==5 && level.Annotations.Count==5, "Objects or notes deleted");
            Check(level.Objects[0].PolygonIndex==0 && level.Objects.Select(o=>o.PolygonIndex).Distinct().Count()==5, "Objects not assigned to their new regions");
            Check(level.Annotations.Select(o=>o.PolygonIndex).Distinct().Count()==5, "Notes not reassigned");
            foreach(Polygon polygon in level.Polygons)
                Check(polygon.FloorHeight==123 && polygon.CeilingLight==5 && polygon.FloorOrigin.Equals(P(21,42)) && polygon.MediaIndex==2 && polygon.AmbientSound==3, "Settings lost");
        });
        Test("nested box preserves neighboring indices and wall textures", () => {
            var level = new Level(); Box(level,0,0,1000,1000); Box(level,1000,0,2000,1000);
            int wall=level.Lines.FindIndex(l=>l.ClockwisePolygonOwner==0 && l.CounterclockwisePolygonOwner==1);
            Check(wall>=0,"Missing initial shared wall");
            var inside = new Side{LineIndex=(short)wall,PolygonIndex=0}; inside.Primary.Texture=(ShapeDescriptor)1;inside.Primary.X=10;
            var outside = new Side{LineIndex=(short)wall,PolygonIndex=1}; outside.Primary.Texture=(ShapeDescriptor)2;outside.Primary.X=20;
            level.Sides.Add(inside);level.Sides.Add(outside);
            level.Lines[wall].ClockwisePolygonSideIndex=0;level.Lines[wall].CounterclockwisePolygonSideIndex=1;
            var neighbor=level.Polygons[1]; neighbor.Type=PolygonType.Teleporter; neighbor.Permutation=0;
            Box(level,200,300,700,800);
            Check(level.Polygons[1]==neighbor && neighbor.Permutation==0,"Neighbor reference changed");
            Check(level.Sides.Count==6,"Split wall lost textures");
            Check(level.Sides.Count(side=>side.Primary.Texture.Equals((ShapeDescriptor)1))==3 && level.Sides.Count(side=>side.Primary.Texture.Equals((ShapeDescriptor)2))==3,"Textures not cloned on both sides");
            foreach(Line line in level.Lines.Where(l=>l.EndpointIndexes.All(i=>level.Endpoints[i].X==1000)))
                Check(line.ClockwisePolygonOwner>=0 && line.CounterclockwisePolygonOwner==1,"Broken outside connection");
            var loaded=new Level();loaded.Load(level.Save().Clone());Validate(loaded);
        });
        Test("inset is one undo action and center remains selected", () => {
            var level=new Level();Box(level,0,0,1000,1000);string before=CanonicalSnapshot(level);
            var editor=Editor(level);editor.StartBox(200,300);editor.UpdateBox(700,800);
            Check(level.Polygons.Count==1,"Preview modified map");
            editor.FinishBox(700,800);Check(editor.Selection.Polygon==0,"Wrong inset selected");
            string after=CanonicalSnapshot(level);editor.Undo();Check(CanonicalSnapshot(level)==before,"Inset undo failed");
            editor.Undo();Check(CanonicalSnapshot(level)==after,"Inset redo failed");Validate(level);
        });
        Test("inset limits and existing internal geometry reject atomically", () => {
            var level=new Level();Box(level,0,0,1000,1000);Box(level,1000,100,1200,200);Box(level,1000,400,1200,500);
            Reject(level,200,600,700,800);
            level=new Level();Box(level,0,0,1000,1000);level.NewLine(level.NewPoint(400,100),level.NewPoint(500,200));
            Reject(level,200,300,700,800);
            level=new Level();Box(level,0,0,1000,1000);level.Polygons[0].Type=PolygonType.Platform;
            Reject(level,200,300,700,800);
        });
        Test("insets can be nested repeatedly", () => {
            var level=new Level();Box(level,0,0,1000,1000);Box(level,200,300,700,800);Box(level,300,400,600,700);
            Check(level.Polygons.Count==9 && level.LastCreatedBoxPolygonIndex==0,"Second inset failed");
            var loaded=new Level();loaded.Load(level.Save().Clone());Validate(loaded);
        });
        Test("circle sides three through eight and reverse drags", () => {
            for(int sides=3;sides<=8;sides++) foreach(bool reverse in new[]{false,true}) {
                var level=new Level();var editor=Editor(level);editor.Tool=Tool.Circle;editor.CircleSides=sides;
                editor.ButtonPress((short)(reverse?1000:0),(short)(reverse?1000:0),EditorModifiers.None);
                editor.ButtonRelease((short)(reverse?0:1000),(short)(reverse?0:1000),EditorModifiers.None);
                Check(level.Polygons.Count==1 && level.Polygons[0].VertexCount==sides,"Wrong side count");Validate(level);
            }
        });
        Test("circle preview, cancel, ellipse and Shift constraint", () => {
            var level=new Level();var editor=Editor(level);editor.Tool=Tool.Circle;editor.CircleSides=8;
            editor.ButtonPress(0,0,EditorModifiers.None);editor.Motion(1000,500,EditorModifiers.None);
            Check(level.Endpoints.Count==0 && editor.DrawingCircle,"Preview changed map");
            Check(editor.CircleVertices.Max(p=>p.Y)==500,"Ellipse bounds wrong");
            editor.CancelCircle();Check(!editor.DrawingCircle && level.Polygons.Count==0,"Cancel failed");
            editor.ButtonPress(0,0,EditorModifiers.None);editor.ButtonRelease(1000,500,EditorModifiers.Shift);
            Check(level.Endpoints.Max(p=>p.Y)==1000 && level.Endpoints.Max(p=>p.X)==1000,"Shift did not constrain circle");Validate(level);
        });
        Test("adjacent circles reuse their diagonal shared edge", () => {
            var level=new Level();var editor=Editor(level);editor.Tool=Tool.Circle;editor.CircleSides=4;
            editor.StartCircle(0,0);editor.FinishCircle(1000,1000,false);
            editor.StartCircle(500,-500);editor.FinishCircle(1500,500,false);Validate(level);
            Check(level.Polygons.Count==2 && level.Endpoints.Count==6 && level.Lines.Count==7,"Shared diagonal duplicated");
        });
        Test("circle corner snapping and undo", () => {
            var level=new Level();level.NewPoint(503,0);var editor=Editor(level);editor.Tool=Tool.Circle;
            editor.CircleSides=4;string before=CanonicalSnapshot(level);
            editor.StartCircle(0,0);editor.FinishCircle(1000,1000,false);Validate(level);
            Check(level.Endpoints.Count==4 && level.Endpoints.Contains(P(503,0)),"Circle did not reuse snapped endpoint");
            string after=CanonicalSnapshot(level);editor.Undo();Check(CanonicalSnapshot(level)==before,"Circle undo failed");
            editor.Undo();Check(CanonicalSnapshot(level)==after,"Circle redo failed");
        });
        Test("circle overlap and crossing lines are rejected", () => {
            var level=new Level();Box(level,0,0,1000,1000);var editor=Editor(level);editor.Tool=Tool.Circle;string before=Snapshot(level);
            editor.StartCircle(200,200);editor.FinishCircle(800,800,false);
            Check(Snapshot(level)==before && editor.CircleMessage!=null,"Inset circle unexpectedly accepted");
            level=new Level();level.NewLine(level.NewPoint(-100,500),level.NewPoint(1100,500));editor=Editor(level);editor.Tool=Tool.Circle;before=Snapshot(level);
            editor.StartCircle(0,0);editor.FinishCircle(1000,1000,false);
            Check(Snapshot(level)==before && editor.CircleMessage!=null,"Circle crossed existing line");
        });
        Test("circle partial shared diagonal wall splitting", () => {
            var level=new Level();var editor=Editor(level);editor.Tool=Tool.Circle;editor.CircleSides=4;
            editor.StartCircle(0,0);editor.FinishCircle(1000,1000,false);
            var triangle=new System.Collections.Generic.List<Point>{P(900,400),P(600,100),P(1000,0)};
            Check(level.CreateCirclePolygon(triangle,out string reason),reason);Validate(level);
            Check(level.Polygons[0].VertexCount==6,"Shared diagonal did not split");
        });
        Test("circle rejects degenerate and too complex boundaries atomically", () => {
            var level=new Level();var editor=Editor(level);editor.Tool=Tool.Circle;
            editor.StartCircle(0,0);editor.FinishCircle(0,1000,false);Check(level.Polygons.Count==0,"Degenerate circle created");
            editor.CircleSides=4;editor.StartCircle(0,0);editor.UpdateCircle(1000,1000,false);
            var corners=new System.Collections.Generic.List<Point>(editor.CircleVertices);
            for(short x=550;x<=750;x+=50)level.NewPoint(x,(short)(x-500));
            string before=Snapshot(level);Check(!level.CreateCirclePolygon(corners,out string reason),"Polygon limit bypassed");
            Check(Snapshot(level)==before,"Failed circle mutated map");
            editor.CircleSides=100;Check(editor.CircleSides==8,"Sides not clamped");editor.CircleSides=1;Check(editor.CircleSides==3,"Minimum sides not clamped");
        });
        Test("Move translates selected vertex by drag displacement", () => {
            var level=new Level();Box(level,0,0,1000,1000);var editor=Editor(level);editor.Selection.Point=0;
            editor.Tool=Tool.Move;Check(editor.Selection.Point==0,"Switching to Move cleared selection");
            editor.ButtonPress(500,500,EditorModifiers.None);editor.Motion(520,530,EditorModifiers.None);editor.ButtonRelease(525,535,EditorModifiers.None);
            Check(level.Endpoints[0].Equals(P(25,35)),"Move jumped to cursor instead of using displacement");
            editor.Undo();Check(level.Endpoints[0].Equals(P(0,0)),"Vertex Move undo failed");
        });
        Test("Move handles selected group and Pan leaves geometry unchanged", () => {
            var level=new Level();Box(level,0,0,100,100);var editor=Editor(level);editor.Tool=Tool.Select;
            editor.ButtonPress(-20,-20,EditorModifiers.None);editor.ButtonRelease(120,120,EditorModifiers.None);
            editor.Tool=Tool.Move;Check(editor.Selection.Points.Count==4,"Move dropped group selection");
            editor.ButtonPress(500,500,EditorModifiers.None);editor.Motion(510,520,EditorModifiers.None);editor.ButtonRelease(510,520,EditorModifiers.None);
            Check(level.Endpoints.Contains(P(10,20)) && level.Endpoints.Contains(P(110,120)),"Group Move failed");
            string before=Snapshot(level);editor.Tool=Tool.Pan;
            editor.ButtonPress(0,0,EditorModifiers.None);editor.Motion(200,200,EditorModifiers.None);editor.ButtonRelease(200,200,EditorModifiers.None);
            Check(Snapshot(level)==before && editor.Selection.Points.Count==4,"Pan changed geometry or selection");
            editor.Tool=Tool.Move;editor.Undo();Check(level.Endpoints.Contains(P(0,0)) && editor.Selection.Points.Count==4,"Group Move undo failed");
        });
        Test("Move translates a polygon with its objects and annotations", () => {
            var level=new Level();Box(level,0,0,1000,1000);level.NewObject(P(200,300),0);
            level.Annotations.Add(new Annotation{X=400,Y=500,PolygonIndex=0,Text="keep"});
            var editor=Editor(level);editor.Selection.Polygon=0;editor.Tool=Tool.Move;
            editor.ButtonPress(1500,1500,EditorModifiers.None);editor.ButtonRelease(1600,1700,EditorModifiers.None);
            Check(level.Endpoints.Contains(P(100,200)) && level.Objects[0].X==300 && level.Objects[0].Y==500,"Polygon objects not moved together");
            Check(level.Annotations[0].X==500 && level.Annotations[0].Y==700,"Polygon notes not moved");Validate(level);
        });
        Test("Move objects across polygon boundaries without moving geometry", () => {
            var level=new Level();Box(level,0,0,1000,1000);Box(level,1000,0,2000,1000);level.NewObject(P(500,500),0);
            var editor=Editor(level);editor.Selection.Object=0;editor.Tool=Tool.Move;
            editor.ButtonPress(3000,3000,EditorModifiers.None);editor.ButtonRelease(4000,3000,EditorModifiers.None);
            Check(level.Objects[0].X==1500 && level.Objects[0].PolygonIndex==1 && level.Endpoints.Contains(P(0,0)),"Object Move failed");
            editor.Undo();Check(level.Objects[0].X==500 && level.Objects[0].PolygonIndex==0,"Object Move undo failed");
        });
        Test("Move selected line, annotations and no-selection drags", () => {
            var level=new Level();Box(level,0,0,1000,1000);var editor=Editor(level);editor.Selection.Line=0;editor.Tool=Tool.Move;
            editor.ButtonPress(3000,3000,EditorModifiers.None);editor.ButtonRelease(3020,3030,EditorModifiers.None);
            Check(level.Endpoints.Contains(P(20,30)) && level.Endpoints.Contains(P(1020,30)),"Line Move failed");
            level=new Level();Box(level,0,0,1000,1000);level.Annotations.Add(new Annotation{X=200,Y=300,PolygonIndex=0,Text="note"});
            editor=Editor(level);editor.Selection.Annotation=0;editor.Tool=Tool.Move;
            editor.ButtonPress(1500,1500,EditorModifiers.None);editor.ButtonRelease(1600,1600,EditorModifiers.None);
            Check(level.Annotations[0].X==300 && level.Annotations[0].Y==400,"Annotation Move failed");
            editor.Selection.Clear();string before=Snapshot(level);editor.Changed=false;
            editor.ButtonPress(0,0,EditorModifiers.None);editor.ButtonRelease(100,100,EditorModifiers.None);
            Check(Snapshot(level)==before && !editor.Changed,"Empty Move changed map");
        });
        Test("right drag moves selection with any active tool and ends on release", () => {
            foreach (Tool tool in new[] {Tool.Select, Tool.Pan, Tool.Zoom, Tool.Box, Tool.Circle, Tool.FloorHeight}) {
                var level=new Level();Box(level,0,0,1000,1000);var editor=Editor(level);editor.Tool=tool;editor.Selection.Point=0;
                editor.ButtonPress(500,500,EditorModifiers.RightClick);
                editor.Motion(520,530,EditorModifiers.RightClick);
                editor.ButtonRelease(525,535,EditorModifiers.RightClick);
                Check(editor.Tool==tool && editor.Selection.Point==0 && level.Endpoints[0].Equals(P(25,35)),"Right drag changed tool/selection or failed to move");
                editor.Undo();Check(level.Endpoints[0].Equals(P(0,0)),"Right drag did not undo as one action");
                Check(!editor.DrawingBox && !editor.DrawingCircle,"Right drag started drawing");
            }
        });
        Test("right drag moves selected objects and empty drags do nothing", () => {
            var level=new Level();Box(level,0,0,1000,1000);level.NewObject(P(200,300),0);
            var editor=Editor(level);editor.Tool=Tool.Select;editor.Selection.Object=0;
            editor.ButtonPress(1500,1500,EditorModifiers.RightClick);editor.ButtonRelease(1600,1600,EditorModifiers.RightClick);
            Check(level.Objects[0].X==300 && level.Objects[0].Y==400 && editor.Selection.Object==0,"Right drag object failed");
            editor.Tool=Tool.Box;editor.Selection.Clear();string before=Snapshot(level);
            editor.ButtonPress(1500,1500,EditorModifiers.RightClick);editor.Motion(1700,1700,EditorModifiers.RightClick);editor.ButtonRelease(1700,1700,EditorModifiers.RightClick);
            Check(Snapshot(level)==before && !editor.DrawingBox,"Empty right drag created geometry");
            editor.ButtonPress(1500,1500,EditorModifiers.None);editor.ButtonRelease(1700,1700,EditorModifiers.None);
            Check(level.Polygons.Count==2,"Right release left move gesture active");
        });
        Console.WriteLine(passed + " test groups passed.");
    }
}
