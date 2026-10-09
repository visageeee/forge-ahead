using System;
using System.Collections.Generic;
using System.Linq;

namespace Weland
{
    public partial class Editor
    {
        public bool RotatingSelection { get; private set; }
        public double RotationCenterX { get; private set; }
        public double RotationCenterY { get; private set; }
        double rotationStartAngle;
        bool rotationAngleReady;
        readonly Dictionary<short, Point> rotationPoints = new Dictionary<short, Point>();
        readonly Dictionary<MapObject, Point> rotationObjects = new Dictionary<MapObject, Point>();
        readonly Dictionary<MapObject, double> rotationFacings = new Dictionary<MapObject, double>();
        readonly Dictionary<Annotation, Point> rotationNotes = new Dictionary<Annotation, Point>();

        public void CancelRotateTool() { RotatingSelection = false; }

        public bool ShowRotationCenter()
        {
            if (Tool != Tool.Rotate && Tool != Tool.Scale) return false;
            if (RotatingSelection || ScalingSelection) return true;
            var points = new HashSet<short>();
            if (Selection.HasMultipleGeometry) points = MultipleEndpoints();
            else if (Selection.Line != -1) points.UnionWith(Level.Lines[Selection.Line].EndpointIndexes);
            else if (Selection.Polygon != -1)
            {
                Polygon polygon = Level.Polygons[Selection.Polygon];
                for (int i = 0; i < polygon.VertexCount; ++i) points.Add(polygon.EndpointIndexes[i]);
            }
            if (points.Count < 2) return false;
            RotationCenterX = (points.Min(i => Level.Endpoints[i].X) + points.Max(i => Level.Endpoints[i].X)) / 2.0;
            RotationCenterY = (points.Min(i => Level.Endpoints[i].Y) + points.Max(i => Level.Endpoints[i].Y)) / 2.0;
            return true;
        }

        void BeginRotateTool(short x, short y)
        {
            BeginMoveTool();
            movingSelection = false;
            rotationPoints.Clear(); rotationObjects.Clear(); rotationFacings.Clear(); rotationNotes.Clear();
            foreach (short index in movePoints) rotationPoints[index] = Level.Endpoints[index];
            RotatingSelection = rotationPoints.Count > 1;
            if (!RotatingSelection) return;
            RotationCenterX = (rotationPoints.Values.Min(p => p.X) + rotationPoints.Values.Max(p => p.X)) / 2.0;
            RotationCenterY = (rotationPoints.Values.Min(p => p.Y) + rotationPoints.Values.Max(p => p.Y)) / 2.0;
            foreach (MapObject obj in moveObjects)
            {
                rotationObjects[obj] = new Point(obj.X, obj.Y);
                rotationFacings[obj] = obj.Facing;
            }
            foreach (Annotation note in moveNotes) rotationNotes[note] = new Point(note.X, note.Y);
            rotationStartAngle = Math.Atan2(y - RotationCenterY, x - RotationCenterX);
            rotationAngleReady = Math.Abs(x - RotationCenterX) + Math.Abs(y - RotationCenterY) >= 1;
            Dirty = RedrawAll = true;
        }

        void UpdateRotateTool(short x, short y, bool snap)
        {
            if (!RotatingSelection || Math.Abs(x - RotationCenterX) + Math.Abs(y - RotationCenterY) < 1) return;
            double angle = Math.Atan2(y - RotationCenterY, x - RotationCenterX);
            if (!rotationAngleReady) { rotationStartAngle = angle; rotationAngleReady = true; return; }
            angle -= rotationStartAngle;
            if (snap) angle = Math.Round(angle / (Math.PI / 12)) * Math.PI / 12;
            double cosine = Math.Cos(angle), sine = Math.Sin(angle);
            bool inRange = true;
            Point Rotate(Point p)
            {
                double dx = p.X - RotationCenterX, dy = p.Y - RotationCenterY;
                double rx = Math.Round(RotationCenterX + dx * cosine - dy * sine);
                double ry = Math.Round(RotationCenterY + dx * sine + dy * cosine);
                if (rx < short.MinValue || rx > short.MaxValue || ry < short.MinValue || ry > short.MaxValue) inRange = false;
                return new Point(ClampCoordinate((int)rx), ClampCoordinate((int)ry));
            }
            var points = rotationPoints.ToDictionary(pair => pair.Key, pair => Rotate(pair.Value));
            var objects = rotationObjects.ToDictionary(pair => pair.Key, pair => Rotate(pair.Value));
            var notes = rotationNotes.ToDictionary(pair => pair.Key, pair => Rotate(pair.Value));
            if (!inRange || points.All(pair => Level.Endpoints[pair.Key].Equals(pair.Value))) return;
            if (!undoSet) { SetUndo("Rotate selection"); undoSet = true; }
            foreach (var pair in points) MovePoint(pair.Key, pair.Value.X, pair.Value.Y);
            foreach (var pair in objects)
            {
                pair.Key.X = pair.Value.X; pair.Key.Y = pair.Value.Y;
                if (pair.Key.Type == ObjectType.Monster || pair.Key.Type == ObjectType.Player)
                    pair.Key.Facing = ((rotationFacings[pair.Key] + angle * 180 / Math.PI) % 360 + 360) % 360;
            }
            foreach (var pair in notes) { pair.Key.X = pair.Value.X; pair.Key.Y = pair.Value.Y; }
            Changed = Dirty = RedrawAll = true;
        }

        void FinishRotateTool(short x, short y, bool snap)
        {
            UpdateRotateTool(x, y, snap);
            CancelRotateTool();
            Dirty = RedrawAll = true;
        }
    }
}
