using System.Collections.Generic;
using System.Linq;

namespace TimeMapGameplay
{
    /// <summary>What changed on one track between two slices (A → B).</summary>
    public sealed class TrackDelta
    {
        public TrackDelta(TrackBase track) => Track = track;

        public TrackBase Track { get; }

        /// <summary>Items reached/started in B but not in A.</summary>
        public List<TimelineItem> Gained { get; } = new();

        /// <summary>Items reached/started in A but not in B (B earlier than A).</summary>
        public List<TimelineItem> Lost { get; } = new();

        /// <summary>Current clip in A and B (clip tracks only).</summary>
        public ClipBase CurrentA { get; set; }
        public ClipBase CurrentB { get; set; }

        /// <summary>Curve values (curve tracks only).</summary>
        public float? ValueA { get; set; }
        public float? ValueB { get; set; }

        public bool CurrentChanged => CurrentA != CurrentB;
        public bool ValueChanged => ValueA.HasValue && ValueB.HasValue && !UnityEngine.Mathf.Approximately(ValueA.Value, ValueB.Value);
        public bool HasChanges => Gained.Count > 0 || Lost.Count > 0 || CurrentChanged || ValueChanged;
    }

    /// <summary>Compares two slices of the same timeline.</summary>
    public static class ProgressionDiff
    {
        public static List<TrackDelta> Compare(ProgressionState a, ProgressionState b)
        {
            var result = new List<TrackDelta>();
            foreach (var stateA in a.Tracks)
            {
                var stateB = b.Of(stateA.Track);
                if (stateB == null) continue;
                var delta = new TrackDelta(stateA.Track);
                switch (stateA)
                {
                    case ClipTrackState clipsA when stateB is ClipTrackState clipsB:
                        Diff(clipsA.Started, clipsB.Started, delta);
                        delta.CurrentA = clipsA.Current;
                        delta.CurrentB = clipsB.Current;
                        break;
                    case MarkerTrackState markersA when stateB is MarkerTrackState markersB:
                        Diff(markersA.Reached, markersB.Reached, delta);
                        break;
                    case CurveTrackState curveA when stateB is CurveTrackState curveB:
                        delta.ValueA = curveA.Value;
                        delta.ValueB = curveB.Value;
                        break;
                }
                result.Add(delta);
            }
            return result;
        }

        static void Diff<T>(IReadOnlyList<T> a, IReadOnlyList<T> b, TrackDelta delta) where T : TimelineItem
        {
            var setA = new HashSet<T>(a);
            var setB = new HashSet<T>(b);
            delta.Gained.AddRange(b.Where(i => !setA.Contains(i)));
            delta.Lost.AddRange(a.Where(i => !setB.Contains(i)));
        }
    }
}
