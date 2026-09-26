using System.Collections.Generic;
using System.Linq;

namespace TimeMapGameplay
{
    /// <summary>Pure query: game time → full progression state.</summary>
    public interface IProgressionQuery
    {
        ProgressionState Evaluate(float time);
    }

    /// <summary>State of every track at a moment in time (the "game slice").</summary>
    public sealed class ProgressionState
    {
        readonly List<TrackState> _tracks;

        public ProgressionState(float time, List<TrackState> tracks, AxisUnit units = AxisUnit.Hours)
        {
            Time = time;
            _tracks = tracks;
            Units = units;
        }

        public float Time { get; }

        /// <summary>Units of the timeline axis, for formatting times.</summary>
        public AxisUnit Units { get; }

        /// <summary>Formats a time in the timeline units.</summary>
        public string Format(float time) => TimeMath.Format(time, Units);

        public IReadOnlyList<TrackState> Tracks => _tracks;

        /// <summary>State of the given track, or null if it is not part of this state.</summary>
        public TrackState Of(TrackBase track) => _tracks.FirstOrDefault(s => s.Track == track);

        /// <summary>State of the first track of type <typeparamref name="TTrack"/>.</summary>
        public TState FirstOf<TTrack, TState>()
            where TTrack : TrackBase
            where TState : TrackState
            => _tracks.FirstOrDefault(s => s.Track is TTrack) as TState;

        /// <summary>Hero level from the first <see cref="LevelCurveTrack"/>, or null when there is none.</summary>
        public float? Level => FirstOf<LevelCurveTrack, CurveTrackState>()?.Value;
    }

    /// <summary>State of one track at a moment in time.</summary>
    public abstract class TrackState
    {
        protected TrackState(TrackBase track, float time)
        {
            Track = track;
            Time = time;
        }

        public TrackBase Track { get; }
        public float Time { get; }
    }

    public sealed class ClipTrackState : TrackState
    {
        public ClipTrackState(TrackBase track, float time, List<ClipBase> active, List<ClipBase> started, int total)
            : base(track, time)
        {
            Active = active;
            Started = started;
            Total = total;
        }

        /// <summary>Clips containing the time, sorted by start.</summary>
        public IReadOnlyList<ClipBase> Active { get; }

        /// <summary>Clips with Start &lt;= time (active and finished), sorted by start.</summary>
        public IReadOnlyList<ClipBase> Started { get; }

        public int Total { get; }

        /// <summary>Earliest active clip, or null.</summary>
        public ClipBase Current => Active.Count > 0 ? Active[0] : null;

        /// <summary>0..1 progress through <see cref="Current"/>.</summary>
        public float CurrentProgress
            => Current == null ? 0f : UnityEngine.Mathf.InverseLerp(Current.Start, Current.End, Time);
    }

    public sealed class MarkerTrackState : TrackState
    {
        public MarkerTrackState(TrackBase track, float time, List<MarkerBase> reached, List<MarkerBase> upcoming)
            : base(track, time)
        {
            Reached = reached;
            Upcoming = upcoming;
        }

        /// <summary>Markers with Start &lt;= time, sorted by start.</summary>
        public IReadOnlyList<MarkerBase> Reached { get; }

        /// <summary>Markers after time, sorted by start.</summary>
        public IReadOnlyList<MarkerBase> Upcoming { get; }

        public int Total => Reached.Count + Upcoming.Count;
        public MarkerBase Latest => Reached.Count > 0 ? Reached[Reached.Count - 1] : null;
        public MarkerBase Next => Upcoming.Count > 0 ? Upcoming[0] : null;
    }

    public sealed class CurveTrackState : TrackState
    {
        public CurveTrackState(TrackBase track, float time, float value) : base(track, time) => Value = value;

        public float Value { get; }
    }
}
