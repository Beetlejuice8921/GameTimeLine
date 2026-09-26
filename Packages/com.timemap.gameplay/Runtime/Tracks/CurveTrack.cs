using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TimeMapGameplay
{
    /// <summary>A key of a <see cref="CurveTrack"/>: value at a point in time.</summary>
    [Serializable]
    public struct CurveKey
    {
        public float time;
        public float value;

        public CurveKey(float time, float value)
        {
            this.time = time;
            this.value = value;
        }
    }

    /// <summary>Piecewise-linear value over time, e.g. hero level.</summary>
    [Serializable, TrackMenu("Кривая")]
    public class CurveTrack : TrackBase
    {
        [SerializeField] float _valueMin = 1f;
        [SerializeField] float _valueMax = 30f;
        [SerializeField, Min(0f), Tooltip("Snap step for values when dragging keys; 0 disables value snapping.")]
        float _valueStep = 1f;
        [SerializeField, Tooltip("Short prefix for value labels, e.g. \"ур.\"")]
        string _valueLabel = "";
        [SerializeField] List<CurveKey> _keys = new();

        public float ValueMin { get => _valueMin; set => _valueMin = value; }
        public float ValueMax { get => _valueMax; set => _valueMax = value; }
        public float ValueStep { get => _valueStep; set => _valueStep = Mathf.Max(0f, value); }
        public string ValueLabel { get => _valueLabel; set => _valueLabel = value; }

        /// <summary>Keys sorted by time. Use <see cref="SortKeys"/> after changing times directly.</summary>
        public List<CurveKey> Keys => _keys;

        public override IEnumerable<TimelineItem> Items => Enumerable.Empty<TimelineItem>();

        public void SortKeys() => _keys.Sort((a, b) => a.time.CompareTo(b.time));

        /// <summary>
        /// Linear interpolation between neighbouring keys; clamps to the first/last key outside their range.
        /// Works with unsorted keys. Returns <see cref="ValueMin"/> when there are no keys.
        /// </summary>
        public float EvaluateValue(float time)
        {
            if (_keys.Count == 0) return _valueMin;

            bool hasPrev = false, hasNext = false;
            CurveKey prev = default, next = default;
            foreach (var key in _keys)
            {
                if (key.time <= time)
                {
                    if (!hasPrev || key.time >= prev.time) { prev = key; hasPrev = true; }
                }
                else if (!hasNext || key.time < next.time)
                {
                    next = key;
                    hasNext = true;
                }
            }

            if (!hasPrev) return next.value;
            if (!hasNext) return prev.value;
            float span = next.time - prev.time;
            return span <= 0f ? next.value : Mathf.Lerp(prev.value, next.value, (time - prev.time) / span);
        }

        /// <summary>Earliest time at which the curve reaches <paramref name="value"/>, or null if never.</summary>
        public float? FirstTimeReaching(float value)
        {
            if (_keys.Count == 0) return null;
            var sorted = _keys.OrderBy(k => k.time).ToList();
            if (sorted[0].value >= value) return sorted[0].time;
            for (int i = 1; i < sorted.Count; i++)
            {
                var a = sorted[i - 1];
                var b = sorted[i];
                if (b.value >= value && a.value < value)
                    return Mathf.Lerp(a.time, b.time, Mathf.InverseLerp(a.value, b.value, value));
            }
            return null;
        }

        public override TrackState Evaluate(float time) => new CurveTrackState(this, time, EvaluateValue(time));
    }

    /// <summary>Hero level curve. The slice uses the first track of this type as "the" level.</summary>
    [Serializable, TrackMenu("Прогресс (уровень)")]
    public class LevelCurveTrack : CurveTrack
    {
    }
}
