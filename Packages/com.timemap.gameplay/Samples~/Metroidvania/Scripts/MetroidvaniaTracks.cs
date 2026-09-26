using System;
using System.ComponentModel;
using UnityEngine;

namespace TimeMapGameplay.Samples.Metroidvania
{
    // Example of project-specific track types. They appear in "+ Трек" automatically;
    // their fields show up in the timeline inspector without editor code.

    /// <summary>A traversal ability (double jump, dash, wall climb...).</summary>
    [Serializable]
    public sealed class AbilityMarker : MarkerBase
    {
        [SerializeField, Tooltip("What the ability opens up, for designers.")]
        string _unlocks = "";

        public string Unlocks { get => _unlocks; set => _unlocks = value; }
    }

    [Serializable, TrackMenu("Metroidvania/Способности")]
    public sealed class AbilityTrack : MarkerTrack
    {
        public override Type ItemType => typeof(AbilityMarker);
    }

    /// <summary>A boss fight.</summary>
    [Serializable]
    public sealed class BossMarker : MarkerBase
    {
        [SerializeField, Min(1)] int _hitPoints = 100;
        [SerializeField, Tooltip("Reward: an ability or an item.")] string _reward = "";

        public int HitPoints { get => _hitPoints; set => _hitPoints = value; }
        public string Reward { get => _reward; set => _reward = value; }
    }

    [Serializable, TrackMenu("Metroidvania/Боссы")]
    public sealed class BossTrack : MarkerTrack
    {
        public override Type ItemType => typeof(BossMarker);
    }

    /// <summary>
    /// Example of a project requirement type: the item needs an ability unlocked by its start
    /// (e.g. a zone needs the double jump). Picked up by the validator and the "+ Требование" menu.
    /// </summary>
    [Serializable, DisplayName("Способность (Metroidvania)")]
    public sealed class AbilityRequirement : ItemAvailableRequirement
    {
        [SerializeField, ItemReference(typeof(AbilityMarker))] ItemReference _ability;

        public AbilityRequirement() { }
        public AbilityRequirement(string abilityId) => _ability = new ItemReference(abilityId);

        protected override ItemReference Target => _ability;
        protected override string Noun => "Способность";
        protected override string Verb => "она открывается";
    }
}
