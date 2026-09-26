using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace TimeMapGameplay
{
    public enum ChangeKind
    {
        TrackAdded,
        TrackRemoved,
        Added,
        Removed,
        Moved,
        Resized,
        Renamed,
        Edited,
        CurveChanged,
        AxisChanged
    }

    /// <summary>One difference between a baseline version of a timeline and the current one.</summary>
    public sealed class TimelineChange
    {
        public ChangeKind Kind;
        public string TrackId;
        public string TrackName;
        public string ItemId;
        public string ItemName;
        public float OldStart;
        public float OldDuration;
        public float NewStart;
        public float NewDuration;
        public string Description;

        /// <summary>True when the baseline position is worth showing as a ghost on the timeline.</summary>
        public bool HasGhost => Kind is ChangeKind.Removed or ChangeKind.Moved or ChangeKind.Resized;

        public override string ToString() => Description;
    }

    /// <summary>
    /// Structural comparison of two timelines (e.g. an older saved version and the current asset).
    /// Items and tracks are matched by id, so moves and renames are recognised.
    /// </summary>
    public static class TimelineDiff
    {
        public static List<TimelineChange> Compare(ProgressionTimeline baseline, ProgressionTimeline current)
        {
            var changes = new List<TimelineChange>();
            var units = current.Axis.Units;
            string F(float t) => TimeMath.Format(t, units);

            if (!Mathf.Approximately(baseline.Axis.Length, current.Axis.Length) || baseline.Axis.Units != current.Axis.Units)
                changes.Add(new TimelineChange
                {
                    Kind = ChangeKind.AxisChanged,
                    Description = $"Ось: {TimeMath.Format(baseline.Axis.Length, baseline.Axis.Units)} {baseline.Axis.Units} → {F(current.Axis.Length)} {units}"
                });

            var oldTracks = baseline.Tracks.Where(t => t != null).ToDictionary(t => t.Id);
            var newTracks = current.Tracks.Where(t => t != null).ToDictionary(t => t.Id);

            foreach (var track in current.Tracks.Where(t => t != null && !oldTracks.ContainsKey(t.Id)))
                changes.Add(new TimelineChange { Kind = ChangeKind.TrackAdded, TrackId = track.Id, TrackName = track.Name, Description = $"Новый трек «{track.Name}»" });
            foreach (var track in baseline.Tracks.Where(t => t != null && !newTracks.ContainsKey(t.Id)))
                changes.Add(new TimelineChange { Kind = ChangeKind.TrackRemoved, TrackId = track.Id, TrackName = track.Name, Description = $"Удалён трек «{track.Name}»" });

            var oldItems = Items(baseline);
            var newItems = Items(current);

            foreach (var (id, (item, track)) in newItems)
            {
                if (!oldItems.TryGetValue(id, out var old))
                {
                    changes.Add(Change(ChangeKind.Added, track, item, null, $"«{item.Name}» добавлен в {F(item.Start)} ({track.Name})"));
                    continue;
                }

                var before = old.item;
                bool moved = !Mathf.Approximately(before.Start, item.Start);
                bool resized = before is ClipBase oc && item is ClipBase nc && !Mathf.Approximately(oc.Duration, nc.Duration);
                if (moved)
                    changes.Add(Change(ChangeKind.Moved, track, item, before,
                        $"«{item.Name}»: {F(before.Start)} → {F(item.Start)} ({Signed(item.Start - before.Start, units)})"));
                if (resized)
                    changes.Add(Change(ChangeKind.Resized, track, item, before,
                        $"«{item.Name}»: длительность {F(((ClipBase)before).Duration)} → {F(((ClipBase)item).Duration)}"));
                if (before.Name != item.Name)
                    changes.Add(Change(ChangeKind.Renamed, track, item, before, $"«{before.Name}» переименован в «{item.Name}»"));
                if (FieldsDiffer(before, item))
                    changes.Add(Change(ChangeKind.Edited, track, item, before, $"«{item.Name}»: изменены свойства"));
            }

            foreach (var (id, (item, track)) in oldItems)
                if (!newItems.ContainsKey(id))
                    changes.Add(Change(ChangeKind.Removed, track, null, item, $"«{item.Name}» удалён (был в {F(item.Start)}, {track.Name})"));

            foreach (var track in current.Tracks.OfType<CurveTrack>())
                if (oldTracks.TryGetValue(track.Id, out var oldTrack) && oldTrack is CurveTrack oldCurve && !SameKeys(oldCurve, track))
                    changes.Add(new TimelineChange { Kind = ChangeKind.CurveChanged, TrackId = track.Id, TrackName = track.Name, Description = $"Изменена кривая «{track.Name}»" });

            return changes;
        }

        static TimelineChange Change(ChangeKind kind, TrackBase track, TimelineItem current, TimelineItem baseline, string description) => new()
        {
            Kind = kind,
            TrackId = track.Id,
            TrackName = track.Name,
            ItemId = (current ?? baseline).Id,
            ItemName = (current ?? baseline).Name,
            OldStart = baseline?.Start ?? 0f,
            OldDuration = baseline is ClipBase oc ? oc.Duration : 0f,
            NewStart = current?.Start ?? 0f,
            NewDuration = current is ClipBase nc ? nc.Duration : 0f,
            Description = description
        };

        static Dictionary<string, (TimelineItem item, TrackBase track)> Items(ProgressionTimeline timeline)
        {
            var map = new Dictionary<string, (TimelineItem, TrackBase)>();
            foreach (var track in timeline.Tracks.Where(t => t != null))
            foreach (var item in track.Items.Where(i => i != null))
                map[item.Id] = (item, track);
            return map;
        }

        /// <summary>Compares everything except position, duration and name (those are reported separately).</summary>
        static bool FieldsDiffer(TimelineItem a, TimelineItem b)
        {
            if (a.GetType() != b.GetType()) return true;
            return Normalized(a) != Normalized(b);
        }

        static string Normalized(TimelineItem item)
        {
            var copy = (TimelineItem)JsonUtility.FromJson(JsonUtility.ToJson(item), item.GetType());
            copy.Start = 0f;
            copy.Name = "";
            if (copy is ClipBase clip) clip.Duration = 1f;
            return JsonUtility.ToJson(copy);
        }

        static bool SameKeys(CurveTrack a, CurveTrack b)
        {
            var ka = a.Keys.OrderBy(k => k.time).ToList();
            var kb = b.Keys.OrderBy(k => k.time).ToList();
            return ka.Count == kb.Count && ka.Zip(kb, (x, y) => Mathf.Approximately(x.time, y.time) && Mathf.Approximately(x.value, y.value)).All(s => s);
        }

        static string Signed(float delta, AxisUnit units) => (delta >= 0f ? "+" : "−") + TimeMath.Format(Math.Abs(delta), units);
    }
}
