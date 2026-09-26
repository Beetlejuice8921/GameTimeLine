using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace TimeMapGameplay.Editor
{
    /// <summary>
    /// Structural and batch edits of a timeline (add/remove/copy/paste/move), all recorded for Undo.
    /// </summary>
    public sealed class TimelineOperations
    {
        /// <summary>Copied items: source track id, item type and JSON. Shared between windows.</summary>
        static readonly List<(string trackId, Type type, string json)> Clipboard = new();

        readonly TimelineEditing _editing;
        readonly Func<ProgressionTimeline> _getTimeline;

        public TimelineOperations(TimelineEditing editing, Func<ProgressionTimeline> getTimeline)
        {
            _editing = editing;
            _getTimeline = getTimeline;
        }

        public static bool HasClipboard => Clipboard.Count > 0;

        ProgressionTimeline Timeline => _getTimeline();

        // ---------- Items ----------

        /// <summary>Largest delta ≤ |<paramref name="delta"/>| that keeps every item inside [0, length].</summary>
        public static float ClampGroupDelta(IEnumerable<TimelineItem> items, float delta, float length)
            => ClampGroupDelta(items.Select(i => (i.Start, i.End)), delta, length);

        /// <summary>Same as above for explicit (start, end) ranges, e.g. positions captured at drag start.</summary>
        public static float ClampGroupDelta(IEnumerable<(float start, float end)> ranges, float delta, float length)
        {
            float minDelta = float.NegativeInfinity, maxDelta = float.PositiveInfinity;
            foreach (var (start, end) in ranges)
            {
                minDelta = Mathf.Max(minDelta, -start);
                maxDelta = Mathf.Min(maxDelta, length - end);
            }
            if (float.IsInfinity(minDelta)) return 0f;
            return Mathf.Clamp(delta, Mathf.Min(0f, minDelta), Mathf.Max(0f, maxDelta));
        }

        /// <summary>Sets starts of several items at once from their original starts plus a shared delta.</summary>
        public void MoveItems(IReadOnlyDictionary<TimelineItem, float> originalStarts, float delta, string undoName = "Move Items")
        {
            if (originalStarts.Count == 0) return;
            bool changed = originalStarts.Any(p => !Mathf.Approximately(p.Key.Start, p.Value + delta));
            if (!changed) return;
            _editing.Apply(undoName, _ =>
            {
                foreach (var pair in originalStarts)
                    pair.Key.Start = pair.Value + delta;
            });
        }

        /// <summary>Shifts items by <paramref name="delta"/>, clamped so the group stays on the axis.</summary>
        public void NudgeItems(IEnumerable<string> ids, float delta)
        {
            var items = Resolve(ids).Select(r => r.item).ToList();
            if (items.Count == 0) return;
            delta = ClampGroupDelta(items, delta, Timeline.Axis.Length);
            MoveItems(items.ToDictionary(i => i, i => i.Start), delta, "Nudge Items");
        }

        public TimelineItem AddItem(TrackBase track, float time)
        {
            var item = track.CreateItem();
            if (item == null) return null;

            int number = track.Items.Count() + 1;
            item.Name = $"{track.Name} {number}";
            if (item is ClipBase clip)
            {
                clip.Duration = Mathf.Max(Timeline.Axis.SnapStep * 4f, ClipBase.MinDuration);
                clip.Start = TimeMath.ClampStart(time, clip.Duration, Timeline.Axis.Length);
            }
            else
            {
                item.Start = Mathf.Clamp(time, 0f, Timeline.Axis.Length);
            }

            _editing.Apply("Add Item", _ => track.AddItem(item));
            return item;
        }

        public void DeleteItems(IEnumerable<string> ids)
        {
            var resolved = Resolve(ids).ToList();
            if (resolved.Count == 0) return;
            _editing.Apply(resolved.Count == 1 ? "Delete Item" : "Delete Items", _ =>
            {
                foreach (var (item, track) in resolved)
                    track.RemoveItem(item);
            });
        }

        public void Copy(IEnumerable<string> ids)
        {
            var resolved = Resolve(ids).ToList();
            if (resolved.Count == 0) return;
            Clipboard.Clear();
            foreach (var (item, track) in resolved.OrderBy(r => r.item.Start))
                Clipboard.Add((track.Id, item.GetType(), EditorJsonUtility.ToJson(item)));
        }

        /// <summary>
        /// Pastes clipboard items so that the earliest one starts at <paramref name="time"/>, keeping relative offsets.
        /// Items go to their source track when it exists, otherwise to the first compatible track.
        /// </summary>
        public List<string> Paste(float time)
        {
            var timeline = Timeline;
            var clones = new List<(TimelineItem item, TrackBase track)>();
            foreach (var (trackId, type, json) in Clipboard)
            {
                var item = Clone(type, json);
                var track = timeline.FindTrack(trackId);
                if (track == null || !track.CanContain(item))
                    track = timeline.Tracks.FirstOrDefault(t => t != null && t.CanContain(item));
                if (track != null)
                    clones.Add((item, track));
            }
            if (clones.Count == 0) return new List<string>();

            float offset = time - clones.Min(c => c.item.Start);
            offset = ClampGroupDelta(clones.Select(c => c.item), offset, timeline.Axis.Length);
            _editing.Apply("Paste Items", _ =>
            {
                foreach (var (item, track) in clones)
                {
                    item.Start += offset;
                    track.AddItem(item);
                }
            });
            return clones.Select(c => c.item.Id).ToList();
        }

        /// <summary>Duplicates items on their own tracks right after the group's end.</summary>
        public List<string> Duplicate(IEnumerable<string> ids)
        {
            var resolved = Resolve(ids).ToList();
            if (resolved.Count == 0) return new List<string>();

            float groupStart = resolved.Min(r => r.item.Start);
            float groupEnd = resolved.Max(r => r.item.End);
            float offset = Mathf.Max(groupEnd - groupStart, Timeline.Axis.SnapStep);
            var clones = resolved.Select(r => (item: Clone(r.item.GetType(), EditorJsonUtility.ToJson(r.item)), r.track)).ToList();
            offset = ClampGroupDelta(clones.Select(c => c.item), offset, Timeline.Axis.Length);

            _editing.Apply("Duplicate Items", _ =>
            {
                foreach (var (item, track) in clones)
                {
                    item.Start += offset;
                    track.AddItem(item);
                }
            });
            return clones.Select(c => c.item.Id).ToList();
        }

        static TimelineItem Clone(Type type, string json)
        {
            var item = (TimelineItem)Activator.CreateInstance(type);
            EditorJsonUtility.FromJsonOverwrite(json, item);
            item.RegenerateId();
            return item;
        }

        IEnumerable<(TimelineItem item, TrackBase track)> Resolve(IEnumerable<string> ids)
        {
            var timeline = Timeline;
            if (timeline == null) yield break;
            foreach (var id in ids.Distinct())
                if (timeline.TryFindItem(id, out var item, out var track))
                    yield return (item, track);
        }

        // ---------- Requirements ----------

        /// <summary>Non-abstract requirement types with menu names (DisplayNameAttribute or nicified class name).</summary>
        public static IEnumerable<(Type type, string name)> RequirementTypes()
            => TypeCache.GetTypesDerivedFrom<Requirement>()
                .Where(t => !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null)
                .Select(t => (t, DisplayName(t)))
                .OrderBy(p => p.Item2);

        public static string DisplayName(Type type)
            => type.GetCustomAttributes(typeof(System.ComponentModel.DisplayNameAttribute), false).FirstOrDefault()
                is System.ComponentModel.DisplayNameAttribute a ? a.DisplayName : ObjectNames.NicifyVariableName(type.Name);

        public Requirement AddRequirement(TimelineItem item, Type type)
        {
            var requirement = (Requirement)Activator.CreateInstance(type);
            _editing.Apply("Add Requirement", _ => item.Requirements.Add(requirement));
            return requirement;
        }

        public void RemoveRequirement(TimelineItem item, int index)
        {
            if (index < 0 || index >= item.Requirements.Count) return;
            _editing.Apply("Remove Requirement", _ => item.Requirements.RemoveAt(index));
        }

        // ---------- Tracks ----------

        /// <summary>Non-abstract track types available for "Add Track", with menu names.</summary>
        public static IEnumerable<(Type type, string name)> TrackTypes()
            => TypeCache.GetTypesDerivedFrom<TrackBase>()
                .Where(t => !t.IsAbstract && !t.IsGenericTypeDefinition && t.GetConstructor(Type.EmptyTypes) != null)
                .Select(t => (t, t.GetCustomAttributes(typeof(TrackMenuAttribute), false).FirstOrDefault() is TrackMenuAttribute a ? a.Path : ObjectNames.NicifyVariableName(t.Name)))
                .OrderBy(p => p.Item2);

        public TrackBase AddTrack(Type type, string name)
        {
            var track = (TrackBase)Activator.CreateInstance(type);
            track.Name = name;
            track.Color = Color.HSVToRGB(UnityEngine.Random.value, 0.45f, 0.85f);
            _editing.Apply("Add Track", tl => tl.Tracks.Add(track));
            return track;
        }

        public void RemoveTrack(TrackBase track)
        {
            _editing.Apply("Remove Track", tl => tl.Tracks.Remove(track));
        }

        public void MoveTrack(TrackBase track, int direction)
        {
            var tracks = Timeline.Tracks;
            int index = tracks.IndexOf(track);
            int target = index + direction;
            if (index < 0 || target < 0 || target >= tracks.Count) return;
            _editing.Apply("Reorder Tracks", tl =>
            {
                tl.Tracks.RemoveAt(index);
                tl.Tracks.Insert(target, track);
            });
        }

        // ---------- Curve keys ----------

        public int AddCurveKey(CurveTrack track, float time)
        {
            var key = new CurveKey(time, SnapValue(track, track.EvaluateValue(time), true));
            int index = 0;
            _editing.Apply("Add Curve Key", _ =>
            {
                track.Keys.Add(key);
                track.SortKeys();
                index = track.Keys.IndexOf(key);
            });
            return index;
        }

        public void RemoveCurveKey(CurveTrack track, int index)
        {
            if (index < 0 || index >= track.Keys.Count) return;
            _editing.Apply("Remove Curve Key", _ => track.Keys.RemoveAt(index));
        }

        /// <summary>
        /// Moves a key, keeping it between its neighbours (so key order never changes during a drag).
        /// </summary>
        public void SetCurveKey(CurveTrack track, int index, float time, float value, bool snap)
        {
            var keys = track.Keys;
            if (index < 0 || index >= keys.Count) return;

            float step = Timeline.Axis.SnapStep;
            if (snap) time = TimeMath.Snap(time, step);
            float gap = snap ? step : 0.01f;
            float minTime = index > 0 ? keys[index - 1].time + gap : 0f;
            float maxTime = index < keys.Count - 1 ? keys[index + 1].time - gap : Timeline.Axis.Length;
            time = maxTime < minTime ? keys[index].time : Mathf.Clamp(time, minTime, maxTime);
            value = SnapValue(track, value, snap);

            var key = new CurveKey(time, value);
            if (Mathf.Approximately(keys[index].time, key.time) && Mathf.Approximately(keys[index].value, key.value)) return;
            _editing.Apply("Move Curve Key", _ => keys[index] = key);
        }

        static float SnapValue(CurveTrack track, float value, bool snap)
        {
            if (snap && track.ValueStep > 0f) value = TimeMath.Snap(value, track.ValueStep);
            return Mathf.Clamp(value, Mathf.Min(track.ValueMin, track.ValueMax), Mathf.Max(track.ValueMin, track.ValueMax));
        }
    }
}
