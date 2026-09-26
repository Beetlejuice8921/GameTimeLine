using System;
using System.Collections.Generic;
using UnityEngine;

namespace TimeMapGameplay
{
    /// <summary>Track holding clips (items with duration).</summary>
    [Serializable, TrackMenu("Клипы")]
    public class ClipTrack : TrackBase
    {
        [SerializeReference, HideInInspector] List<ClipBase> _clips = new();

        public List<ClipBase> Clips => _clips;
        public override IEnumerable<TimelineItem> Items => _clips;
        public override Type ItemType => typeof(ClipBase);

        public override void AddItem(TimelineItem item) => _clips.Add((ClipBase)item);
        public override bool RemoveItem(TimelineItem item) => item is ClipBase clip && _clips.Remove(clip);

        /// <summary>Clips that contain <paramref name="time"/>.</summary>
        public IEnumerable<ClipBase> ClipsAt(float time)
        {
            foreach (var clip in _clips)
                if (clip != null && clip.Contains(time))
                    yield return clip;
        }

        public override TrackState Evaluate(float time)
        {
            var active = new List<ClipBase>();
            var started = new List<ClipBase>();
            int total = 0;
            foreach (var clip in _clips)
            {
                if (clip == null) continue;
                total++;
                if (clip.Start <= time) started.Add(clip);
                if (clip.Contains(time)) active.Add(clip);
            }
            active.Sort(TimelineItem.CompareByStart);
            started.Sort(TimelineItem.CompareByStart);
            return new ClipTrackState(this, time, active, started, total);
        }
    }
}
