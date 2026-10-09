using System;
using System.IO;
using System.Linq;
using Gtk;

namespace Weland
{
    public partial class MapWindow
    {
        sealed class FogControls
        {
            public readonly CheckButton Include = new CheckButton("Include these fog settings");
            readonly CheckButton enabled = new CheckButton("Enable fog");
            readonly CheckButton landscapes = new CheckButton("Affect landscapes");
            readonly SpinButton depth = new SpinButton(0.01, 10000, 0.25) { Digits = 2, Value = 8, Numeric = true };
            readonly ColorButton color = new ColorButton { Rgba = new Gdk.RGBA { Red = .5, Green = .5, Blue = .5, Alpha = 1 } };

            public FogControls(Box parent, string title)
            {
                var frame = new Frame(title);
                var contents = new Box(Orientation.Vertical, 6) { Margin = 10 };
                var settings = new Box(Orientation.Vertical, 6) { Sensitive = false };
                enabled.Active = true;
                landscapes.Active = true;
                settings.PackStart(enabled, false, false, 0);
                settings.PackStart(landscapes, false, false, 0);
                var row = new Box(Orientation.Horizontal, 8);
                row.PackStart(new Label("Depth (world units)"), false, false, 0);
                row.PackStart(depth, false, false, 0);
                row.PackStart(new Label("Color"), false, false, 0);
                row.PackStart(color, false, false, 0);
                settings.PackStart(row, false, false, 0);
                Include.Toggled += (o, e) => settings.Sensitive = Include.Active;
                contents.PackStart(Include, false, false, 0);
                contents.PackStart(settings, false, false, 0);
                frame.Add(contents);
                parent.PackStart(frame, false, false, 0);
            }

            public MmlFog Read() => new MmlFog {
                Include = Include.Active, Enabled = enabled.Active, Landscapes = landscapes.Active,
                Depth = depth.Value, Red = color.Rgba.Red, Green = color.Rgba.Green, Blue = color.Rgba.Blue
            };
        }

        protected void OnExportMml(object sender, EventArgs args)
        {
            var options = LevelMml.ForLevel(Level);
            using var dialog = new Dialog("Export Level MML", window1, DialogFlags.Modal,
                "Cancel", ResponseType.Cancel, "Export…", ResponseType.Accept);
            var contents = new Box(Orientation.Vertical, 10) { Margin = 12 };
            var note = new Label("Save the .mml file in your scenario’s Scripts folder and restart Aleph One.\n" +
                "Collection settings affect every level using this environment; fog affects all levels.\n" +
                "For settings limited to one level, embed the script with Atque.") { Xalign = 0 };
            contents.PackStart(note, false, false, 0);
            var include = new CheckButton("Include texture / scenery collection loading") { Active = options.IncludeCollections };
            contents.PackStart(include, false, false, 0);
            var loading = new Box(Orientation.Vertical, 6) { Sensitive = include.Active };
            loading.PackStart(new Label("Environment " + options.Environment + "; texture collections used: " +
                string.Join(", ", LevelMml.TextureCollections(Level)) + "\n" +
                "-2 = keep scenario setting; -1 = empty; 0–31 = collection ID.\n" +
                "Suggested extras assume M2/Infinity defaults. Check custom mappings before replacing slots.\n" +
                "Scenery collections must be chosen manually. At most seven collections fit this environment.") { Xalign = 0 }, false, false, 0);
            var row = new Box(Orientation.Horizontal, 8);
            var slots = new SpinButton[7];
            for (int i = 0; i < slots.Length; ++i) {
                var column = new Box(Orientation.Vertical, 4);
                column.PackStart(new Label("Slot " + i), false, false, 0);
                slots[i] = new SpinButton(-2, 31, 1) { Numeric = true, Value = options.Collections[i], WidthChars = 3 };
                column.PackStart(slots[i], false, false, 0);
                row.PackStart(column, false, false, 0);
            }
            loading.PackStart(row, false, false, 0);
            contents.PackStart(loading, false, false, 0);
            include.Toggled += (o, e) => loading.Sensitive = include.Active;
            var above = new FogControls(contents, "Above liquid");
            var below = new FogControls(contents, "Below liquid");
            dialog.ContentArea.PackStart(contents, true, true, 0);
            dialog.ShowAll();
            while (dialog.Run() == (int)ResponseType.Accept) {
                try {
                    options.IncludeCollections = include.Active;
                    options.Collections = slots.Select(s => s.ValueAsInt).ToArray();
                    options.Above = above.Read(); options.Below = below.Read();
                    var document = options.Build();
                    using var chooser = new FileChooserDialog("Export MML as", window1, FileChooserAction.Save,
                        "Cancel", ResponseType.Cancel, "Save", ResponseType.Accept);
                    chooser.SetCurrentFolder(Weland.Settings.GetSetting("LastSave/Folder", Environment.GetFolderPath(Environment.SpecialFolder.Personal)));
                    chooser.CurrentName = "level-settings.mml";
                    chooser.DoOverwriteConfirmation = true;
                    if (chooser.Run() != (int)ResponseType.Accept) continue;
                    document.Save(chooser.Filename);
                    Weland.Settings.PutSetting("LastSave/Folder", Path.GetDirectoryName(chooser.Filename));
                    break;
                } catch (Exception e) {
                    using var error = new MessageDialog(dialog, DialogFlags.Modal, MessageType.Error, ButtonsType.Close,
                        "Unable to export MML.");
                    error.SecondaryText = e.Message;
                    error.Run();
                }
            }
        }
    }
}
