using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TimeMapGameplay
{
    /// <summary>Base for anything placed on a track: clips and markers.</summary>
    [Serializable]
    public abstract class TimelineItem
    {
        [SerializeField, HideInInspector] string _id = Guid.NewGuid().ToString("N");
        [SerializeField] string _name = "New Item";
        [SerializeField, Min(0f)] float _start;
        [SerializeField] Object _binding;
        [SerializeReference, HideInInspector] List<Requirement> _requirements = new();

        /// <summary>Conditions that must hold at <see cref="Start"/>; checked by the validator.</summary>
        public List<Requirement> Requirements => _requirements ??= new List<Requirement>();

        public string Id => _id;
        public string Name { get => _name; set => _name = value; }
        public float Start { get => _start; set => _start = Mathf.Max(0f, value); }

        /// <summary>Optional reference to a project asset (quest, location, item...).</summary>
        public Object Binding { get => _binding; set => _binding = value; }

        /// <summary>Time at which the item ends. Equals <see cref="Start"/> for markers.</summary>
        public virtual float End => _start;

        /// <summary>Assigns a fresh unique id (used when duplicating).</summary>
        public void RegenerateId() => _id = Guid.NewGuid().ToString("N");

        /// <summary>Orders items by start time, then by id for a stable order.</summary>
        public static int CompareByStart(TimelineItem a, TimelineItem b)
        {
            int byStart = a.Start.CompareTo(b.Start);
            return byStart != 0 ? byStart : string.CompareOrdinal(a.Id, b.Id);
        }
    }

    /// <summary>An item with duration, e.g. an act or a fabula event.</summary>
    [Serializable]
    public class ClipBase : TimelineItem
    {
        public const float MinDuration = 0.05f;

        [SerializeField, Min(MinDuration)] float _duration = 1f;

        public float Duration { get => _duration; set => _duration = Mathf.Max(MinDuration, value); }
        public override float End => Start + _duration;

        /// <summary>True when <paramref name="time"/> lies inside [Start, End).</summary>
        public bool Contains(float time) => time >= Start && time < End;
    }

    /// <summary>A point-in-time item, e.g. a skill unlock or a location opening.</summary>
    [Serializable]
    public class MarkerBase : TimelineItem
    {
    }
}
