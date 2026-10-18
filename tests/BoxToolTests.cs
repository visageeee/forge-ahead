using System;
using System.Linq;
using Weland;

namespace Weland
{
    // The editor settings normally live on the GTK application entry point.
    public static class Weland
    {
        public static Settings Settings = new Settings(false);
    }
}

class BoxToolTests
{
    static Point P(int x, int y) => new Point((short)x, (short)y);
    static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
    static void Box(Level level, int x0, int y0, int x1, int y1)
    {
        Check(level.CreateBox(P(x0, y0), P(x1, y1), out string reason), reason);
        Validate(level);
    }
    static string Snapshot(Level level) => string.Join("|", level.Save().Chunks.OrderBy(p => p.Key)
        .Select(p => p.Key + ":" + Convert.ToBase64String(p.Value)));
    static string CanonicalSnapshot(Level level)
    {
        var loaded = new Level(); loaded.Load(level.Save().Clone());
        return Snapshot(loaded);
    }
    static void Reject(Level level, int x0, int y0, int x1, int y1)
    {
        string before = Snapshot(level);
        Check(!level.CreateBox(P(x0, y0), P(x1, y1), out string reason), "Invalid box accepted");
        Check(!string.IsNullOrEmpty(reason), "Missing rejection explanation");
        Check(Snapshot(level) == before, "Rejected box changed the map");
    }
    static void Validate(Level level)
    {
        Check(level.Endpoints.Distinct().Count() == level.Endpoints.Count, "Duplicate vertex");
        var edges = level.Lines.Select(l => string.Join(",", l.EndpointIndexes.OrderBy(i => i)));
        Check(edges.Distinct().Count() == level.Lines.Count, "Duplicate line");
        for (int i = 0; i < level.Polygons.Count; ++i)
        {
            Polygon polygon = level.Polygons[i];
            Check(polygon.VertexCount >= 3 && polygon.VertexCount <= 8, "Bad vertex count");
            Check(!polygon.Concave, "Box or neighbor became concave");
            for (int j = 0; j < polygon.VertexCount; ++j)
            {
                short point = polygon.EndpointIndexes[j];
                Line line = level.Lines[polygon.LineIndexes[j]];
                Check(line.EndpointIndexes.Contains(point), "Polygon endpoint does not match line");
                Check(line.EndpointIndexes.Contains(polygon.EndpointIndexes[(j + polygon.VertexCount - 1) % polygon.VertexCount]), "Broken polygon ring");
                Check(line.ClockwisePolygonOwner == i || line.CounterclockwisePolygonOwner == i, "Missing line owner");
                Check(level.EndpointPolygons[point].Contains(polygon), "Missing endpoint polygon cache");
            }
        }
        foreach (Line line in level.Lines)
            foreach (short point in line.EndpointIndexes)
                Check(level.EndpointLines[point].Contains(line), "Missing endpoint line cache");
    }
    static Editor Editor(Level level) => new Editor {
        Level = level, Grid = new Grid { Visible = false }, Selection = new Selection(), Scale = 1,
        DefaultSnapDistance = 8, Tool = Tool.Box, BakeWholeMap = false
    };

    static void Main(string[] args)
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        int passed = 0;
        void Test(string name, Action action) { action(); Console.WriteLine("PASS " + name); ++passed; }
        byte[] PictureFixture(int depth, bool packed, byte[] pixels, int width = 8) {
            using var stream = new System.IO.MemoryStream(); using var writer = new BinaryWriterBE(stream);
            void W(int n) => writer.Write((ushort)n);
            void Rect() { W(0); W(0); W(1); W(width); }
            W(0); Rect(); W(0x11); W(0x2ff); W(depth > 8 ? 0x9a : 0x98);
            if (depth > 8) writer.Write(0);
            W(0x8000 | (width * depth / 8)); Rect(); W(0); W(packed ? 0 : 1);
            writer.Write(new byte[14]); W(depth); W(depth > 8 ? 3 : 1); writer.Write(new byte[14]);
            if (depth <= 8) {
                writer.Write(0); W(0); W(1);
                W(0); W(65535); W(0); W(0); // red
                W(1); W(0); W(65535); W(0); // green
            }
            Rect(); Rect(); W(0);
            if (packed) writer.Write((byte)pixels.Length);
            writer.Write(pixels); if ((stream.Length & 1) != 0) writer.Write((byte)0); W(0xff);
            return stream.ToArray();
        }
        byte[] ResourceFixture(byte[] picture) {
            int map = 260 + picture.Length;
            var bytes = new byte[map + 50];
            void W(int at, int n) { bytes[at] = (byte)(n >> 8); bytes[at+1] = (byte)n; }
            void L(int at, int n) { W(at,n >> 16); W(at+2,n); }
            L(0,256); L(4,map); L(8,picture.Length+4); L(12,50);
            L(256,picture.Length); Buffer.BlockCopy(picture,0,bytes,260,picture.Length);
            W(map+24,28); W(map+28,0); L(map+30,(int)Wadfile.Chunk("PICT")); W(map+34,0); W(map+36,10);
            W(map+38,10001); W(map+40,65535); // ID, unnamed resource, attribute/data-offset = zero
            return bytes;
        }
        Test("Optimize selection merges compatible polygons as one undo step",()=>{
            var level=new Level();Box(level,0,0,1024,1024);Box(level,1024,0,2048,1024);Box(level,2048,0,3072,1024);Box(level,4096,0,5120,1024);
            var editor=Editor(level);editor.Selection.Polygons.UnionWith(new short[]{0,1,2});
            Check(editor.OptimizeSelectedGeometry(out string reason),reason);Validate(level);Check(level.Polygons.Count==2,"Failed to simplify three boxes");Check(editor.Selection.Polygons.Count==1,"Result selection lost");
            Check(level.GetEnclosingPolygon(P(4500,500))>=0,"Unselected polygon lost");editor.Undo();Validate(level);Check(level.Polygons.Count==4,"Optimization undo failed");editor.Redo();Validate(level);Check(level.Polygons.Count==2,"Optimization redo failed");
        });
        Test("Optimize preserves height differences and leaves no-op unchanged",()=>{
            var level=new Level();Box(level,0,0,1024,1024);Box(level,1024,0,2048,1024);level.Polygons[1].FloorHeight=256;
            var editor=Editor(level);editor.Selection.Polygons.UnionWith(new short[]{0,1});string before=CanonicalSnapshot(level);
            Check(!editor.OptimizeSelectedGeometry(out _),"Merged different heights");Check(CanonicalSnapshot(level)==before,"No-op changed geometry");
            level.Polygons[1].FloorHeight=0;level.Polygons[1].FloorLight=1;Check(!editor.OptimizeSelectedGeometry(out _),"Merged different lights");
        });
        Test("floor lighting projects raised ground shadows with preview undo and save",()=>{
            var level=new Level();Box(level,0,0,1024,1024);Box(level,1024,0,2048,1024);Box(level,2048,0,4096,1024);
            foreach(var p in level.Polygons)p.CeilingHeight=2048;level.Polygons[1].FloorHeight=512;
            var ed=Editor(level);ed.Selection.Polygons.Add(2);ed.HasBakeLight=true;ed.BakeLightPosition=P(512,512);
            string before=CanonicalSnapshot(level);
            Check(ed.BakeSelectedLighting(1024,1,.25,64,1,false,out string reason),reason);
            Check(CanonicalSnapshot(level)==before,"Preview mutated map");Check(ed.LightingPreview.Any(p=>p.Shadow) && ed.LightingPreview.Any(p=>!p.Shadow),"Missing shadow boundary");
            Check(ed.BakeSelectedLighting(1024,1,.25,64,1,true,out reason),reason);Validate(level);
            Check(level.Polygons.Count==4,"Unexpected shadow geometry");
            short shadow=level.GetEnclosingPolygon(P(2500,512)),bright=level.GetEnclosingPolygon(P(3900,512));
            Check(level.Lights[level.Polygons[shadow].FloorLight].PrimaryActive.Intensity==.25,"Shadow not dark");
            Check(level.Lights[level.Polygons[bright].FloorLight].PrimaryActive.Intensity==1,"Lit floor not bright");
            var loaded=new Level();loaded.Load(level.Save().Clone());Validate(loaded);
            ed.Undo();Check(CanonicalSnapshot(level)==before,"Bake undo failed");ed.Redo();Validate(level);Check(level.Polygons.Count==4,"Redo failed");
        });
        Test("sun paths declare eight points once before polygon actions",()=>{
            var level=new Level();Box(level,0,0,1024,1024);
            string path=PolygonLuaActions.Generate("sun_path");
            level.Polygons[0].LuaTriggerEnabled=true;level.Polygons[0].LuaTriggerCode=path+path+PolygonLuaActions.Generate("sunrise")+PolygonLuaActions.Generate("sunset");
            string generated=PolygonLua.Generate(level);
            Check(generated.Split(new[]{"sun.path = "},StringSplitOptions.None).Length==2,"Path declared repeatedly");
            Check(generated.IndexOf("sun.path = ")<generated.IndexOf("local actions, once"),"Path declared after actions");
            string declaration=PolygonLuaActions.SunPathDeclaration(new[]{path});
            Check(declaration.Split(new[]{"{azimuth="},StringSplitOptions.None).Length==9,"Path does not have exactly eight steps");
            Check(!PolygonLuaActions.Generate("sunrise").Contains("sun.path ="),"Runtime action redeclares path");
            Check(PolygonLuaActions.Generate("sunset").Contains("target.path_position = 0"),"Sunset does not reverse path");
            Check(PolygonLuaActions.SunPathDeclaration(new[]{"player:print('hello')"})=="","Unrelated trigger enabled prebaking");
            bool conflict=false;try{PolygonLuaActions.SunPathDeclaration(new[]{path,PolygonLuaActions.Generate("sun_path",new System.Collections.Generic.Dictionary<string,double>{{"az0",90}})});}catch(ArgumentException){conflict=true;}
            Check(conflict,"Conflicting level paths silently overwritten");
        });
        Test("sun presets validate duration and survive map trigger serialization",()=>{
            var level=new Level();Box(level,0,0,1024,1024);
            level.Polygons[0].LuaTriggerEnabled=true;
            level.Polygons[0].LuaTriggerCode=PolygonLuaActions.Generate("sunrise",new System.Collections.Generic.Dictionary<string,double>{{"seconds",60},{"start",0},{"end",1}});
            var loaded=new Level();loaded.Load(level.Save().Clone());
            Check(loaded.Polygons[0].LuaTriggerCode==level.Polygons[0].LuaTriggerCode,"Sun action lost on load");
            string runtime=PolygonLua.Generate(loaded);
            Check(runtime.Contains("apply_sun(Game._forge_ahead_sun)") && runtime.Contains("sun_fade.elapsed + 1"),"Sun transition runtime missing");
            foreach(double duration in new[]{0,-1,double.NaN,double.PositiveInfinity,36001}) {
                bool rejected=false;try{PolygonLuaActions.Generate("sunset",new System.Collections.Generic.Dictionary<string,double>{{"seconds",duration}});}catch(ArgumentException){rejected=true;}
                Check(rejected,"Invalid sun duration accepted");
            }
        });
        Test("oblique room lighting welds rounded seams instead of sampling slivers",()=>{
            var level=new Level();
            var rings=new[]{
                new[]{P(-5603,-2146),P(-3631,-2146),P(-3631,-148),P(-5603,-148)},
                new[]{P(-14643,-12863),P(-11442,-7759),P(-12581,-6314),P(-17491,-8902)},
                new[]{P(-12581,-6314),P(-5603,-2146),P(-5603,-148),P(-10065,13829)},
                new[]{P(-10065,13829),P(-5603,-148),P(-3631,-148)},
                new[]{P(-12581,-6314),P(-11442,-7759),P(18851,-6991),P(-3631,-2146),P(-5603,-2146)},
                new[]{P(-3631,-148),P(-3631,-2146),P(18851,-6991)},
                new[]{P(-10065,13829),P(-3631,-148),P(18851,-6991),P(18851,12542)}
            };
            var create=typeof(Level).GetMethod("CreatePlainShape",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            foreach(var ring in rings){object[] args={ring.ToList(),null};Check((bool)create.Invoke(level,args),(string)args[1]);}
            foreach(var p in level.Polygons)p.CeilingHeight=3072;level.Polygons[0].FloorHeight=1024;level.Polygons[1].CeilingHeight=920;
            level.Pave(true,(ShapeDescriptor)125,(ShapeDescriptor)123,(ShapeDescriptor)124);
            var ed=Editor(level);ed.BakeWholeMap=true;ed.RebuildLightingGeometry=true;ed.HasBakeLight=true;ed.BakeLightPosition=P(-14200,-8700);
            string before=CanonicalSnapshot(level);
            Check(ed.BakeSelectedLighting(768,1,.25,256,64,true,out string reason),reason);Validate(level);
            Check(!reason.Contains("approximate") && level.Polygons.Count<=10,"Detailed bake fragmented: "+reason);
            Check(level.Polygons[level.GetEnclosingPolygon(P(-4500,-1000))].FloorHeight==1024,"Raised obstacle lost");
            foreach(var line in level.Lines) {
                if(line.ClockwisePolygonOwner>=0 && line.CounterclockwisePolygonOwner>=0)
                    Check((line.Flags & LineFlags.Solid)==0,"Shadow boundary became solid");
                for(int direction=0;direction<2;direction++) {
                    short owner=direction==0?line.ClockwisePolygonOwner:line.CounterclockwisePolygonOwner;
                    short other=direction==0?line.CounterclockwisePolygonOwner:line.ClockwisePolygonOwner;
                    if(owner<0)continue;
                    var polygon=level.Polygons[owner];
                    if(other>=0 && polygon.FloorHeight>=level.Polygons[other].FloorHeight && polygon.CeilingHeight<=level.Polygons[other].CeilingHeight)continue;
                    short side=direction==0?line.ClockwisePolygonSideIndex:line.CounterclockwisePolygonSideIndex;
                    Check(side>=0 && level.Sides[side].Primary.Texture.Equals((ShapeDescriptor)125),"Bake lost an exposed wall texture");
                }
            }
            var loaded=new Level();loaded.Load(level.Save().Clone());Validate(loaded);
            ed.Undo();Check(CanonicalSnapshot(level)==before,"Seam repair did not undo");ed.Redo();Validate(level);
        });
        Test("ceiling bake samples low-ceiling occlusion independently of floor with undo",()=>{
            foreach(bool rebuild in new[]{false,true}) {
                var level=new Level();Box(level,0,0,1024,1024);Box(level,1024,0,2048,1024);Box(level,2048,0,4096,1024);
                foreach(var p in level.Polygons)p.CeilingHeight=2048;level.Polygons[1].CeilingHeight=1536;
                level.Lights.Add(new Light(.6));short original=(short)(level.Lights.Count-1);
                foreach(var p in level.Polygons)p.CeilingLight=original;
                var ed=Editor(level);ed.Selection.Polygons.Add(2);ed.HasBakeLight=true;ed.BakeLightPosition=P(512,512);ed.RebuildLightingGeometry=rebuild;
                string before=CanonicalSnapshot(level);
                Check(ed.BakeSelectedLighting(512,1,.25,64,1,true,out string reason),reason);Validate(level);
                short receiver=level.GetEnclosingPolygon(P(3000,512));var surface=level.Polygons[receiver];
                double intensity=level.Lights[surface.CeilingLight].PrimaryActive.Intensity;
                Check(intensity>.25 && intensity<1,"Ceiling did not receive partial shadow: "+intensity);
                Check(level.Lights[surface.FloorLight].PrimaryActive.Intensity==1,"Ceiling shading changed bright floor");
                Check(level.Polygons[0].CeilingLight==original && level.Polygons[1].CeilingLight==original,"Shaded unselected ceilings");
                Check(level.Polygons.Count==3,"Ceiling sampling added geometry");
                var loaded=new Level();loaded.Load(level.Save().Clone());Validate(loaded);
                Check(loaded.Polygons[receiver].CeilingLight==surface.CeilingLight,"Ceiling light lost on save");
                ed.Undo();Check(CanonicalSnapshot(level)==before,"Ceiling bake undo failed");ed.Redo();Validate(level);
                Check(level.Lights[level.Polygons[receiver].CeilingLight].PrimaryActive.Intensity==intensity,"Ceiling bake redo failed");
            }
        });
        Test("floor lighting keeps clear floors whole and blocks light across solid walls",()=>{
            var level=new Level();Box(level,0,0,1024,1024);Box(level,2048,0,3072,1024);
            foreach(var p in level.Polygons)p.CeilingHeight=2048;
            var ed=Editor(level);ed.Selection.Polygons.UnionWith(new short[]{0,1});ed.HasBakeLight=true;ed.BakeLightPosition=P(512,512);
            Check(ed.BakeSelectedLighting(1024,.8,.2,16,1,true,out string reason),reason);Validate(level);
            Check(level.Polygons.Count==2,"Unnecessary splits");
            Check(Math.Abs(level.Lights[level.Polygons[0].CeilingLight].PrimaryActive.Intensity-.8)<.001,"Clear ceiling shadowed");
            Check(Math.Abs(level.Lights[level.Polygons[1].CeilingLight].PrimaryActive.Intensity-.2)<.001,"Solid wall leaked ceiling light");
            Check(Math.Abs(level.Lights[level.Polygons[0].FloorLight].PrimaryActive.Intensity-.8)<.001,"Source room shadowed");
            Check(Math.Abs(level.Lights[level.Polygons[1].FloorLight].PrimaryActive.Intensity-.2)<.001,"Solid wall leaked light");
        });
        Test("lighting reduces detail when adjacent polygon has eight vertices",()=>{
            var level=new Level();Box(level,0,0,1024,1024);Box(level,1024,0,2048,1024);Box(level,2048,0,4096,1024);
            Box(level,2048,-1024,4096,0);
            for(int x=2048;x<3072;x+=256)Box(level,x,-2048,x+256,-1024);
            Check(level.Polygons[3].VertexCount==8,"Fixture neighbor must have eight vertices");
            foreach(var p in level.Polygons)p.CeilingHeight=2048;level.Polygons[1].FloorHeight=377;
            var ed=Editor(level);ed.Selection.Polygons.Add(2);ed.HasBakeLight=true;ed.BakeLightPosition=P(513,319);
            string before=CanonicalSnapshot(level);
            var exact=new Level();exact.Load(level.Save().Clone());
            Check(!exact.BakeFloorLighting(new short[]{2},P(513,319),1001,1,.25,128,64,out _,out string rejection,true),"Fixture did not exceed neighbor capacity");
            Check(rejection.Contains("vertex"),rejection);
            Check(ed.BakeSelectedLighting(1001,1,.25,128,64,true,out string reason),reason);Validate(level);
            Check(level.Polygons[3].VertexCount==8,"Changed full neighboring polygon");
            ed.Undo();Check(CanonicalSnapshot(level)==before,"Reduced detail undo failed");
        });
        Test("region reconstruction preserves protected shared walls around alternate routes",()=>{
            var level=new Level();Box(level,0,0,1024,1024);Box(level,1024,0,2048,1024);Box(level,0,1024,1024,2048);Box(level,1024,1024,2048,2048);
            var wall=level.Lines.First(l=>l.EndpointIndexes.All(i=>level.Endpoints[i].X==1024) && l.EndpointIndexes.Any(i=>level.Endpoints[i].Y==0));wall.Flags|=LineFlags.Solid;
            var scope=new System.Collections.Generic.HashSet<short>(new short[]{0,1,2,3});
            Check(level.RebuildLightingRegions(ref scope,out _,out string reason),reason);Validate(level);
            Check(level.Lines.Any(l=>(l.Flags & LineFlags.Solid)!=0 && l.EndpointIndexes.All(i=>level.Endpoints[i].X==1024) && l.EndpointIndexes.Any(i=>level.Endpoints[i].Y==0)),"Protected wall removed via alternate route");
        });
        Test("lighting region reconstruction preserves a hole and removes internal vertices",()=>{
            var level=new Level();
            for(int y=0;y<4;y++)for(int x=0;x<4;x++)Box(level,x*1024,y*1024,(x+1)*1024,(y+1)*1024);
            // The central tile is a protected raised island; surrounding floors form a hole.
            level.Polygons[5].FloorHeight=512;
            var scope=new System.Collections.Generic.HashSet<short>(Enumerable.Range(0,16).Where(i=>i!=5).Select(i=>(short)i));
            Check(level.NormalizeLightingEdgeOrder(out string reason),reason);
            Check(level.RebuildLightingRegions(ref scope,out int removed,out reason),reason);Validate(level);
            Check(removed>0,"Did not discard region subdivisions");
            Check(level.Polygons[level.GetEnclosingPolygon(P(1500,1500))].FloorHeight==512,"Island/hole lost");
            Check(!level.Endpoints.Contains(P(3072,3072)),"Unused interior grid vertex survived reconstruction");
            for(int y=128;y<4096;y+=256)for(int x=128;x<4096;x+=256)Check(level.GetEnclosingPolygon(P(x,y))>=0,"Region reconstruction left a gap");
            var saved=new Level();saved.Load(level.Save().Clone());Validate(saved);
        });
        Test("lighting reconstruction retains textured boundary segments and side orientation",()=>{
            var level=new Level();Box(level,0,0,1024,1024);Box(level,1024,0,2048,1024);
            level.Pave(true,(ShapeDescriptor)125,(ShapeDescriptor)123,(ShapeDescriptor)124);
            var expected=level.Lines.Where(l=>l.ClockwisePolygonOwner<0 || l.CounterclockwisePolygonOwner<0).Select((l,i)=>{
                short si=l.ClockwisePolygonSideIndex>=0?l.ClockwisePolygonSideIndex:l.CounterclockwisePolygonSideIndex;
                level.Sides[si].Primary.Texture=(ShapeDescriptor)(125+i);
                return new {A=level.Endpoints[l.EndpointIndexes[0]],B=level.Endpoints[l.EndpointIndexes[1]],Texture=level.Sides[si].Primary.Texture};
            }).ToList();
            var scope=new System.Collections.Generic.HashSet<short>(new short[]{0,1});
            var plans=scope.ToDictionary(i=>i,i=>new System.Collections.Generic.List<LightingPiece>{new LightingPiece{
                Points=level.Polygons[i].EndpointIndexes.Take(level.Polygons[i].VertexCount).Select(v=>level.Endpoints[v]).ToList(),Shadow=false}});
            Check(level.RebuildLightingRegions(ref scope,out _,out string reason,plans,0,0),reason);Validate(level);
            Check(level.Polygons.Count==1,"Textured boundary prevented removal of internal shading edge");
            foreach(var wall in expected) {
                var line=level.Lines.Single(l=>l.EndpointIndexes.Select(i=>level.Endpoints[i]).Contains(wall.A) && l.EndpointIndexes.Select(i=>level.Endpoints[i]).Contains(wall.B));
                short si=line.ClockwisePolygonOwner>=0?line.ClockwisePolygonSideIndex:line.CounterclockwisePolygonSideIndex;
                Check(si>=0 && level.Sides[si].Primary.Texture.Equals(wall.Texture),"Rebuild lost wall texture or assigned it to wrong side");
            }
            var loaded=new Level();loaded.Load(level.Save().Clone());Validate(loaded);
            Check(loaded.Sides.Count==expected.Count,"Wall sides lost on save/load");
        });
        Test("lighting rebuild repartitions concave lit regions beyond merge-only simplification",()=>{
            var level=new Level();
            for(int y=0;y<5;y++)for(int x=0;x<5;x++)if(!(x==0 && y==1) && !(x==1 && y==4))Box(level,x*256,y*256,(x+1)*256,(y+1)*256);
            var scope=new System.Collections.Generic.HashSet<short>(Enumerable.Range(0,level.Polygons.Count).Select(i=>(short)i));
            var plans=scope.ToDictionary(i=>i,i=>new System.Collections.Generic.List<LightingPiece>{new LightingPiece{
                Points=level.Polygons[i].EndpointIndexes.Take(level.Polygons[i].VertexCount).Select(v=>level.Endpoints[v]).ToList(),Shadow=false}});
            Check(level.RebuildLightingRegions(ref scope,out _,out string reason,plans,0,0),reason);Validate(level);
            Check(level.Polygons.Count<=4,"Old lit subdivisions survived boundary reconstruction: "+level.Polygons.Count);
            for(int y=32;y<1280;y+=64)for(int x=32;x<1280;x+=64) {
                bool expected=!(x/256==0 && y/256==1) && !(x/256==1 && y/256==4);
                Check((level.GetEnclosingPolygon(P(x,y))>=0)==expected,"Reconstruction changed concave region coverage");
            }
            var loaded=new Level();loaded.Load(level.Save().Clone());Validate(loaded);
        });
        Test("lighting rebuild merges a convex pinwheel that cannot merge pairwise",()=>{
            var level=new Level();
            Box(level,1024,1024,2048,2048);Box(level,0,0,2048,1024);
            Box(level,2048,0,3072,2048);Box(level,1024,2048,3072,3072);Box(level,0,1024,1024,3072);
            var scope=new System.Collections.Generic.HashSet<short>(Enumerable.Range(0,5).Select(i=>(short)i));
            var plans=scope.ToDictionary(i=>i,i=>new System.Collections.Generic.List<LightingPiece>{new LightingPiece{
                Points=level.Polygons[i].EndpointIndexes.Take(level.Polygons[i].VertexCount).Select(v=>level.Endpoints[v]).ToList(),Shadow=false}});
            Check(level.RebuildLightingRegions(ref scope,out _,out string reason,plans,0,0),reason);Validate(level);
            Check(level.Polygons.Count==1 && level.Polygons[0].VertexCount==4,"Flat pinwheel did not become one rectangle");
            var loaded=new Level();loaded.Load(level.Save().Clone());Validate(loaded);
        });
        Test("lighting plans reconstruct regions with multiple separated and touching holes",()=>{
            foreach(var holes in new[]{new[]{7,14,21,28},new[]{7,9,19,21}}) {
                var level=new Level();
                for(int y=0;y<6;y++)for(int x=0;x<6;x++)Box(level,x*512,y*512,(x+1)*512,(y+1)*512);
                foreach(int id in holes)level.Polygons[id].FloorHeight=512;
                var scope=new System.Collections.Generic.HashSet<short>(Enumerable.Range(0,36).Select(i=>(short)i));
                var plans=scope.ToDictionary(i=>i,i=>new System.Collections.Generic.List<LightingPiece>{new LightingPiece{
                    Points=level.Polygons[i].EndpointIndexes.Take(level.Polygons[i].VertexCount).Select(v=>level.Endpoints[v]).ToList(),Shadow=false}});
                Check(level.RebuildLightingRegions(ref scope,out int removed,out string reason,plans,0,0),reason);Validate(level);
                Check(removed>0 && level.Polygons.Count<36,"Original grid subdivisions retained");
                for(int y=64;y<3072;y+=128)for(int x=64;x<3072;x+=128) {
                    short id=level.GetEnclosingPolygon(P(x,y));Check(id>=0,"Reconstruction left a gap");
                    Check(level.Polygons[id].FloorHeight==(holes.Contains((y/512)*6+x/512)?512:0),"Raised island boundary changed");
                }
                var loaded=new Level();loaded.Load(level.Save().Clone());Validate(loaded);
            }
        });
        Test("lighting rebuild handles concave region with more than eight boundary vertices",()=>{
            var level=new Level();
            for(int x=0;x<5;x++)Box(level,x*1024,0,(x+1)*1024,1024);
            for(int x=0;x<5;x+=2)Box(level,x*1024,1024,(x+1)*1024,2048);
            foreach(var p in level.Polygons)p.CeilingHeight=2048;
            var scope=new System.Collections.Generic.HashSet<short>(Enumerable.Range(0,level.Polygons.Count).Select(i=>(short)i));
            Check(level.RebuildLightingRegions(ref scope,out int removed,out string reason),reason);Validate(level);
            Check(removed>0 && level.GetEnclosingPolygon(P(1500,1500))<0,"Concave notch filled");
            Check(level.GetEnclosingPolygon(P(2500,1500))>=0,"Region lobe lost");
        });
        Test("rebuild discards previous floor-light boundaries before new shading",()=>{
            var level=new Level();Box(level,0,0,1024,1024);Box(level,1024,0,2048,1024);Box(level,2048,0,3072,1024);
            foreach(var p in level.Polygons)p.CeilingHeight=2048;
            level.Lights.Add(new Light(.25));level.Polygons[1].FloorLight=(short)(level.Lights.Count-1);
            var ed=Editor(level);ed.BakeWholeMap=true;ed.RebuildLightingGeometry=true;ed.HasBakeLight=true;ed.BakeLightPosition=P(512,512);
            string before=CanonicalSnapshot(level);
            Check(ed.BakeSelectedLighting(768,1,.25,64,1,true,out string reason),reason);
            Check(level.Polygons.Count==1,"Old shading preserved unnecessary floor divisions: "+reason);Validate(level);
            Check(level.Lights[level.Polygons[0].FloorLight].PrimaryActive.Intensity==1,"Old shadow remained");
            ed.Undo();Check(CanonicalSnapshot(level)==before,"Discarded floor divisions did not undo");
        });
        Test("floor-light rebuild compatibility preserves ceiling and texture boundaries",()=>{
            var level=new Level();Box(level,0,0,1024,1024);Box(level,1024,0,2048,1024);
            level.Lights.Add(new Light(.25));level.Polygons[1].FloorLight=(short)(level.Lights.Count-1);
            Check(!level.CanOptimizePair(0,1) && level.CanOptimizePair(0,1,true),"Lighting-specific compatibility leaked into optimize");
            level.Polygons[1].CeilingLight=(short)(level.Lights.Count-1);Check(level.CanOptimizePair(0,1,true),"Old ceiling lighting blocked lighting reconstruction");
            level.Polygons[1].CeilingHeight+=256;Check(!level.CanOptimizePair(0,1,true),"Ceiling boundary ignored");
            level.Polygons[1].CeilingHeight=level.Polygons[0].CeilingHeight;
            level.Polygons[1].FloorTexture=new ShapeDescriptor((ushort)123);Check(!level.CanOptimizePair(0,1,true),"Texture boundary ignored");
        });
        Test("moving light replaces old bake and recalculates lit regions",()=>{
            var level=new Level();Box(level,0,0,1024,1024);Box(level,1024,0,2048,1024);Box(level,2048,0,4096,1024);
            foreach(var p in level.Polygons)p.CeilingHeight=2048;level.Polygons[1].FloorHeight=512;
            var original=level.Save().Clone();
            var ed=Editor(level);ed.BakeWholeMap=true;ed.RebuildLightingGeometry=true;ed.HasBakeLight=true;ed.BakeLightPosition=P(512,512);
            Check(ed.BakeSelectedLighting(1024,1,.25,128,1,true,out string reason),reason);string first=CanonicalSnapshot(level);
            ed.BakeLightPosition=P(3900,512);
            Check(ed.BakeSelectedLighting(1024,1,.25,128,1,true,out reason),reason);Validate(level);
            Check(reason.Contains("Replaced the previous bake"),"Did not reuse original geometry");
            var fresh=new Level();fresh.Load(original);var other=Editor(fresh);other.BakeWholeMap=true;other.RebuildLightingGeometry=true;other.HasBakeLight=true;other.BakeLightPosition=P(3900,512);
            Check(other.BakeSelectedLighting(1024,1,.25,128,1,true,out reason),reason);
            Check(CanonicalSnapshot(fresh)==CanonicalSnapshot(level),"Rebake differs from a fresh bake at the moved position");
            ed.Undo();Check(CanonicalSnapshot(level)==first,"Rebake undo did not restore previous bake");
        });
        Test("rebake preserves edits made after earlier lighting",()=>{
            var level=new Level();Box(level,0,0,1024,1024);level.Polygons[0].CeilingHeight=2048;
            var ed=Editor(level);ed.BakeWholeMap=true;ed.HasBakeLight=true;ed.BakeLightPosition=P(512,512);
            Check(ed.BakeSelectedLighting(768,1,.25,64,1,true,out string reason),reason);
            level.Polygons[0].CeilingHeight=3072;
            Check(ed.BakeSelectedLighting(768,.8,.2,64,1,true,out reason),reason);
            Check(level.Polygons[0].CeilingHeight==3072 && !reason.Contains("Replaced the previous bake"),"Rebake discarded intervening edits");
        });
        Test("rebuild does not silently use coarse sampling when exact bake fails",()=>{
            var level=new Level();Box(level,0,0,1024,1024);Box(level,1024,0,2048,1024);Box(level,2048,0,4096,1024);
            foreach(var p in level.Polygons)p.CeilingHeight=2048;level.Polygons[1].FloorHeight=512;
            var ed=Editor(level);ed.Selection.Polygons.Add(2);ed.RebuildLightingGeometry=true;ed.HasBakeLight=true;ed.BakeLightPosition=P(512,512);
            string before=CanonicalSnapshot(level);
            Check(!ed.BakeSelectedLighting(1024,1,.25,1,1,true,out string reason),"Rebuild silently used coarse fallback");
            Check(reason.Contains("coarse sampling") && CanonicalSnapshot(level)==before,"Missing explanation or failed rebuild changed map");
            ed.AllowCoarseRebuild=true;Check(ed.BakeSelectedLighting(1024,1,.25,1,1,true,out reason),reason);
        });
        Test("lighting normalizes outgoing imported edge order before splitting",()=>{
            var level=new Level();Box(level,0,0,1024,1024);Box(level,1024,0,2048,1024);Box(level,2048,0,4096,1024);
            foreach(var p in level.Polygons){p.CeilingHeight=2048;var lines=(short[])p.LineIndexes.Clone();var sides=(short[])p.SideIndexes.Clone();var adjacent=(short[])p.AdjacentPolygonIndexes.Clone();
                for(int i=0;i<p.VertexCount;i++){int next=(i+1)%p.VertexCount;p.LineIndexes[i]=lines[next];p.SideIndexes[i]=sides[next];p.AdjacentPolygonIndexes[i]=adjacent[next];}}
            level.Polygons[1].FloorHeight=512;
            var ed=Editor(level);ed.BakeWholeMap=true;ed.HasBakeLight=true;ed.BakeLightPosition=P(512,512);
            string before=CanonicalSnapshot(level);
            Check(ed.BakeSelectedLighting(1024,1,.25,64,1,true,out string reason),reason);
            Validate(level);Check(level.Polygons.Count>3,"Outgoing edge order forced no-geometry fallback");
            var loaded=new Level();loaded.Load(level.Save().Clone());Validate(loaded);
            ed.Undo();Check(CanonicalSnapshot(level)==before,"Normalization/bake undo lost original");
        });
        Test("lighting rebuild removes original divisions and undoes atomically",()=>{
            var level=new Level();Box(level,0,0,1024,1024);Box(level,1024,0,2048,1024);Box(level,2048,0,3072,1024);
            foreach(var p in level.Polygons)p.CeilingHeight=2048;
            var ed=Editor(level);ed.BakeWholeMap=true;ed.RebuildLightingGeometry=true;ed.HasBakeLight=true;ed.BakeLightPosition=P(512,512);
            string before=CanonicalSnapshot(level);
            Check(ed.BakeSelectedLighting(768,1,.25,64,1,true,out string reason),reason);Validate(level);
            Check(level.Polygons.Count==1,"Original internal boundaries retained: "+reason);
            Check(ed.SelectedLuaPolygons().SequenceEqual(new short[]{0}),"Rebuild selection indexes wrong");
            ed.Undo();Check(CanonicalSnapshot(level)==before,"Rebuild bake undo failed");ed.Redo();Validate(level);Check(level.Polygons.Count==1,"Rebuild redo failed");
        });
        Test("lighting rebuild preserves height boundaries and skips script ID renumbering",()=>{
            var level=new Level();Box(level,0,0,1024,1024);Box(level,1024,0,2048,1024);
            foreach(var p in level.Polygons)p.CeilingHeight=2048;level.Polygons[1].FloorHeight=256;
            var ed=Editor(level);ed.BakeWholeMap=true;ed.RebuildLightingGeometry=true;ed.HasBakeLight=true;ed.BakeLightPosition=P(512,512);
            Check(ed.BakeSelectedLighting(768,1,.25,64,1,true,out string reason),reason);Validate(level);
            Check(level.Polygons.Any(p=>p.FloorHeight==0) && level.Polygons.Any(p=>p.FloorHeight==256),"Height boundary lost");ed.Undo();
            level.Polygons[1].FloorHeight=0;level.Polygons[1].LuaTriggerCode="player:print('test')";
            Check(ed.BakeSelectedLighting(768,1,.25,64,1,true,out reason),reason);
            Check(reason.Contains("rebuilding skipped") && level.Polygons.Count==2,"Script IDs not protected");
            Check(level.Polygons[1].LuaTriggerCode.Contains("test"),"Script moved or lost");
        });
        Test("approximate lighting merges uniform sample triangles into convex floors",()=>{
            var level=new Level();Box(level,0,0,1024,1024);Box(level,2048,0,3072,1024);
            foreach(var p in level.Polygons)p.CeilingHeight=2048;
            Check(level.BakeFloorLighting(new short[]{0,1},P(512,512),768,1,.25,128,1,out var pieces,out string reason,true),reason);
            Validate(level);
            Check(pieces.Count==2 && level.Polygons.Count==2,"Uniform floors kept sampling subdivisions");
            Check(pieces.All(p=>p.Points.Count==4),"Uniform boxes became triangles");
            Check(pieces.Count(p=>p.Shadow)==1 && pieces.Count(p=>!p.Shadow)==1,"Simplification changed shading");
        });
        Test("whole map lighting works without a selection",()=>{
            var level=new Level();Box(level,0,0,1024,1024);Box(level,1024,0,2048,1024);
            foreach(var p in level.Polygons)p.CeilingHeight=2048;
            var ed=Editor(level);ed.BakeWholeMap=true;ed.HasBakeLight=true;ed.BakeLightPosition=P(512,512);
            Check(ed.SelectedLuaPolygons().Length==0,"Unexpected selection");
            Check(ed.BakeSelectedLighting(768,.8,.2,64,1,false,out string reason),reason);
            Check(ed.LightingPreview.Count(p=>!p.Shadow)==2,"Preview did not cover whole map");
            Check(ed.BakeSelectedLighting(768,.8,.2,64,1,true,out reason),reason);Validate(level);
            foreach(var p in level.Polygons)Check(Math.Abs(level.Lights[p.FloorLight].PrimaryActive.Intensity-.8)<.001,"Unselected floor not lit");
        });
        Test("lighting retries small budgets and reports simplified result",()=>{
            var level=new Level();Box(level,0,0,1024,1024);Box(level,1024,0,2048,1024);Box(level,2048,0,4096,1024);
            foreach(var p in level.Polygons)p.CeilingHeight=2048;level.Polygons[1].FloorHeight=512;
            var ed=Editor(level);ed.Selection.Polygons.Add(2);ed.HasBakeLight=true;ed.BakeLightPosition=P(512,512);
            string before=CanonicalSnapshot(level);
            Check(ed.BakeSelectedLighting(1024,1,.25,1,1,true,out string reason),reason);
            Check(reason.Contains("Simplified") && reason.Contains("Fallback reason:") && reason.Contains("0 added"),"Missing fallback explanation: "+reason);
            Check(!reason.Contains("cannot be represented") && !reason.Contains("could not be built:"),"Successful fallback presented an exact-attempt failure");
            Check(level.Polygons.Count==3,"Budget exceeded");Validate(level);ed.Undo();Check(CanonicalSnapshot(level)==before,"Fallback undo failed");
            ed.Selection.Polygons.UnionWith(new short[]{0,1,2});
            Check(!ed.BakeSelectedLighting(1024,1,.25,1,1,true,out reason),"Impossible budget accepted");
            Check(reason.Contains("Increase it to at least 3"),"Missing actionable budget error");
        });
        Test("baked lighting shades far walls behind raised ground",()=>{
            var level=new Level();Box(level,0,0,1024,1024);Box(level,1024,0,2048,1024);Box(level,2048,0,3072,1024);
            foreach(var p in level.Polygons)p.CeilingHeight=2048;level.Polygons[1].FloorHeight=1536;
            level.Pave();
            var far=level.Sides.First(s=>s.PolygonIndex==2 && level.Lines[s.LineIndex].EndpointIndexes.All(i=>level.Endpoints[i].X==3072));
            var texture=far.Primary.Texture;short originalLight=far.PrimaryLightsourceIndex;
            var ed=Editor(level);ed.Selection.Polygons.Add(2);ed.HasBakeLight=true;ed.BakeLightPosition=P(512,512);
            string before=CanonicalSnapshot(level);
            Check(ed.BakeSelectedLighting(768,1,.25,64,1,true,out string reason),reason);Validate(level);
            far=level.Sides.First(s=>s.PolygonIndex==2 && level.Lines[s.LineIndex].EndpointIndexes.All(i=>level.Endpoints[i].X==3072));
            Check(level.Lights[far.PrimaryLightsourceIndex].PrimaryActive.Intensity==.25,"Far wall not shadowed");
            Check(far.Primary.Texture.Equals(texture),"Bake replaced wall texture");
            ed.Undo();Check(CanonicalSnapshot(level)==before,"Wall lighting undo failed");
            level.Polygons[1].FloorHeight=0;
            Check(ed.BakeSelectedLighting(768,1,.25,64,1,true,out reason),reason);
            far=level.Sides.First(s=>s.PolygonIndex==2 && level.Lines[s.LineIndex].EndpointIndexes.All(i=>level.Endpoints[i].X==3072));
            Check(level.Lights[far.PrimaryLightsourceIndex].PrimaryActive.Intensity==1,"Unblocked far wall not lit");
        });
        Test("wall bake detects shadows below a lit wall center",()=>{
            var level=new Level();Box(level,0,0,1024,1024);Box(level,1024,0,2048,1024);Box(level,2048,0,3072,1024);
            foreach(var p in level.Polygons)p.CeilingHeight=4096;level.Polygons[1].FloorHeight=768;level.Pave();
            var ed=Editor(level);ed.Selection.Polygons.Add(2);ed.HasBakeLight=true;ed.BakeLightPosition=P(512,512);
            Check(ed.BakeSelectedLighting(1024,1,.25,64,1,true,out string reason),reason);Validate(level);
            var far=level.Sides.First(s=>s.PolygonIndex==2 && level.Lines[s.LineIndex].EndpointIndexes.All(i=>level.Endpoints[i].X==3072));
            double intensity=level.Lights[far.PrimaryLightsourceIndex].PrimaryActive.Intensity;
            Check(intensity<1 && intensity>.25,"Partial far-wall shadow was lost or flattened to full darkness");
            var loaded=new Level();loaded.Load(level.Save().Clone());
            var saved=loaded.Sides.First(s=>s.PolygonIndex==2 && loaded.Lines[s.LineIndex].EndpointIndexes.All(i=>loaded.Endpoints[i].X==3072));
            Check(Math.Abs(loaded.Lights[saved.PrimaryLightsourceIndex].PrimaryActive.Intensity-intensity)<.001,"Wall shade lost on save/load");
        });
        Test("floor lighting accepts selected floors at and above the light",()=>{
            foreach(short raised in new short[]{768,1200}) {
                var level=new Level();Box(level,0,0,1024,1024);Box(level,1024,0,2048,1024);Box(level,2048,0,3072,1024);
                foreach(var p in level.Polygons)p.CeilingHeight=2048;level.Polygons[1].FloorHeight=raised;
                var ed=Editor(level);ed.Selection.Polygons.UnionWith(new short[]{0,1,2});ed.HasBakeLight=true;ed.BakeLightPosition=P(512,512);
                string before=CanonicalSnapshot(level);
                Check(ed.BakeSelectedLighting(768,1,.25,64,1,false,out string reason),reason);
                Check(ed.LightingPreview.Any(p=>p.Shadow),"Raised floor not shadowed in preview");
                Check(CanonicalSnapshot(level)==before,"Preview changed map");
                Check(ed.BakeSelectedLighting(768,1,.25,64,1,true,out reason),reason);Validate(level);
                Check(level.Lights[level.Polygons[0].FloorLight].PrimaryActive.Intensity==1,"Lower source floor not lit");
                Check(level.Lights[level.Polygons[1].FloorLight].PrimaryActive.Intensity==.25,"Raised top lit from below");
                Check(level.Lights[level.Polygons[2].FloorLight].PrimaryActive.Intensity==.25,"Raised floor did not block light");
                ed.Undo();Check(CanonicalSnapshot(level)==before,"Height case undo failed");
            }
        });
        Test("live shadow preview ignores polygon budget and shared-edge limits",()=>{
            var level=new Level();Box(level,0,0,1024,1024);Box(level,1024,0,2048,1024);Box(level,2048,0,4096,1024);Box(level,2048,-1024,4096,0);
            for(int x=2048;x<3072;x+=256)Box(level,x,-2048,x+256,-1024);
            foreach(var p in level.Polygons)p.CeilingHeight=2048;level.Polygons[1].FloorHeight=377;
            var ed=Editor(level);ed.Selection.Polygons.Add(2);ed.Tool=Tool.BakeLighting;ed.BakePreviewHeight=1001;
            string before=CanonicalSnapshot(level);
            ed.ButtonPress(513,319,EditorModifiers.None);
            Check(ed.LightingPreview.Any(p=>p.Shadow),"Live preview omitted shadows");
            string first=string.Join(";",ed.LightingPreview.SelectMany(p=>p.Points).Select(p=>$"{p.X},{p.Y}"));
            ed.Motion(700,600,EditorModifiers.None);
            Check(first!=string.Join(";",ed.LightingPreview.SelectMany(p=>p.Points).Select(p=>$"{p.X},{p.Y}")),"Drag did not update shadow masks");
            ed.ButtonRelease(700,600,EditorModifiers.None);
            Check(ed.BakeSelectedLighting(1001,1,.25,1,65536,false,out string reason),reason);
            Check(ed.LightingPreview.Any(p=>p.Shadow),"Bake limits affected display preview");
            Check(CanonicalSnapshot(level)==before,"Live preview mutated map");
        });
        Test("lighting tool placement keeps selected geometry unchanged",()=>{
            var level=new Level();Box(level,0,0,1024,1024);var ed=Editor(level);ed.Selection.Polygons.Add(0);
            string before=CanonicalSnapshot(level);ed.Tool=Tool.BakeLighting;
            ed.ButtonPress(256,256,EditorModifiers.None);ed.Motion(512,512,EditorModifiers.None);ed.ButtonRelease(512,512,EditorModifiers.None);
            Check(ed.HasBakeLight && ed.BakeLightPosition.Equals(P(512,512)),"Light placement failed");
            Check(ed.SelectedLuaPolygons().SequenceEqual(new short[]{0}),"Placement lost selection");
            Check(CanonicalSnapshot(level)==before,"Placement moved geometry");
            ed.Tool=Tool.Select;Check(ed.HasBakeLight,"Tool change lost light");
        });
        Test("approximate floor shadows accept fractional projected intersections",()=>{
            var level=new Level();Box(level,0,0,1024,1024);Box(level,1024,0,2048,1024);Box(level,2048,0,4096,1024);
            foreach(var p in level.Polygons)p.CeilingHeight=2048;level.Polygons[1].FloorHeight=377;
            var ed=Editor(level);ed.Selection.Polygons.Add(2);ed.HasBakeLight=true;ed.BakeLightPosition=P(513,319);
            string before=CanonicalSnapshot(level);
            Check(ed.BakeSelectedLighting(1001,1,.25,128,64,false,out string reason),reason);
            Check(CanonicalSnapshot(level)==before,"Approximate preview mutated map");
            Check(ed.BakeSelectedLighting(1001,1,.25,128,64,true,out reason),reason);Validate(level);
            for(int x=2050;x<4096;x+=37)for(int y=2;y<1024;y+=37)Check(level.GetEnclosingPolygon(P(x,y))>=0,"Approximation left floor holes");
            ed.Undo();Check(CanonicalSnapshot(level)==before,"Approximate bake undo failed");
            var trial=new Level();trial.Load(level.Save().Clone());
            Check(trial.BakeFloorLighting(new short[]{2},P(513,319),1001,1,.25,128,64,out var preview,out reason,true),reason);Validate(trial);
            Check(preview.Any(p=>p.Shadow) && preview.Any(p=>!p.Shadow),"Approximation lost shadow contrast");
        });
        Test("floor lighting rejects invalid source and budget atomically",()=>{
            var level=new Level();Box(level,0,0,1024,1024);Box(level,1024,0,2048,1024);Box(level,2048,0,4096,1024);
            foreach(var p in level.Polygons)p.CeilingHeight=2048;level.Polygons[1].FloorHeight=512;
            var ed=Editor(level);ed.Selection.Polygons.Add(2);ed.HasBakeLight=true;ed.BakeLightPosition=P(512,512);string before=CanonicalSnapshot(level);
            Check(!ed.BakeSelectedLighting(1024,1,.25,0,1,true,out _),"Invalid budget ignored");
            Check(!ed.BakeSelectedLighting(0,1,.25,64,1,true,out _),"Source on floor accepted");
            Check(CanonicalSnapshot(level)==before,"Failed bake mutated map");
        });
        Test("Pathfinder diagonal overlap in both directions",()=>{
            foreach(bool subtract in new[]{false,true})foreach(bool reverse in new[]{false,true}) {
                var level=new Level();Box(level,0,0,2048,2048);
                Check(level.CreateCirclePolygon(new System.Collections.Generic.List<Point>{P(4096,100),P(5200,1200),P(4100,2300),P(3000,1100)},out string error),error);
                foreach(short v in level.Polygons[1].EndpointIndexes.Take(4).ToArray()){var p=level.Endpoints[v];level.Endpoints[v]=P(p.X-2500,p.Y);}
                var ed=Editor(level);ed.Selection.Polygons.UnionWith(new short[]{0,1});Check(ed.PathfinderSelection(subtract,reverse,out error),$"subtract={subtract} reverse={reverse}: {error}");Validate(level);
            }
        });
        Test("Saved selection round trip and resource import",()=>{
            var level=new Level();Box(level,0,0,1024,1024);Box(level,2048,0,3072,1024);
            level.Lights[0].PrimaryActive.Intensity=0.123;
            var editor=Editor(level);editor.Selection.Polygon=0;
            string path=System.IO.Path.Combine(System.IO.Path.GetTempPath(),Guid.NewGuid()+".sceA");
            try {
                Check(editor.SaveSelectionFile(path,out string reason),reason);
                var saved=new Wadfile();saved.Load(path);var contents=new Level();contents.Load(saved.Directory[0]);Check(contents.Polygons.Count==1,"Saved unselected geometry");
                var destination=new Level();var other=Editor(destination);Check(other.LoadSelectionFile(path,out reason),reason);Check(other.Pasting,"Missing paste preview");
                Check(other.PasteSelection(4096,4096,out reason),reason);Validate(destination);Check(destination.Polygons.Count==1,"Wrong paste count");Check(Math.Abs(destination.Lights[destination.Polygons[0].FloorLight].PrimaryActive.Intensity-0.123)<0.0001,"Light settings not imported");other.Undo();Check(destination.Polygons.Count==0,"Import not undoable");
            }finally{if(System.IO.File.Exists(path))System.IO.File.Delete(path);}
        });
        Test("Pathfinder joins adjacent polygons and undoes",()=>{
            var level=new Level();Box(level,0,0,1024,1024);Box(level,1024,0,2048,1024);
            var editor=Editor(level);editor.Selection.Polygons.UnionWith(new short[]{0,1});
            Check(editor.PathfinderSelection(false,false,out string reason),reason);Validate(level);Check(level.Polygons.Count==1,"Did not merge");editor.Undo();Validate(level);Check(level.Polygons.Count==2,"Union undo failed");
        });
        Test("Pathfinder overlap union and enclosed difference",()=>{
            foreach(bool subtract in new[]{false,true}) {
                var level=new Level();Box(level,0,0,2048,2048);Box(level,3072,512,4096,1536);
                var cutter=level.Polygons[1];foreach(short v in cutter.EndpointIndexes.Take(cutter.VertexCount).ToArray()){var point=level.Endpoints[v];level.Endpoints[v]=P(point.X-2560,point.Y);}
                var editor=Editor(level);editor.Selection.Polygons.UnionWith(new short[]{0,1});
                Check(editor.PathfinderSelection(subtract,false,out string reason),reason);Validate(level);
                Check((level.GetEnclosingPolygon(P(1024,1024))<0)==subtract,"Wrong boolean coverage");Check(level.GetEnclosingPolygon(P(256,256))>=0,"Outer coverage lost");
                var loaded=new Level();loaded.Load(level.Save().Clone());Validate(loaded);
            }
        });
        Test("Pathfinder failed operation is atomic",()=>{
            var level=new Level();Box(level,0,0,1024,1024);Box(level,2048,0,3072,1024);var editor=Editor(level);editor.Selection.Polygons.UnionWith(new short[]{0,1});
            string before=CanonicalSnapshot(level);Check(!editor.PathfinderSelection(true,false,out string reason),"Non-overlap subtraction accepted");Check(CanonicalSnapshot(level)==before,"Failed boolean changed map");
        });
        Test("Pathfinder subtracts overlapping cutter",()=>{
            var level=new Level();Box(level,0,0,1024,1024);Box(level,2048,256,3072,768);
            var cutter=level.Polygons[1];foreach(short v in cutter.EndpointIndexes.Take(cutter.VertexCount).ToArray()){var point=level.Endpoints[v];level.Endpoints[v]=P(point.X-1536,point.Y);}
            var editor=Editor(level);editor.Selection.Polygons.UnionWith(new short[]{0,1});
            Check(editor.PathfinderSelection(true,false,out string reason),reason);Validate(level);Check(level.GetEnclosingPolygon(P(768,512))==-1,"Cutout remains filled");Check(level.GetEnclosingPolygon(P(256,512))>=0,"Target lost");
        });
        Test("line tool cuts edge to edge and vertex to vertex with undo", () => {
            foreach(bool diagonal in new[]{false,true}) {
                var level=new Level();Box(level,0,0,1000,1000);var editor=Editor(level);editor.Tool=Tool.Line;editor.LineSplitAll=true;
                string before=CanonicalSnapshot(level); editor.StartLine(0,diagonal?(short)0:(short)500);editor.ConnectLine(1000,diagonal?(short)1000:(short)500,false,false);editor.CommitHistory(true);
                Check(level.Polygons.Count==2,"Line did not cut polygon: "+editor.LineToolError);Validate(level);
                Check(level.Endpoints.Count==(diagonal?4:6),"Duplicate or missing edge vertices");
                editor.Undo();Check(CanonicalSnapshot(level)==before,"Line cut undo did not restore original map");editor.Redo();Check(level.Polygons.Count==2,"Line cut redo failed");
            }
        });
        Test("line cut crosses adjacent polygons and preserves shared topology", () => {
            var level=new Level();Box(level,0,0,1000,1000);Box(level,1000,0,2000,1000);
            Check(level.CutPolygonsWithLine(P(0,500),P(2000,500),out bool handled,out string reason) && handled,reason);
            Check(level.Polygons.Count==4,"Adjacent polygons not both cut");Validate(level);
            Check(level.Endpoints.Count==9,"Shared intersection vertex duplicated");
        });
        Test("line cut preserves settings wall textures objects and save load", () => {
            var level=new Level();Box(level,0,0,1000,1000);level.Pave();level.Polygons[0].FloorHeight=123;
            foreach(var side in level.Sides)side.Primary.Texture=(ShapeDescriptor)123;
            level.Objects.Add(new MapObject{X=250,Y=250,PolygonIndex=0});level.Objects.Add(new MapObject{X=750,Y=750,PolygonIndex=0});
            Check(level.CutPolygonsWithLine(P(0,500),P(1000,500),out bool handled,out string reason) && handled,reason);
            Check(level.Polygons.All(p=>p.FloorHeight==123),"Cut lost polygon settings");
            Check(level.Sides.All(s=>s.Primary.Texture.Equals((ShapeDescriptor)123)),"Cut lost perimeter textures");
            Check(level.Objects[0].PolygonIndex!=level.Objects[1].PolygonIndex,"Cut did not reassign objects");
            var loaded=new Level();loaded.Load(level.Save().Clone());Validate(loaded);Check(loaded.Polygons.Count==2,"Cut lost on save/load");
        });
        Test("failed and cancelled polygon line cuts restore map without a history step", () => {
            var level=new Level();Box(level,0,0,1000,1000);var editor=Editor(level);editor.Tool=Tool.Line;editor.LineSplitAll=true;string before=CanonicalSnapshot(level);
            editor.StartLine(0,500);editor.ConnectLine(500,500,false,false);
            Check(CanonicalSnapshot(level)==before && !editor.CanUndo && editor.LineToolError!=null,"Incomplete cut changed map or history");
            editor.StartLine(0,500);editor.ConnectLine(0,500,false,false);Check(CanonicalSnapshot(level)==before,"Cancelled line left edge vertices");
        });
        Test("line tool attaches outside polygons and reuses existing boundary segments", () => {
            var level=new Level();Box(level,0,0,1000,1000);var editor=Editor(level);editor.Tool=Tool.Line;editor.LineSplitAll=true;
            editor.StartLine(0,500);editor.ConnectLine(-500,500,false,false);editor.CommitHistory(true);
            Check(level.Polygons.Count==1 && level.Polygons[0].VertexCount==5 && level.Endpoints.Count==6,"Outside attachment failed");Validate(level);
            editor.StartLine(1000,250);editor.ConnectLine(1000,750,false,false);editor.CommitHistory(true);Validate(level);
            Check(level.Polygons.Count==1,"Boundary segment unnecessarily split polygon");
            int lines=level.Lines.Count,points=level.Endpoints.Count;editor.StartLine(1000,250);editor.ConnectLine(1000,750,false,false);
            Check(level.Lines.Count==lines && level.Endpoints.Count==points,"Redrawing boundary duplicated geometry");
        });
        Test("line tool cuts an eight-sided polygon and cancels on tool change", () => {
            var level=new Level();Check(level.CreateCirclePolygon(new System.Collections.Generic.List<Point>{P(0,300),P(300,0),P(700,0),P(1000,300),P(1000,700),P(700,1000),P(300,1000),P(0,700)},out string reason),reason);
            var editor=Editor(level);editor.Tool=Tool.Line;editor.LineSplitAll=true;string before=CanonicalSnapshot(level);
            editor.StartLine(0,500);editor.Tool=Tool.Select;
            Check(CanonicalSnapshot(level)==before && !editor.CanUndo,"Tool change left unfinished line geometry");
            editor.Tool=Tool.Line;editor.LineSplitAll=true;editor.StartLine(0,500);editor.ConnectLine(1000,500,false,false);
            Check(level.Polygons.Count==2,editor.LineToolError);Validate(level);
        });
        Test("terminal pictures decode indexed PICT PackBits and direct RGB rows", () => {
            var indexed = new TerminalPicture { Data = PictureFixture(8,true,new byte[] { 0xf9, 1 }) }.Decode();
            Check(indexed.Width == 8 && indexed.Height == 1 && indexed.Data.SequenceEqual(Enumerable.Range(0,24).Select(i=>(byte)(i%3==1?255:0))),"Indexed PackBits picture wrong");
            var literal = new TerminalPicture { Data = PictureFixture(8,true,new byte[] { 7,0,1,0,1,0,1,0,1 }) }.Decode();
            Check(literal.Data[0]==255 && literal.Data[4]==255,"Literal PackBits picture wrong");
            foreach (bool packed in new[]{false,true}) {
                var pixels = packed ? new byte[]{0xf9,0x7c,0} : Enumerable.Range(0,16).Select(i=>(byte)(i%2==0?0x7c:0)).ToArray();
                var direct = new TerminalPicture { Data = PictureFixture(16,packed,pixels) }.Decode();
                Check(direct.Data[0]==255 && direct.Data[1]==0 && direct.Data[2]==0 && direct.Data[21]==255,"16-bit picture wrong");
                pixels = packed ? new byte[]{0xe9,0x80} : Enumerable.Range(0,32).Select(i=>(byte)(i%4==0?0:128)).ToArray();
                direct = new TerminalPicture { Data = PictureFixture(32,packed,pixels) }.Decode();
                Check(direct.Data.All(v=>v==128),"32-bit planar/chunky picture wrong");
            }
        });
        Test("terminal pictures resolve map WAD and Windows bitmap color tables", () => {
            var file = new Wadfile(); var entry = new Wadfile.DirectoryEntry(); file.Directory[10001]=entry;
            byte[] pict = PictureFixture(8,false,new byte[]{0,1,0,1,0,1,0,1});
            entry.Chunks[Wadfile.Chunk("PICT")]=pict;
            Check(TerminalPicture.FromWad(file,10001).Decode().Width==8 && TerminalPicture.FromWad(file,2)==null,"Wrong resource ID lookup");
            entry.Chunks.Clear(); var raw = new byte[]{0,0,0,0,0,1,0,2,0,8,0,1}; var clut = new byte[1542]; clut[6]=255; clut[14]=255;
            entry.Chunks[Wadfile.Chunk("pict")]=raw; entry.Chunks[Wadfile.Chunk("clut")]=clut;
            var image=TerminalPicture.FromWad(file,10001).Decode();
            Check(image.Width==2 && image.Data.SequenceEqual(new byte[]{255,0,0,0,255,0}),"Windows palette wrong");
            entry.Chunks[Wadfile.Chunk("pict")]=new byte[]{0,0,0,0,0,1,0,1,0,16,0x7c,0};
            Check(TerminalPicture.FromWad(file,10001).Decode().Data[0]==255,"Windows RGB555 wrong");
            string path=System.IO.Path.GetTempFileName();
            try { file.Save(path); Check(TerminalPicture.FromFile(path,10001).Decode().Width==1,"WAD file lookup failed"); }
            finally { System.IO.File.Delete(path); }
        });
        Test("terminal pictures read resource forks MacBinary and AppleDouble sidecars", () => {
            byte[] pict=PictureFixture(8,false,new byte[]{0,1,0,1,0,1,0,1}), fork=ResourceFixture(pict);
            Check(TerminalPicture.FromResourceFork(fork,10001).Data.SequenceEqual(pict),"Mac resource lookup wrong");
            Check(TerminalPicture.FromResourceFork(fork,999)==null,"Missing resource resolved to another ID");
            string path=System.IO.Path.GetTempFileName(); string sidecar=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path),"._"+System.IO.Path.GetFileName(path));
            void L(byte[] data,int at,int n) { for(int i=0;i<4;++i)data[at+i]=(byte)(n >> (24-8*i)); }
            try {
                var mac=new byte[256+fork.Length];mac[1]=1;mac[2]=(byte)'x';L(mac,83,1);L(mac,87,fork.Length);Buffer.BlockCopy(fork,0,mac,256,fork.Length);
                System.IO.File.WriteAllBytes(path,mac); Check(TerminalPicture.FromFile(path,10001).Decode().Width==8,"MacBinary picture missing");
                var apple=new byte[38+fork.Length];L(apple,0,0x51607);apple[25]=1;L(apple,26,2);L(apple,30,38);L(apple,34,fork.Length);Buffer.BlockCopy(fork,0,apple,38,fork.Length);
                System.IO.File.WriteAllBytes(path,new byte[1]); System.IO.File.WriteAllBytes(sidecar,apple);
                Check(TerminalPicture.FromFile(path,10001).Decode().Width==8,"AppleDouble picture missing");
            } finally { System.IO.File.Delete(path);System.IO.File.Delete(sidecar); }
        });
        Test("terminal pictures reject truncated oversized and invalid compressed data", () => {
            void Bad(byte[] bytes) {
                bool rejected=false; try { new TerminalPicture {Data=bytes}.Decode(); } catch(System.IO.InvalidDataException) { rejected=true; }
                Check(rejected,"Malformed picture accepted");
            }
            Bad(new byte[9]); Bad(PictureFixture(8,true,new byte[]{0xf8,1})); Bad(PictureFixture(8,true,new byte[]{0xfa,1}));
            var truncated=PictureFixture(16,false,new byte[16]);Bad(truncated.Take(truncated.Length-4).ToArray());
            var huge=PictureFixture(8,false,new byte[8]); huge[24]=0x7f; huge[25]=0xff; Bad(huge);
        });
        Test("terminal page and branch order remains sequential across edits and serialization", () => {
            var doc=new TerminalDocument {Dirty=true};
            foreach(var branch in new[]{TerminalPageType.Failure,TerminalPageType.Success,TerminalPageType.Unfinished}) {
                doc.Pages.Add(new TerminalPage {Type=branch});
                foreach(int id in new[]{101,102,103}) {var page=new TerminalPage {Type=TerminalPageType.Picture,Permutation=(short)id};page.SetText(branch+" "+id);doc.Pages.Add(page);}
                doc.Pages.Add(new TerminalPage {Type=TerminalPageType.End});
            }
            var read=TerminalCodec.Read(TerminalCodec.Write(new[]{doc})).Single();
            Check(read.Pages.Select(p=>p.Type).SequenceEqual(doc.Pages.Select(p=>p.Type)),"Branches sorted or reversed");
            Check(read.Pages.Where(p=>p.Type==TerminalPageType.Picture).Select(p=>p.Permutation).SequenceEqual(new short[]{101,102,103,101,102,103,101,102,103}),"Pages reversed");
            var moved=read.Pages[2];read.Pages.RemoveAt(2);read.Pages.Insert(1,moved);read.Dirty=true;
            var reordered=TerminalCodec.Read(TerminalCodec.Write(new[]{read})).Single();
            Check(reordered.Pages[1].Permutation==102 && reordered.Pages[2].Permutation==101 && reordered.Pages[3].Permutation==103,"Move earlier was reversed");
        });
        Test("imported pictures encode engine RGB32 PICT without color or aspect loss", () => {
            foreach(int width in new[]{1,3,9,307,622}) {
                var image=new Image(width,4);for(int i=0;i<image.Data.Length;++i)image.Data[i]=(byte)(i*43);
                byte[] bytes=TerminalPictureImports.Encode(image);
                Check(bytes[10]==0 && bytes[11]==0x11 && bytes[12]==2 && bytes[13]==255,"Wrong PICT engine version");
                var decoded=new TerminalPicture {Data=bytes}.Decode();
                Check(decoded.Width==width && decoded.Height==4 && decoded.Data.SequenceEqual(image.Data),"Imported image distorted or colors lost");
            }
            int id=TerminalPictureImports.NextId(new[]{1000,11001,21002,11003});
            Check(id==1004,"Picture ID/variant collision");
        });
        Test("imported pictures round trip through map save load and undo without stale resources", () => {
            var level=new Level();Box(level,0,0,1000,1000);var ed=Editor(level);
            var image=new Image(3,2);image.Data[0]=255;byte[] picture=TerminalPictureImports.Encode(image);
            var source=new MapFile();var existing=new Wadfile.DirectoryEntry();existing.Chunks[Wadfile.Chunk("PICT")]=picture;source.Directory[1400]=existing;
            ed.SetUndo("Import terminal picture");level.Chunks[TerminalPictureImports.Tag]=TerminalPictureImports.Write(new System.Collections.Generic.Dictionary<int,byte[]>{{1000,picture}});ed.CommitHistory(true);
            var exported=TerminalPictureImports.Export(level.Save(),source);
            Check(TerminalPictureImports.LevelCount(exported)==1 && TerminalPicture.FromWad(exported,1000).Data.SequenceEqual(picture),"Imported picture not exported as an engine resource");
            string path=System.IO.Path.GetTempFileName();
            try {
                exported.Save(path);var loaded=new MapFile();loaded.Load(path);
                Check(loaded.Version==4,"Engine requires WAD version 4 for multiple entries; version 3 aborts");
                Check(TerminalPictureImports.LevelCount(loaded)==1 && loaded.Overlays[1000].EntryPointFlags==0,"Picture became a playable level");
                Check(TerminalPicture.FromFile(path,1000).Decode().Data.SequenceEqual(image.Data),"Picture absent from saved map");
                Check(TerminalPictureImports.Read(loaded.Directory[0])[1000].SequenceEqual(picture),"Editable image state lost on reload");
                Check(TerminalPicture.FromWad(loaded,1400).Data.SequenceEqual(picture),"Existing resource replaced");
                long savedLength=new System.IO.FileInfo(path).Length;
                ed.Undo();var undone=TerminalPictureImports.Export(ed.Level.Save(),loaded);
                Check(!undone.Directory.ContainsKey(1000) && undone.Directory.ContainsKey(1400),"Undo left a stale imported resource or removed existing resource");
                undone.Save(path); Check(new System.IO.FileInfo(path).Length < savedLength,"Smaller map retained stale trailing resource bytes");
                var reloaded=new MapFile();reloaded.Load(path);Check(TerminalPicture.FromWad(reloaded,1000)==null,"Undone picture survived disk save");
                ed.Redo();var redone=TerminalPictureImports.Export(ed.Level.Save(),undone);
                Check(TerminalPicture.FromWad(redone,1000).Decode().Width==3,"Redo lost imported picture");
                var returned=new Wadfile.DirectoryEntry();TerminalPictureImports.RestoreAfterVisualMode(redone.Directory[0],returned);
                Check(returned.Chunks[TerminalPictureImports.Tag].SequenceEqual(redone.Directory[0].Chunks[TerminalPictureImports.Tag]),"Visual mode lost editable image resources");
            } finally {System.IO.File.Delete(path);}
        });
        Test("imported picture archives reject invalid IDs lengths and collisions", () => {
            byte[] picture=TerminalPictureImports.Encode(new Image(1,1));
            byte[] bytes=TerminalPictureImports.Write(new System.Collections.Generic.Dictionary<int,byte[]>{{1000,picture}});
            foreach(var invalid in new[]{bytes.Take(bytes.Length-1).ToArray(),bytes.Concat(new byte[]{1}).ToArray(),new byte[4]}) {
                bool bad=false;try{TerminalPictureImports.Read(invalid);}catch(System.IO.InvalidDataException){bad=true;}Check(bad,"Malformed imported picture archive accepted");
            }
            var level=new Level();Box(level,0,0,1000,1000);level.Chunks[TerminalPictureImports.Tag]=bytes;
            var source=new MapFile();source.Directory[1000]=new Wadfile.DirectoryEntry();
            bool rejected=false;try{TerminalPictureImports.Export(level.Save(),source);}catch(System.IO.InvalidDataException){rejected=true;}Check(rejected,"Resource collision silently replaced existing data");
        });
        Test("engine WAD versions use 2 or 4 and resaving repairs accidental version 3", () => {
            var level=new Level();Box(level,0,0,1000,1000);
            var single=TerminalPictureImports.Export(level.Save(),new MapFile());
            string path=System.IO.Path.GetTempFileName();
            try {
                single.Save(path);var loaded=new MapFile();loaded.Load(path);Check(loaded.Version==2,"Single-entry WAD version must remain 2");
                byte[] picture=TerminalPictureImports.Encode(new Image(2,2));
                level.Chunks[TerminalPictureImports.Tag]=TerminalPictureImports.Write(new System.Collections.Generic.Dictionary<int,byte[]>{{1000,picture}});
                var multi=TerminalPictureImports.Export(level.Save(),single);multi.Save(path);
                byte[] bytes=System.IO.File.ReadAllBytes(path);Check(bytes[0]==0 && bytes[1]==4,"Invalid engine WAD version");
                bytes[1]=3;System.IO.File.WriteAllBytes(path,bytes);var legacy=new MapFile();legacy.Load(path);
                byte[] before=legacy.Directory[0].Chunks[TerminalPictureImports.Tag];
                legacy.Save(path);var repaired=new MapFile();repaired.Load(path);
                Check(repaired.Version==4 && repaired.Directory[0].Chunks[TerminalPictureImports.Tag].SequenceEqual(before),"Resave did not repair version while retaining image archive");
                Check(TerminalPicture.FromFile(path,1000).Data.SequenceEqual(picture),"Repair changed engine picture bytes");
            } finally {System.IO.File.Delete(path);}
        });
        Test("simple terminal gets an engine entry branch without reversing display pages", () => {
            var document=new TerminalDocument();
            foreach(var type in new[]{TerminalPageType.Logon,TerminalPageType.Picture,TerminalPageType.Logoff}) {
                var page=new TerminalPage {Type=type,Permutation=1000};page.SetText(type.ToString());document.Pages.Add(page);
            }
            var originalPages=document.Pages.ToArray();
            Check(document.EnsureEntryBranch() && document.Dirty,"Missing terminal entry branch not repaired");
            Check(document.Pages.Select(p=>p.Type).SequenceEqual(new[]{TerminalPageType.Unfinished,TerminalPageType.Logon,TerminalPageType.Picture,TerminalPageType.Logoff,TerminalPageType.End}),"Wrong playable terminal sequence");
            Check(document.Pages.Skip(1).Take(3).SequenceEqual(originalPages),"Display pages replaced or reversed");
            Check(!document.EnsureEntryBranch() && document.Pages.Count==5,"Repeated Apply duplicates branches");
            var loaded=TerminalCodec.Read(TerminalCodec.Write(new[]{document})).Single();
            Check(loaded.Pages[0].Type==TerminalPageType.Unfinished && loaded.Pages[1].Permutation==1000 && loaded.Pages[2].Text=="Picture","Playable branch failed serialization");
            // An already saved unbranched terminal is repaired by Apply, too.
            var old=new TerminalDocument {Dirty=true};old.Pages.Add(originalPages[1]);old.Pages.Add(new TerminalPage {Type=TerminalPageType.End});
            old=TerminalCodec.Read(TerminalCodec.Write(new[]{old})).Single();
            Check(!old.Dirty && old.EnsureEntryBranch() && old.Pages.Count==3,"Existing terminal needs a text edit to repair or End was duplicated");
        });
        Test("terminal entry repair preserves explicit branches and Marathon 1 fallback", () => {
            foreach(var type in new[]{TerminalPageType.Unfinished,TerminalPageType.Success,TerminalPageType.Failure}) {
                var document=new TerminalDocument();document.Pages.Add(new TerminalPage {Type=type});document.Pages.Add(new TerminalPage {Type=TerminalPageType.Picture});
                Check(!document.EnsureEntryBranch() && !document.Dirty && document.Pages.Count==2,"Explicit completion branch changed");
            }
            var m1=new TerminalDocument();m1.Pages.Add(new TerminalPage {Type=TerminalPageType.Logon,Flags=0x100});
            Check(!m1.EnsureEntryBranch() && !m1.Dirty && m1.Pages.Count==1,"Marathon 1 fallback terminal changed");
            Check(!new TerminalDocument().EnsureEntryBranch(),"Empty terminal gained unused markers");
        });
        Test("terminal engine headers pages styles and MacRoman text", () => {
            var document = TerminalDocument.Create(); document.Pages[1].SetText("Åäö\nHello");
            document.Pages[1].MaximumLines = 2;
            document.Pages[1].Styles[1] = new TerminalStyle(7,4);
            document.Pages.Insert(2,new TerminalPage { Type = TerminalPageType.InterlevelTeleport, Permutation = 3 });
            byte[] bytes = TerminalCodec.Write(new[] {document});
            Check((bytes[0] << 8 | bytes[1]) == bytes.Length && bytes[6] == 0 && bytes[7] == 4, "Wrong terminal engine header");
            var read = TerminalCodec.Read(bytes).Single();
            Check(read.Pages[1].Text == "Åäö\nHello" && read.Pages[1].Styles[1].Face == 7 && read.Pages[1].Styles[1].Color == 4, "Terminal text/format lost");
            Check(read.Pages[2].Type == TerminalPageType.InterlevelTeleport && read.Pages[2].Permutation == 3 && read.Pages[2].MaximumLines == 1, "Action ID or line count wrong");
            Check(TerminalCodec.Write(new[] {read}).SequenceEqual(bytes), "Untouched terminal changed");
            read.Dirty = true;
            Check(TerminalCodec.Read(TerminalCodec.Write(new[] {read}))[0].Pages[1].Text == "Åäö\nHello", "Terminal recompilation lost text");
        });
        Test("terminal encoded text exact preservation and mixed document edits", () => {
            foreach (int length in Enumerable.Range(0,9)) {
                var doc = TerminalDocument.Create(); doc.Flags = 1;
                doc.Pages[1].SetText(new string('x',length));
                byte[] bytes = TerminalCodec.Write(new[] {doc});
                var read = TerminalCodec.Read(bytes).Single();
                Check(read.Pages[1].Text == new string('x',length), "Terminal encoding tail broken");
                Check(TerminalCodec.Write(new[] {read}).SequenceEqual(bytes), "Encoded terminal bytes changed");
                var added = TerminalDocument.Create();
                byte[] combined = TerminalCodec.Write(new[] {read,added});
                Check(combined.Take(bytes.Length).SequenceEqual(bytes) && TerminalCodec.Read(combined).Count == 2, "Adding terminal changed existing IDs/data");
            }
        });
        Test("terminal malformed chunks and encoding limits rejected", () => {
            byte[] bytes = TerminalCodec.Write(new[] {TerminalDocument.Create()});
            foreach (int length in new[] {1,9,10,bytes.Length-1}) {
                bool rejected = false;
                try { TerminalCodec.Read(bytes.Take(length).ToArray()); } catch (System.IO.InvalidDataException) { rejected = true; }
                Check(rejected, "Truncated terminal accepted");
            }
            var doc = TerminalDocument.Create(); doc.Pages[1].SetText(new string('x',32767));
            bool tooLong = false;
            try { TerminalCodec.Write(new[] {doc}); } catch (ArgumentException) { tooLong = true; }
            Check(tooLong, "Terminal text offset overflow accepted");
            doc.Pages[1].SetText("🙂"); bool unsupported = false;
            try { TerminalCodec.Write(new[] {doc}); } catch (System.Text.EncoderFallbackException) { unsupported = true; }
            Check(unsupported, "Unsupported characters silently lost");
        });
        Test("terminals survive disk save load undo redo and Lua coexistence", () => {
            var level = new Level(); Box(level,0,0,100,100); var editor = Editor(level);
            level.Chunks[EmbeddedLua.Tag] = EmbeddedLua.Write(new[] {new EmbeddedLuaScript {Name = "test", Text = "return 1"}});
            byte[] lua = (byte[])level.Chunks[EmbeddedLua.Tag].Clone();
            editor.SetUndo("Edit level terminals");
            level.Chunks[TerminalCodec.Tag] = TerminalCodec.Write(new[] {TerminalDocument.Create()}); editor.CommitHistory(true);
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),Guid.NewGuid()+".sceA");
            try {
                var map = new MapFile(); map.Directory[0] = level.Save(); map.Save(path);
                var reopened = new MapFile(); reopened.Load(path); var loaded = new Level(); loaded.Load(reopened.Directory[0]);
                Check(TerminalCodec.Read(loaded.Chunks[TerminalCodec.Tag]).Count == 1 && loaded.Chunks[EmbeddedLua.Tag].SequenceEqual(lua), "Saved terminals or Lua lost");
            } finally { System.IO.File.Delete(path); }
            editor.Undo(); Check(!level.Chunks.ContainsKey(TerminalCodec.Tag), "Terminal undo failed");
            editor.Redo(); Check(level.Chunks.ContainsKey(TerminalCodec.Tag), "Terminal redo failed");
            var original = level.Save().Clone(); var visual = EmbeddedLua.ForVisualMode(original,null);
            Check(visual.Chunks[TerminalCodec.Tag].SequenceEqual(original.Chunks[TerminalCodec.Tag]), "Visual-mode preparation changed terminals");
        });
        Test("embedded Lua engine layout and UTF8 round trip", () => {
            var scripts = new[] {
                new EmbeddedLuaScript { Name = "Första", Flags = 0x12345678, Text = "-- snö 雪\nTriggers = {}\n" },
                new EmbeddedLuaScript { Name = "Second", Text = "print('hello')\r\n" }
            };
            byte[] bytes = EmbeddedLua.Write(scripts);
            Check(bytes[0] == 0 && bytes[1] == 2, "Wrong big-endian script count");
            Check(bytes.Skip(2).Take(4).SequenceEqual(new byte[] {0x12,0x34,0x56,0x78}), "Flags not big endian");
            int length = scripts[0].Source.Length;
            Check(bytes[72] == 0 && bytes[73] == 0 && bytes[74] == 0 && bytes[75] == length, "Wrong source length offset");
            Check(bytes.Skip(76).Take(length).SequenceEqual(scripts[0].Source), "Wrong engine source offset");
            var read = EmbeddedLua.Read(bytes);
            Check(read.Count == 2 && read[0].Name == "Första" && read[1].Text == scripts[1].Text, "Lua contents changed");
            Check(EmbeddedLua.Write(read).SequenceEqual(bytes), "Untouched script headers or source changed");
        });
        Test("embedded Lua survives disk save load and undo redo", () => {
            var level = new Level(); Box(level,0,0,100,100); var editor = Editor(level);
            var scripts = new[] {new EmbeddedLuaScript { Name = "test.lua", Text = "Triggers = {}\n" }};
            editor.SetUndo("Edit level Lua scripts"); level.Chunks[EmbeddedLua.Tag] = EmbeddedLua.Write(scripts);
            editor.Changed = true; editor.CommitHistory(true);
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid()+".sceA");
            try {
                var map = new MapFile(); map.Directory[0] = level.Save(); map.Save(path);
                var loaded = new MapFile(); loaded.Load(path); var reopened = new Level(); reopened.Load(loaded.Directory[0]);
                Check(reopened.Chunks[EmbeddedLua.Tag].SequenceEqual(level.Chunks[EmbeddedLua.Tag]), "Map save lost embedded Lua");
            } finally { System.IO.File.Delete(path); }
            editor.Undo(); Check(!level.Chunks.ContainsKey(EmbeddedLua.Tag), "Lua undo failed");
            editor.Redo(); Check(EmbeddedLua.Read(level.Chunks[EmbeddedLua.Tag])[0].Name == "test.lua", "Lua redo failed");
            editor.SetUndo("Remove level Lua scripts"); level.Chunks.Remove(EmbeddedLua.Tag); editor.CommitHistory(true);
            editor.Undo(); Check(level.Chunks.ContainsKey(EmbeddedLua.Tag), "Script removal cannot undo");
        });
        Test("embedded Lua runs in visual mode and restores unchanged on return", () => {
            var level = new Level(); Box(level,0,0,100,100);
            byte[] bytes = EmbeddedLua.Write(new[] { new EmbeddedLuaScript { Name = "kill-floor.lua", Text = "Triggers = {}\n" } });
            level.Chunks[EmbeddedLua.Tag] = bytes;
            var original = level.Save().Clone();
            var editing = EmbeddedLua.ForVisualMode(original, "Triggers = {}\n");
            var scripts=EmbeddedLua.Read(editing.Chunks[EmbeddedLua.Tag]);
            Check(scripts.Count==2 && scripts[0].Name=="kill-floor.lua" && scripts[1].Name=="Weland Visual Mode Locator", "Gameplay scripts or locator lost/reordered");
            Check(scripts[0].Source.SequenceEqual(EmbeddedLua.Read(bytes)[0].Source),"Gameplay source changed");
            Check(original.Chunks[EmbeddedLua.Tag].SequenceEqual(bytes) && level.Chunks[EmbeddedLua.Tag].SequenceEqual(bytes), "Preparing visual mode changed map Lua");
            EmbeddedLua.RestoreAfterVisualMode(original, editing);
            Check(editing.Chunks[EmbeddedLua.Tag].SequenceEqual(bytes), "Returning lost gameplay scripts");
            editing.Chunks[EmbeddedLua.Tag][0] = 7;
            Check(original.Chunks[EmbeddedLua.Tag][0] == 0, "Returned Lua shares mutable storage");
            Check(EmbeddedLua.ForVisualMode(original, null).Chunks[EmbeddedLua.Tag].SequenceEqual(bytes), "No-locator visual mode lost gameplay Lua");
            original.Chunks.Remove(EmbeddedLua.Tag); editing = EmbeddedLua.ForVisualMode(original, "Triggers = {}\n");
            EmbeddedLua.RestoreAfterVisualMode(original, editing);
            Check(!editing.Chunks.ContainsKey(EmbeddedLua.Tag), "Locator leaked into map without Lua");
        });
        Test("embedded Lua rejects truncated chunks and invalid names", () => {
            byte[] bytes = EmbeddedLua.Write(new[] {new EmbeddedLuaScript {Name = "test", Text = "return 1"}});
            foreach (int length in new[] {0,1,2,40,75,bytes.Length-1}) {
                bool rejected = false;
                try { EmbeddedLua.Read(bytes.Take(length).ToArray()); } catch (System.IO.InvalidDataException) { rejected = true; }
                Check(rejected, "Truncated Lua accepted");
            }
            foreach (string name in new[] {"", new string('a',66), "a\0b"}) {
                bool rejected = false;
                try { _ = new EmbeddedLuaScript { Name = name }; } catch (ArgumentException) { rejected = true; }
                Check(rejected, "Invalid script name accepted");
            }
            Check(EmbeddedLua.Read(null).Count == 0, "Missing Lua chunk is not empty");
        });
        Test("MML finds and deduplicates surface collections", () => {
            var level = new Level(); Box(level, 0, 0, 100, 100);
            level.Polygons[0].FloorTexture = new ShapeDescriptor { Collection = 18, Bitmap = 3 };
            level.Polygons[0].CeilingTexture = new ShapeDescriptor { Collection = 18, Bitmap = 4 };
            level.Sides.Add(new Side {
                Primary = new Side.TextureDefinition { Texture = new ShapeDescriptor { Collection = 19 } },
                Secondary = new Side.TextureDefinition { Texture = ShapeDescriptor.Empty },
                Transparent = new Side.TextureDefinition { Texture = new ShapeDescriptor { Collection = 20 } }
            });
            Check(LevelMml.TextureCollections(level).SequenceEqual(new[] {18, 19, 20}), "Missing or duplicate collection");
            var mml = LevelMml.ForLevel(level);
            Check(mml.Collections.Take(3).All(c => c == -2), "Scenario base slots overwritten");
            Check(mml.Collections.Skip(3).SequenceEqual(new[] {18,19,20,-2}), "Wrong suggested slots");
            Check(mml.Build().Descendants("texture_env").Count() == 3, "Unchanged slots emitted");
        });
        Test("MML supports all slots and explicit clearing", () => {
            var options = new LevelMml { Environment = 4, IncludeCollections = true,
                Collections = new[] {17,18,19,20,21,22,-1} };
            var entries = options.Build().Descendants("texture_env").ToArray();
            Check(entries.Length == 7 && (int)entries[6].Attribute("coll") == -1, "Wrong slot count or clear value");
            Check(entries.All(e => (int)e.Attribute("index") == 4), "Wrong environment");
            Check(!options.Build().Descendants("texture_loading").Single().Attributes("landscapes").Any(), "Landscape loading changed");
        });
        Test("MML fog uses engine attributes and invariant numbers", () => {
            var culture = System.Globalization.CultureInfo.CurrentCulture;
            try {
                System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.GetCultureInfo("sv-SE");
                var options = new LevelMml();
                options.Above = new MmlFog { Include = true, Enabled = true, Depth = 12.5, Red = .25, Green = .5, Blue = .75 };
                options.Below = new MmlFog { Include = true, Enabled = false, Landscapes = false, Depth = 3.25 };
                var document = System.Xml.Linq.XDocument.Parse(options.Build().ToString());
                var fog = document.Descendants("fog").ToArray();
                Check(fog.Length == 2 && (int)fog[1].Attribute("type") == 1, "Wrong fog types");
                Check((string)fog[0].Attribute("depth") == "12.5", "Localized depth breaks engine parser");
                Check((string)fog[0].Element("color").Attribute("red") == "0.25", "Wrong color attribute or format");
                Check(!(bool)fog[1].Attribute("on") && !(bool)fog[1].Attribute("landscapes"), "Explicit fog disable lost");
                Check(!document.Descendants("texture_loading").Any(), "Fog-only export changed collections");
            } finally { System.Globalization.CultureInfo.CurrentCulture = culture; }
        });
        Test("MML rejects invalid or empty settings", () => {
            void RejectMml(LevelMml mml) {
                bool rejected = false;
                try { mml.Build(); } catch (ArgumentException) { rejected = true; }
                Check(rejected, "Invalid MML settings accepted");
            }
            RejectMml(new LevelMml());
            RejectMml(new LevelMml { IncludeCollections = true });
            RejectMml(new LevelMml { IncludeCollections = true, Collections = new[] {17} });
            RejectMml(new LevelMml { IncludeCollections = true, Environment = 5 });
            RejectMml(new LevelMml { IncludeCollections = true, Collections = new[] {32,-2,-2,-2,-2,-2,-2} });
            RejectMml(new LevelMml { Above = new MmlFog { Include = true, Depth = double.NaN } });
            RejectMml(new LevelMml { Above = new MmlFog { Include = true, Red = -0.1 } });
        });
        Test("MML suggestions never overflow spare slots", () => {
            var level = new Level(); Box(level,0,0,100,100);
            for (int i = 18; i < 23; ++i) level.Sides.Add(new Side {
                Primary = new Side.TextureDefinition { Texture = new ShapeDescriptor { Collection = (byte)i } },
                Secondary = new Side.TextureDefinition { Texture = ShapeDescriptor.Empty },
                Transparent = new Side.TextureDefinition { Texture = ShapeDescriptor.Empty }
            });
            var options = LevelMml.ForLevel(level);
            Check(!options.IncludeCollections && options.Collections.All(c => c == -2), "Partial suggestion silently omitted needed collections");
        });
        Test("all drag directions", () => {
            foreach (var pair in new[] { new[] {0,0,100,100}, new[] {100,100,0,0}, new[] {100,0,0,100}, new[] {0,100,100,0} }) {
                var level = new Level(); Box(level, pair[0],pair[1],pair[2],pair[3]);
                Check(level.Endpoints.Count == 4 && level.Lines.Count == 4, "Bad box counts");
            }
        });
        Test("full shared edge", () => {
            var level = new Level(); Box(level,0,0,100,100); Box(level,100,0,200,100);
            Check(level.Endpoints.Count == 6 && level.Lines.Count == 7, "Shared geometry duplicated");
            Check(level.Lines.Count(l => l.ClockwisePolygonOwner >= 0 && l.CounterclockwisePolygonOwner >= 0) == 1, "No shared owner");
        });
        Test("partial shared edge and neighboring settings", () => {
            var level = new Level(); Box(level,0,0,100,100);
            level.Polygons[0].FloorHeight = 123; level.Polygons[0].CeilingLight = 5;
            Box(level,100,25,200,75);
            Check(level.Polygons[0].VertexCount == 6, "Wall did not split twice");
            Check(level.Polygons[1].FloorHeight == 123 && level.Polygons[1].CeilingLight == 5, "Settings not inherited");
            var loaded = new Level(); loaded.Load(level.Save().Clone()); Validate(loaded);
        });
        Test("splits on both wall orientations", () => {
            foreach (var pair in new[] { new[]{-100,25,0,75}, new[]{25,-100,75,0}, new[]{25,100,75,200} }) {
                var level = new Level(); Box(level,0,0,100,100); Box(level,pair[0],pair[1],pair[2],pair[3]);
            }
        });
        Test("shared boundary containing extra vertex", () => {
            var level = new Level(); Box(level,0,0,100,100);
            var wall = level.Lines.FindIndex(l => l.EndpointIndexes.All(i => level.Endpoints[i].X == 100));
            level.SplitLine((short)wall,100,50); Box(level,100,0,200,100);
            Check(level.Polygons[1].VertexCount == 5, "Intermediate endpoint omitted");
        });
        Test("reuse orphan vertex when splitting a wall", () => {
            var level = new Level(); Box(level,0,0,100,100); level.NewPoint(100,50);
            Box(level,100,0,200,100);
        });
        Test("overlap and containment rejected without mutations", () => {
            var level = new Level(); Box(level,0,0,100,100);
            Reject(level,0,0,100,100); Reject(level,50,50,150,150);
            Reject(level,-10,-10,110,110);
            Reject(level,0,0,0,100);
        });
        Test("crossing stray lines rejected", () => {
            foreach (var ends in new[] {new[]{-50,50,150,50}, new[]{-50,-50,150,150},new[]{50,50,150,150}}) {
                var level = new Level(); level.NewLine(level.NewPoint((short)ends[0],(short)ends[1]),level.NewPoint((short)ends[2],(short)ends[3]));
                Reject(level,0,0,100,100);
            }
        });
        Test("boundary lines reused and exterior corner contact split", () => {
            var level = new Level(); level.NewLine(level.NewPoint(-50,0),level.NewPoint(150,0));
            Box(level,0,0,100,100);
            Check(level.Lines.Count == 6, "Partial existing line not reused");
            level = new Level(); level.NewLine(level.NewPoint(-50,50),level.NewPoint(50,-50)); Box(level,0,0,100,100);
        });
        Test("hidden geometry still blocks overlap", () => {
            var level = new Level(); Box(level,0,0,100,100);
            Level.Filter = p => false;
            try { Reject(level,10,10,90,90); Box(level,100,0,200,100); }
            finally { Level.Filter = p => true; }
        });
        Test("eight vertex limits checked before splitting", () => {
            var level = new Level(); Box(level,0,0,100,100);
            Box(level,100,10,200,20); Box(level,100,30,200,40);
            Check(level.Polygons[0].VertexCount == 8, "Wrong adjacent vertex count");
            Reject(level,100,50,200,60);
            level = new Level();
            for (short y = 10; y <= 50; y += 10) level.NewPoint(0,y);
            Reject(level,0,0,100,100);
        });
        Test("drag preview, snapping, undo and redo", () => {
            var level = new Level(); Box(level,0,0,100,100); string before = Snapshot(level);
            var editor = Editor(level); editor.ButtonPress(103,3,EditorModifiers.None);
            editor.Motion(200,100,EditorModifiers.None);
            Check(editor.BoxStart.Equals(P(100,0)), "Start did not snap");
            Check(Snapshot(level) == before, "Preview changed geometry");
            editor.ButtonRelease(200,100,EditorModifiers.None); Validate(level);
            string after = CanonicalSnapshot(level); editor.Undo();
            Check(level.Polygons.Count == 1 && level.Endpoints.Count == 4 && level.Lines.Count == 4, "Undo failed");
            Validate(level);
            editor.Redo(); Check(CanonicalSnapshot(level) == after, "Redo failed");
        });
        Test("grid and derived-corner snapping", () => {
            var level = new Level(); level.NewPoint(200,0);
            var editor = Editor(level); editor.StartBox(0,0); editor.FinishBox(204,100);
            Check(editor.BoxEnd.X == 200, "Derived corner did not snap"); Validate(level);
            level = new Level(); editor = Editor(level);
            editor.Grid.Visible = true; editor.Grid.Resolution = 100; editor.Grid.UseCustomGrid = false;
            editor.StartBox(2,3); editor.FinishBox(197,201);
            Check(editor.BoxStart.Equals(P(0,0)) && editor.BoxEnd.Equals(P(200,200)), "Grid snapping failed");
        });
        Test("cancel and failed drag preserve existing undo", () => {
            var level = new Level(); var editor = Editor(level);
            editor.StartBox(0,0); editor.FinishBox(100,100);
            string before = Snapshot(level);
            editor.StartBox(100,0); editor.UpdateBox(200,100); editor.CancelBox();
            Check(Snapshot(level) == before && !editor.DrawingBox, "Cancel changed geometry");
            editor.StartBox(50,50); editor.FinishBox(150,150);
            Check(Snapshot(level) == before && editor.BoxMessage != null, "Failed drag changed map");
            editor.Undo(); Check(level.Polygons.Count == 0 && level.Endpoints.Count == 0, "Failed drag replaced undo");
        });
        Test("partial wall split is one undo action", () => {
            var level = new Level(); Box(level,0,0,100,100);
            var editor = Editor(level); editor.StartBox(100,25); editor.FinishBox(200,75);
            Validate(level); Check(level.Polygons[0].VertexCount == 6, "Missing wall splits");
            string after = CanonicalSnapshot(level); editor.Undo(); Validate(level);
            Check(level.Polygons.Count == 1 && level.Polygons[0].VertexCount == 4 && level.Lines.Count == 4, "Undo left split geometry behind");
            editor.Redo(); Validate(level); Check(CanonicalSnapshot(level) == after, "Split redo failed");
        });
        Test("textured wall split preserves remaining sides", () => {
            var level = new Level(); Box(level,0,0,100,100);
            int wall = level.Lines.FindIndex(l => l.EndpointIndexes.All(i => level.Endpoints[i].X == 100));
            var side = new Side(); side.Primary.Texture = (ShapeDescriptor)1; side.Primary.X = 11;
            side.LineIndex = (short)wall; side.PolygonIndex = 0;
            level.Sides.Add(side); level.Lines[wall].ClockwisePolygonSideIndex = 0;
            Box(level,100,25,200,75);
            Check(level.Sides.Count == 3, "Wall sides lost during splitting");
            var loaded = new Level(); loaded.Load(level.Save().Clone()); Validate(loaded);
        });
        Test("textured outer wall expansion keeps side IDs and adjacency on save and undo", () => {
            foreach (bool partial in new[]{false,true})
            foreach (var bounds in new[]{new[]{1000,0,2000,1000},new[]{-1000,0,0,1000},new[]{0,-1000,1000,0},new[]{0,1000,1000,2000}}) {
                var level=new Level();Box(level,0,0,1000,1000);level.Pave();
                var editor=Editor(level);var b=(int[])bounds.Clone();
                if(partial) {if(b[0]==0 && b[2]==1000){b[0]=250;b[2]=750;}else{b[1]=250;b[3]=750;}}
                editor.StartBox((short)b[0],(short)b[1]);editor.FinishBox((short)b[2],(short)b[3]);
                void CheckReferences(Level map) {
                    foreach(var line in map.Lines.Where(l=>l.ClockwisePolygonOwner>=0 && l.CounterclockwisePolygonOwner>=0)) {
                        foreach(short owner in new[]{line.ClockwisePolygonOwner,line.CounterclockwisePolygonOwner}) {
                            var polygon=map.Polygons[owner];int edge=Array.IndexOf(polygon.LineIndexes,(short)map.Lines.IndexOf(line),0,polygon.VertexCount);
                            bool cw=line.ClockwisePolygonOwner==owner;short side=cw?line.ClockwisePolygonSideIndex:line.CounterclockwisePolygonSideIndex;
                            Check(polygon.AdjacentPolygonIndexes[edge]==(cw?line.CounterclockwisePolygonOwner:line.ClockwisePolygonOwner),"Shared edge still has -1 neighbor");
                            Check(polygon.SideIndexes[edge]==side,"Polygon side ID differs from textured line side");
                            if(owner==0) Check(side>=0 && !map.Sides[side].Primary.Texture.IsEmpty() && map.Sides[side].PolygonIndex==owner,"Existing wall texture or owner lost");
                        }
                    }
                }
                CheckReferences(level);var loaded=new Level();loaded.Load(level.Save().Clone());CheckReferences(loaded);
                editor.Undo();Check(level.Polygons.Count==1,"Expansion undo failed");editor.Redo();CheckReferences(level);Validate(level);
            }
        });
        Test("Pave repairs stale sides removes old outer walls and fills missing surfaces", () => {
            var level=new Level();Box(level,0,0,1000,1000);level.Pave();
            int wall=level.Lines.FindIndex(l=>l.EndpointIndexes.All(i=>level.Endpoints[i].X==1000));
            var line=level.Lines[wall];short oldSide=line.ClockwisePolygonSideIndex>=0?line.ClockwisePolygonSideIndex:line.CounterclockwisePolygonSideIndex;
            level.Sides[oldSide].Primary.Texture=(ShapeDescriptor)123;Box(level,1000,0,2000,1000);
            foreach(var side in level.Sides){side.PolygonIndex=-1;side.LineIndex=-1;}
            level.Polygons[1].FloorTexture=ShapeDescriptor.Empty;level.Polygons[1].CeilingTexture=ShapeDescriptor.Empty;
            var editor=Editor(level);editor.SetUndo("Clean up wall surfaces");level.Pave();
            Check(line.ClockwisePolygonSideIndex==-1 && line.CounterclockwisePolygonSideIndex==-1,"Old textured outer wall survived as an invisible surface");
            Check(level.Sides.Count==6 && level.Sides.All(s=>s.LineIndex>=0 && s.PolygonIndex>=0 && !s.Primary.Texture.IsEmpty()),"Pave did not repair and texture exterior sides");
            Check(!level.Polygons[1].FloorTexture.IsEmpty() && !level.Polygons[1].CeilingTexture.IsEmpty(),"Pave skipped floor or ceiling");
            string clean=CanonicalSnapshot(level);level.Pave();Check(CanonicalSnapshot(level)==clean,"Cleanup is not idempotent");
            editor.Undo();Check(level.Sides.Any(s=>s.Primary.Texture.Equals((ShapeDescriptor)123)),"Cleanup undo lost old wall texture");
            editor.Redo();Check(CanonicalSnapshot(level)==clean,"Cleanup redo failed");
            var loaded=new Level();loaded.Load(level.Save().Clone());Check(loaded.Sides.Count==6,"Saved cleanup failed");Validate(loaded);
        });
        Test("Pave textures height steps and preserves transparent portal surfaces", () => {
            var level=new Level();Box(level,0,0,1000,1000);Box(level,1000,0,2000,1000);
            int wall=level.Lines.FindIndex(l=>l.ClockwisePolygonOwner>=0 && l.CounterclockwisePolygonOwner>=0);
            short transparent=level.NewSide(0,(short)wall);level.Sides[transparent].Transparent.Texture=(ShapeDescriptor)123;
            level.Pave();Check(level.Sides.Any(s=>s.Transparent.Texture.Equals((ShapeDescriptor)123)),"Cleanup erased transparent wall");
            level.Polygons[1].FloorHeight=100;level.Polygons[1].CeilingHeight-=100;level.Pave();
            var line=level.Lines[wall];short step=line.ClockwisePolygonOwner==0?line.ClockwisePolygonSideIndex:line.CounterclockwisePolygonSideIndex;
            Check(step>=0 && level.Sides[step].Type==SideType.Split && !level.Sides[step].Primary.Texture.IsEmpty() && !level.Sides[step].Secondary.Texture.IsEmpty(),"Pave skipped upper or lower height step");
            Check(level.Sides[step].Transparent.Texture.Equals((ShapeDescriptor)123),"Pave replaced existing transparent texture");
        });
        Test("large coordinate ranges and narrow boxes", () => {
            var level = new Level(); Box(level,-30000,-30000,30000,30000);
            level = new Level(); Box(level,0,0,1,100); Box(level,0,1,1,99);
            Check(level.Polygons.Count==3,"Narrow full-width inset did not split into three regions");
        });
        Test("selection box in both drag directions", () => {
            foreach (bool reverse in new[] {false,true}) {
                var level = new Level(); Box(level,0,0,100,100); Box(level,200,0,300,100);
                var editor = Editor(level); editor.Tool = Tool.Select; string before = Snapshot(level);
                editor.ButtonPress((short)(reverse?120:-20),(short)(reverse?120:-20),EditorModifiers.None);
                editor.Motion((short)(reverse?-20:120),(short)(reverse?-20:120),EditorModifiers.None);
                Check(editor.DrawingSelectionBox, "Missing selection preview");
                editor.ButtonRelease((short)(reverse?-20:120),(short)(reverse?-20:120),EditorModifiers.None);
                Check(editor.Selection.Points.Count == 4 && editor.Selection.Lines.Count == 4, "Wrong enclosed selection");
                Check(Snapshot(level) == before && !editor.Changed && !editor.DrawingSelectionBox, "Selection changed map");
            }
        });
        Test("only completely enclosed lines selected", () => {
            var level = new Level(); level.NewLine(level.NewPoint(0,0),level.NewPoint(100,0));
            var editor = Editor(level); editor.Tool = Tool.Select;
            editor.ButtonPress(-20,-20,EditorModifiers.None); editor.ButtonRelease(50,20,EditorModifiers.None);
            Check(editor.Selection.Points.SetEquals(new short[]{0}) && editor.Selection.Lines.Count == 0, "Crossing line selected");
        });
        Test("shift extends a box and toggles items", () => {
            var level = new Level(); Box(level,0,0,100,100); Box(level,200,0,300,100);
            var editor = Editor(level); editor.Tool = Tool.Select;
            editor.ButtonPress(-20,-20,EditorModifiers.None); editor.ButtonRelease(120,120,EditorModifiers.None);
            editor.ButtonPress(180,-20,EditorModifiers.Shift); editor.ButtonRelease(320,120,EditorModifiers.Shift);
            Check(editor.Selection.Points.Count == 8 && editor.Selection.Lines.Count == 8, "Shift selection replaced old group");
            editor.ButtonPress(0,0,EditorModifiers.Shift); editor.ButtonRelease(0,0,EditorModifiers.Shift);
            Check(editor.Selection.Points.Count == 7, "Shift click did not toggle vertex");
            editor.ButtonPress(50,0,EditorModifiers.Shift); editor.ButtonRelease(50,0,EditorModifiers.Shift);
            Check(editor.Selection.Lines.Count == 7, "Shift click did not toggle line");
        });
        Test("group drag moves shared endpoints exactly once and undoes", () => {
            var level = new Level(); Box(level,0,0,100,100);
            var editor = Editor(level); editor.Tool = Tool.Select;
            editor.ButtonPress(-20,-20,EditorModifiers.None); editor.ButtonRelease(120,120,EditorModifiers.None);
            string before = CanonicalSnapshot(level);
            editor.ButtonPress(0,0,EditorModifiers.None); editor.Motion(20,30,EditorModifiers.None);
            editor.Motion(25,35,EditorModifiers.None); editor.ButtonRelease(25,35,EditorModifiers.None);
            Check(level.Endpoints.Contains(P(25,35)) && level.Endpoints.Contains(P(125,135)), "Group moved unevenly or more than once");
            editor.Undo(); Check(CanonicalSnapshot(level) == before && editor.Selection.Points.Count == 4, "Group undo lost geometry or selection");
            editor.Redo(); Check(level.Endpoints.Contains(P(25,35)), "Group redo failed");
        });
        Test("group nudge and line-only dragging", () => {
            var level = new Level(); Box(level,0,0,100,100);
            var editor = Editor(level); editor.Tool = Tool.Select;
            editor.ButtonPress(50,0,EditorModifiers.Shift); editor.ButtonRelease(50,0,EditorModifiers.Shift);
            Check(editor.Selection.Lines.Count == 1 && editor.Selection.Points.Count == 0, "Line-only selection failed");
            editor.ButtonPress(50,0,EditorModifiers.None); editor.Motion(50,20,EditorModifiers.None); editor.ButtonRelease(50,20,EditorModifiers.None);
            Check(level.Endpoints.Contains(P(0,20)) && level.Endpoints.Contains(P(100,20)), "Line group drag failed");
            editor.NudgeSelected(10,0); Check(level.Endpoints.Contains(P(10,20)), "Group nudge failed");
        });
        Test("group delete survives endpoint and line renumbering", () => {
            var level = new Level(); Box(level,0,0,100,100); Box(level,200,0,300,100);
            var editor = Editor(level); editor.Tool = Tool.Select;
            editor.ButtonPress(-20,-20,EditorModifiers.None); editor.ButtonRelease(120,120,EditorModifiers.None);
            editor.DeleteSelected(); Validate(level);
            Check(level.Polygons.Count == 1 && level.Endpoints.Count == 4 && !editor.Selection.HasMultipleGeometry, "Group deletion removed unrelated geometry");
            editor.Undo(); Validate(level); Check(level.Polygons.Count == 2 && editor.Selection.Points.Count == 4, "Deletion undo lost selection");
        });
        Test("cancel selection box restores earlier group", () => {
            var level = new Level(); Box(level,0,0,100,100); Box(level,200,0,300,100);
            var editor = Editor(level); editor.Tool = Tool.Select;
            editor.ButtonPress(-20,-20,EditorModifiers.None); editor.ButtonRelease(120,120,EditorModifiers.None);
            editor.ButtonPress(180,-20,EditorModifiers.None); editor.Motion(320,120,EditorModifiers.None);
            editor.CancelSelectionBox(); Check(editor.Selection.Points.SetEquals(new short[]{0,1,2,3}), "Cancel lost previous group");
            var copy = new Selection(); copy.CopyFrom(editor.Selection); editor.Selection.Clear();
            Check(copy.Points.Count == 4, "Selection snapshot shared sets");
        });
        Test("selection respects view filters", () => {
            var level = new Level(); Box(level,0,0,100,100);
            var editor = Editor(level); editor.Tool = Tool.Select;
            Level.Filter = p => false; Level.FilterPoints = true;
            try {
                editor.ButtonPress(-20,-20,EditorModifiers.None); editor.ButtonRelease(120,120,EditorModifiers.None);
                Check(!editor.Selection.HasMultipleGeometry, "Hidden geometry selected");
            } finally { Level.Filter = p => true; Level.FilterPoints = false; }
        });
        Test("nested box creates five covering polygons without duplicate geometry", () => {
            var level = new Level(); Box(level,0,0,1000,1000); Box(level,200,300,700,800);
            Check(level.Polygons.Count == 5 && level.Endpoints.Count == 12 && level.Lines.Count == 16, "Incorrect inset partition counts");
            Check(level.LastCreatedBoxPolygonIndex == 0 && level.Polygons[0].VertexCount == 4, "Center did not retain original index");
            long area = 0;
            foreach (Polygon polygon in level.Polygons) {
                long twice = 0;
                for(int i=0;i<polygon.VertexCount;i++) {
                    Point a=level.Endpoints[polygon.EndpointIndexes[i]], b=level.Endpoints[polygon.EndpointIndexes[(i+1)%polygon.VertexCount]];
                    twice += (long)a.X*b.Y-(long)a.Y*b.X;
                }
                area += Math.Abs(twice);
            }
            Check(area == 2000000, "Inset left a gap or overlap");
            var loaded = new Level(); loaded.Load(level.Save().Clone()); Validate(loaded);
        });
        Test("nested box retains settings and reassigns objects and notes", () => {
            var level = new Level(); Box(level,0,0,1000,1000);
            Polygon original=level.Polygons[0]; original.FloorHeight=123; original.CeilingLight=5;
            original.FloorOrigin=P(21,42); original.MediaIndex=2; original.AmbientSound=3;
            foreach(Point p in new[]{P(400,400),P(500,100),P(500,900),P(100,500),P(900,500)}) {
                level.NewObject(p,0); level.Annotations.Add(new Annotation{X=p.X,Y=p.Y,PolygonIndex=0,Text="keep"});
            }
            Box(level,200,300,700,800);
            Check(level.Objects.Count==5 && level.Annotations.Count==5, "Objects or notes deleted");
            Check(level.Objects[0].PolygonIndex==0 && level.Objects.Select(o=>o.PolygonIndex).Distinct().Count()==5, "Objects not assigned to their new regions");
            Check(level.Annotations.Select(o=>o.PolygonIndex).Distinct().Count()==5, "Notes not reassigned");
            foreach(Polygon polygon in level.Polygons)
                Check(polygon.FloorHeight==123 && polygon.CeilingLight==5 && polygon.FloorOrigin.Equals(P(21,42)) && polygon.MediaIndex==2 && polygon.AmbientSound==3, "Settings lost");
        });
        Test("nested box preserves neighboring indices and wall textures", () => {
            var level = new Level(); Box(level,0,0,1000,1000); Box(level,1000,0,2000,1000);
            int wall=level.Lines.FindIndex(l=>l.ClockwisePolygonOwner==0 && l.CounterclockwisePolygonOwner==1);
            Check(wall>=0,"Missing initial shared wall");
            var inside = new Side{LineIndex=(short)wall,PolygonIndex=0}; inside.Primary.Texture=(ShapeDescriptor)1;inside.Primary.X=10;
            var outside = new Side{LineIndex=(short)wall,PolygonIndex=1}; outside.Primary.Texture=(ShapeDescriptor)2;outside.Primary.X=20;
            level.Sides.Add(inside);level.Sides.Add(outside);
            level.Lines[wall].ClockwisePolygonSideIndex=0;level.Lines[wall].CounterclockwisePolygonSideIndex=1;
            var neighbor=level.Polygons[1]; neighbor.Type=PolygonType.Teleporter; neighbor.Permutation=0;
            Box(level,200,300,700,800);
            Check(level.Polygons[1]==neighbor && neighbor.Permutation==0,"Neighbor reference changed");
            Check(level.Sides.Count==6,"Split wall lost textures");
            Check(level.Sides.Count(side=>side.Primary.Texture.Equals((ShapeDescriptor)1))==3 && level.Sides.Count(side=>side.Primary.Texture.Equals((ShapeDescriptor)2))==3,"Textures not cloned on both sides");
            foreach(Line line in level.Lines.Where(l=>l.EndpointIndexes.All(i=>level.Endpoints[i].X==1000)))
                Check(line.ClockwisePolygonOwner>=0 && line.CounterclockwisePolygonOwner==1,"Broken outside connection");
            var loaded=new Level();loaded.Load(level.Save().Clone());Validate(loaded);
        });
        Test("inset is one undo action and center remains selected", () => {
            var level=new Level();Box(level,0,0,1000,1000);string before=CanonicalSnapshot(level);
            var editor=Editor(level);editor.StartBox(200,300);editor.UpdateBox(700,800);
            Check(level.Polygons.Count==1,"Preview modified map");
            editor.FinishBox(700,800);Check(editor.Selection.Polygon==0,"Wrong inset selected");
            string after=CanonicalSnapshot(level);editor.Undo();Check(CanonicalSnapshot(level)==before,"Inset undo failed");
            editor.Redo();Check(CanonicalSnapshot(level)==after,"Inset redo failed");Validate(level);
        });
        Test("inset limits and existing internal geometry reject atomically", () => {
            var level=new Level();Box(level,0,0,1000,1000);Box(level,1000,100,1200,200);Box(level,1000,400,1200,500);
            Reject(level,200,600,700,800);
            level=new Level();Box(level,0,0,1000,1000);level.NewLine(level.NewPoint(400,100),level.NewPoint(500,200));
            Reject(level,200,300,700,800);
            level=new Level();Box(level,0,0,1000,1000);level.Polygons[0].Type=PolygonType.Platform;
            Reject(level,200,300,700,800);
        });
        Test("insets can be nested repeatedly", () => {
            var level=new Level();Box(level,0,0,1000,1000);Box(level,200,300,700,800);Box(level,300,400,600,700);
            Check(level.Polygons.Count==9 && level.LastCreatedBoxPolygonIndex==0,"Second inset failed");
            var loaded=new Level();loaded.Load(level.Save().Clone());Validate(loaded);
        });
        Test("circle sides three through eight and reverse drags", () => {
            for(int sides=3;sides<=8;sides++) foreach(bool reverse in new[]{false,true}) {
                var level=new Level();var editor=Editor(level);editor.Tool=Tool.Circle;editor.CircleSides=sides;
                editor.ButtonPress((short)(reverse?1000:0),(short)(reverse?1000:0),EditorModifiers.None);
                editor.ButtonRelease((short)(reverse?0:1000),(short)(reverse?0:1000),EditorModifiers.None);
                Check(level.Polygons.Count==1 && level.Polygons[0].VertexCount==sides,"Wrong side count");Validate(level);
            }
        });
        Test("circle preview, cancel, ellipse and Shift constraint", () => {
            var level=new Level();var editor=Editor(level);editor.Tool=Tool.Circle;editor.CircleSides=8;
            editor.ButtonPress(0,0,EditorModifiers.None);editor.Motion(1000,500,EditorModifiers.None);
            Check(level.Endpoints.Count==0 && editor.DrawingCircle,"Preview changed map");
            Check(editor.CircleVertices.Max(p=>p.Y)==500,"Ellipse bounds wrong");
            editor.CancelCircle();Check(!editor.DrawingCircle && level.Polygons.Count==0,"Cancel failed");
            editor.ButtonPress(0,0,EditorModifiers.None);editor.ButtonRelease(1000,500,EditorModifiers.Shift);
            Check(level.Endpoints.Max(p=>p.Y)==1000 && level.Endpoints.Max(p=>p.X)==1000,"Shift did not constrain circle");Validate(level);
        });
        Test("adjacent circles reuse their diagonal shared edge", () => {
            var level=new Level();var editor=Editor(level);editor.Tool=Tool.Circle;editor.CircleSides=4;
            editor.StartCircle(0,0);editor.FinishCircle(1000,1000,false);
            editor.StartCircle(500,-500);editor.FinishCircle(1500,500,false);Validate(level);
            Check(level.Polygons.Count==2 && level.Endpoints.Count==6 && level.Lines.Count==7,"Shared diagonal duplicated");
        });
        Test("circle corner snapping and undo", () => {
            var level=new Level();level.NewPoint(503,0);var editor=Editor(level);editor.Tool=Tool.Circle;
            editor.CircleSides=4;string before=CanonicalSnapshot(level);
            editor.StartCircle(0,0);editor.FinishCircle(1000,1000,false);Validate(level);
            Check(level.Endpoints.Count==4 && level.Endpoints.Contains(P(503,0)),"Circle did not reuse snapped endpoint");
            string after=CanonicalSnapshot(level);editor.Undo();Check(CanonicalSnapshot(level)==before,"Circle undo failed");
            editor.Redo();Check(CanonicalSnapshot(level)==after,"Circle redo failed");
        });
        Test("circle overlap and crossing lines are rejected", () => {
            var level=new Level();Box(level,0,0,1000,1000);var editor=Editor(level);editor.Tool=Tool.Circle;string before=Snapshot(level);
            editor.StartCircle(800,800);editor.FinishCircle(1200,1200,false);
            Check(Snapshot(level)==before && editor.CircleMessage!=null,"Circle crossed outer walls");
            level=new Level();level.NewLine(level.NewPoint(-100,500),level.NewPoint(1100,500));editor=Editor(level);editor.Tool=Tool.Circle;before=Snapshot(level);
            editor.StartCircle(0,0);editor.FinishCircle(1000,1000,false);
            Check(Snapshot(level)==before && editor.CircleMessage!=null,"Circle crossed existing line");
        });
        Test("circle partial shared diagonal wall splitting", () => {
            var level=new Level();var editor=Editor(level);editor.Tool=Tool.Circle;editor.CircleSides=4;
            editor.StartCircle(0,0);editor.FinishCircle(1000,1000,false);
            var triangle=new System.Collections.Generic.List<Point>{P(900,400),P(600,100),P(1000,0)};
            Check(level.CreateCirclePolygon(triangle,out string reason),reason);Validate(level);
            Check(level.Polygons[0].VertexCount==6,"Shared diagonal did not split");
        });
        Test("circle rejects degenerate and too complex boundaries atomically", () => {
            var level=new Level();var editor=Editor(level);editor.Tool=Tool.Circle;
            editor.StartCircle(0,0);editor.FinishCircle(0,1000,false);Check(level.Polygons.Count==0,"Degenerate circle created");
            editor.CircleSides=4;editor.StartCircle(0,0);editor.UpdateCircle(1000,1000,false);
            var corners=new System.Collections.Generic.List<Point>(editor.CircleVertices);
            for(short x=550;x<=750;x+=50)level.NewPoint(x,(short)(x-500));
            string before=Snapshot(level);Check(!level.CreateCirclePolygon(corners,out string reason),"Polygon limit bypassed");
            Check(Snapshot(level)==before,"Failed circle mutated map");
            editor.CircleSides=100;Check(editor.CircleSides==8,"Sides not clamped");editor.CircleSides=1;Check(editor.CircleSides==3,"Minimum sides not clamped");
        });
        Test("Move translates selected vertex by drag displacement", () => {
            var level=new Level();Box(level,0,0,1000,1000);var editor=Editor(level);editor.Selection.Point=0;
            editor.Tool=Tool.Move;Check(editor.Selection.Point==0,"Switching to Move cleared selection");
            editor.ButtonPress(500,500,EditorModifiers.None);editor.Motion(520,530,EditorModifiers.None);editor.ButtonRelease(525,535,EditorModifiers.None);
            Check(level.Endpoints[0].Equals(P(25,35)),"Move jumped to cursor instead of using displacement");
            editor.Undo();Check(level.Endpoints[0].Equals(P(0,0)),"Vertex Move undo failed");
        });
        Test("Move handles selected group and Pan leaves geometry unchanged", () => {
            var level=new Level();Box(level,0,0,100,100);var editor=Editor(level);editor.Tool=Tool.Select;
            editor.ButtonPress(-20,-20,EditorModifiers.None);editor.ButtonRelease(120,120,EditorModifiers.None);
            editor.Tool=Tool.Move;Check(editor.Selection.Points.Count==4,"Move dropped group selection");
            editor.ButtonPress(500,500,EditorModifiers.None);editor.Motion(510,520,EditorModifiers.None);editor.ButtonRelease(510,520,EditorModifiers.None);
            Check(level.Endpoints.Contains(P(10,20)) && level.Endpoints.Contains(P(110,120)),"Group Move failed");
            string before=Snapshot(level);editor.Tool=Tool.Pan;
            editor.ButtonPress(0,0,EditorModifiers.None);editor.Motion(200,200,EditorModifiers.None);editor.ButtonRelease(200,200,EditorModifiers.None);
            Check(Snapshot(level)==before && editor.Selection.Points.Count==4,"Pan changed geometry or selection");
            editor.Tool=Tool.Move;editor.Undo();Check(level.Endpoints.Contains(P(0,0)) && editor.Selection.Points.Count==4,"Group Move undo failed");
        });
        Test("Move translates a polygon with its objects and annotations", () => {
            var level=new Level();Box(level,0,0,1000,1000);level.NewObject(P(200,300),0);
            level.Annotations.Add(new Annotation{X=400,Y=500,PolygonIndex=0,Text="keep"});
            var editor=Editor(level);editor.Selection.Polygon=0;editor.Tool=Tool.Move;
            editor.ButtonPress(1500,1500,EditorModifiers.None);editor.ButtonRelease(1600,1700,EditorModifiers.None);
            Check(level.Endpoints.Contains(P(100,200)) && level.Objects[0].X==300 && level.Objects[0].Y==500,"Polygon objects not moved together");
            Check(level.Annotations[0].X==500 && level.Annotations[0].Y==700,"Polygon notes not moved");Validate(level);
        });
        Test("Move objects across polygon boundaries without moving geometry", () => {
            var level=new Level();Box(level,0,0,1000,1000);Box(level,1000,0,2000,1000);level.NewObject(P(500,500),0);
            var editor=Editor(level);editor.Selection.Object=0;editor.Tool=Tool.Move;
            editor.ButtonPress(3000,3000,EditorModifiers.None);editor.ButtonRelease(4000,3000,EditorModifiers.None);
            Check(level.Objects[0].X==1500 && level.Objects[0].PolygonIndex==1 && level.Endpoints.Contains(P(0,0)),"Object Move failed");
            editor.Undo();Check(level.Objects[0].X==500 && level.Objects[0].PolygonIndex==0,"Object Move undo failed");
        });
        Test("Move selected line, annotations and no-selection drags", () => {
            var level=new Level();Box(level,0,0,1000,1000);var editor=Editor(level);editor.Selection.Line=0;editor.Tool=Tool.Move;
            editor.ButtonPress(3000,3000,EditorModifiers.None);editor.ButtonRelease(3020,3030,EditorModifiers.None);
            Check(level.Endpoints.Contains(P(20,30)) && level.Endpoints.Contains(P(1020,30)),"Line Move failed");
            level=new Level();Box(level,0,0,1000,1000);level.Annotations.Add(new Annotation{X=200,Y=300,PolygonIndex=0,Text="note"});
            editor=Editor(level);editor.Selection.Annotation=0;editor.Tool=Tool.Move;
            editor.ButtonPress(1500,1500,EditorModifiers.None);editor.ButtonRelease(1600,1600,EditorModifiers.None);
            Check(level.Annotations[0].X==300 && level.Annotations[0].Y==400,"Annotation Move failed");
            editor.Selection.Clear();string before=Snapshot(level);editor.Changed=false;
            editor.ButtonPress(0,0,EditorModifiers.None);editor.ButtonRelease(100,100,EditorModifiers.None);
            Check(Snapshot(level)==before && !editor.Changed,"Empty Move changed map");
        });
        Test("right drag moves selection with geometry tools and ends on release", () => {
            foreach (Tool tool in new[] {Tool.Select, Tool.Pan, Tool.Zoom, Tool.Box, Tool.Circle, Tool.Ring}) {
                var level=new Level();Box(level,0,0,1000,1000);var editor=Editor(level);editor.Tool=tool;editor.Selection.Point=0;
                editor.ButtonPress(500,500,EditorModifiers.RightClick);
                editor.Motion(520,530,EditorModifiers.RightClick);
                editor.ButtonRelease(525,535,EditorModifiers.RightClick);
                Check(editor.Tool==tool && editor.Selection.Point==0 && level.Endpoints[0].Equals(P(25,35)),"Right drag changed tool/selection or failed to move");
                editor.Undo();Check(level.Endpoints[0].Equals(P(0,0)),"Right drag did not undo as one action");
                Check(!editor.DrawingBox && !editor.DrawingCircle && !editor.DrawingRing,"Right drag started drawing");
            }
        });
        Test("right drag moves selected objects and empty drags do nothing", () => {
            var level=new Level();Box(level,0,0,1000,1000);level.NewObject(P(200,300),0);
            var editor=Editor(level);editor.Tool=Tool.Select;editor.Selection.Object=0;
            editor.ButtonPress(1500,1500,EditorModifiers.RightClick);editor.ButtonRelease(1600,1600,EditorModifiers.RightClick);
            Check(level.Objects[0].X==300 && level.Objects[0].Y==400 && editor.Selection.Object==0,"Right drag object failed");
            editor.Tool=Tool.Box;editor.Selection.Clear();string before=Snapshot(level);
            editor.ButtonPress(1500,1500,EditorModifiers.RightClick);editor.Motion(1700,1700,EditorModifiers.RightClick);editor.ButtonRelease(1700,1700,EditorModifiers.RightClick);
            Check(Snapshot(level)==before && !editor.DrawingBox,"Empty right drag created geometry");
            editor.ButtonPress(1500,1500,EditorModifiers.None);editor.ButtonRelease(1700,1700,EditorModifiers.None);
            Check(level.Polygons.Count==2,"Right release left move gesture active");
        });
        Test("hover matches point line polygon picking without selecting or editing", () => {
            var level=new Level();Box(level,0,0,1000,1000);var editor=Editor(level);editor.Tool=Tool.Select;
            string before=Snapshot(level);
            foreach (Point p in new[] {P(0,0),P(500,0),P(500,500),P(1500,1500)}) {
                editor.UpdateHover(p.X,p.Y);var hit=new Selection();hit.CopyFrom(editor.Hover);
                Check(editor.Selection.Point==-1 && editor.Selection.Line==-1 && editor.Selection.Polygon==-1,"Hover changed selection");
                editor.ButtonPress(p.X,p.Y,EditorModifiers.None);editor.ButtonRelease(p.X,p.Y,EditorModifiers.None);
                Check(editor.Selection.Point==hit.Point && editor.Selection.Line==hit.Line && editor.Selection.Polygon==hit.Polygon,"Hover disagrees with click");
                editor.Selection.Clear();
            }
            Check(Snapshot(level)==before && !editor.Changed,"Hover changed geometry");
            editor.UpdateHover(500,500);editor.Tool=Tool.Pan;Check(editor.Hover.Polygon==-1,"Tool switch left hover");
        });
        Test("Shift-click toggles polygons and polygon groups drag with contents", () => {
            var level=new Level();Box(level,0,0,1000,1000);Box(level,1000,0,2000,1000);
            level.NewObject(P(200,200),0);level.Annotations.Add(new Annotation{X=1200,Y=200,PolygonIndex=1,Text="note"});
            var editor=Editor(level);editor.Tool=Tool.Select;
            void Click(short x, EditorModifiers mods) {editor.ButtonPress(x,500,mods);editor.ButtonRelease(x,500,mods);}
            Click(500,EditorModifiers.None);Click(1500,EditorModifiers.Shift);
            Check(editor.Selection.Polygons.SetEquals(new short[]{0,1}),"Shift lost first polygon");
            Click(1500,EditorModifiers.Shift);Check(editor.Selection.Polygons.SetEquals(new short[]{0}),"Shift did not toggle off");
            Click(1500,EditorModifiers.Shift);
            editor.ButtonPress(500,500,EditorModifiers.None);editor.Motion(520,530,EditorModifiers.None);editor.ButtonRelease(520,530,EditorModifiers.None);
            Check(level.Endpoints.Contains(P(1020,30)) && !level.Endpoints.Contains(P(1040,60)),"Shared endpoints moved twice");
            Check(level.Objects[0].X==220 && level.Annotations[0].X==1220,"Group contents stayed behind");
            editor.Undo();Check(level.Endpoints.Contains(P(1000,0)) && level.Objects[0].X==200 && editor.Selection.Polygons.Count==2,"Polygon group undo failed");
            editor.ButtonPress(3000,3000,EditorModifiers.RightClick);editor.ButtonRelease(3040,3050,EditorModifiers.RightClick);
            Check(level.Objects[0].X==240 && level.Annotations[0].Y==250,"Right drag missed polygon contents");Validate(level);
        });
        Test("mixed polygon vertex selection nudges and deletes with undo", () => {
            var level=new Level();Box(level,0,0,1000,1000);Box(level,1000,0,2000,1000);var editor=Editor(level);editor.Tool=Tool.Select;
            editor.ButtonPress(500,500,EditorModifiers.None);editor.ButtonRelease(500,500,EditorModifiers.None);
            editor.ButtonPress(2000,0,EditorModifiers.Shift);editor.ButtonRelease(2000,0,EditorModifiers.Shift);
            Check(editor.Selection.Polygons.Count==1 && editor.Selection.Points.Count==1,"Mixed selection failed");
            editor.NudgeSelected(10,20);Check(level.Endpoints.Contains(P(10,20)) && level.Endpoints.Contains(P(2010,20)),"Mixed nudge failed");
            editor.Undo();editor.Selection.Clear();editor.Selection.Polygons.UnionWith(new short[]{0,1});
            editor.DeleteSelected();Check(level.Polygons.Count==0 && level.Lines.Count==7,"Polygon group delete damaged boundary lines");
            editor.Undo();Check(level.Polygons.Count==2 && editor.Selection.Polygons.Count==2,"Polygon deletion undo failed");Validate(level);
        });
        Test("hover and Shift polygon picks respect filtered polygons", () => {
            var level=new Level();Box(level,0,0,1000,1000);Level.Filter=p=>false;var editor=Editor(level);editor.Tool=Tool.Select;
            editor.UpdateHover(500,500);Check(editor.Hover.Polygon==-1,"Hovered hidden polygon");
            editor.ButtonPress(500,500,EditorModifiers.Shift);editor.ButtonRelease(500,500,EditorModifiers.Shift);
            Check(editor.Selection.Polygons.Count==0,"Selected hidden polygon");Level.Filter=p=>true;
        });
        long Area2(Level level)
        {
            long total=0;
            foreach(Polygon polygon in level.Polygons) {
                long sum=0;for(int i=0;i<polygon.VertexCount;i++) {
                    Point a=level.Endpoints[polygon.EndpointIndexes[i]],b=level.Endpoints[polygon.EndpointIndexes[(i+1)%polygon.VertexCount]];
                    sum+=(long)a.X*b.Y-(long)a.Y*b.X;
                }total+=Math.Abs(sum);
            }return total;
        }
        Test("inset box partitions triangles rotated boxes and irregular polygons exactly", () => {
            var outlines=new[] {
                new[]{P(0,0),P(2000,0),P(1000,2000)},
                new[]{P(0,1000),P(1000,0),P(2000,1000),P(1000,2000)},
                new[]{P(0,400),P(800,0),P(2000,300),P(1900,1500),P(1100,2100),P(200,1800)}
            };
            foreach(var outline in outlines) {
                var level=new Level();Check(level.CreateCirclePolygon(outline.ToList(),out string reason),reason);
                long area=Area2(level);var endpoints=level.Endpoints.ToArray();var boundaries=level.Lines.ToArray();
                Box(level,800,700,1200,1100);
                Check(Area2(level)==area,"Inset left overlap or a gap");
                Check(level.LastCreatedBoxPolygonIndex==0 && level.Polygons[0].VertexCount==4,"Inset index changed");
                Check(endpoints.All(p=>level.Endpoints.Contains(p)) && boundaries.All(l=>level.Lines.Contains(l)),"Outer boundaries replaced");
                var loaded=new Level();loaded.Load(level.Save().Clone());Validate(loaded);
            }
        });
        Test("insets inside 3 through 8 sided circles preserve geometry and undo", () => {
            for(int sides=3;sides<=8;sides++) {
                var level=new Level();var editor=Editor(level);editor.Tool=Tool.Circle;editor.CircleSides=sides;
                editor.StartCircle(0,0);editor.FinishCircle(4000,4000,false);
                long area=Area2(level);string before=CanonicalSnapshot(level);editor.Tool=Tool.Box;
                editor.StartBox(1700,1700);editor.FinishBox(2300,2300);
                Check(editor.BoxMessage==null && level.Polygons.Count>1,"Circle inset rejected: "+editor.BoxMessage);
                Check(Area2(level)==area && editor.Selection.Polygon==0,"Circle inset area/index changed");Validate(level);
                string after=CanonicalSnapshot(level);editor.Undo();Check(CanonicalSnapshot(level)==before,"Circle inset undo failed");
                editor.Redo();Check(CanonicalSnapshot(level)==after,"Circle inset redo failed");
            }
        });
        Test("nonrectangular inset preserves settings sides neighbors and object owners", () => {
            var level=new Level();var outline=new[]{P(0,1000),P(1000,0),P(2000,1000),P(1000,2000)};
            Check(level.CreateCirclePolygon(outline.ToList(),out string reason),reason);
            Check(level.CreateCirclePolygon(new[]{P(1000,0),P(0,1000),P(-1000,0)}.ToList(),out reason),reason);
            var neighbor=level.Polygons[1];neighbor.Type=PolygonType.Teleporter;neighbor.Permutation=0;
            level.Polygons[0].FloorHeight=123;level.Polygons[0].FloorOrigin=P(21,42);
            int wall=level.Lines.FindIndex(l=>l.ClockwisePolygonOwner==0 && l.CounterclockwisePolygonOwner==1);
            Check(wall>=0,"No shared wall");var side=new Side{LineIndex=(short)wall,PolygonIndex=0};side.Primary.Texture=(ShapeDescriptor)1;side.Primary.X=33;
            level.Sides.Add(side);level.Lines[wall].ClockwisePolygonSideIndex=0;
            foreach(Point point in new[]{P(1000,1000),P(1000,200),P(1800,1000),P(1000,1800),P(200,1000)}) {
                level.NewObject(point,0);level.Annotations.Add(new Annotation{X=point.X,Y=point.Y,PolygonIndex=0,Text="keep"});
            }
            long area=Area2(level);Box(level,800,800,1200,1200);
            Check(Area2(level)==area && level.Polygons[1]==neighbor && neighbor.Permutation==0,"Neighbor changed");
            Check(level.Sides.Count==1 && level.Sides[0].Primary.X==33 && level.Sides[0].Primary.Texture.Equals((ShapeDescriptor)1),"Perimeter texture lost");
            foreach(MapObject obj in level.Objects)Check(obj.PolygonIndex==level.GetEnclosingPolygon(P(obj.X,obj.Y)),"Object ownership wrong");
            foreach(Annotation note in level.Annotations)Check(note.PolygonIndex==level.GetEnclosingPolygon(P(note.X,note.Y)),"Note ownership wrong");
            for(int i=0;i<level.Polygons.Count;i++)if(i!=1)Check(level.Polygons[i].FloorHeight==123 && level.Polygons[i].FloorOrigin.Equals(P(21,42)),"Settings lost");
            Validate(level);
        });
        Test("nonrectangular inset rejects crossing walls and contained stray geometry atomically", () => {
            var level=new Level();Check(level.CreateCirclePolygon(new[]{P(0,1000),P(1000,0),P(2000,1000),P(1000,2000)}.ToList(),out string reason),reason);
            Reject(level,100,100,800,800);
            level.NewLine(level.NewPoint(500,500),level.NewPoint(700,600));Reject(level,800,800,1200,1200);
        });
        Test("inset handles concave outlines and extra collinear perimeter vertices", () => {
            var outlines=new[] {
                new[]{P(0,0),P(3000,0),P(3000,3000),P(2000,3000),P(2000,1000),P(1000,1000),P(1000,3000),P(0,3000)},
                new[]{P(0,1000),P(500,500),P(1000,0),P(2000,1000),P(1000,2000)}
            };
            for(int shape=0;shape<outlines.Length;shape++) {
                var level=new Level();var ids=outlines[shape].Select(p=>level.NewPoint(p.X,p.Y)).ToArray();
                var edges=new short[ids.Length];
                for(int i=0;i<ids.Length;i++)edges[i]=level.NewLine(ids[i],ids[(i+1)%ids.Length]);
                // Model an existing/imported outline, including a concave one
                // that the ordinary Fill tool intentionally does not generate.
                var polygon=new Polygon{VertexCount=(ushort)ids.Length,Concave=shape==0};
                level.Polygons.Add(polygon);
                for(int i=0;i<ids.Length;i++) {
                    polygon.EndpointIndexes[i]=ids[i];polygon.LineIndexes[i]=edges[(i+ids.Length-1)%ids.Length];
                    level.Lines[edges[i]].ClockwisePolygonOwner=0;level.EndpointPolygons[ids[i]].Add(polygon);
                }
                long area=Area2(level);
                if(shape==0) {
                    Reject(level,800,800,2200,2200);
                    Box(level,200,200,800,800);
                }else Box(level,800,800,1200,1200);
                Check(Area2(level)==area,"Outline area changed");Validate(level);
            }
        });
        Test("skewed quadrilateral inset merges into four convex surrounding regions", () => {
            var level=new Level();Check(level.CreateCirclePolygon(new[]{P(0,300),P(2200,0),P(2200,1800),P(0,2100)}.ToList(),out string reason),reason);
            long area=Area2(level);Box(level,400,600,1800,1400);
            Check(level.Polygons.Count==5,"Unnecessary triangles remain: "+level.Polygons.Count);
            Check(level.Polygons.All(p=>p.VertexCount==4),"Expected four surrounding quadrilaterals");
            Check(level.Endpoints.Count==8 && level.Lines.Count==12,"Removed diagonals left stray geometry");
            Check(Area2(level)==area,"Merging changed area");Validate(level);
        });
        Test("circle inset merges convex pieces without crossing the inset boundary", () => {
            var level=new Level();var editor=Editor(level);editor.Tool=Tool.Circle;editor.CircleSides=8;
            editor.StartCircle(0,0);editor.FinishCircle(4000,4000,false);long area=Area2(level);
            Box(level,1700,1700,2300,2300);
            Check(level.Polygons.Count<13 && level.Polygons.Skip(1).Any(p=>p.VertexCount>3),"Circle inset still only triangles");
            Check(level.Polygons[0].VertexCount==4 && Area2(level)==area,"Merge absorbed inset or changed area");
            Check(level.Lines.All(l=>l.ClockwisePolygonOwner!=-1 || l.CounterclockwisePolygonOwner!=-1),"Stray triangulation diagonal");Validate(level);
        });
        Test("edge and corner box insets create only nonempty surrounding regions", () => {
            foreach(var inset in new[]{new[]{0,200,600,800},new[]{200,0,800,600},new[]{0,0,600,600},new[]{400,400,1000,1000},new[]{0,200,1000,800},new[]{0,0,1000,600}}) {
                var level=new Level();Box(level,0,0,1000,1000);long area=Area2(level);
                Box(level,inset[0],inset[1],inset[2],inset[3]);
                Check(level.Polygons.Count<=4 && Area2(level)==area,"Edge inset has extra regions or changed area");
                Check(level.LastCreatedBoxPolygonIndex==0,"Edge inset lost original index");
                var loaded=new Level();loaded.Load(level.Save().Clone());Validate(loaded);
            }
        });
        Test("inset can touch one or four diagonal polygon edges", () => {
            foreach(var inset in new[]{new[]{500,500,1100,1100},new[]{500,500,1500,1500},new[]{500,600,1400,1500}}) {
                var level=new Level();Check(level.CreateCirclePolygon(new[]{P(0,1000),P(1000,0),P(2000,1000),P(1000,2000)}.ToList(),out string reason),reason);
                long area=Area2(level);Box(level,inset[0],inset[1],inset[2],inset[3]);
                Check(Area2(level)==area,"Diagonal contact changed area");Validate(level);
            }
        });
        Test("corner inset in triangle touches a horizontal outer edge", () => {
            var level=new Level();Check(level.CreateCirclePolygon(new[]{P(0,0),P(2000,0),P(1000,2000)}.ToList(),out string reason),reason);
            long area=Area2(level);Box(level,500,0,1500,600);Check(Area2(level)==area,"Triangle edge inset changed area");Validate(level);
        });
        Test("shared-wall edge inset preserves neighbor textures objects and undo", () => {
            var level=new Level();Box(level,0,0,1000,1000);Box(level,1000,0,2000,1000);
            int wall=level.Lines.FindIndex(l=>l.ClockwisePolygonOwner==0 && l.CounterclockwisePolygonOwner==1);
            var inside=new Side{LineIndex=(short)wall,PolygonIndex=0};inside.Primary.Texture=(ShapeDescriptor)1;inside.Primary.X=10;
            var outside=new Side{LineIndex=(short)wall,PolygonIndex=1};outside.Primary.Texture=(ShapeDescriptor)2;outside.Primary.X=20;
            level.Sides.Add(inside);level.Sides.Add(outside);level.Lines[wall].ClockwisePolygonSideIndex=0;level.Lines[wall].CounterclockwisePolygonSideIndex=1;
            level.NewObject(P(900,500),0);level.NewObject(P(400,500),0);
            level.Annotations.Add(new Annotation{X=400,Y=100,PolygonIndex=0,Text="keep"});
            string before=CanonicalSnapshot(level);var editor=Editor(level);editor.StartBox(600,200);editor.FinishBox(1000,800);
            Check(editor.BoxMessage==null && level.Polygons.Count==5,"Shared-wall inset failed: "+editor.BoxMessage);
            Check(level.Polygons[1].VertexCount==6 && level.Sides.Count==6,"Shared wall did not retain both texture sides");
            Check(level.Objects[0].PolygonIndex==0 && level.Objects[1].PolygonIndex!=0 && level.Annotations[0].PolygonIndex!=0,"Edge inset content ownership wrong");
            Validate(level);string after=CanonicalSnapshot(level);editor.Undo();Check(CanonicalSnapshot(level)==before,"Edge inset undo failed");
            editor.Redo();Check(CanonicalSnapshot(level)==after,"Edge inset redo failed");Validate(level);
        });
        Test("circle insets 3 through 8 sides preserve area selection preview and undo", () => {
            for(int sides=3;sides<=8;sides++) foreach(bool contact in new[]{false,true}) {
                var level=new Level();Box(level,0,0,4000,4000);long area=Area2(level);string before=CanonicalSnapshot(level);
                var editor=Editor(level);editor.Tool=Tool.Circle;editor.CircleSides=sides;
                editor.StartCircle((short)(contact?0:1200),(short)(contact?0:1200));
                editor.UpdateCircle((short)(contact?4000:2800),(short)(contact?4000:2800),false);
                Check(CanonicalSnapshot(level)==before,"Circle inset preview changed map");
                editor.FinishCircle((short)(contact?4000:2800),(short)(contact?4000:2800),false);
                Check(editor.CircleMessage==null && level.Polygons.Count>1,"Circle inset failed: "+editor.CircleMessage);
                Check(Area2(level)==area && editor.Selection.Polygon==0 && level.Polygons[0].VertexCount==sides,"Circle inset lost area/index/side count");Validate(level);
                string after=CanonicalSnapshot(level);editor.Undo();Check(CanonicalSnapshot(level)==before,"Circle inset undo failed");
                editor.Redo();Check(CanonicalSnapshot(level)==after,"Circle inset redo failed");
            }
        });
        Test("circle inset in irregular polygon preserves settings and object ownership", () => {
            var level=new Level();Check(level.CreateCirclePolygon(new[]{P(0,1000),P(2000,0),P(4000,1000),P(3500,3500),P(1000,4000)}.ToList(),out string reason),reason);
            long area=Area2(level);level.Polygons[0].FloorHeight=123;level.Polygons[0].FloorOrigin=P(21,42);
            foreach(Point p in new[]{P(2000,2000),P(1250,1250),P(2000,500),P(500,2000)}) {
                level.NewObject(p,0);level.Annotations.Add(new Annotation{X=p.X,Y=p.Y,PolygonIndex=0,Text="keep"});
            }
            var editor=Editor(level);editor.Tool=Tool.Circle;editor.CircleSides=8;
            editor.StartCircle(1200,1200);editor.FinishCircle(2800,2800,false);
            Check(editor.CircleMessage==null && Area2(level)==area,"Irregular circle inset failed: "+editor.CircleMessage);
            Check(level.Objects[0].PolygonIndex==0 && level.Objects[1].PolygonIndex!=0,"Bounding box used instead of circle for ownership");
            foreach(MapObject obj in level.Objects)Check(obj.PolygonIndex==level.GetEnclosingPolygon(P(obj.X,obj.Y)),"Circle object ownership wrong");
            foreach(Annotation note in level.Annotations)Check(note.PolygonIndex==level.GetEnclosingPolygon(P(note.X,note.Y)),"Circle note ownership wrong");
            foreach(Polygon polygon in level.Polygons)Check(polygon.FloorHeight==123 && polygon.FloorOrigin.Equals(P(21,42)),"Circle inset settings lost");
            Check(level.Polygons.Skip(1).Any(p=>p.VertexCount>3),"Circle regions not merged");Validate(level);
        });
        Test("circle inset along shared wall retains both wall textures and neighbor", () => {
            var level=new Level();Box(level,0,0,4000,4000);Box(level,4000,0,8000,4000);
            var neighbor=level.Polygons[1];neighbor.Type=PolygonType.Teleporter;neighbor.Permutation=0;
            int wall=level.Lines.FindIndex(l=>l.ClockwisePolygonOwner==0 && l.CounterclockwisePolygonOwner==1);
            var inside=new Side{LineIndex=(short)wall,PolygonIndex=0};inside.Primary.Texture=(ShapeDescriptor)1;
            var outside=new Side{LineIndex=(short)wall,PolygonIndex=1};outside.Primary.Texture=(ShapeDescriptor)2;
            level.Sides.Add(inside);level.Sides.Add(outside);level.Lines[wall].ClockwisePolygonSideIndex=0;level.Lines[wall].CounterclockwisePolygonSideIndex=1;
            var editor=Editor(level);editor.Tool=Tool.Circle;editor.CircleSides=4;editor.StartCircle(2000,1000);editor.FinishCircle(4000,3000,false);
            Check(editor.CircleMessage==null && level.Polygons[1]==neighbor && neighbor.Permutation==0,"Circle shared-wall inset failed: "+editor.CircleMessage);
            Check(neighbor.VertexCount==5 && level.Sides.Count==4,"Circle shared wall textures or splits lost");Validate(level);
        });
        Test("circle inset failure leaves map and undo unchanged", () => {
            var level=new Level();var editor=Editor(level);editor.StartBox(0,0);editor.FinishBox(4000,4000);
            editor.Tool=Tool.Circle;level.Polygons[0].Type=PolygonType.Platform;string before=Snapshot(level);
            editor.StartCircle(1200,1200);editor.FinishCircle(2800,2800,false);
            Check(Snapshot(level)==before && editor.CircleMessage!=null,"Special circle container accepted");
            editor.Undo();Check(level.Polygons.Count==0,"Rejected inset replaced undo");
            level=new Level();Box(level,0,0,4000,4000);level.NewLine(level.NewPoint(500,500),level.NewPoint(700,600));editor=Editor(level);editor.Tool=Tool.Circle;before=Snapshot(level);
            editor.StartCircle(1200,1200);editor.FinishCircle(2800,2800,false);Check(Snapshot(level)==before && editor.CircleMessage!=null,"Inset over stray geometry accepted");
        });
        Test("circles and boxes can be nested repeatedly inside circle polygons", () => {
            var level=new Level();var editor=Editor(level);editor.Tool=Tool.Circle;editor.CircleSides=8;
            editor.StartCircle(0,0);editor.FinishCircle(6000,5000,false);long area=Area2(level);
            editor.StartCircle(1800,1400);editor.FinishCircle(4200,3600,false);
            Check(editor.CircleMessage==null && editor.Selection.Polygon==0 && Area2(level)==area,"Circle inside ellipse failed: "+editor.CircleMessage);Validate(level);
            editor.CircleSides=5;editor.StartCircle(2600,2100);editor.FinishCircle(3400,2900,true);
            Check(editor.CircleMessage==null && editor.Selection.Polygon==0 && Area2(level)==area,"Repeated circle inset failed: "+editor.CircleMessage);Validate(level);
            Box(level,2900,2400,3100,2600);Check(Area2(level)==area && level.LastCreatedBoxPolygonIndex==0,"Box inside nested circle failed");
            var loaded=new Level();loaded.Load(level.Save().Clone());Validate(loaded);
        });
        Test("multiple history steps undo redo and jump restore geometry and selections", () => {
            var level=new Level();var editor=Editor(level);var states=new System.Collections.Generic.List<string>{CanonicalSnapshot(level)};
            for(int i=0;i<3;i++) {editor.StartBox((short)(i*200),0);editor.FinishBox((short)(i*200+100),100);states.Add(CanonicalSnapshot(level));}
            Check(editor.HistoryLabels.Count==4 && editor.HistoryPosition==3 && editor.HistoryLabels[3]=="Draw box","History rows missing");
            for(int i=2;i>=0;i--) {editor.Undo();Check(CanonicalSnapshot(level)==states[i],"Multi-step undo wrong");}
            Check(!editor.CanUndo && editor.CanRedo,"Undo boundary wrong");editor.Undo();Check(CanonicalSnapshot(level)==states[0],"Empty undo changed map");
            for(int i=1;i<=3;i++) {editor.Redo();Check(CanonicalSnapshot(level)==states[i],"Multi-step redo wrong");}
            Check(editor.Selection.Polygon==2 && !editor.CanRedo,"Redo lost selection");
            editor.GoToHistory(1);Check(CanonicalSnapshot(level)==states[1] && editor.Selection.Polygon==0,"Backward history jump wrong");
            editor.GoToHistory(3);Check(CanonicalSnapshot(level)==states[3] && editor.Selection.Polygon==2,"Forward history jump wrong");
            editor.GoToHistory(99);Check(editor.HistoryPosition==3,"Invalid history index accepted");
        });
        Test("new edits discard redo while failed and empty edits preserve it", () => {
            var level=new Level();var editor=Editor(level);editor.StartBox(0,0);editor.FinishBox(1000,1000);
            editor.StartBox(1000,0);editor.FinishBox(2000,1000);editor.Undo();Check(editor.CanRedo,"Missing redo");
            editor.StartBox(-100,-100);editor.FinishBox(1100,1100);Check(editor.CanRedo && editor.HistoryLabels.Count==3,"Failed box replaced redo");
            editor.Tool=Tool.Move;editor.Selection.Clear();editor.ButtonPress(0,0,EditorModifiers.None);editor.ButtonRelease(100,100,EditorModifiers.None);
            Check(editor.CanRedo && editor.HistoryLabels.Count==3,"Empty move replaced redo");
            editor.SetUndo("No-op");editor.CommitHistory();Check(editor.CanRedo,"No-op snapshot replaced redo");
            editor.Tool=Tool.Box;editor.StartBox(0,1000);editor.FinishBox(1000,2000);
            Check(!editor.CanRedo && editor.HistoryLabels.Count==3,"New edit failed to truncate future");Validate(level);
        });
        Test("a long drag is one history action and restores group selection", () => {
            var level=new Level();Box(level,0,0,1000,1000);var editor=Editor(level);editor.Tool=Tool.Select;
            editor.Selection.Polygons.Add(0);editor.ButtonPress(500,500,EditorModifiers.RightClick);
            for(short i=1;i<=20;i++) {editor.Motion((short)(500+i*10),(short)(500+i*5),EditorModifiers.RightClick);Check(editor.HistoryLabels.Count==1,"Drag committed partial action");}
            editor.ButtonRelease(700,600,EditorModifiers.RightClick);Check(editor.HistoryLabels.Count==2 && editor.HistoryLabels[1]=="Move selection","Drag not one step");
            editor.Selection.Polygons.Clear();editor.Undo();Check(level.Endpoints.Contains(P(0,0)) && editor.Selection.Polygons.Contains(0),"History group selection not restored");
            editor.Redo();Check(level.Endpoints.Contains(P(200,100)) && editor.Selection.Polygons.Contains(0),"Drag redo wrong");
        });
        Test("history retains 100 actions and clearing starts a new map history", () => {
            var level=new Level();Box(level,0,0,1000,1000);var editor=Editor(level);editor.Tool=Tool.Select;editor.Selection.Point=0;
            for(int i=0;i<105;i++)editor.NudgeSelected(1,0);
            Check(editor.HistoryLabels.Count==101 && editor.HistoryPosition==100 && editor.HistoryLabels[0]=="Earlier state","History limit wrong");
            for(int i=0;i<100;i++)editor.Undo();Check(level.Endpoints[0].X==5 && !editor.CanUndo,"Retained initial state wrong");
            editor.ClearUndo();Check(!editor.CanUndo && !editor.CanRedo && editor.HistoryLabels.Count==1,"Clear history failed");
        });
        Test("failed fill and canceled line do not add history entries", () => {
            var level=new Level();var editor=Editor(level);editor.StartBox(0,0);editor.FinishBox(1000,1000);editor.CommitHistory();
            editor.Tool=Tool.Fill;editor.ButtonPress(500,500,EditorModifiers.None);editor.ButtonRelease(500,500,EditorModifiers.None);
            Check(editor.HistoryLabels.Count==2,"Failed fill added action");
            editor.Tool=Tool.Line;editor.LineSplitAll=true;editor.ButtonPress(2000,2000,EditorModifiers.None);editor.Undo();
            Check(level.Polygons.Count==0 && level.Endpoints.Count==0 && editor.HistoryLabels.Count==2,"Canceled temporary line was recorded");
        });
        Test("Rotate keeps polygon center carries contents and records one undo step", () => {
            var level=new Level();Box(level,0,0,1000,500);level.NewObject(P(750,250),0);level.Objects[0].Type=ObjectType.Player;level.Objects[0].Facing=0;
            level.Annotations.Add(new Annotation{X=750,Y=250,PolygonIndex=0,Text="rotate"});
            var editor=Editor(level);editor.Selection.Polygon=0;editor.Tool=Tool.Rotate;
            Check(editor.Selection.Polygon==0,"Choosing Rotate cleared selection");string before=CanonicalSnapshot(level);
            editor.ButtonPress(1000,250,EditorModifiers.None);editor.Motion(500,750,EditorModifiers.None);
            Check(editor.RotatingSelection && editor.RotationCenterX==500 && editor.RotationCenterY==250,"Wrong rotation pivot");
            Check(editor.HistoryLabels.Count==1,"Rotation drag committed partial history");editor.ButtonRelease(500,750,EditorModifiers.None);
            Check(!editor.RotatingSelection && level.Endpoints.Contains(P(750,-250)) && level.Endpoints.Contains(P(250,750)),"Polygon rotation wrong");
            Check(level.Objects[0].X==500 && level.Objects[0].Y==500 && Math.Abs(level.Objects[0].Facing-90)<1,"Object position or facing not rotated");
            Check(level.Annotations[0].X==500 && level.Annotations[0].Y==500 && level.Objects[0].PolygonIndex==0,"Polygon contents lost");
            Check(editor.HistoryLabels.Count==2 && editor.HistoryLabels[1]=="Rotate selection","Rotation not one history action");
            string after=CanonicalSnapshot(level);editor.Undo();Check(CanonicalSnapshot(level)==before && editor.Selection.Polygon==0,"Rotation undo failed");
            editor.Redo();Check(CanonicalSnapshot(level)==after,"Rotation redo failed");Validate(level);
        });
        Test("Rotate polygon groups share one pivot and do not accumulate rounding", () => {
            var level=new Level();Box(level,0,0,1000,1000);Box(level,1000,0,2000,1000);var original=level.Endpoints.ToArray();
            var editor=Editor(level);editor.Selection.Polygons.UnionWith(new short[]{0,1});editor.Tool=Tool.Rotate;
            editor.ButtonPress(2000,500,EditorModifiers.None);
            for(int degrees=1;degrees<=179;degrees++)editor.Motion((short)Math.Round(1000+1000*Math.Cos(degrees*Math.PI/180)),(short)Math.Round(500+1000*Math.Sin(degrees*Math.PI/180)),EditorModifiers.None);
            editor.ButtonRelease(0,500,EditorModifiers.None);
            for(int i=0;i<original.Length;i++)Check(level.Endpoints[i].Equals(P(2000-original[i].X,1000-original[i].Y)),"Shared endpoint rotated twice or accumulated rounding");
            Validate(level);Check(editor.HistoryLabels.Count==2,"Long group rotation created extra history");
        });
        Test("Rotate line and mixed vertices with Shift snap and no-op gestures", () => {
            var level=new Level();short a=level.NewPoint(0,0),b=level.NewPoint(1000,0);short line=level.NewLine(a,b);
            var editor=Editor(level);editor.Selection.Line=line;editor.Tool=Tool.Rotate;editor.ButtonPress(1000,0,EditorModifiers.None);
            editor.ButtonRelease(501,500,EditorModifiers.Shift);Check(level.Endpoints[a].Equals(P(500,-500)) && level.Endpoints[b].Equals(P(500,500)),"Shift line rotation wrong");
            editor.Selection.Clear();string before=CanonicalSnapshot(level);int count=editor.HistoryLabels.Count;
            editor.ButtonPress(0,0,EditorModifiers.None);editor.ButtonRelease(100,100,EditorModifiers.None);Check(CanonicalSnapshot(level)==before && editor.HistoryLabels.Count==count,"Empty rotation changed map");
            editor.Selection.Point=a;editor.ButtonPress(0,0,EditorModifiers.None);editor.ButtonRelease(100,100,EditorModifiers.None);Check(CanonicalSnapshot(level)==before,"Single vertex rotated away from itself");
            editor.Selection.Clear();editor.Selection.Points.UnionWith(new[]{a,b});editor.ButtonPress(1000,0,EditorModifiers.None);editor.ButtonRelease(0,0,EditorModifiers.None);
            Check(level.Endpoints[a].Equals(P(500,500)) && level.Endpoints[b].Equals(P(500,-500)),"Multiple vertices rotation wrong");
        });
        Test("Rotate rejects coordinate overflow and right drag still moves", () => {
            var level=new Level();Box(level,32500,-1000,32700,1000);var editor=Editor(level);editor.Selection.Polygon=0;editor.Tool=Tool.Rotate;
            string before=CanonicalSnapshot(level);editor.ButtonPress(32700,0,EditorModifiers.None);editor.ButtonRelease(32600,1000,EditorModifiers.None);
            Check(CanonicalSnapshot(level)==before && editor.HistoryLabels.Count==1,"Overflow rotation distorted or changed geometry");
            editor.ButtonPress(32600,0,EditorModifiers.RightClick);editor.ButtonRelease(32500,0,EditorModifiers.RightClick);
            Check(level.Endpoints.Contains(P(32400,-1000)) && editor.HistoryLabels[1]=="Move selection","Rotate replaced right-drag Move");
        });
        Test("Scale polygon around center carries contents and undoes in one step", () => {
            var level=new Level();Box(level,0,0,1000,500);level.NewObject(P(750,250),0);level.Objects[0].Type=ObjectType.Player;level.Objects[0].Facing=90;
            level.Annotations.Add(new Annotation{X=750,Y=250,PolygonIndex=0,Text="scale"});var editor=Editor(level);editor.Selection.Polygon=0;editor.Tool=Tool.Scale;
            Check(editor.Selection.Polygon==0 && editor.ShowRotationCenter(),"Scale lost selection or pivot");string before=CanonicalSnapshot(level);
            editor.ButtonPress(1000,250,EditorModifiers.Shift);editor.Motion(1500,250,EditorModifiers.Shift);
            Check(editor.ScalingSelection && !editor.RotatingSelection && editor.HistoryLabels.Count==1,"Scale drag state wrong");editor.ButtonRelease(1500,250,EditorModifiers.Shift);
            Check(level.Endpoints.Contains(P(-500,-250)) && level.Endpoints.Contains(P(1500,750)),"Scale changed center or aspect ratio");
            Check(level.Objects[0].X==1000 && level.Objects[0].Y==250 && Math.Abs(level.Objects[0].Facing-90)<1 && level.Annotations[0].X==1000,"Scale lost contents or changed facing");
            Check(editor.HistoryLabels.Count==2 && editor.HistoryLabels[1]=="Scale selection","Scale not one undo step");string after=CanonicalSnapshot(level);
            editor.Undo();Check(CanonicalSnapshot(level)==before && editor.Selection.Polygon==0,"Scale undo failed");editor.Redo();Check(CanonicalSnapshot(level)==after,"Scale redo failed");Validate(level);
        });
        Test("Scale groups preserve shared vertices and recompute from original positions", () => {
            var level=new Level();Box(level,0,0,1000,1000);Box(level,1000,0,2000,1000);var original=level.Endpoints.ToArray();var editor=Editor(level);
            editor.Selection.Polygons.UnionWith(new short[]{0,1});editor.Tool=Tool.Scale;editor.ButtonPress(2000,500,EditorModifiers.Shift);
            for(short x=1990;x>=1510;x-=10)editor.Motion(x,500,EditorModifiers.Shift);editor.ButtonRelease(1500,500,EditorModifiers.Shift);
            for(int i=0;i<original.Length;i++)Check(level.Endpoints[i].Equals(P(500+original[i].X/2,250+original[i].Y/2)),"Shared endpoint scaled twice or rounding accumulated");
            Check(editor.HistoryLabels.Count==2,"Scale group created extra history");Validate(level);
        });
        Test("Scale lines and selected vertices snap reject collapse and handle no selection", () => {
            var level=new Level();short a=level.NewPoint(0,0),b=level.NewPoint(1000,0);short line=level.NewLine(a,b);var editor=Editor(level);editor.Selection.Line=line;editor.Tool=Tool.Scale;
            editor.ButtonPress(1000,0,EditorModifiers.None);editor.ButtonRelease(1135,0,EditorModifiers.Shift);
            Check(level.Endpoints[a].Equals(P(-150,0)) && level.Endpoints[b].Equals(P(1150,0)),"Scale 10 percent snapping failed");
            editor.Selection.Clear();string before=CanonicalSnapshot(level);int count=editor.HistoryLabels.Count;
            editor.ButtonPress(1000,0,EditorModifiers.None);editor.ButtonRelease(1500,0,EditorModifiers.None);Check(CanonicalSnapshot(level)==before && editor.HistoryLabels.Count==count,"Empty scale changed map");
            editor.Selection.Point=a;editor.ButtonPress(1000,0,EditorModifiers.None);editor.ButtonRelease(1500,0,EditorModifiers.None);Check(CanonicalSnapshot(level)==before,"Single vertex scaled away from center");
            level=new Level();a=level.NewPoint(0,0);b=level.NewPoint(2,0);level.NewLine(a,b);editor=Editor(level);editor.Selection.Points.UnionWith(new[]{a,b});editor.Tool=Tool.Scale;before=CanonicalSnapshot(level);
            editor.ButtonPress(11,0,EditorModifiers.None);editor.ButtonRelease(2,0,EditorModifiers.None);Check(CanonicalSnapshot(level)==before && editor.HistoryLabels.Count==1,"Tiny scale collapsed vertices");
        });
        Test("Scale overflow is atomic undo cancels active drag and right drag moves", () => {
            var level=new Level();Box(level,30000,0,32000,1000);var editor=Editor(level);editor.Selection.Polygon=0;editor.Tool=Tool.Scale;string before=CanonicalSnapshot(level);
            editor.ButtonPress(32000,500,EditorModifiers.None);editor.ButtonRelease(31000,10000,EditorModifiers.Shift);Check(CanonicalSnapshot(level)==before && editor.HistoryLabels.Count==1,"Overflow scale changed geometry");
            editor.ButtonPress(32000,500,EditorModifiers.None);editor.Motion(31500,500,EditorModifiers.None);editor.Undo();Check(!editor.ScalingSelection && CanonicalSnapshot(level)==before,"Undo did not cancel scale drag");
            editor.ButtonPress(31000,500,EditorModifiers.RightClick);editor.ButtonRelease(30900,500,EditorModifiers.RightClick);Check(level.Endpoints.Contains(P(29900,0)) && editor.HistoryLabels[1]=="Move selection","Scale replaced right-drag move");
        });
        Test("Pave covers existing opaque textures while cleanup preserves them", () => {
            var level=new Level();Box(level,0,0,1000,1000);Box(level,1000,0,2000,1000);
            level.Polygons[1].FloorHeight=100;level.Polygons[1].CeilingHeight-=100;level.Pave();
            foreach(var polygon in level.Polygons) {polygon.FloorTexture=(ShapeDescriptor)123;polygon.CeilingTexture=(ShapeDescriptor)124;polygon.FloorTransferMode=3;}
            foreach(var side in level.Sides) {side.Primary.Texture=(ShapeDescriptor)125;side.PrimaryLightsourceIndex=2;if(side.Type==SideType.Split)side.Secondary.Texture=(ShapeDescriptor)126;}
            level.Pave(false);Check(level.Polygons.All(p=>p.FloorTexture.Equals((ShapeDescriptor)123) && p.CeilingTexture.Equals((ShapeDescriptor)124)) && level.Sides.All(s=>s.Primary.Texture.Equals((ShapeDescriptor)125)),"Cleanup overwrote existing textures");
            var editor=Editor(level);string before=CanonicalSnapshot(level);editor.SetUndo("Pave map");level.Pave(true);
            foreach(var polygon in level.Polygons) Check(polygon.FloorTexture.Collection==level.Environment+17 && polygon.FloorTexture.Bitmap==6 && polygon.CeilingTexture.Bitmap==7 && polygon.FloorTransferMode==3,"Pave skipped existing floor/ceiling or reset transfer mode");
            foreach(var side in level.Sides) Check(side.Primary.Texture.Collection==level.Environment+17 && side.Primary.Texture.Bitmap==5 && side.PrimaryLightsourceIndex==2 && (side.Type!=SideType.Split || side.Secondary.Texture.Bitmap==5),"Pave skipped existing wall or reset lighting");
            editor.Undo();Check(CanonicalSnapshot(level)==before,"Pave undo did not restore previous textures");
        });
        Test("Pave builds complete descriptors without retaining an invalid old palette", () => {
            var level=new Level();Box(level,0,0,1000,1000);level.Pave();
            var old=new ShapeDescriptor((ushort)(7<<13 | 20<<8 | 123));
            foreach(var side in level.Sides)side.Primary.Texture=old;
            level.Polygons[0].FloorTexture=old;level.Polygons[0].CeilingTexture=old;
            level.Pave(true);Check(level.Sides.All(s=>s.Primary.Texture.CLUT==0) && level.Polygons[0].FloorTexture.CLUT==0 && level.Polygons[0].CeilingTexture.CLUT==0,"Pave retained an unsupported palette");
            var wall=new ShapeDescriptor((ushort)(19<<8 | 1));var floor=new ShapeDescriptor((ushort)(19<<8 | 2));var ceiling=new ShapeDescriptor((ushort)(19<<8 | 3));
            level.Pave(true,wall,floor,ceiling);
            Check(level.Sides.All(s=>s.Primary.Texture.Equals(wall)) && level.Polygons[0].FloorTexture.Equals(floor) && level.Polygons[0].CeilingTexture.Equals(ceiling),"Pave ignored available Shapes-file descriptors");
        });
        Test("Default Pave fills missing textures without replacing existing surfaces", () => {
            var level=new Level();Box(level,0,0,1000,1000);Box(level,1000,0,2000,1000);
            level.Polygons[1].FloorHeight=100;level.Polygons[1].CeilingHeight-=100;level.Pave();
            foreach(var side in level.Sides){side.Primary.Texture=(ShapeDescriptor)123;side.PrimaryLightsourceIndex=2;side.Primary.X=37;}
            var split=level.Sides.First(s=>s.Type==SideType.Split);split.Secondary.Texture=ShapeDescriptor.Empty;
            level.Polygons[0].FloorTexture=(ShapeDescriptor)124;level.Polygons[0].CeilingTexture=ShapeDescriptor.Empty;
            level.Pave();
            Check(level.Sides.All(s=>s.Primary.Texture.Equals((ShapeDescriptor)123) && s.PrimaryLightsourceIndex==2 && s.Primary.X==37),"Pave replaced textured walls or settings");
            Check(level.Polygons[0].FloorTexture.Equals((ShapeDescriptor)124),"Pave replaced textured floor");
            Check(!split.Secondary.Texture.IsEmpty() && !level.Polygons[0].CeilingTexture.IsEmpty(),"Pave skipped missing secondary wall or ceiling");
        });
        Test("Pave defaults skip uniform-color placeholder images", () => {
            var image=new Image(2,2);for(int i=2;i<image.Data.Length;i+=3)image.Data[i]=255;
            Check(!PaveTextureSelection.HasTextureDetail(image),"Solid blue placeholder accepted");image.Data[3]=40;
            Check(PaveTextureSelection.HasTextureDetail(image),"Detailed texture rejected");
        });
        if(args.Length==2) Test("ApX hex map paving replaces blue placeholders with detailed textures", () => {
            var shapes=new ShapesFile();using(var reader=new BinaryReaderBE(System.IO.File.OpenRead(args[1])))shapes.Load(reader);
            var map=new MapFile();map.Load(args[0]);var level=new Level();level.Load(map.Directory[0]);
            Check(!PaveTextureSelection.HasTextureDetail(shapes.GetShape(new ShapeDescriptor((ushort)(17<<8|5)))),"ApX reproduction no longer contains stock blue placeholder");
            Check(PaveTextureSelection.TryChoose(shapes,level.Environment+17,out ShapeDescriptor wall,out ShapeDescriptor floor,out ShapeDescriptor ceiling),"No ApX defaults selected");
            Check(wall.Collection==17 && wall.Bitmap==0 && floor.Bitmap==1 && ceiling.Bitmap==2,"Unexpected ApX defaults");
            var endpoints=level.Endpoints.ToArray();int objects=level.Objects.Count,polygons=level.Polygons.Count;
            level.Pave(true,wall,floor,ceiling);var loaded=new Level();loaded.Load(level.Save().Clone());
            Check(loaded.Endpoints.SequenceEqual(endpoints) && loaded.Objects.Count==objects && loaded.Polygons.Count==polygons,"Paving changed map geometry or objects");
            foreach(var texture in loaded.Polygons.SelectMany(p=>new[]{p.FloorTexture,p.CeilingTexture}).Concat(loaded.Sides.Select(s=>s.Primary.Texture)).Where(t=>!t.IsEmpty()))
                Check(PaveTextureSelection.HasTextureDetail(shapes.GetShape(texture)),"Paved surface still has a blank placeholder");
        });
        Test("circle tessellation creates connected convex patches without gaps or overlaps", () => {
            foreach (int sides in new[] {3,4,6,8})
                foreach (int size in new[] {17,1000})
                    foreach (int direction in new[] {-1,1}) {
                        Check(CircleTessellation.TryBuild(P(0,0),P(direction*size*3,direction*size*3),sides,size,out var tiles,out string reason),reason);
                        long Cross(Point a,Point b,Point c) => (long)(b.X-a.X)*(c.Y-a.Y)-(long)(b.Y-a.Y)*(c.X-a.X);
                        bool Separate(System.Collections.Generic.List<Point> a,System.Collections.Generic.List<Point> b) =>
                            Enumerable.Range(0,a.Count).Any(i=>b.All(p=>Cross(a[i],a[(i+1)%a.Count],p)<=0));
                        for(int i=0;i<tiles.Count;i++)for(int j=i+1;j<tiles.Count;j++)
                            Check(Separate(tiles[i],tiles[j]) || Separate(tiles[j],tiles[i]),"Tiles overlap");
                        var level=new Level();
                        Check(level.PlanCircleTessellation(tiles,out var plan,out var selected,out int added,out reason),reason);
                        Check(added==tiles.Count && selected.Distinct().Count()==added,"Missing tile");
                        Validate(plan);
                        Check(plan.Endpoints.Count-plan.Lines.Count+plan.Polygons.Count==1,"Pattern has an enclosed gap");
                        var seen=new System.Collections.Generic.HashSet<short>{0};bool changed=true;
                        while(changed) {changed=false;foreach(var line in plan.Lines)
                            if(line.ClockwisePolygonOwner!=-1 && line.CounterclockwisePolygonOwner!=-1 &&
                               (seen.Contains(line.ClockwisePolygonOwner)||seen.Contains(line.CounterclockwisePolygonOwner))) {
                                changed|=seen.Add(line.ClockwisePolygonOwner);changed|=seen.Add(line.CounterclockwisePolygonOwner);
                            }
                        }
                        Check(seen.Count==plan.Polygons.Count,"Patch is disconnected");
                        if(sides==8) Check(plan.Polygons.Any(p=>p.VertexCount==4) && plan.Polygons.Any(p=>p.VertexCount==8),"Octagon fillers missing");
                        var loaded=new Level();loaded.Load(plan.Save().Clone());Validate(loaded);
                        Check(Area2(loaded)==Area2(plan),"Saving changed tile area");
                    }
        });
        Test("Penrose rhombs share convex boundaries without overlaps or holes", () => {
            foreach(int size in new[]{16,17,1000})foreach(Point direction in new[]{P(-4,-4),P(-4,4),P(4,-4),P(4,4),P(0,0),P(4,0),P(0,4)}) {
                Check(PenroseTessellation.TryBuild(P(-2000,-2000),P(-2000+direction.X*size,-2000+direction.Y*size),size,out var tiles,out var steps,out string reason),reason);
                Check(tiles.Count>0 && tiles.All(t=>t.Count==4) && steps.Count==tiles.Count,"Missing Penrose rhombs or stair steps");
                long Cross(Point a,Point b,Point c)=>(long)(b.X-a.X)*(c.Y-a.Y)-(long)(b.Y-a.Y)*(c.X-a.X);
                bool Separate(System.Collections.Generic.List<Point> a,System.Collections.Generic.List<Point> b)=>Enumerable.Range(0,4).Any(i=>b.All(p=>Cross(a[i],a[(i+1)%4],p)<=0));
                for(int i=0;i<tiles.Count;i++)for(int j=i+1;j<tiles.Count;j++)Check(Separate(tiles[i],tiles[j]) || Separate(tiles[j],tiles[i]),"Penrose tiles overlap");
                var level=new Level();Check(level.PlanCircleTessellation(tiles,out var plan,out _,out int added,out reason),reason);Validate(plan);
                Check(added==tiles.Count && plan.Endpoints.Count-plan.Lines.Count+plan.Polygons.Count==1,"Penrose patch has gaps or disconnected geometry");
                var seen=new System.Collections.Generic.HashSet<short>{0};bool changed=true;
                while(changed) { changed=false;foreach(var l in plan.Lines)if(l.ClockwisePolygonOwner!=-1 && l.CounterclockwisePolygonOwner!=-1 && (seen.Contains(l.ClockwisePolygonOwner) || seen.Contains(l.CounterclockwisePolygonOwner))) { changed|=seen.Add(l.ClockwisePolygonOwner);changed|=seen.Add(l.CounterclockwisePolygonOwner); } }
                Check(seen.Count==tiles.Count,"Penrose tiles only touch at corners or are disconnected");
                var loaded=new Level();loaded.Load(plan.Save().Clone());Validate(loaded);
                Check(Area2(plan)==Area2(loaded),"Penrose save/load area changed");
                if(size==1000 && direction.X!=0 && direction.Y!=0) {
                    var areas=tiles.Select(t=>Math.Abs(Cross(t[0],t[1],t[2])+Cross(t[0],t[2],t[3]))/(double)(size*size)).ToArray();
                    Check(areas.Any(v=>Math.Abs(v-2*Math.Sin(Math.PI/5))<0.01) && areas.Any(v=>Math.Abs(v-2*Math.Sin(2*Math.PI/5))<0.01),"Penrose thin/thick rhomb areas are wrong");
                }
            }
        });
        Test("Penrose preview filled outlines stairs and undo reuse one stable patch", () => {
            foreach(bool filled in new[]{false,true}) {
                var level=new Level();var editor=Editor(level);editor.Tool=Tool.Circle;editor.CircleTessellationEnabled=true;
                editor.CirclePattern=TessellationPattern.PenroseRhombs;editor.CircleTileSize=256;editor.CircleFilled=filled;
                editor.CircleStairs=filled;editor.CircleStairStart=0;editor.CircleStairStep=32;editor.CircleStairCeiling=2048;editor.CircleCeilingFollows=true;
                string before=CanonicalSnapshot(level);editor.StartCircle(0,0);editor.UpdateCircle(768,768,false);
                Check(editor.CircleMessage==null && editor.CircleTiles.Count>1 && CanonicalSnapshot(level)==before,"Penrose preview failed or changed map: "+editor.CircleMessage);
                editor.FinishCircle(768,768,false);Check(editor.CircleMessage==null,"Penrose commit failed: "+editor.CircleMessage);Validate(level);
                Check(filled?level.Polygons.Count>1 && level.Polygons.Select(p=>p.FloorHeight).Distinct().Count()>1:level.Polygons.Count==0 && level.Lines.Count>4,"Filled/stairs/outline behavior failed");
                string after=CanonicalSnapshot(level);editor.Undo();Check(CanonicalSnapshot(level)==before,"Penrose undo failed");editor.Redo();Check(CanonicalSnapshot(level)==after,"Penrose redo failed");
                int history=editor.HistoryLabels.Count;editor.StartCircle(0,0);editor.FinishCircle(768,768,false);
                Check(editor.CircleMessage==null && CanonicalSnapshot(level)==after && editor.HistoryLabels.Count==history,"Penrose repeat duplicates tiles/history");
                editor.StartCircle(0,0);editor.CirclePattern=TessellationPattern.Regular;Check(!editor.DrawingCircle,"Pattern change left stale preview");
            }
            var empty=new Level();var e=Editor(empty);e.Tool=Tool.Circle;e.CircleTessellationEnabled=true;e.CirclePattern=TessellationPattern.PenroseRhombs;e.CircleTileSize=256;
            e.StartCircle(0,0);e.FinishCircle(512,512,false);string original=CanonicalSnapshot(empty);
            e.StartCircle(10,10);e.FinishCircle(522,522,false);Check(e.CircleMessage!=null && CanonicalSnapshot(empty)==original,"Overlapping Penrose patch changed map");
            Check(PenroseTessellation.TryBuild(P(0,0),P(512,512),256,out var small,out _,out string buildReason),buildReason);
            Check(PenroseTessellation.TryBuild(P(0,0),P(1024,1024),256,out var grown,out _,out buildReason),buildReason);
            var fresh=new Level();Check(fresh.PlanCircleTessellation(small,out var initial,out _,out _,out buildReason),buildReason);
            Check(initial.PlanCircleTessellation(grown,out var expanded,out _,out int added,out buildReason),buildReason);Validate(expanded);
            Check(added==grown.Count-small.Count,"Growing Penrose patch changed existing tiles");
            Check(!PenroseTessellation.TryBuild(P(0,0),P(14*256,14*256),256,out var capped,out _,out _) && capped.Count==0,"Penrose tile cap exceeded");
            Check(!PenroseTessellation.TryBuild(P(0,0),P(10000,10000),16,out var oversized,out _,out _) && oversized.Count==0,"Oversized Penrose patch accepted");
            Check(!PenroseTessellation.TryBuild(P(32700,32700),P(32750,32750),1000,out var outside,out _,out _) && outside.Count==0,"Penrose coordinates wrapped");
        });
        Test("circle tessellation previews without changes and records one undo step", () => {
            var level=new Level();var editor=Editor(level);editor.Tool=Tool.Circle;
            editor.CircleSides=6;editor.CircleTileSize=1000;editor.CircleTessellationEnabled=true;
            string before=CanonicalSnapshot(level);
            editor.StartCircle(0,0);editor.UpdateCircle(3000,3000,false);
            Check(level.Polygons.Count==0 && editor.CircleTiles.Count>1 && editor.CircleMessage==null,"Preview modified map or failed");
            int count=editor.CircleTiles.Count;editor.FinishCircle(3000,3000,false);
            Check(level.Polygons.Count==count && editor.Selection.Polygons.Count==count,"Patch was not selected");
            Check(editor.HistoryLabels.Count==2 && editor.HistoryLabels[1]=="Tessellate polygons","Drag made multiple undo steps");
            Validate(level);string after=CanonicalSnapshot(level);
            editor.Undo();Check(CanonicalSnapshot(level)==before,"Undo left tile geometry");
            editor.Redo();Check(CanonicalSnapshot(level)==after && editor.Selection.Polygons.Count==count,"Redo failed");Validate(level);
        });
        Test("circle tessellation reuses existing tiles and shared boundary geometry", () => {
            var level=new Level();Box(level,0,0,1000,1000);var editor=Editor(level);editor.Tool=Tool.Circle;
            editor.CircleSides=4;editor.CircleTileSize=1000;editor.CircleTessellationEnabled=true;
            editor.StartCircle(0,0);editor.FinishCircle(2000,0,false);
            Check(editor.CircleMessage==null && level.Polygons.Count==3,"Existing tile duplicated or blocked");
            Check(level.Endpoints.Count==8 && level.Lines.Count==10,"Shared boundaries duplicated");Validate(level);
            int history=editor.HistoryLabels.Count;string before=CanonicalSnapshot(level);
            editor.StartCircle(0,0);editor.FinishCircle(2000,0,false);
            Check(CanonicalSnapshot(level)==before && editor.HistoryLabels.Count==history,"Repeated patch changed map/history");
            Check(editor.Selection.Polygons.Count==3,"Existing patch not selected");
            editor.Undo();Check(level.Polygons.Count==1,"Undo removed original tile");Validate(level);
            var adjoining=new Level();Box(adjoining,-1000,0,0,1000);
            Check(CircleTessellation.TryBuild(P(0,0),P(0,0),4,1000,out var tiles,out string reason),reason);
            Check(adjoining.PlanCircleTessellation(tiles,out var plan,out _,out int added,out reason),reason);
            Check(added==1 && plan.Endpoints.Count==6 && plan.Lines.Count==7,"Adjacent tile did not share edge");Validate(plan);
        });
        Test("blocked circle tessellation is atomic and cancellation preserves geometry", () => {
            var level=new Level();Box(level,2200,200,2400,400);var editor=Editor(level);editor.Tool=Tool.Circle;
            editor.CircleSides=4;editor.CircleTileSize=1000;editor.CircleTessellationEnabled=true;
            editor.Selection.Polygon=0;string before=CanonicalSnapshot(level);
            editor.StartCircle(0,0);editor.FinishCircle(2000,0,false);
            Check(editor.CircleMessage!=null && CanonicalSnapshot(level)==before && !editor.CanUndo,"Blocked patch changed geometry/history");
            Check(editor.Selection.Polygon==0,"Failed patch lost selection");
            editor.StartCircle(-3000,-3000);editor.UpdateCircle(-2000,-2000,false);editor.CancelCircle();
            Check(editor.CircleTiles.Count==0 && !editor.DrawingCircle && CanonicalSnapshot(level)==before,"Cancel left geometry");
            editor.StartCircle(-3000,-3000);editor.Tool=Tool.Select;
            Check(!editor.DrawingCircle && editor.CircleTiles.Count==0,"Tool change kept preview");
            editor.Tool=Tool.Circle;editor.StartCircle(-3000,-3000);editor.CircleTileSize=2000;
            Check(!editor.DrawingCircle,"Changing size kept stale preview");
            editor.StartCircle(-3000,-3000);editor.CircleSides=6;Check(!editor.DrawingCircle,"Changing sides kept stale preview");
            editor.StartCircle(-3000,-3000);editor.CircleTessellationEnabled=false;Check(!editor.DrawingCircle,"Changing mode kept stale preview");
        });
        Test("circle tessellation handles unsupported sides limits and hidden matching tiles", () => {
            foreach(int sides in new[]{5,7}) Check(!CircleTessellation.TryBuild(P(0,0),P(1000,1000),sides,1000,out _,out string reason) && reason!=null,"Unsupported tiling accepted");
            Check(!CircleTessellation.TryBuild(P(0,0),P(10000,10000),4,16,out var large,out _) && large.Count==0,"Oversized patch accepted");
            Check(CircleTessellation.TryBuild(P(0,0),P(1500,1500),4,100,out var limit,out _) && limit.Count==256,"Exact tile cap rejected");
            Check(!CircleTessellation.TryBuild(P(32700,32700),P(32760,32760),6,1000,out _,out _),"Coordinate overflow wrapped");
            var level=new Level();Box(level,0,0,1000,1000);
            Check(CircleTessellation.TryBuild(P(0,0),P(0,0),4,1000,out var tiles,out _),"Cannot build single tile");
            Level.Filter=p=>false;
            try { Check(!level.PlanCircleTessellation(tiles,out _,out _,out _,out _),"Hidden tile selected"); }
            finally { Level.Filter=p=>true; }
        });
        Test("Filled options default on and box/circle outlines create only reusable boundaries", () => {
            foreach(bool circle in new[]{false,true}) {
                var level=new Level();var editor=Editor(level);editor.Tool=circle?Tool.Circle:Tool.Box;
                Check(editor.BoxFilled && editor.CircleFilled,"Filled defaults changed");
                if(circle) {editor.CircleFilled=false;editor.CircleSides=6;editor.StartCircle(0,0);editor.FinishCircle(2000,2000,true);}
                else {editor.BoxFilled=false;editor.StartBox(0,0);editor.FinishBox(2000,2000);}
                int count=circle?6:4;
                Check(level.Polygons.Count==0 && level.Endpoints.Count==count && level.Lines.Count==count,"Outline created a polygon or duplicate geometry");
                Check(editor.Selection.Lines.Count==count && editor.HistoryLabels.Count==2,"Outline selection/history failed");
                Check(level.Lines.All(l=>l.ClockwisePolygonOwner==-1 && l.CounterclockwisePolygonOwner==-1),"Outline has polygon owners");
                string after=CanonicalSnapshot(level);editor.Undo();Check(level.Lines.Count==0 && level.Endpoints.Count==0,"Outline undo incomplete");
                editor.Redo();Check(CanonicalSnapshot(level)==after && editor.Selection.Lines.Count==count,"Outline redo failed");
                if(circle) {editor.StartCircle(0,0);editor.FinishCircle(2000,2000,true);}
                else {editor.StartBox(0,0);editor.FinishBox(2000,2000);}
                Check(CanonicalSnapshot(level)==after && editor.HistoryLabels.Count==2,"Repeated outline duplicated geometry/history");
                if(circle) {editor.CircleFilled=true;editor.StartCircle(0,0);editor.FinishCircle(2000,2000,true);}
                else {editor.BoxFilled=true;editor.StartBox(0,0);editor.FinishBox(2000,2000);}
                Check(level.Polygons.Count==1 && level.Lines.Count==count,"Filled did not reuse outline");Validate(level);
            }
        });
        Test("unfilled tessellation keeps all shared lines selected and can be filled later", () => {
            foreach(int sides in new[]{3,4,6,8}) {
                var level=new Level();var editor=Editor(level);editor.Tool=Tool.Circle;
                editor.CircleSides=sides;editor.CircleTileSize=1000;editor.CircleTessellationEnabled=true;editor.CircleFilled=false;
                editor.StartCircle(0,0);editor.FinishCircle(3000,3000,false);
                Check(editor.CircleMessage==null && level.Polygons.Count==0 && level.Lines.Count>0,"Outline tessellation failed");
                Check(level.Endpoints.Distinct().Count()==level.Endpoints.Count,"Duplicate outline vertices");
                Check(level.Lines.Select(l=>string.Join(",",l.EndpointIndexes.OrderBy(i=>i))).Distinct().Count()==level.Lines.Count,"Duplicate shared outline lines");
                Check(editor.Selection.Lines.Count==level.Lines.Count && editor.HistoryLabels.Count==2,"Incomplete outline selection/undo");
                string after=CanonicalSnapshot(level);editor.Undo();Check(level.Lines.Count==0,"Patch undo failed");editor.Redo();
                Check(CanonicalSnapshot(level)==after,"Patch redo failed");
                int lines=level.Lines.Count;editor.CircleFilled=true;editor.StartCircle(0,0);editor.FinishCircle(3000,3000,false);
                Check(editor.CircleMessage==null && level.Polygons.Count>0 && level.Lines.Count==lines,"Filling outline patch added duplicate lines");Validate(level);
            }
        });
        Test("outlines attach by splitting shared walls atomically and selecting final line indexes", () => {
            var level=new Level();Box(level,-1000,0,0,3000);
            var editor=Editor(level);editor.Tool=Tool.Circle;editor.CircleSides=4;editor.CircleTileSize=1000;
            editor.CircleFilled=false;editor.CircleTessellationEnabled=true;
            string before=CanonicalSnapshot(level);editor.StartCircle(0,0);editor.FinishCircle(0,2000,false);
            Check(editor.CircleMessage==null && level.Polygons.Count==1 && level.Polygons[0].VertexCount==6,"Outline did not split neighbor wall");
            Check(editor.Selection.Lines.Count==10 && editor.Selection.Lines.All(i=>i>=0 && i<level.Lines.Count),"Renumbering damaged outline selection");Validate(level);
            editor.Undo();Check(CanonicalSnapshot(level)==before,"Shared-wall outline undo failed");
            editor.Tool=Tool.Box;editor.BoxFilled=false;editor.StartBox(-900,100);editor.FinishBox(-100,900);
            Check(editor.BoxMessage!=null && CanonicalSnapshot(level)==before,"Outline created unsplit geometry inside polygon");
            editor.StartBox(0,0);editor.BoxFilled=true;Check(!editor.DrawingBox,"Filled change left stale box preview");
            editor.Tool=Tool.Circle;editor.StartCircle(0,0);editor.CircleFilled=true;Check(!editor.DrawingCircle,"Filled change left stale circle preview");
        });
        Test("merged scenario edits preserve map IDs untouched chunks and directory overlays", () => {
            var source=new MapFile();
            foreach(int index in new[]{2,7,11}) {
                var level=new Level();Box(level,0,0,1000,1000);level.Name="Map "+index;
                level.Chunks[Wadfile.Chunk("TEST")]=new byte[]{(byte)index,99,42};
                level.Chunks[EmbeddedLua.Tag]=EmbeddedLua.Write(new[]{new EmbeddedLuaScript{Name="script",Text="return "+index}});
                level.Chunks[TerminalCodec.Tag]=TerminalCodec.Write(new[]{TerminalDocument.Create()});
                source.Directory[index]=level.Save().Clone();source.Overlays[index]=ScenarioMaps.Overlay(source.Directory[index]);
            }
            source.Overlays[11].LevelName="Directory-only name";source.Overlays[11].EntryPointFlags=(EntryPointFlags)123;
            var resource=new Wadfile.DirectoryEntry();resource.Chunks[Wadfile.Chunk("MML ")]=new byte[]{1,2,3};source.Directory[0]=resource;
            Check(ScenarioMaps.Indexes(source).SequenceEqual(new[]{2,7,11}),"Resources listed as maps");
            var originals=source.Directory.ToDictionary(p=>p.Key,p=>p.Value.Clone());
            var first=new Level();first.Load(source.Directory[2].Clone());first.Name="Edited first";first.Polygons[0].FloorHeight=100;
            var staged=ScenarioMaps.Update(source,2,first.Save());
            var second=new Level();second.Load(staged.Directory[7].Clone());second.Name="Edited second";second.Polygons[0].CeilingHeight=2000;
            var output=TerminalPictureImports.ExportScenario(second.Save(),7,staged);
            Check(output.Directory.Keys.SequenceEqual(new[]{0,2,7,11}),"Scenario map IDs changed");
            Check(ScenarioMaps.Name(output,2)=="Edited first" && ScenarioMaps.Name(output,7)=="Edited second","Staged edits lost");
            Check(output.Overlays[11].LevelName=="Directory-only name" && (int)output.Overlays[11].EntryPointFlags==123,"Untouched overlay changed");
            foreach(int index in new[]{0,11}) foreach(var chunk in originals[index].Chunks)
                Check(output.Directory[index].Chunks[chunk.Key].SequenceEqual(chunk.Value),"Untouched map/resource chunk changed");
            Check(source.Directory[2].Chunks.All(p=>originals[2].Chunks[p.Key].SequenceEqual(p.Value)),"Editing mutated source snapshot");
            string path=System.IO.Path.Combine(System.IO.Path.GetTempPath(),Guid.NewGuid()+".sceA");
            try {
                ScenarioMaps.SaveAtomic(output,path);var loaded=new MapFile();loaded.Load(path);
                Check(loaded.Version==4 && ScenarioMaps.Indexes(loaded).SequenceEqual(new[]{2,7,11}),"Engine scenario format or IDs incorrect");
                Check(ScenarioMaps.Name(loaded,11)=="Directory-only name","Overlay did not survive save");
                foreach(var entry in output.Directory)foreach(var chunk in entry.Value.Chunks)
                    Check(loaded.Directory[entry.Key].Chunks[chunk.Key].SequenceEqual(chunk.Value),"Scenario round trip changed chunk");
                var map=new Level();map.Load(loaded.Directory[2].Clone());Check(map.Polygons[0].FloorHeight==100,"First map edit lost");Validate(map);
                map.Load(loaded.Directory[7].Clone());Check(map.Polygons[0].CeilingHeight==2000,"Second map edit lost");Validate(map);
            } finally {System.IO.File.Delete(path);}
        });
        Test("merged scenario picture imports are global and preserved across map edits", () => {
            var source=new MapFile();var picture=TerminalPictureImports.Encode(new Image(2,2));
            foreach(int index in new[]{0,3}) {
                var level=new Level();Box(level,0,0,1000,1000);
                level.Chunks[TerminalPictureImports.Tag]=TerminalPictureImports.Write(new System.Collections.Generic.Dictionary<int,byte[]>{{1000+index,picture}});
                var doc=TerminalDocument.Create();doc.Pages[1].Type=TerminalPageType.Picture;doc.Pages[1].Permutation=1001;
                level.Chunks[TerminalCodec.Tag]=TerminalCodec.Write(new[]{doc});
                source.Directory[index]=level.Save().Clone();
            }
            var output=TerminalPictureImports.ExportScenario(source.Directory[0],0,source);
            Check(output.Directory.ContainsKey(1000) && output.Directory.ContainsKey(1003),"Another map's picture was dropped");
            Check(TerminalPictureImports.UsedIds(output).Contains(1001) && TerminalPictureImports.NextId(TerminalPictureImports.UsedIds(output))==1002,"Global picture allocation ignored another map");
            // The second map can refer to a managed picture imported by the first.
            var sharedDoc=TerminalDocument.Create();sharedDoc.Pages[1].Type=TerminalPageType.Picture;sharedDoc.Pages[1].Permutation=1000;
            var other=output.Directory[3].Clone();other.Chunks[TerminalCodec.Tag]=TerminalCodec.Write(new[]{sharedDoc});
            var visual=TerminalPictureImports.Export(other,output);
            Check(visual.Directory.ContainsKey(1000) && visual.Directory.ContainsKey(1003),"Visual mode lost shared scenario pictures");
            var modified=output.Directory[0].Clone();modified.Chunks.Remove(TerminalPictureImports.Tag);
            var withoutFirst=TerminalPictureImports.ExportScenario(modified,0,output);
            Check(!withoutFirst.Directory.ContainsKey(1000) && withoutFirst.Directory.ContainsKey(1003),"Removing one archive removed another map's pictures");
            var sharedSource=ScenarioMaps.Update(output,3,other);
            var keepShared=TerminalPictureImports.ExportScenario(modified,0,sharedSource);
            Check(keepShared.Directory.ContainsKey(1000),"Removing the owner's archive broke another map's picture reference");
            var brokenTerminal=modified.Clone();brokenTerminal.Chunks[TerminalCodec.Tag]=new byte[]{1};
            var keepUnknown=TerminalPictureImports.ExportScenario(brokenTerminal,0,output);
            Check(keepUnknown.Directory.ContainsKey(1000),"Unreadable untouched terminal caused a resource loss");
            var conflict=output.Directory[3].Clone();var different=(byte[])picture.Clone();different[different.Length-3]^=1;
            conflict.Chunks[TerminalPictureImports.Tag]=TerminalPictureImports.Write(new System.Collections.Generic.Dictionary<int,byte[]>{{1000,different}});
            bool rejected=false;try{TerminalPictureImports.ExportScenario(conflict,3,output);}catch(System.IO.InvalidDataException){rejected=true;}
            Check(rejected && output.Directory[1000].Chunks[Wadfile.Chunk("PICT")].SequenceEqual(picture),"Conflicting picture IDs overwrote resource");
        });
        Test("merged scenario atomic save keeps the old file on write failure", () => {
            string path=System.IO.Path.Combine(System.IO.Path.GetTempPath(),Guid.NewGuid()+".sceA");
            byte[] original=new byte[]{10,20,30,40};System.IO.File.WriteAllBytes(path,original);
            try {
                var bad=new MapFile();var entry=new Wadfile.DirectoryEntry();entry.Chunks[Wadfile.Chunk("FAIL")]=null;bad.Directory[0]=entry;
                bool failed=false;try{ScenarioMaps.SaveAtomic(bad,path);}catch{failed=true;}
                Check(failed && System.IO.File.ReadAllBytes(path).SequenceEqual(original),"Failed save replaced original scenario");
                Check(!System.IO.Directory.GetFiles(System.IO.Path.GetDirectoryName(path),System.IO.Path.GetFileName(path)+".*.tmp").Any(),"Failed save left temporary file");
            } finally {System.IO.File.Delete(path);}
        });
        Test("Free scale stretches axes independently and carries contents with one undo", () => {
            var level=new Level();Box(level,0,0,1000,1000);level.NewObject(P(750,250),0);level.Objects[0].Type=ObjectType.Player;level.Objects[0].Facing=90;
            level.Annotations.Add(new Annotation{X=750,Y=250,PolygonIndex=0,Text="warp"});var editor=Editor(level);editor.Selection.Polygon=0;editor.Tool=Tool.Scale;
            string before=CanonicalSnapshot(level);editor.ButtonPress(1000,1000,EditorModifiers.None);
            editor.Motion(1250,875,EditorModifiers.None);editor.ButtonRelease(1500,750,EditorModifiers.None);
            Check(level.Endpoints.Contains(P(-500,250)) && level.Endpoints.Contains(P(1500,750)),"Independent factors or pivot wrong");
            Check(level.Objects[0].X==1000 && level.Objects[0].Y==375 && level.Objects[0].Facing==90 && level.Annotations[0].X==1000 && level.Annotations[0].Y==375,"Contents failed to stretch");
            Check(editor.HistoryLabels.Count==2,"Warp created multiple history steps");Validate(level);string after=CanonicalSnapshot(level);
            editor.Undo();Check(CanonicalSnapshot(level)==before,"Warp undo failed");editor.Redo();Check(CanonicalSnapshot(level)==after,"Warp redo failed");
        });
        Test("Scale can toggle Shift during a drag without rounding drift", () => {
            var level=new Level();Box(level,0,0,1000,1000);var editor=Editor(level);editor.Selection.Polygon=0;editor.Tool=Tool.Scale;
            editor.ButtonPress(1000,1000,EditorModifiers.None);editor.Motion(1500,750,EditorModifiers.None);
            var free=level.Endpoints.ToArray();editor.Motion(1500,750,EditorModifiers.Shift);
            Check(level.Endpoints.Contains(P(-250,-250)) && level.Endpoints.Contains(P(1250,1250)),"Shift did not preserve proportions");
            editor.Motion(1500,750,EditorModifiers.None);Check(level.Endpoints.SequenceEqual(free),"Modifier toggle accumulated drift");
            editor.ButtonRelease(1500,750,EditorModifiers.None);Validate(level);
        });
        Test("Free scale preserves shared vertices and supports center-axis drags", () => {
            var level=new Level();Box(level,0,0,1000,1000);Box(level,1000,0,2000,1000);var original=level.Endpoints.ToArray();var editor=Editor(level);
            editor.Selection.Polygons.UnionWith(new short[]{0,1});editor.Tool=Tool.Scale;
            editor.ButtonPress(2000,1000,EditorModifiers.None);editor.ButtonRelease(2500,750,EditorModifiers.None);
            for(int i=0;i<original.Length;i++)Check(level.Endpoints[i].Equals(P((short)(-500+original[i].X*1.5),(short)(250+original[i].Y*.5))),"Shared vertices stretched twice");Validate(level);
            level=new Level();Box(level,0,0,1000,1000);editor=Editor(level);editor.Selection.Polygon=0;editor.Tool=Tool.Scale;
            editor.ButtonPress(1000,500,EditorModifiers.None);editor.ButtonRelease(1500,750,EditorModifiers.None);
            Check(level.Endpoints.Contains(P(-500,-250)) && level.Endpoints.Contains(P(1500,1250)),"Center-axis drag failed");Validate(level);
        });
        Test("Free scale rejects collisions and flipped neighbouring polygons atomically", () => {
            var level=new Level();Box(level,0,0,1000,1000);Box(level,1000,0,2000,1000);var editor=Editor(level);editor.Selection.Polygon=0;editor.Tool=Tool.Scale;
            string before=CanonicalSnapshot(level);
            foreach(short x in new short[]{2000,2500}) {
                editor.ButtonPress(1000,1000,EditorModifiers.None);editor.ButtonRelease(x,1000,EditorModifiers.None);
                Check(CanonicalSnapshot(level)==before && editor.HistoryLabels.Count==1,"Rejected warp mutated shared geometry");
            }
            level=new Level();short a=level.NewPoint(0,0),b=level.NewPoint(1000,0);short line=level.NewLine(a,b);level.NewPoint(1500,0);
            editor=Editor(level);editor.Selection.Line=line;editor.Tool=Tool.Scale;before=CanonicalSnapshot(level);
            editor.ButtonPress(1000,0,EditorModifiers.None);editor.ButtonRelease(1500,0,EditorModifiers.None);
            Check(CanonicalSnapshot(level)==before && editor.HistoryLabels.Count==1,"Warp overlapped an unselected vertex");
        });
        Test("Right click samples polygon paint values without moving or changing history", () => {
            foreach(var tool in new[]{Tool.FloorHeight,Tool.CeilingHeight,Tool.Media,Tool.AmbientSound,Tool.RandomSound,Tool.PolygonType,Tool.FloorTexture,Tool.CeilingTexture,Tool.FloorLight,Tool.CeilingLight,Tool.MediaLight}) {
                var level=new Level();Box(level,0,0,1000,1000);Box(level,1000,0,2000,1000);var source=level.Polygons[0];var target=level.Polygons[1];
                source.FloorHeight=123;source.CeilingHeight=2345;source.MediaIndex=-1;source.AmbientSound=-1;source.RandomSound=-1;source.Type=PolygonType.Hill;
                source.FloorTexture=(ShapeDescriptor)0x1234;source.CeilingTexture=(ShapeDescriptor)0x1543;source.FloorTransferMode=2;source.CeilingTransferMode=3;
                source.FloorLight=3;source.CeilingLight=4;source.MediaLight=5;
                target.MediaIndex=0;target.AmbientSound=0;target.RandomSound=0;
                var editor=Editor(level);editor.Tool=tool;editor.Selection.Polygon=0;editor.PaintIndex=99;editor.PaintDescriptor=(ShapeDescriptor)99;editor.PaintTransferMode=0;
                short Value(Polygon p) => tool switch {Tool.FloorHeight=>p.FloorHeight,Tool.CeilingHeight=>p.CeilingHeight,Tool.Media=>p.MediaIndex,Tool.AmbientSound=>p.AmbientSound,Tool.RandomSound=>p.RandomSound,Tool.PolygonType=>(short)p.Type,Tool.FloorLight=>p.FloorLight,Tool.CeilingLight=>p.CeilingLight,_=>p.MediaLight};
                bool texture=tool==Tool.FloorTexture || tool==Tool.CeilingTexture;
                var descriptor=tool==Tool.FloorTexture?source.FloorTexture:source.CeilingTexture;
                short transfer=tool==Tool.FloorTexture?source.FloorTransferMode:source.CeilingTransferMode;
                string before=CanonicalSnapshot(level);
                editor.ButtonPress(250,250,EditorModifiers.RightClick);
                Check(texture?editor.PaintDescriptor.Equals(descriptor) && editor.PaintTransferMode==transfer:editor.PaintIndex==Value(source),"Did not sample on press: "+tool);
                editor.Motion(750,750,EditorModifiers.RightClick);editor.ButtonRelease(750,750,EditorModifiers.RightClick);
                Check(CanonicalSnapshot(level)==before,"Sampling changed map: "+tool);
                Check(!editor.Changed,"Sampling marked map changed: "+tool);
                Check(editor.HistoryLabels.Count==1 && editor.Selection.Polygon==0,"Sampling changed history or selection: "+tool);
                editor.ButtonPress(-500,-500,EditorModifiers.RightClick);editor.ButtonRelease(-500,-500,EditorModifiers.RightClick);
                Check(texture?editor.PaintDescriptor.Equals(descriptor):editor.PaintIndex==Value(source),"Empty-space sampling reset value: "+tool);
                editor.ButtonPress(1500,500,EditorModifiers.None);editor.ButtonRelease(1500,500,EditorModifiers.None);
                Check(texture?(tool==Tool.FloorTexture?target.FloorTexture.Equals(descriptor) && target.FloorTransferMode==transfer:target.CeilingTexture.Equals(descriptor) && target.CeilingTransferMode==transfer):Value(target)==Value(source),"Left click failed to paint sampled value: "+tool);
                Check(editor.HistoryLabels.Count==2,"Painting sampled value did not record undo: "+tool);
                editor.Undo();Check(CanonicalSnapshot(level)==before,"Paint undo failed: "+tool);
            }
        });
        Test("Editor settings round trip in memory without depending on user preferences", () => {
            var original=Weland.Weland.Settings;
            try {
                Weland.Weland.Settings=new Settings(false);
                var editor=Editor(new Level());
                Check(editor.BoxFilled && editor.CircleFilled && !editor.CircleTessellationEnabled && editor.CircleSides==8,"Isolated defaults wrong");
                editor.BoxFilled=false;editor.CircleFilled=false;editor.CircleTessellationEnabled=true;editor.CircleSides=6;editor.CircleTileSize=512;
                editor.RingSides=32;editor.RingHasInner=false;editor.RingInnerPercent=75;editor.RingInnerCount=4;editor.RingFilled=false;editor.SaveSettings();
                var loaded=Editor(new Level());
                Check(!loaded.BoxFilled && !loaded.CircleFilled && loaded.CircleTessellationEnabled && loaded.CircleSides==6 && loaded.CircleTileSize==512,"In-memory settings failed to round trip");
                Check(loaded.RingSides==32 && !loaded.RingHasInner && loaded.RingInnerPercent==75 && loaded.RingInnerCount==4 && !loaded.RingFilled,"Ring settings failed to round trip");
                Weland.Weland.Settings=new Settings(false);
                var clean=Editor(new Level());Check(clean.BoxFilled && clean.CircleFilled && !clean.CircleTessellationEnabled,"Fresh test settings inherited earlier choices");
            } finally {Weland.Weland.Settings=original;}
        });
        Test("Ring sectors beyond eight sides are convex connected and preserve their hole", () => {
            foreach(int sides in new[]{3,9,16,64,128}) foreach(int inner in new[]{0,25,75}) {
                Check(RingGeometry.TryBuild(P(-8000,-6000),P(8000,6000),sides,inner,out var sectors,out string reason),reason);
                Check(sectors.Count==sides && sectors.All(p=>p.Count==(inner==0?3:4)),"Wrong sector count or size");
                var level=new Level();Check(level.PlanCircleTessellation(sectors,out var plan,out var selected,out int added,out reason),reason);
                Check(added==sides && selected.Count==sides,"Missing sectors");Validate(plan);
                Check(plan.Endpoints.Count==(inner==0?sides+1:2*sides) && plan.Lines.Count==(inner==0?2*sides:3*sides),"Duplicate shared vertices or edges");
                Check(plan.Lines.Count(l=>l.ClockwisePolygonOwner!=-1 && l.CounterclockwisePolygonOwner!=-1)==sides,"Spokes do not connect sectors");
                Check((plan.GetEnclosingPolygon(P(0,0))==-1)==(inner>0),"Ring hole was filled or disk left empty");
                long expected=0;foreach(var sector in sectors)for(int i=0;i<sector.Count;i++){var a=sector[i];var b=sector[(i+1)%sector.Count];expected+=(long)a.X*b.Y-(long)b.X*a.Y;}
                Check(Area2(plan)==expected,"Sector areas changed during creation");
                var loaded=new Level();loaded.Load(plan.Save().Clone());Validate(loaded);Check(Area2(loaded)==expected,"Ring changed on save/load");
            }
        });
        Test("Ring drag previews atomically records one undo and reuses identical sectors", () => {
            var level=new Level();var editor=Editor(level);editor.Tool=Tool.Ring;editor.RingSides=16;editor.RingInnerPercent=50;
            string before=CanonicalSnapshot(level);editor.ButtonPress(0,0,EditorModifiers.None);editor.Motion(4000,2000,EditorModifiers.None);
            Check(level.Polygons.Count==0 && editor.DrawingRing && editor.RingSectors.Count==16,"Ring preview changed map");
            editor.ButtonRelease(4000,2000,EditorModifiers.Shift);
            Check(level.Polygons.Count==16 && editor.Selection.Polygons.Count==16 && editor.HistoryLabels.Count==2 && editor.HistoryLabels[1]=="Draw ring","Ring not one selected undo group");
            Check(level.Endpoints.Max(p=>p.Y)==4000,"Shift did not constrain ring");Validate(level);string after=CanonicalSnapshot(level);
            editor.Undo();Check(CanonicalSnapshot(level)==before,"Ring undo failed");editor.Redo();Check(CanonicalSnapshot(level)==after,"Ring redo failed");
            editor.ButtonPress(4000,4000,EditorModifiers.None);editor.ButtonRelease(0,0,EditorModifiers.None);
            Check(CanonicalSnapshot(level)==after && editor.HistoryLabels.Count==2 && editor.Selection.Polygons.Count==16,"Repeated reverse ring duplicated geometry");
            editor.ButtonPress(-8000,-8000,EditorModifiers.None);editor.Motion(-4000,-4000,EditorModifiers.None);editor.Tool=Tool.Select;
            Check(!editor.DrawingRing && CanonicalSnapshot(level)==after,"Tool switch left a ring preview or changed map");
        });
        Test("Ring placement preserves hole contents rejects overlaps and cancelled tiny rings", () => {
            var level=new Level();Box(level,1900,1900,2100,2100);var editor=Editor(level);editor.Tool=Tool.Ring;editor.RingSides=12;
            editor.StartRing(0,0);editor.FinishRing(4000,4000,false);
            Check(editor.RingMessage==null && level.Polygons.Count==13 && level.GetEnclosingPolygon(P(2000,2000))==0,"Ring did not preserve geometry in its hole");Validate(level);
            level=new Level();Box(level,0,0,4000,4000);editor=Editor(level);editor.Tool=Tool.Ring;string before=CanonicalSnapshot(level);
            editor.StartRing(0,0);editor.FinishRing(4000,4000,false);
            Check(editor.RingMessage!=null && CanonicalSnapshot(level)==before && editor.HistoryLabels.Count==1,"Overlap partially created a ring");
            editor.StartRing(5000,5000);editor.UpdateRing(8000,8000,false);editor.RingInnerPercent=60;
            Check(!editor.DrawingRing && editor.RingSectors.Count==0,"Option change left stale preview");
            editor.RingSides=128;editor.StartRing(5000,5000);editor.FinishRing(5002,5002,false);
            Check(editor.RingMessage!=null && CanonicalSnapshot(level)==before && editor.HistoryLabels.Count==1,"Tiny ring duplicated vertices");
            editor.RingSides=16;editor.RingHasInner=false;editor.StartRing(5000,5000);editor.FinishRing(9000,9000,false);
            Check(editor.RingMessage==null && level.Polygons.Count==17 && level.GetEnclosingPolygon(P(7000,7000))!=-1,"Filled ring failed");Validate(level);
        });
        Test("Multiple concentric ring bands share spokes and leave one hole", () => {
            foreach(int count in new[]{2,3,8,16}) {
                int sides=16;
                Check(RingGeometry.TryBuild(P(0,0),P(16000,16000),sides,count,25,out var sectors,out string reason),reason);
                var level=new Level();Check(level.PlanCircleTessellation(sectors,out var plan,out _,out int added,out reason),reason);
                Check(added==sides*count && plan.Endpoints.Count==(count+1)*sides && plan.Lines.Count==(2*count+1)*sides,"Concentric topology duplicated geometry");
                Check(plan.Endpoints.Count-plan.Lines.Count+plan.Polygons.Count==0 && plan.GetEnclosingPolygon(P(8000,8000))==-1,"Bands filled or divided the central hole");
                Validate(plan);
                Check(plan.Endpoints.Contains(P(8000,6000)),"Innermost size changed");
                if(count==3)Check(plan.Endpoints.Contains(P(8000,2000)) && plan.Endpoints.Contains(P(8000,4000)),"Intermediate rings not evenly spaced");
                var loaded=new Level();loaded.Load(plan.Save().Clone());Validate(loaded);Check(Area2(loaded)==Area2(plan),"Bands changed on save");
            }
            Check(!RingGeometry.TryBuild(P(0,0),P(16000,16000),128,3,25,out _,out _),"Sector limit ignored");
        });
        Test("Multi ring outline creates only shared lines with undo and can be filled later", () => {
            var level=new Level();var editor=Editor(level);editor.Tool=Tool.Ring;editor.RingSides=12;editor.RingInnerCount=3;editor.RingInnerPercent=25;editor.RingFilled=false;
            string before=CanonicalSnapshot(level);editor.StartRing(0,0);editor.FinishRing(8000,8000,false);
            Check(editor.RingMessage==null && level.Polygons.Count==0 && level.Endpoints.Count==48 && level.Lines.Count==84 && editor.Selection.Lines.Count==84,"Outline created polygons or duplicate edges");Validate(level);
            Check(editor.HistoryLabels.Count==2 && editor.HistoryLabels[1]=="Draw ring outline","Outline not one undo step");string after=CanonicalSnapshot(level);
            editor.Undo();Check(CanonicalSnapshot(level)==before,"Multi outline undo failed");editor.Redo();Check(CanonicalSnapshot(level)==after,"Multi outline redo failed");
            editor.StartRing(0,0);editor.FinishRing(8000,8000,false);Check(CanonicalSnapshot(level)==after && editor.HistoryLabels.Count==2,"Repeated outline added geometry/history");
            editor.RingFilled=true;editor.StartRing(0,0);editor.FinishRing(8000,8000,false);
            Check(editor.RingMessage==null && level.Polygons.Count==36 && level.Endpoints.Count==48 && level.Lines.Count==84 && editor.Selection.Polygons.Count==36,"Filled bands did not reuse outline");Validate(level);
            editor.Undo();Check(CanonicalSnapshot(level)==after,"Fill undo did not retain outline");
            editor.StartRing(10000,0);editor.UpdateRing(16000,6000,false);editor.RingInnerCount=2;Check(!editor.DrawingRing,"Count change kept stale preview");
            editor.StartRing(10000,0);editor.UpdateRing(16000,6000,false);editor.RingFilled=false;Check(!editor.DrawingRing,"Filled change kept stale preview");
        });
        Test("Ring stairs step clockwise with fixed ceilings and undo reused height changes", () => {
            var level=new Level();var editor=Editor(level);editor.Tool=Tool.Ring;editor.RingSides=12;editor.RingInnerCount=3;editor.RingStairs=true;editor.RingStairStart=-256;editor.RingStairStep=64;
            string before=CanonicalSnapshot(level);editor.StartRing(0,0);editor.FinishRing(8000,8000,false);
            Check(editor.RingMessage==null && level.Polygons.Count==36,"Ring stairs failed");
            for(int i=0;i<36;i++)Check(level.Polygons[i].FloorHeight==-256+(i%12)*64 && level.Polygons[i].CeilingHeight==1024,"Wrong ring band heights or changed ceiling");
            Check(editor.HistoryLabels.Count==2 && editor.HistoryLabels[1]=="Draw ring stairs","Stair ring not one undo step");string after=CanonicalSnapshot(level);
            editor.Undo();Check(CanonicalSnapshot(level)==before,"Ring stair undo failed");editor.Redo();Check(CanonicalSnapshot(level)==after,"Ring stair redo failed");Validate(level);
            editor.RingStairStart=0;editor.RingStairStep=64;editor.StartRing(0,0);editor.FinishRing(8000,8000,false);
            Check(level.Polygons.Count==36 && level.Polygons[32].FloorHeight==512 && editor.HistoryLabels.Count==3,"Reused ring did not update heights");
            editor.Undo();Check(CanonicalSnapshot(level)==after,"Reused ring stair undo failed");
            editor.RingStairStep=short.MaxValue;editor.StartRing(10000,0);editor.FinishRing(18000,8000,false);
            Check(editor.RingMessage!=null && CanonicalSnapshot(level)==after && editor.HistoryLabels.Count==3,"Invalid ring stair partially edited map");
            editor.RingStairStart=1024;editor.RingStairStep=0;editor.StartRing(10000,0);editor.FinishRing(18000,8000,false);
            Check(editor.RingMessage!=null && CanonicalSnapshot(level)==after,"Stair floor exceeded ceiling");
        });
        Test("Tessellation stairs follow drag axis and direction for every supported tile", () => {
            foreach(int sides in new[]{3,4,6,8}) foreach(var end in new[]{P(900,400),P(-900,-400),P(400,900),P(-400,-900)}) {
                var level=new Level();var editor=Editor(level);editor.Tool=Tool.Circle;editor.CircleSides=sides;editor.CircleTileSize=256;editor.CircleTessellationEnabled=true;
                editor.CircleStairs=true;editor.CircleStairStart=-512;editor.CircleStairStep=64;
                Check(CircleTessellation.TryBuild(P(0,0),end,sides,256,out var tiles,out var steps,out string reason),reason);
                Check(steps.Count==tiles.Count && steps.Min()==0 && steps.Max()>0,"Missing tessellation step levels");
                editor.StartCircle(0,0);editor.FinishCircle(end.X,end.Y,false);
                Check(editor.CircleMessage==null && level.Polygons.Count==tiles.Count,"Tessellation stairs failed");
                for(int i=0;i<tiles.Count;i++)Check(level.Polygons[i].FloorHeight==-512+steps[i]*64 && level.Polygons[i].CeilingHeight==1024,"Wrong tessellation step height");
                Check(editor.HistoryLabels.Count==2 && editor.HistoryLabels[1]=="Tessellate stairs","Tile stairs not one undo");Validate(level);
                string after=CanonicalSnapshot(level);editor.Undo();Check(level.Polygons.Count==0,"Tessellation stairs undo failed");editor.Redo();Check(CanonicalSnapshot(level)==after,"Tessellation stairs redo failed");
            }
            var squares=new Level();var squareEditor=Editor(squares);squareEditor.Tool=Tool.Circle;squareEditor.CircleSides=4;squareEditor.CircleTileSize=1000;squareEditor.CircleTessellationEnabled=true;squareEditor.CircleStairs=true;squareEditor.CircleStairStart=0;squareEditor.CircleStairStep=-128;
            squareEditor.StartCircle(0,0);squareEditor.FinishCircle(2500,1000,false);
            Check(squares.Polygons[0].FloorHeight==0 && squares.Polygons[1].FloorHeight==-128 && squares.Polygons[2].FloorHeight==-256 && squares.Polygons[3].FloorHeight==0,"Square rows did not share descending heights");
            string before=CanonicalSnapshot(squares);squareEditor.CircleStairStart=900;squareEditor.CircleStairStep=128;squareEditor.StartCircle(0,0);squareEditor.FinishCircle(2500,1000,false);
            Check(squareEditor.CircleMessage!=null && CanonicalSnapshot(squares)==before,"Invalid tessellation stairs changed reused floors");
        });
        Test("Stair options persist cancel previews and leave outlines without floors", () => {
            var settings=Weland.Weland.Settings;
            try {
                Weland.Weland.Settings=new Settings(false);var level=new Level();var editor=Editor(level);
                editor.RingStairs=true;editor.RingStairStart=-1024;editor.RingStairStep=-256;editor.CircleStairs=true;editor.CircleStairStart=-2048;editor.CircleStairStep=64;editor.RingStairCeiling=4096;editor.RingCeilingFollows=true;editor.CircleStairCeiling=5120;editor.CircleCeilingFollows=true;editor.SaveSettings();
                var loaded=Editor(new Level());Check(loaded.RingStairs && loaded.RingStairStart==-1024 && loaded.RingStairStep==-256 && loaded.CircleStairs && loaded.CircleStairStart==-2048 && loaded.CircleStairStep==64 && loaded.RingStairCeiling==4096 && loaded.RingCeilingFollows && loaded.CircleStairCeiling==5120 && loaded.CircleCeilingFollows,"Stair settings not saved");
                editor.Tool=Tool.Ring;editor.StartRing(0,0);editor.UpdateRing(4000,4000,false);editor.RingStairStart=0;Check(!editor.DrawingRing,"Ring stair setting left stale preview");
                editor.RingFilled=false;editor.RingStairStart=short.MaxValue;editor.StartRing(0,0);editor.FinishRing(4000,4000,false);Check(editor.RingMessage==null && level.Polygons.Count==0 && level.Lines.Count>0,"Ring outline applied stairs");
                level=new Level();editor=Editor(level);editor.Tool=Tool.Circle;editor.CircleSides=4;editor.CircleTessellationEnabled=true;editor.StartCircle(0,0);editor.UpdateCircle(2000,2000,false);editor.CircleStairStep=128;Check(!editor.DrawingCircle,"Tile stair setting left stale preview");
                editor.CircleFilled=false;editor.CircleStairStart=short.MaxValue;editor.StartCircle(0,0);editor.FinishCircle(2000,2000,false);Check(editor.CircleMessage==null && level.Polygons.Count==0 && level.Lines.Count>0,"Tile outline applied stairs");
            } finally {Weland.Weland.Settings=settings;}
        });
        Test("Clockwise ring stairs share angular heights across bands and ceilings follow floors", () => {
            foreach(short step in new short[]{256,-256}) {
                var level=new Level();var editor=Editor(level);editor.Tool=Tool.Ring;editor.RingSides=12;editor.RingInnerCount=2;
                editor.RingStairs=true;editor.RingStairStart=-512;editor.RingStairStep=step;editor.RingStairCeiling=1536;editor.RingCeilingFollows=true;
                string before=CanonicalSnapshot(level);editor.StartRing(0,0);editor.FinishRing(8000,8000,false);
                Check(editor.RingMessage==null && level.Polygons.Count==24,"Following ceiling ring failed");
                for(int band=0;band<2;band++)for(int sector=0;sector<12;sector++) {
                    var p=level.Polygons[band*12+sector];
                    Check(p.FloorHeight==-512+sector*step && p.CeilingHeight==1536+sector*step && p.CeilingHeight-p.FloorHeight==2048,"Clockwise ring or headroom wrong");
                }
                // First sector lies clockwise of the top vertex; the opposite
                // angular sector has advanced six steps, regardless of band.
                Check(level.Endpoints.Contains(P(4000,0)) && level.Polygons[6].FloorHeight==-512+6*step,"Ring stair origin moved");
                Validate(level);string after=CanonicalSnapshot(level);editor.Undo();Check(CanonicalSnapshot(level)==before,"Following ceiling ring undo failed");editor.Redo();Check(CanonicalSnapshot(level)==after,"Following ceiling ring redo failed");
                editor.StartRing(-8000,-8000);editor.UpdateRing(-4000,-4000,false);editor.RingCeilingFollows=false;Check(!editor.DrawingRing,"Ceiling checkbox kept stale preview");
            }
        });
        Test("Tessellated ceilings stay fixed or follow floor steps with undo on reused polygons", () => {
            var level=new Level();var editor=Editor(level);editor.Tool=Tool.Circle;editor.CircleSides=4;editor.CircleTileSize=1000;editor.CircleTessellationEnabled=true;
            editor.CircleStairs=true;editor.CircleStairStart=2048;editor.CircleStairStep=256;editor.CircleStairCeiling=4096;
            editor.StartCircle(0,0);editor.FinishCircle(2500,0,false);
            Check(editor.CircleMessage==null && level.Polygons.Count==3,"Fixed ceiling stairs failed");
            for(int i=0;i<3;i++)Check(level.Polygons[i].FloorHeight==2048+256*i && level.Polygons[i].CeilingHeight==4096,"Fixed ceiling changed");
            string before=CanonicalSnapshot(level);editor.CircleCeilingFollows=true;editor.StartCircle(0,0);editor.FinishCircle(2500,0,false);
            for(int i=0;i<3;i++)Check(level.Polygons[i].CeilingHeight==4096+256*i && level.Polygons[i].CeilingHeight-level.Polygons[i].FloorHeight==2048,"Following ceiling changed clearance");
            Check(editor.HistoryLabels.Count==3,"Ceiling-only changes to reused tiles lacked undo");editor.Undo();Check(CanonicalSnapshot(level)==before,"Ceiling-only undo failed");
            editor.CircleStairStart=32700;editor.CircleStairCeiling=32760;editor.CircleStairStep=64;editor.StartCircle(0,0);editor.FinishCircle(2500,0,false);
            Check(editor.CircleMessage!=null && CanonicalSnapshot(level)==before,"Following ceiling overflow partially changed reused tiles");
            editor.CircleStairStart=2048;editor.CircleStairCeiling=2048;editor.CircleStairStep=0;editor.StartCircle(0,0);editor.FinishCircle(2500,0,false);
            Check(editor.CircleMessage!=null && CanonicalSnapshot(level)==before,"Zero clearance accepted");
            editor.StartCircle(-4000,0);editor.UpdateCircle(-2000,0,false);editor.CircleStairCeiling=5120;Check(!editor.DrawingCircle,"Ceiling field kept stale preview");
        });
        Test("Select all and deselect preserve geometry and create no history", () => {
            var level=new Level();Box(level,0,0,1000,1000);level.NewPoint(2000,2000);var editor=Editor(level);editor.Tool=Tool.Select;
            string before=CanonicalSnapshot(level);editor.SelectAllGeometry();
            Check(editor.Selection.Points.Count==level.Endpoints.Count && editor.Selection.Lines.Count==level.Lines.Count && editor.Selection.Polygons.Count==level.Polygons.Count,"Select all missed geometry");
            editor.DeselectAllGeometry();Check(!editor.Selection.HasMultipleGeometry && editor.Selection.Polygon==-1 && editor.Selection.Point==-1,"Deselect all kept geometry");
            Check(!editor.Changed && editor.HistoryLabels.Count==1 && CanonicalSnapshot(level)==before,"Selection commands edited map/history");
        });
        Test("Mirror and flip preserve shared topology wall textures contents and undo", () => {
            foreach(bool horizontal in new[]{true,false}) {
                var level=new Level();Box(level,0,0,1000,1000);Box(level,1000,0,2000,1000);level.Polygons[0].FloorHeight=-128;level.Pave();
                for(int i=0;i<level.Sides.Count;i++)level.Sides[i].Primary.Texture=(ShapeDescriptor)(100+i);
                level.NewObject(P(200,300),0);level.Objects[0].Type=ObjectType.Player;level.Objects[0].Facing=30;
                level.Annotations.Add(new Annotation{X=200,Y=300,PolygonIndex=0,Text="mirror"});
                var editor=Editor(level);editor.Tool=Tool.Select;editor.Selection.Polygons.UnionWith(new short[]{0,1});string before=CanonicalSnapshot(level);
                var textures=level.Sides.Select(s=>s.Primary.Texture).ToArray();
                Check(editor.MirrorSelectedGeometry(horizontal,out string reason),reason);Validate(level);
                Check(level.Objects[0].X==(horizontal?1800:200) && level.Objects[0].Y==(horizontal?300:700) && Math.Abs(level.Objects[0].Facing-(horizontal?150:330))<1,"Reflection lost contents or facing");
                Check(level.Annotations[0].X==level.Objects[0].X && level.Annotations[0].Y==level.Objects[0].Y && level.Objects[0].PolygonIndex==0,"Reflection lost annotation/owner");
                Check(level.Sides.Select(s=>s.Primary.Texture).SequenceEqual(textures) && level.Polygons[0].FloorHeight==-128,"Reflection reset textures or heights");
                for(int i=0;i<level.Polygons.Count;i++)for(int edge=0;edge<level.Polygons[i].VertexCount;edge++) {
                    var p=level.Polygons[i];var line=level.Lines[p.LineIndexes[edge]];
                    Check((line.ClockwisePolygonOwner==i)==(line.EndpointIndexes[1]==p.EndpointIndexes[edge]),"Reflection winding or ownership wrong");
                }
                Check(editor.HistoryLabels.Count==2 && editor.Selection.Polygons.Count==2,"Reflection changed selection or history");string after=CanonicalSnapshot(level);
                editor.Undo();Check(CanonicalSnapshot(level)==before,"Reflection undo failed");editor.Redo();Check(CanonicalSnapshot(level)==after,"Reflection redo failed");
                Check(editor.MirrorSelectedGeometry(horizontal,out reason),reason);Check(CanonicalSnapshot(level)==before,"Double reflection did not restore map");Validate(level);
            }
        });
        Test("Reflection rejects overlapping neighbours atomically and handles lines", () => {
            var level=new Level();Box(level,0,0,1000,1000);Box(level,1000,0,2000,1000);var editor=Editor(level);editor.Tool=Tool.Select;editor.Selection.Polygon=0;
            string before=CanonicalSnapshot(level);Check(!editor.MirrorSelectedGeometry(true,out string reason) && reason!=null && CanonicalSnapshot(level)==before && editor.HistoryLabels.Count==1,"Attached mirror duplicated vertices");
            level=new Level();Box(level,0,0,500,1000);Box(level,3000,0,4000,1000);Box(level,600,0,900,1000);editor=Editor(level);editor.Tool=Tool.Select;editor.Selection.Polygons.UnionWith(new short[]{0,1});before=CanonicalSnapshot(level);
            Check(!editor.MirrorSelectedGeometry(true,out reason) && CanonicalSnapshot(level)==before && editor.HistoryLabels.Count==1,"Mirror overlapped unrelated geometry");
            level=new Level();short a=level.NewPoint(0,0),b=level.NewPoint(1000,500);short line=level.NewLine(a,b);editor=Editor(level);editor.Tool=Tool.Select;editor.Selection.Line=line;
            Check(editor.MirrorSelectedGeometry(true,out reason) && level.Endpoints[a].Equals(P(1000,0)) && level.Endpoints[b].Equals(P(0,500)),"Selected line mirror failed");
            level=new Level();a=level.NewPoint(100,0);b=level.NewPoint(100,500);line=level.NewLine(a,b);editor=Editor(level);editor.Tool=Tool.Select;editor.Selection.Line=line;
            Check(editor.MirrorSelectedGeometry(true,out reason) && !editor.Changed && editor.HistoryLabels.Count==1,"Identity reflection marked map changed");
        });
        Test("Shift height handles floor ceiling both attached geometry and atomic limits", () => {
            foreach(var targets in new[]{(true,false),(false,true),(true,true)}) {
                var level=new Level();Box(level,0,0,1000,1000);Box(level,1000,0,2000,1000);var editor=Editor(level);editor.Tool=Tool.Select;editor.Selection.Polygon=0;
                level.Polygons[0].FloorTexture=(ShapeDescriptor)123;string before=CanonicalSnapshot(level);
                Check(editor.ShiftSelectedHeights(128,targets.Item1,targets.Item2,out string reason),reason);
                Check(level.Polygons[0].FloorHeight==(targets.Item1?128:0) && level.Polygons[0].CeilingHeight==(targets.Item2?1152:1024) && level.Polygons[1].FloorHeight==0 && level.Polygons[1].CeilingHeight==1024 && level.Polygons[0].FloorTexture.Equals((ShapeDescriptor)123),"Height shift targets/settings wrong");
                Check(editor.HistoryLabels.Count==2,"Height shift not one history step");string after=CanonicalSnapshot(level);editor.Undo();Check(CanonicalSnapshot(level)==before,"Height shift undo failed");editor.Redo();Check(CanonicalSnapshot(level)==after,"Height shift redo failed");
            }
            var attached=new Level();Box(attached,0,0,1000,1000);Box(attached,1000,0,2000,1000);var e=Editor(attached);e.Tool=Tool.Select;e.Selection.Line=(short)attached.Lines.FindIndex(l=>l.ClockwisePolygonOwner!=-1 && l.CounterclockwisePolygonOwner!=-1);
            Check(e.ShiftSelectedHeights(-256,true,true,out string error),error);Check(attached.Polygons.All(p=>p.FloorHeight==-256 && p.CeilingHeight==768),"Selected shared line missed attached heights");
            string snapshot=CanonicalSnapshot(attached);int count=e.HistoryLabels.Count;
            Check(!e.ShiftSelectedHeights(32768,false,true,out error) && !e.ShiftSelectedHeights(2048,true,false,out error) && CanonicalSnapshot(attached)==snapshot && e.HistoryLabels.Count==count,"Invalid height shift partially changed map");
            Check(!e.ShiftSelectedHeights(128,false,false,out error),"No targets accepted");e.DeselectAllGeometry();Check(!e.ShiftSelectedHeights(128,true,true,out error),"Empty shift accepted");
        });
        Test("Clipboard copies shared polygons textures contents and one undo step", () => {
            var level=new Level();Box(level,0,0,1000,1000);Box(level,1000,0,2000,1000);level.Polygons[0].FloorHeight=-128;level.Pave();
            level.Polygons[0].FloorTexture=(ShapeDescriptor)123;level.Polygons[1].CeilingTexture=(ShapeDescriptor)456;
            for(int i=0;i<level.Sides.Count;i++)level.Sides[i].Primary.Texture=(ShapeDescriptor)(100+i);
            level.NewObject(P(200,300),0);level.Objects[0].Type=ObjectType.Player;level.Objects[0].Facing=30;
            level.Annotations.Add(new Annotation{X=1200,Y=300,PolygonIndex=1,Text="copy"});
            var editor=Editor(level);editor.Tool=Tool.Select;editor.Selection.Polygons.UnionWith(new short[]{0,1});string before=CanonicalSnapshot(level);
            Check(editor.CopySelection(out string reason),reason);Check(editor.BeginPaste(out reason),reason);
            editor.MovePaste(5000,5000);Check(editor.PastePreviewLines.Count()==7 && editor.PastePreviewPoints.Count()==8,"Preview missed geometry/contents");
            Check(CanonicalSnapshot(level)==before && editor.HistoryLabels.Count==1,"Copy/preview changed map/history");
            Check(editor.PasteSelection(5000,5000,out reason),reason);Validate(level);
            Check(level.Polygons.Count==4 && level.Endpoints.Count==12 && level.Lines.Count==14,"Clipboard duplicated shared vertices/lines");
            Check(level.Polygons[2].FloorHeight==-128 && level.Polygons[2].FloorTexture.Equals((ShapeDescriptor)123) && level.Polygons[3].CeilingTexture.Equals((ShapeDescriptor)456),"Polygon settings lost");
            Check(level.Sides.Skip(7).Select(x=>x.Primary.Texture).SequenceEqual(level.Sides.Take(7).Select(x=>x.Primary.Texture)),"Wall textures lost");
            Check(level.Objects[1].X==4200 && level.Objects[1].Y==4800 && level.Objects[1].PolygonIndex==2 && level.Objects[1].Facing==level.Objects[0].Facing,"Object not copied/remapped");
            Check(level.Annotations[1].X==5200 && level.Annotations[1].PolygonIndex==3 && level.Annotations[1].Text=="copy","Note not copied/remapped");
            Check(editor.HistoryLabels.Count==2 && editor.Selection.Polygons.SetEquals(new short[]{2,3}) && !editor.Pasting,"Paste history/selection wrong");
            string after=CanonicalSnapshot(level);editor.Undo();Check(CanonicalSnapshot(level)==before,"Paste undo failed");editor.Redo();Check(CanonicalSnapshot(level)==after,"Paste redo failed");
            var loaded=new Level();loaded.Load(level.Save().Clone());Validate(loaded);Check(loaded.Objects.Count==2 && loaded.Annotations.Count==2,"Paste save/load lost contents");
        });
        Test("Clipboard snapshots survive edits repeat paste and reuse adjacent edges", () => {
            var level=new Level();Box(level,0,0,1000,1000);var editor=Editor(level);editor.Tool=Tool.Select;editor.Selection.Polygon=0;
            level.Polygons[0].FloorHeight=128;Check(editor.CopySelection(out string reason),reason);level.Polygons[0].FloorHeight=256;
            Check(editor.PasteSelection(1500,500,out reason),reason);Validate(level);
            Check(level.Endpoints.Count==6 && level.Lines.Count==7 && level.Polygons[1].FloorHeight==128,"Adjacent paste failed reuse or snapshot");
            Check(editor.PasteSelection(2500,500,out reason),reason);Validate(level);Check(level.Polygons.Count==3 && level.Endpoints.Count==8 && level.Lines.Count==10,"Repeat paste lost original clipboard");
            string before=CanonicalSnapshot(level);int history=editor.HistoryLabels.Count;
            Check(!editor.PasteSelection(500,500,out reason) && !editor.PasteSelection(1000,500,out reason),"Overlap accepted");
            Check(!editor.PasteSelection(32760,500,out reason),"Coordinate overflow accepted");
            Check(CanonicalSnapshot(level)==before && editor.HistoryLabels.Count==history,"Failed paste changed map/history");
            Check(editor.BeginPaste(out reason),reason);editor.CancelPaste();Check(!editor.Pasting && CanonicalSnapshot(level)==before,"Cancel changed map");
        });
        Test("Clipboard supports individual vertices lines objects and annotations", () => {
            var level=new Level();Box(level,0,0,1000,1000);level.NewObject(P(200,200),0);level.Annotations.Add(new Annotation{X=200,Y=200,PolygonIndex=0,Text="note"});
            var editor=Editor(level);editor.Tool=Tool.Select;
            Check(!editor.CopySelection(out string reason) && !editor.BeginPaste(out reason),"Empty clipboard accepted");
            editor.Selection.Object=0;Check(editor.CopySelection(out reason) && editor.PasteSelection(400,400,out reason),reason);
            Check(level.Objects.Count==2 && level.Objects[1].PolygonIndex==0 && level.Objects[1].X==400 && editor.Selection.Object==1,"Single object copy failed");
            editor.Selection.Clear();editor.Selection.Annotation=0;Check(editor.CopySelection(out reason) && editor.PasteSelection(600,600,out reason),reason);
            Check(level.Annotations.Count==2 && level.Annotations[1].PolygonIndex==0,"Single note copy failed");
            editor.Selection.Clear();editor.Selection.Line=0;Check(editor.CopySelection(out reason),reason);Check(editor.PasteSelection(3000,3000,out reason),reason);
            Check(editor.Selection.Lines.Count==1 && editor.Selection.Points.Count==2 && level.Polygons.Count==1,"Line copy made a polygon");Validate(level);
            editor.Selection.Clear();editor.Selection.Point=0;Check(editor.CopySelection(out reason) && editor.PasteSelection(-3000,-3000,out reason),reason);Validate(level);
            int count=editor.HistoryLabels.Count;string before=CanonicalSnapshot(level);Check(!editor.PasteSelection(-3000,-3000,out reason),"Duplicate vertex paste accepted");
            Check(CanonicalSnapshot(level)==before && editor.HistoryLabels.Count==count,"Duplicate vertex paste changed history");
            editor.Selection.Clear();editor.Selection.Object=0;Check(editor.CopySelection(out reason),reason);Check(!editor.PasteSelection(-5000,-5000,out reason),"Object outside polygon accepted");
            editor.ClearClipboard();Check(!editor.BeginPaste(out reason),"Cleared clipboard retained data");
        });
        Test("Clipboard mouse placement cancels and does not start a selection drag", () => {
            var level=new Level();Box(level,0,0,1000,1000);var editor=Editor(level);editor.Tool=Tool.Select;editor.Selection.Polygon=0;
            Check(editor.CopySelection(out string reason) && editor.BeginPaste(out reason),reason);
            editor.ButtonPress(5000,5000,EditorModifiers.None);editor.Motion(6000,6000,EditorModifiers.None);editor.ButtonRelease(6000,6000,EditorModifiers.None);
            Check(level.Polygons.Count==2 && !editor.DrawingSelectionBox && editor.Selection.Polygons.Contains(1),"Placement leaked into selection gesture");
            string before=CanonicalSnapshot(level);Check(editor.BeginPaste(out reason),reason);editor.ButtonPress(7000,7000,EditorModifiers.RightClick);editor.ButtonRelease(7000,7000,EditorModifiers.RightClick);
            Check(!editor.Pasting && CanonicalSnapshot(level)==before,"Right click changed map instead of cancelling");
            Check(editor.BeginPaste(out reason),reason);editor.Tool=Tool.Line;editor.LineSplitAll=true;Check(!editor.Pasting,"Tool switch retained paste preview");
        });
        Test("Clipboard remaps platforms teleports switches and preserves settings", () => {
            var level=new Level();Box(level,0,0,1000,1000);Box(level,1000,0,2000,1000);level.Pave();
            level.Polygons[0].Type=PolygonType.Platform;level.Polygons[0].Permutation=0;
            level.Platforms.Add(new Platform{PolygonIndex=0,Speed=32,Delay=60,Tag=7,MinimumHeight=-512,MaximumHeight=1024,InitiallyActive=true});
            level.Polygons[1].Type=PolygonType.Teleporter;level.Polygons[1].Permutation=0;
            var sourceSide=level.Sides.First(side=>side.PolygonIndex==1);sourceSide.IsControlPanel=true;sourceSide.ControlPanelType=(short)Array.IndexOf(Side.ControlPanelClasses,ControlPanelClass.PlatformSwitch);sourceSide.ControlPanelPermutation=0;
            var editor=Editor(level);editor.Tool=Tool.Select;editor.Selection.Polygons.UnionWith(new short[]{0,1});
            Check(editor.CopySelection(out string reason) && editor.PasteSelection(5000,5000,out reason),reason);Validate(level);
            Check(level.Platforms.Count==2 && level.Polygons[2].Permutation==1 && level.Platforms[1].PolygonIndex==2 && level.Platforms[1].Speed==32 && level.Platforms[1].InitiallyActive && level.Platforms[1].Tag==7,"Platform settings/reference lost");
            Check(level.Polygons[3].Permutation==2 && level.Sides.Any(side=>side.PolygonIndex==3 && side.IsControlPanel && side.ControlPanelPermutation==2),"Internal teleport/switch reference lost");
            var loaded=new Level();loaded.Load(level.Save().Clone());Validate(loaded);Check(loaded.Platforms[1].PolygonIndex==2 && loaded.Polygons[3].Permutation==2,"Serialized reference lost");
        });
        Test("Split-all line crosses separated polygons and connects the full path", () => {
            foreach(bool reverse in new[]{false,true}) {
                var level=new Level();Box(level,0,0,1000,1000);Box(level,2000,0,3000,1000);level.Pave();
                level.Polygons[0].FloorHeight=128;level.Polygons[1].CeilingHeight=2048;
                foreach(var side in level.Sides)side.Primary.Texture=(ShapeDescriptor)123;
                level.NewObject(P(250,250),0);level.NewObject(P(250,750),0);
                level.Annotations.Add(new Annotation{X=2500,Y=750,PolygonIndex=1,Text="cut"});
                var editor=Editor(level);editor.Tool=Tool.Line;editor.LineSplitAll=true;string before=CanonicalSnapshot(level);
                short start=(short)(reverse?3500:-500),end=(short)(reverse?-500:3500);
                editor.ButtonPress(start,500,EditorModifiers.None);editor.Motion(end,500,EditorModifiers.None);editor.ButtonRelease(end,500,EditorModifiers.None);Validate(level);
                Check(editor.LineToolError==null && level.Polygons.Count==4 && level.Endpoints.Count==14,"Split-all missed polygons/vertices: "+editor.LineToolError);
                var path=level.Lines.Where(l=>l.EndpointIndexes.All(i=>level.Endpoints[i].Y==500)).ToList();
                Check(path.Count==5,"Full path through gaps/exterior not connected");
                Check(level.Polygons.Count(p=>p.FloorHeight==128)==2 && level.Polygons.Count(p=>p.CeilingHeight==2048)==2,"Settings lost");
                Check(level.Sides.All(side=>side.Primary.Texture.Equals((ShapeDescriptor)123)),"Wall textures lost");
                Check(level.Objects[0].PolygonIndex!=level.Objects[1].PolygonIndex && level.Annotations[0].PolygonIndex>=0,"Contents not reassigned");
                Check(editor.HistoryLabels.Count==2 && editor.HistoryLabels[1]=="Split intersected polygons","Cut not one history step");
                string after=CanonicalSnapshot(level);editor.Undo();Check(CanonicalSnapshot(level)==before,"Split-all undo failed");editor.Redo();Check(CanonicalSnapshot(level)==after,"Split-all redo failed");
                var loaded=new Level();loaded.Load(level.Save().Clone());Validate(loaded);Check(loaded.Polygons.Count==4,"Split-all serialization lost polygons");
                int count=editor.HistoryLabels.Count;editor.ButtonPress(start,500,EditorModifiers.None);editor.ButtonRelease(end,500,EditorModifiers.None);
                Check(CanonicalSnapshot(level)==after && editor.HistoryLabels.Count==count,"Repeated path duplicated geometry/history");
            }
        });
        Test("Split-all line handles shared walls corner hits and loose lines", () => {
            var level=new Level();Box(level,0,0,1000,1000);Box(level,1000,0,2000,1000);
            level.NewLine(level.NewPoint(2500,0),level.NewPoint(2500,1000));
            Check(level.CutPolygonsWithLine(P(-500,500),P(3000,500),out bool handled,out string reason,true) && handled,reason);Validate(level);
            Check(level.Polygons.Count==4 && level.Endpoints.Count(p=>p.X==1000 && p.Y==500)==1 && level.Endpoints.Count(p=>p.X==2500 && p.Y==500)==1,"Shared or loose crossing not split once");
            Check(level.Lines.Count(l=>l.EndpointIndexes.All(i=>level.Endpoints[i].X==2500))==2,"Loose line not split");
            var corner=new Level();Box(corner,0,0,1000,1000);
            Check(corner.CutPolygonsWithLine(P(-500,-500),P(1500,1500),out handled,out reason,true),reason);Validate(corner);
            Check(corner.Polygons.Count==2 && corner.Endpoints.Count==6,"Corner cut duplicated vertices");
        });
        Test("Split-all line rejects incomplete paths and repairs fractional crossings", () => {
            var level=new Level();Box(level,0,0,1000,1000);var editor=Editor(level);editor.Tool=Tool.Line;editor.LineSplitAll=true;string before=CanonicalSnapshot(level);
            editor.ButtonPress(-500,500,EditorModifiers.None);editor.ButtonRelease(500,500,EditorModifiers.None);
            Check(editor.LineToolError!=null && CanonicalSnapshot(level)==before && editor.HistoryLabels.Count==1,"Partial cut changed map/history");
            var fractional=new Level();Box(fractional,0,0,1000,1000);
            Check(fractional.CutPolygonsWithLine(P(-500,0),P(1500,777),out bool handled,out string reason,true),reason);Validate(fractional);
            Check(fractional.Polygons.Count==2 && fractional.Endpoints.Contains(P(0,194)) && fractional.Endpoints.Contains(P(1000,583)),"Fractional edge hits not snapped to nearest coordinates");
            editor.StartLine(-500,500);editor.UpdateLine(1500,500,false,false);editor.LineSplitAll=false;
            Check(level.TemporaryLineStartIndex==-1 && CanonicalSnapshot(level)==before && !editor.CanUndo,"Changing option left a partial cut");
            editor.ButtonPress(-500,500,EditorModifiers.None);editor.ButtonRelease(1500,500,EditorModifiers.None);
            Check(editor.LineToolError==null && level.Polygons.Count==1,"Disabled option rejected ordinary crossing line");
        });
        Test("Split-all line reuses collinear boundaries and splits paths without polygons", () => {
            var level=new Level();Box(level,0,0,1000,1000);
            Check(level.CutPolygonsWithLine(P(-500,0),P(1500,0),out bool handled,out string reason,true),reason);Validate(level);
            Check(level.Polygons.Count==1 && level.Endpoints.Count==6 && level.Lines.Count==6,"Boundary path duplicated geometry");
            var loose=new Level();loose.NewLine(loose.NewPoint(0,-500),loose.NewPoint(0,500));loose.NewPoint(500,0);
            Check(loose.CutPolygonsWithLine(P(-1000,0),P(1000,0),out handled,out reason,true),reason);Validate(loose);
            Check(loose.Lines.Count==5 && loose.Endpoints.Count(p=>p.X==0 && p.Y==0)==1,"Loose path missed crossings or existing point");
        });
        Test("Split-all line handles eight-sided limits and rolls back late failures", () => {
            var corners=new[]{P(0,300),P(300,0),P(700,0),P(1000,300),P(1000,700),P(700,1000),P(300,1000),P(0,700)}.ToList();
            var level=new Level();Check(level.CreateCirclePolygon(corners,out string reason),reason);string before=CanonicalSnapshot(level);
            Check(!level.CutPolygonsWithLine(P(500,0),P(1500,0),out bool handled,out reason,true) && CanonicalSnapshot(level)==before,"Eight-vertex boundary overflow mutated map");
            Check(level.CutPolygonsWithLine(P(-500,500),P(1500,500),out handled,out reason,true),reason);Validate(level);Check(level.Polygons.Count==2,"Octagon not split");
            var late=new Level();Box(late,0,0,1000,1000);
            Check(late.CreateCirclePolygon(corners.Select(p=>P(p.X+2000,p.Y+500)).ToList(),out reason),reason);
            var editor=Editor(late);editor.Tool=Tool.Line;editor.LineSplitAll=true;before=CanonicalSnapshot(late);
            editor.ButtonPress(-500,500,EditorModifiers.None);editor.ButtonRelease(2500,500,EditorModifiers.None);
            Check(editor.LineToolError!=null && CanonicalSnapshot(late)==before && editor.HistoryLabels.Count==1,"Late loose-line failure retained polygon cuts");
            editor.StartLine(-500,500);editor.UpdateLine(1500,500,false,false);editor.CancelLine();
            Check(late.TemporaryLineStartIndex==-1 && CanonicalSnapshot(late)==before && editor.HistoryLabels.Count==1,"Cancel left pending vertices or history");
        });
        Test("Automatic cut snapping shares sloped edges and is drag-direction independent", () => {
            foreach(bool reverse in new[]{false,true}) {
                var level=new Level();
                Check(level.CreateCirclePolygon(new[]{P(0,0),P(1000,0),P(1100,1000),P(0,1000)}.ToList(),out string reason),reason);
                Check(level.CreateCirclePolygon(new[]{P(1000,0),P(2000,0),P(2000,1000),P(1100,1000)}.ToList(),out reason),reason);
                Point a=P(-500,333),b=P(2500,779);
                Check(level.CutPolygonsWithLine(reverse?b:a,reverse?a:b,out bool handled,out reason,true),reason);Validate(level);
                Check(level.Polygons.Count==4 && level.Endpoints.Count(p=>p.Equals(P(1056,564)))==1,"Sloped shared edge snapped inconsistently");
                var loaded=new Level();loaded.Load(level.Save().Clone());Validate(loaded);
                var halves=new Level();Box(halves,0,0,1000,1000);Box(halves,1000,0,2000,1000);a=P(0,300);b=P(2000,701);
                Check(halves.CutPolygonsWithLine(reverse?b:a,reverse?a:b,out handled,out reason),reason);Validate(halves);
                Check(halves.Endpoints.Count(p=>p.Equals(P(1000,501)))==1 && halves.Polygons.Count==4,"Half-coordinate tie or standard-mode cut failed");
            }
        });
        Test("Automatic cut snapping survives many diagonal paths with undo and no crossings", () => {
            var random=new Random(9821);
            for(int sample=0;sample<40;sample++) {
                var level=new Level();Box(level,0,0,1000,1000);Box(level,1000,0,2000,1000);Box(level,2000,0,3000,1000);
                var editor=Editor(level);editor.Tool=Tool.Line;editor.LineSplitAll=true;string before=CanonicalSnapshot(level);
                short y0=(short)random.Next(100,900),y1=(short)random.Next(100,900);
                editor.ButtonPress(-500,y0,EditorModifiers.None);editor.ButtonRelease(3500,y1,EditorModifiers.None);Validate(level);
                Check(editor.LineToolError==null && level.Polygons.Count==6,"Diagonal snapping failed: "+editor.LineToolError);
                long Cross(Point a,Point b,Point c)=>(long)(b.X-a.X)*(c.Y-a.Y)-(long)(b.Y-a.Y)*(c.X-a.X);
                for(int i=0;i<level.Lines.Count;i++)for(int j=i+1;j<level.Lines.Count;j++) {
                    var one=level.Lines[i];var two=level.Lines[j];Point a=level.Endpoints[one.EndpointIndexes[0]],b=level.Endpoints[one.EndpointIndexes[1]],c=level.Endpoints[two.EndpointIndexes[0]],d=level.Endpoints[two.EndpointIndexes[1]];
                    Check(!(Math.Sign(Cross(a,b,c))*Math.Sign(Cross(a,b,d))<0 && Math.Sign(Cross(c,d,a))*Math.Sign(Cross(c,d,b))<0),"Adjusted segments cross existing edges");
                }
                string after=CanonicalSnapshot(level);editor.Undo();Check(CanonicalSnapshot(level)==before,"Automatic snapping undo failed");editor.Redo();Check(CanonicalSnapshot(level)==after,"Automatic snapping redo failed");
            }
        });
        Test("Automatic cut snapping handles fractional loose-line hits and existing corner precision", () => {
            var level=new Level();level.NewLine(level.NewPoint(100,-100),level.NewPoint(100,100));
            Check(level.CutPolygonsWithLine(P(-100,0),P(300,101),out bool handled,out string reason,true),reason);Validate(level);
            Check(level.Endpoints.Count(p=>p.Equals(P(100,51)))==1 && level.Lines.Count==4,"Fractional loose line was not split at the nearest integer point");
            var corner=new Level();Box(corner,0,0,10,10);
            Check(corner.CutPolygonsWithLine(P(-10,-9),P(10,11),out handled,out reason,true),reason);Validate(corner);
            Check(corner.Polygons.All(p=>p.VertexCount>=3),"Automatic corner snap created a degenerate polygon");
            var unsafePath=new Level();unsafePath.NewLine(unsafePath.NewPoint(0,0),unsafePath.NewPoint(1,100));
            unsafePath.NewLine(unsafePath.NewPoint(-50,75),unsafePath.NewPoint(-20,75));string before=CanonicalSnapshot(unsafePath);
            Check(unsafePath.CutPolygonsWithLine(P(-100,50),P(100,50),out handled,out reason,true),reason);Validate(unsafePath);
            Check(unsafePath.Endpoints.Contains(P(1,50)),"Sparse loose edge intersection jumped to an endpoint");
        });
        Test("Precise diagonal cuts round intersections without lattice detours", () => {
            var level=new Level();Check(level.CreateCirclePolygon(new[]{P(0,0),P(1000,0),P(1100,1000),P(0,1000)}.ToList(),out string reason),reason);
            var editor=Editor(level);editor.Tool=Tool.Line;editor.LineSplitAll=true;string before=CanonicalSnapshot(level);
            editor.ButtonPress(-500,333,EditorModifiers.None);editor.ButtonRelease(2500,779,EditorModifiers.None);
            Check(editor.LineToolError==null && level.Polygons.Count==2 && level.Endpoints.Contains(P(1056,564)),"Default diagonal precision failed: "+editor.LineToolError);Validate(level);
            string after=CanonicalSnapshot(level);editor.Undo();Check(CanonicalSnapshot(level)==before,"Rounded diagonal undo failed");editor.Redo();Check(CanonicalSnapshot(level)==after,"Rounded diagonal redo failed");
            var dense=new Level();Box(dense,0,0,1000,1000);
            Check(dense.CutPolygonsWithLine(P(-500,0),P(1500,777),out bool handled,out reason,true),reason);Validate(dense);
            Check(dense.Endpoints.Contains(P(0,194)) && dense.Endpoints.Contains(P(1000,583)),"Precise small fractional correction failed");
        });
        Test("Sparse sloping shared edges retain textures topology and history", () => {
            foreach(int shift in new[]{0,-2000}) foreach(bool reverse in new[]{false,true}) {
                var level=new Level();
                Check(level.CreateCirclePolygon(new[]{P(shift,shift),P(shift+1000,shift),P(shift+1001,shift+1000),P(shift,shift+1000)}.ToList(),out string reason),reason);
                Check(level.CreateCirclePolygon(new[]{P(shift+1000,shift),P(shift+2000,shift),P(shift+2000,shift+1000),P(shift+1001,shift+1000)}.ToList(),out reason),reason);
                level.Pave();foreach(var side in level.Sides)side.Primary.Texture=(ShapeDescriptor)123;
                var editor=Editor(level);editor.Tool=Tool.Line;editor.LineSplitAll=true;
                string before=CanonicalSnapshot(level);Point a=P(shift-500,shift+333),b=P(shift+2500,shift+779);
                editor.ButtonPress(reverse?b.X:a.X,reverse?b.Y:a.Y,EditorModifiers.None);
                editor.ButtonRelease(reverse?a.X:b.X,reverse?a.Y:b.Y,EditorModifiers.None);
                Check(editor.LineToolError==null,"Sparse diagonal failed: "+editor.LineToolError);Validate(level);
                Check(level.Polygons.Count==4 && level.Endpoints.Count(p=>p.Equals(P(shift+1001,shift+556)))==1,"Sparse crossing detoured or duplicated");
                Check(level.Sides.Count>0 && level.Sides.All(side=>side.Primary.Texture.Equals((ShapeDescriptor)123)),"Rounded walls lost textures");
                string after=CanonicalSnapshot(level);editor.Undo();Check(CanonicalSnapshot(level)==before,"Sparse diagonal undo failed");editor.Redo();Check(CanonicalSnapshot(level)==after,"Sparse diagonal redo failed");
                var loaded=new Level();loaded.Load(level.Save().Clone());Validate(loaded);Check(CanonicalSnapshot(loaded)==after,"Sparse diagonal serialization changed geometry");
            }
        });
        Test("Line shares selection hover and snap targeting during preview and commit", () => {
            var level=new Level();Box(level,0,0,1000,1000);var editor=Editor(level);editor.DefaultSnapDistance=8;
            foreach(Point point in new[]{P(3,3),P(997,500),P(500,500)}) {
                editor.Tool=Tool.Select;editor.UpdateHover(point.X,point.Y);
                short vertex=editor.Hover.Point,line=editor.Hover.Line,polygon=editor.Hover.Polygon;
                editor.Tool=Tool.Line;editor.LineSplitAll=true;editor.UpdateHover(point.X,point.Y);
                Check(editor.Hover.Point==vertex && editor.Hover.Line==line && editor.Hover.Polygon==polygon,"Line hover differs from selection");
            }
            editor.StartLine(3,3);Check(level.Endpoints[level.TemporaryLineStartIndex].Equals(P(0,0)),"Line start missed hovered vertex");
            editor.UpdateLine(997,500,false,false);Check(level.TemporaryLineEnd.Equals(P(1000,500)) && editor.Hover.Line!=-1,"Preview missed hovered edge");
            editor.ConnectLine(997,500,false,false);Check(editor.LineToolError==null && level.Polygons.Count==2,"Snapped cut failed: "+editor.LineToolError);Validate(level);
            foreach(bool reverse in new[]{false,true}) {
                var sparse=new Level();Check(sparse.CreateCirclePolygon(new[]{P(0,0),P(1000,0),P(1001,1000),P(0,1000)}.ToList(),out string reason),reason);
                sparse.Pave();foreach(var side in sparse.Sides)side.Primary.Texture=(ShapeDescriptor)123;
                var e=Editor(sparse);e.Tool=Tool.Line;e.LineSplitAll=true;string before=CanonicalSnapshot(sparse);
                Point a=P(-3,300),b=P(1003,700);
                e.ButtonPress(reverse?b.X:a.X,reverse?b.Y:a.Y,EditorModifiers.None);
                e.Motion(reverse?a.X:b.X,reverse?a.Y:b.Y,EditorModifiers.None);
                Point preview=sparse.TemporaryLineEnd;Check(preview.Equals(reverse?P(0,300):P(1001,700)),"Sparse edge did not project precisely");
                e.ButtonRelease(reverse?a.X:b.X,reverse?a.Y:b.Y,EditorModifiers.None);
                Check(e.LineToolError==null && sparse.Polygons.Count==2,"Sparse edge attachment failed: "+e.LineToolError);Validate(sparse);
                Check(sparse.Endpoints.Contains(preview) && sparse.Sides.All(side=>side.Primary.Texture.Equals((ShapeDescriptor)123)),"Committed endpoint or wall texture differs from preview");
                string after=CanonicalSnapshot(sparse);e.Undo();Check(CanonicalSnapshot(sparse)==before,"Snapped attachment undo failed");e.Redo();Check(CanonicalSnapshot(sparse)==after,"Snapped attachment redo failed");
                var loaded=new Level();loaded.Load(sparse.Save().Clone());Validate(loaded);
            }
        });
        Test("Dense group movement preserves shared geometry contents and one undo step", () => {
            foreach(Tool tool in new[]{Tool.Move,Tool.Select}) {
                Check(CircleTessellation.TryBuild(P(0,0),P(7680,7680),4,512,out var tiles,out string reason),reason);
                var empty=new Level();Check(empty.PlanCircleTessellation(tiles,out var level,out _,out _,out reason),reason);
                level.NewObject(P(256,256),0);level.Annotations.Add(new Annotation{X=256,Y=256,PolygonIndex=0,Text="move"});
                var editor=Editor(level);editor.Tool=tool;editor.Selection.Polygons.UnionWith(Enumerable.Range(0,256).Select(i=>(short)i));
                var original=level.Endpoints.ToArray();string before=CanonicalSnapshot(level);
                editor.ButtonPress(300,300,EditorModifiers.None);
                for(int i=0;i<100;i++)editor.Motion((short)(300+i%40),(short)(300+i%30),EditorModifiers.None);
                editor.ButtonRelease(444,544,EditorModifiers.None);Validate(level);
                Check(level.Endpoints.Select((p,i)=>p.Equals(P(original[i].X+144,original[i].Y+244))).All(v=>v),"Dense group endpoints drifted");
                Check(level.Objects[0].X==400 && level.Objects[0].Y==500 && level.Annotations[0].X==400 && level.Annotations[0].Y==500,"Dense group contents left behind");
                Check(editor.HistoryLabels.Count==2,"Dense move created extra undo steps");
                string after=CanonicalSnapshot(level);editor.Undo();Check(CanonicalSnapshot(level)==before,"Dense move undo failed");editor.Redo();Check(CanonicalSnapshot(level)==after,"Dense move redo failed");Validate(level);
            }
        });
        Test("Polygon Lua bindings round trip preserve other Lua and support undo", () => {
            var level=new Level();Box(level,0,0,1000,1000);Box(level,1000,0,2000,1000);
            var user=new EmbeddedLuaScript{Name="User script",Flags=2,Text="function custom_entry(player, polygon) end"};
            byte[] userBytes=user.Source.ToArray();level.Chunks[EmbeddedLua.Tag]=EmbeddedLua.Write(new[]{user});
            var editor=Editor(level);editor.Tool=Tool.Select;editor.Selection.Polygons.UnionWith(new short[]{0,1});string before=CanonicalSnapshot(level);
            Check(editor.SetSelectedPolygonLua("custom_entry(player, polygon) -- ö\n",true,true,out string reason),reason);
            Check(level.Polygons.All(p=>p.LuaTriggerEnabled && p.LuaTriggerOnce && p.LuaTriggerCode.Contains("ö")),"Trigger properties missing");
            var scripts=EmbeddedLua.Read(level.Save().Chunks[EmbeddedLua.Tag]);Check(scripts.Count==2 && scripts[0].Flags==2 && scripts[0].Source.SequenceEqual(userBytes) && PolygonLua.IsGenerated(scripts[1]),"Other Lua lost or generated dispatcher missing");
            Check(scripts[1].Text.Contains("actions[0]") && scripts[1].Text.Contains("actions[1]"),"Generated polygon indexes incorrect");
            var loaded=new Level();loaded.Load(level.Save().Clone());Validate(loaded);Check(loaded.Polygons[1].LuaTriggerCode==level.Polygons[1].LuaTriggerCode,"Trigger metadata lost on save/load");
            string after=CanonicalSnapshot(level);editor.Undo();Check(CanonicalSnapshot(level)==before,"Trigger undo failed");editor.Redo();Check(CanonicalSnapshot(level)==after,"Trigger redo failed");
            Check(editor.SetSelectedPolygonLua(level.Polygons[0].LuaTriggerCode,false,true,out reason),reason);
            Check(!EmbeddedLua.Read(level.Save().Chunks[EmbeddedLua.Tag]).Any(PolygonLua.IsGenerated) && level.Polygons.All(p=>!p.LuaTriggerEnabled && p.LuaTriggerCode.Contains("custom_entry")),"Disabling erased code or retained dispatcher");
            Check(editor.SetSelectedPolygonLua("",false,false,out reason),reason);Check(!level.Save().Chunks.ContainsKey(PolygonLua.Tag) && EmbeddedLua.Read(level.Chunks[EmbeddedLua.Tag]).Count==1,"Removing triggers removed user script or retained metadata");
        });
        Test("Polygon Lua follows renumbering splitting clipboard and visual mode", () => {
            var level=new Level();Box(level,0,0,1000,1000);Box(level,2000,0,3000,1000);
            level.Polygons[1].LuaTriggerCode="player:print('entered')";level.Polygons[1].LuaTriggerEnabled=true;
            level.DeletePolygon(0);Check(level.Polygons[0].LuaTriggerEnabled && PolygonLua.Generate(level).Contains("actions[0]"),"Polygon deletion broke binding");
            Check(level.CutPolygonsWithLine(P(2000,500),P(3000,500),out bool handled,out string reason),reason);
            Check(level.Polygons.Count==2 && level.Polygons.All(p=>p.LuaTriggerEnabled && p.LuaTriggerCode.Contains("entered")),"Split did not inherit trigger");
            var editor=Editor(level);editor.Tool=Tool.Select;editor.Selection.Polygons.UnionWith(new short[]{0,1});
            Check(editor.CopySelection(out reason) && editor.PasteSelection(7000,5000,out reason),reason);Validate(level);
            Check(level.Polygons.Count==4 && level.Polygons.All(p=>p.LuaTriggerEnabled),"Clipboard lost trigger properties");
            var original=level.Save().Clone();var temporary=EmbeddedLua.ForVisualMode(original,"-- locator");
            Check(EmbeddedLua.Read(temporary.Chunks[EmbeddedLua.Tag]).Count==2 && EmbeddedLua.Read(temporary.Chunks[EmbeddedLua.Tag]).Count(PolygonLua.IsGenerated)==1,"Visual mode lost gameplay trigger");
            temporary.Chunks.Remove(PolygonLua.Tag);EmbeddedLua.RestoreAfterVisualMode(original,temporary);
            var loaded=new Level();loaded.Load(temporary);Check(loaded.Polygons.All(p=>p.LuaTriggerEnabled),"Visual mode lost trigger metadata");
            Check(EmbeddedLua.Read(loaded.Save().Chunks[EmbeddedLua.Tag]).Count(PolygonLua.IsGenerated)==1,"Visual return duplicated dispatcher");
        });
        Test("Polygon Lua rejects malformed metadata and invalid edits atomically", () => {
            var level=new Level();Box(level,0,0,1000,1000);var editor=Editor(level);editor.Selection.Polygon=0;string before=CanonicalSnapshot(level);
            Check(!editor.SetSelectedPolygonLua("",true,false,out string reason) && reason!=null && CanonicalSnapshot(level)==before && !editor.CanUndo,"Empty active trigger accepted");
            Check(!editor.SetSelectedPolygonLua("bad\0code",true,false,out reason) && CanonicalSnapshot(level)==before,"Null code changed map");
            level.Chunks[EmbeddedLua.Tag]=EmbeddedLua.Write(new[]{new EmbeddedLuaScript{Name=PolygonLua.ScriptName,Text="-- User script with reserved name"}});before=CanonicalSnapshot(level);
            Check(!editor.SetSelectedPolygonLua("player:print('test')",true,false,out reason) && CanonicalSnapshot(level)==before,"Reserved-name collision overwrote script");
            level.Chunks.Remove(EmbeddedLua.Tag);level.Polygons[0].LuaTriggerCode="test()";level.Polygons[0].LuaTriggerEnabled=true;var wad=level.Save().Clone();
            byte[] metadata=wad.Chunks[PolygonLua.Tag];
            foreach(byte[] invalid in new[]{metadata.Take(metadata.Length-1).ToArray(),new byte[]{0,2,0,0},metadata.Concat(new byte[]{0}).ToArray()}) {
                wad.Chunks[PolygonLua.Tag]=invalid;bool rejected=false;try { new Level().Load(wad.Clone()); }catch(System.IO.InvalidDataException) { rejected=true; }Check(rejected,"Bad trigger metadata accepted");
            }
        });
        Test("Visual mode restores scripted light definitions and keeps added lights", () => {
            var original=new Wadfile.DirectoryEntry();
            original.Chunks[EmbeddedLua.Tag]=EmbeddedLua.Write(new[]{new EmbeddedLuaScript{Name="Test",Text="-- script"}});
            original.Chunks[Light.Tag]=new byte[]{1,2,3};
            var returned=new Wadfile.DirectoryEntry();returned.Chunks[Light.Tag]=new byte[]{9,9,9,4,5};
            EmbeddedLua.RestoreAfterVisualMode(original,returned);
            Check(returned.Chunks[Light.Tag].SequenceEqual(new byte[]{1,2,3,4,5}),"Runtime changes leaked or appended lights lost");
            returned.Chunks[Light.Tag][0]=7;Check(original.Chunks[Light.Tag][0]==1,"Original lights aliased");
            original.Chunks.Remove(EmbeddedLua.Tag);EmbeddedLua.RestoreAfterVisualMode(original,returned);
            Check(returned.Chunks[Light.Tag][0]==7,"Unscripted light edit lost");
            string smooth=PolygonLuaActions.Generate("lights_dim",new System.Collections.Generic.Dictionary<string,double>{{"seconds",2}});
            Check(smooth.Contains("state.light_function = \"smooth\"") && smooth.Contains("2 * 30"),"Missing native smooth transition");
        });
        Test("Polygon Lua action catalog parameters quoting and preload generation", () => {
            Check(PolygonLuaActions.Catalog.Select(a=>a.Id).Distinct().Count()==PolygonLuaActions.Catalog.Length,"Duplicate action IDs");
            foreach(var action in PolygonLuaActions.Catalog) {
                string code=PolygonLuaActions.Generate(action.Id);PolygonLua.Validate(code);
                Check(code.StartsWith("\n-- ") && code.EndsWith("end\n") && !string.IsNullOrWhiteSpace(action.Category),"Action cannot be composed: "+action.Id);
            }
            Check(PolygonLuaActions.Generate("message",text:"Quotes: \" \\ newline\nö").Contains("\\010"),"Message control characters not escaped");
            bool rejected=false;try { PolygonLuaActions.Generate("ammo_item",new System.Collections.Generic.Dictionary<string,double>{{"item",10}}); }catch(ArgumentException) { rejected=true; }Check(rejected,"Non-ammo item accepted");
            rejected=false;try { PolygonLuaActions.Generate("slow_heal",new System.Collections.Generic.Dictionary<string,double>{{"rate",double.NaN}}); }catch(ArgumentException) { rejected=true; }Check(rejected,"Invalid heal rate accepted");
            var level=new Level();Box(level,0,0,1000,1000);
            level.Polygons[0].LuaTriggerEnabled=true;level.Polygons[0].LuaTriggerCode=PolygonLuaActions.Generate("spawn",new System.Collections.Generic.Dictionary<string,double>{{"collection",9}})+PolygonLuaActions.Generate("spawn",new System.Collections.Generic.Dictionary<string,double>{{"collection",9}});
            string generated=PolygonLua.Generate(level);Check(generated.Split(new[]{"table.insert(CollectionsUsed, 9)"},StringSplitOptions.None).Length==2,"Monster preload missing or duplicated");
            Check(generated.IndexOf("table.insert(CollectionsUsed")<generated.IndexOf("actions[0]"),"Monster collection loaded inside action instead of startup");
            level.Polygons[0].LuaTriggerEnabled=false;Check(!PolygonLua.Generate(level).Contains("table.insert(CollectionsUsed"),"Disabled spawn still preloads collection");
        });
        Test("Spawn presets upgrade without changing custom scripts and persist repaired coordinates", () => {
            string legacy = @"-- Spawn monsters
do
-- Forge Ahead preload collection:9
local target = Polygons[0]
if target and MonsterTypes[8] then
  for i = 1, 2 do
    local monster = Monsters.new(target.x, target.y, 0, target, 8)
    if monster then monster.active = true else player:print('Unable to spawn monster'); break end
  end
else player:print('Spawn polygon or monster type does not exist') end
end
";
            var parameters = new System.Collections.Generic.Dictionary<string,double>{{"collection",9},{"polygon",0},{"monster",8},{"count",2}};
            string expected = PolygonLuaActions.Generate("spawn",parameters).TrimStart('\n');
            Check(PolygonLuaActions.UpgradeSpawnPresets(legacy)==expected,"Legacy preset not repaired");
            Check(PolygonLuaActions.UpgradeSpawnPresets(legacy.Replace("target.y, 0", "target.y, target.floor.height"))==expected,"Old absolute height preset not repaired");
            Check(PolygonLuaActions.UpgradeSpawnPresets(expected)==expected,"Upgrade not idempotent");
            string custom=legacy.Replace("target.x, target.y", "target.x + 1, target.y");
            Check(PolygonLuaActions.UpgradeSpawnPresets(custom)==custom,"Custom spawn coordinates overwritten");
            Check(PolygonLuaActions.UpgradeSpawnPresets(legacy+legacy)==expected+expected,"Multiple actions not upgraded");
            var level = new Level();Box(level,3000,4000,4000,5000);
            level.Polygons[0].LuaTriggerEnabled=true;level.Polygons[0].LuaTriggerCode=legacy;
            var restored=new Level();restored.Load(level.Save().Clone());
            Check(restored.Polygons[0].LuaTriggerCode==expected,"Repaired source not saved");
            Check(EmbeddedLua.Read(restored.Chunks[EmbeddedLua.Tag]).Single(PolygonLua.IsGenerated).Text.Contains("Monsters.new(x, y, 0, target, 8)"),"Runtime source not repaired");
        });
        Test("Fog action parameters and automatic scenario monster graphics preloading", () => {
            var values=new System.Collections.Generic.Dictionary<string,double>{{"underwater",1},{"red",0.25},{"green",0.75},{"blue",1},{"depth",12.5},{"landscapes",1}};
            string fog=PolygonLuaActions.Generate("fog_set",values);
            Check(fog.Contains("Level.underwater_fog") && fog.Contains("fog.color.r = 0.25") && fog.Contains("fog.depth = 12.5") && fog.Contains("fog.active = true"),"Fog parameters lost");
            Check(!PolygonLuaActions.Generate("fog_color",values).Contains("fog.active"),"Color unexpectedly enables fog");
            Check(PolygonLuaActions.Generate("fog_off").Contains("fog.active = false"),"Fog disable missing");
            values["depth"]=0;bool rejected=false;try { PolygonLuaActions.Generate("fog_depth",values); }catch(ArgumentException) { rejected=true; }Check(rejected,"Zero fog depth accepted");
            var level=new Level();Box(level,0,0,1000,1000);var p=level.Polygons[0];
            p.LuaTriggerEnabled=true;p.LuaTriggerCode=PolygonLuaActions.Generate("spawn");
            string script=PolygonLua.Generate(level);
            Check(script.Contains("table.insert(CollectionsUsed, 9)") && script.IndexOf("table.insert(CollectionsUsed, 9)")<script.IndexOf("local actions"),"Automatic collection preload missing or too late");
            p.LuaTriggerCode+=p.LuaTriggerCode;Check(PolygonLua.Generate(level).Split(new[]{"table.insert(CollectionsUsed, 9)"},StringSplitOptions.None).Length==2,"Repeated spawn types not deduplicated");
            var restored=new Level();restored.Load(level.Save().Clone());Check(PolygonLua.Generate(restored).Contains("table.insert(CollectionsUsed, 9)"),"Preload lost on save");
            byte[] physics=new byte[47*156];physics[8*156+1]=(byte)(23 | (2<<5));level.Chunks[Wadfile.Chunk("MNpx")]=physics;
            Check(PolygonLua.SpawnCollection(level,8)==23 && PolygonLua.Generate(level).Contains("table.insert(CollectionsUsed, 23)"),"Embedded physics collection/CLUT not decoded");
            level.Chunks[Wadfile.Chunk("MNpx")]=new byte[10];Check(PolygonLua.SpawnCollection(level,8)==9,"Truncated physics not handled");
            Check(PolygonLua.SpawnCollection(level,47)==-1 && PolygonLua.SpawnCollection(level,-1)==-1,"Invalid monster accepted");
            p.LuaTriggerEnabled=false;Check(!PolygonLua.Generate(level).Contains("table.insert(CollectionsUsed"),"Disabled action preloads graphics");
        });
        Test("Named Lua choices retain IDs and fog transitions validate duration", () => {
            Check(PolygonLuaActions.NamedChoices("monster").Single(n=>n.Key==8).Value.Contains("Fighter"),"Monster ID mismatch");
            Check(PolygonLuaActions.NamedChoices("sound").Single(n=>n.Key==1).Value=="teleport in","Sound ID mismatch");
            foreach(string name in new[]{"monster","sound","item","collection"}) {
                var choices=PolygonLuaActions.NamedChoices(name).ToList();Check(choices.Count>0 && choices.Select(c=>c.Key).Distinct().Count()==choices.Count,"Invalid named choices: "+name);
            }
            bool rejected=false;try { PolygonLuaActions.Generate("fog_fade",new System.Collections.Generic.Dictionary<string,double>{{"seconds",0}}); }catch(ArgumentException) { rejected=true; }Check(rejected,"Zero transition duration accepted");
            Check(PolygonLuaActions.Generate("fog_fade",new System.Collections.Generic.Dictionary<string,double>{{"seconds",1.5}}).Contains("1.5 * 30"),"Duration not invariant");
        });
        Test("Spawner actions preload graphics and validate limits", () => {
            var level=new Level();Box(level,0,0,1000,1000);
            level.Polygons[0].LuaTriggerEnabled=true;level.Polygons[0].LuaTriggerCode=PolygonLuaActions.Generate("spawner");
            Check(PolygonLua.Generate(level).Contains("table.insert(CollectionsUsed, 9)"),"Spawner graphics not preloaded");
            Check(PolygonLuaActions.Generate("spawner",new System.Collections.Generic.Dictionary<string,double>{{"indefinite",1}}).Contains("remaining=-1"),"Indefinite limit missing");
            bool rejected=false;try { PolygonLuaActions.Generate("spawner",new System.Collections.Generic.Dictionary<string,double>{{"interval",0}}); }catch(ArgumentException) { rejected=true; }Check(rejected,"Zero spawn interval accepted");
            var restored=new Level();restored.Load(level.Save().Clone());Check(restored.Polygons[0].LuaTriggerCode==level.Polygons[0].LuaTriggerCode,"Spawner not retained");
        });
        Test("Unchecked line split draws through and inside polygons without cutting", () => {
            foreach(var ends in new[]{new[]{P(-500,500),P(1500,500)},new[]{P(200,200),P(800,800)},new[]{P(0,0),P(1000,1000)}}) {
                var level=new Level();Box(level,0,0,1000,1000);var editor=Editor(level);editor.Tool=Tool.Line;editor.LineSplitAll=false;
                string before=CanonicalSnapshot(level);int lines=level.Lines.Count;
                editor.StartLine(ends[0].X,ends[0].Y);editor.ConnectLine(ends[1].X,ends[1].Y,false,false);editor.CommitHistory(true);
                Check(editor.LineToolError==null && level.Polygons.Count==1 && level.Lines.Count==lines+1,"Unchecked split still cuts/rejects geometry: "+editor.LineToolError);
                string after=CanonicalSnapshot(level);editor.Undo();Check(CanonicalSnapshot(level)==before,"Plain line undo failed");editor.Redo();Check(CanonicalSnapshot(level)==after,"Plain line redo failed");
                var loaded=new Level();loaded.Load(level.Save().Clone());Check(loaded.Polygons.Count==1 && loaded.Lines.Count==lines+1,"Plain line not saved");
            }
        });
        Test("Delayed and conditional actions preserve wrapped scripts and preloads", () => {
            string spawn=PolygonLuaActions.Generate("spawn");
            string delayed=PolygonLuaActions.Generate("delay",body:spawn);
            Check(delayed.Contains("-- Forge Ahead preload monster:8"),"Wrapped spawn lost preload");
            var level=new Level();Box(level,0,0,1000,1000);level.Polygons[0].LuaTriggerEnabled=true;level.Polygons[0].LuaTriggerCode=delayed;
            Check(PolygonLua.Generate(level).Contains("table.insert(CollectionsUsed, 9)"),"Delayed graphics missing");
            Check(PolygonLuaActions.Generate("flag_if",text:"ready",body:spawn).Contains(spawn),"Conditional lost source");
            bool rejected=false;try { PolygonLuaActions.Generate("flag_set",text:" "); }catch(ArgumentException) { rejected=true; }Check(rejected,"Empty flag accepted");
            rejected=false;try { PolygonLuaActions.Generate("waves",new System.Collections.Generic.Dictionary<string,double>{{"waves",0}}); }catch(ArgumentException) { rejected=true; }Check(rejected,"Zero waves accepted");
        });
        Console.WriteLine(passed + " test groups passed.");
    }
}
