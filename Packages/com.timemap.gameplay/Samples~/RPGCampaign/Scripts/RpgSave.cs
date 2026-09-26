using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

namespace TimeMapGameplay.Samples.RPG
{
    /// <summary>The sample game's own save format (what a real project would already have).</summary>
    [Serializable]
    public sealed class RpgSave
    {
        public int version = 1;
        public float playTimeHours;
        public int level;
        public string currentQuestId;
        public List<string> skills = new();
        public List<string> openedLocations = new();
        public List<string> equipment = new();
        public List<string> knownLore = new();
    }

    /// <summary>
    /// ProgressionState → <see cref="RpgSave"/>. Select "RPG sample save" in
    /// Project Settings → TimeMapGameplay to use it for "Create Save" and "Play Mode from t".
    /// </summary>
    public sealed class RpgSaveBuilder : ISaveBuilder
    {
        public const string BuilderId = "sample.rpg";

        public string Id => BuilderId;
        public string DisplayName => "RPG sample save";
        public string FileExtension => "rpgsave";

        public byte[] Build(ProgressionState state, SaveBuildContext context)
        {
            var save = new RpgSave
            {
                playTimeHours = state.Time,
                level = Mathf.FloorToInt((state.Level ?? 1f) + 0.0001f)
            };

            // Bound quest assets carry the game's ids; unbound acts fall back to the clip name.
            var act = state.FirstOf<PlotTrack, ClipTrackState>()?.Current;
            if (act != null)
                save.currentQuestId = act.Binding is QuestAsset quest && !string.IsNullOrEmpty(quest.questId) ? quest.questId : act.Name;

            save.skills.AddRange(Names(state.FirstOf<SkillTrack, MarkerTrackState>()?.Reached));
            save.openedLocations.AddRange(Names(state.FirstOf<LocationTrack, MarkerTrackState>()?.Reached));
            save.knownLore.AddRange(Names(state.FirstOf<FabulaTrack, ClipTrackState>()?.Started));

            // Latest upgrade per slot is what the hero wears.
            var upgrades = state.FirstOf<UpgradeTrack, MarkerTrackState>()?.Reached.OfType<UpgradeMarker>();
            if (upgrades != null)
                save.equipment.AddRange(upgrades.GroupBy(u => string.IsNullOrEmpty(u.Slot) ? u.Id : u.Slot).Select(g => g.Last().Name));

            return Encoding.UTF8.GetBytes(JsonUtility.ToJson(save, true));
        }

        static IEnumerable<string> Names(IEnumerable<TimelineItem> items)
            => items == null ? Enumerable.Empty<string>() : items.Select(i => i.Name);
    }

    /// <summary>Loads <see cref="RpgSave"/> when Play Mode starts from the timeline.</summary>
    public sealed class RpgBootstrap : ITimelineBootstrap
    {
        public bool CanLoad(PlayRequest request) => request.saveBuilderId == RpgSaveBuilder.BuilderId;

        public void Load(PlayRequest request)
        {
            var save = JsonUtility.FromJson<RpgSave>(File.ReadAllText(request.savePath, Encoding.UTF8));
            RpgGameState.Current = save;
            Debug.Log($"[RPG sample] Loaded save: level {save.level}, quest '{save.currentQuestId}', " +
                      $"skills [{string.Join(", ", save.skills)}], equipment [{string.Join(", ", save.equipment)}].");
        }
    }

    /// <summary>Where the sample "game" keeps its state. A real project would apply the save to its systems here.</summary>
    public static class RpgGameState
    {
        public static RpgSave Current { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void Reset() => Current = null;
    }
}
