using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gtk;

namespace Weland
{
    public partial class MapWindow
    {
        protected void OnTerminalEditor(object sender, EventArgs args)
        {
            using var dialog = new Dialog("Level Terminals", window1, DialogFlags.Modal,
                "Cancel", ResponseType.Cancel, "Apply", ResponseType.Accept);
            dialog.SetDefaultSize(1150, 850);
            void Error(Exception e) {
                using var message = new MessageDialog(dialog, DialogFlags.Modal, MessageType.Error, ButtonsType.Close, "Unable to edit terminals.");
                message.SecondaryText = e.Message; message.Run();
            }
            Level.Chunks.TryGetValue(TerminalCodec.Tag, out byte[] original);
            Level.Chunks.TryGetValue(TerminalPictureImports.Tag, out byte[] originalPictures);
            SortedDictionary<int, byte[]> importedPictures;
            List<TerminalDocument> documents;
            try { documents = TerminalCodec.Read(original); importedPictures = TerminalPictureImports.Read(originalPictures); } catch (Exception e) { Error(e); return; }
            var contents = new Box(Orientation.Vertical, 8) { Margin = 12 };
            contents.PackStart(new Label("Edit the formatted page directly. Select text to change its style. Apply, then save the map.\n" +
                "Set a wall's Terminal control panel ID to the terminal ID shown here.") { Xalign = 0 }, false, false, 0);
            var row = new Box(Orientation.Horizontal, 8);
            var terminal = new ComboBoxText();
            var pageRows = new ListStore(typeof(int), typeof(string));
            var pages = new TreeView(pageRows) { HeadersVisible = false, EnableSearch = false };
            pages.AppendColumn("Page order",new CellRendererText(),"text",1);
            var zoom = new ComboBoxText(); int[] zoomLevels = { 100,125,150,200,300 };
            foreach (int percent in zoomLevels) zoom.AppendText(percent + "%");
            int rememberedZoom = Weland.Settings.GetSetting("TerminalEditor/Zoom",150);
            zoom.Active = Array.IndexOf(zoomLevels,rememberedZoom); if (zoom.Active < 0) zoom.Active = 2;
            double previewZoom = zoomLevels[zoom.Active] / 100.0;
            row.PackStart(new Label("Terminal"), false, false, 0); row.PackStart(terminal, true, true, 0);
            row.PackStart(new Label("Preview zoom"), false, false, 0); row.PackStart(zoom,false,false,0);
            contents.PackStart(row, false, false, 0);
            var metadata = new Box(Orientation.Horizontal, 8);
            var type = new ComboBoxText(); foreach (string value in Enum.GetNames(typeof(TerminalPageType))) type.AppendText(value);
            var id = new SpinButton(short.MinValue, short.MaxValue, 1) { Numeric = true, WidthChars = 5 };
            var layout = new ComboBoxText(); foreach (string value in new[] { "Image left", "Image right", "Image centered" }) layout.AppendText(value);
            metadata.PackStart(new Label("Type"), false, false, 0); metadata.PackStart(type, false, false, 0);
            metadata.PackStart(new Label("Action / resource ID"), false, false, 0); metadata.PackStart(id, false, false, 0);
            metadata.PackStart(layout, false, false, 0); contents.PackStart(metadata, false, false, 0);
            var toolbar = new Box(Orientation.Horizontal, 6); contents.PackStart(toolbar, false, false, 0);
            var colors = new ComboBoxText();
            string[] palette = { "#00ff00", "#ffffff", "#ff0000", "#009c00", "#00b0c9", "#ffe700", "#af0000", "#0c00ff" };
            foreach (string value in new[] { "Green", "White", "Red", "Dark green", "Aqua", "Yellow", "Dark red", "Blue" }) colors.AppendText(value);
            colors.Active = 0;
            // Keep the engine's 640 x 320 frame even when the dialog is maximized.
            var canvas = new Fixed { WidthRequest = 640, HeightRequest = 320 };
            var screen = new EventBox { WidthRequest = 640, HeightRequest = 320, Halign = Align.Center, Valign = Align.Center };
            screen.Add(canvas);
            var heading = new Label("TERMINAL") { Xalign = 0, WidthRequest = 640, HeightRequest = 18 };
            canvas.Put(heading, 0, 0);
            var body = new Fixed { WidthRequest = 622, HeightRequest = 266 };
            var picture = new DrawingArea { WidthRequest = 307, HeightRequest = 266, NoShowAll = true };
            Gdk.Pixbuf picturePixels = null;
            var pictureStatus = new Label { Xalign = 0, Wrap = true };
            var pictureCache = new Dictionary<int, Gdk.Pixbuf>();
            var pictureErrors = new Dictionary<int, string>();
            string selectedResourceFile = Weland.Settings.GetSetting("TerminalEditor/Resources", "");
            picture.Drawn += (o,e) => {
                e.Cr.SetSourceRGB(0,0,0); e.Cr.Paint();
                if (picturePixels == null) return;
                double scale = Math.Min(previewZoom, Math.Min((double)picture.AllocatedWidth / picturePixels.Width,
                    (double)picture.AllocatedHeight / picturePixels.Height));
                e.Cr.Save();
                e.Cr.Translate((picture.AllocatedWidth - picturePixels.Width * scale) / 2,
                    (picture.AllocatedHeight - picturePixels.Height * scale) / 2);
                e.Cr.Scale(scale,scale); Gdk.CairoHelper.SetSourcePixbuf(e.Cr,picturePixels,0,0); e.Cr.Paint(); e.Cr.Restore();
            };
            var view = new TextView { WrapMode = WrapMode.WordChar, Monospace = true,
                LeftMargin = 0, RightMargin = 0, TopMargin = 0, BottomMargin = 0 };
            var scroll = new ScrolledWindow(); scroll.SetPolicy(PolicyType.Never, PolicyType.Automatic); scroll.Add(view);
            scroll.SetSizeRequest(307,266);
            body.Put(picture,0,0); body.Put(scroll,315,0);
            canvas.Put(body,9,27);
            var footer = new Label("UESC · CONNECTION ACTIVE") { Xalign = 0, WidthRequest = 640, HeightRequest = 18 };
            canvas.Put(footer,0,302);
            var css = new CssProvider();
            css.LoadFromData(".terminal-screen { background-color: #000000; } .terminal-header { background-color: #270000; color: #ff0000; font-family: monospace; font-size: 12px; } textview.terminal-page, textview.terminal-page text { background-color: #000000; color: #00ff00; font-family: monospace; font-size: 12px; } textview.terminal-page.terminal-overlay, textview.terminal-page.terminal-overlay text { background-color: transparent; }");
            view.StyleContext.AddClass("terminal-page"); view.StyleContext.AddProvider(css, 800);
            screen.StyleContext.AddClass("terminal-screen"); screen.StyleContext.AddProvider(css, 800);
            foreach (var label in new[] {heading,footer}) { label.StyleContext.AddClass("terminal-header"); label.StyleContext.AddProvider(css,800); }
            var previewScroll = new ScrolledWindow(); previewScroll.SetPolicy(PolicyType.Automatic,PolicyType.Automatic);
            previewScroll.Add(screen);
            var pageScroll = new ScrolledWindow { WidthRequest = 185 }; pageScroll.SetPolicy(PolicyType.Automatic,PolicyType.Automatic); pageScroll.Add(pages);
            var pageColumn = new Box(Orientation.Vertical,6);
            pageColumn.PackStart(new Label("Page order ↓") { Xalign = 0 },false,false,0); pageColumn.PackStart(pageScroll,true,true,0);
            var previewRow = new Box(Orientation.Horizontal,8); previewRow.PackStart(pageColumn,false,false,0); previewRow.PackStart(previewScroll,true,true,0);
            contents.PackStart(previewRow, true, true, 0);
            contents.PackStart(pictureStatus,false,false,0);
            contents.PackStart(new Label("Preview: 640 × 320 (2:1), standard colors and an approximate font. Pictures retain their aspect ratio.\n" +
                "Unfinished / Success / Failure start branches; End closes a branch. Simple terminals get Unfinished/End on Apply.\nTeleport level IDs are zero-based.") { Xalign = 0 }, false, false, 0);
            var actions = new Box(Orientation.Horizontal, 6); contents.PackStart(actions, false, false, 0);
            var tags = new TextTag[8,8];
            for (int face = 0; face < 8; ++face) for (int color = 0; color < 8; ++color) {
                var tag = new TextTag("terminal-" + face + "-" + color) { Foreground = palette[color],
                    Weight = (face & 1) != 0 ? Pango.Weight.Bold : Pango.Weight.Normal,
                    Style = (face & 2) != 0 ? Pango.Style.Italic : Pango.Style.Normal,
                    Underline = (face & 4) != 0 ? Pango.Underline.Single : Pango.Underline.None };
                view.Buffer.TagTable.Add(tag); tags[face,color] = tag;
            }
            int currentTerminal = -1, currentPage = -1;
            bool loading = false, richDirty = false;
            TerminalDocument Document() => currentTerminal < 0 ? null : documents[currentTerminal];
            TerminalPage Page() => currentPage < 0 ? null : Document().Pages[currentPage];
            TerminalStyle StyleAt(int index) {
                var iter = view.Buffer.GetIterAtOffset(index);
                for (short face = 0; face < 8; ++face) for (short color = 0; color < 8; ++color)
                    if (iter.HasTag(tags[face,color])) return new TerminalStyle(face,color);
                return new TerminalStyle();
            }
            void Flush() {
                var page = Page(); if (page == null) return;
                var newType = type.Active >= 0 ? (TerminalPageType)type.Active : page.Type;
                int oldLayout = (page.Flags & 2) != 0 ? 2 : (page.Flags & 1) != 0 ? 1 : 0;
                short newFlags = oldLayout == layout.Active ? page.Flags : (short)((page.Flags & ~3) | (layout.Active == 1 ? 1 : layout.Active == 2 ? 2 : 0));
                if (page.Type != newType || page.Permutation != id.ValueAsInt || page.Flags != newFlags) {
                    page.Type = newType; page.Permutation = (short)id.ValueAsInt; page.Flags = newFlags; Document().Dirty = true;
                }
                if (richDirty) {
                    string text = view.Buffer.Text;
                    // Validate encoding before changing the draft page.
                    var encoding = System.Text.Encoding.GetEncoding(10000, System.Text.EncoderFallback.ExceptionFallback, System.Text.DecoderFallback.ExceptionFallback);
                    encoding.GetBytes(text); if (text.Contains('\0')) throw new ArgumentException("Terminal text cannot contain null characters.");
                    var styles = new List<TerminalStyle>();
                    for (int i = 0; i < view.Buffer.CharCount; ++i) styles.Add(StyleAt(i));
                    page.Text = text; page.Styles = styles;
                    int width = newType == TerminalPageType.Information ? 70 : 35;
                    page.MaximumLines = (short)Math.Min(short.MaxValue, Math.Max(1,
                        text.Split('\n').Sum(line => Math.Max(1, (line.Length + width - 1) / width))));
                    Document().Dirty = true; richDirty = false;
                }
            }
            IEnumerable<string> ResourceFiles() {
                if (!string.IsNullOrEmpty(selectedResourceFile)) yield return selectedResourceFile;
                if (!string.IsNullOrEmpty(Filename)) yield return Filename;
                string scenario = Weland.Settings.GetSetting("VisualMode/Scenario", "");
                if (Directory.Exists(scenario)) {
                    foreach (string path in Directory.EnumerateFiles(scenario).OrderBy(p => p)) {
                        string name = Path.GetFileName(path);
                        if (name.StartsWith("._")) continue;
                        if (Path.GetExtension(path).Equals(".sceA",StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("Map",StringComparison.OrdinalIgnoreCase) || name.Equals("Images",StringComparison.OrdinalIgnoreCase) ||
                            name.Equals("Marathon 2",StringComparison.OrdinalIgnoreCase) || name.Equals("Marathon Infinity",StringComparison.OrdinalIgnoreCase)) yield return path;
                    }
                }
                string shapes = Weland.Settings.GetSetting("ShapesFile/Path", "");
                if (!string.IsNullOrEmpty(shapes)) yield return shapes;
            }
            Gdk.Pixbuf LoadPicture(int resourceId) {
                if (pictureCache.TryGetValue(resourceId,out var cached)) return cached;
                if (pictureErrors.ContainsKey(resourceId)) return null;
                string failure = "Resource not found. Select the original scenario Map file with Picture resources…";
                var resources = new List<TerminalPicture>();
                // Read the original merged map too, including its non-level picture WADs.
                if (importedPictures.TryGetValue(resourceId,out var imported)) resources.Add(new TerminalPicture { Data = imported });
                var embedded = TerminalPicture.FromWad(mapfile,resourceId);
                if (string.IsNullOrEmpty(selectedResourceFile) && embedded != null) resources.Add(embedded);
                foreach (string path in ResourceFiles().Distinct()) {
                    try {
                        var resource = TerminalPicture.FromFile(path,resourceId);
                        if (resource != null) resources.Add(resource);
                    } catch (Exception e) when (e is IOException || e is InvalidDataException || e is UnauthorizedAccessException || e is OverflowException || e is ArgumentException) {
                        failure = Path.GetFileName(path) + ": " + e.Message;
                    }
                }
                if (!string.IsNullOrEmpty(selectedResourceFile) && embedded != null) resources.Add(embedded);
                foreach (var resource in resources) {
                    try {
                        Gdk.Pixbuf pixels;
                        // Gdk handles PNG/JPEG resources used by newer scenarios; classic PICTs use our decoder.
                        byte[] data = resource.Data;
                        if (!resource.WindowsBitmap && data.Length >= 4 &&
                            ((data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4e && data[3] == 0x47) || (data[0] == 0xff && data[1] == 0xd8)))
                            pixels = new Gdk.Pixbuf(data);
                        else pixels = ImageUtilities.ImageToPixbuf(resource.Decode());
                        pictureCache[resourceId] = pixels; return pixels;
                    } catch (Exception e) when (e is IOException || e is InvalidDataException || e is NotSupportedException || e is OverflowException || e is GLib.GException) { failure = e.Message; }
                }
                pictureErrors[resourceId] = failure; return null;
            }
            int Z(int value) => (int)Math.Round(value * previewZoom);
            void Reflow() {
                bool checkpoint = type.Active == 8;
                bool logon = type.Active == 0 || type.Active == 13;
                bool hasPicture = logon || checkpoint || type.Active == 12;
                bool centered = logon || layout.Active == 2;
                picturePixels = hasPicture && !checkpoint ? LoadPicture(id.ValueAsInt) : null;
                pictureStatus.Text = checkpoint ? "Checkpoint " + id.ValueAsInt + " refers to a map goal, not a picture resource." :
                    !hasPicture ? "" : picturePixels != null ? "Picture " + id.ValueAsInt + " · " + picturePixels.Width + " × " + picturePixels.Height :
                    "Picture " + id.ValueAsInt + ": " + pictureErrors[id.ValueAsInt];
                if (centered && !logon) view.StyleContext.AddClass("terminal-overlay"); else view.StyleContext.RemoveClass("terminal-overlay");
                canvas.SetSizeRequest(Z(640),Z(320)); screen.SetSizeRequest(Z(640),Z(320));
                heading.SetSizeRequest(Z(640),Z(18)); footer.SetSizeRequest(Z(640),Z(18)); canvas.Move(footer,0,Z(302));
                body.SetSizeRequest(Z(622),Z(266)); canvas.Move(body,Z(9),Z(27));
                picture.SetSizeRequest(Z(centered ? 622 : 307),Z(logon ? 220 : 266));
                body.Move(picture,Z(!centered && layout.Active == 1 ? 315 : 0),0);
                scroll.SetSizeRequest(Z(!hasPicture ? 496 : logon ? 622 : 307),Z(logon ? 46 : 266));
                body.Move(scroll,Z(!hasPicture ? 63 : logon ? 0 : layout.Active == 1 ? 0 : 315),Z(logon ? 220 : 0));
                if (hasPicture) picture.Show(); else picture.Hide();
                picture.QueueDraw();
                heading.Text = "TERMINAL " + currentTerminal + " · " + (type.ActiveText ?? "Unknown action");
            }
            void Display() {
                loading = true;
                try {
                    var page = Page(); metadata.Sensitive = toolbar.Sensitive = view.Sensitive = page != null;
                    type.Active = page != null && (short)page.Type >= 0 && (short)page.Type < 17 ? (short)page.Type : -1;
                    id.Value = page?.Permutation ?? 0;
                    layout.Active = page == null ? 0 : (page.Flags & 2) != 0 ? 2 : (page.Flags & 1) != 0 ? 1 : 0;
                    view.Buffer.Text = page?.Text ?? "";
                    if (page != null) for (int i = 0; i < page.Styles.Count; ++i) {
                        var style = page.Styles[i];
                        if (style.Face >= 0 && style.Face < 8 && style.Color >= 0 && style.Color < 8)
                            view.Buffer.ApplyTag(tags[style.Face,style.Color], view.Buffer.GetIterAtOffset(i), view.Buffer.GetIterAtOffset(i+1));
                    }
                    richDirty = false; Reflow();
                } finally { loading = false; }
            }
            void Refresh(int t, int p) {
                loading = true;
                terminal.RemoveAll(); for (int i = 0; i < documents.Count; ++i) terminal.AppendText("ID " + i);
                currentTerminal = t; currentPage = p; terminal.Active = t;
                pageRows.Clear(); if (t >= 0) for (int i = 0; i < documents[t].Pages.Count; ++i) {
                    var page = documents[t].Pages[i];
                    bool branch = page.Type == TerminalPageType.Unfinished || page.Type == TerminalPageType.Success || page.Type == TerminalPageType.Failure;
                    pageRows.AppendValues(i,(i+1).ToString("D2") + " · " + (branch ? "Branch: " : "") + page.Type);
                }
                if (p >= 0) { var path = new TreePath(p.ToString()); pages.Selection.SelectPath(path); pages.ScrollToCell(path,null,false,0,0); }
                loading = false; Display();
            }
            void Run(System.Action action) { try { action(); } catch (Exception e) { Error(e); } }
            terminal.Changed += (o,e) => {
                if (loading) return;
                try { int target = terminal.Active; Flush(); Refresh(target, target >= 0 && documents[target].Pages.Count > 0 ? 0 : -1); }
                catch (Exception ex) { loading = true; terminal.Active = currentTerminal; loading = false; Error(ex); }
            };
            pages.Selection.Changed += (o,e) => {
                if (loading || !pages.Selection.GetSelected(out TreeIter iter)) return;
                int target = (int)pageRows.GetValue(iter,0);
                try { Flush(); Refresh(currentTerminal,target); }
                catch (Exception ex) { loading = true; if (currentPage >= 0) pages.Selection.SelectPath(new TreePath(currentPage.ToString())); loading = false; Error(ex); }
            };
            void UpdateZoom() {
                previewZoom = zoomLevels[Math.Max(0,zoom.Active)] / 100.0;
                string size = (12 * previewZoom).ToString(System.Globalization.CultureInfo.InvariantCulture) + "px";
                css.LoadFromData(".terminal-screen { background-color: #000; } .terminal-header { background-color: #270000; color: #ff0000; font-family: monospace; font-size: " + size + "; } textview.terminal-page, textview.terminal-page text { background-color: #000; color: #00ff00; font-family: monospace; font-size: " + size + "; } textview.terminal-page.terminal-overlay, textview.terminal-page.terminal-overlay text { background-color: transparent; }");
                Reflow();
            }
            zoom.Changed += (o,e) => { Weland.Settings.PutSetting("TerminalEditor/Zoom",zoomLevels[Math.Max(0,zoom.Active)]); UpdateZoom(); };
            type.Changed += (o,e) => { if (!loading) Reflow(); }; id.ValueChanged += (o,e) => { if (!loading) Reflow(); };
            layout.Changed += (o,e) => { if (!loading) Reflow(); };
            view.Buffer.Changed += (o,e) => { if (!loading) richDirty = true; };
            void Format(int bit, int color) {
                if (Page() == null || !view.Buffer.GetSelectionBounds(out TextIter start, out TextIter end)) return;
                bool set = bit != 0 && (StyleAt(start.Offset).Face & bit) == 0;
                for (int i = start.Offset; i < end.Offset; ++i) {
                    var style = StyleAt(i); if (bit != 0) style.Face = (short)(set ? style.Face | bit : style.Face & ~bit);
                    if (color >= 0) style.Color = (short)color;
                    var a = view.Buffer.GetIterAtOffset(i); var b = view.Buffer.GetIterAtOffset(i+1);
                    view.Buffer.RemoveAllTags(a,b); view.Buffer.ApplyTag(tags[style.Face,style.Color],a,b);
                }
                richDirty = true; view.GrabFocus();
            }
            void Button(Box box, string label, System.Action action) {
                var button = new Gtk.Button(label); button.Clicked += (o,e) => Run(action); box.PackStart(button, false, false, 0);
            }
            Button(toolbar,"Picture resources…",()=> {
                using var chooser = new FileChooserDialog("Choose scenario Map or picture resources",dialog,FileChooserAction.Open,"Cancel",ResponseType.Cancel,"Open",ResponseType.Accept);
                if (chooser.Run() == (int)ResponseType.Accept) {
                    selectedResourceFile = chooser.Filename; Weland.Settings.PutSetting("TerminalEditor/Resources",selectedResourceFile);
                    picturePixels = null; foreach (var pixels in pictureCache.Values) pixels.Dispose();
                    pictureCache.Clear(); pictureErrors.Clear(); Reflow();
                }
            });
            Button(toolbar,"Bold",()=>Format(1,-1)); Button(toolbar,"Italic",()=>Format(2,-1)); Button(toolbar,"Underline",()=>Format(4,-1));
            toolbar.PackStart(colors,false,false,0); Button(toolbar,"Apply color",()=>Format(0,colors.Active));
            Button(actions,"Add terminal",()=> { Flush(); documents.Add(TerminalDocument.Create()); Refresh(documents.Count-1,1); });
            Button(actions,"Add page",()=> {
                if (Document() == null) return; Flush();
                int index = currentPage < 0 ? 0 : Page().Type == TerminalPageType.End ? currentPage : currentPage + 1;
                var page = new TerminalPage(); page.SetText("New message\n"); page.MaximumLines = 2;
                Document().Pages.Insert(index,page); Document().Dirty = true; Refresh(currentTerminal,index);
            });
            Button(actions,"Remove page",()=> {
                if (Page() == null) return;
                Document().Pages.RemoveAt(currentPage); Document().Dirty = true;
                Refresh(currentTerminal,Math.Min(currentPage,Document().Pages.Count-1));
            });
            void Move(int direction) {
                if (Page() == null) return; int target = currentPage + direction;
                if (target < 0 || target >= Document().Pages.Count) return; Flush();
                var page = Page(); Document().Pages.RemoveAt(currentPage); Document().Pages.Insert(target,page);
                Document().Dirty = true; Refresh(currentTerminal,target);
            }
            Button(actions,"Move earlier ↑",()=>Move(-1)); Button(actions,"Move later ↓",()=>Move(1));
            Button(actions,"Import picture…",()=> {
                if (Page() == null) return; Flush();
                using var chooser = new FileChooserDialog("Import terminal picture",dialog,FileChooserAction.Open,"Cancel",ResponseType.Cancel,"Open",ResponseType.Accept);
                var filter = new FileFilter { Name = "Pictures (PNG, JPEG, BMP, GIF…)" }; filter.AddPixbufFormats(); chooser.AddFilter(filter);
                if (chooser.Run() != (int)ResponseType.Accept) return;
                using var source = new Gdk.Pixbuf(chooser.Filename);
                using var oriented = source.ApplyEmbeddedOrientation();
                bool centered = Page().Type == TerminalPageType.Logon || Page().Type == TerminalPageType.Logoff || layout.Active == 2;
                int maxWidth = centered ? 622 : 307, maxHeight = Page().Type == TerminalPageType.Logon || Page().Type == TerminalPageType.Logoff ? 220 : 266;
                double scale = Math.Min(1,Math.Min((double)maxWidth / oriented.Width,(double)maxHeight / oriented.Height));
                int width = Math.Max(1,(int)Math.Round(oriented.Width * scale)), height = Math.Max(1,(int)Math.Round(oriented.Height * scale));
                using var resized = oriented.ScaleSimple(width,height,Gdk.InterpType.Bilinear);
                var pixels = new byte[resized.Rowstride * height]; System.Runtime.InteropServices.Marshal.Copy(resized.Pixels,pixels,0,pixels.Length);
                var image = new Image(width,height);
                for (int y = 0; y < height; ++y) for (int x = 0; x < width; ++x) {
                    int at = y * resized.Rowstride + x * resized.NChannels, output = (y * width + x) * 3;
                    int alpha = resized.HasAlpha ? pixels[at+3] : 255;
                    for (int c = 0; c < 3; ++c) image.Data[output+c] = (byte)((pixels[at+c] * alpha + 127) / 255);
                }
                int resourceId = TerminalPictureImports.NextId(mapfile.Directory.Keys.Concat(importedPictures.Keys)
                    .Concat(documents.SelectMany(d=>d.Pages).Where(p=>p.Type == TerminalPageType.Picture || p.Type == TerminalPageType.Logon || p.Type == TerminalPageType.Logoff).Select(p=>(int)p.Permutation)));
                importedPictures.Add(resourceId,TerminalPictureImports.Encode(image));
                pictureErrors.Remove(resourceId);
                if (pictureCache.TryGetValue(resourceId,out var oldPixels)) { oldPixels.Dispose(); pictureCache.Remove(resourceId); }
                if (Page().Type != TerminalPageType.Logon && Page().Type != TerminalPageType.Logoff) Page().Type = TerminalPageType.Picture;
                Page().Permutation = (short)resourceId;
                if (Page().Type == TerminalPageType.Picture) Page().MaximumLines = (short)Math.Min(short.MaxValue,
                    Math.Max(1,Page().Text.Split('\n').Sum(line=>Math.Max(1,(line.Length+34)/35))));
                Document().Dirty = true;
                Refresh(currentTerminal,currentPage);
            });
            Button(actions,"Import text…",()=> {
                if (Page() == null) return;
                using var chooser = new FileChooserDialog("Import page text",dialog,FileChooserAction.Open,"Cancel",ResponseType.Cancel,"Open",ResponseType.Accept);
                if (chooser.Run() == (int)ResponseType.Accept) view.Buffer.Text = File.ReadAllText(chooser.Filename).Replace("\r\n","\n").Replace('\r','\n');
            });
            dialog.ContentArea.PackStart(contents,true,true,0);
            Refresh(documents.Count > 0 ? 0 : -1, documents.Count > 0 && documents[0].Pages.Count > 0 ? 0 : -1);
            dialog.ShowAll(); UpdateZoom();
            try { while (dialog.Run() == (int)ResponseType.Accept) {
                try {
                    Flush();
                    foreach (var document in documents) document.EnsureEntryBranch();
                    byte[] bytes = TerminalCodec.Write(documents);
                    byte[] pictures = importedPictures.Count == 0 ? null : TerminalPictureImports.Write(importedPictures);
                    bool pictureChanged = originalPictures == null ? pictures != null : pictures == null || !pictures.SequenceEqual(originalPictures);
                    if ((original == null ? bytes.Length > 0 : !bytes.SequenceEqual(original)) || pictureChanged) {
                        editor.SetUndo("Edit level terminals");
                        if (bytes.Length == 0) Level.Chunks.Remove(TerminalCodec.Tag); else Level.Chunks[TerminalCodec.Tag] = bytes;
                        if (pictures == null) Level.Chunks.Remove(TerminalPictureImports.Tag); else Level.Chunks[TerminalPictureImports.Tag] = pictures;
                        editor.Changed = true; Redraw();
                    }
                    break;
                } catch (Exception e) { Error(e); }
            } } finally { picturePixels = null; foreach (var pixels in pictureCache.Values) pixels.Dispose(); }
        }
    }
}
