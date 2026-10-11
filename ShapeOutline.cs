using System.Collections.Generic;
using System.Linq;

namespace Weland
{
    public partial class Level
    {
        public bool PlanShapeOutline(List<List<Point>> shapes, out Level plan,
            out List<short> selected, out bool changed, out string reason)
        {
            plan = null; selected = new List<short>(); changed = false; reason = null;
            if (shapes.Count == 0 || shapes.Count > CircleTessellation.MaxTiles)
            { reason = "Choose between 1 and 256 outlines."; return false; }
            var trial = new Level(); trial.Load(Save().Clone());
            int pointsBefore = trial.Endpoints.Count, linesBefore = trial.Lines.Count;
            foreach (var corners in shapes)
            {
                var ring = trial.CircleRing(corners);
                var points = new HashSet<Point>(ring);
                Polygon existing = trial.Polygons.FirstOrDefault(p => p.VertexCount == ring.Count &&
                    points.SetEquals(p.EndpointIndexes.Take(p.VertexCount).Select(i => trial.Endpoints[i])));
                if (existing != null)
                {
                    if (!Filter(existing))
                    { reason = "An existing boundary is hidden by the current view filter."; return false; }
                    continue;
                }
                if (!trial.CreateShapeBoundary(corners, out _, out reason))
                { reason = "Outline blocked by existing geometry: " + reason; return false; }
            }
            // Splitting a later boundary can renumber earlier lines. Resolve the
            // complete selection only after all geometry changes are finished.
            for (int i = 0; i < trial.Lines.Count; ++i)
            {
                Line line = trial.Lines[i];
                Point a = trial.Endpoints[line.EndpointIndexes[0]], b = trial.Endpoints[line.EndpointIndexes[1]];
                if (shapes.Any(corners => Enumerable.Range(0, corners.Count).Any(edge =>
                    OnSegment(a, corners[edge], corners[(edge+1)%corners.Count]) &&
                    OnSegment(b, corners[edge], corners[(edge+1)%corners.Count])))) selected.Add((short)i);
            }
            changed = trial.Endpoints.Count != pointsBefore || trial.Lines.Count != linesBefore;
            plan = trial; return true;
        }
    }

    public partial class Editor
    {
        bool boxFilled = true, circleFilled = true;
        public bool BoxFilled
        {
            get => boxFilled;
            set { if (boxFilled != value) { CancelBox(); boxFilled = value; Dirty = RedrawAll = true; } }
        }
        public bool CircleFilled
        {
            get => circleFilled;
            set { if (circleFilled != value) { CancelCircle(); circleFilled = value; } }
        }

        List<Point> OutlineBoxCorners()
        {
            short left = System.Math.Min(BoxStart.X, BoxEnd.X), right = System.Math.Max(BoxStart.X, BoxEnd.X);
            short top = System.Math.Min(BoxStart.Y, BoxEnd.Y), bottom = System.Math.Max(BoxStart.Y, BoxEnd.Y);
            return new List<Point> { new Point(left,top), new Point(right,top),
                new Point(right,bottom), new Point(left,bottom) };
        }

        void FinishShapeOutline(List<List<Point>> shapes, string label, out string reason)
        {
            if (!Level.PlanShapeOutline(shapes, out var plan, out var selected, out bool changed, out reason)) return;
            if (changed)
            {
                SetUndo(label);
                Level.Load(plan.Save().Clone());
                Changed = true;
            }
            Selection.Clear();
            foreach (short index in selected) Selection.Lines.Add(index);
            Dirty = RedrawAll = true;
        }
    }
}
