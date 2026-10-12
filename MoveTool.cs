using System;
using System.Collections.Generic;

namespace Weland
{
    public partial class Editor
    {
        bool movingSelection;
        bool rightButtonMove;
        Point moveOrigin;
        int movedX, movedY;
        HashSet<short> movePoints = new HashSet<short>();
        List<MapObject> moveObjects = new List<MapObject>();
        List<Annotation> moveNotes = new List<Annotation>();

        public void CancelMoveTool() { movingSelection = rightButtonMove = false; }

        void BeginMoveTool()
        {
            movePoints.Clear(); moveObjects.Clear(); moveNotes.Clear();
            if (Selection.HasMultipleGeometry)
            {
                movePoints = MultipleEndpoints();
                foreach (MapObject obj in Level.Objects)
                    if (Selection.Polygons.Contains(obj.PolygonIndex)) moveObjects.Add(obj);
                foreach (Annotation note in Level.Annotations)
                    if (Selection.Polygons.Contains(note.PolygonIndex)) moveNotes.Add(note);
            }
            else if (Selection.Point != -1) movePoints.Add(Selection.Point);
            else if (Selection.Line != -1)
                foreach (short point in Level.Lines[Selection.Line].EndpointIndexes) movePoints.Add(point);
            else if (Selection.Polygon != -1)
            {
                Polygon polygon = Level.Polygons[Selection.Polygon];
                for (int i = 0; i < polygon.VertexCount; ++i) movePoints.Add(polygon.EndpointIndexes[i]);
                foreach (MapObject obj in Level.Objects)
                    if (obj.PolygonIndex == Selection.Polygon) moveObjects.Add(obj);
                foreach (Annotation note in Level.Annotations)
                    if (note.PolygonIndex == Selection.Polygon) moveNotes.Add(note);
            }
            else if (Selection.Object != -1) moveObjects.Add(Level.Objects[Selection.Object]);
            else if (Selection.Annotation != -1) moveNotes.Add(Level.Annotations[Selection.Annotation]);
            movingSelection = movePoints.Count > 0 || moveObjects.Count > 0 || moveNotes.Count > 0;
            if (!movingSelection) return;
            if (movePoints.Count > 0) moveOrigin = Level.Endpoints[MinimumIndex(movePoints)];
            else if (moveObjects.Count > 0) moveOrigin = new Point(moveObjects[0].X, moveObjects[0].Y);
            else moveOrigin = new Point(moveNotes[0].X, moveNotes[0].Y);
            movedX = movedY = 0;
            undoSet = false;
        }

        void UpdateMoveTool(short X, short Y)
        {
            if (!movingSelection) return;
            if (!undoSet && Math.Abs(X - downX) < Inertia() && Math.Abs(Y - downY) < Inertia()) return;
            Point target = GridAdjust(new Point(ClampCoordinate(moveOrigin.X + X - downX),
                                               ClampCoordinate(moveOrigin.Y + Y - downY)));
            int dx = target.X - moveOrigin.X - movedX, dy = target.Y - moveOrigin.Y - movedY;
            void Limit(Point p)
            {
                dx = Math.Max(short.MinValue-p.X, Math.Min(short.MaxValue-p.X, dx));
                dy = Math.Max(short.MinValue-p.Y, Math.Min(short.MaxValue-p.Y, dy));
            }
            foreach (short index in movePoints) Limit(Level.Endpoints[index]);
            foreach (MapObject obj in moveObjects) Limit(new Point(obj.X, obj.Y));
            foreach (Annotation note in moveNotes) Limit(new Point(note.X, note.Y));
            if (dx == 0 && dy == 0) return;
            short owner = -1;
            if (movePoints.Count == 0)
            {
                Point p = moveObjects.Count > 0 ? new Point(moveObjects[0].X,moveObjects[0].Y) : new Point(moveNotes[0].X,moveNotes[0].Y);
                owner = Level.GetEnclosingPolygon(new Point((short)(p.X+dx),(short)(p.Y+dy)));
                if (owner == -1) return;
            }
            if (!undoSet) { SetUndo(); undoSet = true; }
            TranslatePoints(movePoints,dx,dy);
            foreach (MapObject obj in moveObjects)
            {
                TranslateObject(obj,dx,dy);
                if (owner != -1) obj.PolygonIndex = owner;
            }
            foreach (Annotation note in moveNotes)
            {
                TranslateAnnotation(note,dx,dy);
                if (owner != -1) note.PolygonIndex = owner;
            }
            movedX += dx; movedY += dy;
            Changed = Dirty = RedrawAll = true;
        }

        void FinishMoveTool(short X, short Y)
        {
            UpdateMoveTool(X,Y);
            movingSelection = false;
        }
    }
}
