using Gtk;

namespace Weland
{
    public partial class MapWindow
    {
        Frame ringOptions;

        void SetupRingOptions(Box sidebar)
        {
            ringOptions = new Frame("Tool options") { NoShowAll = true, Margin = 6, Valign = Align.Start };
            var contents = new Box(Orientation.Vertical, 10) { Margin = 12 };
            contents.PackStart(new Label("Ring") { Xalign = 0 }, false, false, 0);
            System.Action updateStairs = null;
            var filled = new CheckButton("Filled") { Active = editor.RingFilled };
            filled.Toggled += (sender, args) => {
                editor.RingFilled = filled.Active; updateStairs?.Invoke();
                Weland.Settings.PutSetting("Tools/RingFilled", filled.Active); Redraw();
            };
            contents.PackStart(filled, false, false, 0);
            var row = new Box(Orientation.Horizontal, 8);
            row.PackStart(new Label("Sides") { Xalign = 0 }, true, true, 0);
            var sides = new SpinButton(3, RingGeometry.MaxSides, 1) { Numeric = true, Value = editor.RingSides, WidthChars = 4 };
            sides.ValueChanged += (sender, args) => {
                editor.RingSides = sides.ValueAsInt;
                Weland.Settings.PutSetting("Tools/RingSides", editor.RingSides);
                Redraw();
            };
            row.PackStart(sides, false, false, 0); contents.PackStart(row, false, false, 0);
            var inner = new CheckButton("Inner rings") { Active = editor.RingHasInner };
            contents.PackStart(inner, false, false, 0);
            var countRow = new Box(Orientation.Horizontal, 8) { Sensitive = inner.Active };
            countRow.PackStart(new Label("Inner rings") { Xalign = 0 }, true, true, 0);
            var count = new SpinButton(1, RingGeometry.MaxInnerRings, 1) { Numeric = true, Value = editor.RingInnerCount, WidthChars = 4 };
            count.ValueChanged += (sender, args) => {
                editor.RingInnerCount = count.ValueAsInt;
                Weland.Settings.PutSetting("Tools/RingInnerCount", editor.RingInnerCount); Redraw();
            };
            countRow.PackStart(count, false, false, 0); contents.PackStart(countRow, false, false, 0);
            var sizeRow = new Box(Orientation.Horizontal, 8) { Sensitive = inner.Active };
            sizeRow.PackStart(new Label("Innermost size (%)") { Xalign = 0 }, true, true, 0);
            var size = new SpinButton(1, 95, 1) { Numeric = true, Value = editor.RingInnerPercent, WidthChars = 4 };
            size.ValueChanged += (sender, args) => {
                editor.RingInnerPercent = size.ValueAsInt;
                Weland.Settings.PutSetting("Tools/RingInnerPercent", editor.RingInnerPercent);
                Redraw();
            };
            sizeRow.PackStart(size, false, false, 0); contents.PackStart(sizeRow, false, false, 0);
            inner.Toggled += (sender, args) => {
                editor.RingHasInner = inner.Active; sizeRow.Sensitive = countRow.Sensitive = inner.Active;
                Weland.Settings.PutSetting("Tools/RingHasInner", inner.Active);
                Redraw();
            };
            updateStairs = SetupStairsOptions(contents, true, () => filled.Active);
            contents.PackStart(new Label("Drag opposite corners.\nHold Shift for a circle.\nInner rings are evenly spaced.\nInnermost ring leaves an open centre.\nUncheck Filled for lines and spokes.\nUp to 256 sectors per drag.\nPlacement is checked on release.") {
                Xalign = 0, LineWrap = true, MaxWidthChars = 28
            }, false, false, 0);
            ringOptions.Add(contents); contents.ShowAll();
            sidebar.PackStart(ringOptions, false, false, 0);
        }
    }
}
