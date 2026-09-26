using System;
using System.Collections.Generic;
using UnityEngine;

namespace TimeMapGameplay.Editor
{
    /// <summary>
    /// Selected items (multi-selection) or a single selected track. Serializable so it survives domain reloads.
    /// </summary>
    [Serializable]
    public sealed class TimelineSelection
    {
        [SerializeField] List<string> _items = new();
        [SerializeField] string _trackId;

        public event Action Changed;

        public IReadOnlyList<string> Items => _items;
        public int Count => _items.Count;
        public bool IsEmpty => _items.Count == 0 && string.IsNullOrEmpty(_trackId);

        /// <summary>Track selected via its header; null when items (or nothing) are selected.</summary>
        public string TrackId => _trackId;

        /// <summary>Most recently selected item, or null.</summary>
        public string Primary => _items.Count > 0 ? _items[_items.Count - 1] : null;

        public bool Contains(string id) => _items.Contains(id);

        public void Clear()
        {
            if (IsEmpty) return;
            _items.Clear();
            _trackId = null;
            Changed?.Invoke();
        }

        public void Set(IEnumerable<string> ids)
        {
            var next = new List<string>();
            foreach (var id in ids)
                if (!string.IsNullOrEmpty(id) && !next.Contains(id))
                    next.Add(id);

            if (_trackId == null && next.Count == _items.Count && next.TrueForAll(_items.Contains)) return;
            _items = next;
            _trackId = null;
            Changed?.Invoke();
        }

        public void Select(string id) => Set(new[] { id });

        public void Add(string id)
        {
            if (string.IsNullOrEmpty(id) || (_trackId == null && _items.Contains(id))) return;
            _trackId = null;
            _items.Remove(id);
            _items.Add(id);
            Changed?.Invoke();
        }

        public void Toggle(string id)
        {
            if (_items.Remove(id))
            {
                Changed?.Invoke();
                return;
            }
            Add(id);
        }

        public void SelectTrack(string trackId)
        {
            if (_trackId == trackId && _items.Count == 0) return;
            _items.Clear();
            _trackId = trackId;
            Changed?.Invoke();
        }

        /// <summary>Drops ids that no longer exist in <paramref name="timeline"/>. Returns true when something changed.</summary>
        public bool Prune(ProgressionTimeline timeline)
        {
            int removed = _items.RemoveAll(id => timeline == null || !timeline.TryFindItem(id, out _, out _));
            bool trackGone = _trackId != null && (timeline == null || timeline.FindTrack(_trackId) == null);
            if (trackGone) _trackId = null;
            if (removed == 0 && !trackGone) return false;
            Changed?.Invoke();
            return true;
        }
    }
}
