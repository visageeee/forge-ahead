using System;
using System.Collections.Generic;

namespace Weland
{
    public enum Tool
    {
        Select,
        Zoom,
        Pan,
        Line,
        Fill,
        Object,
        Annotation,
        FloorHeight,
        CeilingHeight,
        PolygonType,
        FloorLight,
        CeilingLight,
        MediaLight,
        Media,
        AmbientSound,
        RandomSound,
        FloorTexture,
        CeilingTexture,
        VisualMode,
        Box,
        Circle,
        Move,
        Rotate,
        Scale,
        Ring,
        BakeLighting,
        CameraPath
    }

    [Flags]
    public enum EditorModifiers
    {
        None,
        Shift = 0x01,
        Control = 0x02,
        Alt = 0x04,
        RightClick = 0x08
    }

    public class Grid
    {
        public bool Visible = true;
        public bool Snap = true;
        public short Resolution = 1024;

        /*** begin custom grid code ***/
        public double Rotation = 0;
        public Point Center = new Point(0, 0);
        public double Scale = 1;
        public bool UseCustomGrid = true;

        public int CurrentGrid = 1;

        public double MaxScale = 8.0;
        public double MinScale = 1.0 / 8.0;
        /*** end custom grid code ***/

    }

    public class Selection
    {
        public short Point = -1;
        public short Object = -1;
        public short Line = -1;
        public short Polygon = -1;
        public short Annotation = -1;
        public HashSet<short> Points = new HashSet<short>();
        public HashSet<short> Lines = new HashSet<short>();
        public HashSet<short> Polygons = new HashSet<short>();
        public bool HasMultipleGeometry => Points.Count != 0 || Lines.Count != 0 || Polygons.Count != 0;

        public void Clear()
        {
            Point = -1;
            Object = -1;
            Line = -1;
            Polygon = -1;
            Annotation = -1;
            Points.Clear();
            Lines.Clear();
            Polygons.Clear();
        }

        public void CopyFrom(Selection other)
        {
            Point = other.Point;
            Object = other.Object;
            Line = other.Line;
            Polygon = other.Polygon;
            Annotation = other.Annotation;
            Points = new HashSet<short>(other.Points);
            Lines = new HashSet<short>(other.Lines);
            Polygons = new HashSet<short>(other.Polygons);
        }
    }

    public partial class Editor
    {
        public bool Dirty = false;
        public bool RedrawAll = false;
        public short RedrawTop, RedrawBottom, RedrawLeft, RedrawRight;

        public bool Changed = false;
        public Grid Grid;
        public Selection Selection;

        public double Scale;
        public Level Level;
        public Tool Tool
        {
            get
            {
                return tool;
            }
            set
            {
                if (Level.TemporaryLineStartIndex != -1 && lineBefore != null) AbortLine();
                historyGesture = false;
                cameraDragging=false; PlacingBakeLight=false; draggingBakeLight=false; ClearLightingPreview();
                CancelPaste();
                CancelBox();
                CancelSelectionBox();
                CancelCircle();
                CancelRing();
                CancelMoveTool();
                CancelRotateTool();
                CancelScaleTool();
                ClearHover();
                tool = value;
                if (Level.TemporaryLineStartIndex != -1)
                {
                    if (Level.FindEndpointLines(Level.TemporaryLineStartIndex).Count == 0)
                    {
                        Level.DeletePoint(Level.TemporaryLineStartIndex);
                    }
                    Level.TemporaryLineStartIndex = -1;
                }
                CommitHistory();
                if (value == Tool.Line)
                {
                    short selected_point = Selection.Point;
                    Selection.Clear();
                    Selection.Point = selected_point;
                }
                else if (value == Tool.Object)
                {
                    short selected_object = Selection.Object;
                    Selection.Clear();
                    Selection.Object = selected_object;
                }
                else if (value == Tool.Annotation)
                {
                    short selected_annotation = Selection.Annotation;
                    Selection.Clear();
                    Selection.Annotation = selected_annotation;
                }
                else if (value != Tool.Select && value != Tool.Pan && value != Tool.Move && value != Tool.Rotate && value != Tool.Scale && value != Tool.BakeLighting)
                {
                    Selection.Clear();
                }
            }
        }
        Tool tool;
        public short PaintIndex;
        public ShapeDescriptor PaintDescriptor = new ShapeDescriptor();
        public short PaintTransferMode;
        MapObject lastObject = null;

        bool undoSet = false;
        short lastX;
        short lastY;
        short downX;
        short downY;

        public bool DrawingBox { get; private set; }
        public Point BoxStart { get; private set; }
        public Point BoxEnd { get; private set; }
        public string BoxMessage { get; private set; }

        Point SnapBoxPoint(short X, short Y)
        {
            Point p = new Point(X, Y);
            short index = ClosestPoint(p);
            if (index != -1) return Level.Endpoints[index];
            p = GridAdjust(p);
            index = ClosestPoint(p);
            return index == -1 ? p : Level.Endpoints[index];
        }

        public void CancelBox()
        {
            if (DrawingBox)
            {
                AddDirty(BoxStart);
                AddDirty(BoxEnd);
            }
            DrawingBox = false;
            BoxMessage = null;
        }

        public void StartBox(short X, short Y)
        {
            CommitHistory();
            CancelBox();
            Selection.Clear();
            BoxStart = BoxEnd = SnapBoxPoint(X, Y);
            DrawingBox = true;
            AddDirty(BoxStart);
        }

        public void UpdateBox(short X, short Y)
        {
            if (!DrawingBox) return;
            AddDirty(BoxStart);
            AddDirty(BoxEnd);
            Point end = SnapBoxPoint(X, Y);
            // Snap the two other corners too, while keeping the box rectangular.
            // Lock any coordinate already snapped at the drag endpoints.
            bool lockX = Level.Endpoints.Contains(end);
            bool lockY = lockX;
            short index = ClosestPoint(new Point(end.X, BoxStart.Y));
            if (!lockX && index != -1 && Level.Endpoints[index].Y == BoxStart.Y)
                end.X = Level.Endpoints[index].X;
            index = ClosestPoint(new Point(BoxStart.X, end.Y));
            if (!lockY && index != -1 && Level.Endpoints[index].X == BoxStart.X)
                end.Y = Level.Endpoints[index].Y;
            BoxEnd = end;
            string reason;
            if (BoxFilled) Level.CanCreateBox(BoxStart, BoxEnd, out reason);
            else Level.PlanShapeOutline(new List<List<Point>> { OutlineBoxCorners() }, out _, out _, out _, out reason);
            BoxMessage = reason;
            AddDirty(BoxEnd);
        }

        public void FinishBox(short X, short Y)
        {
            if (!DrawingBox) return;
            UpdateBox(X, Y);
            DrawingBox = false;
            if (!BoxFilled)
            {
                FinishShapeOutline(new List<List<Point>> { OutlineBoxCorners() }, "Draw box outline", out string outlineReason);
                BoxMessage = outlineReason;
                return;
            }
            if (!Level.CanCreateBox(BoxStart, BoxEnd, out string reason))
            {
                BoxMessage = reason;
                return;
            }
            SetUndo();
            if (Level.CreateBox(BoxStart, BoxEnd, out reason))
            {
                Selection.Polygon = Level.LastCreatedBoxPolygonIndex;
                BoxMessage = null;
                Changed = true;
                RedrawAll = true;
            }
            else BoxMessage = reason;
        }

        public int DefaultSnapDistance;
        public int ObjectSnapDistance;
        public int InertiaDistance;
        public bool SplitPolygonLines;

        public void LoadSettings()
        {
            LineSplitAll = Weland.Settings.GetSetting("Tools/LineSplitAll", false);
            BoxFilled = Weland.Settings.GetSetting("Tools/BoxFilled", true);
            CircleFilled = Weland.Settings.GetSetting("Tools/CircleFilled", true);
            RingInnerCount = Weland.Settings.GetSetting("Tools/RingInnerCount", 1);
            RingFilled = Weland.Settings.GetSetting("Tools/RingFilled", true);
            RingStairCeiling = ClampCoordinate(Weland.Settings.GetSetting("Tools/RingStairCeiling", 1024));
            RingCeilingFollows = Weland.Settings.GetSetting("Tools/RingCeilingFollows", false);
            CircleStairCeiling = ClampCoordinate(Weland.Settings.GetSetting("Tools/CircleStairCeiling", 1024));
            CircleCeilingFollows = Weland.Settings.GetSetting("Tools/CircleCeilingFollows", false);
            RingStairs = Weland.Settings.GetSetting("Tools/RingStairs", false);
            RingStairStart = ClampCoordinate(Weland.Settings.GetSetting("Tools/RingStairStart", 0));
            RingStairStep = ClampCoordinate(Weland.Settings.GetSetting("Tools/RingStairStep", 128));
            CircleStairs = Weland.Settings.GetSetting("Tools/CircleStairs", false);
            CircleStairStart = ClampCoordinate(Weland.Settings.GetSetting("Tools/CircleStairStart", 0));
            CircleStairStep = ClampCoordinate(Weland.Settings.GetSetting("Tools/CircleStairStep", 128));
            RingSides = Weland.Settings.GetSetting("Tools/RingSides", 16);
            RingHasInner = Weland.Settings.GetSetting("Tools/RingHasInner", true);
            RingInnerPercent = Weland.Settings.GetSetting("Tools/RingInnerPercent", 50);
            CircleSides = Weland.Settings.GetSetting("Tools/CircleSides", 8);
            CircleTessellationEnabled = Weland.Settings.GetSetting("Tools/CircleTessellation", false);
            CirclePattern = (TessellationPattern)Weland.Settings.GetSetting("Tools/CirclePattern",0);
            CircleTileSize = Weland.Settings.GetSetting("Tools/CircleTileSize", 1024);
            DefaultSnapDistance = Weland.Settings.GetSetting("Distance/Select/Default", 4);
            ObjectSnapDistance = Weland.Settings.GetSetting("Distance/Select/Object", 8);
            InertiaDistance = Weland.Settings.GetSetting("Distance/Inertia/Default", 8);
            SplitPolygonLines = Weland.Settings.GetSetting("Editor/SplitPolygonLines", false);
        }

        public void SaveSettings()
        {
            Weland.Settings.PutSetting("Tools/BoxFilled", BoxFilled);
            Weland.Settings.PutSetting("Tools/CircleFilled", CircleFilled);
            Weland.Settings.PutSetting("Tools/CircleSides", CircleSides);
            Weland.Settings.PutSetting("Tools/RingSides", RingSides);
            Weland.Settings.PutSetting("Tools/RingStairs", RingStairs);
            Weland.Settings.PutSetting("Tools/RingStairCeiling", (int)RingStairCeiling);
            Weland.Settings.PutSetting("Tools/RingCeilingFollows", RingCeilingFollows);
            Weland.Settings.PutSetting("Tools/CircleStairCeiling", (int)CircleStairCeiling);
            Weland.Settings.PutSetting("Tools/CircleCeilingFollows", CircleCeilingFollows);
            Weland.Settings.PutSetting("Tools/RingStairStart", (int)RingStairStart);
            Weland.Settings.PutSetting("Tools/RingStairStep", (int)RingStairStep);
            Weland.Settings.PutSetting("Tools/CircleStairs", CircleStairs);
            Weland.Settings.PutSetting("Tools/CircleStairStart", (int)CircleStairStart);
            Weland.Settings.PutSetting("Tools/CircleStairStep", (int)CircleStairStep);
            Weland.Settings.PutSetting("Tools/RingInnerCount", RingInnerCount);
            Weland.Settings.PutSetting("Tools/RingFilled", RingFilled);
            Weland.Settings.PutSetting("Tools/RingHasInner", RingHasInner);
            Weland.Settings.PutSetting("Tools/RingInnerPercent", RingInnerPercent);
            Weland.Settings.PutSetting("Tools/CircleTessellation", CircleTessellationEnabled);
            Weland.Settings.PutSetting("Tools/CircleTileSize", CircleTileSize);
            Weland.Settings.PutSetting("Tools/CirclePattern", (int)CirclePattern);
            Weland.Settings.PutSetting("Distance/Select/Default", DefaultSnapDistance);
            Weland.Settings.PutSetting("Distance/Select/Object", ObjectSnapDistance);
            Weland.Settings.PutSetting("Distance/Inertia/Default", InertiaDistance);
            Weland.Settings.PutSetting("Editor/SplitPolygonLines", SplitPolygonLines);
        }

        public Editor()
        {
            LoadSettings();
        }

        int DefaultSnap()
        {
            return (int)Math.Round(DefaultSnapDistance / Scale);
        }

        int ObjectSnap()
        {
            return (int)Math.Round(ObjectSnapDistance / Scale);
        }

        int Inertia()
        {
            return (int)Math.Round(InertiaDistance / Scale);
        }

        /*** begin custom grid code ***/

        Point GridAdjust(Point p)
        {
            double s, c;
            int i, j;

            if (Grid.Visible && Grid.Snap)
            {
                if (Grid.UseCustomGrid)
                {
                    c = Math.Cos(Grid.Rotation * 2 * Math.PI / 360);
                    s = Math.Sin(Grid.Rotation * 2 * Math.PI / 360);

                    i = (int)(Grid.Resolution * Grid.Scale * Math.Round((c * (p.X - Grid.Center.X) - s * (p.Y - Grid.Center.Y)) / (Grid.Resolution * Grid.Scale)));
                    j = (int)(Grid.Resolution * Grid.Scale * Math.Round((s * (p.X - Grid.Center.X) + c * (p.Y - Grid.Center.Y)) / (Grid.Resolution * Grid.Scale)));
                    p.X = (short)((double)Grid.Center.X + c * i + s * j);
                    p.Y = (short)((double)Grid.Center.Y - s * i + c * j);
                }
                else
                {
                    p.X = (short)(Math.Round((double)p.X / Grid.Resolution) * Grid.Resolution);
                    p.Y = (short)(Math.Round((double)p.Y / Grid.Resolution) * Grid.Resolution);
                }
            }
            return p;
        }

        /*** old code ***/
        /*
        short GridAdjust(short value) {
            if (Grid.Visible && Grid.Snap) {
            return (short) (Math.Round((double) value / Grid.Resolution) * Grid.Resolution);
            } else {
            return value;
            }
        }
        */

        /*** end custom grid code ***/


        short ClosestAnnotation(Point p)
        {
            short index = Level.GetClosestAnnotation(p);
            if (index != -1 && Level.Distance(p, new Point(Level.Annotations[index].X, Level.Annotations[index].Y)) < DefaultSnap())
            {
                return index;
            }
            else
            {
                return -1;
            }
        }

        short ClosestPoint(Point p)
        {
            short index = Level.GetClosestPoint(p);
            if (index != -1 && Level.Distance(p, Level.Endpoints[index]) < DefaultSnap())
            {
                return index;
            }
            else
            {
                return -1;
            }
        }

        short ClosestObject(Point p)
        {
            short index = Level.GetClosestObject(p);
            if (index != -1 && Level.Distance(p, new Point(Level.Objects[index].X, Level.Objects[index].Y)) < ObjectSnap())
            {
                return index;
            }
            else
            {
                return -1;
            }
        }

        short ClosestLine(Point p)
        {
            short index = Level.GetClosestLine(p);
            if (index != -1 && Level.Distance(p, Level.Lines[index]) < DefaultSnap())
            {
                return index;
            }
            else
            {
                return -1;
            }
        }

        Point Constrain(Point p, bool constrainAngle, bool constrainLength)
        {
            int StartX = Level.Endpoints[Level.TemporaryLineStartIndex].X;
            int StartY = Level.Endpoints[Level.TemporaryLineStartIndex].Y;
            int X = p.X - StartX;
            int Y = p.Y - StartY;

            double r = Math.Sqrt(Math.Pow(X, 2) + Math.Pow(Y, 2));
            double theta = Math.Atan2(Y, X);

            /*** begin custom grid code ***/
            if (constrainLength)
            {
                if (Grid.UseCustomGrid) r = Math.Round(r / (Grid.Resolution * Grid.Scale)) * Grid.Resolution * Grid.Scale;
                else r = Math.Round(r / Grid.Resolution) * Grid.Resolution;
            }

            if (constrainAngle)
            {
                double smallest_angle = Math.PI / 8;
                if (Grid.UseCustomGrid) theta = -Grid.Rotation * Math.PI / 180 + Math.Round((theta + Grid.Rotation * Math.PI / 180) / smallest_angle) * smallest_angle;
                else theta = Math.Round(theta / smallest_angle) * smallest_angle;
            }
            /*** end custom grid code ***/

            return new Point((short)(Math.Round(r * Math.Cos(theta)) + StartX), (short)(Math.Round(r * Math.Sin(theta)) + StartY));
        }

        Wadfile.DirectoryEntry lineBefore;
        Selection lineSelectionBefore;
        bool lineChangedBefore;
        bool lineSplitAll;
        short lineStartEdge = -1;

        Point ResolveLinePoint(Point p, bool useGrid, out Selection target)
        {
            target = PickGeometry(p.X,p.Y);
            if (target.Point != -1) return Level.Endpoints[target.Point];
            if (target.Line != -1 && TrySnapLinePoint(p,Level.Lines[target.Line],out Point attached)) return attached;
            if (useGrid) {
                p=GridAdjust(p); target=PickGeometry(p.X,p.Y);
                if (target.Point != -1) return Level.Endpoints[target.Point];
                if (target.Line != -1 && TrySnapLinePoint(p,Level.Lines[target.Line],out attached)) return attached;
            }
            return p;
        }
        public bool LineSplitAll
        {
            get => lineSplitAll;
            set
            {
                if (lineSplitAll == value) return;
                if (Level != null && Level.TemporaryLineStartIndex != -1 && lineBefore != null) AbortLine();
                lineSplitAll = value; LineToolError = null; Dirty = RedrawAll = true;
            }
        }
        public string LineToolError { get; private set; }
        public void CancelLine() { if (lineBefore != null) AbortLine(); }
        void AbortLine() {
            if (lineBefore != null) { Level.Load(lineBefore.Clone()); Selection.CopyFrom(lineSelectionBefore); Changed = lineChangedBefore; }
            Level.TemporaryLineStartIndex = -1; lineBefore = null; lineStartEdge=-1; ClearHover(); CancelPendingUndo(); RedrawAll = true;
        }
        bool TrySnapLinePoint(Point point, Line line, out Point snapped)
        {
            Point a=Level.Endpoints[line.EndpointIndexes[0]], b=Level.Endpoints[line.EndpointIndexes[1]];
            int dx=b.X-a.X,dy=b.Y-a.Y;
            snapped=point;if(dx==0 && dy==0)return false;
            double t=Math.Max(0,Math.Min(1,((double)(point.X-a.X)*dx+(double)(point.Y-a.Y)*dy)/((double)dx*dx+(double)dy*dy)));
            snapped=new Point((short)Math.Floor(a.X+t*dx+0.5),(short)Math.Floor(a.Y+t*dy+0.5));
            return true;
        }
        bool AttachLineEndpoint(Point point, short edge, out short vertex)
        {
            int existing=Level.Endpoints.IndexOf(point);
            vertex=existing==-1 ? (short)-1 : (short)existing;
            if(edge==-1 || Array.IndexOf(Level.Lines[edge].EndpointIndexes,vertex)!=-1)return true;
            var boundary=Level.Lines[edge];Point a=Level.Endpoints[boundary.EndpointIndexes[0]],b=Level.Endpoints[boundary.EndpointIndexes[1]];
            if(((long)point.X-a.X)*(b.Y-a.Y)-((long)point.Y-a.Y)*(b.X-a.X)==0)return true;
            if(!CanSplitLine(edge)) { LineToolError="This attachment exceeds the eight-vertex limit.";return false; }
            vertex=Level.SplitLine(edge,point.X,point.Y,vertex);
            return true;
        }
        bool CanSplitLine(short index)
        {
            Line line = Level.Lines[index];
            if (line.ClockwisePolygonOwner == -1 && line.CounterclockwisePolygonOwner == -1)
            {
                return true;
            }

            if (line.ClockwisePolygonOwner != -1 && Level.Polygons[line.ClockwisePolygonOwner].VertexCount == Polygon.MaxVertexCount)
            {
                return false;
            }

            if (line.CounterclockwisePolygonOwner != -1 && Level.Polygons[line.CounterclockwisePolygonOwner].VertexCount == Polygon.MaxVertexCount)
            {
                return false;
            }

            return true;
        }

        public void StartLine(short X, short Y)
        {
            SetUndo(LineSplitAll ? "Split intersected polygons" : null);
            lineBefore = Level.Save().Clone(); lineSelectionBefore = new Selection(); lineSelectionBefore.CopyFrom(Selection);
            lineChangedBefore = Changed; LineToolError = null;
            Selection.Clear();
            Point p=ResolveLinePoint(new Point(X,Y),true,out Selection target);
            lineStartEdge=target.Point==-1 ? target.Line : (short)-1;
            Level.TemporaryLineStartIndex=target.Point!=-1 ? target.Point : Level.NewPoint(p.X,p.Y);
            Level.TemporaryLineEnd=p;
            Hover.CopyFrom(target);
            Changed = true;
        }

        public void UpdateLine(short X, short Y, bool constrainAngle, bool constrainLength)
        {
            if (Level.TemporaryLineStartIndex == -1)
            {
                return;
            }

            AddDirty(Level.Endpoints[Level.TemporaryLineStartIndex]);
            AddDirty(Level.TemporaryLineEnd);
            Point p=new Point(X,Y);
            bool constrained=constrainAngle || constrainLength;
            if(constrained)p=Constrain(p,constrainAngle,constrainLength);
            p=ResolveLinePoint(p,!constrained,out Selection target);
            if(SetHover(target))RedrawAll=true;
            Level.TemporaryLineEnd=p;
            AddDirty(Level.TemporaryLineEnd);

            Changed = true;
        }

        public void ConnectLine(short X, short Y, bool constrainAngle, bool constrainLength)
        {
            if (Level.TemporaryLineStartIndex == -1) return;
            Point p = new Point(X,Y);
            if (constrainAngle || constrainLength) p = Constrain(p,constrainAngle,constrainLength);
            p=ResolveLinePoint(p,!constrainAngle && !constrainLength,out Selection target);
            Point start=Level.Endpoints[Level.TemporaryLineStartIndex];
            if(start.Equals(p)) { AbortLine();return; }
            if(!AttachLineEndpoint(start,lineStartEdge,out short startVertex)) { AbortLine();return; }
            // Splitting the start renumbers lines; resolve the end against the updated map.
            short endEdge=ClosestLine(p);
            if(endEdge!=-1 && TrySnapLinePoint(p,Level.Lines[endEdge],out Point onEnd) && onEnd.Equals(p)) {
                if(!AttachLineEndpoint(p,endEdge,out short attachedEnd)) { AbortLine();return; }
            }
            bool handled = false;
            if (LineSplitAll && !Level.CutPolygonsWithLine(start,p,out handled,out string reason,true)) {
                LineToolError = reason; AbortLine(); return;
            }
            if (handled) {
                Level.TemporaryLineStartIndex = -1; lineBefore = null; ClearHover();
                Selection.Point = Level.GetClosestPoint(p); Changed = true; RedrawAll = true; return;
            }
            // Attach the deferred start even when drawing outside a polygon.
            for (int edge = Level.Lines.Count-1; edge >= 0; --edge) {
                var boundary = Level.Lines[edge]; Point a = Level.Endpoints[boundary.EndpointIndexes[0]], b = Level.Endpoints[boundary.EndpointIndexes[1]];
                long cross = ((long)start.X-a.X)*(b.Y-a.Y)-((long)start.Y-a.Y)*(b.X-a.X);
                if (cross == 0 && start.X>=Math.Min(a.X,b.X) && start.X<=Math.Max(a.X,b.X) && start.Y>=Math.Min(a.Y,b.Y) && start.Y<=Math.Max(a.Y,b.Y) &&
                    boundary.EndpointIndexes[0]!=Level.TemporaryLineStartIndex && boundary.EndpointIndexes[1]!=Level.TemporaryLineStartIndex) {
                    if (!CanSplitLine((short)edge)) { LineToolError="This attachment exceeds the eight-vertex limit."; AbortLine(); return; }
                    Level.SplitLine((short)edge,start.X,start.Y,Level.TemporaryLineStartIndex);
                }
            }
            short endIndex = Level.GetClosestPoint(p);
            if (endIndex == -1 || Level.Endpoints[endIndex].X != p.X || Level.Endpoints[endIndex].Y != p.Y) {
                short edge = Level.GetClosestLine(p);
                if (edge != -1 && Level.Distance(p,Level.Lines[edge]) < DefaultSnap() && TrySnapLinePoint(p,Level.Lines[edge],out Point onLine)) {
                    if (!CanSplitLine(edge)) { LineToolError="This attachment exceeds the eight-vertex limit."; AbortLine(); return; }
                    endIndex = Level.SplitLine(edge,onLine.X,onLine.Y);
                } else endIndex = Level.NewPoint(p.X,p.Y);
            }
            bool exists = Level.Lines.Exists(line => (line.EndpointIndexes[0] == Level.TemporaryLineStartIndex && line.EndpointIndexes[1] == endIndex) ||
                (line.EndpointIndexes[1] == Level.TemporaryLineStartIndex && line.EndpointIndexes[0] == endIndex));
            if (!exists && endIndex != Level.TemporaryLineStartIndex) Level.NewLine(Level.TemporaryLineStartIndex,endIndex);
            Selection.Point = endIndex;
            Level.TemporaryLineStartIndex = -1; lineBefore = null;
            Changed = true; RedrawAll = true;
        }

        void Fill(short X, short Y)
        {
            if (!undoSet)
            {
                SetUndo();
                if (!Level.FillPolygon(X, Y))
                {
                    CancelPendingUndo();
                }
                else
                {
                    undoSet = true;
                    Changed = true;
                }
            }
            else
            {
                if (Level.FillPolygon(X, Y))
                {
                    Polygon p = FindPolygon(X, Y);
                    if (p != null) DirtyPolygon(p);
                }
            }
        }

        void SelectSingle(short X, short Y)
        {
            Selection.CopyFrom(PickSelection(X, Y));
            if (Selection.Object != -1) lastObject = Level.Objects[Selection.Object];
            undoSet = false;
        }

        void AltSelect(short X, short Y)
        {
            if (Selection.Polygon != -1)
            {
                Polygon polygon = Level.Polygons[Selection.Polygon];
                if (polygon.Type == PolygonType.Teleporter)
                {
                    short index = Level.GetEnclosingPolygon(new Point(X, Y));
                    if (index != -1)
                    {
                        SetUndo();
                        polygon.Permutation = index;
                    }
                }
            }
        }

        public bool EditAnnotation = false; // HACK

        void PlaceAnnotation(short X, short Y)
        {
            Point p = new Point(X, Y);
            short index = ClosestAnnotation(p);
            if (index != -1)
            {
                Selection.Clear();
                Selection.Annotation = index;
                EditAnnotation = false;
            }
            else
            {
                short polygon_index = Level.GetEnclosingPolygon(p);
                if (polygon_index != -1)
                {
                    SetUndo();
                    Annotation note = new Annotation();
                    note.X = p.X;
                    note.Y = p.Y;
                    note.PolygonIndex = polygon_index;
                    note.Text = "Unknown";
                    Level.Annotations.Add(note);
                    Selection.Annotation = (short)(Level.Annotations.Count - 1);
                    Changed = true;
                    EditAnnotation = true;
                }
            }
        }

        void PlaceVisualMode(short X, short Y)
        {
            Level.VisualModePoint = new Point(X, Y);
            Level.VisualModePolygonIndex = Level.GetEnclosingPolygon(Level.VisualModePoint);
        }

        void PlaceObject(short X, short Y)
        {
            Point p = new Point(X, Y);
            short index = ClosestObject(p);
            if (index != -1)
            {
                Selection.Clear();
                Selection.Object = index;
                lastObject = Level.Objects[index];
            }
            else
            {
                short polygon_index = Level.GetEnclosingPolygon(p);
                if (polygon_index != -1)
                {
                    SetUndo();
                    short object_index = Level.NewObject(p, polygon_index);
                    MapObject o = Level.Objects[object_index];
                    if (lastObject != null)
                    {
                        o.CopyFrom(lastObject);
                    }
                    else
                    {
                        o.Type = ObjectType.Player;
                    }

                    Selection.Object = object_index;
                    lastObject = o;

		    if (o.Type == ObjectType.Monster && o.Index < Level.MonsterPlacement.Count) {
			Level.MonsterPlacement[o.Index].InitialCount++;
		    } else if (o.Type == ObjectType.Item && o.Index < Level.ItemPlacement.Count) {
                        Level.ItemPlacement[o.Index].InitialCount++;
                    }
                    Changed = true;
                }
            }
        }

        void TranslatePoint(short index, int X, int Y)
        {
            Point p = Level.Endpoints[index];
            short x = (short)(p.X + X);
            short y = (short)(p.Y + Y);
            MovePoint(index, x, y);
        }

        void MovePoint(short index, short X, short Y)
        {
            Point p = Level.Endpoints[index];
            AddDirty(p);
            p.X = X;
            p.Y = Y;
            Level.Endpoints[index] = p;
            AddDirty(p);

            // dirty every endpoint line(!)
            HashSet<Line> lines = Level.EndpointLines[index];
            foreach (Line line in lines)
            {
                AddDirty(Level.Endpoints[line.EndpointIndexes[0]]);
                AddDirty(Level.Endpoints[line.EndpointIndexes[1]]);
            }

            // update attached polygon concavity
            // The adjacency cache avoids scanning the whole map per vertex.
            foreach (Polygon polygon in Level.EndpointPolygons[index])
            {
                Level.UpdatePolygonConcavity(polygon);
                DirtyPolygon(polygon);
            }

        }

        void MoveTransformPoints(Dictionary<short, Point> points)
        {
            // Scale and Rotate redraw the map as a whole. Apply the complete
            // frame before updating each affected polygon, rather than updating
            // it repeatedly with intermediate vertex positions.
            var polygons = new HashSet<Polygon>();
            foreach (var pair in points)
            {
                Level.Endpoints[pair.Key] = pair.Value;
                polygons.UnionWith(Level.EndpointPolygons[pair.Key]);
            }
            foreach (Polygon polygon in polygons) Level.UpdatePolygonConcavity(polygon);
        }

        void TranslatePoints(IEnumerable<short> points,int dx,int dy)
        {
            // Group tools repaint the full frame; update the complete group
            // before recalculating each affected polygon just once.
            var polygons=new HashSet<Polygon>();
            foreach(short index in points) {
                Point p=Level.Endpoints[index];p.X=(short)(p.X+dx);p.Y=(short)(p.Y+dy);
                Level.Endpoints[index]=p;polygons.UnionWith(Level.EndpointPolygons[index]);
            }
            foreach(var polygon in polygons)Level.UpdatePolygonConcavity(polygon);
        }

        void TranslateObject(MapObject obj, int X, int Y)
        {
            obj.X = (short)(obj.X + X);
            obj.Y = (short)(obj.Y + Y);
        }

        void TranslateAnnotation(Annotation a, int X, int Y)
        {
            a.X = (short)(a.X + X);
            a.Y = (short)(a.Y + Y);
        }

        public void NudgeSelected(short X, short Y)
        {
            ClearHover();
            if (Selection.HasMultipleGeometry)
            {
                NudgeMultiple(X, Y);
                return;
            }
            bool changed = true;
            SetUndo("Nudge selection");
            if (Selection.Point != -1)
            {
                Point p = Level.Endpoints[Selection.Point];
                short newX = (short)(p.X + X);
                short newY = (short)(p.Y + Y);
                /*** custom grid code ***/
                if (Grid.Visible && Grid.Snap)
                {
                    if (Grid.UseCustomGrid)
                    {
                        if (X > 0)
                        {
                            newX += (short)(Grid.Resolution * Grid.Scale);
                        }
                        else if (X < 0)
                        {
                            newX -= (short)(Grid.Resolution * Grid.Scale);
                        }
                        if (Y > 0)
                        {
                            newY += (short)(Grid.Resolution * Grid.Scale);
                        }
                        else if (Y < 0)
                        {
                            newY -= (short)(Grid.Resolution * Grid.Scale);
                        }
                    }
                    else
                    {
                        //original code
                        if (X > 0)
                        {
                            newX += Grid.Resolution;
                        }
                        else if (X < 0)
                        {
                            newX -= Grid.Resolution;
                        }
                        if (Y > 0)
                        {
                            newY += Grid.Resolution;
                        }
                        else if (Y < 0)
                        {
                            newY -= Grid.Resolution;
                        }
                    }
                }
                Point adjp = new Point(newX, newY);
                adjp = GridAdjust(adjp);
                MovePoint(Selection.Point, adjp.X, adjp.Y);
                //MovePoint(Selection.Point, GridAdjust(newX), GridAdjust(newY));	old code
                /*** end custom grid code ***/
            }
            else if (Selection.Object != -1)
            {
                MapObject obj = Level.Objects[Selection.Object];
                Point p = new Point((short)(obj.X + X), (short)(obj.Y + Y));
                short polygon_index = Level.GetEnclosingPolygon(p);
                if (polygon_index != -1)
                {
                    obj.X = (short)(obj.X + X);
                    obj.Y = (short)(obj.Y + Y);
                    obj.PolygonIndex = polygon_index;
                }
            }
            else if (Selection.Line != -1)
            {
                Line line = Level.Lines[Selection.Line];
                TranslatePoint(line.EndpointIndexes[0], X, Y);
                TranslatePoint(line.EndpointIndexes[1], X, Y);
            }
            else if (Selection.Polygon != -1)
            {
                Polygon polygon = Level.Polygons[Selection.Polygon];
                Dictionary<short, bool> endpoints = new Dictionary<short, bool>();
                for (int i = 0; i < polygon.VertexCount; ++i)
                {
                    Line line = Level.Lines[polygon.LineIndexes[i]];
                    endpoints[line.EndpointIndexes[0]] = true;
                    endpoints[line.EndpointIndexes[1]] = true;
                }

                foreach (short i in endpoints.Keys)
                {
                    TranslatePoint(i, X, Y);
                }

                foreach (MapObject mapObject in Level.Objects)
                {
                    if (mapObject.PolygonIndex == Selection.Polygon)
                    {
                        TranslateObject(mapObject, X, Y);
                    }
                }

                foreach (Annotation annotation in Level.Annotations)
                {
                    if (annotation.PolygonIndex == Selection.Polygon)
                    {
                        TranslateAnnotation(annotation, X, Y);
                    }
                }
            }
            else if (Selection.Annotation != -1)
            {
                Annotation note = Level.Annotations[Selection.Annotation];
                TranslateAnnotation(note, X, Y);
            }
            else
            {
                changed = false;
            }

            if (changed)
            {
                Changed = true;
            }
        }

        void MoveSelected(short X, short Y)
        {
            bool changed = true;
            if (!undoSet)
            {
                if (Math.Abs(X - downX) < Inertia() && Math.Abs(Y - downY) < Inertia())
                {
                    return;
                }
                else
                {
                    lastX = downX;
                    lastY = downY;
                }
            }

            if (Selection.Point != -1)
            {
                if (!undoSet)
                {
                    SetUndo();
                    undoSet = true;
                }

                /*** begin custom grid code ***/
                Point adjp = new Point(X, Y);
                adjp = GridAdjust(adjp);
                MovePoint(Selection.Point, adjp.X, adjp.Y);
                /*** end custom grid code ***/
                //MovePoint(Selection.Point, GridAdjust(X), GridAdjust(Y));	old code
            }
            else if (Selection.Object != -1)
            {
                if (!undoSet)
                {
                    SetUndo();
                    undoSet = true;
                }

                MapObject obj = Level.Objects[Selection.Object];
                AddDirty(new Point(obj.X, obj.Y));
                Point p = new Point((short)(X), (short)(Y));
                short polygon_index = Level.GetEnclosingPolygon(p);
                if (polygon_index != -1)
                {
                    obj.X = p.X;
                    obj.Y = p.Y;
                    AddDirty(new Point(obj.X, obj.Y));
                    obj.PolygonIndex = polygon_index;
                }
            }
            else if (Selection.Line != -1)
            {
                if (!undoSet)
                {
                    SetUndo();
                    undoSet = true;
                }

                Line line = Level.Lines[Selection.Line];
                TranslatePoint(line.EndpointIndexes[0], X - lastX, Y - lastY);
                TranslatePoint(line.EndpointIndexes[1], X - lastX, Y - lastY);
            }
            else if (Selection.Polygon != -1)
            {
                if (!undoSet)
                {
                    SetUndo();
                    undoSet = true;
                }

                Polygon polygon = Level.Polygons[Selection.Polygon];
                Dictionary<short, bool> endpoints = new Dictionary<short, bool>();
                for (int i = 0; i < polygon.VertexCount; ++i)
                {
                    Line line = Level.Lines[polygon.LineIndexes[i]];
                    endpoints[line.EndpointIndexes[0]] = true;
                    endpoints[line.EndpointIndexes[1]] = true;
                }

                foreach (short i in endpoints.Keys)
                {
                    TranslatePoint(i, X - lastX, Y - lastY);
                }

                foreach (MapObject mapObject in Level.Objects)
                {
                    if (mapObject.PolygonIndex == Selection.Polygon)
                    {
                        TranslateObject(mapObject, X - lastX, Y - lastY);
                    }
                }

                foreach (Annotation annotation in Level.Annotations)
                {
                    if (annotation.PolygonIndex == Selection.Polygon)
                    {
                        TranslateAnnotation(annotation, X - lastX, Y - lastY);
                    }
                }
            }
            else if (Selection.Annotation != -1)
            {
                if (!undoSet)
                {
                    SetUndo();
                    undoSet = true;
                }

                Annotation annotation = Level.Annotations[Selection.Annotation];
                Point p = new Point((short)(X), (short)(Y));
                short polygon_index = Level.GetEnclosingPolygon(p);
                if (polygon_index != -1)
                {
                    annotation.X = p.X;
                    annotation.Y = p.Y;
                    annotation.PolygonIndex = polygon_index;
                }
                RedrawAll = true;
                Dirty = true;
            }
            else
            {
                changed = false;
            }

            if (changed)
            {
                Changed = true;
            }
        }

        void DirtyPolygon(Polygon p)
        {
            for (int i = 0; i < p.VertexCount; ++i)
            {
                AddDirty(Level.Endpoints[Level.Lines[p.LineIndexes[i]].EndpointIndexes[0]]);
                AddDirty(Level.Endpoints[Level.Lines[p.LineIndexes[i]].EndpointIndexes[1]]);
            }
        }

        bool Alt(EditorModifiers mods)
        {
            return ((mods & EditorModifiers.Alt) != 0);
        }

        bool RightClick(EditorModifiers mods)
        {
            return ((mods & EditorModifiers.RightClick) != 0);
        }

        Polygon FindPolygon(short X, short Y)
        {
            short index = Level.GetEnclosingPolygon(new Point(X, Y));
            if (index != -1)
            {
                return Level.Polygons[index];
            }
            else
            {
                return null;
            }
        }

        void GetFloorHeight(short X, short Y)
        {
            Polygon p = FindPolygon(X, Y);
            if (p != null)
            {
                PaintIndex = p.FloorHeight;
            }
        }

        void SetFloorHeight(short X, short Y)
        {
            Polygon p = FindPolygon(X, Y);
            if (p != null)
            {
                if (!undoSet)
                {
                    SetUndo();
                    undoSet = true;
                }
                p.FloorHeight = PaintIndex;
                DirtyPolygon(p);
                Changed = true;
            }
        }

        void GetCeilingHeight(short X, short Y)
        {
            Polygon p = FindPolygon(X, Y);
            if (p != null)
            {
                PaintIndex = p.CeilingHeight;
            }
        }

        void SetCeilingHeight(short X, short Y)
        {
            Polygon p = FindPolygon(X, Y);
            if (p != null)
            {
                if (!undoSet)
                {
                    SetUndo();
                    undoSet = true;
                }
                p.CeilingHeight = PaintIndex;
                DirtyPolygon(p);
                Changed = true;
            }
        }

        void GetPolygonType(short X, short Y)
        {
            Polygon p = FindPolygon(X, Y);
            if (p != null)
            {
                PaintIndex = (short)p.Type;
            }
        }

        void SetPolygonType(short X, short Y)
        {
            Polygon p = FindPolygon(X, Y);
            if (p != null)
            {
                if (!undoSet)
                {
                    SetUndo();
                    undoSet = true;
                }

                bool scan = (p.Type == PolygonType.Platform || (PolygonType)PaintIndex == PolygonType.Platform);
                p.Type = (PolygonType)PaintIndex;
                if (scan)
                    ScanPlatforms();
                DirtyPolygon(p);
                Changed = true;
            }
        }

        void GetFloorLight(short X, short Y)
        {
            Polygon p = FindPolygon(X, Y);
            if (p != null)
            {
                PaintIndex = p.FloorLight;
            }
        }

        void SetMediaLight(short X, short Y)
        {
            Polygon p = FindPolygon(X, Y);
            if (p != null)
            {
                if (!undoSet)
                {
                    SetUndo();
                    undoSet = true;
                }
                p.MediaLight = PaintIndex;
                DirtyPolygon(p);
                Changed = true;
            }
        }

        void GetMediaLight(short X, short Y)
        {
            Polygon p = FindPolygon(X, Y);
            if (p != null)
            {
                PaintIndex = p.MediaLight;
            }
        }

        void SetFloorLight(short X, short Y)
        {
            Polygon p = FindPolygon(X, Y);
            if (p != null)
            {
                if (!undoSet)
                {
                    SetUndo();
                    undoSet = true;
                }
                p.FloorLight = PaintIndex;
                DirtyPolygon(p);
                Changed = true;
            }
        }

        void GetCeilingLight(short X, short Y)
        {
            Polygon p = FindPolygon(X, Y);
            if (p != null)
            {
                PaintIndex = p.CeilingLight;
            }
        }

        void SetCeilingLight(short X, short Y)
        {
            Polygon p = FindPolygon(X, Y);
            if (p != null)
            {
                if (!undoSet)
                {
                    SetUndo();
                    undoSet = true;
                }
                p.CeilingLight = PaintIndex;
                DirtyPolygon(p);
                Changed = true;
            }
        }

        void GetMedia(short X, short Y)
        {
            Polygon p = FindPolygon(X, Y);
            if (p != null)
            {
                PaintIndex = p.MediaIndex;
            }
        }

        void SetMedia(short X, short Y)
        {
            Polygon p = FindPolygon(X, Y);
            if (p != null)
            {
                if (!undoSet)
                {
                    SetUndo();
                    undoSet = true;
                }
                p.MediaIndex = PaintIndex;
                DirtyPolygon(p);
                Changed = true;
            }
        }

        void GetAmbientSound(short X, short Y)
        {
            Polygon p = FindPolygon(X, Y);
            if (p != null)
            {
                PaintIndex = p.AmbientSound;
            }
        }

        void SetAmbientSound(short X, short Y)
        {
            Polygon p = FindPolygon(X, Y);
            if (p != null)
            {
                if (!undoSet)
                {
                    SetUndo();
                    undoSet = true;
                }
                p.AmbientSound = PaintIndex;
                DirtyPolygon(p);
                Changed = true;
            }
        }

        void GetRandomSound(short X, short Y)
        {
            Polygon p = FindPolygon(X, Y);
            if (p != null)
            {
                PaintIndex = p.RandomSound;
            }
        }

        void SetRandomSound(short X, short Y)
        {
            Polygon p = FindPolygon(X, Y);
            if (p != null)
            {
                if (!undoSet)
                {
                    SetUndo();
                    undoSet = true;
                }
                p.RandomSound = PaintIndex;
                DirtyPolygon(p);
                Changed = true;
            }
        }

        void GetFloorTexture(short X, short Y)
        {
            Polygon p = FindPolygon(X, Y);
            if (p != null)
            {
                PaintDescriptor = p.FloorTexture;
                PaintTransferMode = p.FloorTransferMode;
            }
        }

        void SetFloorTexture(short X, short Y)
        {
            Polygon p = FindPolygon(X, Y);
            if (p != null)
            {
                if (!undoSet)
                {
                    SetUndo();
                    undoSet = true;
                }
                p.FloorTexture = PaintDescriptor;
                p.FloorTransferMode = PaintTransferMode;
                DirtyPolygon(p);
                Changed = true;
            }
        }

        void GetCeilingTexture(short X, short Y)
        {
            Polygon p = FindPolygon(X, Y);
            if (p != null)
            {
                PaintDescriptor = p.CeilingTexture;
                PaintTransferMode = p.CeilingTransferMode;
            }
        }

        void SetCeilingTexture(short X, short Y)
        {
            Polygon p = FindPolygon(X, Y);
            if (p != null)
            {
                if (!undoSet)
                {
                    SetUndo();
                    undoSet = true;
                }
                p.CeilingTexture = PaintDescriptor;
                p.CeilingTransferMode = PaintTransferMode;
                DirtyPolygon(p);
                Changed = true;
            }
        }

        bool IsPolygonPaintTool => Tool == Tool.FloorHeight || Tool == Tool.CeilingHeight ||
            Tool == Tool.PolygonType || Tool == Tool.FloorLight || Tool == Tool.CeilingLight ||
            Tool == Tool.MediaLight || Tool == Tool.Media || Tool == Tool.AmbientSound ||
            Tool == Tool.RandomSound || Tool == Tool.FloorTexture || Tool == Tool.CeilingTexture;

        public void ButtonPress(short X, short Y, EditorModifiers mods)
        {
            if (Tool == Tool.CameraPath) { CameraPress(X,Y,mods); return; }
            if (Tool == Tool.BakeLighting || PlacingBakeLight) { BakeResultMessage=null; bakeClickConsumed=true; PlacingBakeLight=false; draggingBakeLight=!RightClick(mods); if (draggingBakeLight) { BakeLightPosition=new Point(X,Y); HasBakeLight=true; UpdateLightingPreview(); } else ClearLightingPreview(); return; }
            ClearLightingPreview();
            if (Pasting)
            {
                pasteConsumed = true;
                if (RightClick(mods)) CancelPaste();
                else { MovePaste(X, Y); if (!PasteSelection(pastePosition.X, pastePosition.Y, out string reason)) PasteMessage = reason; }
                return;
            }
            CommitHistory();
            ClearHover();
            historyGesture = true;
            if (RightClick(mods) && !IsPolygonPaintTool)
            {
                downX = lastX = X;
                downY = lastY = Y;
                rightButtonMove = true;
                BeginMoveTool();
                return;
            }
            if (Tool == Tool.FloorHeight)
            {
                if (Alt(mods) || RightClick(mods))
                {
                    GetFloorHeight(X, Y);
                }
                else
                {
                    undoSet = false;
                    SetFloorHeight(X, Y);
                }
            }
            else if (Tool == Tool.CeilingHeight)
            {
                if (Alt(mods) || RightClick(mods))
                {
                    GetCeilingHeight(X, Y);
                }
                else
                {
                    undoSet = false;
                    SetCeilingHeight(X, Y);
                }
            }
            else if (Tool == Tool.PolygonType)
            {
                if (Alt(mods) || RightClick(mods))
                {
                    GetPolygonType(X, Y);
                }
                else
                {
                    undoSet = false;
                    SetPolygonType(X, Y);
                }
            }
            else if (Tool == Tool.FloorLight)
            {
                if (Alt(mods) || RightClick(mods))
                {
                    GetFloorLight(X, Y);
                }
                else
                {
                    undoSet = false;
                    SetFloorLight(X, Y);
                }
            }
            else if (Tool == Tool.CeilingLight)
            {
                if (Alt(mods) || RightClick(mods))
                {
                    GetCeilingLight(X, Y);
                }
                else
                {
                    undoSet = false;
                    SetCeilingLight(X, Y);
                }
            }
            else if (Tool == Tool.MediaLight)
            {
                if (Alt(mods) || RightClick(mods))
                {
                    GetMediaLight(X, Y);
                }
                else
                {
                    undoSet = false;
                    SetMediaLight(X, Y);
                }
            }
            else if (Tool == Tool.Media)
            {
                if (Alt(mods) || RightClick(mods))
                {
                    GetMedia(X, Y);
                }
                else
                {
                    undoSet = false;
                    SetMedia(X, Y);
                }
            }
            else if (Tool == Tool.AmbientSound)
            {
                if (Alt(mods) || RightClick(mods))
                {
                    GetAmbientSound(X, Y);
                }
                else
                {
                    undoSet = false;
                    SetAmbientSound(X, Y);
                }
            }
            else if (Tool == Tool.RandomSound)
            {
                if (Alt(mods) || RightClick(mods))
                {
                    GetRandomSound(X, Y);
                }
                else
                {
                    undoSet = false;
                    SetRandomSound(X, Y);
                }
            }
            else if (Tool == Tool.FloorTexture)
            {
                if (Alt(mods) || RightClick(mods))
                {
                    GetFloorTexture(X, Y);
                }
                else
                {
                    undoSet = false;
                    SetFloorTexture(X, Y);
                }
            }
            else if (Tool == Tool.CeilingTexture)
            {
                if (Alt(mods) || RightClick(mods))
                {
                    GetCeilingTexture(X, Y);
                }
                else
                {
                    undoSet = false;
                    SetCeilingTexture(X, Y);
                }
            }
            else if (Tool == Tool.Select)
            {
                if (Alt(mods) || RightClick(mods))
                {
                    AltSelect(X, Y);
                }
                else
                {
                    BeginSelection(X, Y, (mods & EditorModifiers.Shift) != 0);
                }
            }
            else if (!RightClick(mods))
            {
                if (Tool == Tool.Move)
                {
                    BeginMoveTool();
                }
                else if (Tool == Tool.Rotate)
                {
                    BeginRotateTool(X, Y);
                }
                else if (Tool == Tool.Scale)
                {
                    BeginScaleTool(X, Y);
                }
                else if (Tool == Tool.Line)
                {
                    StartLine(X, Y);
                }
                else if (Tool == Tool.Box)
                {
                    StartBox(X, Y);
                }
                else if (Tool == Tool.Circle)
                {
                    StartCircle(X, Y);
                }
                else if (Tool == Tool.Ring)
                {
                    StartRing(X, Y);
                }
                else if (Tool == Tool.Fill)
                {
                    undoSet = false;
                    Fill(X, Y);
                }
                else if (Tool == Tool.Object)
                {
                    PlaceObject(X, Y);
                }
                else if (Tool == Tool.Annotation)
                {
                    PlaceAnnotation(X, Y);
                }
                else if (Tool == Tool.VisualMode)
                {
                    PlaceVisualMode(X, Y);
                }
            }

            downX = lastX = X;
            downY = lastY = Y;
        }

        public void Motion(short X, short Y, EditorModifiers mods)
        {
            if (Tool == Tool.CameraPath) { CameraMotion(X,Y); return; }
            if (bakeClickConsumed || PlacingBakeLight) { if(draggingBakeLight) { BakeLightPosition=new Point(X,Y); UpdateLightingPreview(); } return; }
            if (pasteConsumed) return;
            if (rightButtonMove || Tool == Tool.Move)
            {
                UpdateMoveTool(X, Y);
            }
            else if (Tool == Tool.Rotate)
            {
                UpdateRotateTool(X, Y, (mods & EditorModifiers.Shift) != 0);
            }
            else if (Tool == Tool.Scale)
            {
                UpdateScaleTool(X, Y, (mods & EditorModifiers.Shift) != 0);
            }
            else if (Tool == Tool.Circle)
            {
                UpdateCircle(X, Y, (mods & EditorModifiers.Shift) != 0);
            }
            else if (Tool == Tool.Ring)
            {
                UpdateRing(X, Y, (mods & EditorModifiers.Shift) != 0);
            }
            else if (Tool == Tool.Box)
            {
                UpdateBox(X, Y);
            }
            else if (Tool == Tool.Line)
            {
                bool constrainAngle = (mods & EditorModifiers.Shift) != 0;
                bool constrainLength = (mods & EditorModifiers.Alt) != 0;
                UpdateLine(X, Y, constrainAngle, constrainLength);
            }
            else if (Tool == Tool.Fill)
            {
                Fill(X, Y);
            }
            else if (Tool == Tool.Select)
            {
                UpdateSelection(X, Y);
            }
            else if (Tool == Tool.FloorHeight)
            {
                if (Alt(mods) || RightClick(mods))
                {
                    GetFloorHeight(X, Y);
                }
                else
                {
                    SetFloorHeight(X, Y);
                }
            }
            else if (Tool == Tool.CeilingHeight)
            {
                if (Alt(mods) || RightClick(mods))
                {
                    GetCeilingHeight(X, Y);
                }
                else
                {
                    SetCeilingHeight(X, Y);
                }
            }
            else if (Tool == Tool.PolygonType)
            {
                if (Alt(mods) || RightClick(mods))
                {
                    GetPolygonType(X, Y);
                }
                else
                {
                    SetPolygonType(X, Y);
                }
            }
            else if (Tool == Tool.FloorLight)
            {
                if (Alt(mods) || RightClick(mods))
                {
                    GetFloorLight(X, Y);
                }
                else
                {
                    SetFloorLight(X, Y);
                }
            }
            else if (Tool == Tool.CeilingLight)
            {
                if (Alt(mods) || RightClick(mods))
                {
                    GetCeilingLight(X, Y);
                }
                else
                {
                    SetCeilingLight(X, Y);
                }
            }
            else if (Tool == Tool.MediaLight)
            {
                if (Alt(mods) || RightClick(mods))
                {
                    GetMediaLight(X, Y);
                }
                else
                {
                    SetMediaLight(X, Y);
                }
            }
            else if (Tool == Tool.Media)
            {
                if (Alt(mods) || RightClick(mods))
                {
                    GetMedia(X, Y);
                }
                else
                {
                    SetMedia(X, Y);
                }
            }
            else if (Tool == Tool.AmbientSound)
            {
                if (Alt(mods) || RightClick(mods))
                {
                    GetAmbientSound(X, Y);
                }
                else
                {
                    SetAmbientSound(X, Y);
                }
            }
            else if (Tool == Tool.RandomSound)
            {
                if (Alt(mods) || RightClick(mods))
                {
                    GetRandomSound(X, Y);
                }
                else
                {
                    SetRandomSound(X, Y);
                }
            }
            else if (Tool == Tool.FloorTexture)
            {
                if (Alt(mods) || RightClick(mods))
                {
                    GetFloorTexture(X, Y);
                }
                else
                {
                    SetFloorTexture(X, Y);
                }
            }
            else if (Tool == Tool.CeilingTexture)
            {
                if (Alt(mods) || RightClick(mods))
                {
                    GetCeilingTexture(X, Y);
                }
                else
                {
                    SetCeilingTexture(X, Y);
                }

            }

            lastX = X;
            lastY = Y;
        }

        public void ButtonRelease(short X, short Y, EditorModifiers mods)
        {
            if (Tool == Tool.CameraPath) { cameraDragging=false; historyGesture=false; CommitHistory(); CameraUpdated?.Invoke(); return; }
            if (bakeClickConsumed) { if(draggingBakeLight) {BakeLightPosition=new Point(X,Y);UpdateLightingPreview();} draggingBakeLight=false;bakeClickConsumed=false; return; }
            if (pasteConsumed) { pasteConsumed = false; historyGesture = false; return; }
            historyGesture = false;
            if (rightButtonMove)
            {
                FinishMoveTool(X, Y);
                rightButtonMove = false;
                CommitHistory();
                return;
            }
            if (Tool == Tool.Move)
            {
                FinishMoveTool(X, Y);
            }
            else if (Tool == Tool.Rotate)
            {
                FinishRotateTool(X, Y, (mods & EditorModifiers.Shift) != 0);
            }
            else if (Tool == Tool.Scale)
            {
                FinishScaleTool(X, Y, (mods & EditorModifiers.Shift) != 0);
            }
            else if (Tool == Tool.Select)
            {
                FinishSelection(X, Y);
            }
            else if (Tool == Tool.Circle)
            {
                FinishCircle(X, Y, (mods & EditorModifiers.Shift) != 0);
            }
            else if (Tool == Tool.Ring)
            {
                FinishRing(X, Y, (mods & EditorModifiers.Shift) != 0);
            }
            else if (Tool == Tool.Box)
            {
                FinishBox(X, Y);
            }
            else if (Tool == Tool.Line)
            {
                bool constrainAngle = (mods & EditorModifiers.Shift) != 0;
                bool constrainLength = (mods & EditorModifiers.Alt) != 0;
                ConnectLine(X, Y, constrainAngle, constrainLength);
            }
            CommitHistory();
        }

        public void DeleteSelected()
        {
            ClearHover();
            if (Selection.HasMultipleGeometry)
            {
                DeleteMultiple();
                return;
            }
            bool changed = true;
            if (Selection.Point != -1)
            {
                // find the closest point to delete next

                // this is pretty ridiculous: endpoint indices can
                // change after the delete, and so can line indices;
                // so remember a line reference and index into that
                // line's EndpointIndexes list
                List<short> nextPointCandidates = new List<short>();
                foreach (short i in Level.FindEndpointLines(Selection.Point))
                {
                    if (Level.Lines[i].EndpointIndexes[0] == Selection.Point)
                    {
                        nextPointCandidates.Add(Level.Lines[i].EndpointIndexes[1]);
                    }
                    else
                    {
                        nextPointCandidates.Add(Level.Lines[i].EndpointIndexes[0]);
                    }
                }

                nextPointCandidates.Sort(delegate (short p0, short p1)
                {
                    return Level.Distance(Level.Endpoints[p0], Level.Endpoints[Selection.Point]) - Level.Distance(Level.Endpoints[p1], Level.Endpoints[Selection.Point]);
                });

                Line nextLine = null;
                int nextLineEndpointIndex = 0;
                foreach (short point_index in nextPointCandidates)
                {
                    foreach (short i in Level.FindEndpointLines(point_index))
                    {
                        if (Level.Lines[i].EndpointIndexes[0] == point_index &&
                            Level.Lines[i].EndpointIndexes[1] != Selection.Point)
                        {
                            nextLine = Level.Lines[i];
                            nextLineEndpointIndex = 0;
                            break;
                        }
                        else if (Level.Lines[i].EndpointIndexes[1] == point_index &&
                             Level.Lines[i].EndpointIndexes[0] != Selection.Point)
                        {
                            nextLine = Level.Lines[i];
                            nextLineEndpointIndex = 1;
                        }
                    }
                    if (nextLine != null)
                    {
                        break;
                    }
                }
                SetUndo("Delete selection");
                Level.DeletePoint(Selection.Point);
                if (nextLine != null)
                {
                    Selection.Point = nextLine.EndpointIndexes[nextLineEndpointIndex];
                }
                else
                {
                    Selection.Point = -1;
                }
            }
            else if (Selection.Object != -1)
            {
                SetUndo("Delete selection");
                Level.DeleteObject(Selection.Object);
                Selection.Object = -1;
            }
            else if (Selection.Line != -1)
            {
                SetUndo("Delete selection");
                Level.DeleteLine(Selection.Line);
                Selection.Line = -1;
            }
            else if (Selection.Polygon != -1)
            {
                SetUndo("Delete selection");
                Level.DeletePolygon(Selection.Polygon);
                Selection.Polygon = -1;
            }
            else if (Selection.Annotation != -1)
            {
                SetUndo("Delete selection");
                Level.Annotations.RemoveAt(Selection.Annotation);
                Selection.Annotation = -1;
            }
            else
            {
                changed = false;
            }

            if (changed) Changed = true;
        }

        // add to the dirty rectangle
        void AddDirty(Point p)
        {
            if (!Dirty)
            {
                Dirty = true;
                RedrawTop = RedrawBottom = p.Y;
                RedrawLeft = RedrawRight = p.X;
            }
            else
            {
                if (p.X < RedrawLeft)
                {
                    RedrawLeft = p.X;
                }
                if (p.X > RedrawRight)
                {
                    RedrawRight = p.X;
                }
                if (p.Y > RedrawBottom)
                {
                    RedrawBottom = p.Y;
                }
                if (p.Y < RedrawTop)
                {
                    RedrawTop = p.Y;
                }
            }
        }

        public void ClearDirty()
        {
            RedrawAll = false;
            Dirty = false;
        }

        public SortedList<short, bool> GetFloorHeights()
        {
            SortedList<short, bool> list = new SortedList<short, bool>();
            foreach (Polygon p in Level.Polygons)
            {
                list[p.FloorHeight] = true;
            }

            return list;
        }

        public SortedList<short, bool> GetCeilingHeights()
        {
            SortedList<short, bool> list = new SortedList<short, bool>();
            foreach (Polygon p in Level.Polygons)
            {
                list[p.CeilingHeight] = true;
            }
            return list;
        }

        public void ChangeFloorHeights(short old_height, short new_height)
        {
            if (old_height != new_height)
            {
                SetUndo();
                foreach (Polygon p in Level.Polygons)
                {
                    if (p.FloorHeight == old_height)
                    {
                        p.FloorHeight = new_height;
                    }
                }
            }
        }

        public void ChangeCeilingHeights(short old_height, short new_height)
        {
            if (old_height != new_height)
            {
                SetUndo();
                foreach (Polygon p in Level.Polygons)
                {
                    if (p.CeilingHeight == old_height)
                    {
                        p.CeilingHeight = new_height;
                    }
                }
            }
        }

        // ensures 1:1 platform:polygon mapping
        public void ScanPlatforms()
        {
            Dictionary<short, Platform> map = new Dictionary<short, Platform>();
            for (int i = Level.Platforms.Count - 1; i >= 0; --i)
            {
                Platform platform = Level.Platforms[i];
                if (platform.PolygonIndex == -1)
                {
                    Level.DeletePlatform((short)i);
                }
                else
                {
                    Polygon polygon = Level.Polygons[platform.PolygonIndex];
                    if (polygon.Type != PolygonType.Platform)
                    {
                        Level.DeletePlatform((short)i);
                    }
                    else
                    {
                        map[platform.PolygonIndex] = platform;
                    }
                }
            }

            for (int i = 0; i < Level.Polygons.Count; ++i)
            {
                Polygon polygon = Level.Polygons[i];
                if (polygon.Type == PolygonType.Platform && !map.ContainsKey((short)i))
                {
                    Platform platform = new Platform();
                    platform.SetTypeWithDefaults(PlatformType.SphtDoor);
                    platform.PolygonIndex = (short)i;
                    Level.Platforms.Add(platform);
                    polygon.Permutation = (short)(Level.Platforms.Count - 1);
                }
            }
        }

        public void Recenter()
        {
            SetUndo();
            // find the extents
            int minX = short.MaxValue;
            int maxX = short.MinValue;
            int minY = short.MaxValue;
            int maxY = short.MinValue;

            foreach (Point p in Level.Endpoints)
            {
                if (p.X < minX) minX = p.X;
                if (p.X > maxX) maxX = p.X;
                if (p.Y < minY) minY = p.Y;
                if (p.Y > maxY) maxY = p.Y;
            }

            foreach (Annotation a in Level.Annotations)
            {
                if (a.X < minX) minX = a.X;
                if (a.X > maxX) maxX = a.X;
                if (a.Y < minY) minY = a.Y;
                if (a.Y > maxY) maxY = a.Y;
            }

            // we assume nobody has map objects outside of polygon bounds!

            int marginX = (ushort.MaxValue - (maxX - minX)) / 2;
            int marginY = (ushort.MaxValue - (maxY - minY)) / 2;

            int newMinX = short.MinValue + marginX;
            int newMinY = short.MinValue + marginY;

            int offsetX = ((newMinX - minX) / World.One) * World.One;
            int offsetY = ((newMinY - minY) / World.One) * World.One;

            for (int i = 0; i < Level.Endpoints.Count; ++i)
            {
                TranslatePoint((short)i, offsetX, offsetY);
            }

            foreach (MapObject o in Level.Objects)
            {
                TranslateObject(o, offsetX, offsetY);
            }

            foreach (Annotation a in Level.Annotations)
            {
                TranslateAnnotation(a, offsetX, offsetY);
            }

            Changed = true;
        }

        public bool SetCustomGridOriginToSelected()
        {
            if (Selection.Point != -1)
            {
                Point p = Level.Endpoints[Selection.Point];
                Grid.Center.X = p.X;
                Grid.Center.Y = p.Y;
                return true;
            }
            else if (Selection.Line != -1)
            {
                Line l = Level.Lines[Selection.Line];
                Point p1 = Level.Endpoints[l.EndpointIndexes[0]];
                Point p2 = Level.Endpoints[l.EndpointIndexes[1]];

                if (Grid.Center.X == p1.X && Grid.Center.Y == p1.Y)
                {
                    Grid.Center.X = p2.X;
                    Grid.Center.Y = p2.Y;
                }
                else
                {
                    Grid.Center.X = p1.X;
                    Grid.Center.Y = p1.Y;
                }
                return true;
            }

            return false;
        }

        public bool SetCustomGridRotationToSelected()
        {
            if (Selection.Line != -1)
            {
                Line l = Level.Lines[Selection.Line];
                Point p1 = Level.Endpoints[l.EndpointIndexes[0]];
                Point p2 = Level.Endpoints[l.EndpointIndexes[1]];
                Grid.Rotation = -(180 / Math.PI) * Math.Atan2(p2.Y - p1.Y, p2.X - p1.X);
                while (Grid.Rotation < 0)
                    Grid.Rotation += 90;

                while (Grid.Rotation > 90)
                    Grid.Rotation -= 90;

                return true;
            }

            return false;
        }

        public bool SetCustomGridScaleToSelected()
        {
            if (Selection.Line != -1)
            {
                Line l = Level.Lines[Selection.Line];
                Point p1 = Level.Endpoints[l.EndpointIndexes[0]];
                Point p2 = Level.Endpoints[l.EndpointIndexes[1]];
                double d = Math.Sqrt((double)(p2.X - p1.X) * (p2.X - p1.X) + (p2.Y - p1.Y) * (p2.Y - p1.Y)) / World.One;

                while (d > Grid.MaxScale)
                {
                    d /= 2.0;
                }
                while (d < Grid.MinScale)
                {
                    d *= 2.0;
                }

                Grid.Scale = d;
                return true;
            }

            return false;
        }

        public void SetObjectType(MapObject o, ObjectType type) {
            if (o.Type != type) {
                if (o.Type == ObjectType.Monster) {
                    if (o.Index < Level.MonsterPlacement.Count &&
                        Level.MonsterPlacement[o.Index].InitialCount > 0) {
                        --Level.MonsterPlacement[o.Index].InitialCount;
                    }
                } else if (o.Type == ObjectType.Item) {
                    if (o.Index < Level.ItemPlacement.Count &&
                        Level.ItemPlacement[o.Index].InitialCount > 0) {
                        --Level.ItemPlacement[o.Index].InitialCount;
                    }
                }

                if (type == ObjectType.Monster && o.Index < Level.MonsterPlacement.Count) {
                    ++Level.MonsterPlacement[o.Index].InitialCount;
                } else if (type == ObjectType.Item && o.Index < Level.ItemPlacement.Count) {
                    ++Level.ItemPlacement[o.Index].InitialCount;
                }
            }

            o.Type = type;
        }
    }
}


namespace Weland {
 public partial class Editor {
    public int CameraPathIndex=-1, CameraPointIndex=-1;
    public System.Action CameraUpdated;
    bool cameraDragging;
    public string CameraMessage="";
    public System.Collections.Generic.List<CameraPath> GetCameraPaths()=>CameraPaths.Read(Level);
    public void StoreCameraPaths(System.Collections.Generic.List<CameraPath> paths,string label) {
        SetUndo(label); CameraPaths.Write(Level,paths); Changed=true;RedrawAll=true;CommitHistory();CameraUpdated?.Invoke();
    }
    void CameraPress(short x,short y,EditorModifiers mods) {
        cameraDragging=false;
        var paths=GetCameraPaths();if(CameraPathIndex<0||CameraPathIndex>=paths.Count)return;
        var p=paths[CameraPathIndex];CameraMessage="";
        if(RightClick(mods)) {
            short poly=Level.GetEnclosingPolygon(new Point(x,y));
            if(poly<0){CameraMessage="Place waypoints inside a polygon.";CameraUpdated?.Invoke();return;}
            SetUndo("Add camera waypoint");
            var polygon=Level.Polygons[poly];
            p.Points.Add(new CameraWaypoint{X=x,Y=y,Z=(short)((polygon.FloorHeight+polygon.CeilingHeight)/2)});CameraPointIndex=p.Points.Count-1;
            CameraPaths.Write(Level,paths);Changed=true;RedrawAll=true;CommitHistory();
        } else {
            CameraPointIndex=-1;double nearest=Math.Pow(8/Math.Max(Scale,0.001),2);
            for(int i=0;i<p.Points.Count;i++) {
                double dx=p.Points[i].X-x,dy=p.Points[i].Y-y,d=dx*dx+dy*dy;
                if(d<=nearest){nearest=d;CameraPointIndex=i;}
            }
            if(CameraPointIndex>=0){SetUndo("Move camera waypoint");cameraDragging=true;historyGesture=true;}
            RedrawAll=true;
        }
        CameraUpdated?.Invoke();
    }
    void CameraMotion(short x,short y){
        if(!cameraDragging)return;
        var paths=GetCameraPaths();if(CameraPathIndex<0||CameraPathIndex>=paths.Count||CameraPointIndex<0||CameraPointIndex>=paths[CameraPathIndex].Points.Count)return;
        if(Level.GetEnclosingPolygon(new Point(x,y))<0)return;
        var p=paths[CameraPathIndex].Points[CameraPointIndex];p.X=x;p.Y=y;
        CameraPaths.Write(Level,paths);Changed=true;Dirty=true;RedrawAll=true;
    }
 }
}
