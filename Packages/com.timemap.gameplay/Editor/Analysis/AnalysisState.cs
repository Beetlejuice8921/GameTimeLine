using System;
using UnityEditor;
using UnityEngine;

namespace TimeMapGameplay.Editor
{
    /// <summary>
    /// Per-window analysis settings: A/B comparison, telemetry overlay and a baseline version to compare with.
    /// Not stored in the timeline asset (it is a view of the data, not the data).
    /// </summary>
    [Serializable]
    public sealed class AnalysisState
    {
        [SerializeField] bool _abEnabled;
        [SerializeField] float _timeB;
        [SerializeField] TelemetryData _telemetry;
        [SerializeField] bool _showTelemetry = true;
        [SerializeField] ProgressionTimeline _baseline;

        [NonSerialized] TelemetryIndex _index;
        [NonSerialized] int _indexedCount = -1;

        public event Action Changed;

        public bool AbEnabled
        {
            get => _abEnabled;
            set { if (_abEnabled == value) return; _abEnabled = value; Changed?.Invoke(); }
        }

        /// <summary>Time of playhead B (A is the main playhead).</summary>
        public float TimeB
        {
            get => _timeB;
            set { if (Mathf.Approximately(_timeB, value)) return; _timeB = value; Changed?.Invoke(); }
        }

        public TelemetryData Telemetry
        {
            get => _telemetry;
            set { if (_telemetry == value) return; _telemetry = value; _index = null; Changed?.Invoke(); }
        }

        public bool ShowTelemetry
        {
            get => _showTelemetry;
            set { if (_showTelemetry == value) return; _showTelemetry = value; Changed?.Invoke(); }
        }

        public ProgressionTimeline Baseline
        {
            get => _baseline;
            set { if (_baseline == value) return; _baseline = value; Changed?.Invoke(); }
        }

        /// <summary>Lookup over the telemetry samples; rebuilt when the data asset changes.</summary>
        public TelemetryIndex TelemetryIndex
        {
            get
            {
                if (_telemetry == null) return null;
                if (_index == null || _index.Data != _telemetry || _indexedCount != _telemetry.Samples.Count)
                {
                    _index = new TelemetryIndex(_telemetry);
                    _indexedCount = _telemetry.Samples.Count;
                }
                return _index;
            }
        }

        /// <summary>Stats for an item, also matching samples keyed by the bound asset's GUID.</summary>
        public bool TryGetStats(TimelineItem item, out TelemetryStats stats)
        {
            stats = default;
            var index = TelemetryIndex;
            if (index == null) return false;
            string guid = item.Binding != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(item.Binding, out string g, out long _) ? g : null;
            return index.TryGet(item, out stats, guid, item.Binding != null ? item.Binding.name : null);
        }

        public void NotifyChanged() => Changed?.Invoke();
    }
}
