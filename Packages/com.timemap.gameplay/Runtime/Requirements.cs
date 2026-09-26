using System;
using System.ComponentModel;
using UnityEngine;

namespace TimeMapGameplay
{
    /// <summary>Reference to another timeline item by id (survives reordering and serialisation).</summary>
    [Serializable]
    public struct ItemReference
    {
        [SerializeField] string _id;

        public ItemReference(string id) => _id = id;

        public string Id => _id;
        public bool IsSet => !string.IsNullOrEmpty(_id);

        public TimelineItem Resolve(ProgressionTimeline timeline)
            => IsSet && timeline != null && timeline.TryFindItem(_id, out var item, out _) ? item : null;
    }

    /// <summary>Restricts which item type an <see cref="ItemReference"/> field may point to (used by the editor picker).</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class ItemReferenceAttribute : PropertyAttribute
    {
        public ItemReferenceAttribute(Type itemType) => ItemType = itemType;

        public Type ItemType { get; }
    }

    /// <summary>Items with a minimum hero level (checked against the level curve).</summary>
    public interface ILevelGated
    {
        int MinLevel { get; }
    }

    /// <summary>Data passed to <see cref="Requirement.Check"/>.</summary>
    public sealed class RequirementContext
    {
        ProgressionState _state;

        public RequirementContext(ProgressionTimeline timeline, TimelineItem owner)
        {
            Timeline = timeline;
            Owner = owner;
        }

        public ProgressionTimeline Timeline { get; }
        public TimelineItem Owner { get; }

        /// <summary>Time at which the requirement must hold: the owner's start.</summary>
        public float Time => Owner.Start;

        /// <summary>Progression state at <see cref="Time"/>, evaluated lazily.</summary>
        public ProgressionState State => _state ??= Timeline.Evaluate(Time);

        public string Format(float time) => TimeMath.Format(time, Timeline.Axis.Units);
    }

    /// <summary>
    /// A condition an item needs at its start (e.g. an act requires an opened location).
    /// Projects add their own by deriving from this class; the validator picks them up automatically.
    /// </summary>
    [Serializable]
    public abstract class Requirement
    {
        /// <summary>Returns true when satisfied; otherwise <paramref name="message"/> explains the violation.</summary>
        public abstract bool Check(RequirementContext context, out string message);

        /// <summary>Short human-readable description, e.g. "Локация: Шахты".</summary>
        public abstract string Describe(ProgressionTimeline timeline);
    }

    /// <summary>Requires another item (of a given kind) to have started by the owner's start.</summary>
    [Serializable]
    public abstract class ItemAvailableRequirement : Requirement
    {
        protected abstract ItemReference Target { get; }
        protected abstract string Noun { get; }
        protected abstract string Verb { get; }

        public override bool Check(RequirementContext context, out string message)
        {
            if (!Target.IsSet)
            {
                message = $"{Noun}: не выбрано.";
                return false;
            }
            var item = Target.Resolve(context.Timeline);
            if (item == null)
            {
                message = $"{Noun}: ссылка на удалённый элемент.";
                return false;
            }
            if (item.Start > context.Time + 0.0001f)
            {
                message = $"Требует «{item.Name}» ({Noun.ToLowerInvariant()}), но {Verb} только в {context.Format(item.Start)}, " +
                          $"а «{context.Owner.Name}» начинается в {context.Format(context.Time)}.";
                return false;
            }
            message = null;
            return true;
        }

        public override string Describe(ProgressionTimeline timeline)
            => $"{Noun}: {Target.Resolve(timeline)?.Name ?? "—"}";
    }

    [Serializable, DisplayName("Открытая локация")]
    public sealed class LocationRequirement : ItemAvailableRequirement
    {
        [SerializeField, ItemReference(typeof(LocationMarker))] ItemReference _location;

        public LocationRequirement() { }
        public LocationRequirement(string locationId) => _location = new ItemReference(locationId);

        protected override ItemReference Target => _location;
        protected override string Noun => "Локация";
        protected override string Verb => "она открывается";
    }

    [Serializable, DisplayName("Раскрытый факт фабулы")]
    public sealed class FabulaRequirement : ItemAvailableRequirement
    {
        [SerializeField, ItemReference(typeof(FabulaEvent))] ItemReference _fact;

        public FabulaRequirement() { }
        public FabulaRequirement(string factId) => _fact = new ItemReference(factId);

        protected override ItemReference Target => _fact;
        protected override string Noun => "Факт фабулы";
        protected override string Verb => "игрок узнаёт его";
    }

    [Serializable, DisplayName("Уровень героя")]
    public sealed class LevelRequirement : Requirement
    {
        [SerializeField, Min(0)] int _minLevel = 1;

        public LevelRequirement() { }
        public LevelRequirement(int minLevel) => _minLevel = minLevel;

        public int MinLevel => _minLevel;

        public override bool Check(RequirementContext context, out string message)
            => LevelCheck.Check(context.Timeline, context.Owner, _minLevel, out message);

        public override string Describe(ProgressionTimeline timeline) => $"Уровень ≥ {_minLevel}";
    }

    /// <summary>Shared level check used by <see cref="LevelRequirement"/> and the min-level rule.</summary>
    public static class LevelCheck
    {
        public static bool Check(ProgressionTimeline timeline, TimelineItem owner, int minLevel, out string message)
        {
            LevelCurveTrack curve = null;
            foreach (var track in timeline.Tracks)
                if (track is LevelCurveTrack level) { curve = level; break; }

            if (curve == null)
            {
                message = $"Требует уровень {minLevel}, но в таймлайне нет трека уровня.";
                return false;
            }

            float value = curve.EvaluateValue(owner.Start);
            if (value + 0.0001f >= minLevel)
            {
                message = null;
                return true;
            }

            var format = new Func<float, string>(t => TimeMath.Format(t, timeline.Axis.Units));
            float? reached = curve.FirstTimeReaching(minLevel);
            message = $"Требует уровень {minLevel}, а в {format(owner.Start)} по кривой только {Mathf.FloorToInt(value + 0.0001f)}" +
                      (reached.HasValue ? $"; уровень {minLevel} достигается в {format(reached.Value)}." : "; кривая его не достигает.");
            return false;
        }
    }
}
