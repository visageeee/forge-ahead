using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Weland
{
    // Private, immutable map clipboard; resource IDs refer to the current level.
    public partial class Editor
    {
        Level clipboard;
        HashSet<short> clipboardPoints, clipboardLines, clipboardPolygons;
        List<int> clipboardObjects, clipboardNotes;
        Point clipboardOrigin, pastePosition;
        List<Polygon> clipboardPolygonIdentities;
        bool pasteConsumed;
        public bool Pasting { get; private set; }
        public string PasteMessage { get; private set; }
        public void CancelPaste() { Pasting = false; PasteMessage = null; Dirty = RedrawAll = true; }
        public void ClearClipboard() { CancelPaste(); clipboard = null; pasteConsumed = false; }

        public bool CopySelection(out string reason)
        {
            PrepareHistoryNavigation(); reason = null;
            var polygons = new HashSet<short>(Selection.Polygons);
            if (Selection.Polygon != -1) polygons.Add(Selection.Polygon);
            var lines = new HashSet<short>(Selection.Lines);
            if (Selection.Line != -1) lines.Add(Selection.Line);
            foreach (short i in polygons) lines.UnionWith(Level.Polygons[i].LineIndexes.Take(Level.Polygons[i].VertexCount));
            var points = new HashSet<short>(Selection.Points);
            if (Selection.Point != -1) points.Add(Selection.Point);
            foreach (short i in lines) points.UnionWith(Level.Lines[i].EndpointIndexes);
            var objects = Enumerable.Range(0, Level.Objects.Count).Where(i => i == Selection.Object || polygons.Contains(Level.Objects[i].PolygonIndex)).ToList();
            var notes = Enumerable.Range(0, Level.Annotations.Count).Where(i => i == Selection.Annotation || polygons.Contains(Level.Annotations[i].PolygonIndex)).ToList();
            var positions = points.Select(i => Level.Endpoints[i]).Concat(objects.Select(i => new Point(Level.Objects[i].X, Level.Objects[i].Y)))
                .Concat(notes.Select(i => new Point(Level.Annotations[i].X, Level.Annotations[i].Y))).ToList();
            if (positions.Count == 0) { reason = "Select something to copy first."; return false; }
            clipboardPolygonIdentities = Level.Polygons.ToList();
            clipboard = new Level(); clipboard.Load(Level.Save().Clone());
            clipboardPoints = points; clipboardLines = lines; clipboardPolygons = polygons;
            clipboardObjects = objects; clipboardNotes = notes;
            clipboardOrigin = new Point((short)((positions.Min(p => p.X) + positions.Max(p => p.X)) / 2),
                (short)((positions.Min(p => p.Y) + positions.Max(p => p.Y)) / 2));
            return true;
        }

        public bool BeginPaste(out string reason)
        {
            PrepareHistoryNavigation(); reason = null;
            if (clipboard == null) { reason = "Copy a selection first."; return false; }
            Pasting = true; pastePosition = clipboardOrigin;
            PasteMessage = "Click to place the copy. Escape or right-click cancels.";
            Dirty = RedrawAll = true; return true;
        }
        public void MovePaste(short x, short y)
        {
            if (!Pasting) return;
            pastePosition = GridAdjust(new Point(x, y)); Dirty = RedrawAll = true;
        }
        Point ClipboardPosition(Point p) => new Point(ClampCoordinate(p.X + pastePosition.X - clipboardOrigin.X), ClampCoordinate(p.Y + pastePosition.Y - clipboardOrigin.Y));
        public IEnumerable<Point[]> PastePreviewLines => !Pasting ? Enumerable.Empty<Point[]>() : clipboardLines.Select(i => clipboard.Lines[i].EndpointIndexes.Select(v => ClipboardPosition(clipboard.Endpoints[v])).ToArray());
        public IEnumerable<Point> PastePreviewPoints => !Pasting ? Enumerable.Empty<Point>() : clipboardPoints.Select(i => ClipboardPosition(clipboard.Endpoints[i]))
            .Concat(clipboardObjects.Select(i => ClipboardPosition(new Point(clipboard.Objects[i].X, clipboard.Objects[i].Y))))
            .Concat(clipboardNotes.Select(i => ClipboardPosition(new Point(clipboard.Annotations[i].X, clipboard.Annotations[i].Y))));

        public bool PasteSelection(short x, short y, out string reason)
        {
            reason = null;
            if (clipboard == null) { reason = "Copy a selection first."; return false; }
            int dx = x - clipboardOrigin.X, dy = y - clipboardOrigin.Y;
            bool inRange = true;
            Point Offset(Point p)
            {
                long a = p.X + dx, b = p.Y + dy;
                if (a < short.MinValue || a > short.MaxValue || b < short.MinValue || b > short.MaxValue) inRange = false;
                return new Point(ClampCoordinate((int)a), ClampCoordinate((int)b));
            }
            var trial = new Level(); trial.Load(Level.Save().Clone());
            if (trial.Endpoints.Count + clipboardPoints.Count > short.MaxValue || trial.Lines.Count + clipboardLines.Count > short.MaxValue ||
                trial.Polygons.Count + clipboardPolygons.Count > short.MaxValue || trial.Objects.Count + clipboardObjects.Count > short.MaxValue ||
                trial.Sides.Count + clipboardLines.Count * 2 > short.MaxValue || trial.Annotations.Count + clipboardNotes.Count > short.MaxValue ||
                trial.Platforms.Count + clipboardPolygons.Count(i => clipboard.Polygons[i].Type == PolygonType.Platform) > short.MaxValue)
            { reason = "The copy exceeds map item limits."; return false; }
            var points = new Dictionary<short, short>(); var lines = new Dictionary<short, short>(); var polygons = new Dictionary<short, short>();
            foreach (short i in clipboardPoints.OrderBy(i => i))
            {
                Point p = Offset(clipboard.Endpoints[i]); int existing = trial.Endpoints.IndexOf(p);
                points[i] = existing == -1 ? trial.NewPoint(p.X, p.Y) : (short)existing;
            }
            foreach (short i in clipboardLines.OrderBy(i => i))
            {
                var source = clipboard.Lines[i]; short a = points[source.EndpointIndexes[0]], b = points[source.EndpointIndexes[1]];
                int existing = trial.Lines.FindIndex(l => (l.EndpointIndexes[0] == a && l.EndpointIndexes[1] == b) || (l.EndpointIndexes[0] == b && l.EndpointIndexes[1] == a));
                short index = existing == -1 ? trial.NewLine(a, b) : (short)existing;
                if (existing == -1) trial.Lines[index].Flags = source.Flags;
                lines[i] = index;
            }
            foreach (short i in clipboardPolygons.OrderBy(i => i))
            { polygons[i] = (short)trial.Polygons.Count; trial.Polygons.Add(CloneClipboardItem(clipboard.Polygons[i])); }
            // Geometry references inside the copy follow the new polygons. External
            // references follow their original target even after index renumbering.
            short ResolvePolygon(short index)
            {
                if (index < 0) return index;
                if (polygons.TryGetValue(index, out short copied)) return copied;
                if (index >= clipboard.Polygons.Count) return -1;
                int current = Level.Polygons.IndexOf(clipboardPolygonIdentities[index]);
                if (current != -1) return (short)current;
                var original = clipboard.Polygons[index];
                var ring = new HashSet<Point>(original.EndpointIndexes.Take(original.VertexCount).Select(i => clipboard.Endpoints[i]));
                current = Level.Polygons.FindIndex(p => p.VertexCount == original.VertexCount &&
                    ring.SetEquals(p.EndpointIndexes.Take(p.VertexCount).Select(i => Level.Endpoints[i])));
                return (short)current;
            }
            foreach (var pair in polygons)
            {
                var polygon = trial.Polygons[pair.Value]; var source = clipboard.Polygons[pair.Key];
                polygon.LuaTriggerCode=source.LuaTriggerCode;polygon.LuaTriggerEnabled=source.LuaTriggerEnabled;polygon.LuaTriggerOnce=source.LuaTriggerOnce;
                if (polygon.Type == PolygonType.Platform)
                {
                    if (source.Permutation < 0 || source.Permutation >= clipboard.Platforms.Count)
                    { reason = "A copied platform has no platform settings. Repair it before copying."; return false; }
                    var platform = CloneClipboardItem(clipboard.Platforms[source.Permutation]);
                    platform.PolygonIndex = pair.Value; polygon.Permutation = (short)trial.Platforms.Count; trial.Platforms.Add(platform);
                }
                else if (polygon.Type == PolygonType.Teleporter || polygon.Type == PolygonType.PlatformOnTrigger || polygon.Type == PolygonType.PlatformOffTrigger)
                {
                    short target = ResolvePolygon(polygon.Permutation);
                    if (polygon.Permutation >= 0 && target == -1) { reason = "A copied polygon references a deleted target. Copy the selection again."; return false; }
                    polygon.Permutation = target;
                }
                for (int edge = 0; edge < polygon.VertexCount; edge++)
                {
                    short oldLine = source.LineIndexes[edge], index = lines[oldLine];
                    polygon.EndpointIndexes[edge] = points[source.EndpointIndexes[edge]];
                    polygon.LineIndexes[edge] = index; polygon.AdjacentPolygonIndexes[edge] = polygon.SideIndexes[edge] = -1;
                    var line = trial.Lines[index]; bool clockwise = line.EndpointIndexes[1] == polygon.EndpointIndexes[edge];
                    if ((clockwise ? line.ClockwisePolygonOwner : line.CounterclockwisePolygonOwner) != -1)
                    { reason = "The copy overlaps an existing polygon."; return false; }
                    short sideIndex = -1, sourceSide = source.SideIndexes[edge];
                    if (sourceSide != -1)
                    {
                        var side = CloneClipboardItem(clipboard.Sides[sourceSide]); side.LineIndex = index; side.PolygonIndex = pair.Value;
                        if (side.IsControlPanel && side.ControlPanelType >= 0 && side.ControlPanelType < Side.ControlPanelClasses.Length && side.IsPlatformSwitch())
                        {
                            short target = ResolvePolygon(side.ControlPanelPermutation);
                            if (side.ControlPanelPermutation >= 0 && target == -1) { reason = "A copied switch references a deleted platform. Copy the selection again."; return false; }
                            side.ControlPanelPermutation = target;
                        }
                        sideIndex = (short)trial.Sides.Count; trial.Sides.Add(side);
                    }
                    if (clockwise) { line.ClockwisePolygonOwner = pair.Value; line.ClockwisePolygonSideIndex = sideIndex; }
                    else { line.CounterclockwisePolygonOwner = pair.Value; line.CounterclockwisePolygonSideIndex = sideIndex; }
                }
            }
            var validationOriginal = new Level(); validationOriginal.Load(trial.Save().Clone());
            if (!trial.RepairReflection(validationOriginal, new HashSet<short>(points.Values), out reason))
            { reason = reason.Replace("reflection", "copy"); return false; }
            var selected = new Selection(); selected.Points.UnionWith(points.Values); selected.Lines.UnionWith(lines.Values); selected.Polygons.UnionWith(polygons.Values);
            foreach (int i in clipboardObjects)
            {
                var obj = CloneClipboardItem(clipboard.Objects[i]); Point p = Offset(new Point(obj.X, obj.Y)); obj.X = p.X; obj.Y = p.Y;
                obj.PolygonIndex = polygons.TryGetValue(obj.PolygonIndex, out short target) ? target : trial.GetEnclosingPolygon(p);
                if (obj.PolygonIndex == -1) { reason = "Objects must be pasted inside a polygon."; return false; }
                selected.Object = (short)trial.Objects.Count; trial.Objects.Add(obj);
            }
            foreach (int i in clipboardNotes)
            {
                var note = CloneClipboardItem(clipboard.Annotations[i]); Point p = Offset(new Point(note.X, note.Y)); note.X = p.X; note.Y = p.Y;
                note.PolygonIndex = polygons.TryGetValue(note.PolygonIndex, out short target) ? target : trial.GetEnclosingPolygon(p);
                if (note.PolygonIndex == -1) { reason = "Notes must be pasted inside a polygon."; return false; }
                selected.Annotation = (short)trial.Annotations.Count; trial.Annotations.Add(note);
            }
            if (!inRange) { reason = "The copy exceeds map coordinate limits."; return false; }
            // Avoid duplicate-point/line-only pastes adding an empty history entry.
            var after = trial.Save().Clone(); var before = Level.Save().Clone();
            if (before.Chunks.Count == after.Chunks.Count && before.Chunks.All(p => after.Chunks.TryGetValue(p.Key, out byte[] data) && p.Value.SequenceEqual(data)))
            { reason = "The copied geometry already exists here."; return false; }
            historyGesture = false; SetUndo("Paste selection"); Level.Load(after); Selection.CopyFrom(selected);
            CancelPaste(); Changed = Dirty = RedrawAll = true; CommitHistory(); return true;
        }
        static T CloneClipboardItem<T>(T item) where T : ISerializableBE, new()
        {
            using (var stream = new MemoryStream())
            {
                item.Save(new BinaryWriterBE(stream)); stream.Position = 0;
                var result = new T(); result.Load(new BinaryReaderBE(stream)); return result;
            }
        }
    }
}
