using System;
using System.IO;

namespace Weland
{
    // Terminal pictures live in the map's resource fork or indexed WAD entries.
    // Formats follow Aleph One's RenderOther/images.cpp and classic Resource Manager layout.
    public sealed class TerminalPicture
    {
        public byte[] Data, ColorTable;
        public bool WindowsBitmap;

        public static TerminalPicture FromWad(Wadfile file, int id)
        {
            if (!file.Directory.TryGetValue(id, out var entry)) return null;
            if (entry.Chunks.TryGetValue(Wadfile.Chunk("PICT"), out var pict)) return new TerminalPicture { Data = pict };
            if (!entry.Chunks.TryGetValue(Wadfile.Chunk("pict"), out pict)) return null;
            entry.Chunks.TryGetValue(Wadfile.Chunk("clut"), out var colors);
            return new TerminalPicture { Data = pict, ColorTable = colors, WindowsBitmap = true };
        }

        public static TerminalPicture FromFile(string path, int id)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            byte[] bytes = File.ReadAllBytes(path);
            var picture = FromResourceFork(bytes, id);
            if (picture != null) return picture;
            // MacBinary: the resource fork follows the padded data fork.
            if (bytes.Length >= 128 && bytes[0] == 0 && bytes[1] > 0 && bytes[1] <= 63 && bytes[2] != 0 && bytes[74] == 0) {
                long start = 128L + ((Read32(bytes, 83) + 127L) & ~127L);
                long length = Read32(bytes, 87);
                if (length > 0 && start + length <= bytes.Length) {
                    picture = FromResourceFork(Slice(bytes, (int)start, (int)length), id);
                    if (picture != null) return picture;
                }
            }
            // AppleDouble sidecar (or an AppleSingle file): entry 2 is the resource fork.
            picture = FromAppleDouble(bytes, id);
            if (picture != null) return picture;
            string sidecar = Path.Combine(Path.GetDirectoryName(path) ?? "", "._" + Path.GetFileName(path));
            if (File.Exists(sidecar)) {
                picture = FromAppleDouble(File.ReadAllBytes(sidecar), id);
                if (picture != null) return picture;
            }
            // Reject unrelated files before the general WAD loader sees their header.
            int wadStart = bytes.Length >= 128 && bytes[0] == 0 && bytes[1] > 0 && bytes[1] <= 63 && bytes[2] != 0 && bytes[74] == 0 ? 128 : 0;
            if (bytes.Length < wadStart + 128 || Read16(bytes, wadStart) < 2 || Read16(bytes, wadStart + 80) != 16 || Read16(bytes, wadStart + 82) != 10) return null;
            try { var wad = new Wadfile(); wad.Load(path); return FromWad(wad, id); }
            catch (Wadfile.BadMapException) { return null; }
            catch (EndOfStreamException) { return null; }
        }

        static TerminalPicture FromAppleDouble(byte[] bytes, int id)
        {
            if (bytes.Length < 26 || (Read32(bytes, 0) != 0x00051607 && Read32(bytes, 0) != 0x00051600)) return null;
            int count = Read16(bytes, 24);
            for (int i = 0, at = 26; i < count; ++i, at += 12) {
                Require(bytes, at, 12);
                if (Read32(bytes, at) != 2) continue;
                return FromResourceFork(Slice(bytes, checked((int)Read32(bytes, at + 4)), checked((int)Read32(bytes, at + 8))), id);
            }
            return null;
        }

        public static TerminalPicture FromResourceFork(byte[] bytes, int id)
        {
            if (bytes.Length < 16) return null;
            long data = Read32(bytes, 0), map = Read32(bytes, 4), dataLength = Read32(bytes, 8), mapLength = Read32(bytes, 12);
            if (data < 16 || map < 16 || mapLength < 28 || data + dataLength > bytes.Length || map + mapLength > bytes.Length) return null;
            int types = checked((int)map + Read16(bytes, (int)map + 24));
            int count = Read16(bytes, types) + 1;
            if (count == 65536) return null; // -1 denotes an empty type list.
            for (int i = 0, at = types + 2; i < count; ++i, at += 8) {
                Require(bytes, at, 8);
                if (Read32(bytes, at) != Wadfile.Chunk("PICT")) continue;
                int resources = Read16(bytes, at + 4) + 1, refs = types + Read16(bytes, at + 6);
                for (int j = 0; j < resources; ++j, refs += 12) {
                    Require(bytes, refs, 12);
                    if ((short)Read16(bytes, refs) != id) continue;
                    int offset = checked((int)data + (int)(Read32(bytes, refs + 4) & 0xffffff));
                    int length = checked((int)Read32(bytes, offset));
                    if ((long)offset + 4 + length > data + dataLength) throw new InvalidDataException("Picture exceeds resource data.");
                    return new TerminalPicture { Data = Slice(bytes, offset + 4, length) };
                }
            }
            return null;
        }

        static int Read16(byte[] data, int at) { Require(data, at, 2); return data[at] << 8 | data[at + 1]; }
        static uint Read32(byte[] data, int at) { Require(data, at, 4); return (uint)(data[at] << 24 | data[at + 1] << 16 | data[at + 2] << 8 | data[at + 3]); }
        static void Require(byte[] data, int at, int length) {
            if (at < 0 || length < 0 || (long)at + length > data.Length) throw new InvalidDataException("Truncated picture resource.");
        }
        static byte[] Slice(byte[] bytes, int at, int length) { Require(bytes, at, length); var result = new byte[length]; Buffer.BlockCopy(bytes, at, result, 0, length); return result; }

        public Image Decode()
        {
            if (WindowsBitmap) return DecodeWindows();
            var reader = new PictureReader(Data); reader.Skip(10);
            while (reader.Position < Data.Length) {
                if ((reader.Position & 1) != 0) reader.Skip(1);
                int op = reader.Word();
                if (op == 0xff) break;
                if (op == 0x90 || op == 0x91 || op == 0x98 || op == 0x99 || op == 0x9a || op == 0x9b) return DecodeBitmap(reader, op);
                switch (op) {
                    case 0: case 0x11: case 0x1c: case 0x1e: case 0x38: case 0x39: case 0x3a: case 0x3b: case 0x3c: case 0x2ff: break;
                    case 1: int size = reader.Word(); if (size < 2) throw new InvalidDataException("Invalid clipping region."); reader.Skip(size - 2); break;
                    case 3: case 4: case 5: case 8: case 0xd: case 0x15: case 0x16: case 0x23: case 0xa0: reader.Skip(2); break;
                    case 6: case 7: case 0xb: case 0xc: case 0xe: case 0xf: case 0x21: reader.Skip(4); break;
                    case 0x1a: case 0x1b: case 0x1d: case 0x1f: case 0x22: reader.Skip(6); break;
                    case 2: case 9: case 0xa: case 0x10: case 0x20: case 0x30: case 0x31: case 0x32: case 0x33: case 0x34: reader.Skip(8); break;
                    case 0xc00: reader.Skip(24); break;
                    case 0xa1: reader.Skip(2); reader.Skip(reader.Word()); break;
                    default:
                        if (op >= 0x300 && op < 0x8000) reader.Skip((op >> 8) * 2);
                        else if (op < 0x8000 || op >= 0x8100) throw new NotSupportedException("Unsupported PICT opcode 0x" + op.ToString("x4") + ".");
                        break;
                }
            }
            throw new InvalidDataException("No bitmap found in picture resource.");
        }

        Image DecodeWindows()
        {
            int height = Read16(Data, 4) - (short)Read16(Data, 0), width = Read16(Data, 6) - (short)Read16(Data, 2), depth = Read16(Data, 8);
            if (depth != 8 && depth != 16) throw new NotSupportedException("Unsupported Windows picture depth.");
            var image = NewImage(width, height); int bytes = depth / 8;
            Require(Data, 10, checked(width * height * bytes));
            if (depth == 8 && (ColorTable == null || ColorTable.Length != 1542)) throw new InvalidDataException("Picture color table is missing.");
            for (int i = 0; i < width * height; ++i) {
                if (depth == 8) for (int c = 0; c < 3; ++c) image.Data[i * 3 + c] = ColorTable[6 + Data[10 + i] * 6 + c * 2];
                else RGB16(image.Data, i * 3, Read16(Data, 10 + i * 2));
            }
            return image;
        }

        static Image NewImage(int width, int height) {
            if (width <= 0 || height <= 0 || width > 8192 || height > 8192 || (long)width * height > 16777216)
                throw new InvalidDataException("Invalid or excessively large picture dimensions.");
            return new Image(width, height);
        }
        static void RGB16(byte[] rgb, int at, int pixel) {
            rgb[at] = (byte)(((pixel >> 10) & 31) * 255 / 31); rgb[at + 1] = (byte)(((pixel >> 5) & 31) * 255 / 31); rgb[at + 2] = (byte)((pixel & 31) * 255 / 31);
        }

        static Image DecodeBitmap(PictureReader r, int op)
        {
            bool direct = op == 0x9a || op == 0x9b;
            if (direct) r.Skip(4);
            int row = r.Word(); bool pixmap = (row & 0x8000) != 0; row &= 0x3fff;
            int top = (short)r.Word(), left = (short)r.Word(); int height = (short)r.Word() - top, width = (short)r.Word() - left;
            var image = NewImage(width, height);
            int packing = 0, depth = 1, components = 1;
            if (pixmap) { r.Skip(2); packing = r.Word(); r.Skip(14); depth = r.Word(); components = r.Word(); r.Skip(14); }
            if (depth != 1 && depth != 2 && depth != 4 && depth != 8 && depth != 16 && depth != 32) throw new NotSupportedException("Unsupported picture depth.");
            if (row < (width * depth + 7) / 8) throw new InvalidDataException("Invalid picture row size.");
            var colors = new byte[256 * 3]; colors[0] = colors[1] = colors[2] = 255;
            if (pixmap && !direct) {
                r.Skip(4); int flags = r.Word(), count = r.Word() + 1;
                if (count > 256) throw new InvalidDataException("Invalid picture palette.");
                for (int i = 0; i < count; ++i) {
                    int index = r.Word(); if ((flags & 0x8000) != 0) index = i;
                    if (index > 255) throw new InvalidDataException("Invalid palette index.");
                    for (int c = 0; c < 3; ++c) colors[index * 3 + c] = (byte)(r.Word() >> 8);
                }
            }
            r.Skip(18);
            if ((op & 1) != 0) { int size = r.Word(); if (size < 2) throw new InvalidDataException("Invalid bitmap region."); r.Skip(size - 2); }
            if (packing == 0 && depth > 8) packing = depth == 16 ? 3 : 4;
            bool packed = row >= 8 && packing != 1 && op != 0x90 && op != 0x91;
            bool planar = depth == 32 && packed && packing == 4;
            if (packed && depth > 8 && packing != 3 && packing != 4) throw new NotSupportedException("Unsupported picture packing.");
            if (planar && components != 3 && components != 4) throw new InvalidDataException("Invalid picture components.");
            for (int y = 0; y < height; ++y) {
                int expected = planar ? row / 4 * components : row;
                byte[] scan;
                if (!packed) scan = r.Bytes(row);
                else { int length = row > 250 ? r.Word() : r.Byte(); scan = Unpack(r.Bytes(length), expected, depth == 16 ? 2 : 1); }
                for (int x = 0; x < width; ++x) {
                    int at = (y * width + x) * 3;
                    if (depth <= 8) {
                        int bit = x * depth, index = (scan[bit / 8] >> (8 - depth - bit % 8)) & ((1 << depth) - 1);
                        Buffer.BlockCopy(colors, index * 3, image.Data, at, 3);
                    } else if (depth == 16) RGB16(image.Data, at, scan[x * 2] << 8 | scan[x * 2 + 1]);
                    else for (int c = 0; c < 3; ++c) image.Data[at + c] = planar ? scan[(components - 3 + c) * (row / 4) + x] : scan[x * 4 + c + 1];
                }
            }
            return image;
        }

        static byte[] Unpack(byte[] source, int size, int unit)
        {
            var dest = new byte[size]; int at = 0, output = 0;
            while (at < source.Length) {
                int code = (sbyte)source[at++]; if (code == -128) continue;
                int count = (code >= 0 ? code + 1 : 1 - code) * unit;
                if (output + count > size) throw new InvalidDataException("Picture scanline overflows.");
                Require(source, at, code >= 0 ? count : unit);
                if (code >= 0) { Buffer.BlockCopy(source, at, dest, output, count); at += count; }
                else { for (int i = 0; i < count; ++i) dest[output + i] = source[at + i % unit]; at += unit; }
                output += count;
            }
            if (output != size) throw new InvalidDataException("Incomplete picture scanline.");
            return dest;
        }

        sealed class PictureReader
        {
            readonly byte[] data; public int Position;
            public PictureReader(byte[] value) { data = value; }
            public int Byte() { Require(data, Position, 1); return data[Position++]; }
            public int Word() { int value = Read16(data, Position); Position += 2; return value; }
            public void Skip(int count) { Require(data, Position, count); Position += count; }
            public byte[] Bytes(int count) { var value = Slice(data, Position, count); Position += count; return value; }
        }
    }
}
