using System;
using Gtk;

namespace Weland
{
    public partial class MapWindow
    {
        System.Action SetupStairsOptions(Box contents, bool ring, Func<bool> available)
        {
            var group = new Box(Orientation.Vertical, 6);
            var enabled = new CheckButton("Stairs") { Active = ring ? editor.RingStairs : editor.CircleStairs };
            group.PackStart(enabled, false, false, 0);
            var fields = new Box(Orientation.Vertical, 6);
            SpinButton Height(string label, short initial, System.Action<short> update, string setting)
            {
                var row = new Box(Orientation.Horizontal, 8);
                row.PackStart(new Label(label) { Xalign = 0 }, true, true, 0);
                var value = new SpinButton(-32, short.MaxValue / 1024.0, 0.1) {
                    Numeric = true, Digits = 4, WidthChars = 7, Value = World.ToDouble(initial)
                };
                value.ValueChanged += (sender, args) => {
                    short height = (short)Math.Max(short.MinValue, Math.Min(short.MaxValue, Math.Round(value.Value * 1024)));
                    update(height); Weland.Settings.PutSetting(setting, (int)height); Redraw();
                };
                row.PackStart(value, false, false, 0); fields.PackStart(row, false, false, 0);
                return value;
            }
            Height("Starting height (WU)", ring ? editor.RingStairStart : editor.CircleStairStart,
                value => { if (ring) editor.RingStairStart = value; else editor.CircleStairStart = value; },
                ring ? "Tools/RingStairStart" : "Tools/CircleStairStart");
            Height("Step height (WU)", ring ? editor.RingStairStep : editor.CircleStairStep,
                value => { if (ring) editor.RingStairStep = value; else editor.CircleStairStep = value; },
                ring ? "Tools/RingStairStep" : "Tools/CircleStairStep");
            Height("Ceiling height (WU)", ring ? editor.RingStairCeiling : editor.CircleStairCeiling,
                value => { if (ring) editor.RingStairCeiling = value; else editor.CircleStairCeiling = value; },
                ring ? "Tools/RingStairCeiling" : "Tools/CircleStairCeiling");
            var follows = new CheckButton("Ceiling follows floor") { Active = ring ? editor.RingCeilingFollows : editor.CircleCeilingFollows };
            follows.Toggled += (sender, args) => {
                if (ring) editor.RingCeilingFollows = follows.Active; else editor.CircleCeilingFollows = follows.Active;
                Weland.Settings.PutSetting(ring ? "Tools/RingCeilingFollows" : "Tools/CircleCeilingFollows", follows.Active);
                Redraw();
            };
            fields.PackStart(follows, false, false, 0);
            group.PackStart(fields, false, false, 0);
            group.PackStart(new Label(ring ? "Steps clockwise from the top.\nNegative steps descend." : "Steps along the longer drag axis.\nNegative steps descend.") {
                Xalign = 0, LineWrap = true, MaxWidthChars = 28
            }, false, false, 0);
            void Refresh() { group.Sensitive = available(); fields.Sensitive = enabled.Active; }
            enabled.Toggled += (sender, args) => {
                if (ring) editor.RingStairs = enabled.Active; else editor.CircleStairs = enabled.Active;
                Weland.Settings.PutSetting(ring ? "Tools/RingStairs" : "Tools/CircleStairs", enabled.Active);
                Refresh(); Redraw();
            };
            contents.PackStart(group, false, false, 0); Refresh();
            return Refresh;
        }
    }
}
