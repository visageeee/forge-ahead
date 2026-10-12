using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Weland
{
    public sealed class EmbeddedLuaScript
    {
        internal byte[] NameBytes = new byte[66];
        public uint Flags;
        public byte[] Source = Array.Empty<byte>();
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        public string Name {
            get { return Encoding.GetEncoding(10000).GetString(NameBytes).Split('\0')[0]; }
            set {
                var encoding = Encoding.GetEncoding(10000, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
                if (string.IsNullOrWhiteSpace(value) || value.Contains('\0'))
                    throw new ArgumentException("Give each script a name without null characters.");
                byte[] bytes = encoding.GetBytes(value);
                if (bytes.Length > 65) throw new ArgumentException("Script names can be at most 65 bytes long.");
                NameBytes = new byte[66]; Array.Copy(bytes, NameBytes, bytes.Length);
            }
        }
        public string Text {
            get { return Utf8.GetString(Source); }
            set {
                if (value.Contains('\0')) throw new ArgumentException("Lua source cannot contain null characters.");
                Source = Utf8.GetBytes(value);
            }
        }
    }

    public static class EmbeddedLua
    {
        public static readonly uint Tag = Wadfile.Chunk("LUAS");

        public static List<EmbeddedLuaScript> Read(byte[] chunk)
        {
            var scripts = new List<EmbeddedLuaScript>();
            if (chunk == null) return scripts;
            using var stream = new MemoryStream(chunk, false);
            using var reader = new BinaryReaderBE(stream);
            if (stream.Length < 2) throw new InvalidDataException("The embedded Lua chunk is truncated.");
            ushort count = reader.ReadUInt16();
            for (int i = 0; i < count; ++i) {
                if (stream.Length - stream.Position < 74) throw new InvalidDataException("An embedded Lua header is truncated.");
                var script = new EmbeddedLuaScript { Flags = reader.ReadUInt32(), NameBytes = reader.ReadBytes(66) };
                uint length = reader.ReadUInt32();
                if (length > stream.Length - stream.Position) throw new InvalidDataException("Embedded Lua source is truncated.");
                script.Source = reader.ReadBytes((int)length);
                scripts.Add(script);
            }
            if (stream.Position != stream.Length) throw new InvalidDataException("The embedded Lua script count does not match its contents.");
            return scripts;
        }

        public static byte[] Write(IList<EmbeddedLuaScript> scripts)
        {
            if (scripts.Count > ushort.MaxValue) throw new ArgumentException("Too many embedded Lua scripts.");
            using var stream = new MemoryStream();
            using var writer = new BinaryWriterBE(stream);
            writer.Write((ushort)scripts.Count);
            foreach (var script in scripts) {
                writer.Write(script.Flags); writer.Write(script.NameBytes);
                writer.Write((uint)script.Source.Length); writer.Write(script.Source);
            }
            return stream.ToArray();
        }

        public static Wadfile.DirectoryEntry ForVisualMode(Wadfile.DirectoryEntry original, string locator)
        {
            var temporary = original.Clone();
            if (locator != null) {
                temporary.Chunks.TryGetValue(Tag,out byte[] existing);
                var scripts=Read(existing);
                scripts.Add(new EmbeddedLuaScript {
                    Name = "Weland Visual Mode Locator", Flags = 4, Text = locator
                });
                temporary.Chunks[Tag]=Write(scripts);
            }
            return temporary;
        }

        public static string VisualModeLocator(double x,double y,double z,int polygon)
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "Triggers = Triggers or {{}}\nlocal previous_init = Triggers.init\nfunction Triggers.init(restoring)\n  if not restoring then Players[0]:position({0}, {1}, {2}, {3}) end\n  if previous_init then previous_init(restoring) end\nend\n",x,y,z,polygon);
        }

        public static void RestoreAfterVisualMode(Wadfile.DirectoryEntry original, Wadfile.DirectoryEntry returned)
        {
            if(original.Chunks.TryGetValue(PolygonLua.Tag,out byte[] triggerData))returned.Chunks[PolygonLua.Tag]=(byte[])triggerData.Clone();
            else returned.Chunks.Remove(PolygonLua.Tag);
            if (original.Chunks.TryGetValue(Tag, out byte[] bytes)) returned.Chunks[Tag] = (byte[])bytes.Clone();
            else returned.Chunks.Remove(Tag);
        }
    }
}
