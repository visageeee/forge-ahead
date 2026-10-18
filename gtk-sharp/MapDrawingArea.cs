using Pango;
using System;
using System.Collections.Generic;

namespace Weland
{
    [Flags]
    public enum CohenSutherland
    {
        Inside = 0x0,
        Top = 0x1,
        Bottom = 0x2,
        Left = 0x4,
        Right = 0x8
    }

    public class Transform
    {
        public Transform() { }
        public double Scale = 1.0 / 16.0;
        public short XOffset = 0;
        public short YOffset = 0;

        public double ToScreenX(short x)
        {
            return Math.Floor((x - XOffset) * Scale);
        }

        public double ToScreenY(short y)
        {
            return Math.Floor((y - YOffset) * Scale);
        }

        public short ToMapX(double X)
        {
            return (short)Math.Min(short.MaxValue, Math.Max(short.MinValue, ((double)(X / Scale) + XOffset)));
        }

        public short ToMapY(double Y)
        {
            return (short)Math.Min(short.MaxValue, Math.Max(short.MinValue, ((double)(Y / Scale) + YOffset)));
        }

        public Drawer.Point ToScreenPoint(Point p)
        {
            return new Drawer.Point(ToScreenX(p.X), ToScreenY(p.Y));
        }
    }

    public enum DrawMode
    {
        Draw,
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
        CeilingTexture
    }

    public class MapDrawingArea : Gtk.DrawingArea
    {

        public Transform Transform = new Transform();
        public Level Level;
        public Editor Editor;
        public Grid Grid = new Grid();
        public Selection Selection = new Selection();
        public bool ShowMonsters = true;
        public bool ShowObjects = true;
        public bool ShowScenery = true;
        public bool ShowPlayers = true;
        public bool ShowGoals = true;
        public bool ShowSounds = true;
        public bool Antialias;
        public int LineWidth = 1;
        public int VertexSize = 2;
        public DrawMode Mode = DrawMode.Draw;
        public PolygonFilter Filter = x => true;

        public Dictionary<short, Drawer.Color> PaintColors = new Dictionary<short, Drawer.Color>();

        Drawer drawer;

        public MapDrawingArea()
        {
            itemImages[ItemType.Magnum] = new Gdk.Pixbuf(null, "pistol.png");
            itemImages[ItemType.MagnumMagazine] = new Gdk.Pixbuf(null, "pistol-ammo.png");
            itemImages[ItemType.PlasmaPistol] = new Gdk.Pixbuf(null, "fusion.png");
            itemImages[ItemType.PlasmaMagazine] = new Gdk.Pixbuf(null, "fusion-ammo.png");
            itemImages[ItemType.AssaultRifle] = new Gdk.Pixbuf(null, "ar.png");
            itemImages[ItemType.AssaultRifleMagazine] = new Gdk.Pixbuf(null, "ar-ammo.png");
            itemImages[ItemType.AssaultGrenadeMagazine] = new Gdk.Pixbuf(null, "ar-grenades.png");
            itemImages[ItemType.MissileLauncher] = new Gdk.Pixbuf(null, "rl.png");
            itemImages[ItemType.MissileLauncherMagazine] = new Gdk.Pixbuf(null, "rl-ammo.png");
            itemImages[ItemType.InvisibilityPowerup] = new Gdk.Pixbuf(null, "powerup.png");
            itemImages[ItemType.InvincibilityPowerup] = new Gdk.Pixbuf(null, "invinc.png");
            itemImages[ItemType.InfravisionPowerup] = new Gdk.Pixbuf(null, "powerup.png");
            itemImages[ItemType.AlienShotgun] = new Gdk.Pixbuf(null, "alien-gun.png");
            itemImages[ItemType.Flamethrower] = new Gdk.Pixbuf(null, "tozt.png");
            itemImages[ItemType.FlamethrowerCanister] = new Gdk.Pixbuf(null, "tozt-ammo.png");
            itemImages[ItemType.ExtravisionPowerup] = new Gdk.Pixbuf(null, "powerup.png");
            itemImages[ItemType.OxygenPowerup] = new Gdk.Pixbuf(null, "oxygen.png");
            itemImages[ItemType.EnergyPowerup] = new Gdk.Pixbuf(null, "1x.png");
            itemImages[ItemType.DoubleEnergyPowerup] = new Gdk.Pixbuf(null, "2x.png");
            itemImages[ItemType.TripleEnergyPowerup] = new Gdk.Pixbuf(null, "3x.png");
            itemImages[ItemType.Shotgun] = new Gdk.Pixbuf(null, "shotgun.png");
            itemImages[ItemType.ShotgunMagazine] = new Gdk.Pixbuf(null, "shotgun-ammo.png");
            itemImages[ItemType.UplinkChip] = new Gdk.Pixbuf(null, "uplink-chip.png");
            itemImages[ItemType.SphtDoorKey] = new Gdk.Pixbuf(null, "keycard.png");
            itemImages[ItemType.RedBall] = new Gdk.Pixbuf(null, "skull.png");
            itemImages[ItemType.Smg] = new Gdk.Pixbuf(null, "smg.png");
            itemImages[ItemType.SmgAmmo] = new Gdk.Pixbuf(null, "smg-ammo.png");

            LineWidth = Math.Max(1, Math.Min(8, Weland.Settings.GetSetting("Drawer/LineWidth", 1)));
            VertexSize = Math.Max(2, Math.Min(16, Weland.Settings.GetSetting("Drawer/VertexSize", 2)));
            Antialias = Weland.Settings.GetSetting("Drawer/SmoothLines", true);

            LoadColors();
        }

        public Drawer.Color backgroundColor;
        public Drawer.Color pointColor;
        public Drawer.Color annotationColor;
        public Drawer.Color impassableLineColor;
        public Drawer.Color solidLineColor;
        public Drawer.Color transparentLineColor;
        public Drawer.Color selectedLineColor;
        public Drawer.Color selectedPolygonColor;
        public Drawer.Color destinationPolygonColor;
        public Drawer.Color polygonColor;
        readonly Drawer.Color luaTriggerColor = new Drawer.Color(1.0, 0.65, 0.82);
        public Drawer.Color invalidPolygonColor;
        public Drawer.Color gridLineColor;
        public Drawer.Color gridPointColor;
        public Drawer.Color objectColor;
        public Drawer.Color playerColor;
        public Drawer.Color monsterColor;
        public Drawer.Color civilianColor;

        Gdk.Pixbuf sceneryImage = new Gdk.Pixbuf(null, "flower.png");
        Gdk.Pixbuf soundImage = new Gdk.Pixbuf(null, "sound.png");
        Gdk.Pixbuf goalImage = new Gdk.Pixbuf(null, "flag.png");
        Gdk.Pixbuf visualModeImage = new Gdk.Pixbuf(null, "dingle.png");

        Dictionary<ItemType, Gdk.Pixbuf> itemImages = new Dictionary<ItemType, Gdk.Pixbuf>();

        protected override bool OnDrawn(Cairo.Context cr)
        {
            drawer = new CairoDrawer(cr, Antialias, LineWidth, VertexSize);
            drawer.Clear(backgroundColor);

            if (Grid.Visible)
            {
                DrawGrid();
            }

            if (Level != null)
            {

                // clipping area to Map
                var rectangle = cr.ClipExtents();
                int Left = Transform.ToMapX(rectangle.X);
                int Right = Transform.ToMapX(rectangle.Width + rectangle.X);
                int Top = Transform.ToMapY(rectangle.Y);
                int Bottom = Transform.ToMapY(rectangle.Height + rectangle.Y);

                CohenSutherland[] Points = new CohenSutherland[Level.Endpoints.Count];

                for (short i = 0; i < Level.Endpoints.Count; ++i)
                {
                    Point p = Level.Endpoints[i];
                    Points[i] = CohenSutherland.Inside;
                    if (p.X < Left)
                    {
                        Points[i] |= CohenSutherland.Left;
                    }
                    else if (p.X > Right)
                    {
                        Points[i] |= CohenSutherland.Right;
                    }

                    if (p.Y < Top)
                    {
                        Points[i] |= CohenSutherland.Top;
                    }
                    else if (p.Y > Bottom)
                    {
                        Points[i] |= CohenSutherland.Bottom;
                    }
                }

                for (int i = 0; i < Level.Polygons.Count; ++i)
                {
                    Polygon polygon = Level.Polygons[i];
                    CohenSutherland code = ~CohenSutherland.Inside;
                    for (short v = 0; v < polygon.VertexCount; ++v)
                    {
                        code &= Points[polygon.EndpointIndexes[v]];
                    }
                    if (code == CohenSutherland.Inside && Filter(polygon))
                    {
                        DrawPolygon((short)i, Selection.Polygons.Contains((short)i) ? PolygonColor.Selected :
                            (Editor != null && (Editor.Tool == Tool.Select || Editor.Tool == Tool.Line) && Editor.Hover.Polygon == i ? PolygonColor.Hover : PolygonColor.Normal), cr);
                    }
                }

                if (Selection.Polygon != -1)
                {
                    Polygon polygon = Level.Polygons[Selection.Polygon];
                    CohenSutherland code = ~CohenSutherland.Inside;
                    if (Filter(polygon))
                    {
                        for (short i = 0; i < polygon.VertexCount; ++i)
                        {
                            code &= Points[polygon.EndpointIndexes[i]];
                        }
                        if (code == CohenSutherland.Inside)
                        {
                            DrawPolygon(Selection.Polygon, PolygonColor.Selected, cr);
                        }
                    }

                    if (polygon.Type == PolygonType.Teleporter && polygon.Permutation >= 0 && polygon.Permutation < Level.Polygons.Count)
                    {
                        Polygon destination = Level.Polygons[polygon.Permutation];
                        if (Filter(destination))
                        {
                            code = ~CohenSutherland.Inside;
                            for (short i = 0; i < destination.VertexCount; ++i)
                            {
                                code &= Points[destination.EndpointIndexes[i]];
                            }
                            if (code == CohenSutherland.Inside)
                            {
                                DrawPolygon(polygon.Permutation, PolygonColor.Destination, cr);
                            }
                        }
                    }
                }

                Line selectedLine = null;
                if (Selection.Line != -1)
                {
                    selectedLine = Level.Lines[Selection.Line];
                }

                for (int lineIndex = 0; lineIndex < Level.Lines.Count; ++lineIndex)
                {
                    Line line = Level.Lines[lineIndex];
                    if ((Points[line.EndpointIndexes[0]] & Points[line.EndpointIndexes[1]]) == CohenSutherland.Inside && line != selectedLine)
                    {
                        if (Level.FilterLine(Filter, line))
                        {
                            DrawLine(line, Selection.Lines.Contains((short)lineIndex), Editor != null && (Editor.Tool == Tool.Select || Editor.Tool == Tool.Line) && Editor.Hover.Line == lineIndex);
                        }
                    }
                }

                if (selectedLine != null)
                {
                    DrawLine(selectedLine);
                }

                for (short i = 0; i < Level.Endpoints.Count; ++i)
                {
                    if (Level.FilterPoint(Filter, i) && Points[i] == CohenSutherland.Inside)
                    {
                        if (Selection.Points.Contains(i)) DrawFatPoint(selectedLineColor, Level.Endpoints[i]);
                        else if (Editor != null && (Editor.Tool == Tool.Select || Editor.Tool == Tool.Line) && Editor.Hover.Point == i && Selection.Point != i)
                            DrawFatPoint(hoverColor, Level.Endpoints[i]);
                        else DrawPoint(Level.Endpoints[i]);
                    }
                }

                Annotation selectedAnnotation = null;
                if (Selection.Annotation != -1)
                {
                    selectedAnnotation = Level.Annotations[Selection.Annotation];
                }
                if (Mode == DrawMode.Draw)
                {
                    foreach (Annotation note in Level.Annotations)
                    {
                        if (note.PolygonIndex == -1 || Filter(Level.Polygons[note.PolygonIndex]))
                        {
                            DrawAnnotation(note, note == selectedAnnotation, cr);
                        }
                    }

                    int ObjectSize = (int)(16 / 2 / Transform.Scale);
                    MapObject selectedObj = null;
                    if (Selection.Object != -1)
                    {
                        selectedObj = Level.Objects[Selection.Object];
                    }
                    foreach (MapObject obj in Level.Objects)
                    {
                        if (obj != selectedObj && obj.X > Left - ObjectSize && obj.X < Right + ObjectSize && obj.Y > Top - ObjectSize && obj.Y < Bottom + ObjectSize)
                        {
                            if (obj.PolygonIndex == -1 || Filter(Level.Polygons[obj.PolygonIndex]))
                            {
                                DrawObject(obj, false);
                            }
                        }
                    }

                    if (Level.VisualModePolygonIndex != -1 &&
                        Level.VisualModePoint.X > Left - ObjectSize &&
                        Level.VisualModePoint.X < Right + ObjectSize &&
                        Level.VisualModePoint.Y > Top - ObjectSize &&
                        Level.VisualModePoint.Y < Bottom + ObjectSize)
                    {

                        DrawVisualMode(Level.VisualModePoint);
                    }

                    if (selectedObj != null && selectedObj.X > Left - ObjectSize && selectedObj.X < Right + ObjectSize && selectedObj.Y > Top - ObjectSize && selectedObj.Y < Bottom + ObjectSize)
                    {
                        if (selectedObj.PolygonIndex == -1 || Filter(Level.Polygons[selectedObj.PolygonIndex]))
                        {
                            DrawObject(selectedObj, true);
                        }
                    }
                }

                if (Level.TemporaryLineStartIndex != -1)
                {
                    // draw the temporarily drawn line
                    drawer.DrawLine(selectedLineColor, Transform.ToScreenPoint(Level.Endpoints[Level.TemporaryLineStartIndex]), Transform.ToScreenPoint(Level.TemporaryLineEnd));
                }

                if (Selection.Point != -1)
                {
                    // draw the selected point
                    DrawFatPoint(selectedLineColor, Level.Endpoints[Selection.Point]);
                }

                if (Editor != null && Editor.ShowRotationCenter())
                {
                    var center = new Drawer.Point(Transform.ToScreenX((short)Math.Round(Editor.RotationCenterX)), Transform.ToScreenY((short)Math.Round(Editor.RotationCenterY)));
                    drawer.DrawLine(selectedLineColor, new Drawer.Point(center.X - 6, center.Y), new Drawer.Point(center.X + 6, center.Y));
                    drawer.DrawLine(selectedLineColor, new Drawer.Point(center.X, center.Y - 6), new Drawer.Point(center.X, center.Y + 6));
                }

                if (Editor != null && Editor.Tool == Tool.BakeLighting && Editor.HasBakeLight)
                {
                    foreach (var piece in Editor.LightingPreview)
                    {
                        var points=new Drawer.Point[piece.Points.Count];
                        for(int i=0;i<points.Length;i++)points[i]=Transform.ToScreenPoint(piece.Points[i]);
                        drawer.FillPolygon(piece.Shadow ? new Drawer.Color(.18,.18,.22) : new Drawer.Color(.85,.75,.35),new List<Drawer.Point>(points));

                    }
                    var light=Transform.ToScreenPoint(Editor.BakeLightPosition);
                    var lightColor=new Drawer.Color(1, .8, 0);
                    drawer.DrawLine(lightColor,new Drawer.Point(light.X-8,light.Y),new Drawer.Point(light.X+8,light.Y));
                    drawer.DrawLine(lightColor,new Drawer.Point(light.X,light.Y-8),new Drawer.Point(light.X,light.Y+8));
                }

                if (Editor != null && Editor.DrawingCircle)
                {
                    Drawer.Color color = Editor.CircleMessage == null ? selectedLineColor : invalidPolygonColor;
                    var rings = Editor.DrawingCircleTessellation ? Editor.CircleTiles
                        : new System.Collections.Generic.List<System.Collections.Generic.List<Point>> { Editor.CircleVertices };
                    foreach (var points in rings)
                        for (int i = 0; i < points.Count; ++i)
                        {
                            drawer.DrawLine(color, Transform.ToScreenPoint(points[i]), Transform.ToScreenPoint(points[(i + 1) % points.Count]));
                            DrawFatPoint(color, points[i]);
                        }
                }

                if (Editor != null && Editor.Pasting)
                {
                    foreach (var edge in Editor.PastePreviewLines) drawer.DrawLine(selectedLineColor, Transform.ToScreenPoint(edge[0]), Transform.ToScreenPoint(edge[1]));
                    foreach (var point in Editor.PastePreviewPoints) DrawFatPoint(selectedLineColor, point);
                }
                if (Editor != null && Editor.DrawingRing)
                {
                    Drawer.Color color = Editor.RingMessage == null ? selectedLineColor : invalidPolygonColor;
                    var edges = new System.Collections.Generic.HashSet<(Point, Point)>();
                    var vertices = new System.Collections.Generic.HashSet<Point>();
                    foreach (var sector in Editor.RingSectors)
                        for (int i = 0; i < sector.Count; ++i)
                        {
                            Point p = sector[i], q = sector[(i + 1) % sector.Count];
                            if (p.X > q.X || (p.X == q.X && p.Y > q.Y)) { Point t = p; p = q; q = t; }
                            if (edges.Add((p, q))) drawer.DrawLine(color, Transform.ToScreenPoint(p), Transform.ToScreenPoint(q));
                            if (vertices.Add(p)) DrawFatPoint(color, p);
                            if (vertices.Add(q)) DrawFatPoint(color, q);
                        }
                }

                if (Editor != null && Editor.DrawingSelectionBox)
                {
                    Point a = Editor.SelectionBoxStart, b = Editor.SelectionBoxEnd;
                    Point[] corners = { a, new Point(b.X, a.Y), b, new Point(a.X, b.Y) };
                    for (int i = 0; i < 4; ++i)
                        drawer.DrawLine(selectedLineColor, Transform.ToScreenPoint(corners[i]), Transform.ToScreenPoint(corners[(i + 1) % 4]));
                }

                if (Editor != null && Editor.DrawingBox)
                {
                    Point a = Editor.BoxStart, b = Editor.BoxEnd;
                    Point[] corners = { a, new Point(b.X, a.Y), b, new Point(a.X, b.Y) };
                    Drawer.Color color = Editor.BoxMessage == null ? selectedLineColor : invalidPolygonColor;
                    for (int i = 0; i < 4; ++i)
                    {
                        drawer.DrawLine(color, Transform.ToScreenPoint(corners[i]), Transform.ToScreenPoint(corners[(i + 1) % 4]));
                        DrawFatPoint(color, corners[i]);
                    }
                }
            }

            drawer.Dispose();
            return true;
        }

        public void Center(short X, short Y)
        {
            int cx = (int)Math.Round(X - Allocation.Width / 2 / Transform.Scale);
            int cy = (int)Math.Round(Y - Allocation.Height / 2 / Transform.Scale);
            int maxX = (int)Math.Round(short.MaxValue - Allocation.Width / Transform.Scale);
            int maxY = (int)Math.Round(short.MaxValue - Allocation.Height / Transform.Scale);

            if (cx < short.MinValue)
            {
                cx = short.MinValue;
            }
            else if (cx > maxX)
            {
                cx = maxX;
            }
            if (cy < short.MinValue)
            {
                cy = short.MinValue;
            }
            else if (cy > maxY)
            {
                cy = maxY;
            }
            Transform.XOffset = (short)cx;
            Transform.YOffset = (short)cy;
        }

        void DrawPoint(Point point)
        {
            drawer.DrawPoint(pointColor, Transform.ToScreenPoint(point));
        }

        void DrawFatPoint(Drawer.Color color, Point point)
        {
            double r = VertexSize / 2.0 + 1;
            List<Drawer.Point> points = new List<Drawer.Point>();
            double X = Transform.ToScreenX(point.X);
            double Y = Transform.ToScreenY(point.Y);
            points.Add(new Drawer.Point(X - r, Y - r));
            points.Add(new Drawer.Point(X + r, Y - r));
            points.Add(new Drawer.Point(X + r, Y + r));
            points.Add(new Drawer.Point(X - r, Y + r));
            drawer.FillStrokePolygon(color, new Drawer.Color(0, 0, 0), points, false);
        }

        void DrawGrid()
        {
            Point p1 = new Point();
            Point p2 = new Point();

            short Left = Transform.ToMapX(0);
            short Right = Transform.ToMapX(Allocation.Width);
            short Top = Transform.ToMapY(0);
            short Bottom = Transform.ToMapY(Allocation.Height);

            /*** begin custom grid code ***/

            double s, c, lpx, lpy, tmp, tmp2;
            int nlines, wu, intoffpar, intoffperp, intskip, r;

            if (!Grid.UseCustomGrid)
            {
                // Thin grids become an expensive solid haze when zoomed out.
                // Decimate only the display; snapping still uses Grid.Resolution.
                int displayResolution=Math.Max(1,(int)Grid.Resolution);
                while(displayResolution*Transform.Scale<8 && displayResolution<32768)displayResolution*=2;
                //the original code
                // draw horizontal map lines
                for (int j = (Top / displayResolution) * displayResolution; j < Bottom; j += displayResolution)
                {
                    p1.X = Left;
                    p1.Y = (short)j;
                    p2.X = Right;
                    p2.Y = (short)j;

                    drawer.DrawGridLine(gridLineColor, Transform.ToScreenPoint(p1), Transform.ToScreenPoint(p2));
                }

                // draw vertical map lines
                for (int i = (Left / displayResolution) * displayResolution; i < Right; i += displayResolution)
                {
                    p1.X = (short)i;
                    p1.Y = Top;
                    p2.X = (short)i;
                    p2.Y = Bottom;

                    drawer.DrawGridLine(gridLineColor, Transform.ToScreenPoint(p1), Transform.ToScreenPoint(p2));
                }

                // draw grid intersects
                wu = Math.Max(1024, displayResolution);
                while(wu*Transform.Scale<16 && wu<32768)wu*=2;
                for (int i = (Left / wu) * wu; i < Right; i += wu)
                {
                    for (int j = (Top / wu) * wu; j < Bottom; j += wu)
                    {
                        p1.X = (short)i;
                        p1.Y = (short)j;
                        drawer.DrawGridIntersect(gridPointColor, Transform.ToScreenPoint(p1));
                    }
                }
                return;
            }

            //lpx and lpy represent a point on a line. they are used to write the parametric form of each line,
            //x(t) = lpx + t*c ,  y(t) = lpy - t*s   for lines that are horizontal when the rotation angle is 0, and
            //x(t) = lpx + t*s ,  y(t) = lpy + t*c   for the perpendicular lines.

            int displayStride=1;
            r=Math.Max(1,(int)(Grid.Resolution*Grid.Scale));
            while(r*Transform.Scale<8 && r<32768) { r*=2;displayStride*=2; }
            c = Math.Cos(Grid.Rotation * 2 * Math.PI / 360);
            s = Math.Sin(Grid.Rotation * 2 * Math.PI / 360);
            lpx = Grid.Center.X; lpy = Grid.Center.Y;

            //draw the lines that are horizontal with no rotation
            //find the line that is closest to the upper left corner and move (lpx,lpy) onto it
            tmp = c * (Left - lpx) - s * (Top - lpy);
            //tmp=r*Math.Ceiling(tmp/r);
            lpx += tmp * c; lpy -= tmp * s;
            if (Math.Abs(s) > Math.Abs(c)) tmp = Math.Ceiling((Left - lpx) / (r * s));
            else tmp = Math.Ceiling((Top - lpy) / (r * c));
            lpx += r * tmp * s; lpy += r * tmp * c;

            //calculate the number of lines to draw
            tmp = c * (Right - lpx) - s * (Bottom - lpy);
            if (Math.Abs(s) > Math.Abs(c)) nlines = (int)Math.Ceiling((Right - lpx - tmp * c) / (r * s));
            else nlines = (int)Math.Ceiling((Bottom - lpy + tmp * s) / (r * c));

            //draw the lines
            for (; nlines > 0; nlines--)
            {
                //calculate the intersection of this line with the edges of the viewing area
                //have to use a double temporary value because of precision issues
                tmp = lpx + (lpy - Bottom) * c / s;
                //i used to check here in case s is close to 0 but for some reason that doesn't seem necessary
                if (tmp < Left) { p1.X = Left; p1.Y = (short)(lpy + (lpx - Left) * s / c); }
                else { p1.X = (short)tmp; p1.Y = Bottom; }
                tmp = lpx + (lpy - Top) * c / s;
                if (tmp > Right) { p2.X = Right; p2.Y = (short)(lpy + (lpx - Right) * s / c); }
                else { p2.X = (short)tmp; p2.Y = Top; }
                drawer.DrawGridLine(gridLineColor, Transform.ToScreenPoint(p1), Transform.ToScreenPoint(p2));

                //move to the next line
                lpx += r * s; lpy += r * c;
            }

            //do the same for the perpendicular lines, drawing from bottom left to upper right
            //also draw intersections while drawing lines since it's easier to do that here than afterward
            //fist calculate the number of lines between drawn intersections
            wu = Math.Max(1024, (int)Grid.Resolution);
            intskip = Math.Max(1, wu / Math.Max(1,Grid.Resolution*displayStride));
            while(r*intskip*Transform.Scale<16 && intskip<32768)intskip*=2;
            //note that i use Grid.Resolution instead of r here because the scaling factor should multiply wu, which would then cancel in the division

            lpx = Grid.Center.X; lpy = Grid.Center.Y;
            tmp = s * (Left - lpx) + c * (Bottom - lpy);
            tmp = r * Math.Ceiling(tmp / r);
            lpx += tmp * s; lpy += tmp * c;
            //save the offset between the center and the next intersection that needs to be drawn, parallel to the lines
            intoffpar = ((int)(tmp / r)) % intskip;
            if (Math.Abs(c) > Math.Abs(s)) tmp = Math.Ceiling((Left - lpx) / (r * c));
            else tmp = Math.Ceiling((Bottom - lpy) / (r * -s));
            lpx += r * tmp * c; lpy += r * tmp * -s;
            intoffperp = ((int)tmp) % intskip;  //same as above but in the perpendicular direction
            tmp = s * (Right - lpx) + c * (Top - lpy);
            if (Math.Abs(c) > Math.Abs(s)) nlines = (int)Math.Ceiling((Right - lpx - tmp * s) / (r * c));
            else nlines = (int)Math.Ceiling((Top - lpy - tmp * c) / (r * -s));

            for (int j = 0; j < nlines; j++)
            {
                tmp = lpx - (lpy - Bottom) * s / c;
                if (tmp > Right) { p1.X = Right; p1.Y = (short)(lpy - (lpx - Right) * c / s); }
                else { p1.X = (short)tmp; p1.Y = Bottom; }
                tmp = lpx - (lpy - Top) * s / c;
                if (tmp < Left) { p2.X = Left; p2.Y = (short)(lpy - (lpx - Left) * c / s); }
                else { p2.X = (short)tmp; p2.Y = Top; }
                drawer.DrawGridLine(gridLineColor, Transform.ToScreenPoint(p1), Transform.ToScreenPoint(p2));

                //draw the intersections
                //this is done in two steps, once for either direction away from the perpendicular line passing through (lpx,lpy)
                //only do this when we are at one of the intersections that needs drawing
                if ((j + intoffperp) % intskip == 0)
                {
                    //find the next line 
                    tmp = lpx - r * intoffpar * s; tmp2 = lpy - r * intoffpar * c;
                    //this seems a little silly, but i don't know of an easier/better way around it; when the screen contains portions of the world
                    //near the edges, we might start out offworld - so just keep hopping over lines until we're onworld or we've gone too far
                    while ((tmp < short.MinValue || tmp2 < short.MinValue || tmp > short.MaxValue || tmp2 > short.MaxValue) && (tmp > Left && tmp2 > Top))
                    {
                        tmp -= r * intskip * s; tmp2 -= r * intskip * c;
                    }
                    while (tmp > Left && tmp2 > Top)
                    {
                        p1.X = (short)tmp; p1.Y = (short)tmp2;
                        drawer.DrawGridIntersect(gridPointColor, Transform.ToScreenPoint(p1));
                        tmp -= r * intskip * s; tmp2 -= r * intskip * c;
                    }
                    tmp = lpx + r * (intskip - intoffpar) * s; tmp2 = lpy + r * (intskip - intoffpar) * c;
                    while ((tmp < short.MinValue || tmp2 < short.MinValue || tmp > short.MaxValue || tmp2 > short.MaxValue) && (tmp < Right && tmp2 < Bottom))
                    {
                        tmp += r * intskip * s; tmp2 += r * intskip * c;
                    }
                    while (tmp < Right && tmp2 < Bottom)
                    {
                        p1.X = (short)tmp; p1.Y = (short)tmp2;
                        drawer.DrawGridIntersect(gridPointColor, Transform.ToScreenPoint(p1));
                        tmp += r * intskip * s; tmp2 += r * intskip * c;
                    }
                }

                lpx += r * c; lpy -= r * s;
            }

            /*** end custom grid code ***/
        }


        public Drawer.Color hoverColor;
        public Drawer.Color polygonHoverColor;

        void DrawLine(Line line, bool selected = false, bool hovered = false)
        {
            Point p1 = Level.Endpoints[line.EndpointIndexes[0]];
            Point p2 = Level.Endpoints[line.EndpointIndexes[1]];

            Drawer.Color color = solidLineColor;
            if (selected || (Selection.Line != -1 && line == Level.Lines[Selection.Line]))
            {
                color = selectedLineColor;
            }
            else if (hovered)
            {
                color = hoverColor;
            }
            else if (line.Transparent)
            {
                color = transparentLineColor;
            }
            else if (line.Solid && line.ClockwisePolygonOwner != -1 && line.CounterclockwisePolygonOwner != -1)
            {
                Polygon poly1 = Level.Polygons[line.ClockwisePolygonOwner];
                Polygon poly2 = Level.Polygons[line.CounterclockwisePolygonOwner];
                if (poly1.FloorHeight != poly2.FloorHeight)
                {
                    color = impassableLineColor;
                }
            }

            drawer.DrawLine(color, Transform.ToScreenPoint(p1), Transform.ToScreenPoint(p2));
        }

        enum PolygonColor
        {
            Normal,
            Selected,
            Destination,
            Hover
        }

        Drawer.Color PaintColor(short index)
        {
            // Imported maps and live edits may introduce values before the
            // active palette has been rebuilt. Keep those polygons drawable.
            return PaintColors.TryGetValue(index, out Drawer.Color color) ? color : polygonColor;
        }

        void DrawPolygon(short polygon_index, PolygonColor color, Cairo.Context cr)
        {
            Polygon polygon = Level.Polygons[polygon_index];
            List<Drawer.Point> points = new List<Drawer.Point>();
            for (int i = 0; i < polygon.VertexCount; ++i)
            {
                points.Add(Transform.ToScreenPoint(Level.Endpoints[polygon.EndpointIndexes[i]]));
            }
            if (Mode == DrawMode.FloorHeight)
            {
                drawer.FillPolygon(PaintColor(polygon.FloorHeight), points);
            }
            else if (Mode == DrawMode.CeilingHeight)
            {
                drawer.FillPolygon(PaintColor(polygon.CeilingHeight), points);
            }
            else if (Mode == DrawMode.PolygonType)
            {
                drawer.FillPolygon(PaintColor((short)polygon.Type), points);
            }
            else if (Mode == DrawMode.FloorLight)
            {
                drawer.FillPolygon(PaintColor((short)polygon.FloorLight), points);
            }
            else if (Mode == DrawMode.CeilingLight)
            {
                drawer.FillPolygon(PaintColor((short)polygon.CeilingLight), points);
            }
            else if (Mode == DrawMode.MediaLight)
            {
                drawer.FillPolygon(PaintColor((short)polygon.MediaLight), points);
            }
            else if (Mode == DrawMode.Media)
            {
                drawer.FillPolygon(PaintColor((short)polygon.MediaIndex), points);
            }
            else if (Mode == DrawMode.AmbientSound)
            {
                drawer.FillPolygon(PaintColor((short)polygon.AmbientSound), points);
            }
            else if (Mode == DrawMode.RandomSound)
            {
                drawer.FillPolygon(PaintColor((short)polygon.RandomSound), points);
            }
            else if (Mode == DrawMode.FloorTexture)
            {
                drawer.TexturePolygon(polygon.FloorTexture, points);
            }
            else if (Mode == DrawMode.CeilingTexture)
            {
                drawer.TexturePolygon(polygon.CeilingTexture, points);
            }
            else
            {
                if (color == PolygonColor.Selected)
                {
                    drawer.FillPolygon(selectedPolygonColor, points);
                }
                else if (color == PolygonColor.Hover)
                {
                    drawer.FillPolygon(polygonHoverColor, points);
                }
                else if (color == PolygonColor.Destination)
                {
                    drawer.FillPolygon(destinationPolygonColor, points);
                }
                else if (polygon.Concave)
                {
                    drawer.FillPolygon(invalidPolygonColor, points);
                }
                else if (polygon.LuaTriggerEnabled && !string.IsNullOrWhiteSpace(polygon.LuaTriggerCode))
                {
                    drawer.FillPolygon(luaTriggerColor, points);
                }
                else
                {
                    drawer.FillPolygon(polygonColor, points);
                }
            }

            if (Mode == DrawMode.Draw && polygon.Type == PolygonType.Platform)
            {
                ((CairoDrawer)drawer).Flush();
                Drawer.Point center = Transform.ToScreenPoint(Level.PolygonCenter(polygon));
                using Layout layout = new Layout(PangoContext);
                layout.SetMarkup(String.Format("{0}", polygon_index));
                layout.GetPixelSize(out int width, out int height);
                cr.Save();
                cr.MoveTo((int)center.X - width / 2, (int)center.Y - height / 2);
                CairoHelper.ShowLayout(cr, layout);
                cr.Restore();
            }
        }

        void DrawTriangle(Drawer.Color c, double X, double Y, double angle, bool highlight, bool invisible)
        {
            double rads = angle * Math.PI / 180;
            List<Drawer.Point> points = new List<Drawer.Point>();
            points.Add(new Drawer.Point(X + 8 * Math.Cos(rads), Y + 8 * Math.Sin(rads)));
            points.Add(new Drawer.Point(X + 10 * Math.Cos(rads + 2 * Math.PI * 0.4), Y + 10 * Math.Sin(rads + 2 * Math.PI * 0.4)));
            points.Add(new Drawer.Point(X + 10 * Math.Cos(rads - 2 * Math.PI * 0.4), Y + 10 * Math.Sin(rads - 2 * Math.PI * 0.4)));

            Drawer.Color stroke = new Drawer.Color(0, 0, 0);
            if (highlight)
            {
                drawer.FillStrokePolygon(stroke, c, points, invisible);
            }
            else
            {
                drawer.FillStrokePolygon(c, stroke, points, invisible);
            }

        }

        void DrawImage(Gdk.Pixbuf image, double X, double Y, bool highlight)
        {
            int x = (int)X - image.Width / 2;
            int y = (int)Y - image.Height / 2;

            Cairo.Context context = ((CairoDrawer)drawer).Context;

            if (highlight)
            {
                List<Drawer.Point> points = new List<Drawer.Point>();
                points.Add(new Drawer.Point(x - 2.5, y - 2.5));
                points.Add(new Drawer.Point(x + image.Width + 1.5, y - 2.5));
                points.Add(new Drawer.Point(x + image.Width + 1.5, y + image.Height + 1.5));
                points.Add(new Drawer.Point(x - 2.5, y + image.Height + 1.5));
                drawer.FillPolygon(new Drawer.Color(1, 1, 0), points);
            }

            ((CairoDrawer)drawer).Flush();
            Gdk.CairoHelper.SetSourcePixbuf(context, image, x, y);
            context.Paint();
        }

        void DrawObject(MapObject obj, bool highlight)
        {
            if (obj.Type == ObjectType.Player)
            {
                if (ShowPlayers)
                {
                    DrawTriangle(playerColor, Transform.ToScreenX(obj.X), Transform.ToScreenY(obj.Y), obj.Facing, highlight, false);
                }
            }
            else if (obj.Type == ObjectType.Monster)
            {
                if (ShowMonsters)
                {
                    Drawer.Color color;
                    if ((obj.Index >= 12 && obj.Index <= 15) || (obj.Index >= 43 && obj.Index <= 46))
                    {
                        color = civilianColor;
                    }
                    else
                    {
                        color = monsterColor;
                    }

                    DrawTriangle(color, Transform.ToScreenX(obj.X), Transform.ToScreenY(obj.Y), obj.Facing, highlight, obj.Invisible);
                }
            }
            else if (obj.Type == ObjectType.Scenery)
            {
                if (ShowScenery)
                {
                    DrawImage(sceneryImage, Transform.ToScreenX(obj.X), Transform.ToScreenY(obj.Y), highlight);
                }
            }
            else if (obj.Type == ObjectType.Sound)
            {
                if (ShowSounds)
                {
                    DrawImage(soundImage, Transform.ToScreenX(obj.X), Transform.ToScreenY(obj.Y), highlight);
                }
            }
            else if (obj.Type == ObjectType.Goal)
            {
                if (ShowGoals)
                {
                    DrawImage(goalImage, Transform.ToScreenX(obj.X), Transform.ToScreenY(obj.Y), highlight);
                }
            }
            else if (obj.Type == ObjectType.Item)
            {
                if (ShowObjects)
                {
                    if (itemImages.ContainsKey((ItemType)obj.Index) && itemImages[(ItemType)obj.Index] != null)
                    {
                        DrawImage(itemImages[(ItemType)obj.Index], Transform.ToScreenX(obj.X), Transform.ToScreenY(obj.Y), highlight);
                    }
                    else
                    {
                        drawer.DrawPoint(objectColor, new Drawer.Point(Transform.ToScreenX(obj.X), Transform.ToScreenY(obj.Y)));
                    }
                }
            }
        }

        void DrawVisualMode(Point p)
        {
            DrawImage(visualModeImage, Transform.ToScreenX(p.X), Transform.ToScreenY(p.Y), false);
        }

        void DrawAnnotation(Annotation note, bool selected, Cairo.Context cr)
        {
            ((CairoDrawer)drawer).Flush();
            int X = (int)Transform.ToScreenX(note.X);
            int Y = (int)Transform.ToScreenY(note.Y);
            using Layout layout = new Layout(PangoContext);
            layout.SetMarkup(note.Text);
            layout.GetPixelSize(out int width, out int height);
            cr.Save();
            cr.MoveTo(X, Y - height);
            CairoHelper.ShowLayout(cr, layout);
            cr.Restore();
            if (selected)
            {
                DrawFatPoint(selectedLineColor, new Point(note.X, note.Y));
            }
            else
            {
                DrawFatPoint(annotationColor, new Point(note.X, note.Y));
            }
        }

        Drawer.Color LoadColor(string xPath, Drawer.Color defaultColor)
        {
            Drawer.Color c;
            c.R = Weland.Settings.GetSetting(xPath + "/Red", defaultColor.R);
            c.G = Weland.Settings.GetSetting(xPath + "/Green", defaultColor.G);
            c.B = Weland.Settings.GetSetting(xPath + "/Blue", defaultColor.B);

            return c;
        }

        void SaveColor(string xPath, Drawer.Color c)
        {
            Weland.Settings.PutSetting(xPath + "/Red", c.R);
            Weland.Settings.PutSetting(xPath + "/Green", c.G);
            Weland.Settings.PutSetting(xPath + "/Blue", c.B);
        }

        public void DefaultColors()
        {
            backgroundColor = new Drawer.Color(0.33, 0.33, 0.33);
            pointColor = new Drawer.Color(1, 0, 0);
            annotationColor = new Drawer.Color(0, 0, 1);
            impassableLineColor = new Drawer.Color(0.2, 0.98, 0.48);
            solidLineColor = new Drawer.Color(0, 0, 0);
            transparentLineColor = new Drawer.Color(0.2, 0.8, 0.8);
            selectedLineColor = new Drawer.Color(1, 1, 0);
            hoverColor = new Drawer.Color(0.2, 0.8, 1.0);
            polygonHoverColor = new Drawer.Color(0.535, 0.835, 0.935);
            selectedPolygonColor = new Drawer.Color((double)0xff / 0xff,
                                (double)0xcc / 0xff,
                                (double)0x66 / 0xff);
            destinationPolygonColor = new Drawer.Color((double)0x99 / 0xff,
                                   (double)0xbb / 0xff,
                                   (double)0x55 / 0xff);
            polygonColor = new Drawer.Color(0.87, 0.87, 0.87);
            invalidPolygonColor = new Drawer.Color((double)0xfb / 0xff,
                               (double)0x48 / 0xff,
                               (double)0x09 / 0xff);
            gridLineColor = new Drawer.Color(0.6, 0.6, 0.6);
            gridPointColor = new Drawer.Color(0, 0.8, 0.8);
            objectColor = new Drawer.Color(1, 1, 0);
            playerColor = new Drawer.Color(1, 1, 0);
            monsterColor = new Drawer.Color(1, 0, 0);
            civilianColor = new Drawer.Color(0, 0, 1);
        }

        string colorsPrefix = "Drawer/Colors/";

        public void LoadColors()
        {
            DefaultColors();
            backgroundColor = LoadColor(colorsPrefix + "Background", backgroundColor);
            pointColor = LoadColor(colorsPrefix + "Point", pointColor);
            annotationColor = LoadColor(colorsPrefix + "Annotation", annotationColor);
            impassableLineColor = LoadColor(colorsPrefix + "ImpassableLine", impassableLineColor);
            solidLineColor = LoadColor(colorsPrefix + "Line", solidLineColor);
            transparentLineColor = LoadColor(colorsPrefix + "TransparentLine", transparentLineColor);
            selectedLineColor = LoadColor(colorsPrefix + "Selection", selectedLineColor);
            hoverColor = LoadColor(colorsPrefix + "Hover", hoverColor);
            selectedPolygonColor = LoadColor(colorsPrefix + "SelectedPolygon", selectedPolygonColor);
            destinationPolygonColor = LoadColor(colorsPrefix + "TargetPolygon", destinationPolygonColor);
            polygonColor = LoadColor(colorsPrefix + "Polygon", polygonColor);
            // Preserve the previous hover fill until a separate color is saved.
            polygonHoverColor = LoadColor(colorsPrefix + "PolygonHover", new Drawer.Color(
                (polygonColor.R + hoverColor.R) / 2, (polygonColor.G + hoverColor.G) / 2,
                (polygonColor.B + hoverColor.B) / 2));
            invalidPolygonColor = LoadColor(colorsPrefix + "InvalidPolygon", invalidPolygonColor);
            gridLineColor = LoadColor(colorsPrefix + "GridLine", gridLineColor);
            gridPointColor = LoadColor(colorsPrefix + "GridPoint", gridPointColor);
            objectColor = LoadColor(colorsPrefix + "Object", objectColor);
            playerColor = LoadColor(colorsPrefix + "Player", playerColor);
            monsterColor = LoadColor(colorsPrefix + "Monster", monsterColor);
            civilianColor = LoadColor(colorsPrefix + "Civilian", civilianColor);
        }

        public void SaveColors()
        {
            SaveColor(colorsPrefix + "Background", backgroundColor);
            SaveColor(colorsPrefix + "Point", pointColor);
            SaveColor(colorsPrefix + "Annotation", annotationColor);
            SaveColor(colorsPrefix + "ImpassableLine", impassableLineColor);
            SaveColor(colorsPrefix + "Line", solidLineColor);
            SaveColor(colorsPrefix + "TransparentLine", transparentLineColor);
            SaveColor(colorsPrefix + "Selection", selectedLineColor);
            SaveColor(colorsPrefix + "Hover", hoverColor);
            SaveColor(colorsPrefix + "PolygonHover", polygonHoverColor);
            SaveColor(colorsPrefix + "SelectedPolygon", selectedPolygonColor);
            SaveColor(colorsPrefix + "TargetPolygon", destinationPolygonColor);
            SaveColor(colorsPrefix + "Polygon", polygonColor);
            SaveColor(colorsPrefix + "InvalidPolygon", invalidPolygonColor);
            SaveColor(colorsPrefix + "GridLine", gridLineColor);
            SaveColor(colorsPrefix + "GridPoint", gridPointColor);
            SaveColor(colorsPrefix + "Object", objectColor);
            SaveColor(colorsPrefix + "Player", playerColor);
            SaveColor(colorsPrefix + "Monster", monsterColor);
            SaveColor(colorsPrefix + "Civilian", civilianColor);
        }
    }
}
