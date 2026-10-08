using Gtk;

namespace Weland
{
    public partial class MapWindow
    {
        Frame circleOptions;

        void SetupCircleOptions()
        {
            circleOptions = new Frame("Tool options") { NoShowAll = true, Margin = 6, Valign = Align.Start };
            var contents = new Box(Orientation.Vertical, 10) { Margin = 12 };
            contents.PackStart(new Label("Circle") { Xalign = 0 }, false, false, 0);
            var row = new Box(Orientation.Horizontal, 12);
            row.PackStart(new Label("Sides") { Xalign = 0 }, true, true, 0);
            var sides = new SpinButton(3, 8, 1) { Numeric = true, Value = editor.CircleSides, WidthChars = 3 };
            sides.ValueChanged += (sender, args) => {
                editor.CircleSides = sides.ValueAsInt;
                Weland.Settings.PutSetting("Tools/CircleSides", editor.CircleSides);
            };
            row.PackStart(sides, false, false, 0);
            contents.PackStart(row, false, false, 0);
            contents.PackStart(new Label("Drag opposite corners.\nHold Shift for a circle.\n3–8 sides per polygon.") { Xalign = 0 }, false, false, 0);
            circleOptions.Add(contents);
            contents.ShowAll();
            grid1.Attach(circleOptions, 5, 0, 1, 1);
            circleOptions.Hide();
        }
    }
}
