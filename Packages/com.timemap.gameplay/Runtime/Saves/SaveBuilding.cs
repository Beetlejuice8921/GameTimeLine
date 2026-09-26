using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TimeMapGameplay
{
    /// <summary>
    /// Converts a progression state into a game save. Implemented by the project, one class per save format;
    /// the active builder is chosen in Project Settings → TimeMapGameplay. Needs a public parameterless constructor.
    /// </summary>
    public interface ISaveBuilder
    {
        /// <summary>Stable id stored in settings and play requests, e.g. "mygame.binary".</summary>
        string Id { get; }

        string DisplayName { get; }

        /// <summary>File extension without the dot.</summary>
        string FileExtension { get; }

        byte[] Build(ProgressionState state, SaveBuildContext context);
    }

    /// <summary>Extra data available to save builders.</summary>
    public sealed class SaveBuildContext
    {
        public SaveBuildContext(ProgressionTimeline timeline, Func<Object, string> assetKeyResolver = null)
        {
            Timeline = timeline;
            AssetKeyResolver = assetKeyResolver;
        }

        public ProgressionTimeline Timeline { get; }

        /// <summary>Maps a bound asset to a stable key (GUID in the editor). May be null.</summary>
        public Func<Object, string> AssetKeyResolver { get; }

        public string AssetKey(Object asset) => asset == null ? "" : AssetKeyResolver?.Invoke(asset) ?? asset.name;
    }

    /// <summary>
    /// Format-neutral dump of a <see cref="ProgressionState"/>. Written by <see cref="JsonSaveBuilder"/>
    /// and usable by game code to apply the state without a custom save format.
    /// </summary>
    [Serializable]
    public sealed class ProgressionSnapshot
    {
        public string timeline;
        public float time;
        public bool hasLevel;
        public float level;
        public List<TrackSnapshot> tracks = new();

        public TrackSnapshot FindTrack(string nameOrType)
            => tracks.Find(t => t.name == nameOrType || t.type == nameOrType);

        public static ProgressionSnapshot From(ProgressionState state, SaveBuildContext context)
        {
            var snapshot = new ProgressionSnapshot
            {
                timeline = context.Timeline != null ? context.Timeline.name : "",
                time = state.Time,
                hasLevel = state.Level.HasValue,
                level = state.Level ?? 0f
            };

            foreach (var trackState in state.Tracks)
            {
                var track = new TrackSnapshot
                {
                    id = trackState.Track.Id,
                    name = trackState.Track.Name,
                    type = trackState.Track.GetType().Name
                };
                switch (trackState)
                {
                    case ClipTrackState clips:
                        foreach (var c in clips.Active) track.active.Add(ItemSnapshot.From(c, context));
                        foreach (var c in clips.Started) track.reached.Add(ItemSnapshot.From(c, context));
                        break;
                    case MarkerTrackState markers:
                        foreach (var m in markers.Reached) track.reached.Add(ItemSnapshot.From(m, context));
                        break;
                    case CurveTrackState curve:
                        track.value = curve.Value;
                        break;
                }
                snapshot.tracks.Add(track);
            }
            return snapshot;
        }
    }

    [Serializable]
    public sealed class TrackSnapshot
    {
        public string id;
        public string name;
        public string type;
        public float value;

        /// <summary>Clips containing the time.</summary>
        public List<ItemSnapshot> active = new();

        /// <summary>Clips/markers that started by the time (for clips includes active ones).</summary>
        public List<ItemSnapshot> reached = new();
    }

    [Serializable]
    public sealed class ItemSnapshot
    {
        public string id;
        public string name;
        public string type;
        public float start;
        public float end;

        /// <summary>Key of the bound asset (GUID in the editor), empty when unbound.</summary>
        public string asset;

        public static ItemSnapshot From(TimelineItem item, SaveBuildContext context) => new()
        {
            id = item.Id,
            name = item.Name,
            type = item.GetType().Name,
            start = item.Start,
            end = item.End,
            asset = context.AssetKey(item.Binding)
        };
    }

    /// <summary>Built-in format: <see cref="ProgressionSnapshot"/> as JSON. Works with no project integration.</summary>
    public sealed class JsonSaveBuilder : ISaveBuilder
    {
        public const string BuilderId = "timemap.json";

        public string Id => BuilderId;
        public string DisplayName => "JSON (ProgressionSnapshot)";
        public string FileExtension => "json";

        public byte[] Build(ProgressionState state, SaveBuildContext context)
            => Encoding.UTF8.GetBytes(JsonUtility.ToJson(ProgressionSnapshot.From(state, context), true));
    }
}
