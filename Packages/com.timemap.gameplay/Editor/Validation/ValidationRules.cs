using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace TimeMapGameplay.Editor
{
    public enum IssueSeverity
    {
        Warning,
        Error
    }

    /// <summary>A validation problem, attached to an item and/or a track.</summary>
    public sealed class ValidationIssue
    {
        public ValidationIssue(string ruleId, IssueSeverity severity, string message, string itemId = null, string trackId = null)
        {
            RuleId = ruleId;
            Severity = severity;
            Message = message;
            ItemId = itemId;
            TrackId = trackId;
        }

        public string RuleId { get; }
        public IssueSeverity Severity { get; }
        public string Message { get; }
        public string ItemId { get; }
        public string TrackId { get; }

        /// <summary>Identity used to detect new issues between runs.</summary>
        public string Key => $"{RuleId}|{ItemId}|{TrackId}|{Message}";

        public override string ToString() => Message;
    }

    /// <summary>
    /// A dependency check over the whole timeline. Implementations with a public parameterless constructor are
    /// discovered automatically (TypeCache), so projects add rules by just declaring a class.
    /// </summary>
    public interface IValidationRule
    {
        string Id { get; }
        IEnumerable<ValidationIssue> Validate(ProgressionTimeline timeline);
    }

    /// <summary>Checks every <see cref="Requirement"/> attached to items (locations, fabula facts, levels, project types).</summary>
    public sealed class RequirementsRule : IValidationRule
    {
        public string Id => "requirements";

        public IEnumerable<ValidationIssue> Validate(ProgressionTimeline timeline)
        {
            foreach (var track in timeline.Tracks.Where(t => t != null))
            foreach (var item in track.Items.Where(i => i != null))
            {
                if (item.Requirements.Count == 0) continue;
                var context = new RequirementContext(timeline, item);
                foreach (var requirement in item.Requirements)
                {
                    if (requirement == null)
                    {
                        yield return new ValidationIssue(Id, IssueSeverity.Error,
                            $"«{item.Name}»: требование неизвестного типа (класс удалён или переименован).", item.Id, track.Id);
                        continue;
                    }
                    if (!requirement.Check(context, out string message))
                        yield return new ValidationIssue(Id, IssueSeverity.Warning, message, item.Id, track.Id);
                }
            }
        }
    }

    /// <summary>Skills/upgrades (any <see cref="ILevelGated"/>) must not unlock before the level curve reaches their level.</summary>
    public sealed class MinLevelRule : IValidationRule
    {
        public string Id => "min-level";

        public IEnumerable<ValidationIssue> Validate(ProgressionTimeline timeline)
        {
            foreach (var track in timeline.Tracks.Where(t => t != null))
            foreach (var item in track.Items)
            {
                if (item is not ILevelGated gated || gated.MinLevel <= 0) continue;
                if (!LevelCheck.Check(timeline, item, gated.MinLevel, out string message))
                    yield return new ValidationIssue(Id, IssueSeverity.Warning, $"«{item.Name}»: {message}", item.Id, track.Id);
            }
        }
    }

    /// <summary>Missing asset bindings (deleted assets) and missing managed-reference types.</summary>
    public sealed class BrokenReferencesRule : IValidationRule
    {
        public string Id => "broken-references";

        public IEnumerable<ValidationIssue> Validate(ProgressionTimeline timeline)
        {
            for (int i = 0; i < timeline.Tracks.Count; i++)
            {
                var track = timeline.Tracks[i];
                if (track == null)
                {
                    yield return new ValidationIssue(Id, IssueSeverity.Error,
                        $"Трек №{i + 1}: неизвестный тип (класс удалён или переименован).");
                    continue;
                }
                foreach (var item in track.Items)
                {
                    if (item == null)
                    {
                        yield return new ValidationIssue(Id, IssueSeverity.Error,
                            $"Трек «{track.Name}»: элемент неизвестного типа (класс удалён или переименован).", trackId: track.Id);
                        continue;
                    }
                    if (IsMissing(item.Binding))
                        yield return new ValidationIssue(Id, IssueSeverity.Error,
                            $"«{item.Name}»: привязанный ассет удалён.", item.Id, track.Id);
                }
            }
        }

        /// <summary>
        /// A reference to a deleted asset: Unity reports it as null, but it still carries the entity id of the lost object.
        /// Empty fields loaded from disk are also "fake null" objects, but with no valid entity id — those are just unbound.
        /// </summary>
        public static bool IsMissing(Object reference)
            => !ReferenceEquals(reference, null) && reference == null && reference.GetEntityId().IsValid();
    }

    /// <summary>Items that stick out of the axis (e.g. after shortening the timeline).</summary>
    public sealed class AxisBoundsRule : IValidationRule
    {
        public string Id => "axis-bounds";

        public IEnumerable<ValidationIssue> Validate(ProgressionTimeline timeline)
        {
            float length = timeline.Axis.Length;
            foreach (var track in timeline.Tracks.Where(t => t != null))
            foreach (var item in track.Items.Where(i => i != null))
            {
                if (item.End > length + 0.0001f)
                    yield return new ValidationIssue(Id, IssueSeverity.Warning,
                        $"«{item.Name}» заканчивается в {TimeMath.Format(item.End, timeline.Axis.Units)}, " +
                        $"позже конца оси ({TimeMath.Format(length, timeline.Axis.Units)}).", item.Id, track.Id);
            }
        }
    }
}
