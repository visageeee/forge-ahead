using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gtk;

namespace Weland
{
    public partial class MapWindow
    {
        protected void OnEmbeddedLua(object sender, EventArgs args)
        {
            using var dialog = new Dialog("Level Lua Scripts", window1, DialogFlags.Modal,
                "Cancel", ResponseType.Cancel, "Apply", ResponseType.Accept);
            dialog.SetDefaultSize(800, 600);
            void Error(Exception e) {
                using var message = new MessageDialog(dialog, DialogFlags.Modal, MessageType.Error,
                    ButtonsType.Close, "Unable to update Lua scripts.");
                message.SecondaryText = e.Message; message.Run();
            }
            List<EmbeddedLuaScript> scripts;
            Level.Chunks.TryGetValue(EmbeddedLua.Tag, out byte[] original);
            try {
                scripts = EmbeddedLua.Read(original);
                // Reject undecodable source without changing the existing chunk.
                foreach (var script in scripts) _ = script.Text;
            } catch (Exception e) { Error(e); return; }
            var contents = new Box(Orientation.Vertical, 8) { Margin = 12 };
            contents.PackStart(new Label("Apply embeds scripts in this level. Save the map to keep them.\n" +
                "Scripts run in list order in-game; gameplay scripts are inactive in visual mode.") { Xalign = 0 }, false, false, 0);
            var picker = new ComboBoxText();
            contents.PackStart(picker, false, false, 0);
            var name = new Entry { PlaceholderText = "Script name" };
            contents.PackStart(name, false, false, 0);
            var source = new TextView { Monospace = true, WrapMode = WrapMode.None };
            var scroll = new ScrolledWindow(); scroll.Add(source);
            contents.PackStart(scroll, true, true, 0);
            var buttons = new Box(Orientation.Horizontal, 6);
            contents.PackStart(buttons, false, false, 0);
            int selected = -1;
            bool refreshing = false;
            void Flush() {
                if (selected < 0) return;
                var script = scripts[selected];
                // Preserve original byte encoding, headers and flags when untouched.
                var validated = new EmbeddedLuaScript { Name = name.Text, Text = source.Buffer.Text };
                if (script.Name != name.Text) script.Name = validated.Name;
                if (script.Text != source.Buffer.Text) script.Source = validated.Source;
            }
            void Display(int index) {
                selected = index;
                name.Sensitive = source.Sensitive = index >= 0;
                name.Text = index >= 0 ? scripts[index].Name : "";
                source.Buffer.Text = index >= 0 ? scripts[index].Text : "";
            }
            void Refresh(int index) {
                refreshing = true;
                try {
                    picker.RemoveAll();
                    foreach (var script in scripts) picker.AppendText(script.Name);
                    picker.Active = index; Display(index);
                } finally { refreshing = false; }
            }
            picker.Changed += (o, e) => {
                if (refreshing) return;
                int target = picker.Active;
                try { Flush(); Refresh(target); }
                catch (Exception ex) {
                    refreshing = true; picker.Active = selected; refreshing = false; Error(ex);
                }
            };
            void Button(string title, System.Action action) {
                var button = new Gtk.Button(title);
                button.Clicked += (o, e) => { try { action(); } catch (Exception ex) { Error(ex); } };
                buttons.PackStart(button, false, false, 0);
            }
            Button("Add", () => {
                Flush(); scripts.Add(new EmbeddedLuaScript { Name = "New script", Text = "-- Level script\n" });
                Refresh(scripts.Count - 1);
            });
            Button("Import .lua…", () => {
                Flush();
                using var chooser = new FileChooserDialog("Import Lua script", dialog, FileChooserAction.Open,
                    "Cancel", ResponseType.Cancel, "Import", ResponseType.Accept);
                var filter = new FileFilter { Name = "Lua scripts" }; filter.AddPattern("*.lua"); chooser.AddFilter(filter);
                var all = new FileFilter { Name = "All files" }; all.AddPattern("*"); chooser.AddFilter(all);
                if (chooser.Run() != (int)ResponseType.Accept) return;
                var script = new EmbeddedLuaScript { Name = Path.GetFileName(chooser.Filename),
                    Text = File.ReadAllText(chooser.Filename, new System.Text.UTF8Encoding(false, true)) };
                scripts.Add(script); Refresh(scripts.Count - 1);
            });
            Button("Export…", () => {
                if (selected < 0) return;
                Flush();
                using var chooser = new FileChooserDialog("Export Lua script", dialog, FileChooserAction.Save,
                    "Cancel", ResponseType.Cancel, "Save", ResponseType.Accept);
                chooser.CurrentName = "level-script.lua"; chooser.DoOverwriteConfirmation = true;
                if (chooser.Run() == (int)ResponseType.Accept) File.WriteAllBytes(chooser.Filename, scripts[selected].Source);
            });
            Button("Remove", () => {
                if (selected < 0) return;
                int next = selected; scripts.RemoveAt(selected); Refresh(Math.Min(next, scripts.Count - 1));
            });
            void Move(int direction) {
                if (selected < 0 || selected + direction < 0 || selected + direction >= scripts.Count) return;
                Flush(); int target = selected + direction;
                var script = scripts[selected]; scripts.RemoveAt(selected); scripts.Insert(target, script); Refresh(target);
            }
            Button("↑", () => Move(-1)); Button("↓", () => Move(1));
            dialog.ContentArea.PackStart(contents, true, true, 0);
            Refresh(scripts.Count > 0 ? 0 : -1); dialog.ShowAll();
            while (dialog.Run() == (int)ResponseType.Accept) {
                try {
                    Flush();
                    byte[] bytes = scripts.Count == 0 ? null : EmbeddedLua.Write(scripts);
                    if (bytes == null ? original != null : original == null || !bytes.SequenceEqual(original)) {
                        editor.SetUndo("Edit level Lua scripts");
                        if (bytes == null) Level.Chunks.Remove(EmbeddedLua.Tag);
                        else Level.Chunks[EmbeddedLua.Tag] = bytes;
                        editor.Changed = true; Redraw();
                    }
                    break;
                } catch (Exception e) { Error(e); }
            }
        }
    }
}
