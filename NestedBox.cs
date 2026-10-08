using System;
using System.Collections.Generic;
using System.IO;

namespace Weland
{
    public partial class Level
    {
        public short LastCreatedBoxPolygonIndex { get; private set; } = -1;

        bool RectangleBounds(Polygon polygon, out Point min, out Point max)
        {
            min = new Point(short.MaxValue, short.MaxValue);
            max = new Point(short.MinValue, short.MinValue);
            if (polygon.VertexCount < 4 || polygon.Concave) return false;
            for (int i = 0; i < polygon.VertexCount; ++i)
            {
                Point p = Endpoints[polygon.EndpointIndexes[i]];
                min.X = Math.Min(min.X, p.X); min.Y = Math.Min(min.Y, p.Y);
                max.X = Math.Max(max.X, p.X); max.Y = Math.Max(max.Y, p.Y);
            }
            if (min.X == max.X || min.Y == max.Y) return false;
            var corners = new HashSet<Point>();
            for (int i = 0; i < polygon.VertexCount; ++i)
            {
                Point p = Endpoints[polygon.EndpointIndexes[i]];
                Point q = Endpoints[polygon.EndpointIndexes[(i + 1) % polygon.VertexCount]];
                if (p.X != q.X && p.Y != q.Y) return false;
                if (p.X != min.X && p.X != max.X && p.Y != min.Y && p.Y != max.Y) return false;
                if ((p.X == min.X || p.X == max.X) && (p.Y == min.Y || p.Y == max.Y)) corners.Add(p);
            }
            return corners.Count == 4;
        }

        short BoxContainer(Point start, Point end)
        {
            int left = Math.Min(start.X, end.X), right = Math.Max(start.X, end.X);
            int top = Math.Min(start.Y, end.Y), bottom = Math.Max(start.Y, end.Y);
            if (left == right || top == bottom) return -1;
            for (int i = 0; i < Polygons.Count; ++i)
                if (RectangleBounds(Polygons[i], out Point min, out Point max) &&
                    left > min.X && right < max.X && top > min.Y && bottom < max.Y)
                    return (short)i;
            return -1;
        }

        public bool CanCreateBox(Point start, Point end, out string reason)
        {
            if (CanCreatePlainBox(start, end, out reason)) return true;
            short container = BoxContainer(start, end);
            if (container == -1) return false;
            if (!Filter(Polygons[container]))
            {
                reason = "Box overlaps geometry hidden by the current view filter.";
                return false;
            }
            if (Polygons[container].Type != PolygonType.Normal)
            {
                reason = "Inset boxes currently require a normal rectangular polygon.";
                return false;
            }
            // Run every split and geometry-limit check on a separate level.
            // Failed plans leave the actual geometry and editor undo untouched.
            var trial = new Level();
            trial.Load(Save().Clone());
            return trial.SplitBox(container, start, end, out reason);
        }

        public bool CreateBox(Point start, Point end, out string reason)
        {
            if (CanCreatePlainBox(start, end, out reason)) return CreatePlainBox(start, end, out reason);
            short container = BoxContainer(start, end);
            if (container == -1 || !CanCreateBox(start, end, out reason)) return false;
            return SplitBox(container, start, end, out reason);
        }

        class BoxWall
        {
            public Point Start, End;
            public bool Clockwise;
            public LineFlags Flags;
            public byte[] Side;
        }

        static void CopyBoxSettings(Polygon source, Polygon target)
        {
            target.Flags = source.Flags;
            target.FloorHeight = source.FloorHeight; target.CeilingHeight = source.CeilingHeight;
            target.FloorTexture = source.FloorTexture; target.CeilingTexture = source.CeilingTexture;
            target.FloorOrigin = source.FloorOrigin; target.CeilingOrigin = source.CeilingOrigin;
            target.FloorTransferMode = source.FloorTransferMode; target.CeilingTransferMode = source.CeilingTransferMode;
            target.FloorLight = source.FloorLight; target.CeilingLight = source.CeilingLight;
            target.MediaIndex = source.MediaIndex; target.MediaLight = source.MediaLight;
            target.AmbientSound = source.AmbientSound; target.RandomSound = source.RandomSound;
        }

        bool SplitBox(short index, Point start, Point end, out string reason)
        {
            reason = null;
            Polygon original = Polygons[index];
            RectangleBounds(original, out Point min, out Point max);
            short left = Math.Min(start.X, end.X), right = Math.Max(start.X, end.X);
            short top = Math.Min(start.Y, end.Y), bottom = Math.Max(start.Y, end.Y);
            var walls = new List<BoxWall>();
            // Detach the perimeter without deleting objects, references, or
            // neighboring polygons. Store textured sides before splitting.
            foreach (Line line in Lines)
            {
                bool clockwise = line.ClockwisePolygonOwner == index;
                bool counterclockwise = line.CounterclockwisePolygonOwner == index;
                if (!clockwise && !counterclockwise) continue;
                short sideIndex = clockwise ? line.ClockwisePolygonSideIndex : line.CounterclockwisePolygonSideIndex;
                var wall = new BoxWall { Start = Endpoints[line.EndpointIndexes[0]], End = Endpoints[line.EndpointIndexes[1]],
                    Clockwise = clockwise, Flags = line.Flags };
                if (sideIndex != -1)
                {
                    var stream = new MemoryStream();
                    Sides[sideIndex].Save(new BinaryWriterBE(stream));
                    wall.Side = stream.ToArray();
                }
                walls.Add(wall);
                if (clockwise) line.ClockwisePolygonOwner = -1;
                if (counterclockwise) line.CounterclockwisePolygonOwner = -1;
            }
            for (int i = Sides.Count - 1; i >= 0; --i)
                if (Sides[i].PolygonIndex == index) DeleteSide((short)i);
            foreach (var polygons in EndpointPolygons) polygons.Remove(original);
            // Reserve the original polygon index for the center. Existing
            // teleporter and script references continue to refer to that index.
            Polygons[index] = new Polygon { VertexCount = 0 };
            int first = Polygons.Count;
            Point[][] boxes = {
                new[] { min, new Point(max.X, top) },
                new[] { new Point(min.X, bottom), max },
                new[] { new Point(min.X, top), new Point(left, bottom) },
                new[] { new Point(right, top), new Point(max.X, bottom) },
                new[] { new Point(left, top), new Point(right, bottom) }
            };
            foreach (Point[] box in boxes)
            {
                if (!CreatePlainBox(box[0], box[1], out reason)) return false;
                CopyBoxSettings(original, Polygons[Polygons.Count - 1]);
            }
            short centerIndex = (short)(Polygons.Count - 1);
            Polygons[index] = Polygons[centerIndex];
            Polygons.RemoveAt(centerIndex);
            foreach (Line line in Lines)
            {
                if (line.ClockwisePolygonOwner == centerIndex) line.ClockwisePolygonOwner = index;
                if (line.CounterclockwisePolygonOwner == centerIndex) line.CounterclockwisePolygonOwner = index;
            }
            foreach (Side side in Sides)
                if (side.PolygonIndex == centerIndex) side.PolygonIndex = index;
            foreach (BoxWall wall in walls)
                for (int i = 0; i < Lines.Count; ++i)
                {
                    Line line = Lines[i];
                    Point a = Endpoints[line.EndpointIndexes[0]], b = Endpoints[line.EndpointIndexes[1]];
                    if (!OnSegment(a, wall.Start, wall.End) || !OnSegment(b, wall.Start, wall.End)) continue;
                    line.Flags = wall.Flags;
                    if (wall.Side == null) continue;
                    var side = new Side(); side.Load(new BinaryReaderBE(new MemoryStream(wall.Side)));
                    short offset = (short)Distance(wall.Start, a);
                    if (!side.Primary.Texture.IsEmpty()) side.Primary.X += offset;
                    if (!side.Secondary.Texture.IsEmpty()) side.Secondary.X += offset;
                    if (!side.Transparent.Texture.IsEmpty()) side.Transparent.X += offset;
                    side.LineIndex = (short)i;
                    side.PolygonIndex = wall.Clockwise ? line.ClockwisePolygonOwner : line.CounterclockwisePolygonOwner;
                    if (Sides.Count >= short.MaxValue)
                    {
                        reason = "Inset wall textures would exceed the map's side limit.";
                        return false;
                    }
                    if (wall.Clockwise) line.ClockwisePolygonSideIndex = (short)Sides.Count;
                    else line.CounterclockwisePolygonSideIndex = (short)Sides.Count;
                    Sides.Add(side);
                }
            short PartFor(Point p)
            {
                // Inclusive bounds give objects on an internal edge a stable
                // valid owner, with the inset taking priority.
                if (p.X >= left && p.X <= right && p.Y >= top && p.Y <= bottom) return index;
                if (p.Y <= top) return (short)first;
                if (p.Y >= bottom) return (short)(first + 1);
                return (short)(first + (p.X <= left ? 2 : 3));
            }
            foreach (MapObject obj in Objects)
                if (obj.PolygonIndex == index) obj.PolygonIndex = PartFor(new Point(obj.X, obj.Y));
            foreach (Annotation note in Annotations)
                if (note.PolygonIndex == index) note.PolygonIndex = PartFor(new Point(note.X, note.Y));
            if (VisualModePolygonIndex == index) VisualModePolygonIndex = PartFor(VisualModePoint);
            for (int i = 0; i < Polygons.Count; ++i)
                for (int edge = 0; edge < Polygons[i].VertexCount; ++edge)
                {
                    Line line = Lines[Polygons[i].LineIndexes[edge]];
                    bool clockwise = line.ClockwisePolygonOwner == i;
                    Polygons[i].AdjacentPolygonIndexes[edge] = clockwise ? line.CounterclockwisePolygonOwner : line.ClockwisePolygonOwner;
                    Polygons[i].SideIndexes[edge] = clockwise ? line.ClockwisePolygonSideIndex : line.CounterclockwisePolygonSideIndex;
                }
            LastCreatedBoxPolygonIndex = index;
            return true;
        }
    }
}
