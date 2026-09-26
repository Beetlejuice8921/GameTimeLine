using System;
using UnityEditor;

namespace TimeMapGameplay.Editor
{
    /// <summary>
    /// Wraps timeline modifications in Undo. Continuous edits (drags) are collapsed into one undo step.
    /// </summary>
    public sealed class TimelineEditing
    {
        readonly Func<ProgressionTimeline> _getTimeline;
        int _gestureGroup = -1;

        public TimelineEditing(Func<ProgressionTimeline> getTimeline)
        {
            _getTimeline = getTimeline;
        }

        /// <summary>Raised after any modification applied through this object.</summary>
        public event Action Changed;

        public bool InGesture => _gestureGroup >= 0;

        public void BeginGesture(string name)
        {
            Undo.IncrementCurrentGroup();
            Undo.SetCurrentGroupName(name);
            _gestureGroup = Undo.GetCurrentGroup();
        }

        public void EndGesture()
        {
            if (_gestureGroup < 0) return;
            Undo.CollapseUndoOperations(_gestureGroup);
            _gestureGroup = -1;
        }

        /// <summary>Records the timeline for undo, applies <paramref name="change"/> and marks the asset dirty.</summary>
        public void Apply(string name, Action<ProgressionTimeline> change)
        {
            var timeline = _getTimeline();
            if (timeline == null) return;

            Undo.RecordObject(timeline, name);
            change(timeline);
            EditorUtility.SetDirty(timeline);
            Changed?.Invoke();
        }
    }
}
