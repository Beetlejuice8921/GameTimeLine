using System.Collections.Generic;
using UnityEngine;

namespace TimeMapGameplay
{
    /// <summary>Progression timeline asset: a game-time axis and a list of tracks.</summary>
    [CreateAssetMenu(menuName = "TimeMapGameplay/Progression Timeline", fileName = "NewProgression", order = 0)]
    public class ProgressionTimeline : ScriptableObject, IProgressionQuery
    {
        [SerializeField] TimeAxis _axis = new();
        [SerializeReference] List<TrackBase> _tracks = new();

        public TimeAxis Axis => _axis;
        public List<TrackBase> Tracks => _tracks;

        /// <summary>State of all tracks at <paramref name="time"/>. Pure: does not modify the asset.</summary>
        public ProgressionState Evaluate(float time)
        {
            var states = new List<TrackState>(_tracks.Count);
            foreach (var track in _tracks)
                if (track != null)
                    states.Add(track.Evaluate(time));
            return new ProgressionState(time, states, _axis.Units);
        }

        /// <summary>Finds an item by id across all tracks.</summary>
        public bool TryFindItem(string id, out TimelineItem item, out TrackBase track)
        {
            foreach (var t in _tracks)
            {
                if (t == null) continue;
                foreach (var i in t.Items)
                {
                    if (i != null && i.Id == id)
                    {
                        item = i;
                        track = t;
                        return true;
                    }
                }
            }
            item = null;
            track = null;
            return false;
        }

        /// <summary>Finds a track by id.</summary>
        public TrackBase FindTrack(string id)
        {
            foreach (var t in _tracks)
                if (t != null && t.Id == id)
                    return t;
            return null;
        }
    }
}
