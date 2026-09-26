using System;
using UnityEngine;

namespace TimeMapGameplay
{
    /// <summary>Units in which the timeline axis is displayed.</summary>
    public enum AxisUnit
    {
        Hours,
        Sessions,
        Quests
    }

    /// <summary>Game-time axis of a timeline: units, total length and snap step.</summary>
    [Serializable]
    public class TimeAxis
    {
        [SerializeField] AxisUnit _units = AxisUnit.Hours;
        [SerializeField, Min(1f)] float _length = 20f;
        [SerializeField, Min(0.001f)] float _snapStep = 0.25f;

        public AxisUnit Units { get => _units; set => _units = value; }

        /// <summary>Total axis length in axis units.</summary>
        public float Length { get => _length; set => _length = Mathf.Max(1f, value); }

        /// <summary>Snap step in axis units (0.25 hours = 15 minutes by default).</summary>
        public float SnapStep { get => _snapStep; set => _snapStep = Mathf.Max(0.001f, value); }
    }
}
