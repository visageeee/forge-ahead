using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Weland
{
    public enum TerminalPageType : short {
        Logon, Unfinished, Success, Failure, Information, End, InterlevelTeleport,
        IntralevelTeleport, Checkpoint, Sound, Movie, Track, Picture, Logoff, Camera, Static, Tag
    }
    public struct TerminalStyle {
        public short Face, Color;
        public TerminalStyle(short face, short color) { Face = face; Color = color; }
    }
    public sealed class TerminalPage {
        public short Flags, Permutation, MaximumLines;
        public TerminalPageType Type = TerminalPageType.Information;
        public string Text = "";
        public List<TerminalStyle> Styles = new List<TerminalStyle>();
        public void SetText(string text) {
            Text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            Styles = Enumerable.Repeat(new TerminalStyle(), Text.Length).ToList();
        }
    }
    public sealed class TerminalDocument {
        public ushort Flags;
        public short LinesPerPage = 22;
        public List<TerminalPage> Pages = new List<TerminalPage>();
        public bool Dirty;
        internal byte[] Original;
        public bool EnsureEntryBranch() {
            // The engine enters a completion branch, not the first display page.
            // Leave explicitly branched and Marathon 1 terminals in their original order.
            if (Pages.Count == 0 || Pages.Any(p => p.Type == TerminalPageType.Unfinished ||
                p.Type == TerminalPageType.Success || p.Type == TerminalPageType.Failure) || (Pages[0].Flags & 0x100) != 0) return false;
            Pages.Insert(0,new TerminalPage { Type = TerminalPageType.Unfinished });
            if (Pages[Pages.Count - 1].Type != TerminalPageType.End)
                Pages.Add(new TerminalPage { Type = TerminalPageType.End, MaximumLines = 1 });
            Dirty = true; return true;
        }
        public static TerminalDocument Create() {
            var document = new TerminalDocument { Dirty = true };
            document.Pages.Add(new TerminalPage { Type = TerminalPageType.Unfinished });
            var page = new TerminalPage { MaximumLines = 2 }; page.SetText("Incoming message…\n"); document.Pages.Add(page);
            document.Pages.Add(new TerminalPage { Type = TerminalPageType.End, MaximumLines = 1 });
            return document;
        }
    }
    public static class TerminalCodec {
        public static readonly uint Tag = Wadfile.Chunk("term");
        static Encoding Mac => Encoding.GetEncoding(10000, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
        public static void Crypt(byte[] text) {
            int end = text.Length / 4 * 4;
            for (int i = 0; i < end; i += 4) { text[i+2] ^= 0xfe; text[i+3] ^= 0xed; }
            for (int i = end; i < text.Length; ++i) text[i] ^= 0xfe;
        }
        public static List<TerminalDocument> Read(byte[] chunk) {
            var result = new List<TerminalDocument>();
            if (chunk == null) return result;
            using var stream = new MemoryStream(chunk, false); using var reader = new BinaryReaderBE(stream);
            while (stream.Position < stream.Length) {
                long start = stream.Position;
                if (stream.Length - start < 10) throw new InvalidDataException("Truncated terminal header.");
                int size = reader.ReadUInt16();
                var document = new TerminalDocument { Flags = reader.ReadUInt16(), LinesPerPage = reader.ReadInt16() };
                int groups = reader.ReadUInt16(), fonts = reader.ReadUInt16();
                int header = 10 + groups * 12 + fonts * 6;
                if (size < header || size > stream.Length - start) throw new InvalidDataException("Invalid terminal length.");
                var ranges = new List<(short Start, short Length)>();
                for (int i = 0; i < groups; ++i) {
                    var page = new TerminalPage { Flags = reader.ReadInt16(), Type = (TerminalPageType)reader.ReadInt16(), Permutation = reader.ReadInt16() };
                    ranges.Add((reader.ReadInt16(), reader.ReadInt16())); page.MaximumLines = reader.ReadInt16(); document.Pages.Add(page);
                }
                var changes = new List<(short Index, TerminalStyle Style)>();
                for (int i = 0; i < fonts; ++i) changes.Add((reader.ReadInt16(), new TerminalStyle(reader.ReadInt16(), reader.ReadInt16())));
                byte[] text = reader.ReadBytes(size - header);
                if ((document.Flags & 1) != 0) Crypt(text);
                var styles = new TerminalStyle[text.Length]; int cursor = 0; var style = new TerminalStyle();
                foreach (var change in changes) {
                    if (change.Index < cursor || change.Index > text.Length) throw new InvalidDataException("Invalid terminal style offset.");
                    while (cursor < change.Index) styles[cursor++] = style;
                    style = change.Style;
                }
                while (cursor < text.Length) styles[cursor++] = style;
                for (int i = 0; i < groups; ++i) {
                    var range = ranges[i];
                    if (range.Start < 0 || range.Length < 0 || range.Start + range.Length > text.Length)
                        throw new InvalidDataException("Invalid terminal page text offset.");
                    int length = range.Length;
                    for (int j = 0; j < length; ++j) if (text[range.Start+j] == 0) { length = j; break; }
                    var page = document.Pages[i];
                    page.Text = Mac.GetString(text, range.Start, length).Replace('\r', '\n');
                    page.Styles = styles.Skip(range.Start).Take(length).ToList();
                }
                document.Original = chunk.Skip((int)start).Take(size).ToArray(); result.Add(document);
            }
            return result;
        }
        public static byte[] Write(IList<TerminalDocument> documents) {
            using var output = new MemoryStream();
            foreach (var document in documents) {
                if (!document.Dirty && document.Original != null) { output.Write(document.Original); continue; }
                using var text = new MemoryStream();
                var ranges = new List<(short Start, short Length)>();
                var changes = new List<(short Index, TerminalStyle Style)>();
                foreach (var page in document.Pages) {
                    if (page.Text.Contains('\0')) throw new ArgumentException("Terminal text cannot contain null characters.");
                    byte[] bytes = Mac.GetBytes(page.Text.Replace('\n', '\r'));
                    if (page.Styles.Count != bytes.Length) throw new ArgumentException("Terminal formatting does not match text length.");
                    if (text.Length + bytes.Length + 1 > short.MaxValue) throw new ArgumentException("A terminal's text must fit in 32767 bytes.");
                    ranges.Add(((short)text.Length, (short)bytes.Length));
                    var previous = new TerminalStyle(-1,-1);
                    for (int j = 0; j < bytes.Length; ++j) {
                        var style = page.Styles[j];
                        if (style.Face != previous.Face || style.Color != previous.Color) {
                            changes.Add(((short)(text.Length+j), style)); previous = style;
                        }
                    }
                    text.Write(bytes); text.WriteByte(0);
                }
                int size = 10 + document.Pages.Count * 12 + changes.Count * 6 + (int)text.Length;
                if (size > ushort.MaxValue || document.Pages.Count > short.MaxValue || changes.Count > short.MaxValue)
                    throw new ArgumentException("Terminal exceeds Aleph One's size limit. Split it into smaller terminals.");
                using var stream = new MemoryStream(); using var writer = new BinaryWriterBE(stream);
                writer.Write((ushort)size); writer.Write(document.Flags); writer.Write(document.LinesPerPage);
                writer.Write((ushort)document.Pages.Count); writer.Write((ushort)changes.Count);
                for (int i = 0; i < document.Pages.Count; ++i) {
                    var p = document.Pages[i]; writer.Write(p.Flags); writer.Write((short)p.Type); writer.Write(p.Permutation);
                    writer.Write(ranges[i].Start); writer.Write(ranges[i].Length);
                    short lines = p.Type == TerminalPageType.Unfinished || p.Type == TerminalPageType.Success || p.Type == TerminalPageType.Failure ? (short)0 :
                        p.Type == TerminalPageType.Information || p.Type == TerminalPageType.Picture || p.Type == TerminalPageType.Checkpoint ? p.MaximumLines : (short)1;
                    writer.Write(lines);
                }
                foreach (var change in changes) { writer.Write(change.Index); writer.Write(change.Style.Face); writer.Write(change.Style.Color); }
                byte[] body = text.ToArray(); if ((document.Flags & 1) != 0) Crypt(body); writer.Write(body);
                output.Write(stream.ToArray());
            }
            return output.ToArray();
        }
    }
}
