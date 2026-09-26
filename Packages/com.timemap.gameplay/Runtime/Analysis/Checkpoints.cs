using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TimeMapGameplay
{
    /// <summary>A QA checkpoint: a moment for which a save should be generated.</summary>
    [Serializable]
    public sealed class CheckpointMarker : MarkerBase
    {
        [SerializeField, TextArea(1, 4), Tooltip("What QA should check from this point.")]
        string _notes = "";

        public string Notes { get => _notes; set => _notes = value; }
    }

    [Serializable, TrackMenu("Контрольные точки")]
    public sealed class CheckpointTrack : MarkerTrack
    {
        public override Type ItemType => typeof(CheckpointMarker);
    }

    public readonly struct Checkpoint
    {
        public Checkpoint(float time, string name, string source)
        {
            Time = time;
            Name = name;
            Source = source;
        }

        public float Time { get; }
        public string Name { get; }

        /// <summary>Where the checkpoint came from: "act" or "marker".</summary>
        public string Source { get; }
    }

    public static class Checkpoints
    {
        /// <summary>
        /// Checkpoints for batch saves: the start of every clip on plot tracks and every checkpoint marker.
        /// Sorted by time; points closer than <paramref name="mergeDistance"/> are merged (markers win the name).
        /// </summary>
        public static List<Checkpoint> Collect(ProgressionTimeline timeline, bool includeActs = true, bool includeMarkers = true,
            float mergeDistance = 0.001f)
        {
            var points = new List<Checkpoint>();
            foreach (var track in timeline.Tracks)
            {
                if (includeActs && track is PlotTrack plot)
                    points.AddRange(plot.Clips.Where(c => c != null).Select(c => new Checkpoint(c.Start, c.Name, "act")));
                if (includeMarkers && track is CheckpointTrack markers)
                    points.AddRange(markers.Markers.Where(m => m != null).Select(m => new Checkpoint(m.Start, m.Name, "marker")));
            }

            var result = new List<Checkpoint>();
            foreach (var point in points.OrderBy(p => p.Time).ThenBy(p => p.Source == "marker" ? 0 : 1))
            {
                if (result.Count > 0 && Mathf.Abs(result[^1].Time - point.Time) <= mergeDistance) continue;
                result.Add(point);
            }
            return result;
        }
    }
}
