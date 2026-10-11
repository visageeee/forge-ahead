using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Weland
{
    // Keep imported resources in level snapshots for undo, then export standard PICT WADs.
    public static class TerminalPictureImports
    {
        public static readonly uint Tag = Wadfile.Chunk("TPIC");
        static readonly uint ManagedTag = Wadfile.Chunk("TOWN");
        public static SortedDictionary<int, byte[]> Read(Wadfile.DirectoryEntry level) =>
            Read(level.Chunks.TryGetValue(Tag, out var bytes) ? bytes : null);
        public static SortedDictionary<int, byte[]> Read(byte[] bytes)
        {
            var result = new SortedDictionary<int, byte[]>();
            if (bytes == null) return result;
            using var stream = new MemoryStream(bytes); using var reader = new BinaryReaderBE(stream);
            if (bytes.Length < 4 || reader.ReadUInt16() != 1) throw new InvalidDataException("Invalid imported picture header.");
            int count = reader.ReadUInt16();
            for (int i = 0; i < count; ++i) {
                if (stream.Length - stream.Position < 6) throw new InvalidDataException("Truncated imported picture.");
                int id = reader.ReadInt16(), length = reader.ReadInt32();
                if (id < 1 || length < 10 || length > stream.Length - stream.Position || result.ContainsKey(id))
                    throw new InvalidDataException("Invalid imported picture ID or length.");
                result.Add(id,reader.ReadBytes(length));
            }
            if (stream.Position != stream.Length) throw new InvalidDataException("Unexpected imported picture data.");
            return result;
        }
        public static byte[] Write(IDictionary<int, byte[]> pictures)
        {
            using var stream = new MemoryStream(); using var writer = new BinaryWriterBE(stream);
            if (pictures.Count > short.MaxValue) throw new ArgumentException("Too many imported pictures.");
            writer.Write((ushort)1); writer.Write((ushort)pictures.Count);
            foreach (var pair in pictures.OrderBy(p => p.Key)) {
                if (pair.Key < 1 || pair.Key > short.MaxValue || pair.Value == null || pair.Value.Length < 10)
                    throw new ArgumentException("Invalid imported picture.");
                writer.Write((short)pair.Key); writer.Write(pair.Value.Length); writer.Write(pair.Value);
            }
            return stream.ToArray();
        }
        public static int NextId(IEnumerable<int> used)
        {
            var ids = new HashSet<int>(used);
            for (int i = 1000; i <= short.MaxValue; ++i) {
                // Reserve higher-color variants as well as the base resource ID.
                if (!ids.Contains(i) && !ids.Contains(i + 10000) && !ids.Contains(i + 20000) &&
                    !ids.Contains(i - 10000) && !ids.Contains(i - 20000)) return i;
            }
            throw new InvalidOperationException("No unused terminal picture IDs remain.");
        }
        public static int LevelCount(Wadfile file) => file.Directory.Values.Count(e => e.Chunks.ContainsKey(MapInfo.Tag));
        public static MapFile Export(Wadfile.DirectoryEntry level, Wadfile source)
        {
            var output = new MapFile(); output.Directory[0] = level.Clone();
            foreach (var pair in source.Directory) {
                if (!pair.Value.Chunks.ContainsKey(MapInfo.Tag) && !pair.Value.Chunks.ContainsKey(ManagedTag) && pair.Key != 0)
                    output.Directory[pair.Key] = pair.Value.Clone();
            }
            foreach (var pair in Read(level)) {
                if (output.Directory.ContainsKey(pair.Key)) throw new InvalidDataException("Imported picture ID conflicts with an existing resource: " + pair.Key);
                var entry = new Wadfile.DirectoryEntry(); entry.Chunks[Wadfile.Chunk("PICT")] = pair.Value;
                entry.Chunks[ManagedTag] = new byte[] {1}; output.Directory[pair.Key] = entry;
            }
            foreach (var pair in output.Directory) {
                var overlay = new MapFile.Overlay();
                if (pair.Value.Chunks.TryGetValue(MapInfo.Tag,out var bytes)) {
                    using var reader = new BinaryReaderBE(new MemoryStream(bytes)); var info = new MapInfo(); info.Load(reader);
                    overlay.LevelName = info.Name; overlay.MissionFlags = info.MissionFlags;
                    overlay.EnvironmentFlags = info.EnvironmentFlags; overlay.EntryPointFlags = info.EntryPointFlags;
                } else overlay.LevelName = "";
                output.Overlays[pair.Key] = overlay;
            }
            return output;
        }
        public static void RestoreAfterVisualMode(Wadfile.DirectoryEntry original, Wadfile.DirectoryEntry returned)
        {
            if (original.Chunks.TryGetValue(Tag,out var bytes)) returned.Chunks[Tag] = (byte[])bytes.Clone();
            else returned.Chunks.Remove(Tag);
        }
        public static byte[] Encode(Image image)
        {
            if (image.Width < 1 || image.Height < 1 || image.Width > 4095 || image.Height > 8192 || image.Data.Length != image.Width * image.Height * 3)
                throw new ArgumentException("Invalid imported image dimensions.");
            using var stream = new MemoryStream(); using var writer = new BinaryWriterBE(stream);
            void W(int value) => writer.Write((ushort)value);
            void Rect() { W(0); W(0); W(image.Height); W(image.Width); }
            W(0); Rect(); W(0x11); W(0x2ff); W(0xc00); // PICT version 2 and extended header
            writer.Write(0xfffe0000u); writer.Write(72 << 16); writer.Write(72 << 16); Rect(); writer.Write(0);
            W(0x9a); writer.Write(0); W(0x8000 | image.Width * 4); Rect();
            W(0); W(1); writer.Write(0); writer.Write(72 << 16); writer.Write(72 << 16);
            W(16); W(32); W(3); W(8); writer.Write(0); writer.Write(0); writer.Write(0);
            Rect(); Rect(); W(0);
            for (int i = 0; i < image.Data.Length; i += 3) {
                writer.Write((byte)0); writer.Write(image.Data,i,3);
            }
            W(0xff); return stream.ToArray();
        }
    }
}
