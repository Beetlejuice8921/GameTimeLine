using System;
using System.Collections.Generic;
using UnityEngine;

namespace TimeMapGameplay
{
    /// <summary>Track holding point-in-time markers.</summary>
    [Serializable, TrackMenu("Маркеры")]
    public class MarkerTrack : TrackBase
    {
        [SerializeReference, HideInInspector] List<MarkerBase> _markers = new();

        public List<MarkerBase> Markers => _markers;
        public override IEnumerable<TimelineItem> Items => _markers;
        public override Type ItemType => typeof(MarkerBase);

        public override void AddItem(TimelineItem item) => _markers.Add((MarkerBase)item);
        public override bool RemoveItem(TimelineItem item) => item is MarkerBase marker && _markers.Remove(marker);

        /// <summary>Markers reached by <paramref name="time"/>, i.e. with Start &lt;= time.</summary>
        public IEnumerable<MarkerBase> MarkersReachedBy(float time)
        {
            foreach (var marker in _markers)
                if (marker != null && marker.Start <= time)
                    yield return marker;
        }

        public override TrackState Evaluate(float time)
        {
            var reached = new List<MarkerBase>();
            var upcoming = new List<MarkerBase>();
            foreach (var marker in _markers)
            {
                if (marker == null) continue;
                (marker.Start <= time ? reached : upcoming).Add(marker);
            }
            reached.Sort(TimelineItem.CompareByStart);
            upcoming.Sort(TimelineItem.CompareByStart);
            return new MarkerTrackState(this, time, reached, upcoming);
        }
    }
}
