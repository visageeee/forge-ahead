using Cairo;
using System;
using System.Collections.Generic;

namespace Weland
{
    public class CairoDrawer : Drawer
    {

        readonly Context context;
        // Flush before callers paint text or images directly onto the context.
        public Context Context { get { Flush();return context; } }
        enum BatchKind { None, Stroke, Fill }
        BatchKind batchKind;
        Color batchColor;
        double batchWidth;
        int batchItems;

        void BeginBatch(BatchKind kind,Color color,double width=0)
        {
            if(batchKind!=kind || batchColor.R!=color.R || batchColor.G!=color.G || batchColor.B!=color.B || batchWidth!=width || batchItems>=2048)Flush();
            if(batchKind==BatchKind.None) { context.NewPath();batchKind=kind;batchColor=color;batchWidth=width; }
            batchItems++;
        }

        public void Flush()
        {
            if(batchKind==BatchKind.None)return;
            context.Save();context.SetSourceRGB(batchColor.R,batchColor.G,batchColor.B);
            if(batchKind==BatchKind.Stroke) { context.LineWidth=batchWidth;context.Stroke(); }
            else context.Fill();
            context.Restore();batchKind=BatchKind.None;batchItems=0;
        }

        readonly double lineWidth;
        readonly double vertexSize;

        public CairoDrawer(Context context, bool antialias, double lineWidth = 1, double vertexSize = 2)
        {
            if (!antialias)
            {
                context.Antialias = Antialias.None;
            }
            this.context = context;
            this.lineWidth = lineWidth;
            this.vertexSize = vertexSize;
        }

        class TextureSurface : IDisposable
        {
            public TextureSurface(ShapeDescriptor d)
            {
                var texture = Weland.Shapes.GetShape(d);
                if (d.Collection < 27)
                {
                    texture = texture.RotateCW();
                }

                surface = ImageUtilities.ImageToSurface(texture);
            }

            public void Dispose()
            {
                surface.Dispose();
            }

            public ImageSurface Surface
            {
                get { return surface; }
            }

            private ImageSurface surface;
        }

        class TextureCache : IDisposable
        {
            public TextureCache()
            {
                Weland.ShapesChanged += new ShapesFileChangedEventHandler(OnShapesChanged);
            }

            public void Dispose()
            {
                Weland.ShapesChanged -= new ShapesFileChangedEventHandler(OnShapesChanged);
                foreach (var kvp in cache)
                {
                    kvp.Value.Dispose();
                }
            }

            public void OnShapesChanged()
            {
                foreach (var kvp in cache)
                {
                    kvp.Value.Dispose();
                }
                cache.Clear();
            }

            public ImageSurface GetSurface(ShapeDescriptor d)
            {
                if (!cache.ContainsKey((ushort)d))
                {
                    cache.Add((ushort)d, new TextureSurface(d));
                }

                return cache[(ushort)d].Surface;
            }

            Dictionary<ushort, TextureSurface> cache = new Dictionary<ushort, TextureSurface>();
        }

        static TextureCache cache = new TextureCache();

        public override void Clear(Color c)
        {
            Flush();
            context.Save();

            context.SetSourceRGB(c.R, c.G, c.B);
            context.Paint();

            context.Restore();
        }

        public override void DrawPoint(Color c, Point p)
        {
            BeginBatch(BatchKind.Fill,c);
            double radius=vertexSize/2;
            context.Rectangle(p.X+0.5-radius,p.Y+0.5-radius,vertexSize,vertexSize);
        }

        public override void DrawGridIntersect(Color c, Point p)
        {
            BeginBatch(BatchKind.Stroke,c,1);
            context.MoveTo(p.X-0.5,p.Y+0.5);context.LineTo(p.X+1.5,p.Y+0.5);context.ClosePath();
            context.MoveTo(p.X+0.5,p.Y-0.5);context.LineTo(p.X+0.5,p.Y+1.5);context.ClosePath();
        }

        public override void DrawLine(Color c, Point p1, Point p2)
        {
            StrokeLine(c, p1, p2, lineWidth);
        }

        public override void DrawGridLine(Color c, Point p1, Point p2)
        {
            StrokeLine(c, p1, p2, 1);
        }

        void StrokeLine(Color c, Point p1, Point p2, double width)
        {
            BeginBatch(BatchKind.Stroke,c,width);
            context.MoveTo(p1.X+0.5,p1.Y+0.5);context.LineTo(p2.X+0.5,p2.Y+0.5);context.ClosePath();
        }

        void OutlinePolygon(List<Point> points)
        {
            context.MoveTo(new PointD(points[0].X + 0.5, points[0].Y + 0.5));
            for (int i = 1; i < points.Count; ++i)
            {
                context.LineTo(new PointD(points[i].X + 0.5, points[i].Y + 0.5));
            }
            context.ClosePath();
        }

        public override void FillPolygon(Color c, List<Point> points)
        {
            BeginBatch(BatchKind.Fill,c);
            OutlinePolygon(points);
        }

        public override void FillStrokePolygon(Color fill, Color stroke, List<Point> points, bool dashed)
        {
            Flush();
            context.Save();

            OutlinePolygon(points);
            context.SetSourceRGB(fill.R, fill.G, fill.B);
            context.FillPreserve();

            if (dashed)
            {
                context.SetDash(new double[] { 2.0, 2.0 }, 0);
            }
            context.SetSourceRGB(stroke.R, stroke.G, stroke.B);
            context.LineWidth = 1.0;
            context.Stroke();

            context.Restore();
        }

        public override void TexturePolygon(ShapeDescriptor d, List<Point> points)
        {
            Flush();
            context.Save();

            OutlinePolygon(points);
            using (SurfacePattern pattern = new SurfacePattern(cache.GetSurface(d)))
            {
                pattern.Extend = Cairo.Extend.Repeat;
                context.SetSource(pattern);
                context.Fill();
            }
            context.Restore();
        }

        public override void Dispose() { Flush(); }

    }
}
