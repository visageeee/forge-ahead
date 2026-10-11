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
        DefaultSnapDistance = 8, Tool = Tool.Box
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
        Test("line tool cuts edge to edge and vertex to vertex with undo", () => {
            foreach(bool diagonal in new[]{false,true}) {
                var level=new Level();Box(level,0,0,1000,1000);var editor=Editor(level);editor.Tool=Tool.Line;
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
            var level=new Level();Box(level,0,0,1000,1000);var editor=Editor(level);editor.Tool=Tool.Line;string before=CanonicalSnapshot(level);
            editor.StartLine(0,500);editor.ConnectLine(500,500,false,false);
            Check(CanonicalSnapshot(level)==before && !editor.CanUndo && editor.LineToolError!=null,"Incomplete cut changed map or history");
            editor.StartLine(0,500);editor.ConnectLine(0,500,false,false);Check(CanonicalSnapshot(level)==before,"Cancelled line left edge vertices");
        });
        Test("line tool attaches outside polygons and reuses existing boundary segments", () => {
            var level=new Level();Box(level,0,0,1000,1000);var editor=Editor(level);editor.Tool=Tool.Line;
            editor.StartLine(0,500);editor.ConnectLine(-500,500,false,false);editor.CommitHistory(true);
            Check(level.Polygons.Count==1 && level.Polygons[0].VertexCount==5 && level.Endpoints.Count==6,"Outside attachment failed");Validate(level);
            editor.StartLine(1000,250);editor.ConnectLine(1000,750,false,false);editor.CommitHistory(true);Validate(level);
            Check(level.Polygons.Count==1,"Boundary segment unnecessarily split polygon");
            int lines=level.Lines.Count,points=level.Endpoints.Count;editor.StartLine(1000,250);editor.ConnectLine(1000,750,false,false);
            Check(level.Lines.Count==lines && level.Endpoints.Count==points,"Redrawing boundary duplicated geometry");
        });
        Test("line tool cuts an eight-sided polygon and cancels on tool change", () => {
            var level=new Level();Check(level.CreateCirclePolygon(new System.Collections.Generic.List<Point>{P(0,300),P(300,0),P(700,0),P(1000,300),P(1000,700),P(700,1000),P(300,1000),P(0,700)},out string reason),reason);
            var editor=Editor(level);editor.Tool=Tool.Line;string before=CanonicalSnapshot(level);
            editor.StartLine(0,500);editor.Tool=Tool.Select;
            Check(CanonicalSnapshot(level)==before && !editor.CanUndo,"Tool change left unfinished line geometry");
            editor.Tool=Tool.Line;editor.StartLine(0,500);editor.ConnectLine(1000,500,false,false);
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
        Test("embedded Lua protected during visual mode", () => {
            var level = new Level(); Box(level,0,0,100,100);
            byte[] bytes = EmbeddedLua.Write(new[] { new EmbeddedLuaScript { Name = "kill-floor.lua", Text = "Triggers = {}\n" } });
            level.Chunks[EmbeddedLua.Tag] = bytes;
            var original = level.Save().Clone();
            var editing = EmbeddedLua.ForVisualMode(original, "Triggers = {}\n");
            Check(EmbeddedLua.Read(editing.Chunks[EmbeddedLua.Tag]).Single().Name == "Weland Visual Mode Locator", "Gameplay script runs in visual mode");
            Check(original.Chunks[EmbeddedLua.Tag].SequenceEqual(bytes) && level.Chunks[EmbeddedLua.Tag].SequenceEqual(bytes), "Preparing visual mode changed map Lua");
            EmbeddedLua.RestoreAfterVisualMode(original, editing);
            Check(editing.Chunks[EmbeddedLua.Tag].SequenceEqual(bytes), "Returning lost gameplay scripts");
            editing.Chunks[EmbeddedLua.Tag][0] = 7;
            Check(original.Chunks[EmbeddedLua.Tag][0] == 0, "Returned Lua shares mutable storage");
            Check(!EmbeddedLua.ForVisualMode(original, null).Chunks.ContainsKey(EmbeddedLua.Tag), "No-locator visual mode kept gameplay Lua");
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
            foreach (Tool tool in new[] {Tool.Select, Tool.Pan, Tool.Zoom, Tool.Box, Tool.Circle}) {
                var level=new Level();Box(level,0,0,1000,1000);var editor=Editor(level);editor.Tool=tool;editor.Selection.Point=0;
                editor.ButtonPress(500,500,EditorModifiers.RightClick);
                editor.Motion(520,530,EditorModifiers.RightClick);
                editor.ButtonRelease(525,535,EditorModifiers.RightClick);
                Check(editor.Tool==tool && editor.Selection.Point==0 && level.Endpoints[0].Equals(P(25,35)),"Right drag changed tool/selection or failed to move");
                editor.Undo();Check(level.Endpoints[0].Equals(P(0,0)),"Right drag did not undo as one action");
                Check(!editor.DrawingBox && !editor.DrawingCircle,"Right drag started drawing");
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
            editor.Tool=Tool.Line;editor.ButtonPress(2000,2000,EditorModifiers.None);editor.Undo();
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
                editor.BoxFilled=false;editor.CircleFilled=false;editor.CircleTessellationEnabled=true;editor.CircleSides=6;editor.CircleTileSize=512;editor.SaveSettings();
                var loaded=Editor(new Level());
                Check(!loaded.BoxFilled && !loaded.CircleFilled && loaded.CircleTessellationEnabled && loaded.CircleSides==6 && loaded.CircleTileSize==512,"In-memory settings failed to round trip");
                Weland.Weland.Settings=new Settings(false);
                var clean=Editor(new Level());Check(clean.BoxFilled && clean.CircleFilled && !clean.CircleTessellationEnabled,"Fresh test settings inherited earlier choices");
            } finally {Weland.Weland.Settings=original;}
        });
        Console.WriteLine(passed + " test groups passed.");
    }
}
