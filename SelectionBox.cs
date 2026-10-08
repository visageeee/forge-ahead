using System;
using System.Collections.Generic;

namespace Weland
{
    public partial class Editor
    {
        public Selection Hover { get; } = new Selection();

        Selection PickSelection(short X, short Y)
        {
            var hit = new Selection();
            Point p = new Point(X, Y);
            hit.Point = ClosestPoint(p);
            if (hit.Point != -1) return hit;
            hit.Object = ClosestObject(p);
            if (hit.Object != -1) return hit;
            hit.Annotation = ClosestAnnotation(p);
            if (hit.Annotation != -1) return hit;
            hit.Line = ClosestLine(p);
            if (hit.Line != -1) return hit;
            short polygon = Level.GetEnclosingPolygon(p);
            if (polygon != -1 && Level.Filter(Level.Polygons[polygon])) hit.Polygon = polygon;
            return hit;
        }

        public bool UpdateHover(short X, short Y)
        {
            var hit = Tool == Tool.Select ? PickSelection(X, Y) : new Selection();
            if (Hover.Point == hit.Point && Hover.Line == hit.Line && Hover.Polygon == hit.Polygon &&
                Hover.Object == hit.Object && Hover.Annotation == hit.Annotation) return false;
            Hover.CopyFrom(hit);
            return true;
        }

        public void ClearHover() { Hover.Clear(); }

        public bool DrawingSelectionBox { get; private set; }
        public Point SelectionBoxStart { get; private set; }
        public Point SelectionBoxEnd { get; private set; }
        bool pendingSelectionBox;
        bool additiveSelectionBox;
        Selection beforeSelectionBox;
        int groupMovedX, groupMovedY;

        public void CancelSelectionBox()
        {
            if (pendingSelectionBox && beforeSelectionBox != null)
                Selection.CopyFrom(beforeSelectionBox);
            pendingSelectionBox = DrawingSelectionBox = false;
            beforeSelectionBox = null;
            Dirty = RedrawAll = true;
        }

        void BeginSelection(short X, short Y, bool additive)
        {
            CancelSelectionBox();
            undoSet = false;
            groupMovedX = groupMovedY = 0;
            Point p = new Point(X, Y);
            Selection hit = PickSelection(X, Y);
            short point = hit.Point, line = hit.Line, polygon = hit.Polygon;
            beforeSelectionBox = new Selection();
            beforeSelectionBox.CopyFrom(Selection);
            additiveSelectionBox = additive;
            SelectionBoxStart = SelectionBoxEnd = p;
            if (additive)
            {
                // Shift-click toggles an item. Shift-drag instead extends the
                // original selection with everything enclosed by the box.
                IncludeSingleGeometry();
                if (point != -1)
                {
                    if (!Selection.Points.Add(point)) Selection.Points.Remove(point);
                }
                else if (line != -1)
                {
                    if (!Selection.Lines.Add(line)) Selection.Lines.Remove(line);
                }
                else if (polygon != -1)
                {
                    if (!Selection.Polygons.Add(polygon)) Selection.Polygons.Remove(polygon);
                }
                pendingSelectionBox = true;
            }
            else if (Selection.HasMultipleGeometry &&
                ((point != -1 && Selection.Points.Contains(point)) ||
                 (line != -1 && Selection.Lines.Contains(line)) ||
                 (polygon != -1 && Selection.Polygons.Contains(polygon)) ||
                 (point != -1 && MultipleEndpoints().Contains(point))))
            {
                // Pressing selected geometry keeps the group for dragging.
            }
            else
            {
                SelectSingle(X, Y);
                pendingSelectionBox = Selection.Point == -1 && Selection.Line == -1 &&
                    Selection.Object == -1 && Selection.Annotation == -1 && Selection.Polygon == -1;
            }
            Dirty = RedrawAll = true;
        }

        void IncludeSingleGeometry()
        {
            if (Selection.Point != -1) Selection.Points.Add(Selection.Point);
            if (Selection.Line != -1) Selection.Lines.Add(Selection.Line);
            if (Selection.Polygon != -1) Selection.Polygons.Add(Selection.Polygon);
            Selection.Point = Selection.Line = Selection.Object = Selection.Polygon = Selection.Annotation = -1;
        }

        void UpdateSelection(short X, short Y)
        {
            if (pendingSelectionBox)
            {
                if (!DrawingSelectionBox && Math.Abs(X - downX) < Inertia() && Math.Abs(Y - downY) < Inertia()) return;
                DrawingSelectionBox = true;
                SelectionBoxEnd = new Point(X, Y);
                Selection.Clear();
                if (additiveSelectionBox)
                {
                    Selection.CopyFrom(beforeSelectionBox);
                    IncludeSingleGeometry();
                }
                int left = Math.Min(downX, X), right = Math.Max(downX, X);
                int top = Math.Min(downY, Y), bottom = Math.Max(downY, Y);
                bool Inside(Point p) => p.X >= left && p.X <= right && p.Y >= top && p.Y <= bottom;
                for (int i = 0; i < Level.Endpoints.Count; ++i)
                    if (Level.FilterPoint(Level.Filter, (short)i) && Inside(Level.Endpoints[i])) Selection.Points.Add((short)i);
                for (int i = 0; i < Level.Lines.Count; ++i)
                {
                    Line line = Level.Lines[i];
                    if (Level.FilterLine(Level.Filter, line) && Inside(Level.Endpoints[line.EndpointIndexes[0]]) &&
                        Inside(Level.Endpoints[line.EndpointIndexes[1]])) Selection.Lines.Add((short)i);
                }
                Dirty = RedrawAll = true;
            }
            else if (Selection.HasMultipleGeometry)
            {
                if (!undoSet && Math.Abs(X - downX) < Inertia() && Math.Abs(Y - downY) < Inertia()) return;
                var points = MultipleEndpoints();
                if (points.Count == 0) return;
                short anchor = MinimumIndex(points);
                Point origin = Level.Endpoints[anchor];
                // Snap one common translation, not each vertex individually.
                int targetX = origin.X + X - downX - groupMovedX;
                int targetY = origin.Y + Y - downY - groupMovedY;
                Point target = GridAdjust(new Point(ClampCoordinate(targetX), ClampCoordinate(targetY)));
                int dx = target.X - origin.X, dy = target.Y - origin.Y;
                if (dx == 0 && dy == 0) return;
                if (!undoSet) { SetUndo(); undoSet = true; }
                TranslateMultiple(points, ref dx, ref dy);
                groupMovedX += dx;
                groupMovedY += dy;
            }
            else MoveSelected(X, Y);
        }

        void FinishSelection(short X, short Y)
        {
            if (pendingSelectionBox) UpdateSelection(X, Y);
            pendingSelectionBox = DrawingSelectionBox = false;
            beforeSelectionBox = null;
            Dirty = RedrawAll = true;
        }

        HashSet<short> MultipleEndpoints()
        {
            var points = new HashSet<short>(Selection.Points);
            foreach (short index in Selection.Lines)
                foreach (short endpoint in Level.Lines[index].EndpointIndexes) points.Add(endpoint);
            foreach (short index in Selection.Polygons)
            {
                Polygon polygon = Level.Polygons[index];
                for (int i = 0; i < polygon.VertexCount; ++i) points.Add(polygon.EndpointIndexes[i]);
            }
            return points;
        }

        static short MinimumIndex(HashSet<short> points)
        {
            short min = short.MaxValue;
            foreach (short index in points) min = Math.Min(min, index);
            return min;
        }

        static short ClampCoordinate(int value) => (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, value));

        void TranslateMultiple(HashSet<short> points, ref int dx, ref int dy)
        {
            foreach (short index in points)
            {
                Point p = Level.Endpoints[index];
                dx = Math.Max(short.MinValue - p.X, Math.Min(short.MaxValue - p.X, dx));
                dy = Math.Max(short.MinValue - p.Y, Math.Min(short.MaxValue - p.Y, dy));
            }
            var objects = Level.Objects.FindAll(obj => Selection.Polygons.Contains(obj.PolygonIndex));
            var notes = Level.Annotations.FindAll(note => Selection.Polygons.Contains(note.PolygonIndex));
            foreach (MapObject obj in objects)
            {
                dx = Math.Max(short.MinValue - obj.X, Math.Min(short.MaxValue - obj.X, dx));
                dy = Math.Max(short.MinValue - obj.Y, Math.Min(short.MaxValue - obj.Y, dy));
            }
            foreach (Annotation note in notes)
            {
                dx = Math.Max(short.MinValue - note.X, Math.Min(short.MaxValue - note.X, dx));
                dy = Math.Max(short.MinValue - note.Y, Math.Min(short.MaxValue - note.Y, dy));
            }
            foreach (short index in points) TranslatePoint(index, dx, dy);
            foreach (MapObject obj in objects) TranslateObject(obj, dx, dy);
            foreach (Annotation note in notes) TranslateAnnotation(note, dx, dy);
            if (dx != 0 || dy != 0) Changed = true;
            Dirty = RedrawAll = true;
        }

        void NudgeMultiple(short X, short Y)
        {
            var points = MultipleEndpoints();
            if (points.Count == 0 || (X == 0 && Y == 0)) return;
            Point anchor = Level.Endpoints[MinimumIndex(points)];
            int dx = X, dy = Y;
            if (Grid.Visible && Grid.Snap)
            {
                int step = (int)Math.Round(Grid.Resolution * (Grid.UseCustomGrid ? Grid.Scale : 1));
                Point target = GridAdjust(new Point(ClampCoordinate(anchor.X + Math.Sign(X) * step),
                                                   ClampCoordinate(anchor.Y + Math.Sign(Y) * step)));
                dx = target.X - anchor.X; dy = target.Y - anchor.Y;
            }
            SetUndo();
            TranslateMultiple(points, ref dx, ref dy);
        }

        void DeleteMultiple()
        {
            SetUndo();
            ClearHover();
            var polygons = new List<Polygon>();
            foreach (short index in Selection.Polygons) polygons.Add(Level.Polygons[index]);
            var coordinates = new HashSet<Point>();
            foreach (short index in Selection.Points) coordinates.Add(Level.Endpoints[index]);
            var lines = new HashSet<Line>();
            foreach (short index in Selection.Lines) lines.Add(Level.Lines[index]);
            foreach (Line line in Level.Lines)
                if (Selection.Points.Contains(line.EndpointIndexes[0]) || Selection.Points.Contains(line.EndpointIndexes[1])) lines.Add(line);
            // Keep references, because deleting a line renumbers lines and can
            // remove orphaned endpoints. Original numeric indexes are unsafe.
            foreach (Line line in lines)
            {
                int index = Level.Lines.IndexOf(line);
                if (index != -1) Level.DeleteLine((short)index);
            }
            for (int i = Level.Endpoints.Count - 1; i >= 0; --i)
                if (coordinates.Contains(Level.Endpoints[i])) Level.DeletePoint((short)i);
            foreach (Polygon polygon in polygons)
            {
                int index = Level.Polygons.IndexOf(polygon);
                if (index != -1) Level.DeletePolygon((short)index);
            }
            Selection.Clear();
            Changed = Dirty = RedrawAll = true;
        }
    }
}
