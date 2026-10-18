using System;
using System.Collections.Generic;
using System.Linq;

namespace Weland
{
    public partial class Editor
    {
        const int HistoryLimit = 100;
        sealed class HistoryState
        {
            public Wadfile.DirectoryEntry Map;
            public Selection Selection;
        }
        sealed class HistoryEntry
        {
            public HistoryState Before, After;
            public string Label;
        }
        readonly List<HistoryEntry> history = new List<HistoryEntry>();
        int historyPosition;
        bool historyTruncated;
        HistoryState pendingUndo;
        string pendingLabel;
        bool historyGesture;

        HistoryState CaptureHistory()
        {
            var selection = new Selection(); selection.CopyFrom(Selection);
            return new HistoryState { Map = Level.Save().Clone(), Selection = selection };
        }

        static bool SameHistoryMap(HistoryState a, HistoryState b) =>
            a.Map.Chunks.Count == b.Map.Chunks.Count && a.Map.Chunks.All(pair =>
                b.Map.Chunks.TryGetValue(pair.Key, out byte[] data) && pair.Value.SequenceEqual(data));

        string HistoryAction()
        {
            if (rightButtonMove) return "Move selection";
            switch (Tool)
            {
                case Tool.Box: return "Draw box";
                case Tool.Circle: return "Draw circle";
                case Tool.Line: return "Draw line";
                case Tool.Fill: return "Fill polygon";
                case Tool.Select: case Tool.Move: return "Move selection";
                case Tool.Object: return "Place object";
                case Tool.Rotate: return "Rotate selection";
                case Tool.Scale: return "Scale selection";
                case Tool.Annotation: return "Place annotation";
                default: return "Edit " + Tool.ToString();
            }
        }

        public void SetUndo(string label = null)
        {
            CommitHistory(true);
            pendingUndo = CaptureHistory();
            pendingLabel = label ?? HistoryAction();
        }

        void CancelPendingUndo() { pendingUndo = null; }

        public void CommitHistory(bool force = false)
        {
            if (pendingUndo == null || (!force && (historyGesture || DrawingBox || DrawingCircle ||
                DrawingSelectionBox || DrawingRing || Level.TemporaryLineStartIndex != -1))) return;
            HistoryState after = CaptureHistory();
            if (!SameHistoryMap(pendingUndo, after))
            {
                if (historyPosition < history.Count) history.RemoveRange(historyPosition, history.Count - historyPosition);
                history.Add(new HistoryEntry { Before = pendingUndo, After = after, Label = pendingLabel });
                if (history.Count > HistoryLimit) { history.RemoveAt(0); historyTruncated = true; }
                historyPosition = history.Count;
            }
            pendingUndo = null;
        }

        public IReadOnlyList<string> HistoryLabels
        {
            get { CommitHistory(); return new[] { historyTruncated ? "Earlier state" : "Initial state" }.Concat(history.Select(entry => entry.Label)).ToArray(); }
        }
        public int HistoryPosition { get { CommitHistory(); return historyPosition; } }
        public bool CanUndo { get { CommitHistory(); return historyPosition > 0; } }
        public bool CanRedo { get { CommitHistory(); return historyPosition < history.Count; } }

        public void ClearUndo()
        {
            history.Clear(); historyPosition = 0; historyTruncated = false; pendingUndo = null; historyGesture = false; undoSet = false;
        }

        void PrepareHistoryNavigation()
        {
            PlacingBakeLight=false; ClearLightingPreview();
            if (Level.TemporaryLineStartIndex != -1 && lineBefore != null) AbortLine();
            CancelPaste();
            ClearHover(); CancelBox(); CancelSelectionBox(); CancelCircle(); CancelRing(); CancelMoveTool();
            CancelRotateTool();
            CancelScaleTool();
            if (Level.TemporaryLineStartIndex != -1)
            {
                if (Level.FindEndpointLines(Level.TemporaryLineStartIndex).Count == 0)
                    Level.DeletePoint(Level.TemporaryLineStartIndex);
                Level.TemporaryLineStartIndex = -1;
            }
            historyGesture = false; CommitHistory(true);
        }
        public void Undo()
        {
            PrepareHistoryNavigation();
            if (historyPosition > 0) RestoreHistory(historyPosition - 1, history[historyPosition - 1].Before);
        }
        public void Redo() { PrepareHistoryNavigation(); GoToHistory(historyPosition + 1); }

        public void GoToHistory(int position)
        {
            PrepareHistoryNavigation();
            if (position < 0 || position > history.Count || position == historyPosition) return;
            HistoryState state = position == 0 ? history[0].Before : history[position - 1].After;
            RestoreHistory(position, state);
        }

        void RestoreHistory(int position, HistoryState state)
        {
            Level.Load(state.Map.Clone()); Selection.CopyFrom(state.Selection);
            historyPosition = position; undoSet = false;
            Changed = Dirty = RedrawAll = true;
        }
    }
}
