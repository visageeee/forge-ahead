using Gtk;
using System;

namespace Weland
{
    public partial class MapWindow
    {
        Frame selectOptions;

        void SetupSelectOptions(Box sidebar)
        {
            selectOptions = new Frame("Tool options") { NoShowAll = true, Margin = 6, Valign = Align.Start };
            var contents = new Box(Orientation.Vertical, 10) { Margin = 12 };
            contents.PackStart(new Label("Select") { Xalign = 0 }, false, false, 0);
            var message = new Label("") { Xalign = 0, LineWrap = true, MaxWidthChars = 28 };
            void Refresh(string reason = null) { message.Text = reason ?? ""; UpdateInspector(); UpdateStatusBar(); Redraw(); }
            var selectionRow = new Box(Orientation.Horizontal, 6);
            var all = new Button("Select all"); var none = new Button("Deselect all");
            all.Clicked += (sender, args) => { editor.SelectAllGeometry(); Refresh(); };
            none.Clicked += (sender, args) => { editor.DeselectAllGeometry(); Refresh(); };
            selectionRow.PackStart(all, true, true, 0); selectionRow.PackStart(none, true, true, 0);
            contents.PackStart(selectionRow, false, false, 0);
            contents.PackStart(new Label("Ctrl+A selects all geometry.\nCtrl+Shift+A clears the selection.") { Xalign = 0 }, false, false, 0);
            var clipboardRow = new Box(Orientation.Horizontal, 6);
            var copy = new Button("Copy"); var paste = new Button("Paste");
            copy.Clicked += (sender, args) => { editor.CopySelection(out string reason); Refresh(reason); };
            paste.Clicked += (sender, args) => { editor.BeginPaste(out string reason); Refresh(reason); };
            clipboardRow.PackStart(copy, true, true, 0); clipboardRow.PackStart(paste, true, true, 0);
            contents.PackStart(clipboardRow, false, false, 0);
            contents.PackStart(new Label("Ctrl+C copies; Ctrl+V starts paste.\nClick to place; Escape cancels.") { Xalign = 0 }, false, false, 0);
            var mirror = new Button("Mirror horizontal"); var flip = new Button("Flip vertical");
            mirror.Clicked += (sender, args) => { editor.MirrorSelectedGeometry(true, out string reason); Refresh(reason); };
            flip.Clicked += (sender, args) => { editor.MirrorSelectedGeometry(false, out string reason); Refresh(reason); };
            contents.PackStart(mirror, false, false, 0); contents.PackStart(flip, false, false, 0);
            contents.PackStart(new Label("Reflect around the selection centre.") { Xalign = 0, LineWrap = true, MaxWidthChars = 28 }, false, false, 0);
            var heightRow = new Box(Orientation.Horizontal, 8);
            heightRow.PackStart(new Label("Shift height (WU)") { Xalign = 0 }, true, true, 0);
            var amount = new SpinButton(-64, 64, 0.1) { Numeric = true, Digits = 4, WidthChars = 7, Value = 0.25 };
            heightRow.PackStart(amount, false, false, 0); contents.PackStart(heightRow, false, false, 0);
            var floor = new CheckButton("Floor") { Active = true }; var ceiling = new CheckButton("Ceiling") { Active = true };
            var targets = new Box(Orientation.Horizontal, 8);
            targets.PackStart(floor, true, true, 0); targets.PackStart(ceiling, true, true, 0); contents.PackStart(targets, false, false, 0);
            var apply = new Button("Shift height");
            apply.Clicked += (sender, args) => {
                editor.ShiftSelectedHeights((int)Math.Round(amount.Value * 1024), floor.Active, ceiling.Active, out string reason); Refresh(reason);
            };
            contents.PackStart(apply, false, false, 0);
            contents.PackStart(new Label("Positive raises; negative lowers.\nUses selected polygons, or polygons\ntouching selected vertices/lines.") { Xalign = 0, LineWrap = true, MaxWidthChars = 28 }, false, false, 0);
            var lua=new Button("Polygon Lua trigger…");lua.Clicked+=OnPolygonLua;
            contents.PackStart(lua,false,false,0);
            contents.PackStart(message, false, false, 0);
            selectOptions.Add(contents); contents.ShowAll(); sidebar.PackStart(selectOptions, false, false, 0);
        }
    }
}
