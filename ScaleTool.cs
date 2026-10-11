using System;
using System.Collections.Generic;
using System.Linq;

namespace Weland
{
    public partial class Editor
    {
        public bool ScalingSelection { get; private set; }
        double scaleStartRadius, scaleStartX, scaleStartY, scaleAxisX, scaleAxisY;
        HashSet<Point> scaleUnselectedPoints;
        Line[] scaleLines;
        Polygon[] scalePolygons;
        int scaleOriginalDistinctPoints;

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
            scaleStartX = x - RotationCenterX;
            scaleStartY = y - RotationCenterY;
            if (!ScalingSelection) return;
            scaleUnselectedPoints = Level.Endpoints.Where((p, i) => !rotationPoints.ContainsKey((short)i)).ToHashSet();
            scaleLines = rotationPoints.Keys.SelectMany(i => Level.EndpointLines[i]).Distinct().ToArray();
            scalePolygons = rotationPoints.Keys.SelectMany(i => Level.EndpointPolygons[i]).Distinct().ToArray();
            scaleOriginalDistinctPoints = rotationPoints.Values.Distinct().Count();
            double halfWidth = (rotationPoints.Values.Max(p => p.X) - rotationPoints.Values.Min(p => p.X)) / 2.0;
            double halfHeight = (rotationPoints.Values.Max(p => p.Y) - rotationPoints.Values.Min(p => p.Y)) / 2.0;
            // If the click lies on a center axis, use the selection's extent so
            // dragging that axis still works, without dividing by zero.
            scaleAxisX = Math.Abs(scaleStartX) >= 1 ? scaleStartX : Math.Max(1, halfWidth);
            scaleAxisY = Math.Abs(scaleStartY) >= 1 ? scaleStartY : Math.Max(1, halfHeight);
        }

        void UpdateScaleTool(short x, short y, bool constrain)
        {
            if (!ScalingSelection) return;
            double factorX, factorY;
            if (constrain)
            {
                double radius = ScaleRadius(x, y);
                if (scaleStartRadius < 1) { if (radius >= 1) scaleStartRadius = radius; return; }
                double factor = Math.Round(radius / scaleStartRadius * 10) / 10;
                factorX = factorY = factor;
            }
            else
            {
                factorX = 1 + (x - RotationCenterX - scaleStartX) / scaleAxisX;
                factorY = 1 + (y - RotationCenterY - scaleStartY) / scaleAxisY;
            }
            if (factorX <= 0 || factorY <= 0) return;
            bool inRange = true;
            Point Scale(Point p)
            {
                double sx = Math.Round(RotationCenterX + (p.X - RotationCenterX) * factorX);
                double sy = Math.Round(RotationCenterY + (p.Y - RotationCenterY) * factorY);
                if (sx < short.MinValue || sx > short.MaxValue || sy < short.MinValue || sy > short.MaxValue) inRange = false;
                return new Point(ClampCoordinate((int)sx), ClampCoordinate((int)sy));
            }
            // Always use the drag's original coordinates to avoid rounding drift.
            var points = rotationPoints.ToDictionary(pair => pair.Key, pair => Scale(pair.Value));
            var objects = rotationObjects.ToDictionary(pair => pair.Key, pair => Scale(pair.Value));
            var notes = rotationNotes.ToDictionary(pair => pair.Key, pair => Scale(pair.Value));
            if (!inRange || points.Values.Distinct().Count() < scaleOriginalDistinctPoints ||
                points.All(pair => Level.Endpoints[pair.Key].Equals(pair.Value))) return;
            if (points.Values.Any(p => scaleUnselectedPoints.Contains(p))) return;
            Point Proposed(short index) => points.TryGetValue(index, out Point p) ? p : Level.Endpoints[index];
            foreach (var line in scaleLines)
                if (Proposed(line.EndpointIndexes[0]).Equals(Proposed(line.EndpointIndexes[1]))) return;
            foreach (var polygon in scalePolygons)
            {
                long area = 0, originalArea = 0;
                for (int i = 0; i < polygon.VertexCount; ++i)
                {
                    Point a = Proposed(polygon.EndpointIndexes[i]), b = Proposed(polygon.EndpointIndexes[(i + 1) % polygon.VertexCount]);
                    area += (long)a.X * b.Y - (long)b.X * a.Y;
                    Point originalA = Level.Endpoints[polygon.EndpointIndexes[i]], originalB = Level.Endpoints[polygon.EndpointIndexes[(i + 1) % polygon.VertexCount]];
                    originalArea += (long)originalA.X * originalB.Y - (long)originalB.X * originalA.Y;
                }
                if (area == 0 || Math.Sign(area) != Math.Sign(originalArea)) return;
                // A partial selection can warp neighbouring polygons too. Reject
                // folded or concave rings before changing any vertex.
                for (int i = 0; i < polygon.VertexCount; ++i)
                {
                    Point a = Proposed(polygon.EndpointIndexes[i]), b = Proposed(polygon.EndpointIndexes[(i+1)%polygon.VertexCount]);
                    for (int j = 0; j < polygon.VertexCount; ++j)
                    {
                        Point p = Proposed(polygon.EndpointIndexes[j]);
                        long cross = (long)(b.X-a.X)*(p.Y-a.Y) - (long)(b.Y-a.Y)*(p.X-a.X);
                        if ((area > 0 && cross < 0) || (area < 0 && cross > 0)) return;
                    }
                }
            }
            if (!undoSet) { SetUndo("Scale selection"); undoSet = true; }
            MoveTransformPoints(points);
            foreach (var pair in objects) { pair.Key.X = pair.Value.X; pair.Key.Y = pair.Value.Y; }
            foreach (var pair in notes) { pair.Key.X = pair.Value.X; pair.Key.Y = pair.Value.Y; }
            Changed = Dirty = RedrawAll = true;
        }

        void FinishScaleTool(short x, short y, bool constrain)
        {
            UpdateScaleTool(x, y, constrain);
            CancelScaleTool();
            Dirty = RedrawAll = true;
        }
    }
}
