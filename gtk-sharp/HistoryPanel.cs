using System;
using System.Linq;
using Gtk;

namespace Weland
{
    public partial class MapWindow
    {
        ListStore historyRows;
        TreeView historyView;
        Button historyUndo, historyRedo;
        bool refreshingHistory;
        string[] displayedHistory;
        int displayedHistoryPosition = -1;

        void SetupHistory(Box sidebar)
        {
            var frame = new Frame("History") { Margin = 6, HeightRequest = 240 };
            var contents = new Box(Orientation.Vertical, 6) { Margin = 8 };
            var controls = new Box(Orientation.Horizontal, 6);
            historyUndo = new Button("Undo"); historyRedo = new Button("Redo");
            historyUndo.Clicked += OnUndo; historyRedo.Clicked += OnRedo;
            controls.PackStart(historyUndo, true, true, 0); controls.PackStart(historyRedo, true, true, 0);
            contents.PackStart(controls, false, false, 0);
            historyRows = new ListStore(typeof(int), typeof(string));
            // GTK's built-in type-ahead search changes the selected row while
            // typing. History navigation must require an explicit action.
            historyView = new TreeView(historyRows) { HeadersVisible = false, EnableSearch = false };
            historyView.AppendColumn("Action", new CellRendererText(), "text", 1);
            historyView.ButtonReleaseEvent += (sender, args) => {
                if (args.Event.Button == 1 && historyView.GetPathAtPos((int)args.Event.X, (int)args.Event.Y, out TreePath path))
                    NavigateHistory(path);
            };
            historyView.RowActivated += (sender, args) => NavigateHistory(args.Path);
            var scroll = new ScrolledWindow();
            scroll.SetPolicy(PolicyType.Automatic, PolicyType.Automatic);
            scroll.Add(historyView); contents.PackStart(scroll, true, true, 0);
            frame.Add(contents);
            // Keep the panel at the bottom of the right sidebar.
            sidebar.PackEnd(frame, false, false, 0);
            frame.ShowAll();
        }

        void NavigateHistory(TreePath path)
        {
            if (refreshingHistory || !historyRows.GetIter(out TreeIter iter, path)) return;
            int position = (int)historyRows.GetValue(iter, 0);
            if (position == editor.HistoryPosition) return;
            editor.GoToHistory(position);
            AfterHistoryNavigation();
        }

        void RefreshHistory()
        {
            if (historyRows == null || editor.Level == null) return;
            var labels = editor.HistoryLabels.ToArray();
            int position = editor.HistoryPosition;
            historyUndo.Sensitive = editor.CanUndo; historyRedo.Sensitive = editor.CanRedo;
            if (displayedHistoryPosition == position && displayedHistory != null && displayedHistory.SequenceEqual(labels)) return;
            refreshingHistory = true;
            try
            {
                historyRows.Clear();
                for (int i = 0; i < labels.Length; ++i)
                    historyRows.AppendValues(i, (i == position ? "▶ " : i > position ? "↶ " : "  ") + labels[i]);
                var path = new TreePath(position.ToString());
                historyView.Selection.SelectPath(path);
                historyView.ScrollToCell(path, null, false, 0, 0);
                displayedHistory = labels; displayedHistoryPosition = position;
            }
            finally { refreshingHistory = false; }
        }

        void AfterHistoryNavigation()
        {
            var restored = new Selection(); restored.CopyFrom(selection);
            ChooseTool(editor.Tool);
            selection.CopyFrom(restored);
            window1.Title = Level.Name;
            BuildLevelMenu(); UpdateInspector(); UpdateStatusBar(); Redraw();
        }

        internal void OnRedo(object sender, EventArgs args)
        {
            editor.Redo(); AfterHistoryNavigation();
        }
    }
}
