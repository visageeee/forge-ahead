using Gtk;
using System;

namespace Weland
{
    public partial class MapWindow
    {
        Frame circleOptions, boxOptions;

        void SetupCircleOptions()
        {
            circleOptions = new Frame("Tool options") { NoShowAll = true, Margin = 6, Valign = Align.Start };
            var contents = new Box(Orientation.Vertical, 10) { Margin = 12 };
            contents.PackStart(new Label("Circle") { Xalign = 0 }, false, false, 0);
            var filled = new CheckButton("Filled") { Active = editor.CircleFilled };
            filled.Toggled += (sender, args) => {
                editor.CircleFilled = filled.Active;
                Weland.Settings.PutSetting("Tools/CircleFilled", filled.Active);
            };
            contents.PackStart(filled, false, false, 0);
            var row = new Box(Orientation.Horizontal, 12);
            row.PackStart(new Label("Sides") { Xalign = 0 }, true, true, 0);
            var sides = new SpinButton(3, 8, 1) { Numeric = true, Value = editor.CircleSides, WidthChars = 3 };
            sides.ValueChanged += (sender, args) => {
                editor.CircleSides = sides.ValueAsInt;
                Weland.Settings.PutSetting("Tools/CircleSides", editor.CircleSides);
            };
            row.PackStart(sides, false, false, 0);
            contents.PackStart(row, false, false, 0);
            var tessellate = new CheckButton("Tessellate") { Active = editor.CircleTessellationEnabled };
            contents.PackStart(tessellate, false, false, 0);
            var sizeRow = new Box(Orientation.Horizontal, 8);
            sizeRow.PackStart(new Label("Edge length (WU)") { Xalign = 0 }, true, true, 0);
            var size = new SpinButton(1.0 / 64, 8, 0.125) {
                Numeric = true, Digits = 4, Value = editor.CircleTileSize / 1024.0, WidthChars = 6
            };
            sizeRow.PackStart(size, false, false, 0);
            contents.PackStart(sizeRow, false, false, 0);
            var help = new Label("") { Xalign = 0, LineWrap = true, MaxWidthChars = 28 };
            void UpdateHelp() {
                sizeRow.Sensitive = tessellate.Active;
                help.Text = tessellate.Active
                    ? "Drag to expand a tile patch.\n3, 4, or 6 sides tile directly.\n8 sides adds square fillers.\nExisting matching tiles are reused."
                    : "Drag opposite corners.\nHold Shift for a circle.\n3–8 sides per polygon.\nUncheck Filled for an outline.";
            }
            tessellate.Toggled += (sender, args) => {
                editor.CircleTessellationEnabled = tessellate.Active;
                Weland.Settings.PutSetting("Tools/CircleTessellation", tessellate.Active);
                UpdateHelp();
            };
            size.ValueChanged += (sender, args) => {
                editor.CircleTileSize = (int)Math.Round(size.Value * 1024);
                Weland.Settings.PutSetting("Tools/CircleTileSize", editor.CircleTileSize);
            };
            UpdateHelp();
            contents.PackStart(help, false, false, 0);
            circleOptions.Add(contents);
            contents.ShowAll();
            var sidebar = new Box(Orientation.Vertical, 6) { WidthRequest = 240, Vexpand = true };
            sidebar.PackStart(circleOptions, false, false, 0);
            boxOptions = new Frame("Tool options") { NoShowAll = true, Margin = 6, Valign = Align.Start };
            var boxContents = new Box(Orientation.Vertical, 10) { Margin = 12 };
            boxContents.PackStart(new Label("Box") { Xalign = 0 }, false, false, 0);
            var boxFill = new CheckButton("Filled") { Active = editor.BoxFilled };
            boxFill.Toggled += (sender, args) => {
                editor.BoxFilled = boxFill.Active;
                Weland.Settings.PutSetting("Tools/BoxFilled", boxFill.Active);
            };
            boxContents.PackStart(boxFill, false, false, 0);
            boxContents.PackStart(new Label("Drag opposite corners.\nUncheck Filled for an outline.") { Xalign = 0 }, false, false, 0);
            boxOptions.Add(boxContents);
            boxContents.ShowAll();
            sidebar.PackStart(boxOptions, false, false, 0);
            SetupHistory(sidebar);
            grid1.Attach(sidebar, 5, 0, 1, 1);
            sidebar.ShowAll();
            circleOptions.Hide();
            boxOptions.Hide();
        }
    }
}
