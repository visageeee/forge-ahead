using System;
using System.Linq;

namespace Weland
{
    public partial class Editor
    {
        public bool ScalingSelection { get; private set; }
        double scaleStartRadius;

        public void CancelScaleTool() { ScalingSelection = false; }

        double ScaleRadius(short x, short y)
        {
            double dx = x - RotationCenterX, dy = y - RotationCenterY;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        void BeginScaleTool(short x, short y)
        {
            // Capture the same geometry, contents and bounding-box pivot as Rotate.
            BeginRotateTool(x, y);
            ScalingSelection = RotatingSelection;
            CancelRotateTool();
            scaleStartRadius = ScaleRadius(x, y);
        }

        void UpdateScaleTool(short x, short y, bool snap)
        {
            if (!ScalingSelection) return;
            double radius = ScaleRadius(x, y);
            if (scaleStartRadius < 1) { if (radius >= 1) scaleStartRadius = radius; return; }
            double factor = radius / scaleStartRadius;
            if (snap) factor = Math.Round(factor * 10) / 10;
            if (factor <= 0) return;
            bool inRange = true;
            Point Scale(Point p)
            {
                double sx = Math.Round(RotationCenterX + (p.X - RotationCenterX) * factor);
                double sy = Math.Round(RotationCenterY + (p.Y - RotationCenterY) * factor);
                if (sx < short.MinValue || sx > short.MaxValue || sy < short.MinValue || sy > short.MaxValue) inRange = false;
                return new Point(ClampCoordinate((int)sx), ClampCoordinate((int)sy));
            }
            // Always use the drag's original coordinates to avoid rounding drift.
            var points = rotationPoints.ToDictionary(pair => pair.Key, pair => Scale(pair.Value));
            var objects = rotationObjects.ToDictionary(pair => pair.Key, pair => Scale(pair.Value));
            var notes = rotationNotes.ToDictionary(pair => pair.Key, pair => Scale(pair.Value));
            if (!inRange || points.Values.Distinct().Count() < rotationPoints.Values.Distinct().Count() ||
                points.All(pair => Level.Endpoints[pair.Key].Equals(pair.Value))) return;
            Point Proposed(short index) => points.TryGetValue(index, out Point p) ? p : Level.Endpoints[index];
            foreach (var line in points.Keys.SelectMany(i => Level.EndpointLines[i]).Distinct())
                if (Proposed(line.EndpointIndexes[0]).Equals(Proposed(line.EndpointIndexes[1]))) return;
            foreach (var polygon in points.Keys.SelectMany(i => Level.EndpointPolygons[i]).Distinct())
            {
                long area = 0;
                for (int i = 0; i < polygon.VertexCount; ++i)
                {
                    Point a = Proposed(polygon.EndpointIndexes[i]), b = Proposed(polygon.EndpointIndexes[(i + 1) % polygon.VertexCount]);
                    area += (long)a.X * b.Y - (long)b.X * a.Y;
                }
                if (area == 0) return;
            }
            if (!undoSet) { SetUndo("Scale selection"); undoSet = true; }
            foreach (var pair in points) MovePoint(pair.Key, pair.Value.X, pair.Value.Y);
            foreach (var pair in objects) { pair.Key.X = pair.Value.X; pair.Key.Y = pair.Value.Y; }
            foreach (var pair in notes) { pair.Key.X = pair.Value.X; pair.Key.Y = pair.Value.Y; }
            Changed = Dirty = RedrawAll = true;
        }

        void FinishScaleTool(short x, short y, bool snap)
        {
            UpdateScaleTool(x, y, snap);
            CancelScaleTool();
            Dirty = RedrawAll = true;
        }
    }
}
