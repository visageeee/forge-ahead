using Gtk;

namespace Weland
{
    public partial class MapWindow
    {
        Frame lineOptions;
        void SetupLineOptions(Box sidebar)
        {
            lineOptions = new Frame("Tool options") { NoShowAll = true, Margin = 6, Valign = Align.Start };
            var contents = new Box(Orientation.Vertical, 10) { Margin = 12 };
            contents.PackStart(new Label("Line") { Xalign = 0 }, false, false, 0);
            var split = new CheckButton("Split all intersected polygons") { Active = editor.LineSplitAll };
            split.Toggled += (sender, args) => {
                editor.LineSplitAll = split.Active;
                Weland.Settings.PutSetting("Tools/LineSplitAll", split.Active);
                UpdateStatusBar(); Redraw();
            };
            contents.PackStart(split, false, false, 0);
            contents.PackStart(new Label("Vertices and edges use the Select tool's hover colors and snap distance.\nGrid snapping still follows the grid.") { Xalign=0,LineWrap=true,MaxWidthChars=28 },false,false,0);
            contents.PackStart(new Label("Draw across several polygons.\nCreates shared vertices at crossings\nand connects the complete path.\nStart and end outside polygons\nor on their boundaries.") { Xalign = 0, LineWrap = true, MaxWidthChars = 28 }, false, false, 0);
            lineOptions.Add(contents); contents.ShowAll(); sidebar.PackStart(lineOptions, false, false, 0);
        }
    }
}
